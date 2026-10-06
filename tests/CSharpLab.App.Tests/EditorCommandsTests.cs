using CSharpLab.Core.Projects;
using CSharpLab.Core.Settings;
using CSharpLab.Editor;
using CSharpLab.ViewModels;

namespace CSharpLab.App.Tests;

public sealed class EditorCommandsTests
{
    private static (MainViewModel Vm, CodeEditor Editor) Create(string text)
    {
        var vm = new MainViewModel(new AppSettings()) { Dialogs = new FakeDialogs(), Terminal = new FakeTerminal() };
        var doc = vm.NewDraft(text.Replace("\n", "\r\n"));
        return (vm, new CodeEditor(doc, vm));
    }

    private static string Text(CodeEditor e) => e.Document.Text.Replace("\r\n", "\n");

    [Fact]
    public void Comentar_e_descomentar_linhas() => Ui.Run(() =>
    {
        var (vm, editor) = Create("if (x)\n{\n    a();\n    b();\n}");
        editor.Select(editor.Document.GetLineByNumber(3).Offset, 10);
        editor.ToggleComment();
        Assert.Equal("if (x)\n{\n    // a();\n    b();\n}", Text(editor));

        // Bloco de duas linhas com recuo comum.
        var l3 = editor.Document.GetLineByNumber(3);
        var l4 = editor.Document.GetLineByNumber(4);
        editor.Select(l3.Offset, l4.EndOffset - l3.Offset);
        editor.ToggleComment();
        Assert.Equal("if (x)\n{\n    // // a();\n    // b();\n}", Text(editor));
        editor.ToggleComment();
        Assert.Equal("if (x)\n{\n    // a();\n    b();\n}", Text(editor));

        // Um único desfazer volta tudo.
        editor.Undo();
        Assert.Equal("if (x)\n{\n    // // a();\n    // b();\n}", Text(editor));
        vm.Dispose();
        return Task.CompletedTask;
    });

    [Fact]
    public void Duplicar_mover_e_apagar_linhas() => Ui.Run(() =>
    {
        var (vm, editor) = Create("um\ndois\ntres");
        editor.CaretOffset = editor.Document.GetLineByNumber(2).Offset + 1;
        editor.DuplicateLines();
        Assert.Equal("um\ndois\ndois\ntres", Text(editor));
        Assert.Equal(3, editor.Document.GetLineByOffset(editor.CaretOffset).LineNumber);

        editor.MoveLines(1);
        Assert.Equal("um\ndois\ntres\ndois", Text(editor));
        Assert.Equal(4, editor.Document.GetLineByOffset(editor.CaretOffset).LineNumber);

        editor.MoveLines(-1);
        editor.MoveLines(-1);
        editor.MoveLines(-1);
        Assert.Equal("dois\num\ndois\ntres", Text(editor));
        editor.MoveLines(-1); // já está no topo: nada muda
        Assert.Equal("dois\num\ndois\ntres", Text(editor));

        editor.DeleteLines();
        Assert.Equal("um\ndois\ntres", Text(editor));
        editor.CaretOffset = editor.Document.TextLength;
        editor.DeleteLines();
        Assert.Equal("um\ndois", Text(editor));
        vm.Dispose();
        return Task.CompletedTask;
    });

    [Fact]
    public void Renomear_altera_abas_abertas_e_abre_os_outros_arquivos() => Ui.Run(async () =>
    {
        var dialogs = new FakeDialogs { TextAnswer = "Pontos" };
        using var vm = new MainViewModel(new AppSettings()) { Dialogs = dialogs, Terminal = new FakeTerminal() };
        await vm.InitializeAsync();
        var created = ProjectCreator.CreateConsoleProject(Ui.NewFolder("ren"), "Ren",
            "var h = new Heroi();" + Environment.NewLine + "h.Vida = 3;" + Environment.NewLine);
        File.WriteAllText(Path.Combine(created.Directory, "Heroi.cs"), "class Heroi { public int Vida; }");
        await vm.OpenFolderAsync(created.Directory, created.ProjectPath, promptForUnsaved: false);
        var program = vm.OpenFile(created.ProgramPath)!;
        await Ui.WaitUntil(() => vm.Language?.GetDocument(program.LanguageKey) != null, what: "documento no Roslyn");

        var editor = new CodeEditor(program, vm);
        editor.CaretOffset = program.Document.Text.IndexOf("Vida") + 1;
        await editor.RenameSymbolAsync();

        Assert.Contains("h.Pontos = 3;", program.Document.Text);
        var heroi = vm.FindDocument(Path.Combine(created.Directory, "Heroi.cs"));
        Assert.NotNull(heroi);
        Assert.Equal("class Heroi { public int Pontos; }", heroi!.Document.Text);
        Assert.True(heroi.IsDirty);
        // Nada é gravado sem o usuário salvar.
        Assert.Equal("class Heroi { public int Vida; }", File.ReadAllText(Path.Combine(created.Directory, "Heroi.cs")));
    });
}
