using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CSharpLab.GameEngine;
using Color = CSharpLab.GameEngine.Color;

namespace CSharpLab.Tests;

/// <summary>O arquivo das telas desenhadas (Screens/*.json): leitura, gravação e erros em português.</summary>
public sealed class ScreenFileTests
{
    private const string Sample = """
        {
          "format": 1,
          "background": "forest.png",
          "pieces": [
            { "type": "Text", "name": "Story", "x": 40, "y": 40, "width": 560, "height": 120, "text": "Um goblin aparece!", "size": 22, "bold": true, "align": "Center", "color": "Gold" },
            { "type": "Bar", "name": "PlayerHealth", "x": 640, "y": 40, "width": 280, "height": 44, "text": "Vida", "value": 70, "max": 100, "color": "Green" },
            { "type": "Button", "name": "Attack", "x": 40, "y": 460, "width": 180, "height": 52, "text": "Atacar" },
            { "type": "Image", "name": "Goblin", "x": 600, "y": 200, "width": 200, "height": 200, "image": "goblin.png", "visible": false },
            { "type": "Input", "name": "NameField", "x": 40, "y": 300, "width": 420, "height": 88, "text": "Seu nome?" },
            { "type": "Messages", "name": "Log", "x": 40, "y": 180, "width": 520, "height": 100 },
            { "type": "Box", "name": "Panel", "x": 620, "y": 20, "width": 320, "height": 100, "color": "Blue" }
          ]
        }
        """;

