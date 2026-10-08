using System.Runtime.CompilerServices;
using CSharpLab.Core.Settings;

namespace CSharpLab.Tests;

/// <summary>
/// Antes de qualquer teste, a pasta de dados do CSharp Lab (cache de projetos, compilação, recuperação)
/// passa a ser uma pasta temporária. Assim os testes nunca enchem o %LocalAppData%\CSharpLab de quem usa o editor.
/// </summary>
internal static class TestData
{
    public static string Root { get; } = Path.Combine(Path.GetTempPath(), "csharplab-testdata", Guid.NewGuid().ToString("N")[..8]);

#pragma warning disable CA2255 // inicializador de módulo de propósito: roda antes de qualquer teste
    [ModuleInitializer]
    internal static void Initialize()
#pragma warning restore CA2255
    {
        Directory.CreateDirectory(Root);
        AppPaths.Root = Root;
        // As pastas de execuções antigas (de mais de um dia) vão embora.
        foreach (var old in Directory.EnumerateDirectories(Path.GetDirectoryName(Root)!))
        {
            try
            {
                if (Directory.GetLastWriteTimeUtc(old) < DateTime.UtcNow.AddDays(-1)) Directory.Delete(old, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}

public sealed class TestDataTests
{
    [Fact]
    public void Testes_nao_usam_a_pasta_de_dados_do_editor_de_verdade()
    {
        var real = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CSharpLab");
        Assert.StartsWith(Path.GetTempPath(), AppPaths.Root, StringComparison.OrdinalIgnoreCase);
        Assert.False(AppPaths.CacheDir.StartsWith(real, StringComparison.OrdinalIgnoreCase));
    }
}
