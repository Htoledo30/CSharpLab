using System.Windows;
using System.Windows.Input;
using CSharpLab.Core.Settings;
using CSharpLab.Editor;
using CSharpLab.ViewModels;

namespace CSharpLab.App.Tests;

/// <summary>
/// A sensação de digitar: teclas de verdade passando pelo mesmo caminho do app (PreviewKeyDown → KeyDown
/// → comandos do AvalonEdit), com o editor numa janela fora da tela que não rouba o foco.
/// </summary>
public sealed class EditorTypingTests
{
    private const string Method = "class A\n{\n    void M()\n    {\n        |\n    }\n}";

    /// <summary>Editor com o texto dado; "|" marca o cursor (e some do texto).</summary>
    private sealed class Typist : IDisposable
    {
        private readonly Window _window;
        private readonly MainViewModel _vm;

        public Typist(string text)
        {
            _vm = new MainViewModel(new AppSettings()) { Dialogs = new FakeDialogs(), Terminal = new FakeTerminal() };
            int caret = text.IndexOf('|');
            var doc = _vm.NewDraft(text.Replace("|", "").Replace("\n", "\r\n"));
            Editor = new CodeEditor(doc, _vm);
            _window = new Window
            {
                Content = Editor, Width = 800, Height = 600, Left = -20000, Top = -20000,
                ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None,
            };
            _window.Show();
            if (caret >= 0) Editor.CaretOffset = Offset(caret);
        }

        public CodeEditor Editor { get; }

        /// <summary>Posição no texto com "\r\n" a partir da posição no texto com "\n".</summary>
        private int Offset(int plain) => Editor.Document.Text.Replace("\r\n", "\n")[..plain].Count(c => c == '\n') + plain;

        /// <summary>Texto com "\n" e o cursor como "|" (seleção entre "[" e "]").</summary>
        public string Text
        {
            get
            {
                var text = Editor.Document.Text;
                var marks = new List<(int At, string Mark)> { (Editor.CaretOffset, "|") };
                if (!Editor.TextArea.Selection.IsEmpty)
                {
                    var s = Editor.TextArea.Selection.SurroundingSegment;
                    marks.Add((s.Offset, "["));
                    marks.Add((s.EndOffset, "]"));
                }
                foreach (var (at, mark) in marks.OrderByDescending(m => m.At).ThenBy(m => m.Mark == "|" ? 1 : 0))
                    text = text.Insert(at, mark);
                return text.Replace("\r\n", "\n");
            }
        }

        public string Selected => Editor.SelectedText;

        public void Type(string text)
        {
            foreach (var c in text)
            {
                if (c == '\n') Press(Key.Enter);
                else Editor.TextArea.PerformTextInput(c.ToString());
            }
        }

        public void Press(Key key, int times = 1)
        {
            for (int i = 0; i < times; i++)
            {
                var source = PresentationSource.FromVisual(_window)!;
                var preview = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                Editor.TextArea.RaiseEvent(preview);
                if (preview.Handled) continue;
                // O Enter do AvalonEdit só age com o foco do teclado; sem roubar o foco, faz o mesmo que ele.
                if (key == Key.Enter)
                {
                    Editor.TextArea.PerformTextInput("\n");
                    continue;
                }
                var down = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.KeyDownEvent };
                Editor.TextArea.RaiseEvent(down);
            }
        }

        public void Select(string text)
        {
            int at = Editor.Document.Text.IndexOf(text, StringComparison.Ordinal);
            Assert.True(at >= 0, $"\"{text}\" não está no texto");
            Editor.Select(at, text.Length);
        }

