using CSharpLab.Core.Build;
using CSharpLab.Core.Projects;

namespace CSharpLab.Tests;

/// <summary>Os exemplos para estudar compilam sem erros nem avisos e nunca apagam o que o usuário mudou.</summary>
public sealed class ExamplesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "csharplab-exemplos", Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    public static TheoryData<string> Ids => new(Examples.All.Select(e => e.Id));

    [Theory]
    [MemberData(nameof(Ids))]
    public async Task Exemplo_compila_sem_erros_nem_avisos(string id)
    {
        var example = Examples.All.Single(e => e.Id == id);
        var created = Examples.Create(example, _dir);
        Assert.True(File.Exists(created.ProgramPath));
        var result = await BuildService.BuildAsync(created.ProjectPath, null, CancellationToken.None);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => $"{d.Id} {d.Message}")) + "\n" + result.Log);
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == BuildSeverity.Warning).Select(d => $"{d.Id} {d.FilePath}:{d.Line} {d.Message}"));
    }

    [Fact]
    public void Exemplo_ja_existente_e_reaberto_sem_apagar_mudancas()
    {
        var example = Examples.All.Single(e => e.Id == "RpgBattle");
        var first = Examples.Create(example, _dir);
        Assert.True(File.Exists(Path.Combine(first.Directory, "Character.cs")));
        File.WriteAllText(first.ProgramPath, "// meu código");
        var again = Examples.Create(example, _dir);
        Assert.Equal(first.ProjectPath, again.ProjectPath);
        Assert.Equal("// meu código", File.ReadAllText(again.ProgramPath));
    }

    [Fact]
    public void Exemplos_tem_codigo_em_ingles_e_comentarios_em_portugues()
    {
        foreach (var example in Examples.All)
        {
            var program = Examples.FilesOf(example)["Program.cs"];
            Assert.StartsWith("// ", program);
            Assert.DoesNotContain("var vida", program);
        }
    }
}
