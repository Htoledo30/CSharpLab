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

        /// <summary>Com os serviços de C# carregados (formatação automática, sugestões).</summary>
        public static async Task<Typist> WithRoslyn(string text, double width = 800)
        {
            var vm = new MainViewModel(new AppSettings()) { Dialogs = new FakeDialogs(), Terminal = new FakeTerminal() };
            await vm.InitializeAsync();
            var typist = new Typist(text, vm, width);
            await Ui.WaitUntil(() => typist.Editor.CanFormat, 60_000, "formatação automática pronta");
            return typist;
        }

        public Typist(string text, MainViewModel? vm = null, double width = 800)
        {
            _vm = vm ?? new MainViewModel(new AppSettings()) { Dialogs = new FakeDialogs(), Terminal = new FakeTerminal() };
            int caret = text.IndexOf('|');
            var doc = _vm.NewDraft(text.Replace("|", "").Replace("\n", "\r\n"));
            Editor = new CodeEditor(doc, _vm);
            _window = new Window
            {
                Content = Editor, Width = width, Height = 600, Left = -20000, Top = -20000,
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
            // O WPF lê Shift/Ctrl/Alt do teclado de verdade: alguém segurando uma delas muda o que a tecla faz.
            Assert.True(Keyboard.Modifiers == ModifierKeys.None,
                $"{Keyboard.Modifiers} estava apertado no teclado durante o teste; rode de novo sem segurar teclas.");
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

        /// <summary>Cola como o Ctrl+V do AvalonEdit (aviso de "colando" e depois o texto), sem usar a área de transferência.</summary>
        public async Task Paste(string text)
        {
            text = text.Replace("\n", "\r\n");
            Editor.TextArea.RaiseEvent(new DataObjectPastingEventArgs(new DataObject(DataFormats.UnicodeText, text), false, DataFormats.UnicodeText));
            Editor.TextArea.Selection.ReplaceSelectionWithText(text);
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
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

    // ================================================================ formatação automática

    [Fact]
    public void Ponto_e_virgula_formata_a_instrucao_e_desfazer_volta_como_foi_digitado() => Ui.Run(async () =>
    {
        using var t = await Typist.WithRoslyn("class A\n{\n    void M()\n    {\n  int  vida=10|\n    }\n}");
        t.Type(";");
        Assert.Equal("class A\n{\n    void M()\n    {\n        int vida = 10;|\n    }\n}", t.Text);
        t.Editor.Undo();
        Assert.Equal("class A\n{\n    void M()\n    {\n  int  vida=10;|\n    }\n}", t.Text);

        // ";" do cabeçalho do for não formata nada (a pessoa ainda está no meio dele).
        using var loop = await Typist.WithRoslyn("class A\n{\n    void M()\n    {\n        for(int i=0|\n    }\n}");
        loop.Type(";");
        Assert.Contains("for(int i=0;|", loop.Text);
    });

    [Fact]
    public void Fechar_chave_formata_o_bloco_inteiro() => Ui.Run(async () =>
    {
        using var t = await Typist.WithRoslyn("class A\n{\n    void M(bool x)\n    {\n        if(x)\n        {\n  a();\n         b();\n|\n    }\n    void a() { }\n    void b() { }\n}");
        t.Type("}");
        Assert.Equal("class A\n{\n    void M(bool x)\n    {\n        if (x)\n        {\n            a();\n            b();\n        }|\n    }\n    void a() { }\n    void b() { }\n}", t.Text);
    });

    [Fact]
    public void Colar_varias_linhas_ajusta_o_recuo_ao_lugar_onde_entrou() => Ui.Run(async () =>
    {
        using var t = await Typist.WithRoslyn(Method);
        await t.Paste("for (int i=0;i<3;i++)\n{\nConsole.WriteLine(i);\n}");
        Assert.Equal("class A\n{\n    void M()\n    {\n        for (int i = 0; i < 3; i++)\n        {\n            Console.WriteLine(i);\n        }|\n    }\n}", t.Text);
        // Desfazer tira só o ajuste; de novo, tira o que foi colado.
        t.Editor.Undo();
        Assert.Contains("        for (int i=0;i<3;i++)\n{\nConsole.WriteLine(i);\n}", t.Text);
        t.Editor.Undo();
        Assert.Equal(Method, t.Text);

        // Uma linha só entra como está.
        await t.Paste("x=1");
        Assert.Contains("        x=1|", t.Text);
    });

    // ================================================================ sugestões dentro de $"…{ }…"

    [Fact]
    public void Sugestoes_aparecem_dentro_das_chaves_de_uma_string_interpolada() => Ui.Run(async () =>
    {
        const string code = "class Goblin { public string Nome = \"\"; public int Recompensa; }\nclass A\n{\n    void M()\n    {\n        var goblin = new Goblin();\n        var s = |;\n    }\n}";
        using var t = await Typist.WithRoslyn(code);
        t.Type("$\"Ganhou ");
        t.Type("{");
        // A chave de fechar entra junto: sem ela, a aspa do fim vira o começo de outro texto e nada mais é código.
        Assert.Contains("$\"Ganhou {|}\"", t.Text);
        t.Type("gob");
        await Ui.WaitUntil(() => t.Editor.CompletionController.IsOpen, 20_000, "sugestões ao digitar o nome");
        t.Editor.CompletionController.Close();
        t.Type("lin.");
        await Ui.WaitUntil(() => t.Editor.CompletionController.IsOpen, 20_000, "sugestões depois do ponto");
        t.Editor.CompletionController.Close();
        t.Type("Recompensa}");
        Assert.Contains("$\"Ganhou {goblin.Recompensa}|\"", t.Text);
    });

    [Fact]
    public void Sugestoes_nao_somem_quando_a_tela_anda_para_o_lado() => Ui.Run(async () =>
    {
        // Linha comprida numa janela estreita: cada letra no fim da tela faz o texto rolar para o lado.
        // Antes, a rolagem fechava (e cancelava) as sugestões — e parecia que elas não existiam ali.
        const string code = "class Goblin { public string Nome = \"\"; public int Recompensa; }\nclass A\n{\n    void M()\n    {\n        var goblin = new Goblin();\n        var s = $\"Voce recebeu {goblin.Recompensa} de Gold. Por matar o {goblin.|}, parabens pela vitoria\";\n    }\n}";
        using var t = await Typist.WithRoslyn(code, width: 420);
        var view = t.Editor.TextArea.TextView;
        view.EnsureVisualLines();
        var caretX = view.GetVisualPosition(t.Editor.TextArea.Caret.Position, ICSharpCode.AvalonEdit.Rendering.VisualYPosition.LineTop).X;
        t.Editor.ScrollToHorizontalOffset(Math.Max(0, caretX - view.ActualWidth + 4));   // cursor encostado na borda direita
        t.Editor.UpdateLayout();
        double before = view.ScrollOffset.X;
        t.Type("N");
        await Ui.WaitUntil(() => t.Editor.CompletionController.IsOpen, 20_000, "sugestões com a tela rolando");
        Assert.True(view.ScrollOffset.X > before, "o teste precisa que a tela role para o lado");
    });

    [Fact]
    public void Sugestoes_aparecem_ao_editar_chaves_que_ja_existem() => Ui.Run(async () =>
    {
        const string code = "class Goblin { public string Nome = \"\"; public int Recompensa; }\nclass A\n{\n    void M()\n    {\n        var goblin = new Goblin();\n        var s = $\"Por matar o {goblin.|}\";\n    }\n}";
        using var t = await Typist.WithRoslyn(code);
        t.Type("N");
        await Ui.WaitUntil(() => t.Editor.CompletionController.IsOpen, 20_000, "sugestões dentro de chaves fechadas");
        t.Editor.CompletionController.Close();

        // O ponto também abre a lista, mesmo com o texto da string continuando depois das chaves.
        t.Editor.Document.Replace(t.Editor.Document.Text.IndexOf("{goblin.N}"), "{goblin.N}".Length, "{goblin}");
        t.Editor.CaretOffset = t.Editor.Document.Text.IndexOf("{goblin}") + 7;
        t.Type(".");
        await Ui.WaitUntil(() => t.Editor.CompletionController.IsOpen, 20_000, "sugestões depois do ponto em chaves fechadas");
    });

    // ================================================================ estrutura: blocos, ocorrências, linhas

    private const string Game =
        "var game = new Game(\"x\");\nint gold = 0;\ngame.Scene(\"Start\", () =>\n{\n    game.Find(\"Search\").OnClick(() =>\n    {\n        gold += 5;\n    });\n});\ngame.Scene(\"Forest\", () =>\n{\n    gold = 1;\n});\n";

    [Fact]
    public void Blocos_recolhem_e_abrem_quando_o_cursor_entra() => Run(() =>
    {
        using var t = new Typist(Game);
        // Cada bloco de mais de uma linha: as duas cenas e o OnClick.
        Assert.Equal(3, t.Editor.Foldings.Count());

        t.Editor.CaretOffset = t.Editor.Document.Text.IndexOf("game.Scene(\"Start\"");
        t.Editor.FoldAtCaret();
        var start = t.Editor.Foldings.First();
        Assert.True(start.Folded);
        // Recolhido na ponta da linha de cima: "game.Scene("Start", () =>" continua à vista.
        Assert.Equal(t.Editor.Document.Text.IndexOf("() =>") + 5, start.Start);

        // Ir para dentro (F12, busca, erro clicado) abre o bloco.
        t.Editor.CaretOffset = t.Editor.Document.Text.IndexOf("gold += 5");
        Assert.False(t.Editor.Foldings.First().Folded);

        // Recolher tudo: as cenas, não o que está dentro delas.
        t.Editor.CaretOffset = 0;
        t.Editor.FoldAll();
        Assert.Equal([true, false, true], t.Editor.Foldings.Select(f => f.Folded));
        t.Editor.UnfoldAll();
        Assert.All(t.Editor.Foldings, f => Assert.False(f.Folded));
    });

    [Fact]
    public void Recolher_tudo_numa_classe_recolhe_os_metodos_e_nao_a_classe() => Run(() =>
    {
        using var t = new Typist("class A\n{\n    void M()\n    {\n        if (true)\n        {\n        }\n    }\n\n    void N()\n    {\n    }\n}");
        t.Editor.FoldAll();
        // class A, M, if, N (na ordem do texto)
        Assert.Equal([false, true, false, true], t.Editor.Foldings.Select(f => f.Folded));
    });

    [Fact]
    public void Ocorrencias_do_nome_sob_o_cursor_e_ir_para_a_proxima() => Ui.Run(async () =>
    {
        using var t = await Typist.WithRoslyn("class A\n{\n    int vida = 3;\n    void M()\n    {\n        vida--;\n        int x = vida;\n    }\n    void N()\n    {\n        int x = 1;\n    }\n}");
        t.Editor.CaretOffset = t.Editor.Document.Text.IndexOf("vida--") + 2;
        await Ui.WaitUntil(() => t.Editor.Occurrences.Count == 3, 20_000, "as três ocorrências de vida");

        // Outro x, em outro método, não é o mesmo nome.
        t.Editor.CaretOffset = t.Editor.Document.Text.IndexOf("int x = vida") + 4;
        await Task.Delay(800);
        Assert.Empty(t.Editor.Occurrences);   // aparece uma vez só neste método: nada a destacar

        // Ctrl+Shift+↓: vai de vida em vida, selecionando, e volta ao começo.
        t.Editor.CaretOffset = t.Editor.Document.Text.IndexOf("int vida") + 5;
        await Ui.WaitUntil(() => t.Editor.Occurrences.Count == 3, 20_000);
        t.Editor.GoToOccurrence(1);
        Assert.Contains("[vida|]--", t.Text);
        t.Editor.GoToOccurrence(1);
        Assert.Contains("int x = [vida|];", t.Text);
        t.Editor.GoToOccurrence(1);
        Assert.Contains("int [vida|] = 3", t.Text);
        t.Editor.GoToOccurrence(-1);
        Assert.Contains("int x = [vida|];", t.Text);
    });

    [Fact]
    public void Linha_nova_acima_e_abaixo_e_recuar_sem_mexer_no_cursor() => Run(() =>
    {
        using var t = new Typist("if (x)\n{\n    a(|1);\n}");
        t.Editor.InsertLineBelow();
        Assert.Equal("if (x)\n{\n    a(1);\n    |\n}", t.Text);
        t.Type("b();");
        t.Editor.InsertLineAbove();
        Assert.Equal("if (x)\n{\n    a(1);\n    |\n    b();\n}", t.Text);

        using var lines = new Typist("a(|);\nb();");
        lines.Editor.IndentLines();
        Assert.Equal("    a(|);\nb();", lines.Text);
        lines.Editor.OutdentLines();
        lines.Editor.OutdentLines();     // já no começo: nada muda
        Assert.Equal("a(|);\nb();", lines.Text);
    });
}
