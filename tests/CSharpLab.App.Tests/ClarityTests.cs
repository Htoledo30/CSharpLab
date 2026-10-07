using CSharpLab.Core.Language;
using CSharpLab.Core.Projects;
using CSharpLab.Core.Settings;
using CSharpLab.ViewModels;

namespace CSharpLab.App.Tests;

/// <summary>Textos que dizem ao iniciante o que vai acontecer: qual projeto roda, quantos erros há, onde digitar.</summary>
public sealed class ClarityTests
{
    [Fact]
    public void Botao_Executar_mostra_o_projeto_que_vai_rodar() => Ui.Run(async () =>
    {
        using var vm = new MainViewModel(new AppSettings()) { Dialogs = new FakeDialogs(), Terminal = new FakeTerminal() };
        await vm.InitializeAsync();
        Assert.Null(vm.RunTargetName);
        Assert.Contains("vira um projeto", vm.RunButtonToolTip);

        var created = ProjectCreator.CreateConsoleProject(Ui.NewFolder("alvo"), "Arena");
        await vm.OpenFolderAsync(created.Directory, created.ProjectPath, promptForUnsaved: false);
        Assert.Equal("Arena", vm.RunTargetName);
        Assert.Contains("\"Arena\"", vm.RunButtonToolTip);
    });

    [Fact]
    public void Escolher_um_csproj_em_Abrir_arquivo_abre_o_projeto() => Ui.Run(async () =>
    {
        var dialogs = new FakeDialogs();
        using var vm = new MainViewModel(new AppSettings()) { Dialogs = dialogs, Terminal = new FakeTerminal() };
        await vm.InitializeAsync();
        var created = ProjectCreator.CreateConsoleProject(Ui.NewFolder("abrir"), "Jogo");
        dialogs.FileAnswer = created.ProjectPath;
        await vm.OpenFileDialogCommand.ExecuteAsync(null);
        Assert.Equal(created.Directory, vm.CurrentFolder);
        Assert.Equal("Jogo", vm.RunTargetName);
        Assert.DoesNotContain(vm.Documents, d => d.FilePath?.EndsWith(".csproj") == true);
    });

    [Fact]
    public void Resumo_de_problemas_por_extenso()
    {
        var problems = new ProblemsViewModel(k => k ?? "");
        Assert.Equal("", problems.Summary);
        CodeDiagnostic D(DiagnosticLevel level, int line) =>
            new("CS0000", level, "x", "x", @"C:\p\Program.cs", line, 1, -1, 0, FromBuild: false);
        problems.SetLive([D(DiagnosticLevel.Error, 1)]);
        Assert.Equal("1 erro", problems.Summary);
        problems.SetLive([D(DiagnosticLevel.Error, 1), D(DiagnosticLevel.Error, 2), D(DiagnosticLevel.Warning, 3)]);
        Assert.Equal("2 erros, 1 aviso", problems.Summary);
        problems.SetLive([D(DiagnosticLevel.Warning, 1), D(DiagnosticLevel.Warning, 2)]);
        Assert.Equal("2 avisos", problems.Summary);
        Assert.False(problems.HasErrors);
    }

    [Fact]
    public void Dica_do_terminal_aparece_so_nas_primeiras_execucoes() => Ui.Run(async () =>
    {
        var settings = new AppSettings();
        using var vm = new MainViewModel(settings) { Dialogs = new FakeDialogs(), Terminal = new FakeTerminal() };
        await vm.InitializeAsync();
        var created = ProjectCreator.CreateConsoleProject(Ui.NewFolder("dica"), "Dica", "Console.ReadLine();" + Environment.NewLine);
        await vm.OpenFolderAsync(created.Directory, created.ProjectPath, promptForUnsaved: false);
        Assert.False(vm.ShowTerminalHint);

        for (int i = 0; i < 4; i++)
        {
            vm.RunOrStopCommand.Execute(null);
            await Ui.WaitUntil(() => vm.RunState == RunState.Running, 120_000, "execução");
            Assert.Equal(i < 3, vm.ShowTerminalHint);
            vm.StopRun();
            await Ui.WaitUntil(() => vm.RunState == RunState.Idle, 15_000, "fim");
            Assert.False(vm.ShowTerminalHint);
        }
        Assert.Equal(3, settings.TerminalHintRuns);
    }, 300);
}
