using System.IO.Pipes;
using System.Reflection;
using System.Windows.Threading;
using CSharpLab.Core.Language;
using CSharpLab.Core.Projects;
using CSharpLab.Core.Settings;
using CSharpLab.ViewModels;

namespace CSharpLab.App.Tests;

public sealed class LifecycleRegressionTests
{
    private static MainViewModel Create() => new(new AppSettings { CheckForUpdates = false })
        { Dialogs = new FakeDialogs(), Terminal = new FakeTerminal() };

    private static void Event(MainViewModel vm, WatcherChangeTypes type, string path, string? oldPath = null) =>
        typeof(MainViewModel).GetMethod("Enqueue", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, [type, path, oldPath]);

    [Fact]
    public void Evento_ja_enfileirado_nao_reinicia_monitor_apos_fechar() => Ui.Run(async () =>
    {
        using var vm = Create();
        await vm.InitializeAsync();
        vm.CurrentFolder = Ui.NewFolder();
        var file = Path.Combine(vm.CurrentFolder, "Novo.cs");
        Task.Run(() => Event(vm, WatcherChangeTypes.Created, file)).GetAwaiter().GetResult();
        vm.Dispose();
        vm.ScheduleDiagnostics();
        vm.ScheduleSettingsSave();
        vm.NotifyInfo("Callback atrasado");
        await Task.Delay(100);
        foreach (var name in new[] { "_fsTimer", "_diagnosticsTimer", "_settingsTimer", "_noticeTimer", "_recoveryTimer" })
        {
            var timer = (DispatcherTimer)typeof(MainViewModel).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
            Assert.False(timer.IsEnabled, name + " não pode reiniciar depois de Dispose.");
        }
    });

    [Fact]
    public void Fechar_descarta_descoberta_de_arquivos_ainda_em_andamento() => Ui.Run(async () =>
    {
        using var vm = Create();
        await vm.InitializeAsync();
        var dir = Ui.NewFolder();
        File.WriteAllText(Path.Combine(dir, "Classe.cs"), "class Classe {}");
        var opening = vm.OpenFolderAsync(dir, null, promptForUnsaved: false);
        vm.Dispose();
        Assert.False(await opening);
        Assert.Null(typeof(MainViewModel).GetField("_model", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm));
    });

    [Fact]
    public void Renomear_para_pasta_ignorada_retira_o_arquivo_do_contexto() => Ui.Run(async () =>
    {
        using var vm = Create();
        await vm.InitializeAsync();
        var dir = Ui.NewFolder();
        var file = Path.Combine(dir, "Classe.cs");
        File.WriteAllText(file, "class Classe {} ");
        await vm.OpenFolderAsync(dir, null, promptForUnsaved: false);
        // Injeta a notificação de rename completa, sem notificações duplicadas de create/delete mascarando o caso.
        ((FileSystemWatcher)typeof(MainViewModel).GetField("_watcher", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!).Dispose();
        var generated = Path.Combine(dir, "bin");
        Directory.CreateDirectory(generated);
        var renamed = Path.Combine(generated, "Classe.cs");
        File.Move(file, renamed);
        Event(vm, WatcherChangeTypes.Renamed, renamed, file);
        await Task.Delay(750);
        Assert.Null(vm.Language!.GetDocument(LanguageService.KeyFor(file)));
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Parar_ou_fechar_cancela_MSBuild_travado_no_projeto(bool close) => Ui.Run(async () =>
    {
        using var vm = Create();
        await vm.InitializeAsync();
        var created = ProjectCreator.CreateConsoleProject(Ui.NewFolder(), "Espera");
        var pipeName = "csharplab-review-" + Guid.NewGuid().ToString("N");
        using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var connection = pipe.WaitForConnectionAsync(timeout.Token);
        var projectText = File.ReadAllText(created.ProjectPath).Replace("</Project>",
            "<PropertyGroup><ReviewWait>$([System.IO.File]::ReadAllText('\\\\.\\pipe\\" + pipeName + "'))</ReviewWait></PropertyGroup></Project>");
        File.WriteAllText(created.ProjectPath, projectText);
        vm.CurrentFolder = created.Directory;
        vm.RunProject = ProjectFile.Read(created.ProjectPath);
        var run = vm.RunCommand.ExecuteAsync(null);
        try
        {
            await connection;
            // O MSBuild está travado avaliando o .csproj (antes ou durante a compilação).
            Assert.True(vm.RunState is RunState.Preparing or RunState.Building, $"Estado: {vm.RunState}");
            if (close) vm.Dispose();
            else vm.StopRun();
            await Task.WhenAny(run, Task.Delay(1500));
            Assert.True(run.IsCompleted, "Parar precisa encerrar o MSBuild mesmo antes da compilação.");
            await run;
            Assert.Empty(((FakeDialogs)vm.Dialogs).Errors);
        }
        finally
        {
            vm.StopRun();
            // Libera a leitura se a versão sob teste ainda ignorar o cancelamento.
            pipe.Dispose();
            await Task.WhenAny(run, Task.Delay(5000));
        }
    });
}
