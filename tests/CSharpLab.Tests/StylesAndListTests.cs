using System.Windows;
using System.Windows.Controls;
using CSharpLab.Core.Language;
using CSharpLab.GameEngine;
using Color = CSharpLab.GameEngine.Color;
using Font = CSharpLab.GameEngine.Font;

namespace CSharpLab.Tests;

/// <summary>
/// Os recursos novos das telas: estilos (caixa, texto, botão), Enabled, a Lista com cartões,
/// o histórico das Mensagens, o game.Wait e os movimentos (Shake, Flash).
/// </summary>
public sealed class StylesAndListTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "csharplab-estilos", Guid.NewGuid().ToString("N")[..8]);

    public StylesAndListTests() => Directory.CreateDirectory(Path.Combine(_dir, "Screens"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    /// <summary>Guarda cada tela mostrada e cada pausa do game.Wait.</summary>
    private sealed class FakeView : IGameView
    {
        public List<Screen> Shown { get; } = [];
        public List<double> Pauses { get; } = [];
        /// <summary>O texto da peça Gold em cada tela mostrada (as peças mudam depois, então guarda na hora).</summary>
        public List<string?> Golds { get; } = [];
        public Screen Last => Shown[^1];
        public void Show(Screen screen)
        {
            Shown.Add(screen);
            Golds.Add(screen.Designed?.Find("Gold")?.Text);
        }
        public void Pause(double seconds) => Pauses.Add(seconds);
        public Piece Piece(string name) => Last.Designed!.Layout.Find(name)!;
    }

    private const string ShopScreen = """
        {
          "format": 1,
          "pieces": [
            { "type": "Text", "name": "Title", "x": 40, "y": 20, "width": 600, "height": 60, "text": "Loja", "size": 34, "font": "Fantasy", "shadow": true, "color": "Gold" },
            { "type": "Text", "name": "Gold", "x": 700, "y": 30, "width": 200, "height": 40, "text": "💰 0", "italic": true },
            { "type": "List", "name": "Weapons", "x": 40, "y": 100, "width": 880, "height": 300, "text": "Nada à venda.", "cardWidth": 280, "cardHeight": 140, "gap": 20 },
            { "type": "Box", "name": "CardBack", "list": "Weapons", "x": 0, "y": 0, "width": 280, "height": 140, "color": "Red", "shade": "Dark", "opacity": 80, "border": true, "corner": "Square" },
            { "type": "Text", "name": "Name", "list": "Weapons", "x": 16, "y": 12, "width": 248, "height": 34, "text": "Espada", "bold": true },
            { "type": "Text", "name": "Price", "list": "Weapons", "x": 16, "y": 50, "width": 248, "height": 30, "text": "💰 10", "color": "Gold" },
            { "type": "Button", "name": "Buy", "list": "Weapons", "x": 16, "y": 88, "width": 248, "height": 40, "text": "Comprar", "style": "Outline", "color": "Green" },
            { "type": "Messages", "name": "Log", "x": 40, "y": 410, "width": 600, "height": 110 },
            { "type": "Button", "name": "Back", "x": 700, "y": 460, "width": 200, "height": 52, "text": "Voltar", "style": "Text", "enabled": false }
          ]
        }
        """;

    private sealed record Weapon(string Name, int Price);

    private (Game Game, FakeView View) Started(string scene, string json, Action<Game> setup)
    {
        File.WriteAllText(Path.Combine(_dir, "Screens", scene + ".json"), json);
        var game = new Game("Teste") { Screens = new ScreenLibrary { Roots = [_dir] } };
        setup(game);
        var view = new FakeView();
        game.Begin(scene, view);
        return (game, view);
    }

    // ------------------------------------------------------------------ arquivo

    [Fact]
    public void Estilos_e_lista_vao_e_voltam_no_arquivo()
    {
        var result = ScreenFile.Parse(ShopScreen);
        Assert.True(result.Success, result.Error);
        Assert.Empty(result.Warnings);
        var layout = result.Layout!;

        var title = layout.Find("Title")!;
        Assert.Equal((Font.Fantasy, true), (title.Font, title.Shadow));
        Assert.True(layout.Find("Gold")!.Italic);
        var back = layout.Find("CardBack")!;
        Assert.Equal(("Weapons", Shade.Dark, 80, true, Corner.Square), (back.List, back.Shade, back.Opacity, back.Border, back.Corner));
        var list = layout.Find("Weapons")!;
        Assert.Equal((280d, 140d, 20d, "Nada à venda."), (list.CardW, list.CardH, list.CardGap, list.Text));
        Assert.Equal(ButtonStyle.Outline, layout.Find("Buy")!.Style);
        Assert.False(layout.Find("Back")!.Enabled);
        Assert.Equal(["CardBack", "Name", "Price", "Buy"], layout.MembersOf("weapons").Select(p => p.Name));

        // Gravar e ler de novo dá o mesmo texto, com as chaves novas numa linha só por peça.
        var first = ScreenFile.Serialize(layout);
        Assert.Equal(first, ScreenFile.Serialize(ScreenFile.Parse(first).Layout!));
        Assert.Contains("{ \"type\": \"Box\", \"name\": \"CardBack\", \"list\": \"Weapons\", \"x\": 0, \"y\": 0, \"width\": 280, \"height\": 140, \"color\": \"Red\", \"shade\": \"Dark\", \"opacity\": 80, \"border\": true, \"corner\": \"Square\" }", first);
        Assert.Contains("\"style\": \"Text\", \"enabled\": false", first);
        Assert.Contains("\"cardWidth\": 280, \"cardHeight\": 140, \"gap\": 20", first);
    }

    [Theory]
    [InlineData("""{ "type": "Text", "name": "A", "list": "Nope" }""", "não tem uma Lista com esse nome")]
    [InlineData("""{ "type": "Messages", "name": "A", "list": "Weapons" }""", "não pode ficar dentro de uma Lista")]
    [InlineData("""{ "type": "Text", "name": "A", "list": "Box1" }""", "é Caixa, não Lista")]
    [InlineData("""{ "type": "Text", "name": "A", "font": "Comic" }""", "Normal, Fantasy, Book ou Hand")]
    [InlineData("""{ "type": "Box", "name": "A", "opacity": 140 }""", "vai de 0 a 100")]
    [InlineData("""{ "type": "Button", "name": "A", "style": "Big" }""", "Filled, Outline ou Text")]
    public void Erros_das_chaves_novas_em_portugues(string piece, string expected)
    {
        var json = "{ \"pieces\": [ { \"type\": \"List\", \"name\": \"Weapons\" }, { \"type\": \"Box\", \"name\": \"Box1\" }, " + piece + " ] }";
        var result = ScreenFile.Parse(json);
        Assert.False(result.Success);
        Assert.Contains(expected, result.Error);
    }

    // ------------------------------------------------------------------ lista

    [Fact]
    public void Show_cria_um_cartao_por_item_e_o_clique_sabe_qual_e()
    {
        var weapons = new List<Weapon> { new("Adaga", 5), new("Espada", 12), new("Machado", 20) };
        var bought = new List<string>();
        var (game, view) = Started("Shop", ShopScreen, g => g.Scene("Shop", () =>
        {
            g.Find("Weapons").Show(weapons, (card, weapon) =>
            {
                card.Find("Name").Text = weapon.Name;
                card.Find("Price").Text = $"💰 {weapon.Price}";
                card.Find("Buy").Enabled = weapon.Price <= 12;
                card.Find("Buy").OnClick(() => bought.Add(weapon.Name));
            });
        }));

        var cards = view.Last.Designed!.CardsOf(view.Piece("Weapons"));
        Assert.Equal(3, cards.Count);
        Assert.Equal(["Adaga", "Espada", "Machado"], cards.Select(c => c.Find("Name").Text));
        Assert.Equal(1, cards[1].Index);
        Assert.False(cards[2].Find("Buy").Enabled);
        // O cartão modelo continua como desenhado.
        Assert.Equal("Espada", view.Last.Designed.Layout.Find("Name")!.Text);

        game.ClickPiece(cards[1].Find("Buy").Piece);
        Assert.Equal(["Espada"], bought);
        // Botão apagado: o clique não faz nada.
        var redrawn = view.Last.Designed!.CardsOf(view.Piece("Weapons"));
        game.ClickPiece(redrawn[2].Find("Buy").Piece);
        Assert.Equal(["Espada"], bought);

        // Com 7 itens, 7 cartões (a rolagem é da janela).
        weapons.AddRange([new("Arco", 9), new("Lança", 14), new("Martelo", 18), new("Cajado", 11)]);
        game.ClickPiece(redrawn[0].Find("Buy").Piece);
        Assert.Equal(7, view.Last.Designed!.CardsOf(view.Piece("Weapons")).Count);
    }

    [Fact]
    public void Erros_da_lista_explicam_o_Show_e_o_card_Find()
    {
        var error = Assert.Throws<GameException>(() => Started("Shop", ShopScreen, g => g.Scene("Shop", () => g.Find("Price").Text = "x")));
        Assert.Contains("\"Price\" faz parte do cartão da lista \"Weapons\"", error.Message);
        Assert.Contains("card.Find(\"Price\")", error.Message);

        error = Assert.Throws<GameException>(() => Started("Shop", ShopScreen, g => g.Scene("Shop", () =>
            g.Find("Weapons").Show(new[] { 1 }, (card, _) => card.Find("Prize").Text = "x"))));
        Assert.Contains("O cartão da lista \"Weapons\" não tem a peça \"Prize\". Você quis dizer \"Price\"?", error.Message);

        error = Assert.Throws<GameException>(() => Started("Shop", ShopScreen, g => g.Scene("Shop", () => g.Find("Title").Show(new[] { 1 }, (_, _) => { }))));
        Assert.Contains("só a Lista tem Show", error.Message);

        error = Assert.Throws<GameException>(() => Started("Shop", ShopScreen, g => g.Scene("Shop", () => g.Find("Gold").Opacity = 10)));
        Assert.Contains("só a Caixa tem Opacity", error.Message);
    }

    [Fact]
    public void Lista_vazia_mostra_o_texto_de_vazia()
    {
        var (_, view) = Started("Shop", ShopScreen, g => g.Scene("Shop", () => g.Find("Weapons").Show(new List<Weapon>(), (_, _) => { })));
        Assert.Empty(view.Last.Designed!.CardsOf(view.Piece("Weapons")));
        Assert.Equal("Nada à venda.", view.Last.Designed.Find("Weapons")!.Text);
    }

    // ------------------------------------------------------------------ mensagens, pausa e movimento

    [Fact]
    public void Mensagens_guardam_o_historico_da_cena()
    {
        int round = 0;
        var (game, view) = Started("Shop", ShopScreen, g => g.Scene("Shop", () =>
        {
            g.Find("Back").Enabled = true;
            g.Find("Back").OnClick(() => g.Write($"Rodada {++round}"));
        }));

        game.ClickPiece(view.Piece("Back"));
        game.ClickPiece(view.Piece("Back"));
        game.ClickPiece(view.Piece("Back"));
        Assert.Equal(
            [new MessageLine("Rodada 1", null, false, IsOld: true), new MessageLine("Rodada 2", null, false, IsOld: true), new MessageLine("Rodada 3", null, true)],
            view.Last.Messages);
    }

    [Fact]
    public void Wait_mostra_o_meio_do_clique_e_depois_continua()
    {
        int enemy = 20, health = 30;
        var (game, view) = Started("Shop", ShopScreen, g => g.Scene("Shop", () =>
        {
            g.Find("Gold").Text = $"Inimigo {enemy} · Você {health}";
            g.Find("Back").Enabled = true;
            g.Find("Back").OnClick(() =>
            {
                enemy -= 7;
                g.Write("Você causou 7 de dano!", Color.Green);
                g.Find("Title").Shake();
                g.Wait(0.6);
                health -= 4;
                g.Write("O goblin revida: -4.", Color.Red);
                g.Find("Gold").Flash();
            });
        }));
        int before = view.Shown.Count;

        game.ClickPiece(view.Piece("Back"));
        Assert.Equal([0.6], view.Pauses);
        Assert.Equal(before + 2, view.Shown.Count);   // a tela do meio (antes da pausa) e a do fim

        var middle = view.Shown[^2];
        Assert.Equal("Inimigo 13 · Você 30", view.Golds[^2]);
        Assert.Equal([new MessageLine("Você causou 7 de dano!", Color.Green, true)], middle.Messages);
        Assert.Equal(Effect.Shake, Assert.Single(middle.Effects).Effect);

        // Depois da pausa: o golpe do herói fica apagado e a resposta do inimigo destacada.
        Assert.Equal("Inimigo 13 · Você 26", view.Last.Designed!.Find("Gold")!.Text);
        Assert.Equal(
            [new MessageLine("Você causou 7 de dano!", Color.Green, false, IsOld: true), new MessageLine("O goblin revida: -4.", Color.Red, true)],
            view.Last.Messages);
        Assert.Equal((Effect.Flash, "Gold"), (Assert.Single(view.Last.Effects).Effect, view.Last.Effects[0].Piece.Name));
        // O aviso por cima da tela (sem peça Mensagens) mostraria o clique inteiro.
        Assert.Equal(2, view.Last.Toast!.Count);
    }

    [Fact]
    public void Wait_fora_de_um_clique_explica_onde_usar()
    {
        var error = Assert.Throws<GameException>(() => Started("Shop", ShopScreen, g => g.Scene("Shop", () => g.Wait(1))));
        Assert.Contains("game.Wait funciona dentro de um clique", error.Message);

        var (game, view) = Started("Shop", ShopScreen, g => g.Scene("Shop", () =>
        {
            g.Find("Back").Enabled = true;
            g.Find("Back").OnClick(() => g.Wait(30));
        }));
        error = Assert.Throws<GameException>(() => game.ClickPiece(view.Piece("Back")));
        Assert.Contains("de 0 a 5 segundos", error.Message);
    }

    [Fact]
    public void Wait_com_troca_de_cena_mostra_a_cena_nova_antes_da_pausa()
    {
        File.WriteAllText(Path.Combine(_dir, "Screens", "Fight.json"), """{ "pieces": [ { "type": "Messages", "name": "Log", "x": 0, "y": 0, "width": 400, "height": 200 } ] }""");
        var (game, view) = Started("Shop", ShopScreen, g =>
        {
            g.Scene("Shop", () =>
            {
                g.Find("Back").Enabled = true;
                g.Find("Back").OnClick(() =>
                {
                    g.Write("Um lobo aparece!");
                    g.GoTo("Fight");
                    g.Wait(0.5);
                    g.Write("Ele rosna.");
                });
            });
            g.Scene("Fight", () => { });
        });

        game.ClickPiece(view.Piece("Back"));
        Assert.Equal("Fight", view.Shown[^2].SceneName);
        Assert.Equal(["Um lobo aparece!", "Ele rosna."], view.Last.Messages.Select(m => m.Text));
        Assert.Equal("Fight", game.CurrentScene);
    }

    /// <summary>
    /// A janela de verdade (fora da tela): o game.Wait espera sem travar, a troca de cena escurece e clareia
    /// e, depois, nada fica cobrindo a tela.
    /// </summary>
    [Fact]
    public void Janela_espera_no_Wait_e_troca_de_cena_com_escurecer()
    {
        File.WriteAllText(Path.Combine(_dir, "Screens", "Fight.json"), """{ "pieces": [ { "type": "Messages", "name": "Log", "x": 0, "y": 0, "width": 400, "height": 200 } ] }""");
        File.WriteAllText(Path.Combine(_dir, "Screens", "Shop.json"), ShopScreen);
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var game = new Game("Teste") { Screens = new ScreenLibrary { Roots = [_dir] } };
                game.Scene("Shop", () =>
                {
                    game.Find("Back").Enabled = true;
                    game.Find("Back").OnClick(() =>
                    {
                        game.GoTo("Fight");
                        game.Wait(0.3);
                        game.Write("Depois da pausa.");
                    });
                });
                game.Scene("Fight", () => { });
                var window = new GameWindow(game) { Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
                window.Show();
                game.Begin("Shop", window);
                window.UpdateLayout();

                var clock = System.Diagnostics.Stopwatch.StartNew();
                game.ClickPiece(game.Find("Back").Piece);
                Assert.True(clock.ElapsedMilliseconds >= 250, $"O Wait esperou só {clock.ElapsedMilliseconds} ms");
                Assert.Equal("Fight", game.CurrentScene);

                // Deixa a troca de cena terminar (ela dura ~0,35 s) e confere que a cortina saiu.
                window.Pause(0.6);
                var root = (Grid)((System.Windows.Controls.Viewbox)window.Content).Child;
                var curtain = root.Children.OfType<System.Windows.Shapes.Rectangle>().Single();
                Assert.Equal(0, curtain.Opacity);
                Assert.False(curtain.IsHitTestVisible);
                window.Close();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.Null(failure?.ToString());
    }

    // ------------------------------------------------------------------ dicas do editor

    [Fact]
    public void Todo_nome_publico_do_motor_tem_explicacao_em_portugues()
    {
        var missing = new List<string>();
        foreach (var type in typeof(Game).Assembly.GetExportedTypes())
        {
            var key = type.FullName!;
            if (!HasDoc(key)) missing.Add(key);
            if (type.IsEnum)
            {
                missing.AddRange(Enum.GetNames(type).Select(n => key + "." + n).Where(k => !HasDoc(k)));
                continue;
            }
            foreach (var member in type.GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly))
            {
                if (member is System.Reflection.MethodInfo { IsSpecialName: true } || member is System.Reflection.ConstructorInfo) continue;
                if (!HasDoc(key + "." + member.Name)) missing.Add(key + "." + member.Name);
            }
        }
        Assert.Empty(missing.Distinct());

        static bool HasDoc(string key) => PortugueseDocs.ForSignature(key) != null;
    }

    // ------------------------------------------------------------------ foto

    /// <summary>
    /// Os estilos novos na janela do jogo: fontes, itálico, sombra, tons, caixa transparente, cantos, círculo,
    /// botões cheio/contorno/só texto, botão apagado, lista com cartões e mensagens com histórico.
    /// A imagem fica em %TEMP%\csharplab-estilos.png.
    /// </summary>
    [Fact]
    public void Estilos_novos_aparecem_na_janela()
    {
        const string json = """
            { "pieces": [
              { "type": "Box", "name": "Frame", "x": 24, "y": 20, "width": 140, "height": 140, "color": "Red", "shade": "Dark", "opacity": 100, "border": true, "corner": "Circle" },
              { "type": "Text", "name": "Face", "x": 24, "y": 40, "width": 140, "height": 100, "text": "👺", "size": 60, "align": "Center", "color": "Red", "shade": "Light" },
              { "type": "Text", "name": "Title", "x": 184, "y": 14, "width": 520, "height": 60, "text": "A Coroa Perdida", "size": 34, "font": "Fantasy", "color": "Gold", "shadow": true },
              { "type": "Text", "name": "Scroll", "x": 184, "y": 74, "width": 380, "height": 40, "text": "Era uma vez, num reino distante…", "size": 18, "font": "Book", "italic": true },
              { "type": "Text", "name": "Note", "x": 184, "y": 112, "width": 380, "height": 40, "text": "Bilhete: volto já!", "size": 16, "font": "Hand", "color": "Orange" },
              { "type": "Box", "name": "Glass", "x": 600, "y": 80, "width": 330, "height": 80, "color": "Blue", "opacity": 50, "border": false, "corner": "Square" },
              { "type": "Text", "name": "OnGlass", "x": 616, "y": 96, "width": 300, "height": 50, "text": "Caixa azul 50% sem borda", "size": 16 },
              { "type": "List", "name": "Items", "x": 24, "y": 176, "width": 600, "height": 186, "cardWidth": 186, "cardHeight": 186, "gap": 21 },
              { "type": "Box", "name": "CardBack", "list": "Items", "x": 0, "y": 0, "width": 186, "height": 186, "color": "Gold", "shade": "Dark", "opacity": 35 },
              { "type": "Text", "name": "Icon", "list": "Items", "x": 0, "y": 8, "width": 186, "height": 60, "text": "🗡", "size": 40, "align": "Center", "color": "Gold" },
              { "type": "Text", "name": "Name", "list": "Items", "x": 8, "y": 72, "width": 170, "height": 30, "text": "Item", "align": "Center", "bold": true },
              { "type": "Text", "name": "Price", "list": "Items", "x": 8, "y": 102, "width": 170, "height": 26, "text": "💰 0", "align": "Center", "color": "Gold" },
              { "type": "Button", "name": "Buy", "list": "Items", "x": 18, "y": 134, "width": 150, "height": 40, "text": "Comprar", "size": 15, "color": "Gold" },
              { "type": "Messages", "name": "Log", "x": 640, "y": 176, "width": 296, "height": 186, "size": 14 },
              { "type": "Button", "name": "Attack", "x": 24, "y": 380, "width": 200, "height": 52, "text": "⚔ Atacar", "color": "Red" },
              { "type": "Button", "name": "Defend", "x": 236, "y": 380, "width": 200, "height": 52, "text": "Defender", "style": "Outline", "color": "Blue" },
              { "type": "Button", "name": "Back", "x": 448, "y": 380, "width": 160, "height": 52, "text": "↩ Voltar", "style": "Text" },
              { "type": "Button", "name": "Locked", "x": 620, "y": 380, "width": 160, "height": 52, "text": "🔒 Torre", "enabled": false },
              { "type": "Button", "name": "Dark", "x": 792, "y": 380, "width": 144, "height": 52, "text": "Escuro", "color": "Green", "shade": "Dark" },
              { "type": "Bar", "name": "Health", "x": 24, "y": 450, "width": 300, "height": 44, "text": "Vida", "value": 70, "max": 100, "color": "Red", "shade": "Dark" },
              { "type": "Box", "name": "Plain", "x": 344, "y": 450, "width": 280, "height": 70, "corner": "Square", "border": true },
              { "type": "Text", "name": "PlainText", "x": 360, "y": 466, "width": 250, "height": 40, "text": "Caixa sem cor, com borda", "size": 15, "color": "Gray" }
            ] }
            """;
        File.WriteAllText(Path.Combine(_dir, "Screens", "Styles.json"), json);
        var items = new[] { ("Espada", "🗡", 30), ("Escudo", "🛡", 25), ("Poção", "🧪", 8), ("Arco", "🏹", 18) };
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var game = new Game("Teste") { Screens = new ScreenLibrary { Roots = [_dir] } };
                game.Scene("Styles", () =>
                {
                    game.Find("Items").Show(items, (card, item) =>
                    {
                        card.Find("Name").Text = item.Item1;
                        card.Find("Icon").Text = item.Item2;
                        card.Find("Price").Text = $"💰 {item.Item3}";
                        card.Find("Buy").Enabled = item.Item3 <= 25;
                    });
                    game.Find("Attack").OnClick(() =>
                    {
                        game.Write("Você causou 7 de dano!", Color.Green);
                        game.Wait(0.1);
                        game.Write("O Rei Goblin revida: -9 de vida.", Color.Red);
                    });
                });
                var view = new FakeView();
                game.Begin("Styles", view);
                game.ClickPiece(view.Piece("Attack"));
                game.ClickPiece(view.Piece("Attack"));
                var window = new GameWindow(game);
                window.Show(view.Last);
                DesignedSceneTests.Render(window, "csharplab-estilos.png");
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.Null(failure?.ToString());
    }
}
