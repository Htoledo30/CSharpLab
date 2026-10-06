using System.Collections;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using static CSharpLab.Core.Terminal.NativeMethods;

namespace CSharpLab.Core.Terminal;

public sealed record ProcessLaunch(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string?>? Environment = null);

/// <summary>
/// Um processo do usuário rodando dentro de um pseudoconsole do Windows (ConPTY).
/// O processo e todos os filhos ficam num Job Object: parar ou fechar encerra a árvore inteira.
/// </summary>
public sealed class PseudoConsoleSession : IDisposable
{
    private readonly object _gate = new();
    private IntPtr _pseudoConsole;
    private IntPtr _job;
    private IntPtr _process;
    private SafeFileHandle? _inputWrite;
    private FileStream? _inputStream;
    private Thread? _readerThread;
    private readonly TaskCompletionSource<int> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _lastOutputTicks;
    private bool _disposed;

    private const int MaxPendingOutput = 4 * 1024 * 1024;
    private readonly object _outputGate = new();
    private readonly StringBuilder _pendingOutput = new();
    private Action<string>? _output;

    /// <summary>
    /// Saída já decodificada (UTF-8). Disparado numa thread de fundo. O que o programa escrever
    /// antes do primeiro assinante fica guardado e é entregue a ele na assinatura.
    /// </summary>
    public event Action<string>? Output
    {
        add
        {
            if (value == null) return;
            lock (_outputGate)
            {
                _output += value;
                if (_pendingOutput.Length > 0)
                {
                    var pending = _pendingOutput.ToString();
                    _pendingOutput.Clear();
                    value(pending);
                }
            }
        }
        remove
        {
            lock (_outputGate) _output -= value;
        }
    }

    private void RaiseOutput(string text)
    {
        lock (_outputGate)
        {
            if (_output != null)
            {
                _output(text);
            }
            else if (_pendingOutput.Length < MaxPendingOutput)
            {
                _pendingOutput.Append(text);
            }
        }
    }

    public int ProcessId { get; private set; }

    /// <summary>Completa com o código de saída depois que a saída restante foi lida.</summary>
    public Task<int> Completion => _completion.Task;

    public bool HasExited => _completion.Task.IsCompleted;

    private PseudoConsoleSession() { }

