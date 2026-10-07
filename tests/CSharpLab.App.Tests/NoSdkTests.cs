using CSharpLab.Core.Projects;
using CSharpLab.Core.Settings;
using CSharpLab.ViewModels;

namespace CSharpLab.App.Tests;

/// <summary>Notebook novo, sem o SDK .NET: o editor abre, sugere e mostra erros; só o Executar pede o SDK.</summary>
public sealed class NoSdkTests
{
    [Fact]
    public void Sem_SDK_o_editor_funciona_e_o_Executar_explica_o_que_falta() => Ui.Run(async () =>
    {
        // O projeto é criado antes, como se tivesse vindo de outro computador.
        var created = ProjectCreator.CreateConsoleProject(Ui.NewFolder("semsdk"), "SemSdk", "Console.WriteLine(\"oi\");\nint x = \"a\";\n");
        Dotnet.SimulateMissing = true;
        try
        {
            var dialogs = new FakeDialogs();
            using var vm = new MainViewModel(new AppSettings()) { Dialogs = dialogs, Terminal = new FakeTerminal() };
            await vm.InitializeAsync();
            await vm.OpenFolderAsync(created.Directory, created.ProjectPath, promptForUnsaved: false);
            await Ui.WaitUntil(() => vm.SdkMissing, 30_000, "aviso de SDK ausente");

            var doc = vm.OpenFile(created.ProgramPath)!;
            await Ui.WaitUntil(() => vm.Language?.GetDocument(doc.LanguageKey) != null, 30_000, "documento no Roslyn");
            var completions = await vm.Language!.GetCompletionsAsync(doc.LanguageKey, "Console.".Length, '.', CancellationToken.None);
            Assert.Contains(completions!.Items, i => i.DisplayText == "WriteLine");

            // Erros continuam aparecendo em português, sem compilar.
            await Ui.WaitUntil(() => vm.Problems.Errors.Any(p => p.Diagnostic.Id == "CS0029"), 30_000, "erro ao vivo");

            vm.RunOrStopCommand.Execute(null);
            await Ui.WaitUntil(() => dialogs.SdkMissingShown == 1, 30_000, "explicação do SDK");
            await Ui.WaitUntil(() => vm.RunState == RunState.Idle, 10_000, "voltar ao normal");
            Assert.Empty(dialogs.Errors);
        }
        finally
        {
            Dotnet.SimulateMissing = false;
            Dotnet.ResetCache();
        }
    }, 180);
}
