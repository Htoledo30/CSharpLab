using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ellipse = System.Windows.Shapes.Ellipse;
using ShapePath = System.Windows.Shapes.Path;
using System.Windows.Threading;
using CSharpLab.GameEngine;
using Color = CSharpLab.GameEngine.Color;

namespace CSharpLab.Tests;

/// <summary>Os ícones prontos (peça Ícone e ícone do Botão) e o Dado: arquivo, código e desenho.</summary>
public sealed class IconsAndDiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "csharplab-icones", Guid.NewGuid().ToString("N")[..8]);

    public IconsAndDiceTests() => Directory.CreateDirectory(Path.Combine(_dir, "Screens"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private const string Fight = """
        {
          "format": 1,
          "pieces": [
            { "type": "Icon", "name": "EnemyIcon", "x": 380, "y": 30, "width": 200, "height": 200, "icon": "Goblin", "color": "Green", "frame": "Circle", "shadow": true },
            { "type": "Icon", "name": "HeroIcon", "x": 60, "y": 60, "width": 140, "height": 140, "icon": "Shield", "color": "Gold", "frame": "Square", "back": "#2A2238" },
            { "type": "Icon", "name": "Heart", "x": 760, "y": 60, "width": 60, "height": 60, "icon": "Heart", "color": "Red" },
            { "type": "Dice", "name": "D20", "x": 420, "y": 260, "width": 120, "height": 120, "sides": 20, "value": 17, "color": "Purple" },
            { "type": "Dice", "name": "D6", "x": 600, "y": 280, "width": 90, "height": 90, "sides": 6, "value": 5, "color": "White" },
            { "type": "Dice", "name": "D4", "x": 720, "y": 280, "width": 90, "height": 90, "sides": 4, "color": "Orange" },
            { "type": "Dice", "name": "D12", "x": 830, "y": 280, "width": 90, "height": 90, "sides": 12, "value": 7, "color": "Blue" },
            { "type": "Dice", "name": "D8", "x": 60, "y": 280, "width": 90, "height": 90, "sides": 8, "value": 3, "color": "Cyan" },
            { "type": "Dice", "name": "D10", "x": 170, "y": 280, "width": 90, "height": 90, "sides": 10, "value": 9, "color": "Pink" },
            { "type": "Dice", "name": "D100", "x": 280, "y": 280, "width": 90, "height": 90, "sides": 100, "value": 42, "color": "Brown" },
            { "type": "Button", "name": "Attack", "x": 300, "y": 430, "width": 180, "height": 56, "text": "Atacar", "icon": "Sword", "color": "Red", "style": "Gradient" },
            { "type": "Button", "name": "Defend", "x": 500, "y": 430, "width": 180, "height": 56, "text": "Defender", "icon": "Shield", "style": "Outline" }
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
        File.WriteAllText(Path.Combine(_dir, "Screens", "Fight.json"), Fight);
        var game = new Game("Teste") { Screens = new ScreenLibrary { Roots = [_dir] } };
        game.Scene("Fight", () => scene(game));
        var view = new FakeView();
        game.Begin("Fight", view);
        return (game, view);
    }

    // ------------------------------------------------------------------ ícones

    [Fact]
    public void Todo_icone_tem_desenho_nome_em_portugues_e_grupo() => OnSta(() =>
    {
        var all = Enum.GetValues<Icon>();
        Assert.Equal(all.Length, IconSet.All.Count);
        Assert.Equal(all.Length, IconSet.All.Select(e => e.Icon).Distinct().Count());
        foreach (var icon in all)
        {
            var entry = IconSet.Of(icon);
            Assert.Equal(icon, entry.Icon);
            Assert.Contains(entry.Group, IconSet.Groups);
            Assert.False(string.IsNullOrWhiteSpace(entry.Portuguese));
            // O desenho existe de verdade: o contorno tem tamanho (um símbolo que a fonte não tem vira nada ou um quadrado vazio).
            var bounds = IconSet.Shape(icon).Bounds;
            Assert.False(bounds.IsEmpty, $"{icon} sem desenho");
            Assert.True(Math.Max(bounds.Width, bounds.Height) > 40 && Math.Min(bounds.Width, bounds.Height) > 8, $"{icon}: {bounds}");   // o "!" é fininho
        }
    });

    [Fact]
    public void Busca_de_icone_acha_em_portugues_sem_acento_e_em_ingles()
    {
        Assert.Contains(IconSet.Search("dragao"), e => e.Icon == Icon.Dragon);
        Assert.Contains(IconSet.Search("Poção"), e => e.Icon == Icon.Potion);
        Assert.Contains(IconSet.Search("sword"), e => e.Icon == Icon.Sword);
        Assert.Empty(IconSet.Search("xyzzy"));
        Assert.Equal(IconSet.All.Count, IconSet.Search("").Count());
    }

    // ------------------------------------------------------------------ arquivo

    [Fact]
    public void Arquivo_guarda_icones_e_dados()
    {
        var result = ScreenFile.Parse(Fight);
        Assert.True(result.Success, result.Error);
        Assert.Empty(result.Warnings);
        var layout = result.Layout!;
        var enemy = layout.Find("EnemyIcon")!;
        Assert.Equal((Icon.Goblin, IconFrame.Circle), (enemy.Icon!.Value, enemy.Frame!.Value));
        Assert.Equal("#2A2238", layout.Find("HeroIcon")!.Back.ToString());
        Assert.Equal(Icon.Sword, layout.Find("Attack")!.Icon);
        Assert.Equal((20, 17), (layout.Find("D20")!.DiceSides, layout.Find("D20")!.DiceValue));
        Assert.Equal(4, layout.Find("D4")!.DiceValue);   // sem valor: o maior

        // Gravar e ler de novo dá a mesma tela.
        var again = ScreenFile.Parse(ScreenFile.Serialize(layout));
        Assert.True(again.Success, again.Error);
        Assert.Equal(ScreenFile.Serialize(layout), ScreenFile.Serialize(again.Layout!));
        Assert.Contains("\"icon\": \"Goblin\"", ScreenFile.Serialize(layout));
        Assert.Contains("\"sides\": 20", ScreenFile.Serialize(layout));

        // Erros que explicam.
        var bad = ScreenFile.Parse("""{ "pieces": [ { "type": "Dice", "name": "D7", "sides": 7 } ] }""");
        Assert.Contains("4, 6, 8, 10, 12, 20 ou 100", bad.Error);
        var noIcon = ScreenFile.Parse("""{ "pieces": [ { "type": "Icon", "name": "X", "icon": "Banana" } ] }""");
        Assert.Contains("\"icon\"", noIcon.Error);
    }

    // ------------------------------------------------------------------ código

    [Fact]
    public void Codigo_troca_o_icone_a_moldura_e_os_lados()
    {
        var (game, _) = Started(g =>
        {
            g.Find("EnemyIcon").Icon = Icon.Dragon;
            g.Find("EnemyIcon").Frame = IconFrame.Square;
            g.Find("Attack").Icon = null;
            g.Find("D6").Sides = 8;
        });
        Assert.Equal(Icon.Dragon, game.Find("EnemyIcon").Icon);
        Assert.Equal(IconFrame.Square, game.Find("EnemyIcon").Frame);
        Assert.Null(game.Find("Attack").Icon);
        Assert.Equal(8, game.Find("D6").Sides);
        Assert.Equal(5, game.Find("D6").Value);
        game.Find("D6").Value = 99;
        Assert.Equal(8, game.Find("D6").Value);              // fica dentro do dado

        Assert.Contains("4, 6, 8, 10, 12, 20 ou 100", Assert.Throws<GameException>(() => game.Find("D6").Sides = 7).Message);
        Assert.Contains("Visible = false", Assert.Throws<GameException>(() => game.Find("EnemyIcon").Icon = null).Message);
        Assert.Contains("só o Dado tem Roll", Assert.Throws<GameException>(() => game.Find("EnemyIcon").Roll()).Message);
        Assert.Contains("só o Ícone e o Botão têm Icon", Assert.Throws<GameException>(() => _ = game.Find("D20").Icon).Message);
    }

    [Fact]
    public void Dado_rola_dentro_do_clique_e_devolve_o_numero()
    {
        var rolls = new List<int>();
        var (game, view) = Started(g => g.Find("Attack").OnClick(() => rolls.Add(g.Find("D20").Roll())));
        for (int i = 0; i < 30; i++) game.ClickPiece(view.Last!.Designed!.Layout.Find("Attack")!);
        Assert.Equal(30, rolls.Count);
        Assert.All(rolls, r => Assert.InRange(r, 1, 20));
        Assert.True(rolls.Distinct().Count() > 3);              // sorteia de verdade
        Assert.Equal(rolls[^1], game.Find("D20").Value);        // o dado fica mostrando o que saiu
        Assert.All(view.Pauses, p => Assert.Equal(1.8, p));     // o código espera o dado parar

        var error = Assert.Throws<GameException>(() => game.Find("D20").Roll());
        Assert.Contains("dentro de um clique", error.Message);
    }

    // ------------------------------------------------------------------ desenho

    /// <summary>Ícones, botões com ícone e todos os dados na janela do jogo. Foto em %TEMP%\csharplab-icones-e-dados.png.</summary>
    [Fact]
    public void Icones_e_dados_na_janela() => OnSta(() =>
    {
        File.WriteAllText(Path.Combine(_dir, "Screens", "Fight.json"), Fight);
        var game = new Game("Teste") { Screens = new ScreenLibrary { Roots = [_dir] } };
        game.Scene("Fight", () => { });
        var window = new GameWindow(game) { Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        try
        {
            game.Begin("Fight", window);
            Pump(TimeSpan.FromMilliseconds(150));
            var stage = Stage(window);
            // O botão tem o desenho da espada antes do texto.
            var attack = Descendants(stage).OfType<Button>().First(b => Descendants(b).OfType<TextBlock>().Any(t => t.Text == "Atacar"));
            Assert.Contains(Descendants(attack).OfType<ShapePath>(), p => p.Data == IconSet.Shape(Icon.Sword));
            // O d6 mostra bolinhas (5); o d20, o número.
            Assert.Contains(Descendants(stage).OfType<TextBlock>(), t => t.Text == "17");
            Assert.Equal(5, Descendants(stage).OfType<Ellipse>().Count(e => e.Width < 20));
            Snapshot(stage, "csharplab-icones-e-dados.png");

            // Rolar: os números passam e ele para no resultado.
            var d20 = Descendants(stage).OfType<FrameworkElement>().First(e => e.Tag is ScreenRenderer.DiceFace);
            var number = Descendants(d20).OfType<TextBlock>().First();
            EffectPlayer.Roll(d20, 20, 20);
            Pump(TimeSpan.FromMilliseconds(300));
            Pump(EffectPlayer.RollTime + TimeSpan.FromMilliseconds(200));
            Assert.Equal("20", number.Text);
            Assert.IsType<System.Windows.Media.Effects.DropShadowEffect>(d20.Effect);   // o 20 acende dourado
        }
        finally { window.Close(); }
    });

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
        if (failure != null) throw new Exception(failure.ToString(), failure);
    }
}