    public static PseudoConsoleSession Start(ProcessLaunch launch, short columns, short rows)
    {
        var session = new PseudoConsoleSession();
        try
        {
            session.StartCore(launch, Math.Max((short)10, columns), Math.Max((short)3, rows));
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    private void StartCore(ProcessLaunch launch, short columns, short rows)
    {
        if (!CreatePipe(out var inputRead, out var inputWrite, IntPtr.Zero, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!CreatePipe(out var outputRead, out var outputWrite, IntPtr.Zero, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        int hr = CreatePseudoConsole(new COORD { X = columns, Y = rows }, inputRead, outputWrite, 0, out _pseudoConsole);
        // O pseudoconsole duplica as pontas que usa; as nossas cópias podem ser fechadas.
        inputRead.Dispose();
        outputWrite.Dispose();
        if (hr != 0)
            throw new Win32Exception(hr, "Não foi possível criar o pseudoconsole.");

        _inputWrite = inputWrite;
        _inputStream = new FileStream(inputWrite, FileAccess.Write, 1);

        _job = CreateJobObject(IntPtr.Zero, null);
        if (_job == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        if (!SetInformationJobObject(_job, JobObjectExtendedLimitInformation, ref limits, Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>()))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        StartProcess(launch);

        var outputStream = new FileStream(outputRead, FileAccess.Read, 1);
        _readerThread = new Thread(() => ReadLoop(outputStream))
        {
            IsBackground = true,
            Name = "ConPTY output",
        };
        _readerThread.Start();

        var waitThread = new Thread(WaitForExit) { IsBackground = true, Name = "ConPTY exit" };
        waitThread.Start();
    }

    private void StartProcess(ProcessLaunch launch)
    {
        IntPtr attrSize = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attrSize);
        IntPtr attrList = Marshal.AllocHGlobal(attrSize);
        IntPtr env = IntPtr.Zero;
        try
        {
            if (!InitializeProcThreadAttributeList(attrList, 1, 0, ref attrSize))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (!UpdateProcThreadAttribute(attrList, 0, PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE, _pseudoConsole, IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var startup = new STARTUPINFOEX();
            startup.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
            startup.lpAttributeList = attrList;
            // Sem isso, se o editor tiver handles padrão redirecionados, o filho os herdaria
            // em vez de usar o pseudoconsole.
            startup.StartupInfo.dwFlags = STARTF_USESTDHANDLES;

            env = Marshal.StringToHGlobalUni(BuildEnvironmentBlock(launch.Environment));
            var commandLine = CommandLine.Build(launch.FileName, launch.Arguments);
            var cmdChars = (commandLine + '\0').ToCharArray();

            // "Ignorar Ctrl+C" é herdado do processo pai; garante que o programa possa ser interrompido.
            SetConsoleCtrlHandler(IntPtr.Zero, false);

            if (!CreateProcess(null, cmdChars, IntPtr.Zero, IntPtr.Zero, false,
                    EXTENDED_STARTUPINFO_PRESENT | CREATE_UNICODE_ENVIRONMENT | CREATE_SUSPENDED,
                    env, launch.WorkingDirectory, ref startup, out var pi))
            {
                int err = Marshal.GetLastWin32Error();
                throw new Win32Exception(err, $"Não foi possível iniciar \"{Path.GetFileName(launch.FileName)}\": {new Win32Exception(err).Message}");
            }

            _process = pi.hProcess;
            ProcessId = pi.dwProcessId;
            if (!AssignProcessToJobObject(_job, pi.hProcess))
            {
                // Sem o job não há como garantir o encerramento da árvore: aborta.
                int err = Marshal.GetLastWin32Error();
                TerminateProcess(pi.hProcess, 1);
                CloseHandle(pi.hThread);
                throw new Win32Exception(err);
            }

            ResumeThread(pi.hThread);
            CloseHandle(pi.hThread);
            ConsoleCodePage.TrySetUtf8Output(pi.dwProcessId);
        }
        finally
        {
            DeleteProcThreadAttributeList(attrList);
            Marshal.FreeHGlobal(attrList);
            if (env != IntPtr.Zero)
                Marshal.FreeHGlobal(env);
        }
    }

    private static string BuildEnvironmentBlock(IReadOnlyDictionary<string, string?>? overrides)
    {
        var vars = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry e in System.Environment.GetEnvironmentVariables())
            vars[(string)e.Key] = (string?)e.Value ?? "";
        if (overrides != null)
        {
            foreach (var (k, v) in overrides)
            {
                if (v is null) vars.Remove(k);
                else vars[k] = v;
            }
        }

        var sb = new StringBuilder();
        foreach (var (k, v) in vars)
            sb.Append(k).Append('=').Append(v).Append('\0');
        sb.Append('\0');
        return sb.ToString();
    }

    private void ReadLoop(FileStream stream)
    {
        var decoder = new UTF8Encoding(false).GetDecoder();
        var bytes = new byte[16 * 1024];
        var chars = new char[16 * 1024 + 4];
        try
        {
            while (true)
            {
                int n = stream.Read(bytes, 0, bytes.Length);
                if (n <= 0) break;
                Interlocked.Exchange(ref _lastOutputTicks, Environment.TickCount64);
                int c = decoder.GetChars(bytes, 0, n, chars, 0, flush: false);
                if (c > 0)
                    RaiseOutput(new string(chars, 0, c));
            }
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        finally
        {
            stream.Dispose();
        }
    }

    private void WaitForExit()
    {
        WaitForSingleObject(_process, INFINITE);
        GetExitCodeProcess(_process, out uint exitCode);

        // Dá ao conhost um instante para entregar o que o programa escreveu por último.
        var deadline = Environment.TickCount64 + 400;
        while (Environment.TickCount64 < deadline &&
               Environment.TickCount64 - Interlocked.Read(ref _lastOutputTicks) < 60)
        {
            Thread.Sleep(15);
        }
        Thread.Sleep(30);

        lock (_gate)
        {
            if (_pseudoConsole != IntPtr.Zero)
            {
                ClosePseudoConsole(_pseudoConsole);
                _pseudoConsole = IntPtr.Zero;
            }
        }

        _readerThread?.Join(2000);
        _completion.TrySetResult(unchecked((int)exitCode));
    }

    public void Write(string text)
    {
        if (HasExited || string.IsNullOrEmpty(text)) return;
        var bytes = Encoding.UTF8.GetBytes(text);
        lock (_gate)
        {
            try
            {
                _inputStream?.Write(bytes, 0, bytes.Length);
                _inputStream?.Flush();
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        }
    }

    public void Resize(short columns, short rows)
    {
        lock (_gate)
        {
            if (_pseudoConsole != IntPtr.Zero && columns > 0 && rows > 0)
                ResizePseudoConsole(_pseudoConsole, new COORD { X = columns, Y = rows });
        }
    }

    /// <summary>Encerra o processo e todos os processos filhos criados por ele.</summary>
    public void Kill()
    {
        lock (_gate)
        {
            if (_job != IntPtr.Zero && !HasExited)
                TerminateJobObject(_job, 0xC000013A);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Kill();
        if (_process != IntPtr.Zero)
            _completion.Task.Wait(3000);
        lock (_gate)
        {
            if (_pseudoConsole != IntPtr.Zero)
            {
                ClosePseudoConsole(_pseudoConsole);
                _pseudoConsole = IntPtr.Zero;
            }
            _inputStream?.Dispose();
            _inputStream = null;
            _inputWrite?.Dispose();
        }
        _readerThread?.Join(1000);
        if (_process != IntPtr.Zero)
        {
            CloseHandle(_process);
            _process = IntPtr.Zero;
        }
        if (_job != IntPtr.Zero)
        {
            CloseHandle(_job);
            _job = IntPtr.Zero;
        }
    }
}
