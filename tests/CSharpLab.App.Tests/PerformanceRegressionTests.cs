using System.Diagnostics;
using CSharpLab.Core.Projects;
using CSharpLab.Core.Settings;
using CSharpLab.ViewModels;
using Xunit.Abstractions;

namespace CSharpLab.App.Tests;

/// <summary>Garantias de desempenho do ciclo editar → salvar → executar.</summary>
public sealed class PerformanceRegressionTests(ITestOutputHelper output)
{
    [Fact]
    public void Salvar_nao_recarrega_o_projeto_e_reexecutar_sem_mudancas_nao_recompila() => Ui.Run(async () =>
    {
        var terminal = new FakeTerminal();
        using var vm = new MainViewModel(new AppSettings()) { Dialogs = new FakeDialogs(), Terminal = terminal };
        await vm.InitializeAsync();
        var created = ProjectCreator.CreateConsoleProject(Ui.NewFolder("perf"), "Rapido",
            "Console.WriteLine(\"oi\");" + Environment.NewLine + "Console.ReadLine();" + Environment.NewLine);
        await vm.OpenFolderAsync(created.Directory, created.ProjectPath, promptForUnsaved: false);
        await Ui.WaitUntil(() => vm.Language?.MainModel?.IsEvaluated == true, 60_000, "avaliação do projeto");

        async Task<long> RunOnce()
        {
            var sw = Stopwatch.StartNew();
            vm.RunOrStopCommand.Execute(null);
            await Ui.WaitUntil(() => vm.RunState == RunState.Running, 120_000, "programa em execução");
            var elapsed = sw.ElapsedMilliseconds;
            vm.StopRun();
            await Ui.WaitUntil(() => vm.RunState == RunState.Idle, 15_000, "fim da execução");
            return elapsed;
        }

        output.WriteLine($"Primeira execução: {await RunOnce()} ms");
        Assert.False(vm.LastRunReusedBuild);
        var again = await RunOnce();
        output.WriteLine($"Executar de novo sem mudanças: {again} ms");
        Assert.True(vm.LastRunReusedBuild, "Sem mudanças, o programa deve rodar sem recompilar.");

        int reloads = 0;
        vm.Language!.ProjectChanged += () => reloads++;
        var doc = vm.OpenFile(created.ProgramPath)!;
        doc.Document.Insert(0, "// mudança" + Environment.NewLine);
        Assert.True(vm.Save(doc));
        await Task.Delay(2000);
        Assert.Equal(0, reloads);

        doc.Document.Insert(0, "// outra" + Environment.NewLine);
        output.WriteLine($"Editar e executar: {await RunOnce()} ms");
        Assert.False(vm.LastRunReusedBuild);
        Assert.False(doc.IsDirty);
    }, 300);

    [Fact]
    public void Erro_de_compilacao_mostra_Problemas_e_nunca_reaproveita_build_antigo() => Ui.Run(async () =>
    {
        using var vm = new MainViewModel(new AppSettings()) { Dialogs = new FakeDialogs(), Terminal = new FakeTerminal() };
        await vm.InitializeAsync();
        var created = ProjectCreator.CreateConsoleProject(Ui.NewFolder("falha"), "Falha", "Console.WriteLine(\"ok\");" + Environment.NewLine);
        await vm.OpenFolderAsync(created.Directory, created.ProjectPath, promptForUnsaved: false);

        async Task Run(RunState expected)
        {
            vm.RunOrStopCommand.Execute(null);
            await Ui.WaitUntil(() => vm.RunState == expected, 120_000, expected.ToString());
            if (expected == RunState.Running) vm.StopRun();
            await Ui.WaitUntil(() => vm.RunState == RunState.Idle, 15_000, "fim");
        }

        await Run(RunState.Running);
        vm.PanelTab = "terminal";

        var doc = vm.OpenFile(created.ProgramPath)!;
        doc.Document.Text = "Console.WriteLine(\"ok\")" + Environment.NewLine; // faltou ";"
        vm.RunOrStopCommand.Execute(null);
        await Ui.WaitUntil(() => vm.RunState == RunState.Idle && vm.StatusText.Contains("falhou"), 120_000, "compilação com erro");
        Assert.False(vm.LastRunReusedBuild);
        Assert.Equal("problems", vm.PanelTab);
        Assert.True(vm.IsPanelOpen);

        // Volta ao texto que já compilou antes: compila de novo em vez de usar o programa velho.
        doc.Document.Text = "Console.WriteLine(\"ok\");" + Environment.NewLine;
        await Run(RunState.Running);
        Assert.False(vm.LastRunReusedBuild);
    }, 300);
}
