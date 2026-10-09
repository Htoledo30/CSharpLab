using System.Windows.Input;
using System.Windows.Threading;
using CSharpLab.GameEngine;
using Color = CSharpLab.GameEngine.Color;

namespace CSharpLab.Tests;

/// <summary>
/// Comandos que esperam o jogador no meio do clique (Pause, Read, Choose), as teclas da cena (OnKey)
/// e o fim do jogo (Close).
/// </summary>
public sealed class InputTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "csharplab-entrada", Guid.NewGuid().ToString("N")[..8]);

    public InputTests() => Directory.CreateDirectory(Path.Combine(_dir, "Screens"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    /// <summary>Um jogador de mentira: responde as perguntas e guarda o que viu.</summary>
    private sealed class FakeView : IGameView
    {
        public List<Screen> Shown { get; } = [];
        public List<List<string>> MessagesShown { get; } = [];
        public List<string> Waits { get; } = [];
        public List<string> Questions { get; } = [];
        public string Answer { get; set; } = "";
        public int Choice { get; set; }
        public bool Closed { get; private set; }
        public Screen Last => Shown[^1];

        public void Show(Screen screen)
        {
            Shown.Add(screen);
            MessagesShown.Add(screen.Messages.Select(m => m.Text).ToList());
        }

        public void WaitForPlayer(string button) => Waits.Add(button);

        public string ReadAnswer(string question)
        {
            Questions.Add(question);
            return Answer;
        }

        public int ChooseOption(string question, IReadOnlyList<string> options)
        {
            Questions.Add(question + " " + string.Join("/", options));
            return Choice;
        }

        public void CloseGame() => Closed = true;

        public Piece Piece(string name) => Last.Designed!.Layout.Find(name)!;
    }

    private const string Room = """
        { "pieces": [
          { "type": "Messages", "name": "Log", "x": 40, "y": 40, "width": 500, "height": 200 },
          { "type": "Button", "name": "Go", "x": 40, "y": 460, "width": 180, "height": 52, "text": "Ir" },
          { "type": "Text", "name": "Pos", "x": 600, "y": 40, "width": 200, "height": 40, "text": "x" }
        ] }
        """;

    private (Game Game, FakeView View) Started(Action<Game> setup, string first = "Room")
    {
        foreach (var scene in new[] { "Room", "Hall" })
            File.WriteAllText(Path.Combine(_dir, "Screens", scene + ".json"), Room);
        var game = new Game("Teste") { Screens = new ScreenLibrary { Roots = [_dir] } };
        setup(game);
        var view = new FakeView();
        game.Begin(first, view);
        return (game, view);
    }

    [Fact]
    public void Pause_mostra_a_mensagem_espera_o_jogador_e_depois_continua()
    {
        var (game, view) = Started(g => g.Scene("Room", () => g.Find("Go").OnClick(() =>
        {
            g.Write("Você encontrou uma chave!");
            g.Pause();
            g.Clear();
            g.Write("A porta se abre.");
        })));

        game.ClickPiece(view.Piece("Go"));
        Assert.Equal(["Continuar"], view.Waits);
        // Antes da pausa o jogador vê a mensagem; depois do Clear, só a nova.
        Assert.Equal(["Você encontrou uma chave!"], view.MessagesShown[^2]);
        Assert.Equal(["A porta se abre."], view.MessagesShown[^1]);
    }

    [Fact]
    public void Read_devolve_o_que_o_jogador_escreveu_e_Choose_a_opcao()
    {
        int gold = 20;
        string? choice = null;
        var (game, view) = Started(g => g.Scene("Room", () => g.Find("Go").OnClick(() =>
        {
            string answer = g.Read("Quanto você aposta?");
            if (int.TryParse(answer, out int bet)) gold -= bet;
            choice = g.Choose("Abrir o baú?", "Sim", "Não");
        })));
        view.Answer = "  12 ";
        view.Choice = 1;

        game.ClickPiece(view.Piece("Go"));
        Assert.Equal(8, gold);
        Assert.Equal("Não", choice);
        Assert.Equal(["Quanto você aposta?", "Abrir o baú? Sim/Não"], view.Questions);
    }

    [Fact]
    public void Comandos_de_esperar_fora_de_um_clique_explicam_onde_usar()
    {
        var error = Assert.Throws<GameException>(() => Started(g => g.Scene("Room", () => g.Pause())));
        Assert.Contains("game.Pause funciona dentro de um clique", error.Message);

        error = Assert.Throws<GameException>(() => Started(g => g.Scene("Room", () => g.Read("Nome?"))));
        Assert.Contains("game.Read funciona dentro de um clique", error.Message);

        var (game, view) = Started(g => g.Scene("Room", () => g.Find("Go").OnClick(() => g.Choose("Só uma?", "Sim"))));
        error = Assert.Throws<GameException>(() => game.ClickPiece(view.Piece("Go")));
        Assert.Contains("de 2 a 6 opções", error.Message);

        // OnKey dentro de um botão: o erro aparece no clique.
        (game, view) = Started(g => g.Scene("Room", () => g.Find("Go").OnClick(() => g.OnKey(_ => { }))));
        error = Assert.Throws<GameException>(() => game.ClickPiece(view.Piece("Go")));
        Assert.Contains("game.OnKey fica direto dentro da cena", error.Message);
    }

    [Fact]
    public void OnKey_recebe_as_teclas_da_cena_como_ConsoleKey()
    {
        int x = 0;
        var keys = new List<ConsoleKey>();
        var (game, view) = Started(g =>
        {
            g.Scene("Room", () =>
            {
                g.Find("Pos").Text = $"x = {x}";
                g.OnKey(key =>
                {
                    keys.Add(key);
                    if (key == ConsoleKey.D || key == ConsoleKey.RightArrow) x++;
                    else if (key == ConsoleKey.A) x--;
                    else if (key == ConsoleKey.Escape) g.GoTo("Hall");
                });
            });
            g.Scene("Hall", () => { });
        });

        Assert.True(game.PressKey(Key.D));
        Assert.True(game.PressKey(Key.Right));
        Assert.True(game.PressKey(Key.A));
        Assert.Equal(1, x);
        Assert.Equal("x = 1", view.Last.Designed!.Find("Pos")!.Text);   // a cena desenhou de novo
        Assert.False(game.PressKey(Key.LeftShift));                       // tecla que o jogo não usa

        Assert.True(game.PressKey(Key.Escape));
        Assert.Equal("Hall", game.CurrentScene);
        Assert.False(game.PressKey(Key.D));                               // a cena nova não tem OnKey
        Assert.Equal([ConsoleKey.D, ConsoleKey.RightArrow, ConsoleKey.A, ConsoleKey.Escape], keys);
    }

    [Theory]
    [InlineData(Key.W, ConsoleKey.W)]
    [InlineData(Key.D7, ConsoleKey.D7)]
    [InlineData(Key.NumPad3, ConsoleKey.NumPad3)]
    [InlineData(Key.Up, ConsoleKey.UpArrow)]
    [InlineData(Key.Space, ConsoleKey.Spacebar)]
    [InlineData(Key.Enter, ConsoleKey.Enter)]
    [InlineData(Key.F5, ConsoleKey.F5)]
    public void Teclas_da_janela_viram_os_nomes_do_terminal(Key key, ConsoleKey expected) =>
        Assert.Equal(expected, Game.ToConsoleKey(key));

    [Fact]
    public void Close_fecha_a_janela_e_o_resto_do_clique_nao_roda()
    {
        int after = 0;
        var (game, view) = Started(g => g.Scene("Room", () => g.Find("Go").OnClick(() =>
        {
            g.Write("Até logo!", Color.Gold);
            g.Close();
            after++;
        })));

        game.ClickPiece(view.Piece("Go"));
        Assert.True(view.Closed);
        Assert.Equal(0, after);
        int draws = view.Shown.Count;
        game.ClickPiece(view.Piece("Go"));   // fechado: nada mais acontece
        Assert.Equal(draws, view.Shown.Count);
    }

    /// <summary>
    /// A janela de verdade (fora da tela): o Pause espera até o "jogador" continuar, o Choose devolve
    /// o botão escolhido e o Read a resposta escrita. Nada fica cobrindo a tela depois.
    /// </summary>
    [Fact]
    public void Janela_espera_o_jogador_no_Pause_Read_e_Choose()
    {
        foreach (var scene in new[] { "Room" })
            File.WriteAllText(Path.Combine(_dir, "Screens", scene + ".json"), Room);
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var steps = new List<string>();
                var game = new Game("Teste") { Screens = new ScreenLibrary { Roots = [_dir] } };
                game.Scene("Room", () => game.Find("Go").OnClick(() =>
                {
                    steps.Add("antes");
                    game.Pause();
                    steps.Add("depois do Pause");
                    steps.Add(game.Choose("Qual porta?", "Esquerda", "Direita"));
                    steps.Add(game.Read("Senha?"));
                }));
                var window = new GameWindow(game) { Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
                window.Show();
                game.Begin("Room", window);
                window.UpdateLayout();

                // O "jogador" responde cada pedido um pouco depois de ele aparecer.
                var answers = new Queue<(int Choice, string Text)>([(0, ""), (1, ""), (0, "abacaxi")]);
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
                timer.Tick += (_, _) =>
                {
                    if (window.HasPrompt && answers.Count > 0)
                    {
                        Photo(window, $"csharplab-pedido-{3 - answers.Count + 1}.png");
                        var (choice, text) = answers.Dequeue();
                        window.SubmitPrompt(choice, text);
                    }
                };
                timer.Start();
                game.ClickPiece(game.Find("Go").Piece);
                timer.Stop();

                Assert.Equal(["antes", "depois do Pause", "Direita", "abacaxi"], steps);
                Assert.False(window.HasPrompt);
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

    /// <summary>Foto do palco do jogo como está agora (em %TEMP%, para conferir o visual dos pedidos).</summary>
    private static void Photo(GameWindow window, string fileName)
    {
        var stage = ((System.Windows.Controls.Viewbox)window.Content).Child;
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)GameWindow.StageWidth, (int)GameWindow.StageHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(stage);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(Path.GetTempPath(), fileName));
        encoder.Save(file);
    }
}
