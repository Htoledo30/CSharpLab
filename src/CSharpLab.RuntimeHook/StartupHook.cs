using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

/// <summary>
/// O .NET chama <see cref="Initialize"/> antes do Main do programa do usuário. Se o programa
/// parar com uma exceção não tratada, grava tipo, mensagem e as linhas do código num arquivo
/// que o CSharp Lab lê para explicar o erro em português. Não muda nada no comportamento do programa.
/// </summary>
internal static class StartupHook
{
    public const string ReportVariable = "CSHARPLAB_CRASH_REPORT";

    public static void Initialize()
    {
        var report = Environment.GetEnvironmentVariable(ReportVariable);
        if (string.IsNullOrEmpty(report)) return;
        // Processos que o programa iniciar não herdam o gancho.
        Environment.SetEnvironmentVariable(ReportVariable, null);
        Environment.SetEnvironmentVariable("DOTNET_STARTUP_HOOKS", null);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            try
            {
                if (e.ExceptionObject is Exception ex) File.WriteAllText(report, Describe(Unwrap(ex)), Encoding.UTF8);
            }
            catch
            {
                // Nunca atrapalha o programa do usuário.
            }
        };
    }

    /// <summary>Erros "embrulhados" (ex.: num construtor static) mostram o erro de verdade.</summary>
    private static Exception Unwrap(Exception ex)
    {
        while (true)
        {
            if (ex is TargetInvocationException or TypeInitializationException && ex.InnerException != null)
                ex = ex.InnerException;
            else if (ex is AggregateException { InnerExceptions.Count: 1 } aggregate)
                ex = aggregate.InnerExceptions[0];
            else
                return ex;
        }
    }

    private static string Describe(Exception ex)
    {
        var sb = new StringBuilder();
        sb.Append("type=").Append(ex.GetType().FullName).Append('\n');
        sb.Append("message=").Append(OneLine(ex.Message)).Append('\n');
        foreach (var frame in new StackTrace(ex, fNeedFileInfo: true).GetFrames())
        {
            var file = frame.GetFileName();
            if (string.IsNullOrEmpty(file)) continue;
            var method = frame.GetMethod();
            sb.Append("frame=").Append(file).Append('|').Append(frame.GetFileLineNumber()).Append('|')
              .Append(frame.GetFileColumnNumber()).Append('|').Append(method?.Name ?? "").Append('\n');
        }
        return sb.ToString();
    }

    private static string OneLine(string text) => text.Replace("\r", " ").Replace("\n", " ");
}
