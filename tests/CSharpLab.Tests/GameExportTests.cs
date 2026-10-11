using System.IO.Compression;
using System.Text.Json;
using CSharpLab.Core.Build;
using CSharpLab.Core.Projects;

namespace CSharpLab.Tests;

public sealed class GameExportTests
{
    private readonly string _root = Path.Combine(TestData.Root, "exports", Guid.NewGuid().ToString("N"));
    private string Destination => Path.Combine(_root, "Jogos prontos");
    private ProjectFile Project
    {
        get
        {
            var source = Path.Combine(_root, "codigo");
            Directory.CreateDirectory(source);
            return new() { Path = Path.Combine(source, "Aventura.csproj") };
        }
    }

    private GameExportService Publisher(Action<string>? publish = null, string executable = "Aventura")
        => new((args, _, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            if (args[0] == "msbuild") return Task.FromResult(new ProcessResult(0, executable + "\n", ""));
            var output = args[args.ToList().IndexOf("-o") + 1];
            File.WriteAllText(Path.Combine(output, executable + ".exe"), "nova versão");
            Directory.CreateDirectory(Path.Combine(output, "Screens"));
            File.WriteAllText(Path.Combine(output, "Screens", "Start.json"), "tela");
            publish?.Invoke(output);
            return Task.FromResult(new ProcessResult(0, "", ""));
        });

    private string OldFolder => Path.Combine(Destination, Project.Name);
    private string OldZip => OldFolder + ".zip";

    private void CreatePrevious()
    {
        Directory.CreateDirectory(OldFolder);
        File.WriteAllText(Path.Combine(OldFolder, "Aventura.exe"), "versão anterior");
        File.WriteAllText(Path.Combine(OldFolder, "anotacoes.txt"), "arquivo extra do usuário");
        File.WriteAllText(OldZip, "ZIP anterior");
    }

    private void AssertPrevious()
    {
        Assert.Equal("versão anterior", File.ReadAllText(Path.Combine(OldFolder, "Aventura.exe")));
        Assert.Equal("arquivo extra do usuário", File.ReadAllText(Path.Combine(OldFolder, "anotacoes.txt")));
        Assert.Equal("ZIP anterior", File.ReadAllText(OldZip));
    }

    [Fact]
    public async Task Primeira_exportacao_tem_exe_e_zip_com_nome_do_jogo()
    {
        var result = await Publisher(executable: "NomeEmRelease").ExportAsync(Project, Destination, null, default);
        Assert.Null(result.PreviousVersion);
        Assert.Equal("NomeEmRelease.exe", Path.GetFileName(result.Executable));
        using var zip = ZipFile.OpenRead(result.Zip);
        Assert.Contains(zip.Entries, entry => entry.FullName == "Aventura/NomeEmRelease.exe");
        Assert.Contains(zip.Entries, entry => entry.FullName == "Aventura/Screens/Start.json");
        Assert.False(Directory.Exists(Path.Combine(Destination, ".exportacao", "Aventura", "pendente")));
    }

    [Fact]
    public async Task Reexportacao_guarda_toda_a_versao_anterior_inclusive_arquivos_extras()
    {
        CreatePrevious();
        var result = await Publisher().ExportAsync(Project, Destination, null, default);
        Assert.Equal("nova versão", File.ReadAllText(result.Executable));
        Assert.NotNull(result.PreviousVersion);
        Assert.Equal("versão anterior", File.ReadAllText(Path.Combine(result.PreviousVersion, "jogo", "Aventura.exe")));
        Assert.Equal("arquivo extra do usuário", File.ReadAllText(Path.Combine(result.PreviousVersion, "jogo", "anotacoes.txt")));
        Assert.Equal("ZIP anterior", File.ReadAllText(Path.Combine(result.PreviousVersion, "jogo.zip")));
        var again = await Publisher().ExportAsync(Project, Destination, null, default);
        Assert.NotEqual(result.PreviousVersion, again.PreviousVersion);
        Assert.True(Directory.Exists(result.PreviousVersion));
    }

    [Theory]
    [InlineData("compile")]
    [InlineData("missing-exe")]
    [InlineData("empty-exe")]
    public async Task Publicacao_com_erro_nao_toca_na_versao_anterior(string failure)
    {
        CreatePrevious();
        var exporter = failure == "compile"
            ? new GameExportService((args, _, _) => Task.FromResult(args[0] == "msbuild"
                ? new ProcessResult(0, "Aventura", "") : new ProcessResult(1, "error CS1002", "")))
            : Publisher(folder =>
            {
                if (failure == "missing-exe") File.Delete(Path.Combine(folder, "Aventura.exe"));
                else File.WriteAllText(Path.Combine(folder, "Aventura.exe"), "");
            });
        await Assert.ThrowsAsync<GameExportException>(() => exporter.ExportAsync(Project, Destination, null, default));
        AssertPrevious();
    }

