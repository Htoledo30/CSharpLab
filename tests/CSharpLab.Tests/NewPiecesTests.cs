using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using CSharpLab.GameEngine;
using Color = CSharpLab.GameEngine.Color;

namespace CSharpLab.Tests;

/// <summary>
/// As peças de escolher e de sorte: Escolha (Choice), Contador (Counter), Roleta (Wheel) e Grade (Grid). O arquivo da
/// tela, o código (game.Find) e o desenho na janela do jogo.
/// </summary>
public sealed class NewPiecesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "csharplab-pecas-novas", Guid.NewGuid().ToString("N")[..8]);

    public NewPiecesTests() => Directory.CreateDirectory(Path.Combine(_dir, "Screens"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private const string Arcade = """
        {
          "format": 1,
          "pieces": [
            { "type": "Choice", "name": "Difficulty", "x": 40, "y": 30, "width": 480, "height": 52, "options": ["Fácil", "Normal", "Difícil"], "selected": "Normal", "color": "Purple" },
            { "type": "Counter", "name": "Amount", "x": 40, "y": 100, "width": 300, "height": 52, "text": "Quantidade", "value": 1, "min": 1, "max": 5 },
            { "type": "Counter", "name": "Bet", "x": 40, "y": 170, "width": 220, "height": 52, "value": 10, "min": 0, "max": 100, "step": 10 },
            { "type": "Wheel", "name": "Wheel", "x": 600, "y": 30, "width": 320, "height": 320, "options": ["10 moedas", "Nada", "50 moedas", "Poção", "Nada", "100 moedas"] },
            { "type": "Grid", "name": "Board", "x": 40, "y": 240, "width": 270, "height": 270, "columns": 3, "rows": 3, "corner": "Square" },
            { "type": "Button", "name": "SpinButton", "x": 660, "y": 400, "width": 200, "height": 52, "text": "Girar" }
          ]
        }
        """;

    private sealed class FakeView : IGameView
    {
        public Screen? Last { get; private set; }
        public List<double> Pauses { get; } = [];
        public void Show(Screen screen) => Last = screen;
        public void Pause(double seconds) => Pauses.Add(seconds);
    }

    private (Game Game, FakeView View) Started(Action<Game> scene)
    {
        File.WriteAllText(Path.Combine(_dir, "Screens", "Arcade.json"), Arcade);
        var game = new Game("Teste") { Screens = new ScreenLibrary { Roots = [_dir] } };
        game.Scene("Arcade", () => scene(game));
        var view = new FakeView();
        game.Begin("Arcade", view);
        return (game, view);
    }

    // ------------------------------------------------------------------ arquivo

    [Fact]
    public void Arquivo_guarda_as_pecas_novas()
    {
        var result = ScreenFile.Parse(Arcade);
        Assert.True(result.Success, result.Error);
        Assert.Empty(result.Warnings);
        var layout = result.Layout!;
        var choice = layout.Find("Difficulty")!;
        Assert.Equal(["Fácil", "Normal", "Difícil"], choice.OptionList);
        Assert.Equal("Normal", choice.SelectedOption);
        Assert.Equal(3, choice.ChoiceColumns);   // peça larga: todas numa linha
        var bet = layout.Find("Bet")!;
        Assert.Equal((10, 0, 100, 10), (bet.CounterValue, bet.CounterMin, bet.CounterMax, bet.CounterStep));
        Assert.Equal((3, 3), (layout.Find("Board")!.GridColumns, layout.Find("Board")!.GridRows));

        var text = ScreenFile.Serialize(layout);
        Assert.Equal(text, ScreenFile.Serialize(ScreenFile.Parse(text).Layout!));
        Assert.Contains("\"options\": [\"Fácil\", \"Normal\", \"Difícil\"], \"selected\": \"Normal\"", text);
        Assert.Contains("\"value\": 10, \"min\": 0, \"max\": 100, \"step\": 10", text);
        Assert.Contains("\"columns\": 3, \"rows\": 3", text);

        // Cada peça nova nasce pronta para usar e volta igual do arquivo.
        foreach (var type in new[] { PieceType.Choice, PieceType.Counter, PieceType.Wheel, PieceType.Grid })
        {
            var piece = Piece.CreateDefault(type, type + "1", 10, 10);
            var json = ScreenFile.Serialize(new ScreenLayout { Pieces = [piece] });
            var back = ScreenFile.Parse(json);
            Assert.True(back.Success, back.Error);
            Assert.Equal(json, ScreenFile.Serialize(back.Layout!));
        }
    }

    [Theory]
    [InlineData("""{ "type": "Choice", "name": "A", "options": "Sim, Não" }""", "lista de textos entre colchetes")]
    [InlineData("""{ "type": "Choice", "name": "A", "options": ["Sim", 2] }""", "precisa ser um texto entre aspas")]
    [InlineData("""{ "type": "Grid", "name": "A", "columns": 30 }""", "vai de 1 a 20")]
    [InlineData("""{ "type": "Counter", "name": "A", "min": 10, "max": 5 }""", "maior ou igual ao \"min\"")]
    [InlineData("""{ "type": "Counter", "name": "A", "step": 0 }""", "maior que 0")]
    public void Erros_das_pecas_novas_em_portugues(string piece, string expected)
    {
        var result = ScreenFile.Parse("{ \"pieces\": [ " + piece + " ] }");
        Assert.False(result.Success);
        Assert.Contains(expected, result.Error);
    }

    [Fact]
    public void Escolhida_que_nao_esta_nas_opcoes_vira_aviso()
    {
        var result = ScreenFile.Parse("""{ "pieces": [ { "type": "Choice", "name": "A", "options": ["Sim", "Não"], "selected": "Talvez" } ] }""");
        Assert.True(result.Success);
        Assert.Contains("\"Talvez\" não está nas opções", Assert.Single(result.Warnings));
        Assert.Null(result.Layout!.Find("A")!.SelectedOption);
    }

    // ------------------------------------------------------------------ código

    [Fact]
    public void Escolha_guarda_a_opcao_e_roda_o_OnChoose()
    {
        var chosen = new List<string>();
        var (game, view) = Started(g => g.Find("Difficulty").OnChoose(choice => chosen.Add(choice)));
        var piece = view.Last!.Designed!.Layout.Find("Difficulty")!;

        game.ChoosePiece(piece, "Difícil");
        Assert.Equal(["Difícil"], chosen);
        Assert.Equal("Difícil", game.Find("Difficulty").Selected);

        game.Find("Difficulty").Selected = "fácil";            // sem diferença de maiúsculas
        Assert.Equal("Fácil", game.Find("Difficulty").Selected);
        game.Find("Difficulty").Selected = "";
        Assert.Equal("", game.Find("Difficulty").Selected);
        var error = Assert.Throws<GameException>(() => game.Find("Difficulty").Selected = "Médio");
        Assert.Contains("\"Fácil\", \"Normal\", \"Difícil\"", error.Message);

        game.Find("Difficulty").Options = ["Guerreiro", "Mago"];
        Assert.Equal(["Guerreiro", "Mago"], game.Find("Difficulty").Options);
        Assert.Contains("só a Escolha tem OnChoose", Assert.Throws<GameException>(() => game.Find("Wheel").OnChoose(_ => { })).Message);
    }

    [Fact]
    public void Contador_anda_de_passo_em_passo_sem_passar_dos_limites()
    {
        var changes = new List<int>();
        var (game, view) = Started(g => g.Find("Bet").OnChange(value => changes.Add(value)));
        var bet = view.Last!.Designed!.Layout.Find("Bet")!;
        var amount = view.Last.Designed.Layout.Find("Amount")!;

        game.StepPiece(bet, +1);
        game.StepPiece(bet, +1);
        game.StepPiece(bet, -1);
        Assert.Equal([20, 30, 20], changes);
        Assert.Equal(20, game.Find("Bet").Value);

        for (int i = 0; i < 10; i++) game.StepPiece(amount, +1);
        Assert.Equal(5, game.Find("Amount").Value);            // parou no máximo
        game.StepPiece(amount, -1);
        Assert.Equal(4, game.Find("Amount").Value);            // sem OnChange, o número muda do mesmo jeito

        game.Find("Amount").Value = 50;
        Assert.Equal(5, game.Find("Amount").Value);            // pelo código também fica entre Min e Max
        game.Find("Amount").Max = 10;
        game.Find("Amount").Min = 2;
        game.Find("Bet").Step = 25;
        Assert.Equal((2, 10, 25), (game.Find("Amount").Min, game.Find("Amount").Max, game.Find("Bet").Step));
        Assert.Contains("maior ou igual ao Min", Assert.Throws<GameException>(() => game.Find("Amount").Max = 1).Message);
    }

    [Fact]
    public void Grade_recebe_cliques_e_guarda_as_casas()
    {
        var (game, view) = Started(g => g.Find("Board").OnCellClick((x, y) =>
        {
            var cell = g.Find("Board").Cell(x, y);
            if (cell.IsEmpty)
            {
                cell.Text = "X";
                cell.Color = Color.Blue;
            }
        }));
        var board = view.Last!.Designed!.Layout.Find("Board")!;

        game.ClickCell(board, 2, 0);
        var cell = game.Find("Board").Cell(2, 0);
        Assert.Equal(("X", (Color?)Color.Blue, false), (cell.Text, cell.Color, cell.IsEmpty));
        Assert.True(game.Find("Board").Cell(0, 0).IsEmpty);
        Assert.Equal("Casa (2, 0): X", cell.ToString());

        var error = Assert.Throws<GameException>(() => game.Find("Board").Cell(3, 0));
        Assert.Contains("3 colunas (x de 0 a 2)", error.Message);

        game.Find("Board").Clear();
        Assert.True(game.Find("Board").Cell(2, 0).IsEmpty);
        game.Find("Board").Columns = 4;
        game.Find("Board").Cell(3, 0).Text = "O";
        Assert.Contains("vai de 1 a 20", Assert.Throws<GameException>(() => game.Find("Board").Rows = 0).Message);
    }

    [Fact]
    public void Grade_sem_OnCellClick_explica_o_que_falta()
    {
        var (game, view) = Started(_ => { });
        game.ClickCell(view.Last!.Designed!.Layout.Find("Board")!, 0, 0);
        Assert.Contains(view.Last!.Messages, m => m.Text.Contains("OnCellClick((x, y) =>"));
    }

    [Fact]
    public void Roleta_gira_dentro_do_clique_e_devolve_a_fatia()
    {
        var prizes = new List<string>();
        var (game, view) = Started(g => g.Find("SpinButton").OnClick(() => prizes.Add(g.Find("Wheel").Spin())));

        game.ClickPiece(view.Last!.Designed!.Layout.Find("SpinButton")!);
        var prize = Assert.Single(prizes);
        Assert.Contains(prize, game.Find("Wheel").Options);
        Assert.Equal(prize, game.Find("Wheel").Selected);       // a seta fica na fatia sorteada
        Assert.Equal([3.4], view.Pauses);                        // o código espera a roleta parar

        // Fora de um clique, explica onde o Spin funciona.
        var error = Assert.Throws<GameException>(() => game.Find("Wheel").Spin());
        Assert.Contains("dentro de um clique", error.Message);
    }

    // ------------------------------------------------------------------ desenho

    /// <summary>As quatro peças na janela do jogo. Foto em %TEMP%\csharplab-pecas-novas.png.</summary>
    [Fact]
    public void Pecas_novas_na_janela_respondem_ao_jogador() => OnSta(() =>
    {
        File.WriteAllText(Path.Combine(_dir, "Screens", "Arcade.json"), Arcade);
        var game = new Game("Teste") { Screens = new ScreenLibrary { Roots = [_dir] } };
        string? chosen = null;
        game.Scene("Arcade", () =>
        {
            game.Find("Difficulty").OnChoose(choice => chosen = choice);
            game.Find("Board").OnCellClick((x, y) => game.Find("Board").Cell(x, y).Text = x == y ? "X" : "O");
        });
        var window = new GameWindow(game) { Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        try
        {
            game.Begin("Arcade", window);
            Pump(TimeSpan.FromMilliseconds(100));
            var stage = Stage(window);

            // Escolha: clicar no botão "Difícil" escolhe ele.
            var hard = Descendants(stage).OfType<Button>().First(b => Descendants(b).OfType<TextBlock>().Any(t => t.Text == "Difícil"));
            hard.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal("Difícil", chosen);

            // Contador: o + sobe, e no máximo ele fica apagado.
            Button Plus() => Descendants(stage).OfType<Button>().First(b => b.Content is TextBlock { Text: "+" } &&
                                                                             Descendants(VisualTreeHelper.GetParent(b)).OfType<TextBlock>().Any(t => t.Text == "Quantidade"));
            for (int i = 0; i < 6; i++)
            {
                Plus().RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Pump(TimeSpan.FromMilliseconds(30));   // cada clique desenha a tela de novo
            }
            Assert.Equal(5, game.Find("Amount").Value);
            Pump(TimeSpan.FromMilliseconds(50));
            Assert.False(Plus().IsEnabled);

            // Grade: as casas recebem o texto.
            var board = game.Find("Board").Piece;
            game.ClickCell(board, 0, 0);
            game.ClickCell(board, 1, 0);
            Pump(TimeSpan.FromMilliseconds(80));
            Assert.Contains(Descendants(stage).OfType<TextBlock>(), t => t.Text == "X");
            Assert.Contains(Descendants(stage).OfType<TextBlock>(), t => t.Text == "O");

            // Roleta: o giro anima e para na fatia escolhida.
            var wheel = (FrameworkElement)stage.Children.OfType<FrameworkElement>().First(e => e.Tag is RotateTransform);
            var rotate = (RotateTransform)wheel.Tag;
            double target = rotate.Angle;
            EffectPlayer.Spin(wheel, target);
            Pump(TimeSpan.FromMilliseconds(400));
            Assert.NotEqual(target, rotate.Angle, 1);              // girando
            Snapshot(stage, "csharplab-pecas-novas.png");
            Pump(EffectPlayer.SpinTime + TimeSpan.FromMilliseconds(300));
            Assert.Equal(target, rotate.Angle, 1);                 // parou onde devia
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Angulo_da_roleta_poe_a_fatia_embaixo_da_seta()
    {
        var wheel = Piece.CreateDefault(PieceType.Wheel, "Wheel", 0, 0);
        wheel.Options = ["A", "B", "C", "D"];
        Assert.Equal(-45, ScreenRenderer.WheelAngle(wheel, "A"));    // a primeira fatia vai de 0° a 90°: o meio dela sobe para a seta
        Assert.Equal(-135, ScreenRenderer.WheelAngle(wheel, "b"));
        Assert.Equal(-45, ScreenRenderer.WheelAngle(wheel, null));
    }

    private static Canvas Stage(GameWindow window) =>
        Descendants((DependencyObject)window.Content).OfType<Canvas>().First(c => c.Width == GameWindow.StageWidth && c.Children.Count > 0);

    private static void Snapshot(Canvas stage, string fileName)
    {
        var root = (FrameworkElement)VisualTreeHelper.GetParent(stage);
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)GameWindow.StageWidth, (int)GameWindow.StageHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(Path.GetTempPath(), fileName));
        encoder.Save(file);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var deeper in Descendants(child)) yield return deeper;
        }
    }

    private static void Pump(TimeSpan time)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = time };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void OnSta(Action test)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { test(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw new Exception(failure.Message, failure);
    }
}
