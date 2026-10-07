using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using CSharpLab.Core.Settings;
using CSharpLab.Core.Updates;

namespace CSharpLab.Tests;

[CollectionDefinition("Updates", DisableParallelization = true)]
public sealed class UpdatesCollection;

[Collection("Updates")]
public sealed class UpdateServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "csharplab-updates", Guid.NewGuid().ToString("N"));
    private readonly string _previousRoot = AppPaths.Root;
    private readonly string? _previousFeed = Environment.GetEnvironmentVariable("CSHARPLAB_UPDATE_FEED");
    private readonly string? _previousRepo = Environment.GetEnvironmentVariable("CSHARPLAB_UPDATE_REPO");

    public UpdateServiceTests()
    {
        AppPaths.Root = Path.Combine(_dir, "data");
        Directory.CreateDirectory(AppPaths.Root);
        Environment.SetEnvironmentVariable("CSHARPLAB_UPDATE_REPO", null);
        Environment.SetEnvironmentVariable("CSHARPLAB_UPDATE_FEED", Path.Combine(_dir, "release.json"));
    }

    public void Dispose()
    {
        AppPaths.Root = _previousRoot;
        Environment.SetEnvironmentVariable("CSHARPLAB_UPDATE_FEED", _previousFeed);
        Environment.SetEnvironmentVariable("CSHARPLAB_UPDATE_REPO", _previousRepo);
        try { Directory.Delete(_dir, true); } catch { }
    }

    private UpdateInfo Package(string version = "1.1.0", bool traversal = false)
    {
        var zip = Path.Combine(_dir, version + ".zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            foreach (var (name, content) in new[]
            {
                ("CSharpLab-win-x64/app/CSharpLab.exe", "nova versão " + version),
                ("CSharpLab-win-x64/app/CSharpLab.dll", "biblioteca " + version),
                ("CSharpLab-win-x64/app/.oculto", "arquivo oculto"),
                ("CSharpLab-win-x64/app/pt-BR/recurso.dll", "recurso"),
            })
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write(content);
            }
            if (traversal)
            {
                using var writer = new StreamWriter(archive.CreateEntry("../../escape.txt").Open());
                writer.Write("inválido");
            }
        }
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zip)));
        return new UpdateInfo(Version.Parse(version), "v" + version, zip, hash, "Correções");
    }

    private void Feed(UpdateInfo info, bool digest = true, bool prerelease = false)
    {
        var checksum = Path.Combine(_dir, "checksum.sha256");
        File.WriteAllText(checksum, info.Sha256 + "  " + UpdateService.PackageName + "\n");
        File.WriteAllText(UpdateService.FeedUrl!, JsonSerializer.Serialize(new
        {
            tag_name = info.Tag, draft = false, prerelease,
            assets = new[]
            {
                new { name = UpdateService.PackageName, browser_download_url = info.PackageUrl, digest = digest ? "sha256:" + info.Sha256 : null },
                new { name = UpdateService.PackageName + ".sha256", browser_download_url = checksum, digest = (string?)null },
            },
        }));
    }

    [Theory]
    [InlineData("v1.2.3", true)]
    [InlineData("1.2.3+build.5", true)]
    [InlineData("1.2.3-beta", false)]
    [InlineData("1.2", false)]
    [InlineData("1.2.3.4", false)]
    [InlineData("texto", false)]
    public void Versoes_estaveis_sao_comparadas_sem_aceitar_preview(string tag, bool accepted) =>
        Assert.Equal(accepted, UpdateService.TryParseVersion(tag, out _));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Consulta_detecta_nova_versao_e_checksum(bool digest)
    {
        var info = Package();
        Feed(info, digest);
        var newer = await UpdateService.CheckAsync(new Version(1, 0, 0), CancellationToken.None);
        Assert.Equal(info.Sha256, newer!.Sha256);
        Assert.Equal(info.Version, newer.Version);
        Assert.Null(await UpdateService.CheckAsync(info.Version, CancellationToken.None));
        Feed(info, prerelease: true);
        Assert.Null(await UpdateService.CheckAsync(new Version(1, 0, 0), CancellationToken.None));
    }

    [Fact]
    public async Task Pacote_pronto_mais_novo_e_escolhido_e_alteracoes_invalidam_a_copia()
    {
        await UpdateService.DownloadAndStageAsync(Package("1.1.0"), null, CancellationToken.None);
        var newest = await UpdateService.DownloadAndStageAsync(Package("1.2.0"), null, CancellationToken.None);
        Assert.Equal(new Version(1, 2, 0), UpdateService.FindStaged(new Version(1, 0, 0))!.Value.Version);
        File.AppendAllText(Path.Combine(newest, "CSharpLab.dll"), "corrompido");
        Assert.Equal(new Version(1, 1, 0), UpdateService.FindStaged(new Version(1, 0, 0))!.Value.Version);
        UpdateService.CleanUp(new Version(1, 1, 0));
        Assert.Null(UpdateService.FindStaged(new Version(1, 0, 0)));
    }

    [Fact]
    public async Task Checksum_incorreto_e_zip_com_traversal_nao_ficam_prontos()
    {
        var package = Package();
        await Assert.ThrowsAsync<InvalidDataException>(() => UpdateService.DownloadAndStageAsync(
            package with { Sha256 = new string('0', 64) }, null, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => UpdateService.DownloadAndStageAsync(
            package with { Sha256 = null }, null, CancellationToken.None));
        await Assert.ThrowsAsync<IOException>(() => UpdateService.DownloadAndStageAsync(
            Package("1.2.0", traversal: true), null, CancellationToken.None));
        Assert.Null(UpdateService.FindStaged(new Version(1, 0, 0)));
        Assert.Empty(Directory.EnumerateDirectories(UpdateService.UpdatesDir));
        Assert.False(File.Exists(Path.Combine(_dir, "escape.txt")));
    }

    [Fact]
    public async Task Download_cancelado_nao_deixa_uma_atualizacao_pronta()
    {
        var package = Package();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => UpdateService.DownloadAndStageAsync(package, null, cancelled.Token));
        Assert.Null(UpdateService.FindStaged(new Version(1, 0, 0)));
        Assert.Empty(Directory.EnumerateDirectories(UpdateService.UpdatesDir));
    }

    private string OldInstallation()
    {
        var target = Path.Combine(_dir, "instalado");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "CSharpLab.exe"), "antiga");
        File.WriteAllText(Path.Combine(target, "desinstalar.ps1"), "desinstalador");
        File.WriteAllText(Path.Combine(target, "meu-projeto.cs"), "trabalho pessoal");
        File.WriteAllText(Path.Combine(target, "obsoleto.dll"), "removido no pacote novo");
        File.WriteAllText(Path.Combine(target, ".csharplab-package.json"), "{\"obsoleto.dll\":\"hash antigo\"}");
        return target;
    }

    private async Task<int> Apply(string source, string target, bool failSwap = false, int processId = int.MaxValue)
    {
        var start = UpdateService.CreateApplierStartInfo(source, new Version(1, 1, 0), false, processId, target);
        if (failSwap)
        {
            var scriptIndex = start.ArgumentList.IndexOf("-File") + 1;
            var realScript = start.ArgumentList[scriptIndex];
            var wrapper = Path.Combine(_dir, "falha-simulada.ps1");
            File.WriteAllText(wrapper, """
                function Move-Item([string]$LiteralPath, [string]$Destination) {
                    if ($LiteralPath.Contains('.update-')) { throw 'Falha simulada na troca.' }
                    Microsoft.PowerShell.Management\Move-Item -LiteralPath $LiteralPath -Destination $Destination -ErrorAction Stop
                }
                """ + "\n. '" + realScript.Replace("'", "''") + "' @args\nexit $LASTEXITCODE\n");
            start.ArgumentList[scriptIndex] = wrapper;
        }
        start.RedirectStandardError = true;
        start.RedirectStandardOutput = true;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
        catch { process.Kill(entireProcessTree: true); throw; }
        var details = await output + await error;
        if (process.ExitCode != 0) Assert.True(File.Exists(Path.Combine(UpdateService.UpdatesDir, "falha.json")), details);
        return process.ExitCode;
    }

    [Fact]
    public void Aplicador_nao_herda_modulos_do_PowerShell_7()
    {
        var start = UpdateService.CreateApplierStartInfo(_dir, new Version(1, 1, 0), false, Environment.ProcessId, _dir);
        Assert.False(start.Environment.ContainsKey("PSModulePath"));
    }

    [Fact]
    public async Task Aplicacao_real_preserva_desinstalador_e_arquivos_pessoais()
    {
        var staged = await UpdateService.DownloadAndStageAsync(Package(), null, CancellationToken.None);
        var target = OldInstallation();
        Assert.Equal(0, await Apply(staged, target));
        Assert.Equal("nova versão 1.1.0", File.ReadAllText(Path.Combine(target, "CSharpLab.exe")));
        Assert.Equal("desinstalador", File.ReadAllText(Path.Combine(target, "desinstalar.ps1")));
        Assert.Equal("trabalho pessoal", File.ReadAllText(Path.Combine(target, "meu-projeto.cs")));
        Assert.True(File.Exists(Path.Combine(target, ".oculto")));
        Assert.False(File.Exists(Path.Combine(target, "obsoleto.dll")));
        Assert.DoesNotContain(Directory.EnumerateDirectories(_dir), d => Path.GetFileName(d).StartsWith("instalado."));
    }

    [Fact]
    public async Task Falha_na_troca_restaura_a_versao_anterior()
    {
        var staged = await UpdateService.DownloadAndStageAsync(Package(), null, CancellationToken.None);
        var target = OldInstallation();
        Assert.Equal(1, await Apply(staged, target, failSwap: true));
        Assert.Equal("antiga", File.ReadAllText(Path.Combine(target, "CSharpLab.exe")));
        Assert.Contains("simulada", UpdateService.TakeApplyFailure());
        Assert.Null(UpdateService.TakeApplyFailure());
        Assert.DoesNotContain(Directory.EnumerateDirectories(_dir), d => Path.GetFileName(d).StartsWith("instalado."));
    }

    [Fact]
    public async Task Outra_instancia_ou_pacote_alterado_impede_a_troca()
    {
        var staged = await UpdateService.DownloadAndStageAsync(Package(), null, CancellationToken.None);
        var target = OldInstallation();
        using (var lease = InstanceLease.TryAcquire(AppPaths.Root))
        {
            Assert.NotNull(lease);
            Assert.Equal(1, await Apply(staged, target));
            Assert.Equal("antiga", File.ReadAllText(Path.Combine(target, "CSharpLab.exe")));
        }
        File.AppendAllText(Path.Combine(staged, "CSharpLab.dll"), "corrompido");
        Assert.Equal(1, await Apply(staged, target));
        Assert.Equal("antiga", File.ReadAllText(Path.Combine(target, "CSharpLab.exe")));
    }

    [Fact]
    public async Task Espera_o_processo_antigo_terminar_antes_de_trocar_arquivos()
    {
        var staged = await UpdateService.DownloadAndStageAsync(Package(), null, CancellationToken.None);
        var target = OldInstallation();
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("Start-Sleep -Seconds 3");
        using var previous = Process.Start(start)!;
        try
        {
            var apply = Apply(staged, target, processId: previous.Id);
            await Task.Delay(200);
            Assert.False(previous.HasExited);
            Assert.Equal("antiga", File.ReadAllText(Path.Combine(target, "CSharpLab.exe")));
            Assert.Equal(0, await apply);
            Assert.True(previous.HasExited);
            Assert.Equal("nova versão 1.1.0", File.ReadAllText(Path.Combine(target, "CSharpLab.exe")));
        }
        finally { if (!previous.HasExited) previous.Kill(entireProcessTree: true); }
    }
}
