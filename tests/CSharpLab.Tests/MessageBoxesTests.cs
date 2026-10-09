using CSharpLab.GameEngine;
using Color = CSharpLab.GameEngine.Color;

namespace CSharpLab.Tests;

/// <summary>
/// Várias caixas de Mensagens na mesma tela: game.Find("Nome").Write escreve só numa, e a caixa com
/// "Recebe o game.Write" desligado mostra só as dela (uma caixa para o jogador, outra para o inimigo).
/// </summary>
public sealed class MessageBoxesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "csharplab-caixas", Guid.NewGuid().ToString("N")[..8]);

    public MessageBoxesTests() => Directory.CreateDirectory(Path.Combine(_dir, "Screens"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    // A tela de combate do projeto Aprendizado do Henrique (com a caixa do goblin só com as mensagens dela).
    private const string Combat = """
        { "pieces": [
          { "type": "Box", "name": "Box1", "x": 16, "y": 384, "width": 904, "height": 116, "shadow": true, "color": "#000000", "shade": "Dark", "opacity": 70, "corner": "Square" },
          { "type": "Bar", "name": "HpHeroi", "x": 32, "y": 396, "width": 232, "height": 36, "text": "Vida", "font": "Retro", "color": "#A31F1F", "barText": "Inside", "value": 20, "max": 20 },
          { "type": "Bar", "name": "GoblinHP", "x": 632, "y": 32, "width": 312, "height": 48, "text": "Goblin", "font": "Retro", "color": "#A31F1F", "value": 15, "max": 15 },
          { "type": "Button", "name": "AtaqueSimples", "x": 440, "y": 396, "width": 176, "height": 46, "text": "Ataque", "size": 17, "font": "Retro", "color": "Brown", "corner": "Square", "style": "Outline" },
          { "type": "Button", "name": "PassarTurno", "x": 640, "y": 447, "width": 176, "height": 46, "text": "PASSAR TURNO", "color": "#FFFFFF", "corner": "Square", "style": "Outline", "textColor": "White" },
          { "type": "Messages", "name": "AcoesJogador", "x": 40, "y": 56, "width": 352, "height": 296, "size": 17, "font": "Retro" },
          { "type": "Messages", "name": "AcoesGoblin", "x": 480, "y": 112, "width": 440, "height": 220, "size": 17, "font": "Retro", "gameWrite": false }
        ] }
        """;

    private sealed class FakeView : IGameView
    {
        public Screen Last { get; private set; } = new();
        public void Show(Screen screen) => Last = screen;
        public Piece Piece(string name) => Last.Designed!.Layout.Find(name)!;
        public IReadOnlyList<MessageLine> Box(string name) => Last.MessagesByPiece![name];
    }

    private (Game Game, FakeView View) Started(Action<Game> setup)
    {
        File.WriteAllText(Path.Combine(_dir, "Screens", "CenaCombate.json"), Combat);
        var game = new Game("Teste") { Screens = new ScreenLibrary { Roots = [_dir] } };
        setup(game);
        var view = new FakeView();
        game.Begin("CenaCombate", view);
        return (game, view);
    }

    [Fact]
    public void Cada_caixa_mostra_as_suas_mensagens()
    {
        int hpGoblin = 15, hpHeroi = 20, turno = 0;
        var (game, view) = Started(g => g.Scene("CenaCombate", () =>
        {
            g.Find("AtaqueSimples").OnClick(() =>
            {
                turno++;
                hpGoblin -= 4;
                g.Find("AcoesJogador").Write($"Goblin recebeu 4 de dano.");
                g.Wait(0.5);
                hpHeroi -= 2;
                g.Find("AcoesGoblin").Write($"Goblin causou 2 de dano.", Color.Red);
                g.Write($"Turno {turno}.");   // geral: só nas caixas que recebem o game.Write
            });
            g.Find("PassarTurno").OnClick(() => g.Find("AcoesGoblin").Write("Goblin rosna."));
        }));

        game.ClickPiece(view.Piece("AtaqueSimples"));
        Assert.Equal(["Goblin recebeu 4 de dano.", "Turno 1."], view.Box("AcoesJogador").Select(m => m.Text));
        Assert.Equal(["Goblin causou 2 de dano."], view.Box("AcoesGoblin").Select(m => m.Text));
        Assert.Equal(Color.Red, view.Box("AcoesGoblin")[0].Color);
        Assert.True(view.Box("AcoesGoblin")[0].IsNews);

        // Um clique que só escreve na caixa do goblin: lá a nova fica destacada; na do jogador, tudo apagadinho.
        game.ClickPiece(view.Piece("PassarTurno"));
        Assert.Equal([false, true], view.Box("AcoesGoblin").Select(m => m.IsNews));
        Assert.All(view.Box("AcoesJogador"), m => Assert.True(m.IsOld));
        // O game.Write também não vai para a caixa do goblin.
        Assert.DoesNotContain(view.Box("AcoesGoblin"), m => m.Text.StartsWith("Turno"));
    }

    [Fact]
    public void Clear_da_caixa_apaga_so_ela_e_erros_explicam()
    {
        var (game, view) = Started(g => g.Scene("CenaCombate", () =>
        {
            g.Find("AtaqueSimples").OnClick(() =>
            {
                g.Find("AcoesJogador").Write("Você atacou.");
                g.Find("AcoesGoblin").Write("O goblin atacou.");
            });
            g.Find("PassarTurno").OnClick(() => g.Find("AcoesGoblin").Clear());
        }));
        game.ClickPiece(view.Piece("AtaqueSimples"));
        game.ClickPiece(view.Piece("PassarTurno"));
        Assert.Empty(view.Box("AcoesGoblin"));
        Assert.Equal(["Você atacou."], view.Box("AcoesJogador").Select(m => m.Text));

        var error = Assert.Throws<GameException>(() => Started(g => g.Scene("CenaCombate", () => g.Find("GoblinHP").Write("x"))));
        Assert.Contains("só a peça Mensagens tem Write", error.Message);

        // "gameWrite": false vai e volta no arquivo.
        var layout = ScreenFile.Parse(Combat).Layout!;
        Assert.False(layout.Find("AcoesGoblin")!.GameWrite);
        Assert.Contains("\"gameWrite\": false", ScreenFile.Serialize(layout));
    }

    /// <summary>A tela de combate com as duas caixas, como o jogo mostra (%TEMP%\csharplab-duas-caixas.png).</summary>
    [Fact]
    public void Duas_caixas_aparecem_na_janela()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                int hpGoblin = 15;
                var (game, view) = Started(g => g.Scene("CenaCombate", () =>
                {
                    g.Find("GoblinHP").Value = hpGoblin;
                    g.Find("AtaqueSimples").OnClick(() =>
                    {
                        int dano = 3;
                        hpGoblin -= dano;
                        g.Find("AcoesJogador").Write($"Goblin recebeu {dano} de dano.");
                        g.Find("AcoesGoblin").Write("Goblin causou 2 de dano.", Color.Red);
                    });
                }));
                game.ClickPiece(view.Piece("AtaqueSimples"));
                game.ClickPiece(view.Piece("AtaqueSimples"));
                var window = new GameWindow(game);
                window.Show(view.Last);
                DesignedSceneTests.Render(window, "csharplab-duas-caixas.png");
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
