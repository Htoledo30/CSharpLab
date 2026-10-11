using CSharpLab.Core.Build;
using CSharpLab.Core.Projects;
using CSharpLab.Core.Settings;
using CSharpLab.ViewModels;
using CSharpLab.Views;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CSharpLab.App.Tests;

public sealed class GameExportTests
{
    private static async Task<MainViewModel> Editor(FakeDialogs dialogs, IGameExporter exporter)
    {
        var created = ProjectCreator.CreateGameProject(Ui.NewFolder("exportacao"), "Aventura");
        var vm = new MainViewModel(new AppSettings())
        {
            Dialogs = dialogs, Terminal = new FakeTerminal(), Exporter = exporter,
            ExportDestination = Ui.NewFolder("pacotes"), RevealExport = _ => { },
        };
        await vm.InitializeAsync();
        await vm.OpenFolderAsync(created.Directory, created.ProjectPath, promptForUnsaved: false);
        await Ui.WaitUntil(() => vm.HasGame, what: "projeto do jogo");
        return vm;
    }

    [Fact]
    public void Exportacao_bloqueia_F5_e_jogar_daqui_e_Shift_F5_cancela() => Ui.Run(async () =>
    {
        var exporter = new PausedExporter();
        var dialogs = new FakeDialogs();
        using var vm = await Editor(dialogs, exporter);
        var task = vm.ExportGameCommand.ExecuteAsync(null);
        await exporter.Started.Task;
        await Ui.WaitUntil(() => vm.StatusText.StartsWith("Compilando"));
        Assert.True(vm.IsBusy);
        Assert.Equal(RunState.Idle, vm.RunState);
        Assert.Equal("Cancelar exportação", vm.RunButtonText);
        Assert.True(vm.CanUseExportButton);

        await vm.RunCommand.ExecuteAsync(null); // F5
        vm.PlayFromScene("Start");
        await vm.ExportGameCommand.ExecuteAsync(null);
        Assert.Equal(RunState.Idle, vm.RunState);
        Assert.Equal(1, exporter.Calls);

        vm.StopCommand.Execute(null); // Shift+F5
        Assert.Equal("Cancelando…", vm.ExportButtonText);
        Assert.False(vm.CanUseExportButton);
        await task;
        Assert.True(exporter.WasCancelled);
        Assert.False(vm.IsBusy);
        Assert.False(vm.IsExporting);
        Assert.Equal("Exportar", vm.ExportButtonText);
        Assert.Contains("versão anterior foi preservada", vm.StatusText);
        Assert.Empty(dialogs.Errors);
    });

    [Fact]
    public void Botao_do_estudio_cancela_e_outra_exportacao_pode_comecar() => Ui.Run(async () =>
    {
        var exporter = new PausedExporter();
        using var vm = await Editor(new FakeDialogs(), exporter);
        vm.ExportOrCancelCommand.Execute(null);
        await exporter.Started.Task;
        vm.ExportOrCancelCommand.Execute(null);
        await Ui.WaitUntil(() => !vm.IsExporting);
        Assert.True(exporter.WasCancelled);

        var next = new CompletedExporter();
        vm.Exporter = next;
        string? revealed = null;
        vm.RevealExport = path => revealed = path;
        await vm.ExportGameCommand.ExecuteAsync(null);
        Assert.Equal(1, next.Calls);
        Assert.Equal("Exportação concluída.", vm.StatusText);
        Assert.EndsWith("Aventura.zip", revealed);
        Assert.False(vm.IsBusy);
    });

    [Fact]
    public void Erro_de_exportacao_mostra_detalhes_e_libera_o_editor() => Ui.Run(async () =>
    {
        var dialogs = new FakeDialogs();
        using var vm = await Editor(dialogs, new FailedExporter());
        await vm.ExportGameCommand.ExecuteAsync(null);
        Assert.False(vm.IsBusy);
        Assert.Contains(dialogs.Errors, e => e.Contains("Não foi possível compilar") && e.Contains("CS1002"));
    });

    [Fact]
    public void Fechar_editor_cancela_preparacao_sem_avisos_tardios() => Ui.Run(async () =>
    {
        var exporter = new PausedExporter();
        var dialogs = new FakeDialogs();
        using var vm = await Editor(dialogs, exporter);
        var task = vm.ExportGameCommand.ExecuteAsync(null);
        await exporter.Started.Task;
        vm.Dispose();
        await task;
        Assert.True(exporter.WasCancelled);
        Assert.Empty(dialogs.Errors);
        Assert.False(vm.IsExporting);
    });

