using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CSharpLab.Core.Settings;
using CSharpLab.Core.Updates;
using CSharpLab.ViewModels;

namespace CSharpLab.App.Tests;

public sealed class UpdateFlowTests
{
    [Fact]
    public void Falha_ao_aplicar_tem_prioridade_sobre_aviso_de_versao() => Ui.Run(async () =>
    {
        var dir = UpdateService.UpdatesDir;
        Directory.CreateDirectory(dir);
        var failurePath = Path.Combine(dir, "falha.json");
        File.WriteAllText(failurePath, JsonSerializer.Serialize(new { Message = "Falha simulada" }));
        using var vm = new MainViewModel(new AppSettings { CheckForUpdates = false, LastRunVersion = "0.0.0" })
            { Dialogs = new FakeDialogs(), Terminal = new FakeTerminal() };
        try
        {
            await vm.InitializeAsync();
            Assert.True(vm.NoticeIsError);
            Assert.Contains("Falha simulada", vm.Notice);
            Assert.Equal(UpdateService.CurrentVersion.ToString(3), vm.Settings.LastRunVersion);
        }
        finally { if (File.Exists(failurePath)) File.Delete(failurePath); }
    });

    private static async Task WithFeed(Func<MainViewModel, FakeDialogs, Task> test, bool checkOnStart = false, bool validHash = true)
    {
        var previousRoot = AppPaths.Root;
        var previousFeed = Environment.GetEnvironmentVariable("CSHARPLAB_UPDATE_FEED");
        var root = Ui.NewFolder("atualizacoes");
        AppPaths.Root = root;
        var version = new Version(UpdateService.CurrentVersion.Major + 1, 0, 0);
        var zip = Path.Combine(root, "package.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(archive.CreateEntry("pacote/app/CSharpLab.exe").Open());
            writer.Write("versão nova");
        }
        var hash = validHash ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zip))) : new string('0', 64);
        var feed = Path.Combine(root, "feed.json");
        File.WriteAllText(feed, JsonSerializer.Serialize(new
        {
            tag_name = "v" + version.ToString(3), draft = false, prerelease = false,
            assets = new[] { new { name = UpdateService.PackageName, browser_download_url = zip, digest = "sha256:" + hash } },
        }));
        Environment.SetEnvironmentVariable("CSHARPLAB_UPDATE_FEED", feed);
        var dialogs = new FakeDialogs();
        using var vm = new MainViewModel(new AppSettings { CheckForUpdates = checkOnStart })
            { Dialogs = dialogs, Terminal = new FakeTerminal() };
        try
        {
            await vm.InitializeAsync();
            await test(vm, dialogs);
        }
        finally
        {
            vm.Dispose();
            await Ui.WaitUntil(() => !vm.IsCheckingForUpdates);
            Environment.SetEnvironmentVariable("CSHARPLAB_UPDATE_FEED", previousFeed);
            AppPaths.Root = previousRoot;
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void Ao_abrir_baixa_avisa_e_restart_respeita_cancelamento_de_arquivo_sujo() => Ui.Run(() => WithFeed(async (vm, dialogs) =>
    {
        await Ui.WaitUntil(() => vm.UpdateReady, 15_000, "atualização pronta");
        Assert.Contains("está pronta", vm.UpdateMessage);
        Assert.True(vm.CheckUpdatesNowCommand.CanExecute(null));
        bool requested = false;
        vm.ShutdownForUpdateRequested += () => requested = true;
        var doc = vm.ActiveDocument!;
        doc.Document.Insert(0, "// trabalho não salvo\n");
        var text = doc.Document.Text;
        dialogs.SaveAnswer = SaveChoice.Cancel;
        vm.RestartToUpdateCommand.Execute(null);
        Assert.False(requested);
        Assert.Equal(text, doc.Document.Text);
        vm.UpdateLaterCommand.Execute(null);
        Assert.False(vm.UpdateReady);
        Assert.Null(vm.UpdateMessage);
        await vm.CheckForUpdatesAsync(manual: true);
        Assert.True(vm.UpdateReady);
        dialogs.SaveAnswer = SaveChoice.Discard;
        vm.RestartToUpdateCommand.Execute(null);
        Assert.True(requested);
    }, checkOnStart: true));

    [Fact]
    public void Fechar_cancela_busca_sem_atualizar_estado_depois_de_dispose() => Ui.Run(() => WithFeed(async (vm, _) =>
    {
        vm.CheckForUpdatesOnStart = true;
        var checking = vm.CheckForUpdatesAsync(manual: false);
        Assert.True(vm.IsCheckingForUpdates);
        Assert.False(vm.CheckUpdatesNowCommand.CanExecute(null));
        vm.Dispose();
        await checking;
        Assert.False(vm.IsCheckingForUpdates);
        Assert.False(vm.UpdateReady);
        Assert.Null(vm.UpdateMessage);
        Assert.False(vm.CheckUpdatesNowCommand.CanExecute(null));
    }));

    [Fact]
    public void Checksum_invalido_nao_oferece_instalacao() => Ui.Run(() => WithFeed(async (vm, _) =>
    {
        await vm.CheckForUpdatesAsync(manual: true);
        Assert.False(vm.UpdateReady);
        Assert.Null(vm.UpdateMessage);
        Assert.Null(UpdateService.FindStaged(UpdateService.CurrentVersion));
    }, validHash: false));

    [Fact]
    public void Preferencia_persiste_e_menu_mostra_marca_de_selecao() => Ui.Run(() => WithFeed((vm, _) =>
    {
        vm.CheckForUpdatesOnStart = false;
        vm.SaveSettings();
        Assert.False(SettingsStore.Load().CheckForUpdates);
        var item = new MenuItem { Header = "Procurar atualizações ao abrir", IsCheckable = true, IsChecked = true };
        item.ApplyTemplate();
        var check = Assert.IsType<TextBlock>(item.Template.FindName("Check", item));
        Assert.Equal(Visibility.Visible, check.Visibility);
        item.IsChecked = false;
        Assert.Equal(Visibility.Collapsed, check.Visibility);
        return Task.CompletedTask;
    }));
}
