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
}