    [Theory]
    [InlineData(GameExportStage.Compiling)]
    [InlineData(GameExportStage.CreatingZip)]
    [InlineData(GameExportStage.CheckingZip)]
    public async Task Cancelar_preparacao_preserva_pasta_e_zip(GameExportStage stage)
    {
        CreatePrevious();
        using var cts = new CancellationTokenSource();
        var progress = new ImmediateProgress(s => { if (s == stage) cts.Cancel(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Publisher().ExportAsync(Project, Destination, progress, cts.Token));
        AssertPrevious();
    }

    [Fact]
    public async Task Cancelamento_durante_a_troca_nao_deixa_meia_exportacao()
    {
        CreatePrevious();
        using var cts = new CancellationTokenSource();
        var result = await Publisher().ExportAsync(Project, Destination,
            new ImmediateProgress(s => { if (s == GameExportStage.Installing) cts.Cancel(); }), cts.Token);
        Assert.Equal("nova versão", File.ReadAllText(result.Executable));
        using var zip = ZipFile.OpenRead(result.Zip);
        Assert.NotEmpty(zip.Entries);
        Assert.True(Directory.Exists(result.PreviousVersion));
    }

    [Fact]
    public async Task Arquivo_publicado_bloqueado_impede_zip_sem_apagar_anterior()
    {
        CreatePrevious();
        FileStream? locked = null;
        try
        {
            var exporter = Publisher(folder => locked = new FileStream(Path.Combine(folder, "Aventura.exe"), FileMode.Open, FileAccess.Read, FileShare.None));
            await Assert.ThrowsAsync<IOException>(() => exporter.ExportAsync(Project, Destination, null, default));
            AssertPrevious();
        }
        finally { locked?.Dispose(); }
        // A próxima tentativa limpa uma preparação que ficou aberta por outro programa.
        await Publisher().ExportAsync(Project, Destination, null, default);
    }

    [Fact]
    public async Task Zip_anterior_aberto_desfaz_movimentacao_da_pasta()
    {
        CreatePrevious();
        using (var locked = new FileStream(OldZip, FileMode.Open, FileAccess.Read, FileShare.Read))
            await Assert.ThrowsAsync<GameExportException>(() => Publisher().ExportAsync(Project, Destination, null, default));
        AssertPrevious();
        await Publisher().ExportAsync(Project, Destination, null, default);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Troca_interrompida_recupera_anterior_antes_de_nova_compilacao(bool firstExport)
    {
        if (!firstExport) CreatePrevious();
        using (var transaction = ExportTransaction.Open(Project, Destination))
        {
            File.WriteAllText(Path.Combine(transaction.PreparedFolder, "Aventura.exe"), "versão interrompida");
            File.WriteAllText(transaction.PreparedZip, "ZIP interrompido");
            var stage = Path.GetDirectoryName(transaction.PreparedFolder)!;
            File.WriteAllText(Path.Combine(stage, "troca.json"), JsonSerializer.Serialize(
                new ExportTransaction.Journal("teste_123", !firstExport, !firstExport)));
            if (!firstExport)
            {
                var backup = Path.Combine(Destination, "Versões anteriores", "Aventura", "teste_123");
                Directory.CreateDirectory(backup);
                Directory.Move(OldFolder, Path.Combine(backup, "jogo"));
                File.Move(OldZip, Path.Combine(backup, "jogo.zip"));
            }
            Directory.Move(transaction.PreparedFolder, OldFolder);
            // Interrupção após instalar a pasta, antes de instalar o ZIP.
        }
        var failing = new GameExportService((_, _, _) => Task.FromResult(new ProcessResult(1, "falha de compilação", "")));
        await Assert.ThrowsAsync<GameExportException>(() => failing.ExportAsync(Project, Destination, null, default));
        if (!firstExport) AssertPrevious();
        else { Assert.False(Directory.Exists(OldFolder)); Assert.False(File.Exists(OldZip)); }
    }

    [Theory]
    [InlineData("{invalido")]
    [InlineData("{\"BackupName\":\"teste_123\"}")]
    public async Task Registro_de_recuperacao_invalido_nao_apaga_arquivos(string journal)
    {
        CreatePrevious();
        using (var transaction = ExportTransaction.Open(Project, Destination))
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(transaction.PreparedFolder)!, "troca.json"), journal);
        var ex = await Assert.ThrowsAsync<GameExportException>(() => Publisher().ExportAsync(Project, Destination, null, default));
        Assert.Contains("recuperada", ex.Message);
        AssertPrevious();
    }

    [Fact]
    public async Task Recuperacao_parcial_preserva_registro_e_pode_ser_retentada()
    {
        CreatePrevious();
        string journal;
        string backup;
        using (var transaction = ExportTransaction.Open(Project, Destination))
        {
            journal = Path.Combine(Path.GetDirectoryName(transaction.PreparedFolder)!, "troca.json");
            File.WriteAllText(journal, JsonSerializer.Serialize(new ExportTransaction.Journal("teste_123", true, true)));
            backup = Path.Combine(Destination, "Versões anteriores", "Aventura", "teste_123");
            Directory.CreateDirectory(backup);
            Directory.Move(OldFolder, Path.Combine(backup, "jogo"));
            File.Move(OldZip, Path.Combine(backup, "jogo.zip"));
            File.WriteAllText(Path.Combine(transaction.PreparedFolder, "Aventura.exe"), "incompleto");
            Directory.Move(transaction.PreparedFolder, OldFolder);
            File.WriteAllText(OldZip, "ZIP incompleto");
        }
        using (var locked = new FileStream(OldZip, FileMode.Open, FileAccess.Read, FileShare.Read))
            await Assert.ThrowsAsync<GameExportException>(() => Publisher().ExportAsync(Project, Destination, null, default));
        Assert.True(File.Exists(journal));
        Assert.Equal("versão anterior", File.ReadAllText(Path.Combine(OldFolder, "Aventura.exe")));
        Assert.Equal("ZIP anterior", File.ReadAllText(Path.Combine(backup, "jogo.zip")));

        var failing = new GameExportService((_, _, _) => Task.FromResult(new ProcessResult(1, "erro", "")));
        await Assert.ThrowsAsync<GameExportException>(() => failing.ExportAsync(Project, Destination, null, default));
        AssertPrevious();
        Assert.False(File.Exists(journal));
    }

    [Fact]
    public async Task Troca_concluida_com_limpeza_interrompida_mantem_a_nova_versao()
    {
        CreatePrevious();
        using (var transaction = ExportTransaction.Open(Project, Destination))
        {
            File.WriteAllText(Path.Combine(transaction.PreparedFolder, "Aventura.exe"), "nova versão");
            File.WriteAllText(transaction.PreparedZip, "novo ZIP");
            var backup = transaction.Install();
            Assert.NotNull(backup);
            // Não limpar: simula encerramento após gravar o registro de conclusão.
        }
        var failing = new GameExportService((_, _, _) => Task.FromResult(new ProcessResult(1, "erro", "")));
        await Assert.ThrowsAsync<GameExportException>(() => failing.ExportAsync(Project, Destination, null, default));
        Assert.Equal("nova versão", File.ReadAllText(Path.Combine(OldFolder, "Aventura.exe")));
        Assert.Equal("novo ZIP", File.ReadAllText(OldZip));
    }

    [Fact]
    public async Task Dois_exportadores_nao_alteram_o_mesmo_destino_ao_mesmo_tempo()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();
        var first = new GameExportService(async (_, _, ct) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return new(0, "", "");
        }).ExportAsync(Project, Destination, null, cts.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            var ex = await Assert.ThrowsAsync<GameExportException>(() => Publisher().ExportAsync(Project, Destination, null, default));
            Assert.Contains("em andamento", ex.Message);
        }
        finally { cts.Cancel(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
    }

    [Fact]
    public async Task Projeto_dentro_da_pasta_exportada_nao_e_movido()
    {
        CreatePrevious();
        var inside = new ProjectFile { Path = Path.Combine(OldFolder, "Aventura.csproj") };
        File.WriteAllText(inside.Path, "código importante");
        await Assert.ThrowsAsync<GameExportException>(() => Publisher().ExportAsync(inside, Destination, null, default));
        AssertPrevious();
        Assert.Equal("código importante", File.ReadAllText(inside.Path));
    }

    [Fact]
    public async Task Zip_preserva_tambem_as_pastas_vazias_publicadas()
    {
        var result = await Publisher(folder => Directory.CreateDirectory(Path.Combine(folder, "Data", "Saves")))
            .ExportAsync(Project, Destination, null, default);
        var extracted = Path.Combine(_root, "extraido");
        ZipFile.ExtractToDirectory(result.Zip, extracted);
        Assert.True(Directory.Exists(Path.Combine(extracted, "Aventura", "Data", "Saves")));
    }

    [Fact]
    public void Conferencia_do_zip_detecta_conteudo_alterado()
    {
        Directory.CreateDirectory(_root);
        var folder = Path.Combine(_root, "publicado");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "jogo.exe"), "original");
        var zip = Path.Combine(_root, "teste.zip");
        var contents = ExportArchive.Create(folder, zip, "Jogo", default);
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update))
        {
            archive.GetEntry("Jogo/jogo.exe")!.Delete();
            using var writer = new StreamWriter(archive.CreateEntry("Jogo/jogo.exe").Open());
            writer.Write("alterado");
        }
        Assert.Throws<InvalidDataException>(() => ExportArchive.Verify(zip, contents, default));
    }

    private sealed class ImmediateProgress(Action<GameExportStage> report) : IProgress<GameExportStage>
    {
        public void Report(GameExportStage value) => report(value);
    }
}
