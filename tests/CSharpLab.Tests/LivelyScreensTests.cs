using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CSharpLab.GameEngine;
using Color = CSharpLab.GameEngine.Color;

namespace CSharpLab.Tests;

/// <summary>
/// Telas com mais vida e mais escolhas: Caixa com degradê e borda própria, fundo da tela com cor, Barra animada,
/// o Pop (texto que salta em cima da peça) e a trava do Estúdio (que o jogo ignora).
/// </summary>
public sealed class LivelyScreensTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "csharplab-vida", Guid.NewGuid().ToString("N")[..8]);

    public LivelyScreensTests() => Directory.CreateDirectory(Path.Combine(_dir, "Screens"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private const string FightScreen = """
        {
          "format": 1,
          "backgroundColor": "#1A1030",
          "backgroundGradient": "Black",
          "pieces": [
            { "type": "Box", "name": "Frame", "x": 16, "y": 384, "width": 904, "height": 116, "color": "Black", "opacity": 70, "borderColor": "Gold", "borderWidth": 3, "gradient": "#3A0D0D", "gradientDirection": "Right", "locked": true },
            { "type": "Bar", "name": "EnemyHealth", "x": 596, "y": 32, "width": 312, "height": 48, "text": "Goblin", "value": 20, "max": 20, "color": "Red", "animate": true },
            { "type": "Bar", "name": "Energy", "x": 32, "y": 430, "width": 233, "height": 44, "text": "AP", "value": 4, "max": 4, "barStyle": "Blocks", "animate": true },
            { "type": "Button", "name": "Attack", "x": 440, "y": 396, "width": 176, "height": 46, "text": "Atacar" }
          ]
        }
        """;

    private sealed class FakeView : IGameView
    {
        public Screen? Last { get; private set; }
        public void Show(Screen screen) => Last = screen;
        public void Pause(double seconds) { }
    }

    // ------------------------------------------------------------------ arquivo

    [Fact]
    public void Chaves_novas_vao_e_voltam_no_arquivo()
    {
        var result = ScreenFile.Parse(FightScreen);
        Assert.True(result.Success, result.Error);
        Assert.Empty(result.Warnings);
        var layout = result.Layout!;
        Assert.Equal((Color.Hex("#1A1030"), Color.Black), (layout.BackgroundColor!.Value, layout.BackgroundGradient!.Value));

        var frame = layout.Find("Frame")!;
        Assert.Equal((Color.Gold, 3, Color.Hex("#3A0D0D"), GradientDirection.Right, true),
            (frame.BorderColor!.Value, frame.BorderWidth!.Value, frame.Gradient!.Value, frame.GradientDirection!.Value, frame.Locked));
        Assert.True(frame.HasBorder);   // escolher a cor da borda já liga a borda
        Assert.True(layout.Find("EnemyHealth")!.Animate);

        var text = ScreenFile.Serialize(layout);
        Assert.Equal(text, ScreenFile.Serialize(ScreenFile.Parse(text).Layout!));
        Assert.Contains("\"backgroundColor\": \"#1A1030\",", text);
        Assert.Contains("\"backgroundGradient\": \"Black\",", text);
        Assert.Contains("\"borderColor\": \"Gold\", \"borderWidth\": 3, \"gradient\": \"#3A0D0D\", \"gradientDirection\": \"Right\"", text);
        Assert.Contains("\"locked\": true }", text);
        Assert.Contains("\"value\": 20, \"max\": 20, \"animate\": true }", text);

        // O que é o normal não vai para o arquivo: degradê para baixo, borda fininha, sem trava.
        frame.GradientDirection = GradientDirection.Down;
        frame.BorderWidth = 1;
        frame.Locked = false;
        var plain = ScreenFile.Serialize(layout);
        Assert.DoesNotContain("gradientDirection", plain);
        Assert.DoesNotContain("borderWidth", plain);
        Assert.DoesNotContain("locked", plain);
    }

    [Theory]
    [InlineData("""{ "type": "Box", "name": "A", "borderWidth": 30 }""", "vai de 1 a 20")]
    [InlineData("""{ "type": "Box", "name": "A", "gradientDirection": "Up" }""", "Down, Right ou Diagonal")]
    [InlineData("""{ "type": "Box", "name": "A", "gradient": "Roxo" }""", "\"gradient\" precisa ser uma destas cores")]
    [InlineData("""{ "type": "Bar", "name": "A", "animate": "sim" }""", "true ou false")]
    public void Erros_das_chaves_novas_em_portugues(string piece, string expected)
    {
        var result = ScreenFile.Parse("{ \"pieces\": [ " + piece + " ] }");
        Assert.False(result.Success);
        Assert.Contains(expected, result.Error);
    }

    [Fact]
    public void Cor_do_fundo_errada_explica_e_mostra_a_linha()
    {
        var result = ScreenFile.Parse("{\n  \"backgroundColor\": \"Azulzinho\",\n  \"pieces\": []\n}");
        Assert.False(result.Success);
        Assert.Contains("backgroundColor", result.Error);
        Assert.Equal(2, result.ErrorLine);
    }

    // ------------------------------------------------------------------ nome sugerido e barra baixa

    [Theory]
    [InlineData("SUAS AÇÕES", "YourActions")]
    [InlineData("Ataque Pesado", "HeavyAttack")]
    [InlineData("Ataque", "Attack")]
    [InlineData("PASSAR TURNO", "PassTurn")]
    [InlineData("Poção de vida", "HealthPotion")]
    [InlineData("Vida do Goblin", "GoblinHealth")]
    [InlineData("Ouro: 0", "Gold")]
    [InlineData("Inimigos", "Enemies")]
    [InlineData("Explorar floresta", "ExploreForest")]
    [InlineData("Vida máxima", "MaxHealth")]
    public void Nome_sugerido_pelo_texto(string text, string expected) => Assert.Equal(expected, Core.Language.PieceNames.Suggest(text));

    [Theory]
    [InlineData("Você achou 5 moedas!")]     // palavra que a lista não conhece: melhor não sugerir
    [InlineData("💰 15")]
    [InlineData("Seu")]
    [InlineData("")]
    [InlineData("Ataque de espada com fogo e gelo")]
    public void Sem_sugestao_quando_nao_tem_certeza(string text) => Assert.Null(Core.Language.PieceNames.Suggest(text));

    [Fact]
    public void Barra_baixa_demais_avisa_que_some() => OnSta(() =>
    {
        // A barra de AP do combate: 26 de altura com o nome em cima não sobra lugar para a barra.
        var bar = new Piece { Type = PieceType.Bar, Name = "Ap", Width = 233, Height = 26, Text = "AP", Value = 2, Max = 2 };
        var fit = ScreenRenderer.Overflow(bar);
        Assert.NotNull(fit);
        Assert.InRange(fit!.Height, 28, 40);
        bar.Height = fit.Height;
        Assert.Null(ScreenRenderer.Overflow(bar));   // com a altura sugerida, aparece
        bar.Height = 26;
        bar.BarText = BarText.Inside;                // com o nome dentro, a barra ocupa a peça toda
        Assert.Null(ScreenRenderer.Overflow(bar));
    });

    // ------------------------------------------------------------------ código

    private (Game Game, FakeView View) Started(Action<Game> scene)
    {
        File.WriteAllText(Path.Combine(_dir, "Screens", "Fight.json"), FightScreen);
        var game = new Game("Teste") { Screens = new ScreenLibrary { Roots = [_dir] } };
        game.Scene("Fight", () => scene(game));
        var view = new FakeView();
        game.Begin("Fight", view);
        return (game, view);
    }

    [Fact]
    public void Pop_vai_para_a_tela_com_texto_e_cor()
    {
        int enemy = 20;
        var (game, view) = Started(g =>
        {
            g.Find("EnemyHealth").Value = enemy;
            g.Find("Attack").OnClick(() =>
            {
                enemy -= 7;
                g.Find("EnemyHealth").Pop("-7", Color.Red);
                g.Find("EnemyHealth").Pop("Crítico!");
            });
        });
        Assert.Empty(view.Last!.Effects);

        game.ClickPiece(view.Last.Designed!.Layout.Find("Attack")!);
        Assert.Equal(13, view.Last!.Designed!.Find("EnemyHealth")!.Value);
        Assert.Equal(
            [(Effect.Pop, "-7", (Color?)Color.Red), (Effect.Pop, "Crítico!", null)],
            view.Last.Effects.Select(e => (e.Effect, e.Text, e.Color)));
        Assert.All(view.Last.Effects, e => Assert.Equal("EnemyHealth", e.Piece.Name));

        var error = Assert.Throws<GameException>(() => game.Find("Attack").Pop("  "));
        Assert.Contains("precisa de um texto", error.Message);
    }

    [Fact]
    public void Propriedades_novas_pelo_codigo_e_erros_claros()
    {
        var (game, _) = Started(_ => { });
        var frame = game.Find("Frame");
        Assert.Equal(Color.Hex("#3A0D0D"), frame.Gradient);
        frame.Gradient = null;
        frame.GradientDirection = GradientDirection.Diagonal;
        frame.BorderWidth = 5;
        Assert.Equal((null, GradientDirection.Diagonal, 5), (frame.Gradient, frame.GradientDirection, frame.BorderWidth));
        Assert.Contains("vai de 1 (fininha) a 20", Assert.Throws<GameException>(() => frame.BorderWidth = 0).Message);

        var bar = game.Find("EnemyHealth");
        Assert.True(bar.Animate);
        bar.Animate = false;
        Assert.False(bar.Animate);

        Assert.Contains("só barras têm Animate", Assert.Throws<GameException>(() => frame.Animate = true).Message);
        Assert.Contains("só a Caixa tem Gradient", Assert.Throws<GameException>(() => bar.Gradient = Color.Red).Message);
        Assert.Contains("só a Caixa tem BorderColor", Assert.Throws<GameException>(() => game.Find("Attack").BorderColor = Color.Red).Message);

        // Uma caixa sem borda ganha a borda ao escolher a cor dela.
        frame.Border = false;
        frame.BorderColor = Color.Cyan;
        Assert.True(frame.Border);    // BorderColor segue o contrato: escolher a cor liga a borda
        frame.Border = false;         // desligar depois de escolher a cor continua possível
        Assert.False(frame.Border);
        game.BackgroundColor = Color.Hex("#000000");
        Assert.Equal(Color.Hex("#000000"), game.BackgroundColor);
    }

    // ------------------------------------------------------------------ desenho

    [Fact]
    public void Caixa_com_degrade_e_borda_propria_e_fundo_com_cor() => OnSta(() =>
    {
        var layout = ScreenFile.Parse(FightScreen).Layout!;
        var frame = (Border)ScreenRenderer.Create(layout.Find("Frame")!, new RenderContext { Live = true });
        var brush = Assert.IsType<LinearGradientBrush>(frame.Background);
        Assert.Equal((new Point(0, 0.5), new Point(1, 0.5)), (brush.StartPoint, brush.EndPoint));   // para o lado
        Assert.Equal(178, brush.GradientStops[0].Color.A);   // o preenchimento de 70% vale para as duas cores
        Assert.Equal(new Thickness(3), frame.BorderThickness);
        Assert.Equal(Theme.Rgb(Color.Gold), ((SolidColorBrush)frame.BorderBrush).Color);

        var background = (Panel)ScreenRenderer.Background(layout)!;
        var sky = Assert.IsType<LinearGradientBrush>(background.Background);
        Assert.Equal(Theme.Rgb(Color.Hex("#1A1030")), sky.GradientStops[0].Color);
        Assert.Null(ScreenRenderer.Background(new ScreenLayout()));   // sem nada escolhido, fica o fundo do tema
    });

    /// <summary>
    /// No jogo de verdade: a barra animada perde vida e o rastro mostra o pedaço perdido antes de encolher; o Pop
    /// aparece por cima da tela. Fotos em %TEMP%\csharplab-barra-animada-*.png.
    /// </summary>
    [Fact]
    public void Barra_animada_deixa_rastro_e_pop_aparece_na_janela() => OnSta(() =>
    {
        int enemy = 20, energy = 4;
        File.WriteAllText(Path.Combine(_dir, "Screens", "Fight.json"), FightScreen);
        var game = new Game("Teste") { Screens = new ScreenLibrary { Roots = [_dir] } };
        game.Scene("Fight", () =>
        {
            game.Find("EnemyHealth").Value = enemy;
            game.Find("Energy").Value = energy;
            game.Find("Attack").OnClick(() =>
            {
                enemy -= 10;
                energy -= 2;
                game.Find("EnemyHealth").Pop("-10", Color.Red);
            });
        });
        var window = new GameWindow(game) { Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        try
        {
            game.Begin("Fight", window);
            Pump(TimeSpan.FromMilliseconds(100));
            var canvas = Stage(window);   // o palco é o mesmo a cada desenho (as peças dentro dele mudam)
            var (fill, trail) = BarParts(canvas, "Goblin");
            Assert.Null(trail);   // ao chegar na cena, a barra aparece direto no valor
            double fullWidth = fill.ActualWidth;
            Assert.True(fullWidth > 250, $"Barra cheia: {fullWidth}");

            game.ClickPiece(game.Find("Attack").Piece);
            Pump(TimeSpan.FromMilliseconds(120));
            (fill, trail) = BarParts(canvas, "Goblin");
            Assert.NotNull(trail);
            // A parte cheia já está na metade; o rastro ainda mostra o tamanho antigo (o pedaço perdido).
            Assert.InRange(fill.ActualWidth, fullWidth / 2 - 3, fullWidth / 2 + 3);
            Assert.True(trail!.ActualWidth > fullWidth - 5, $"Rastro: {trail.ActualWidth}");
            Assert.Contains(Descendants(canvas).OfType<TextBlock>(), t => t.Text == "-10");
            Snapshot(canvas, "csharplab-barra-animada-1.png");

            Pump(TimeSpan.FromMilliseconds(1100));
            (_, trail) = BarParts(canvas, "Goblin");
            Assert.InRange(trail!.ActualWidth, fullWidth / 2 - 3, fullWidth / 2 + 3);   // o rastro encolheu até o valor novo
            Assert.DoesNotContain(Descendants(canvas).OfType<TextBlock>(), t => t.Text == "-10");   // o Pop sumiu
            Snapshot(canvas, "csharplab-barra-animada-2.png");
        }
        finally { window.Close(); }
    });

    private static Canvas Stage(GameWindow window) =>
        Descendants((DependencyObject)window.Content).OfType<Canvas>().First(c => c.Width == GameWindow.StageWidth && c.Children.Count > 0);

    /// <summary>A parte cheia e o rastro (se tiver) da barra com este nome.</summary>
    private static (Border Fill, Border? Trail) BarParts(Canvas stage, string label)
    {
        var bar = stage.Children.OfType<FrameworkElement>().First(e => Descendants(e).OfType<TextBlock>().Any(t => t.Text == label));
        // A parte cheia e o rastro ficam juntos dentro da barra, encostados na esquerda (o rastro atrás).
        var content = Descendants(bar).OfType<Grid>().First(g => g.Children.OfType<Border>().Count(b => b.HorizontalAlignment == HorizontalAlignment.Left) >= 1);
        var parts = content.Children.OfType<Border>().Where(b => b.HorizontalAlignment == HorizontalAlignment.Left && b.VerticalAlignment != VerticalAlignment.Top).ToList();
        return parts.Count >= 2 ? (parts[1], parts[0]) : (parts[0], null);
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

    /// <summary>Foto do palco do jogo (com o fundo), sem tirar ele da janela: as animações continuam depois.</summary>
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

    /// <summary>Deixa a janela trabalhar (desenhar, animar) por um tempo.</summary>
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
