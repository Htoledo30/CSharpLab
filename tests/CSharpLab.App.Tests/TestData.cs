using System.Runtime.CompilerServices;
using CSharpLab.Core.Settings;

namespace CSharpLab.App.Tests;

/// <summary>
/// Antes de qualquer teste (mesmo os que não usam a classe Ui), a pasta de dados do CSharp Lab passa a ser
/// uma pasta temporária: os testes nunca mexem no %LocalAppData%\CSharpLab de quem usa o editor.
/// </summary>
internal static class TestData
{
#pragma warning disable CA2255 // inicializador de módulo de propósito: roda antes de qualquer teste
    [ModuleInitializer]
    internal static void Initialize()
#pragma warning restore CA2255
    {
        var parent = Path.Combine(Path.GetTempPath(), "csharplab-apptests");
        AppPaths.Root = Path.Combine(parent, Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(AppPaths.Root);
        // As pastas de execuções antigas (de mais de um dia) vão embora: elas chegavam a 150 MB.
        foreach (var old in Directory.EnumerateDirectories(parent))
        {
            try
            {
                if (Directory.GetLastWriteTimeUtc(old) < DateTime.UtcNow.AddDays(-1)) Directory.Delete(old, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
