using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CSharpLab.GameEngine;

namespace CSharpLab.Tests;

public sealed class KeyboardShortcutTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "csharplab-shortcuts", Guid.NewGuid().ToString("N"));

    public KeyboardShortcutTests() => Directory.CreateDirectory(Path.Combine(_dir, "Screens"));

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class View : IGameView
    {
        public Screen Last { get; private set; } = new();
        public Action? DuringPause { get; set; }
        public void Show(Screen screen) => Last = screen;
        public void Pause(double seconds) => DuringPause?.Invoke();
    }

    private Game Create(string json)
    {
        File.WriteAllText(Path.Combine(_dir, "Screens", "Menu.json"), json);
        return new Game("Atalhos") { Screens = new ScreenLibrary { Roots = [_dir] } };
    }

    private const string Menu = """
        { "pieces": [
          { "type": "Text", "name": "Count", "text": "0" },
          { "type": "Button", "name": "StartGame", "text": "Pressione Espaço", "shortcut": "Space" }
        ] }
        """;

    [Fact]
    public void Clique_e_espaco_executam_a_mesma_acao_e_atualizam_a_tela()
    {
        int count = 0;
        var game = Create(Menu);
        var view = new View();
        game.Scene("Menu", () =>
        {
            game.Find("Count").Text = count.ToString();
            game.Find("StartGame").OnClick(() => count++);
        });
        game.Begin("Menu", view);

        game.ClickPiece(game.Find("StartGame").Piece);
        Assert.True(game.PressShortcut("Space"));
        Assert.Equal(2, count);
        Assert.Equal("2", game.Find("Count").Text);
        Assert.False(game.PressShortcut("Enter"));
    }

    [Fact]
    public void Atalho_respeita_visibilidade_ativacao_e_cena_atual()
    {
        int count = 0;
        var game = Create(Menu);
        game.Scene("Menu", () => game.Find("StartGame").OnClick(() => { count++; game.GoTo("Play"); }));
        game.Scene("Play", () => game.Button("Voltar", () => game.GoTo("Menu")));
        game.Begin("Menu", new View());
        var button = game.Find("StartGame");
        button.Enabled = false;
        Assert.False(game.PressShortcut("Space"));
        button.Enabled = true;
        button.Visible = false;
        Assert.False(game.PressShortcut("Space"));
        button.Visible = true;
        Assert.True(game.PressShortcut("Space"));
        Assert.Equal("Play", game.CurrentScene);
        Assert.False(game.PressShortcut("Space"));
        Assert.Equal(1, count);
    }

    [Fact]
    public void Atalho_nao_interrompe_Wait_nem_roda_depois_de_fechar()
    {
        int count = 0;
        var game = Create(Menu);
        var view = new View { DuringPause = () => Assert.False(game.PressShortcut("Space")) };
        game.Scene("Menu", () => game.Find("StartGame").OnClick(() => { count++; game.Wait(0.1); }));
        game.Begin("Menu", view);
        Assert.True(game.PressShortcut("Space"));
        Assert.Equal(1, count);
        game.Close();
        Assert.False(game.PressShortcut("Space"));
    }

    [Fact]
    public void Atalho_de_cartao_escolhe_so_o_primeiro_botao_disponivel()
    {
        var game = Create("""
            { "pieces": [
              { "type": "List", "name": "Choices" },
              { "type": "Button", "name": "Pick", "list": "Choices", "shortcut": "Enter" }
            ] }
            """);
        int chosen = -1;
        game.Scene("Menu", () => game.Find("Choices").Show(new[] { 0, 1, 2 }, (card, value) =>
        {
            card.Find("Pick").Enabled = value != 0;
            card.Find("Pick").OnClick(() => chosen = value);
        }));
        game.Begin("Menu", new View());
        Assert.True(game.PressShortcut("Enter"));
        Assert.Equal(1, chosen);
        game.Find("Choices").Visible = false;
        Assert.False(game.PressShortcut("Enter"));
        game.Find("Choices").Visible = true;
        game.Find("Choices").Piece.Enabled = false;
        Assert.False(game.PressShortcut("Enter"));
    }

    [Fact]
    public void Shortcut_no_codigo_pode_trocar_e_remover_a_tecla()
    {
        int count = 0;
        var game = Create(Menu);
        game.Scene("Menu", () => game.Find("StartGame").OnClick(() => count++));
        game.Begin("Menu", new View());
        var button = game.Find("StartGame");
        button.Shortcut = " enter ";
        Assert.Equal("Enter", button.Shortcut);
        Assert.False(game.PressShortcut("Space"));
        Assert.True(game.PressShortcut("Enter"));
        button.Shortcut = "";
        Assert.False(game.PressShortcut("Enter"));
        Assert.Contains("Shortcut", Assert.Throws<GameException>(() => button.Shortcut = "Spcae").Message);
        Assert.Throws<GameException>(() => game.Find("Count").Shortcut = "Space");
        Assert.Equal(1, count);
    }

    [Theory]
    [InlineData("\" space \"", "Space")]
    [InlineData("\"Enter\"", "Enter")]
    [InlineData("null", null)]
    [InlineData("\"\"", null)]
    public void Atalho_e_preservado_no_JSON(string value, string? expected)
    {
        var result = ScreenFile.Parse("{ \"pieces\": [ { \"type\": \"Button\", \"name\": \"Play\", \"shortcut\": " + value + " } ] }");
        Assert.True(result.Success, result.Error);
        Assert.Empty(result.Warnings);
        var saved = ScreenFile.Serialize(result.Layout!);
        Assert.Equal(expected, ScreenFile.Parse(saved).Layout!.Find("Play")!.Shortcut);
        Assert.Null(ScreenFile.Parse("{ \"pieces\": [ { \"type\": \"Button\", \"name\": \"Old\" } ] }").Layout!.Find("Old")!.Shortcut);
    }

    [Theory]
    [InlineData("Button", "\"Spcae\"")]
    [InlineData("Button", "12")]
    [InlineData("Text", "\"Space\"")]
    public void Atalho_invalido_explica_o_erro(string type, string value)
    {
        var result = ScreenFile.Parse("{ \"pieces\": [ { \"type\": \"" + type + "\", \"name\": \"Play\", \"shortcut\": " + value + " } ] }");
        Assert.False(result.Success);
        Assert.Contains("shortcut", result.Error);
    }

    [Fact]
    public void Janela_recebe_espaco_e_ignora_repeticao_digitacao_e_modificadores() => OnSta(() =>
    {
        int count = 0;
        var game = Create(Menu);
        game.Scene("Menu", () => game.Find("StartGame").OnClick(() => count++));
        var window = new GameWindow(game) { Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        try
        {
            game.Begin("Menu", window);
            var input = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, Key.Space)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            window.RaiseEvent(input);
            Assert.True(input.Handled);
            Assert.Equal(1, count);
            Assert.False(window.HandleKey(Key.Space, ModifierKeys.None, isRepeat: true, isTyping: false));
            Assert.False(window.HandleKey(Key.Space, ModifierKeys.None, isRepeat: false, isTyping: true));
            Assert.False(window.HandleKey(Key.Space, ModifierKeys.Control, isRepeat: false, isTyping: false));
            Assert.False(window.HandleKey(Key.Space, ModifierKeys.Shift, isRepeat: false, isTyping: false));
            Assert.Equal(1, count);
            var typing = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, Key.Space)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent, Source = new TextBox() };
            window.RaiseEvent(typing);
            Assert.False(typing.Handled);
            Assert.Equal(1, count);

            game.Find("StartGame").Shortcut = "Enter";
            Assert.True(window.HandleKey(Key.Enter, ModifierKeys.None, false, false));
            game.Find("StartGame").Shortcut = "D1";
            Assert.True(window.HandleKey(Key.NumPad1, ModifierKeys.None, false, false));
            Assert.Equal(3, count);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Troca_de_cena_bloqueia_o_atalho_e_cenas_automaticas_mantem_os_numeros() => OnSta(() =>
    {
        int count = 0;
        var game = Create(Menu);
        game.Scene("Menu", () => game.Find("StartGame").OnClick(() => game.GoTo("Play")));
        game.Scene("Play", () => game.Button("Contar", () => count++));
        var window = new GameWindow(game) { Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        try
        {
            game.Begin("Menu", window);
            window.UpdateLayout();
            Assert.True(window.HandleKey(Key.Space, ModifierKeys.None, false, false));
            Assert.Equal("Play", game.CurrentScene);
            Assert.False(window.HandleKey(Key.D1, ModifierKeys.None, false, false));
            Assert.Equal(0, count);
        }
        finally { window.Close(); }

        // A cena automática é testada numa janela nova, sem depender do tempo da animação anterior.
        var automatic = new Game("Automático") { Screens = new ScreenLibrary { Roots = [_dir] } };
        automatic.Scene("Play", () => automatic.Button("Contar", () => count++));
        var automaticWindow = new GameWindow(automatic) { Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        automaticWindow.Show();
        try
        {
            automatic.Begin("Play", automaticWindow);
            Assert.True(automaticWindow.HandleKey(Key.D1, ModifierKeys.None, false, false));
            Assert.Equal(1, count);
        }
        finally { automaticWindow.Close(); }
    });

    private static void OnSta(Action test)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { test(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) Assert.Fail(failure.ToString());
    }
}
