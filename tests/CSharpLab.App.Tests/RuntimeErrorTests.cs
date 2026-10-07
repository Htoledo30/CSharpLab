using CSharpLab.Core.Language;
using CSharpLab.Core.Projects;
using CSharpLab.Core.Settings;
using CSharpLab.ViewModels;

namespace CSharpLab.App.Tests;

/// <summary>Programa real que para com um erro: a explicação em português aparece no terminal e em Problemas.</summary>
public sealed class RuntimeErrorTests
{
    private static async Task<(MainViewModel Vm, FakeTerminal Terminal, FakeDialogs Dialogs)> RunCrashing(
        string program, string? extraFile = null, string? extraCode = null)
    {
        var terminal = new FakeTerminal();
        var dialogs = new FakeDialogs();
        var vm = new MainViewModel(new AppSettings()) { Dialogs = dialogs, Terminal = terminal };
        await vm.InitializeAsync();
        var created = ProjectCreator.CreateConsoleProject(Ui.NewFolder("crash"), "Crash", program);
        if (extraFile != null) File.WriteAllText(Path.Combine(created.Directory, extraFile), extraCode);
        await vm.OpenFolderAsync(created.Directory, created.ProjectPath, promptForUnsaved: false);
        vm.RunOrStopCommand.Execute(null);
        await Ui.WaitUntil(() => vm.RunState == RunState.Running, 120_000, "execução");
        await Ui.WaitUntil(() => vm.RunState == RunState.Idle, 30_000, "o programa parar");
        return (vm, terminal, dialogs);
    }

    [Fact]
    public void Texto_que_nao_e_numero_aponta_a_linha() => Ui.Run(async () =>
    {
        var (vm, terminal, dialogs) = await RunCrashing("Console.WriteLine(\"oi\");\nstring texto = \"abc\";\nint idade = int.Parse(texto);\n");
        using var _ = vm;
        Assert.Contains(terminal.Notices, n => n.Contains("Erro na linha 3 (Program.cs)") && n.Contains("\"abc\" não é um número válido"));
        Assert.Contains(terminal.Notices, n => n.StartsWith("Dica:") && n.Contains("TryParse"));
        var problem = Assert.Single(vm.Problems.Errors, p => p.Diagnostic.Id == RuntimeErrors.DiagnosticId);
        Assert.Equal(3, problem.Diagnostic.Line);
        Assert.True(problem.HasHelp);

        await vm.OpenProblemHelpCommand.ExecuteAsync(problem);
        Assert.Equal("https://learn.microsoft.com/pt-br/dotnet/api/system.formatexception", Assert.Single(dialogs.OpenedUrls));

        // Ao executar de novo, o erro antigo some.
        vm.RunOrStopCommand.Execute(null);
        await Ui.WaitUntil(() => vm.RunState != RunState.Idle, 10_000, "nova execução");
        Assert.DoesNotContain(vm.Problems.Errors, p => p.Diagnostic.Id == RuntimeErrors.DiagnosticId);
        await Ui.WaitUntil(() => vm.RunState == RunState.Idle, 60_000, "fim");
    }, 300);

    [Fact]
    public void Erro_dentro_de_outro_arquivo_aponta_esse_arquivo() => Ui.Run(async () =>
    {
        var (vm, terminal, _) = await RunCrashing("var h = new Heroi();\nh.Atacar();\n", "Heroi.cs",
            "class Heroi\n{\n    string? arma;\n    public void Atacar()\n    {\n        System.Console.WriteLine(arma!.Length);\n    }\n}\n");
        using var _ = vm;
        Assert.Contains(terminal.Notices, n => n.Contains("Erro na linha 6 (Heroi.cs)") && n.Contains("null"));
        var problem = Assert.Single(vm.Problems.Errors, p => p.Diagnostic.Id == RuntimeErrors.DiagnosticId);
        Assert.EndsWith("Heroi.cs", problem.Diagnostic.FilePath);
    }, 300);
}
