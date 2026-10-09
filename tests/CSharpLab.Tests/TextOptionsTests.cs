using System.Windows;
using CSharpLab.GameEngine;
using Color = CSharpLab.GameEngine.Color;
using Font = CSharpLab.GameEngine.Font;

namespace CSharpLab.Tests;

/// <summary>
/// Toda peça com letras (Texto, Botão, Barra, Campo de escrita, Mensagens e Lista) tem as mesmas opções de letra:
/// fonte, tamanho, negrito, itálico, alinhamento, sombra e cor da letra.
/// </summary>
public sealed class TextOptionsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "csharplab-letras", Guid.NewGuid().ToString("N")[..8]);

    public TextOptionsTests() => Directory.CreateDirectory(Path.Combine(_dir, "Screens"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private const string Styled = """
        { "pieces": [
          { "type": "Text", "name": "Title", "x": 40, "y": 16, "width": 880, "height": 60, "text": "Todas as letras", "size": 30, "font": "Fantasy", "align": "Center", "shadow": true, "color": "Gold" },
          { "type": "Button", "name": "Left", "x": 40, "y": 92, "width": 280, "height": 52, "text": "À esquerda", "align": "Left", "italic": true, "textShadow": true, "textColor": "Gold" },
          { "type": "Button", "name": "Plain", "x": 340, "y": 92, "width": 280, "height": 52, "text": "Botão normal", "bold": true },
          { "type": "Button", "name": "Right", "x": 640, "y": 92, "width": 280, "height": 52, "text": "À direita", "align": "Right", "font": "Book", "size": 19, "color": "Green" },
          { "type": "Bar", "name": "Health", "x": 40, "y": 164, "width": 420, "height": 52, "text": "Vida", "value": 70, "max": 100, "size": 20, "italic": true, "textColor": "Red", "font": "Book" },
          { "type": "Bar", "name": "Mana", "x": 500, "y": 164, "width": 420, "height": 52, "text": "Mana", "value": 30, "max": 50, "color": "Blue", "bold": false, "align": "Center" },
          { "type": "Bar", "name": "Rage", "x": 40, "y": 232, "width": 420, "height": 40, "text": "Fúria", "value": 9, "max": 10, "color": "Orange", "barText": "Inside", "align": "Right", "font": "Hand" },
          { "type": "Input", "name": "Answer", "x": 500, "y": 226, "width": 420, "height": 96, "text": "Qual é a senha?", "bold": true, "italic": true, "align": "Center", "textColor": "Purple", "size": 18 },
          { "type": "Messages", "name": "Log", "x": 40, "y": 332, "width": 420, "height": 150, "font": "Book", "italic": true, "textColor": "Gold", "align": "Right", "size": 15 },
          { "type": "List", "name": "Bag", "x": 500, "y": 340, "width": 420, "height": 140, "text": "A mochila está vazia.", "size": 22, "font": "Hand", "textColor": "Orange", "align": "Left", "shadow": true },
          { "type": "Text", "name": "Slot", "list": "Bag", "x": 0, "y": 0, "width": 100, "height": 30, "text": "item" }
        ] }
        """;

    [Fact]
    public void Opcoes_de_letra_vao_e_voltam_no_arquivo_em_toda_peca()
    {
        var result = ScreenFile.Parse(Styled);
        Assert.True(result.Success, result.Error);
        Assert.Empty(result.Warnings);
        var layout = result.Layout!;

        var left = layout.Find("Left")!;
        Assert.Equal((TextAlign.Left, true, true, Color.Gold), (left.LetterAlign, left.Italic, left.HasLetterShadow, left.TextColor));
        Assert.Equal(TextAlign.Center, layout.Find("Plain")!.LetterAlign);         // botão: centro por padrão
        Assert.True(layout.Find("Health")!.IsBold);                                // o nome da barra já vem em negrito
        Assert.False(layout.Find("Mana")!.IsBold);                                 // ... e dá para tirar
        Assert.Equal((20d, Font.Book), (layout.Find("Health")!.FontSize, layout.Find("Health")!.Font));
        Assert.Equal((true, true, Color.Purple), (layout.Find("Answer")!.IsBold, layout.Find("Answer")!.Italic == true, layout.Find("Answer")!.TextColor));
        Assert.Equal(TextAlign.Left, layout.Find("Bag")!.LetterAlign);             // lista: centro por padrão, aqui à esquerda

        var text = ScreenFile.Serialize(layout);
        Assert.Equal(text, ScreenFile.Serialize(ScreenFile.Parse(text).Layout!));
        Assert.Contains("\"textShadow\": true", text);
        Assert.Contains("\"bold\": false", text);                                 // o contrário do padrão da barra
        Assert.DoesNotContain("\"align\": \"Center\", \"bold\"", text);
        Assert.Contains("\"name\": \"Bag\"", text);
        Assert.Contains("\"align\": \"Left\"", text);                             // o contrário do padrão da lista
    }

    [Fact]
    public void Pelo_codigo_toda_peca_com_letras_aceita_as_opcoes()
    {
        File.WriteAllText(Path.Combine(_dir, "Screens", "Room.json"), Styled);
        var game = new Game("Teste") { Screens = new ScreenLibrary { Roots = [_dir] } };
        game.Scene("Room", () =>
        {
            foreach (var name in new[] { "Title", "Left", "Health", "Answer", "Log", "Bag" })
            {
                var piece = game.Find(name);
                piece.Font = Font.Hand;
                piece.Bold = true;
                piece.Italic = true;
            }
            game.Find("Health").TextColor = Color.Blue;
            game.Find("Log").TextColor = Color.Green;
            game.Find("Left").TextShadow = false;
        });
        game.Begin("Room", new NullView());

        var error = Assert.Throws<GameException>(() => game.Find("Title").TextColor = Color.Red);
        Assert.Contains("no Texto, a cor da letra é o Color", error.Message);
        error = Assert.Throws<GameException>(() => game.Find("Health").TextShadow = true);
        Assert.Contains("só o Botão tem TextShadow", error.Message);
    }

    private sealed class NullView : IGameView
    {
        public void Show(Screen screen) { }
    }

    /// <summary>Todas as peças com letras estilizadas, como o jogo desenha (foto em %TEMP%\csharplab-letras.png).</summary>
    [Fact]
    public void Letras_aparecem_em_todas_as_pecas()
    {
        File.WriteAllText(Path.Combine(_dir, "Screens", "Room.json"), Styled);
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var game = new Game("Teste") { Screens = new ScreenLibrary { Roots = [_dir] } };
                game.Scene("Room", () =>
                {
                    game.Write("Uma mensagem sem cor: fica na cor da peça.");
                    game.Write("Uma mensagem com cor própria.", Color.Red);
                });
                var window = new GameWindow(game);
                game.Begin("Room", window);
                DesignedSceneTests.Render(window, "csharplab-letras.png");
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