    [Fact]
    public void Sem_SDK_exportacao_explica_o_que_falta_e_nao_publica() => Ui.Run(async () =>
    {
        var dialogs = new FakeDialogs();
        var exporter = new CompletedExporter();
        using var vm = await Editor(dialogs, exporter);
        Dotnet.SimulateMissing = true;
        try
        {
            await vm.ExportGameCommand.ExecuteAsync(null);
            Assert.Equal(1, dialogs.SdkMissingShown);
            Assert.Equal(0, exporter.Calls);
            Assert.False(vm.IsBusy);
            Assert.Empty(dialogs.Errors);
        }
        finally { Dotnet.SimulateMissing = false; Dotnet.ResetCache(); }
    });

    [Fact]
    public void Estudio_mostra_cancelamento_e_finalizacao_sem_permitir_interromper_a_troca() => Ui.Run(async () =>
    {
        var exporter = new FinalizingExporter();
        using var vm = await Editor(new FakeDialogs(), exporter);
        var studio = new StudioView();
        studio.Bind(vm);
        var window = new Window { Content = studio, Width = 1320, Height = 800, ShowInTaskbar = false, ShowActivated = false, Left = -10000, Top = -10000 };
        window.Show();
        try
        {
            vm.OpenStudio();
            studio.Open(null);
            var task = vm.ExportGameCommand.ExecuteAsync(null);
            await Ui.WaitUntil(() => vm.IsExportFinalizing);
            window.UpdateLayout();
            var export = Descendants(studio).OfType<Button>().Single(b => Descendants(b).OfType<TextBlock>().Any(t => t.Text == "Finalizando…"));
            Assert.False(export.IsEnabled);
            Assert.Equal("Finalizando…", vm.RunButtonText);
            vm.StopCommand.Execute(null);
            Assert.False(exporter.Token.IsCancellationRequested);
            Assert.False(task.IsCompleted);

            var bitmap = new RenderTargetBitmap(1320, 800, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(Path.GetTempPath(), "csharplab-exportacao.png"))) png.Save(file);

            exporter.Finish.SetResult(new("pacotes", "Aventura.exe", "Aventura.zip", null));
            await task;
            Assert.True(export.IsEnabled);
            Assert.False(vm.IsBusy);
        }
        finally
        {
            exporter.Finish.TrySetResult(new("pacotes", "Aventura.exe", "Aventura.zip", null));
            window.Close();
        }
    });

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var item in Descendants(child)) yield return item;
        }
    }

    [Fact]
    public void Nao_inicia_exportacao_enquanto_o_jogo_esta_rodando() => Ui.Run(async () =>
    {
        var exporter = new CompletedExporter();
        using var vm = await Editor(new FakeDialogs(), exporter);
        vm.RunState = RunState.Running;
        try
        {
            Assert.False(vm.CanUseExportButton);
            await vm.ExportGameCommand.ExecuteAsync(null);
            Assert.Equal(0, exporter.Calls);
            Assert.False(vm.IsExporting);
        }
        finally { vm.RunState = RunState.Idle; }
    });

    private sealed class PausedExporter : IGameExporter
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }
        public bool WasCancelled { get; private set; }
        public async Task<GameExportResult> ExportAsync(ProjectFile project, string destination, IProgress<GameExportStage>? progress, CancellationToken ct)
        {
            Calls++;
            progress?.Report(GameExportStage.Compiling);
            Started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, ct); }
            catch (OperationCanceledException) { WasCancelled = true; throw; }
            throw new InvalidOperationException();
        }
    }

    private sealed class CompletedExporter : IGameExporter
    {
        public int Calls { get; private set; }
        public Task<GameExportResult> ExportAsync(ProjectFile project, string destination, IProgress<GameExportStage>? progress, CancellationToken ct)
        {
            Calls++;
            progress?.Report(GameExportStage.Installing);
            return Task.FromResult(new GameExportResult(destination, "Jogo.exe", Path.Combine(destination, "Aventura.zip"), null));
        }
    }

    private sealed class FinalizingExporter : IGameExporter
    {
        public TaskCompletionSource<GameExportResult> Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; private set; }
        public Task<GameExportResult> ExportAsync(ProjectFile project, string destination, IProgress<GameExportStage>? progress, CancellationToken ct)
        {
            Token = ct;
            progress?.Report(GameExportStage.Installing);
            return Finish.Task;
        }
    }

    private sealed class FailedExporter : IGameExporter
    {
        public Task<GameExportResult> ExportAsync(ProjectFile project, string destination, IProgress<GameExportStage>? progress, CancellationToken ct)
            => Task.FromException<GameExportResult>(new GameExportException("Não foi possível compilar", "CS1002"));
    }
}
