using System.Reflection;
using CSharpLab.Core.Files;
using CSharpLab.Core.Projects;
using CSharpLab.Core.Settings;
using CSharpLab.Editor;
using CSharpLab.ViewModels;

namespace CSharpLab.App.Tests;

public sealed class EditorRecoveryTests
{
    private static MainViewModel Create() => new(new AppSettings { CheckForUpdates = false })
        { Dialogs = new FakeDialogs(), Terminal = new FakeTerminal() };

    [Fact]
    public void Recuperacao_sobrevive_a_duas_aberturas_sem_gravar_substituta() => Ui.Run(async () =>
    {
        var store = new RecoveryStore();
        var id = Guid.NewGuid().ToString("N");
        var path = Path.Combine(Ui.NewFolder(), "Program.cs");
        File.WriteAllText(path, "// disco");
        store.Save(RecoveryStore.Create(id, path, null, "// trabalho recuperado", TextFileIO.Utf8NoBom, 0));
        Assert.True(store.Flush(TimeSpan.FromSeconds(5)));
        try
        {
            for (int i = 0; i < 2; i++)
            {
                using var vm = Create();
                await vm.InitializeAsync();
                var doc = Assert.Single(vm.Documents, d => d.RecoveryId == id);
                Assert.Equal("// trabalho recuperado", doc.Document.Text);
                Assert.True(doc.IsDirty);
                Assert.True(doc.MatchesDiskVersion("// disco"));
                Assert.Contains(store.LoadAll(), e => e.Id == id);
            }
        }
        finally { store.Delete(id); store.Flush(TimeSpan.FromSeconds(5)); }
    });

    [Fact]
    public void Editor_atualiza_extensao_defines_e_indentacao() => Ui.Run(async () =>
    {
        using var vm = Create();
        await vm.InitializeAsync();
        var folder = Ui.NewFolder();
        var path = Path.Combine(folder, "Teste.txt");
        File.WriteAllText(path, "#if ATIVO\nclass C {}\n#endif");
        File.WriteAllText(Path.Combine(folder, ".editorconfig"), "root=true\n[*.cs]\nindent_size=2\nindent_style=space\n");
        var doc = vm.OpenFile(path)!;
        var editor = new CodeEditor(doc, vm);
        try
        {
            Assert.DoesNotContain(editor.TextArea.TextView.LineTransformers, t => t is CSharpColorizer);
            var renamed = Path.ChangeExtension(path, ".cs");
            File.Move(path, renamed);
            vm.OnPathRenamed(path, renamed);
            await Ui.WaitUntil(() => editor.Options.IndentationSize == 2);
            Assert.Single(editor.TextArea.TextView.LineTransformers.OfType<CSharpColorizer>());
            var model = ProjectModel.Loose("Teste", folder) with { CompileFiles = [renamed], DefineConstants = ["ATIVO"] };
            vm.Language!.LoadProject(model);
            await Ui.WaitUntil(() => editor.Syntax.Options.PreprocessorSymbolNames.Contains("ATIVO"));
            Assert.Single(((Microsoft.CodeAnalysis.CSharp.Syntax.CompilationUnitSyntax)editor.Syntax.Root).Members);
            vm.Language.LoadProject(model with { DefineConstants = [] });
            await Ui.WaitUntil(() => !editor.Syntax.Options.PreprocessorSymbolNames.Contains("ATIVO"));
            Assert.Empty(((Microsoft.CodeAnalysis.CSharp.Syntax.CompilationUnitSyntax)editor.Syntax.Root).Members);
            File.Move(renamed, path);
            vm.OnPathRenamed(renamed, path);
            await Ui.WaitUntil(() => !editor.TextArea.TextView.LineTransformers.OfType<CSharpColorizer>().Any());
        }
        finally { editor.Detach(); }
    });

