using System.Diagnostics;
using System.Text;

namespace CSharpLab.Core.Projects;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public string Combined => string.IsNullOrEmpty(StandardError) ? StandardOutput : StandardOutput + "\n" + StandardError;
}

/// <summary>Localiza o dotnet.exe e executa comandos do SDK com argumentos estruturados.</summary>
public static class Dotnet
{
    private static string? _path;

    /// <summary>Variáveis para saída previsível e sem ruído de primeira execução.</summary>
    public static readonly IReadOnlyDictionary<string, string> QuietEnvironment = new Dictionary<string, string>
    {
        ["DOTNET_CLI_UI_LANGUAGE"] = "en",
        ["DOTNET_NOLOGO"] = "1",
        ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
        ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
        ["MSBUILDTERMINALLOGGER"] = "off",
    };

    /// <summary>Testes: age como num computador sem o SDK instalado.</summary>
    internal static bool SimulateMissing { get; set; }

    public static string? FindExecutable()
    {
        if (SimulateMissing) return null;
        if (_path != null && File.Exists(_path)) return _path;

        var candidates = new List<string>();
        var root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrEmpty(root)) candidates.Add(Path.Combine(root, "dotnet.exe"));
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try { candidates.Add(Path.Combine(dir.Trim().Trim('"'), "dotnet.exe")); } catch { }
        }
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe"));

        // Ignora um dotnet.exe que esteja dentro da pasta do próprio aplicativo.
        var appDir = AppContext.BaseDirectory.TrimEnd('\\');
        _path = candidates.FirstOrDefault(c =>
            File.Exists(c) && !Path.GetDirectoryName(c)!.Equals(appDir, StringComparison.OrdinalIgnoreCase));
        return _path;
    }

    public static void ResetCache() => _path = null;

    public static string? Root => FindExecutable() is { } exe ? Path.GetDirectoryName(exe) : null;

    public static ProcessStartInfo CreateStartInfo(IEnumerable<string> args, string? workingDirectory)
    {
        var exe = FindExecutable() ?? throw new InvalidOperationException("SDK .NET não encontrado.");
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            // Sem pasta de projeto: a pasta do usuário, nunca a do aplicativo (os servidores de build do
            // dotnet ficam abertos minutos depois e travariam a atualização).
            WorkingDirectory = workingDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        foreach (var (k, v) in QuietEnvironment)
        {
            if (string.IsNullOrEmpty(v)) psi.Environment.Remove(k);
            else psi.Environment[k] = v;
        }
        return psi;
    }

    /// <summary>Executa e captura a saída. Cancelar encerra a árvore de processos.</summary>
    public static async Task<ProcessResult> RunAsync(IEnumerable<string> args, string? workingDirectory, CancellationToken ct = default)
    {
        using var process = new Process { StartInfo = CreateStartInfo(args, workingDirectory) };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (stdout) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (stderr) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            try { await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); } catch { }
            throw;
        }

        lock (stdout) lock (stderr)
            return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }
}
