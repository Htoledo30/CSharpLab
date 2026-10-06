using System.Runtime.InteropServices;
using static CSharpLab.Core.Terminal.NativeMethods;

namespace CSharpLab.Core.Terminal;

/// <summary>
/// O console criado pelo ConPTY começa com a página de código OEM (850 em pt-BR), o que
/// transforma caracteres fora dela em "?". Logo que o processo inicia, o editor se anexa
/// rapidamente ao console dele e troca apenas a página de SAÍDA para UTF-8 (como um "chcp").
/// A entrada continua na página padrão, que já cobre os acentos do português.
/// </summary>
internal static class ConsoleCodePage
{
    private static readonly object Gate = new();
    private delegate bool HandlerRoutine(uint ctrlType);
    private static readonly HandlerRoutine IgnoreAll = _ => true;

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "SetConsoleCtrlHandler")]
    private static extern bool SetHandler(HandlerRoutine handler, bool add);

    public static bool TrySetUtf8Output(int processId)
    {
        lock (Gate)
        {
            // Enquanto anexado, um Ctrl+C no console também chegaria a este processo.
            SetHandler(IgnoreAll, true);
            try
            {
                FreeConsole();
                // O processo só se conecta ao console depois de retomado; isso acontece
                // bem antes de o runtime .NET chegar ao Main, então algumas tentativas bastam.
                var deadline = Environment.TickCount64 + 250;
                while (!AttachConsole(processId))
                {
                    if (Environment.TickCount64 > deadline)
                        return false;
                    Thread.Yield();
                }
                try
                {
                    return SetConsoleOutputCP(65001);
                }
                finally
                {
                    FreeConsole();
                }
            }
            catch
            {
                return false;
            }
            finally
            {
                SetHandler(IgnoreAll, false);
            }
        }
    }
}