    [Theory]
    [InlineData("Console.WriteLine(\"{}\");")]
    [InlineData("// {}")]
    [InlineData("/* {} */")]
    public void Enter_entre_chaves_em_texto_nao_expande_bloco(string text) => Ui.Run(async () =>
    {
        using var vm = Create();
        await vm.InitializeAsync();
        var doc = vm.ActiveDocument!;
        doc.Document.Text = text;
        var editor = new CodeEditor(doc, vm) { CaretOffset = text.IndexOf('{') + 1 };
        try
        {
            Assert.False((bool)typeof(CodeEditor).GetMethod("TryEnterBetweenBraces", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(editor, null)!);
            Assert.Equal(text, doc.Document.Text);
        }
        finally { editor.Detach(); }
    });

    [Theory]
    [InlineData("", "  ")]
    [InlineData("    ", "      ")]
    [InlineData("      ", "\t")]
    [InlineData("\t", "\t  ")]
    public void Enter_respeita_indent_size_diferente_da_largura_do_tab(string indent, string inner) => Ui.Run(async () =>
    {
        using var vm = Create();
        await vm.InitializeAsync();
        var dir = Ui.NewFolder();
        File.WriteAllText(Path.Combine(dir, ".editorconfig"), "root=true\n[*.cs]\nindent_style=tab\nindent_size=2\ntab_width=8\n");
        var path = Path.Combine(dir, "Teste.cs");
        File.WriteAllText(path, indent + "if (true) {}");
        var doc = vm.OpenFile(path)!;
        var editor = new CodeEditor(doc, vm);
        try
        {
            await Ui.WaitUntil(() => !editor.Options.ConvertTabsToSpaces && editor.Options.IndentationSize == 8);
            editor.CaretOffset = doc.Document.Text.IndexOf('{') + 1;
            Assert.True((bool)typeof(CodeEditor).GetMethod("TryEnterBetweenBraces", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(editor, null)!);
            Assert.Equal(inner, doc.Document.GetText(doc.Document.GetLineByNumber(2)));
            Assert.Equal(indent + "}", doc.Document.GetText(doc.Document.GetLineByNumber(3)));
        }
        finally { editor.Detach(); }
    });

    [Fact]
    public void Completion_substitui_sufixo_no_meio_de_palavra_e_prefixo_digitado_depois() => Ui.Run(async () =>
    {
        using var vm = Create();
        await vm.InitializeAsync();
        var doc = vm.ActiveDocument!;
        var editor = new CodeEditor(doc, vm);
        try
        {
            foreach (var (word, extra) in new[] { ("Writexyz", ""), ("Wri", "te") })
            {
                doc.Document.Text = "Console." + word;
                editor.CaretOffset = "Console.Wri".Length;
                var result = await vm.Language!.GetCompletionsAsync(doc.LanguageKey, editor.CaretOffset, null, CancellationToken.None);
                Assert.NotNull(result);
                var item = Assert.Single(result.Items, i => i.DisplayText == "WriteLine");
                if (extra.Length > 0) doc.Document.Insert(editor.CaretOffset, extra);
                editor.CompletionController.Commit(new CompletionEntry(item, result), "Console.".Length, word.Length + extra.Length);
                // WriteLine tem parâmetros: entra com "()" e o cursor dentro.
                await Ui.WaitUntil(() => doc.Document.Text == "Console.WriteLine()", what: "completion sem sufixo duplicado");
                Assert.Equal("Console.WriteLine(".Length, editor.CaretOffset);
            }

            // Tab seguido de "(" antes do Roslyn responder: a sugestão entra e o "(" fica depois dela.
            doc.Document.Text = "Console.Wri";
            editor.CaretOffset = doc.Document.TextLength;
            var r = await vm.Language!.GetCompletionsAsync(doc.LanguageKey, editor.CaretOffset, null, CancellationToken.None);
            var writeLine = Assert.Single(r!.Items, i => i.DisplayText == "WriteLine");
            editor.CompletionController.Commit(new CompletionEntry(writeLine, r), "Console.".Length, "Wri".Length);
            editor.TextArea.PerformTextInput("(");
            await Ui.WaitUntil(() => doc.Document.Text.StartsWith("Console.WriteLine("), what: "completion com tecla digitada logo depois");
            Assert.Equal(doc.Document.Text.IndexOf('(') + 1, editor.CaretOffset);
            Assert.DoesNotContain("((", doc.Document.Text);

            // Método sem parâmetros: "()" e o cursor depois, pronto para o ";".
            doc.Document.Text = "string nome = Console.Read";
            editor.CaretOffset = doc.Document.TextLength;
            var rl = await vm.Language!.GetCompletionsAsync(doc.LanguageKey, editor.CaretOffset, null, CancellationToken.None);
            var readLine = Assert.Single(rl!.Items, i => i.DisplayText == "ReadLine");
            editor.CompletionController.Commit(new CompletionEntry(readLine, rl), "string nome = Console.".Length, "Read".Length);
            await Ui.WaitUntil(() => doc.Document.Text == "string nome = Console.ReadLine()", what: "parênteses do ReadLine");
            Assert.Equal(doc.Document.TextLength, editor.CaretOffset);
        }
        finally { editor.Detach(); }
    });
}
