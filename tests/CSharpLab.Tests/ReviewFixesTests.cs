using System.Text;
using CSharpLab.Core.Files;
using CSharpLab.Core.Projects;
using CSharpLab.Core.Settings;
using CSharpLab.Core.Terminal;

namespace CSharpLab.Tests;

public sealed class ReviewFixesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "csharplab-fix", Guid.NewGuid().ToString("N")[..8]);

    public ReviewFixesTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void Recuperacao_apagada_nao_reaparece_por_gravacao_atrasada()
    {
        var store = new RecoveryStore(Path.Combine(_dir, "rec"));
        for (int i = 0; i < 50; i++)
            store.Save(RecoveryStore.Create("doc", null, "Sem título.cs", "versão " + i, TextFileIO.Utf8NoBom, 0));
        store.Delete("doc");
        Assert.True(store.Flush(TimeSpan.FromSeconds(10)));
        Assert.Empty(store.LoadAll());
    }

    [Fact]
    public void Recuperacao_mantem_a_versao_mais_nova()
    {
        var store = new RecoveryStore(Path.Combine(_dir, "rec2"));
        for (int i = 0; i < 50; i++)
            store.Save(RecoveryStore.Create("doc", null, "Sem título.cs", "versão " + i, TextFileIO.Utf8NoBom, 0));
        Assert.True(store.Flush(TimeSpan.FromSeconds(10)));
        Assert.Equal("versão 49", Assert.Single(store.LoadAll()).Text);
    }

    [Fact]
    public void Windows1252_nao_troca_caracteres_em_silencio()
    {
        var path = Path.Combine(_dir, "velho.cs");
        File.WriteAllBytes(path, TextFileIO.Legacy.GetBytes("// ação"));
        var ex = Assert.Throws<UnrepresentableTextException>(() => TextFileIO.Save(path, "// ação 😀", TextFileIO.Legacy));
        Assert.Equal("😀", ex.Sample);
        Assert.Equal(TextFileIO.Legacy.GetBytes("// ação"), File.ReadAllBytes(path));
    }

    [Fact]
    public void Busca_encontra_mais_de_dez_mil_ocorrencias_sem_limite()
    {
        var text = string.Concat(Enumerable.Repeat("ab ", 25_000));
        Assert.Equal(25_000, TextOccurrences.FindAll(text, "ab", matchCase: true).Count);
        Assert.Equal(10_000, TextOccurrences.FindAll(text, "AB", matchCase: false, limit: 10_000).Count);
        Assert.Empty(TextOccurrences.FindAll(text, "AB", matchCase: true));
    }

    [Fact]
    public void Restore_considera_Directory_Packages_props()
    {
        var created = ProjectCreator.CreateConsoleProject(_dir, "Pacotes");
        var obj = Path.Combine(created.Directory, "obj");
        Directory.CreateDirectory(obj);
        var assets = Path.Combine(obj, "project.assets.json");
        File.WriteAllText(assets, "{}");
        File.SetLastWriteTimeUtc(created.ProjectPath, DateTime.UtcNow.AddMinutes(-10));
        File.SetLastWriteTimeUtc(assets, DateTime.UtcNow.AddMinutes(-5));
        Assert.False(ProjectEvaluator.NeedsRestore(created.ProjectPath));

        var props = Path.Combine(_dir, "Directory.Packages.props");
        File.WriteAllText(props, "<Project />");
        File.SetLastWriteTimeUtc(props, DateTime.UtcNow);
        Assert.True(ProjectEvaluator.NeedsRestore(created.ProjectPath));
    }

    [Fact]
    public async Task Cache_respeita_Compile_Remove_e_invalida_com_arquivo_novo()
    {
        var created = ProjectCreator.CreateConsoleProject(_dir, "ComRemove");
        File.WriteAllText(Path.Combine(created.Directory, "Fora.cs"), "class Fora { }");
        var csproj = File.ReadAllText(created.ProjectPath)
            .Replace("</Project>", "  <ItemGroup>\n    <Compile Remove=\"Fora.cs\" />\n  </ItemGroup>\n</Project>");
        File.WriteAllText(created.ProjectPath, csproj);

        var model = await ProjectEvaluator.EvaluateAsync(created.ProjectPath, allowRestore: true, CancellationToken.None);
        Assert.True(model.IsEvaluated);
        Assert.DoesNotContain(model.CompileFiles, f => f.EndsWith("Fora.cs"));

        var cached = ProjectEvaluator.TryLoadCached(created.ProjectPath);
        Assert.NotNull(cached);
        Assert.DoesNotContain(cached!.CompileFiles, f => f.EndsWith("Fora.cs"));

        // Arquivo novo: o cache deixa de valer e o projeto é avaliado de novo.
        File.WriteAllText(Path.Combine(created.Directory, "Novo.cs"), "class Novo { }");
        Assert.Null(ProjectEvaluator.TryLoadCached(created.ProjectPath));
    }
}

[Collection("SampleApp")]
public sealed class TerminalOutputBufferTests(SampleConsoleApp app)
{
    [Fact]
    public async Task Saida_antes_da_assinatura_nao_se_perde()
    {
        using var session = PseudoConsoleSession.Start(new ProcessLaunch(app.ExePath, ["exit3"], app.Directory), 100, 30);
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(10));

        // Assina só depois de o programa terminar.
        var received = new StringBuilder();
        session.Output += text => { lock (received) received.Append(text); };
        lock (received) Assert.Contains("fim sem quebra", received.ToString());
    }
}
