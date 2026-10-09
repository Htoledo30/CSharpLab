using CSharpLab.GameEngine;
using Color = CSharpLab.GameEngine.Color;
using Font = CSharpLab.GameEngine.Font;

namespace CSharpLab.Tests;

/// <summary>
/// Personalização: qualquer cor (Color.Hex), cores com nome novas, fontes novas, estilos de botão
/// (degradê, suave, cor da letra, cantos, sombra) e de barra (brilhante, em blocos, texto dentro).
/// </summary>
public sealed class PersonalizationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "csharplab-personalizar", Guid.NewGuid().ToString("N")[..8]);

    public PersonalizationTests() => Directory.CreateDirectory(Path.Combine(_dir, "Screens"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private sealed class FakeView : IGameView
    {
        public Screen? Last { get; private set; }
        public void Show(Screen screen) => Last = screen;
        public void Pause(double seconds) { }
    }

    [Fact]
    public void Cor_propria_pelo_codigo_e_pelo_nome()
    {
        Assert.Equal("#8A2BE2", Color.Hex("#8a2be2").ToString());
        Assert.Equal(Color.Hex("#8A2BE2"), Color.Rgb(138, 43, 226));
        Assert.Equal(Color.Hex("#FFF"), Color.Hex("#FFFFFF"));
        Assert.Equal("Red", Color.Red.ToString());
        Assert.True(Color.Red == Color.Red);
        Assert.True(Color.Red != Color.Hex("#FF0000"));   // vermelho do tema não é o mesmo que um vermelho escolhido
        Assert.Contains("#8A2BE2", Assert.Throws<GameException>(() => Color.Hex("roxo")).Message);
        Assert.Contains("0 a 255", Assert.Throws<GameException>(() => Color.Rgb(300, 0, 0)).Message);

        Assert.True(Color.TryParse("pink", out var pink) && pink == Color.Pink);
        Assert.True(Color.TryParse("#3fa9f5", out var custom) && custom == Color.Hex("#3FA9F5"));
        Assert.False(Color.TryParse("Yellow", out _));
        Assert.Equal(12, Color.Named.Count);
    }

    [Fact]
    public void Arquivo_da_tela_guarda_os_estilos_novos()
    {
        const string json = """
            { "pieces": [
              { "type": "Button", "name": "Buy", "x": 0, "y": 0, "width": 200, "height": 50, "text": "Comprar", "color": "#2DBE60", "textColor": "Gold", "style": "Gradient", "corner": "Circle", "bold": true, "shadow": true },
              { "type": "Bar", "name": "Mana", "x": 0, "y": 60, "width": 300, "height": 30, "text": "Mana", "value": 3, "max": 10, "color": "#3FA9F5", "barStyle": "Shine", "barText": "Inside", "corner": "Square", "font": "Tech" },
              { "type": "Box", "name": "Card", "x": 0, "y": 100, "width": 100, "height": 100, "color": "Brown", "shadow": true },
              { "type": "Text", "name": "Title", "x": 0, "y": 210, "width": 300, "height": 50, "text": "GAME OVER", "font": "Strong", "color": "Cyan" }
            ] }
            """;
        var parsed = ScreenFile.Parse(json);
        Assert.True(parsed.Success, parsed.Error);
        Assert.Empty(parsed.Warnings);
        var layout = parsed.Layout!;
        var buy = layout.Find("Buy")!;
        Assert.Equal(Color.Hex("#2DBE60"), buy.Color);
        Assert.Equal(Color.Gold, buy.TextColor);
        Assert.Equal(ButtonStyle.Gradient, buy.Style);
        Assert.Equal(Corner.Circle, buy.Corner);
        var mana = layout.Find("Mana")!;
        Assert.Equal(BarStyle.Shine, mana.BarStyle);
        Assert.Equal(BarText.Inside, mana.BarText);
        Assert.Equal(Font.Tech, mana.Font);
        Assert.Equal(Font.Strong, layout.Find("Title")!.Font);

        // Gravar e ler de novo dá o mesmo arquivo.
        var text = ScreenFile.Serialize(layout);
        Assert.Contains("\"color\": \"#2DBE60\"", text);
        Assert.Contains("\"textColor\": \"Gold\"", text);
        Assert.Contains("\"barStyle\": \"Shine\"", text);
        Assert.Contains("\"barText\": \"Inside\"", text);
        Assert.Equal(text, ScreenFile.Serialize(ScreenFile.Parse(text).Layout!));

        // Cor que não existe: o erro lista as cores e lembra do código.
        var bad = ScreenFile.Parse("""{ "pieces": [ { "type": "Button", "name": "A", "textColor": "Yellow" } ] }""");
        Assert.Contains("\"textColor\" precisa ser uma destas cores", bad.Error);
        Assert.Contains("#8A2BE2", bad.Error);
    }

    [Fact]
    public void Codigo_muda_os_estilos_novos_e_explica_onde_nao_vale()
    {
        File.WriteAllText(Path.Combine(_dir, "Screens", "Hud.json"), """
            { "pieces": [
              { "type": "Bar", "name": "Mana", "x": 0, "y": 0, "width": 300, "height": 44, "text": "Mana", "value": 5, "max": 10 },
              { "type": "Button", "name": "Cast", "x": 0, "y": 60, "width": 200, "height": 50, "text": "Magia" },
              { "type": "Text", "name": "Info", "x": 0, "y": 120, "width": 200, "height": 50, "text": "Oi" }
            ] }
            """);
        var game = new Game("Teste") { Screens = new ScreenLibrary { Roots = [_dir] } };
        game.Scene("Hud", () =>
        {
            var mana = game.Find("Mana");
            mana.Color = Color.Hex("#3FA9F5");
            mana.BarStyle = BarStyle.Blocks;
            mana.BarText = BarText.None;
            mana.Corner = Corner.Square;
            mana.Font = Font.Retro;
            var cast = game.Find("Cast");
            cast.Style = ButtonStyle.Soft;
            cast.TextColor = Color.Pink;
            cast.Bold = true;
            cast.Shadow = true;
            cast.Corner = Corner.Circle;
        });
        var view = new FakeView();
        game.Begin("Hud", view);
        var layout = view.Last!.Designed!.Layout;
        var bar = layout.Find("Mana")!;
        Assert.Equal((BarStyle?)BarStyle.Blocks, bar.BarStyle);
        Assert.Equal((BarText?)BarText.None, bar.BarText);
        Assert.Equal(Color.Hex("#3FA9F5"), bar.Color);
        var button = layout.Find("Cast")!;
        Assert.Equal((ButtonStyle?)ButtonStyle.Soft, button.Style);
        Assert.Equal(Color.Pink, button.TextColor);
        Assert.True(button.Shadow);

        // Texto não tem TextColor (nele a cor da letra é o Color): o erro explica.
        var game2 = new Game("Teste") { Screens = new ScreenLibrary { Roots = [_dir] } };
        game2.Scene("Hud", () => game2.Find("Info").TextColor = Color.Red);
        var error = Assert.Throws<GameException>(() => game2.Begin("Hud", new FakeView()));
        Assert.Contains("no Texto, a cor da letra é o Color", error.Message);
    }

    /// <summary>
    /// Os estilos novos desenhados de verdade. A imagem fica em %TEMP%\csharplab-personalizar.png para conferir o visual.
    /// </summary>
    [Fact]
    public void Estilos_novos_aparecem_na_janela()
    {
        const string json = """
            { "pieces": [
              { "type": "Text", "name": "Title", "x": 24, "y": 14, "width": 900, "height": 60, "text": "VITÓRIA!", "size": 44, "font": "Strong", "color": "Gold", "shadow": true },
              { "type": "Text", "name": "F1", "x": 24, "y": 78, "width": 300, "height": 34, "text": "Clássica: era uma vez", "font": "Classic" },
              { "type": "Text", "name": "F2", "x": 330, "y": 78, "width": 300, "height": 34, "text": "Elegante: Reino de Aurora", "font": "Elegant" },
              { "type": "Text", "name": "F3", "x": 636, "y": 78, "width": 300, "height": 34, "text": "Divertida: oi, herói!", "font": "Fun" },
              { "type": "Text", "name": "F4", "x": 24, "y": 116, "width": 300, "height": 34, "text": "Retrô: > INICIAR_", "font": "Retro", "color": "#39FF7A" },
              { "type": "Text", "name": "F5", "x": 330, "y": 116, "width": 300, "height": 34, "text": "Técnica: PLACAR 0120", "font": "Tech", "color": "Cyan" },
              { "type": "Text", "name": "F6", "x": 636, "y": 116, "width": 300, "height": 34, "text": "Rosa, marrom e preto", "color": "Pink" },
              { "type": "Bar", "name": "Health", "x": 24, "y": 170, "width": 290, "height": 44, "text": "❤ Vida", "value": 70, "max": 100, "color": "Red" },
              { "type": "Bar", "name": "Mana", "x": 330, "y": 170, "width": 290, "height": 44, "text": "✦ Mana", "value": 45, "max": 100, "color": "#3FA9F5", "barStyle": "Shine" },
              { "type": "Bar", "name": "Energy", "x": 636, "y": 170, "width": 290, "height": 44, "text": "⚡ Energia", "value": 7, "max": 10, "color": "Gold", "barStyle": "Blocks" },
              { "type": "Bar", "name": "Boss", "x": 24, "y": 232, "width": 600, "height": 34, "text": "REI GOBLIN", "value": 320, "max": 500, "color": "Purple", "barStyle": "Shine", "barText": "Inside", "font": "Strong" },
              { "type": "Bar", "name": "Hearts", "x": 636, "y": 232, "width": 200, "height": 22, "text": "", "value": 3, "max": 5, "color": "Pink", "barStyle": "Blocks", "barText": "None", "corner": "Square" },
              { "type": "Bar", "name": "Xp", "x": 24, "y": 280, "width": 600, "height": 12, "text": "", "value": 60, "max": 100, "color": "#B04CF5", "barText": "None" },
              { "type": "Button", "name": "B1", "x": 24, "y": 320, "width": 170, "height": 52, "text": "Cheio", "color": "Red" },
              { "type": "Button", "name": "B2", "x": 206, "y": 320, "width": 170, "height": 52, "text": "Degradê", "color": "Green", "style": "Gradient", "bold": true, "shadow": true },
              { "type": "Button", "name": "B3", "x": 388, "y": 320, "width": 170, "height": 52, "text": "Suave", "color": "Purple", "style": "Soft" },
              { "type": "Button", "name": "B4", "x": 570, "y": 320, "width": 170, "height": 52, "text": "Pílula", "color": "#FF8C1A", "corner": "Circle", "textColor": "Black", "bold": true },
              { "type": "Button", "name": "B5", "x": 752, "y": 320, "width": 170, "height": 52, "text": "Retos", "color": "Cyan", "style": "Gradient", "corner": "Square", "font": "Retro" },
              { "type": "Box", "name": "Card", "x": 24, "y": 400, "width": 280, "height": 110, "color": "Brown", "opacity": 90, "shadow": true },
              { "type": "Text", "name": "OnCard", "x": 40, "y": 420, "width": 250, "height": 70, "text": "Caixa marrom com sombra", "size": 18 },
              { "type": "Box", "name": "Night", "x": 330, "y": 400, "width": 280, "height": 110, "color": "Black", "opacity": 100, "border": true }
            ] }
            """;
        File.WriteAllText(Path.Combine(_dir, "Screens", "Look.json"), json);
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var game = new Game("Teste") { Screens = new ScreenLibrary { Roots = [_dir] } };
                game.Scene("Look", () => { });
                var view = new FakeView();
                game.Begin("Look", view);
                var window = new GameWindow(game);
                window.Show(view.Last!);
                DesignedSceneTests.Render(window, "csharplab-personalizar.png");
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
