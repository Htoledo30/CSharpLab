using System.Windows.Controls;
using System.Windows.Media;
using CSharpLab.Core.Projects;
using CSharpLab.GameEngine;
using Color = CSharpLab.GameEngine.Color;

namespace CSharpLab.Tests;

/// <summary>Temas do jogo (GameStyle.json): o arquivo, o que o tema muda e o que continua sendo da peça.</summary>
public sealed class ThemeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "csharplab-temas", Guid.NewGuid().ToString("N")[..8]);

    public ThemeTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
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
        Assert.Null(failure?.ToString());
    }

    [Fact]
    public void Arquivo_do_tema_le_grava_e_explica_erros()
    {
        Assert.Equal((ThemeName.Fantasy, (string?)null), GameStyle.Parse("""{ "format": 1, "theme": "Fantasy" }"""));
        Assert.Equal(ThemeName.Book, GameStyle.Parse("""{ "theme": "book" }""").Theme);   // maiúsculas tanto faz
        Assert.Equal(ThemeName.Classic, GameStyle.Parse("""{ "format": 1 }""").Theme);   // sem tema: o clássico
        Assert.Equal(ThemeName.Modern, GameStyle.Parse(GameStyle.Serialize(ThemeName.Modern)).Theme);

        var (theme, error) = GameStyle.Parse("""{ "theme": "Pirata" }""");
        Assert.Null(theme);
        Assert.Contains("Classic, Fantasy, Book, Modern", error);
        Assert.Contains("não está escrito certo", GameStyle.Parse("""{ "theme": "Book" """).Error);

        // Sem arquivo: Clássico. Com arquivo na pasta: o tema dele.
        Assert.Equal(ThemeName.Classic, GameStyle.Load([_dir], out var none).Name);
        Assert.Null(none);
        File.WriteAllText(Path.Combine(_dir, GameStyle.FileName), GameStyle.Serialize(ThemeName.Book));
        Assert.Equal(ThemeName.Book, GameStyle.Load([Path.Combine(_dir, "nao-existe"), _dir], out _).Name);
        File.WriteAllText(Path.Combine(_dir, GameStyle.FileName), """{ "theme": "Pirata" }""");
        Assert.Equal(ThemeName.Classic, GameStyle.Load([_dir], out var problem).Name);
        Assert.Contains("Pirata", problem);
    }

    [Fact]
    public void Jogo_com_tema_errado_para_no_Start_explicando()
    {
        File.WriteAllText(Path.Combine(_dir, GameStyle.FileName), """{ "theme": "Pirata" }""");
        var game = new Game("T") { Screens = new ScreenLibrary { Roots = [_dir] } };
        game.Scene("Start", () => game.Write("Oi"));
        var error = Assert.Throws<GameException>(() => game.Begin("Start", new NullView()));
        Assert.Contains("Pirata", error.Message);
    }

    private sealed class NullView : IGameView
    {
        public void Show(Screen screen) { }
    }

    [Fact]
    public void Tema_muda_o_padrao_e_o_que_a_peca_escolheu_continua_valendo() => OnSta(() =>
    {
        var title = new Piece { Type = PieceType.Text, Name = "Title", Width = 400, Height = 60, Text = "A Coroa", Size = 34 };
        var story = new Piece { Type = PieceType.Text, Name = "Story", Width = 400, Height = 60, Text = "Era uma vez", Size = 18 };
        var chosen = new Piece { Type = PieceType.Text, Name = "Note", Width = 400, Height = 60, Text = "Bilhete", Size = 34, Font = Font.Hand, Color = Color.Red };

        Theme.Current = Look.Fantasy;
        Assert.Equal(Font.Fantasy, Theme.FontFor(title));   // título: fonte de título do tema
        Assert.Equal(Font.Book, Theme.FontFor(story));      // texto: fonte de texto do tema
        Assert.Equal(Font.Hand, Theme.FontFor(chosen));     // escolhida na peça: vale a da peça

        Theme.Current = Look.Book;
        var text = (TextBlock)((Border)ScreenRenderer.Create(story, new RenderContext())).Child;
        Assert.Equal(Look.Book.Text, ((SolidColorBrush)text.Foreground).Color);   // tinta escura no papel
        var red = (TextBlock)((Border)ScreenRenderer.Create(chosen, new RenderContext())).Child;
        Assert.Equal(Look.Book.Colors[Color.Red], ((SolidColorBrush)red.Foreground).Color);   // o vermelho do tema claro

        // Cada thread tem o seu tema: outra thread continua no Clássico.
        Look? other = null;
        var thread = new Thread(() => other = Theme.Current);
        thread.Start();
        thread.Join();
        Assert.Equal(ThemeName.Classic, other!.Name);
        Theme.Current = Look.Classic;
    });

    /// <summary>A tela da vila do jogo novo e a luta da Coroa Perdida nos quatro temas (em %TEMP%\csharplab-tema-*.png).</summary>
    [Fact]
    public void Telas_nos_quatro_temas() => OnSta(() =>
    {
        var village = ScreenFile.Parse(GameKit.StarterScreens["Start.json"]).Layout!;
        var fight = ScreenFile.Parse(Examples.FilesOf(Examples.All.Single(e => e.Id == "RpgAdventure"))["Screens/Fight.json"]).Layout!;
        foreach (var look in Look.All)
        {
            foreach (var (name, layout) in new[] { ("vila", village), ("luta", fight) })
            {
                Theme.Current = look;
                var window = new GameWindow(new Game("Teste"));
                var screen = new Screen { Designed = new DesignedScene(name, layout.Clone()) };
                screen.Items.Add(new TextItem("Você causou 7 de dano!", Color.Green, true));
                window.Show(screen);
                DesignedSceneTests.Render(window, $"csharplab-tema-{look.Name}-{name}.png");
                window.Close();
            }
        }
        Theme.Current = Look.Classic;
    });
}
