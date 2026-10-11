using CSharpLab.Core.Language;
using CSharpLab.GameEngine;
using Microsoft.CodeAnalysis.CSharp;

namespace CSharpLab.Tests;

/// <summary>
/// "Jogar esta cena" (Ctrl+F5) com valores escolhidos: o game.Test da cena roda antes dela,
/// depois do OnEnter, e só quando o jogo começa nela por esse caminho.
/// </summary>
public sealed class SceneTestTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "csharplab-testar-cena", Guid.NewGuid().ToString("N")[..8]);

    public SceneTestTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "Screens"));
        File.WriteAllText(Path.Combine(_dir, "Screens", "Start.json"), """
            { "pieces": [ { "type": "Button", "name": "Forest", "x": 40, "y": 40, "width": 200, "height": 50, "text": "Floresta" } ] }
            """);
        File.WriteAllText(Path.Combine(_dir, "Screens", "Fight.json"), """
            { "pieces": [
              { "type": "Bar", "name": "Health", "x": 40, "y": 40, "width": 300, "height": 40, "text": "Vida", "value": 20, "max": 20 },
              { "type": "Messages", "name": "Log", "x": 40, "y": 100, "width": 400, "height": 200 }
            ] }
            """);
    }

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

    private Game NewGame() => new("Teste") { Screens = new ScreenLibrary { Roots = [_dir] } };

    [Fact]
    public void Jogar_esta_cena_comeca_nela_com_os_valores_do_teste_depois_do_OnEnter()
    {
        int health = 20, enemyHealth = 0, enters = 0;
        var game = NewGame();
        game.Scene("Start", () => game.Find("Forest").OnClick(() => game.GoTo("Fight")));
        game.Scene("Fight", () =>
        {
            game.OnEnter(() =>
            {
                enters++;
                enemyHealth = 30;
                game.Write("Um goblin aparece!");
            });
            game.Find("Health").Value = health;
        });
        game.Test("Fight", () =>
        {
            health = 3;
            enemyHealth = 5;   // ganha do OnEnter
        });
        game.PlayFrom = "fight";
        var view = new FakeView();
        game.Begin("Start", view);

        Assert.Equal("Fight", game.CurrentScene);
        Assert.Equal((3, 5, 1), (health, enemyHealth, enters));
        Assert.Equal(3, view.Piece("Health").Value);
        Assert.Equal(["Um goblin aparece!"], view.Last.Messages.Select(m => m.Text));
    }

    [Fact]
    public void Cena_sem_OnEnter_mostra_os_valores_do_teste_sem_repetir_mensagens()
    {
        int health = 20, draws = 0;
        var game = NewGame();
        game.Scene("Start", () => { });
        game.Scene("Fight", () =>
        {
            draws++;
            game.Find("Health").Value = health;
            if (health < 5) game.Find("Log").Write("Você está quase morto!");
        });
        game.Test("Fight", () => health = 2);
        game.PlayFrom = "Fight";
        var view = new FakeView();
        game.Begin("Start", view);

        Assert.Equal(2, view.Piece("Health").Value);
        Assert.Equal(2, draws);   // desenhada de novo, já com o teste
        Assert.Equal(["Você está quase morto!"], view.Last.MessagesByPiece!["Log"].Select(m => m.Text));
    }

    [Fact]
    public void No_F5_de_sempre_o_teste_nao_roda()
    {
        int health = 20;
        var game = NewGame();
        game.Scene("Start", () => game.Find("Forest").OnClick(() => game.GoTo("Fight")));
        game.Scene("Fight", () => game.Find("Health").Value = health);
        game.Test("Fight", () => health = 3);
        var view = new FakeView();
        game.Begin("Start", view);
        Assert.Equal("Start", game.CurrentScene);

        game.ClickPiece(view.Piece("Forest"));
        Assert.Equal("Fight", game.CurrentScene);
        Assert.Equal(20, health);

        // Jogar outra cena: o teste da Fight também não roda.
        int other = 20;
        var second = NewGame();
        second.Scene("Start", () => { });
        second.Scene("Fight", () => { });
        second.Test("Fight", () => other = 3);
        second.PlayFrom = "Start";
        second.Begin("Start", new FakeView());
        Assert.Equal(20, other);
    }

    [Fact]
    public void Erros_explicam_onde_o_game_Test_fica()
    {
        var game = NewGame();
        game.Scene("Fight", () => game.Test("Fight", () => { }));
        game.Scene("Start", () => { });
        game.Test("Fight", () => { });
        var duplicate = Assert.Throws<GameException>(() => game.Test("fight", () => { }));
        Assert.Contains("Já existe um game.Test", duplicate.Message);
        var inside = Assert.Throws<GameException>(() => { game.PlayFrom = "Fight"; game.Begin("Start", new FakeView()); });
        Assert.Contains("fora das cenas", inside.Message);

        var typo = NewGame();
        typo.Scene("Fight", () => { });
        typo.Test("Figth", () => { });
        var unknown = Assert.Throws<GameException>(() => typo.Begin("Fight", new FakeView()));
        Assert.Contains("game.Test(\"Figth\"", unknown.Message);
        Assert.Contains("Você quis dizer \"Fight\"?", unknown.Message);
    }

    [Fact]
    public void Editor_reconhece_o_nome_da_cena_no_game_Test()
    {
        const string code = """game.Test("Fight", () => { health = 3; });""";
        var root = CSharpSyntaxTree.ParseText(code).GetRoot();
        var reference = GameAssist.SceneReferenceAt(root, code.IndexOf("Fight", StringComparison.Ordinal) + 1);
        Assert.Equal("Fight", reference?.Name);
        Assert.False(reference?.IsDeclaration);
    }
}
