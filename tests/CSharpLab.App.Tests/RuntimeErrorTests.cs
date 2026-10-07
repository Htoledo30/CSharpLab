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

    /// <summary>
    /// Jogo com botões pelo F5: compila, roda pelo terminal e um erro do motor aparece em português,
    /// com a linha. A cena quebra antes de a janela abrir, então nada aparece na tela durante o teste.
    /// </summary>
    [Fact]
    public void Jogo_com_erro_do_motor_aponta_a_linha_em_portugues() => Ui.Run(async () =>
    {
        var terminal = new FakeTerminal();
        using var vm = new MainViewModel(new AppSettings()) { Dialogs = new FakeDialogs(), Terminal = terminal };
        await vm.InitializeAsync();
        var created = ProjectCreator.CreateGameProject(Ui.NewFolder("jogo"), "Jogo",
            "var game = new Game(\"Teste\");\ngame.Scene(\"Start\", () =>\n{\n    game.GoTo(\"Florest\");\n});\ngame.Scene(\"Forest\", () => { });\ngame.Start(\"Start\");\n");
        await vm.OpenFolderAsync(created.Directory, created.ProjectPath, promptForUnsaved: false);
        Assert.True(vm.RunProject?.IsWindowApp);

        var dialogs = (FakeDialogs)vm.Dialogs;
        vm.RunOrStopCommand.Execute(null);
        // O jogo para logo ao abrir: espera o aviso do terminal, não o estado "executando" (que dura pouco).
        await Ui.WaitUntil(() => terminal.Notices.Any(n => n.Contains("janela própria")) || vm.RunState == RunState.Idle, 120_000, "execução");
        Assert.True(terminal.Notices.Any(n => n.Contains("janela própria")),
            "O jogo não rodou: " + vm.StatusText + " " + string.Join(" | ", vm.Problems.Errors.Select(p => p.Diagnostic.Message)) + string.Join(" | ", dialogs.Errors));
        Assert.Empty(dialogs.Errors);
        await Ui.WaitUntil(() => vm.RunState == RunState.Idle, 30_000, "o jogo parar");

        Assert.Contains(terminal.Notices, n => n.Contains("Erro na linha 4 (Program.cs)") &&
                                               n.Contains("A cena \"Florest\" não existe. Você quis dizer \"Forest\"?"));
        var problem = Assert.Single(vm.Problems.Errors, p => p.Diagnostic.Id == RuntimeErrors.DiagnosticId);
        Assert.Equal(4, problem.Diagnostic.Line);
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