    [Fact]
    public void Le_todas_as_pecas()
    {
        var result = ScreenFile.Parse(Sample);
        Assert.True(result.Success, result.Error);
        var layout = result.Layout!;
        Assert.Equal("forest.png", layout.Background);
        Assert.Equal(7, layout.Pieces.Count);

        var story = layout.Find("story")!; // maiúsculas não importam
        Assert.Equal(PieceType.Text, story.Type);
        Assert.Equal((40d, 40d, 560d, 120d), (story.X, story.Y, story.Width, story.Height));
        Assert.Equal(("Um goblin aparece!", 22d, true, TextAlign.Center, Color.Gold), (story.Text, story.Size, story.Bold, story.Align, story.Color));

        var bar = layout.Find("PlayerHealth")!;
        Assert.Equal((70, 100, Color.Green), (bar.Value, bar.Max, bar.Color));
        Assert.False(layout.Find("Goblin")!.Visible);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Gravar_e_ler_de_novo_da_o_mesmo_arquivo()
    {
        var first = ScreenFile.Serialize(ScreenFile.Parse(Sample).Layout!);
        var second = ScreenFile.Serialize(ScreenFile.Parse(first).Layout!);
        Assert.Equal(first, second);
        // Uma peça por linha, acentos legíveis (sem ã).
        Assert.Contains("{ \"type\": \"Button\", \"name\": \"Attack\", \"x\": 40, \"y\": 460, \"width\": 180, \"height\": 52, \"text\": \"Atacar\" }", first);
        Assert.Contains("\"Um goblin aparece!\"", first);
        Assert.DoesNotContain("\\u", first);
    }

    [Fact]
    public void Tela_nova_tem_titulo_e_botao()
    {
        var layout = ScreenLayout.CreateDefault("Fight");
        Assert.Equal(["Title", "Continue"], layout.Pieces.Select(p => p.Name));
        Assert.True(ScreenFile.Parse(ScreenFile.Serialize(layout)).Success);
        Assert.Equal("Button1", layout.NewName(PieceType.Button));
    }

    [Theory]
    [InlineData("{ \"pieces\": [ { \"type\": \"Text\" \"name\": \"A\" } ] }", "perto da linha 1")]
    [InlineData("{ \"pieces\": [ { \"type\": \"Label\", \"name\": \"A\" } ] }", "\"type\" válido: Text, Button, Bar, Image, Box, Input, Messages")]
    [InlineData("{ \"pieces\": [ { \"type\": \"Button\" } ] }", "precisa de um \"name\"")]
    [InlineData("{ \"pieces\": [ { \"type\": \"Button\", \"name\": \"Atacar já\" } ] }", "letras sem acento")]
    [InlineData("{ \"pieces\": [ { \"type\": \"Text\", \"name\": \"A\" }, { \"type\": \"Text\", \"name\": \"a\" } ] }", "Duas peças se chamam \"a\"")]
    [InlineData("{ \"pieces\": [ { \"type\": \"Text\", \"name\": \"A\", \"color\": \"Yellow\" } ] }", "White, Gray, Red, Green, Blue, Gold, Purple, Orange")]
    [InlineData("{ \"pieces\": [ { \"type\": \"Text\", \"name\": \"A\", \"x\": \"40\" } ] }", "\"x\" precisa ser um número, sem aspas")]
    [InlineData("{ \"pieces\": [ { \"type\": \"Text\", \"name\": \"A\", \"width\": 0 } ] }", "\"width\" precisa ser maior que 0")]
    [InlineData("{ \"format\": 9, \"pieces\": [] }", "versão mais nova")]
    [InlineData("[]", "precisa começar com {")]
    public void Erros_explicam_o_que_corrigir(string json, string expected)
    {
        var result = ScreenFile.Parse(json);
        Assert.False(result.Success);
        Assert.Contains(expected, result.Error);
    }

    [Fact]
    public void Erro_aponta_a_linha_da_peca()
    {
        var json = "{\n  \"pieces\": [\n    { \"type\": \"Text\", \"name\": \"Ok\" },\n    { \"type\": \"Text\", \"name\": \"Bad\", \"color\": \"Pink\" }\n  ]\n}";
        var result = ScreenFile.Parse(json);
        Assert.Equal(4, result.ErrorLine);
    }

    [Fact]
    public void Chave_desconhecida_vira_aviso_e_nao_erro()
    {
        var result = ScreenFile.Parse("{ \"pieces\": [ { \"type\": \"Text\", \"name\": \"A\", \"colour\": \"Red\" } ] }");
        Assert.True(result.Success);
        Assert.Contains(result.Warnings, w => w.Contains("\"colour\""));
    }

    [Fact]
    public void Aceita_virgula_sobrando_e_comentarios_que_uma_ia_pode_escrever()
    {
        var result = ScreenFile.Parse("{ // tela\n \"pieces\": [ { \"type\": \"Text\", \"name\": \"A\", }, ], }");
        Assert.True(result.Success, result.Error);
    }
}

/// <summary>Cenas desenhadas rodando: game.Find, cliques, estado das peças e erros claros.</summary>
public sealed class DesignedSceneTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "csharplab-telas", Guid.NewGuid().ToString("N")[..8]);

    public DesignedSceneTests() => Directory.CreateDirectory(Path.Combine(_dir, "Screens"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private sealed class FakeView : IGameView
    {
        public Screen Last { get; private set; } = new();
        public void Show(Screen screen) => Last = screen;
        public Piece Piece(string name) => Last.Designed!.Layout.Find(name)!;
    }

    private void Draw(string scene, params Piece[] pieces) =>
        File.WriteAllText(Path.Combine(_dir, "Screens", scene + ".json"), ScreenFile.Serialize(new ScreenLayout { Pieces = [.. pieces] }));

    private static Piece P(PieceType type, string name, string? text = null)
    {
        var piece = Piece.CreateDefault(type, name, 10, 10);
        if (text != null) piece.Text = text;
        return piece;
    }

    private (Game Game, FakeView View) Started(Action<Game> setup, string first)
    {
        var game = new Game("Teste") { Screens = new ScreenLibrary { Roots = [_dir] } };
        setup(game);
        var view = new FakeView();
        game.Begin(first, view);
        return (game, view);
    }

    [Fact]
    public void Find_muda_as_pecas_e_o_clique_roda_o_codigo()
    {
        Draw("Fight", P(PieceType.Text, "Story"), P(PieceType.Bar, "PlayerHealth"), P(PieceType.Button, "Attack", "Atacar"), P(PieceType.Button, "Potion"));
        int enemy = 30, potions = 0;
        var (game, view) = Started(g => g.Scene("Fight", () =>
        {
            g.Find("Story").Text = $"Goblin: {enemy}";
            g.Find("PlayerHealth").Value = 42;
            g.Find("Potion").Visible = potions > 0;
            g.Find("Attack").OnClick(() => enemy -= 10);
        }), "Fight");

        Assert.Equal("Goblin: 30", view.Piece("Story").Text);
        Assert.Equal(42, view.Piece("PlayerHealth").Value);
        Assert.False(view.Piece("Potion").Visible);
        Assert.Equal("Atacar", view.Piece("Attack").Text); // o que o código não mudou fica como desenhado

        game.ClickPiece(view.Piece("Attack"));
        Assert.Equal(20, enemy);
        Assert.Equal("Goblin: 20", view.Piece("Story").Text);
    }

    [Fact]
    public void Mudanca_feita_no_clique_continua_ate_sair_da_cena()
    {
        Draw("Room", P(PieceType.Text, "Story", "Sala vazia."), P(PieceType.Button, "Look"), P(PieceType.Button, "Leave"));
        Draw("Hall", P(PieceType.Button, "Back"));
        var (game, view) = Started(g =>
        {
            g.Scene("Room", () =>
            {
                g.Find("Look").OnClick(() => g.Find("Story").Text = "Você vê uma chave!");
                g.Find("Leave").OnClick(() => g.GoTo("Hall"));
            });
            g.Scene("Hall", () => g.Find("Back").OnClick(() => g.GoTo("Room")));
        }, "Room");

        game.ClickPiece(view.Piece("Look"));
        Assert.Equal("Você vê uma chave!", view.Piece("Story").Text);
        game.ClickPiece(view.Piece("Leave"));
        game.ClickPiece(view.Piece("Back"));
        Assert.Equal("Sala vazia.", view.Piece("Story").Text); // voltou para a cena: começa como desenhada
    }

    [Fact]
    public void Write_vai_para_as_mensagens()
    {
        Draw("Fight", P(PieceType.Messages, "Log"), P(PieceType.Button, "Attack"));
        var (game, view) = Started(g => g.Scene("Fight", () =>
        {
            g.Write("Um goblin aparece!");
            g.Find("Attack").OnClick(() => g.Write("7 de dano!", Color.Red));
        }), "Fight");

        game.ClickPiece(view.Piece("Attack"));
        Assert.Equal([new MessageLine("Um goblin aparece!", null, false), new MessageLine("7 de dano!", Color.Red, true)], view.Last.Messages);
    }

    [Fact]
    public void Botao_sem_OnClick_explica_o_que_falta()
    {
        Draw("Start", P(PieceType.Button, "Go"));
        var (game, view) = Started(g => g.Scene("Start", () => { }), "Start");
        game.ClickPiece(view.Piece("Go"));
        Assert.Contains(view.Last.Messages, m => m.IsNews && m.Text.Contains("game.Find(\"Go\").OnClick"));
    }

    [Fact]
    public void Campo_de_escrita_entrega_a_resposta()
    {
        Draw("Name", P(PieceType.Input, "NameField"));
        Draw("Hello", P(PieceType.Text, "Greeting"));
        string player = "";
        var (game, view) = Started(g =>
        {
            g.Scene("Name", () => g.Find("NameField").OnAnswer(answer =>
            {
                player = answer;
                g.GoTo("Hello");
            }));
            g.Scene("Hello", () => g.Find("Greeting").Text = $"Olá, {player}!");
        }, "Name");

        game.AnswerPiece(view.Piece("NameField"), "Ana");
        Assert.Equal("Olá, Ana!", view.Piece("Greeting").Text);
    }

    [Fact]
    public void Erros_de_uso_em_portugues()
    {
        Draw("Fight", P(PieceType.Text, "Story"), P(PieceType.Button, "Attack"));
        var error = Assert.Throws<GameException>(() => Started(g => g.Scene("Fight", () => g.Find("Atack")), "Fight"));
        Assert.Contains("\"Atack\" não existe na tela \"Fight\". Você quis dizer \"Attack\"?", error.Message);

        error = Assert.Throws<GameException>(() => Started(g => g.Scene("Fight", () => g.Find("Story").Value = 3), "Fight"));
        Assert.Contains("\"Story\" (na tela \"Fight\") é do tipo Texto: só barras têm Value.", error.Message);

        error = Assert.Throws<GameException>(() => Started(g => g.Scene("Fight", () => g.Find("Story").OnClick(() => { })), "Fight"));
        Assert.Contains("só botões e imagens têm OnClick", error.Message);

        error = Assert.Throws<GameException>(() => Started(g => g.Scene("Fight", () => g.Button("Fugir", () => { })), "Fight"));
        Assert.Contains("foi desenhada na aba Tela", error.Message);

        error = Assert.Throws<GameException>(() => Started(g => g.Scene("Plain", () => g.Find("X")), "Plain"));
        Assert.Contains("não foi desenhada na aba Tela", error.Message);
    }

    [Fact]
    public void Tela_com_erro_no_arquivo_para_com_a_linha()
    {
        File.WriteAllText(Path.Combine(_dir, "Screens", "Bad.json"), "{\n  \"pieces\": [\n    { \"type\": \"Text\", \"name\": \"A\", \"color\": \"Pink\" }\n  ]\n}");
        var error = Assert.Throws<GameException>(() => Started(g => g.Scene("Bad", () => { }), "Bad"));
        Assert.Contains("Screens/Bad.json tem um erro (linha 3)", error.Message);
    }

    /// <summary>
    /// Desenha uma tela com todos os tipos de peça, como no jogo. A imagem fica em
    /// %TEMP%\csharplab-tela-previa.png para conferir o visual.
    /// </summary>
    [Fact]
    public void Tela_desenhada_aparece_na_janela()
    {
        var layout = ScreenFile.Parse("""
            { "pieces": [
              { "type": "Box", "name": "Panel", "x": 620, "y": 24, "width": 316, "height": 130 },
              { "type": "Text", "name": "Title", "x": 40, "y": 30, "width": 560, "height": 50, "text": "A Torre do Goblin", "size": 32, "bold": true },
              { "type": "Text", "name": "Story", "x": 40, "y": 90, "width": 560, "height": 70, "text": "Um goblin aparece segurando uma faca enferrujada.", "size": 19 },
              { "type": "Bar", "name": "PlayerHealth", "x": 640, "y": 40, "width": 276, "height": 44, "text": "Henrique", "value": 72, "max": 100 },
              { "type": "Bar", "name": "EnemyHealth", "x": 640, "y": 96, "width": 276, "height": 44, "text": "Goblin", "value": 9, "max": 30, "color": "Red" },
              { "type": "Image", "name": "Goblin", "x": 660, "y": 180, "width": 240, "height": 200, "image": "goblin.png" },
              { "type": "Messages", "name": "Log", "x": 40, "y": 180, "width": 560, "height": 150 },
              { "type": "Input", "name": "Answer", "x": 40, "y": 345, "width": 420, "height": 88, "text": "Diga algo ao goblin:" },
              { "type": "Button", "name": "Attack", "x": 40, "y": 460, "width": 170, "height": 52, "text": "Atacar" },
              { "type": "Button", "name": "Potion", "x": 224, "y": 460, "width": 200, "height": 52, "text": "Beber poção", "color": "Green" },
              { "type": "Button", "name": "Flee", "x": 438, "y": 460, "width": 140, "height": 52, "text": "Fugir", "color": "Gray" }
            ] }
            """).Layout!;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var game = new Game("Teste");
                var window = new GameWindow(game);
                var screen = new Screen { Designed = new DesignedScene("Fight", layout) };
                screen.Items.Add(new TextItem("Você entra na torre.", null, false));
                screen.Items.Add(new TextItem("Você causou 7 de dano!", null, true));
                window.Show(screen);
                Render(window, "csharplab-tela-previa.png");
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

    /// <summary>As telas do exemplo "RPG com botões", como o jogo mostra (em %TEMP%\csharplab-exemplo-*.png).</summary>
    [Fact]
    public void Telas_do_exemplo_aparecem_na_janela()
    {
        var example = CSharpLab.Core.Projects.Examples.All.Single(e => e.Id == "RpgScreens");
        var screens = CSharpLab.Core.Projects.Examples.FilesOf(example).Where(f => f.Key.StartsWith("Screens/", StringComparison.Ordinal)).ToList();
        // E as telas do jogo novo (Arquivo → Novo projeto → Jogo com botões).
        screens.AddRange(CSharpLab.Core.Projects.GameKit.StarterScreens.Select(s => new KeyValuePair<string, string>("Screens/Novo-" + s.Key, s.Value)));
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                foreach (var (name, text) in screens)
                {
                    var window = new GameWindow(new Game("Teste"));
                    var screen = new Screen { Designed = new DesignedScene(Path.GetFileNameWithoutExtension(name), ScreenFile.Parse(text).Layout!) };
                    screen.Items.Add(new TextItem("Você comprou uma poção.", Color.Green, true));
                    window.Show(screen);
                    Render(window, $"csharplab-exemplo-{Path.GetFileNameWithoutExtension(name)}.png");
                }
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

    internal static void Render(Window window, string fileName)
    {
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        var size = new Size(GameWindow.StageWidth, GameWindow.StageHeight);
        var host = new Grid { Background = window.Background, Width = size.Width, Height = size.Height };
        host.Children.Add(content);
        host.Measure(size);
        host.Arrange(new Rect(size));
        host.UpdateLayout();
        // Os eventos Loaded (rolagem das mensagens) rodam antes da foto.
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
        host.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(host);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(Path.GetTempPath(), fileName));
        encoder.Save(file);
    }
}