        public void Dispose()
        {
            _window.Close();
            _vm.Dispose();
        }
    }

    private static void Run(Action test) => Ui.Run(() =>
    {
        test();
        return Task.CompletedTask;
    });

    [Fact]
    public void Backspace_no_recuo_volta_um_nivel_inteiro() => Run(() =>
    {
        using var t = new Typist(Method);
        t.Press(Key.Back);
        Assert.Equal("class A\n{\n    void M()\n    {\n    |\n    }\n}", t.Text);
        t.Press(Key.Back);
        Assert.Equal("class A\n{\n    void M()\n    {\n|\n    }\n}", t.Text);

        // Recuo fora do passo (6 espaços): volta até o nível de 4.
        using var odd = new Typist("x\n      |y");
        odd.Press(Key.Back);
        Assert.Equal("x\n    |y", odd.Text);

        // Depois do texto, um caractere por vez; no começo da linha, junta com a de cima.
        using var word = new Typist("    vida|\nx");
        word.Press(Key.Back);
        Assert.Equal("    vid|\nx", word.Text);
        using var join = new Typist("um\n|dois");
        join.Press(Key.Back);
        Assert.Equal("um|dois", join.Text);
    });

    [Fact]
    public void Desfazer_volta_uma_palavra_e_nao_uma_letra() => Run(() =>
    {
        using var t = new Typist(Method);
        t.Type("int vida = 10;");
        t.Editor.Undo();
        Assert.Equal("class A\n{\n    void M()\n    {\n        int vida = |\n    }\n}", t.Text);
        t.Editor.Undo();
        Assert.Equal("class A\n{\n    void M()\n    {\n        int vida |\n    }\n}", t.Text);
        t.Editor.Redo();
        t.Editor.Redo();
        Assert.Contains("int vida = 10;", t.Text);

        // Apagar seguido também é um passo só.
        t.Press(Key.Back, 3);
        Assert.Contains("int vida = |\n", t.Text);
        t.Editor.Undo();
        Assert.Contains("int vida = 10;\n", t.Editor.Document.Text.Replace("\r\n", "\n"));

        // Mover o cursor começa um passo novo, mesmo no meio de uma palavra.
        t.Editor.CaretOffset = t.Editor.Document.Text.IndexOf("vida") + 4;
        t.Type("s");
        t.Editor.CaretOffset = t.Editor.Document.Text.IndexOf("10;") + 2;
        t.Type("0");
        t.Editor.Undo();
        Assert.Contains("int vidas = 10;", t.Editor.Document.Text);
    });

    [Fact]
    public void Enter_numa_linha_vazia_mantem_o_recuo_e_nao_deixa_espacos() => Run(() =>
    {
        using var t = new Typist(Method);
        t.Press(Key.Back);             // volta para o recuo do método (4)
        t.Press(Key.Enter);
        Assert.Equal("class A\n{\n    void M()\n    {\n\n    |\n    }\n}", t.Text);

        // Depois de um "{", um nível a mais, como antes.
        using var brace = new Typist("if (x)\n{|");
        brace.Press(Key.Enter);
        Assert.Equal("if (x)\n{\n    |", brace.Text);
    });

    [Fact]
    public void Snippet_Tab_avanca_os_campos_e_depois_vai_para_o_corpo() => Run(() =>
    {
        using var t = new Typist(Method);
        t.Type("if");
        t.Press(Key.Tab);
        Assert.Equal("condition", t.Selected);
        t.Type("vida > 0");
        t.Press(Key.Tab);              // último campo: vai para dentro do if
        Assert.Equal("class A\n{\n    void M()\n    {\n        if (vida > 0)\n        {\n            |\n        }\n    }\n}", t.Text);
        Assert.False(t.Editor.InSnippet);

        t.Press(Key.Tab);              // agora é Tab normal (antes voltava a selecionar a condição)
        Assert.Contains("            " + "    |\n", t.Text);
        Assert.Equal("", t.Selected);
    });

    [Fact]
    public void Snippet_termina_quando_o_cursor_sai_e_Enter_volta_ao_normal() => Run(() =>
    {
        using var t = new Typist(Method);
        t.Type("for");
        t.Press(Key.Tab);
        Assert.Equal("i", t.Selected);
        Assert.True(t.Editor.InSnippet);
        t.Press(Key.Enter);            // Enter num campo: conclui e vai para o corpo
        Assert.False(t.Editor.InSnippet);
        Assert.Equal("", t.Selected);
        Assert.Contains("{\n            |\n        }", t.Text);

        // Outro snippet; o cursor sai dele (clique em outro lugar): Tab e Enter fazem o de sempre.
        using var other = new Typist(Method);
        other.Type("while");
        other.Press(Key.Tab);
        Assert.True(other.Editor.InSnippet);
        other.Editor.TextArea.ClearSelection();
        other.Editor.CaretOffset = other.Editor.Document.Text.IndexOf("void M()") + 8;
        Assert.False(other.Editor.InSnippet);
        other.Press(Key.Enter);
        Assert.Contains("void M()\n    |\n", other.Text);
    });

    [Fact]
    public void Tab_no_meio_de_uma_palavra_nao_vira_snippet() => Run(() =>
    {
        using var t = new Typist("class A\n{\n    void M()\n    {\n        for|each\n    }\n}");
        t.Press(Key.Tab);
        Assert.False(t.Editor.InSnippet);
        Assert.Contains("for |each", t.Text);   // Tab normal: espaços até o próximo nível
        Assert.DoesNotContain("condition", t.Text);
        Assert.DoesNotContain("int i", t.Text);
    });

    [Fact]
    public void Tab_com_varias_linhas_recua_so_as_que_tem_texto() => Run(() =>
    {
        using var t = new Typist("a();\n\nb();\n");
        t.Editor.Select(0, t.Editor.Document.GetLineByNumber(3).EndOffset);
        t.Press(Key.Tab);
        Assert.Equal("    a();\n\n    b();\n", t.Editor.Document.Text.Replace("\r\n", "\n"));
        // A seleção continua cobrindo as linhas, para recuar de novo.
        Assert.Equal("    a();\r\n\r\n    b();", t.Selected);
    });

    [Fact]
    public void Parenteses_e_aspas_envolvem_a_selecao() => Run(() =>
    {
        using var t = new Typist(Method.Replace("|", "x = vida + 1;"));
        t.Select("vida + 1");
        t.Type("(");
        Assert.Contains("x = ([vida + 1|]);", t.Text);
        t.Select("x");
        t.Type("\"");
        Assert.Contains("\"[x|]\" = (vida + 1);", t.Text);
        t.Editor.Undo();
        Assert.Contains("x = (vida + 1);", t.Editor.Document.Text);
    });

    [Fact]
    public void Mover_e_duplicar_linhas_mantem_a_selecao_nas_mesmas_colunas() => Run(() =>
    {
        using var t = new Typist("um\ndois tres\nquatro");
        t.Select("tres");
        t.Editor.MoveLines(-1);
        Assert.Equal("dois [tres|]\num\nquatro", t.Text);
        t.Editor.MoveLines(1);
        t.Editor.MoveLines(1);
        Assert.Equal("um\nquatro\ndois [tres|]", t.Text);

        t.Select("tres");
        t.Editor.DuplicateLines();       // seleção dentro de uma linha: duplica o trecho (como antes)
        Assert.Equal("um\nquatro\ndois trestres", t.Editor.Document.Text.Replace("\r\n", "\n"));

        using var lines = new Typist("um\ndois");
        lines.Editor.Select(1, 6);       // "m⏎doi"
        lines.Editor.DuplicateLines();
        Assert.Equal("um\ndois\nu[m\ndoi|]s", lines.Text);
    });
}
