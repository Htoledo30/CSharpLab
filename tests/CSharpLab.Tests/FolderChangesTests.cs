using CSharpLab.Core.Build;
using CSharpLab.Core.Projects;
using CSharpLab.Core.Terminal;

namespace CSharpLab.Tests;

/// <summary>Como os eventos do disco viram ações do editor (reler explorador, reavaliar projeto…).</summary>
public sealed class FolderChangesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "csharplab-pasta", Guid.NewGuid().ToString("N")[..8]);

    public FolderChangesTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string Touch(string relative)
    {
        var path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        return path;
    }

    private FolderChanges Analyze(IEnumerable<string>? compileFiles, params FolderEvent[] events) =>
        FolderChanges.Analyze(events, _dir, compileFiles, evaluationRunning: false);

    [Fact]
    public void Salvar_um_cs_conhecido_nao_reavalia_o_projeto()
    {
        var program = Touch("Program.cs");
        // Salvar grava um temporário e substitui: o monitor vê "apagado" e "renomeado".
        var changes = Analyze([program],
            new FolderEvent(WatcherChangeTypes.Deleted, program),
            new FolderEvent(WatcherChangeTypes.Renamed, program, Path.Combine(_dir, ".Program.cs.1a2b3c4d.tmp")),
            new FolderEvent(WatcherChangeTypes.Changed, program));
        Assert.False(changes.Reevaluate);
        Assert.False(changes.ReloadProjects);
        Assert.Contains(program, changes.Present);
        Assert.Empty(changes.Gone);
    }

    [Fact]
    public void Cs_novo_ou_apagado_reavalia()
    {
        var novo = Touch("Inimigo.cs");
        Assert.True(Analyze([], new FolderEvent(WatcherChangeTypes.Created, novo)).Reevaluate);

        var apagado = Path.Combine(_dir, "Velho.cs");
        var changes = Analyze([apagado], new FolderEvent(WatcherChangeTypes.Deleted, apagado));
        Assert.True(changes.Reevaluate);
        Assert.Contains(apagado, changes.Gone);
        Assert.Contains(_dir, changes.Directories);
    }

    [Fact]
    public void Csproj_e_configuracoes_recarregam_projetos()
    {
        foreach (var name in new[] { "Jogo.csproj", "Directory.Build.props", "global.json" })
            Assert.True(Analyze(null, new FolderEvent(WatcherChangeTypes.Changed, Touch(name))).ReloadProjects, name);
        Assert.True(Analyze(null, new FolderEvent(WatcherChangeTypes.Changed, Touch(".editorconfig"))).Reevaluate);
    }

    [Fact]
    public void Bin_e_obj_sao_ignorados_menos_o_restore()
    {
        var dll = Touch(@"bin\Debug\Jogo.dll");
        Assert.False(FolderChanges.ShouldTrack(new FolderEvent(WatcherChangeTypes.Changed, dll), _dir));
        var assets = Touch(@"obj\project.assets.json");
        var e = new FolderEvent(WatcherChangeTypes.Changed, assets);
        Assert.True(FolderChanges.ShouldTrack(e, _dir));

        var changes = Analyze(null, e);
        Assert.True(changes.Reevaluate);
        Assert.Empty(changes.Directories);
        // Restore feito pela própria avaliação em andamento: não dispara outra.
        Assert.False(FolderChanges.Analyze([e], _dir, null, evaluationRunning: true).Reevaluate);
    }

    [Fact]
    public void Eventos_perdidos_pedem_releitura_completa_e_fora_da_pasta_sao_ignorados()
    {
        Assert.True(Analyze(null, new FolderEvent(WatcherChangeTypes.All, _dir)).FullRefresh);
        var outside = Analyze(null, new FolderEvent(WatcherChangeTypes.Created, Path.Combine(Path.GetTempPath(), "outro.csproj")));
        Assert.False(outside.ReloadProjects);
        Assert.Empty(outside.Directories);
    }

    [Fact]
    public void Temporarios_do_salvamento_nao_contam()
    {
        var temp = Touch(".Program.cs.1a2b3c4d.tmp");
        var changes = Analyze([], new FolderEvent(WatcherChangeTypes.Created, temp));
        Assert.Empty(changes.Directories);
        Assert.Empty(changes.Present);
    }
}

public sealed class RunCacheTests : IDisposable
{
    private readonly string _exe = Path.Combine(Path.GetTempPath(), "csharplab-cache-" + Guid.NewGuid().ToString("N")[..8] + ".exe");

    public RunCacheTests() => File.WriteAllText(_exe, "");

    public void Dispose() => File.Delete(_exe);

    [Fact]
    public void Reaproveita_so_com_o_mesmo_projeto_e_as_mesmas_entradas()
    {
        var cache = new RunCache();
        var launch = new ProcessLaunch(_exe, [], Path.GetTempPath());
        Assert.Null(cache.TryReuse(@"C:\p\Jogo.csproj", "a"));

        cache.RememberBuild(@"C:\p\Jogo.csproj", "a", launch);
        Assert.Same(launch, cache.TryReuse(@"c:\P\jogo.csproj", "a"));
        Assert.Null(cache.TryReuse(@"C:\p\Jogo.csproj", "b"));
        Assert.Null(cache.TryReuse(@"C:\p\Outro.csproj", "a"));

        File.Delete(_exe); // apagaram bin/: precisa compilar de novo
        Assert.Null(cache.TryReuse(@"C:\p\Jogo.csproj", "a"));
    }

    [Fact]
    public void Sdk_conferido_vale_por_pasta_e_versao_ate_limpar()
    {
        var cache = new RunCache();
        cache.MarkSdkVerified(@"C:\p", 10);
        Assert.True(cache.IsSdkVerified(@"c:\P", 10));
        Assert.False(cache.IsSdkVerified(@"C:\p", 8));
        cache.Clear();
        Assert.False(cache.IsSdkVerified(@"C:\p", 10));
    }
}
