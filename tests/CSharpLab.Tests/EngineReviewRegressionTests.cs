using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CSharpLab.GameEngine;
using Color = CSharpLab.GameEngine.Color;

namespace CSharpLab.Tests;

public sealed class EngineReviewRegressionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "csharplab-review", Guid.NewGuid().ToString("N"));

    private sealed class View : IGameView
    {
        public List<Screen> Screens { get; } = [];
        public Screen Last => Screens[^1];
        public void Show(Screen screen) => Screens.Add(screen);
    }

    private Game Create(string? pieces, Action<Game> build)
    {
        Directory.CreateDirectory(Path.Combine(_dir, "Screens"));
        if (pieces != null) File.WriteAllText(Path.Combine(_dir, "Screens", "Test.json"), "{\"pieces\":[" + pieces + "]}");
        var game = new Game("Review", "review") { Screens = new ScreenLibrary { Roots = [_dir] } };
        game.Scene("Test", () => build(game));
        return game;
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-10, -1)]
    [InlineData(-10, 0)]
    public void Contador_aceita_intervalos_que_terminam_em_zero_ou_negativo(int min, int max)
    {
        var piece = Piece.CreateDefault(PieceType.Counter, "Count", 0, 0);
        piece.Min = min;
        piece.Max = max;
        var parsed = ScreenFile.Parse(ScreenFile.Serialize(new ScreenLayout { Pieces = [piece] }));
        Assert.True(parsed.Success, parsed.Error);
        Assert.Equal(min, parsed.Layout!.Find("Count")!.CounterMin);
        Assert.Equal(max, parsed.Layout.Find("Count")!.CounterMax);
        piece.Min = max + 1;
        Assert.False(ScreenFile.Parse(ScreenFile.Serialize(new ScreenLayout { Pieces = [piece] })).Success);
        Assert.False(ScreenFile.Parse("""{"pieces":[{"type":"Bar","name":"Health","max":0}]}""").Success);
    }

    [Fact]
    public void Salvar_preserva_dimensoes_fracionarias_e_continua_legivel()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            var layout = ScreenFile.Parse("""
                {"pieces":[
                  {"type":"Box","name":"Line","x":0.15,"y":-0.25,"width":0.4,"height":0.2},
                  {"type":"List","name":"Items","cardWidth":0.4,"cardHeight":0.3,"gap":0.25}
                ]}
                """).Layout!;
            var json = ScreenFile.Serialize(layout);
            var result = ScreenFile.Parse(json);
            Assert.True(result.Success, result.Error);
            var line = result.Layout!.Find("Line")!;
            Assert.Equal((0.15, -0.25, 0.4, 0.2), (line.X, line.Y, line.Width, line.Height));
            var list = result.Layout.Find("Items")!;
            Assert.Equal((0.4, 0.3, 0.25), (list.CardW, list.CardH, list.CardGap));
            Assert.Equal(json, ScreenFile.Serialize(result.Layout));
        }
        finally { CultureInfo.CurrentCulture = before; }
    }

    [Fact]
    public void Fonte_normal_explicita_sobrepoe_o_tema_e_sobrevive_ao_arquivo() => OnSta(() =>
    {
        Theme.Current = Look.Fantasy;
        var piece = new Piece { Type = PieceType.Text, Name = "Title", Text = "Título", Size = 32, Width = 300, Height = 60 };
        Assert.Equal(Font.Fantasy, Theme.FontFor(piece));
        new Item(piece, "Test").Font = Font.Normal;
        var loaded = ScreenFile.Parse(ScreenFile.Serialize(new ScreenLayout { Pieces = [piece] })).Layout!.Find("Title")!;
        Assert.Equal(Font.Normal, Theme.FontFor(loaded));
        var text = (TextBlock)((Border)ScreenRenderer.Create(loaded, new RenderContext())).Child;
        Assert.Equal(Theme.DefaultFont, text.FontFamily);
        loaded.Font = null;
        Assert.Equal(Font.Fantasy, Theme.FontFor(loaded));

        var frame = new Piece { Type = PieceType.Box, Name = "Frame", Width = 100, Height = 100, Border = false };
        new Item(frame, "Test").BorderColor = Color.Red;
        var border = Assert.IsType<Border>(ScreenRenderer.Create(frame, new RenderContext()));
        Assert.True(border.BorderThickness.Left > 0);
        Assert.Equal(Theme.Rgb(Color.Red), ((SolidColorBrush)border.BorderBrush).Color);
    });

    [Theory]
    [InlineData(2147483646, 10, 1, 2147483647)]
    [InlineData(-2147483647, 10, -1, -2147483648)]
    public void Contador_nao_estoura_antes_de_aplicar_o_limite(int value, int step, int direction, int expected)
    {
        var game = Create("""{"type":"Counter","name":"Count","min":-2147483648,"max":2147483647}""", _ => { });
        game.Begin("Test", new View());
        var item = game.Find("Count");
        item.Value = value;
        item.Step = step;
        game.StepPiece(item.Piece, direction);
        Assert.Equal(expected, item.Value);
    }

    private const string ListPieces = """
        {"type":"List","name":"Goods","width":600,"height":300},
        {"type":"Button","name":"Buy","list":"Goods","text":"Comprar"},
        {"type":"Input","name":"Note","list":"Goods","y":60}
        """;

    [Fact]
    public void Efeitos_do_cartao_seguem_o_item_apos_reordenar_e_esperar() => OnSta(() =>
    {
        var values = new List<string> { "Espada", "Escudo" };
        var view = new View();
        var game = Create(ListPieces, g => g.Find("Goods").Show(values, (card, value) =>
        {
            card.Find("Buy").OnClick(() =>
            {
                card.Find("Buy").Pop(value);
                values.Reverse();
                g.Wait(0);
                card.Find("Buy").Shake(); // a variável card ainda é a do clique, antes do Wait
                card.Find("Buy").Flash();
            });
        }));
        game.Begin("Test", view);
        var clicked = game.Find("Goods").Cards[0].Find("Buy").Piece;
        game.ClickPiece(clicked);
        var moved = game.Find("Goods").Cards[1].Find("Buy").Piece;
        Assert.Same(clicked.RuntimeId, moved.RuntimeId);
        Assert.NotSame(game.Find("Goods").Cards[0].Find("Buy").Piece.RuntimeId, moved.RuntimeId);
        Assert.All(view.Last.Effects, effect => Assert.Same(moved.RuntimeId, effect.Piece.RuntimeId));
        var window = new GameWindow(game);
        try
        {
            window.Show(view.Screens[^2]);
            Assert.Contains(Descendants(window).OfType<TextBlock>(), text => text.Text == "Espada");
            window.Show(view.Last);
            Assert.Single(Descendants(window).OfType<Button>(), b => b.RenderTransform is TranslateTransform);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Cartoes_iguais_tem_identidades_distintas_e_item_removido_nao_transfere_efeito()
    {
        var values = new List<string> { "Espada", "Espada", "Escudo" };
        var view = new View();
        var game = Create(ListPieces, g => g.Find("Goods").Show(values, (card, value) =>
            card.Find("Buy").OnClick(() => { card.Find("Buy").Pop("Saiu"); values.Remove(value); })));
        game.Begin("Test", view);
        var cards = game.Find("Goods").Cards;
        Assert.NotSame(cards[0].Find("Buy").Piece.RuntimeId, cards[1].Find("Buy").Piece.RuntimeId);
        var removed = cards[2].Find("Buy").Piece;
        game.ClickPiece(removed);
        Assert.DoesNotContain(game.Find("Goods").Cards, c => ReferenceEquals(c.Find("Buy").Piece.RuntimeId, removed.RuntimeId));
    }

    [Fact]
    public void Campo_preserva_texto_e_selecao_mas_limpa_ao_enviar_ou_reentrar() => OnSta(() =>
    {
        string? answer = null;
        var game = Create("""{"type":"Input","name":"Name"},{"type":"Input","name":"OtherName","y":100},{"type":"Button","name":"Other"}""", g =>
        {
            g.Find("Other").OnClick(() => { });
            g.Find("Name").OnAnswer(value => answer = value);
        });
        var window = new GameWindow(game);
        try
        {
            game.Begin("Test", window);
            var inputs = Descendants(window).OfType<TextBox>().ToArray();
            inputs[0].Text = "Henrique";
            inputs[0].Select(2, 3);
            inputs[1].Text = "Outra resposta";
            game.ClickPiece(game.Find("Other").Piece);
            inputs = Descendants(window).OfType<TextBox>().ToArray();
            Assert.Equal("Henrique", inputs[0].Text);
            Assert.Equal((2, 3), (inputs[0].SelectionStart, inputs[0].SelectionLength));
            Assert.Equal("Outra resposta", inputs[1].Text);
            Descendants(window).OfType<Button>().First(b => b.Content is TextBlock { Text: "OK" })
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("Henrique", answer);
            inputs = Descendants(window).OfType<TextBox>().ToArray();
            Assert.Empty(inputs[0].Text);
            Assert.Equal("Outra resposta", inputs[1].Text);
            game.GoTo("Test");
            Assert.All(Descendants(window).OfType<TextBox>(), input => Assert.Empty(input.Text));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Campo_de_cartao_mantem_a_resposta_com_o_item_apos_reordenar() => OnSta(() =>
    {
        var values = new List<string> { "Espada", "Escudo" };
        var game = Create(ListPieces, g => g.Find("Goods").Show(values, (card, _) =>
            card.Find("Buy").OnClick(values.Reverse)));
        var window = new GameWindow(game);
        try
        {
            game.Begin("Test", window);
            Descendants(window).OfType<TextBox>().First().Text = "Para a espada";
            game.ClickPiece(game.Find("Goods").Cards[0].Find("Buy").Piece);
            var inputs = Descendants(window).OfType<TextBox>().ToArray();
            Assert.Empty(inputs[0].Text);
            Assert.Equal("Para a espada", inputs[1].Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Ask_tambem_preserva_a_escrita_em_clique_na_mesma_cena() => OnSta(() =>
    {
        var game = Create(null, g => { g.Ask("Nome?", _ => { }); g.Button("Outro", () => { }); });
        var view = new View();
        game.Begin("Test", view);
        var window = new GameWindow(game);
        try
        {
            window.Show(view.Last);
            Descendants(window).OfType<TextBox>().Single().Text = "Henrique";
            game.Act(view.Last.Buttons.Single().OnClick);
            window.Show(view.Last);
            Assert.Equal("Henrique", Descendants(window).OfType<TextBox>().Single().Text);
            game.GoTo("Test");
            window.Show(view.Last);
            Assert.Empty(Descendants(window).OfType<TextBox>().Single().Text);
        }
        finally { window.Close(); }
    });

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void OnSta(Action test)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { test(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "O teste de interface não terminou.");
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }
}
