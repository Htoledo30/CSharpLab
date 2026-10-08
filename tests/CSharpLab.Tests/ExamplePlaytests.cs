using System.Reflection;
using System.Runtime.Loader;
using CSharpLab.Core.Projects;
using CSharpLab.GameEngine;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CSharpLab.Tests;

/// <summary>
/// Os jogos de exemplo jogados de verdade, sem abrir janela: o Program.cs é compilado, o game.Start
/// entrega o jogo para o teste, e o teste clica nos botões como um jogador.
/// As telas ficam em %TEMP%\csharplab-jogada-*.png para conferir o visual.
/// </summary>
public sealed class ExamplePlaytests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "csharplab-jogadas", Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private sealed class PlayView : IGameView
    {
        public Screen? Screen { get; private set; }
        public void Show(Screen screen) => Screen = screen;
    }

    /// <summary>Um jogador de mentira: clica, responde e lê o que está na tela.</summary>
    private sealed class Player(Game game, PlayView view)
    {
        public Game Game => game;
        public Screen Screen => view.Screen!;
        public string Scene => game.CurrentScene!;
        private DesignedScene Designed => Screen.Designed ?? throw new InvalidOperationException($"A cena {Scene} não é desenhada.");

        public Item Piece(string name) => Designed.Find(name) ?? throw new InvalidOperationException($"{Scene} não tem a peça {name}.");
        public bool Visible(string name) => Piece(name).Visible;
        public string Text(string name) => Piece(name).Text;
        /// <summary>As mensagens do último clique (destacadas na peça Mensagens).</summary>
        public IEnumerable<string> News => Screen.Messages.Where(m => m.IsNews).Select(m => m.Text);

        public bool Enabled(string name) => Piece(name).Enabled;

        public void Click(string name)
        {
            Assert.True(Visible(name), $"{name} está escondido na cena {Scene}");
            Assert.True(Enabled(name), $"{name} está apagado na cena {Scene}");
            game.ClickPiece(Designed.Layout.Pieces.Single(p => p.Name == name));
        }

        /// <summary>Os cartões que o Show pôs na Lista.</summary>
        public IReadOnlyList<Card> Cards(string list) => Designed.CardsOf(Designed.Layout.Find(list)!);

        /// <summary>Clica num botão de um cartão da Lista (a loja, a escolha de classe).</summary>
        public void ClickCard(string list, int index, string name)
        {
            var button = Cards(list)[index].Find(name);
            Assert.True(button.Enabled, $"{name} do cartão {index} de {list} está apagado");
            game.ClickPiece(button.Piece);
        }

        public void Answer(string name, string text) =>
            game.AnswerPiece(Designed.Layout.Pieces.Single(p => p.Name == name), text);
    }

    /// <summary>Cria o exemplo, compila o Program.cs dele e começa o jogo com a tela falsa.</summary>
    private Player Load(string id)
    {
        var example = Examples.All.Single(e => e.Id == id);
        var created = Examples.Create(example, _dir);
        var trees = new List<SyntaxTree>
        {
            CSharpSyntaxTree.ParseText(
                "global using System; global using System.IO; global using System.Linq; global using System.Collections.Generic; " +
                "global using System.Threading; global using System.Threading.Tasks; global using CSharpLab.GameEngine;"),
        };
        trees.AddRange(Directory.EnumerateFiles(created.Directory, "*.cs").Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f), path: f)));
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(p => MetadataReference.CreateFromFile(p));
        var compilation = CSharpCompilation.Create("Jogo" + Guid.NewGuid().ToString("N")[..6], trees, references,
            new CSharpCompilationOptions(OutputKind.WindowsApplication, nullableContextOptions: NullableContextOptions.Enable));
        using var assembly = new MemoryStream();
        var emit = compilation.Emit(assembly);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

        var view = new PlayView();
        Game? started = null;
        Game.StartForTests = (game, first) =>
        {
            game.Screens = new ScreenLibrary { Roots = [created.Directory] };
            Theme.ImageRoots = [created.Directory];   // as imagens da pasta Assets do jogo, para as fotos
            game.Begin(first, view);
            started = game;
        };
        try
        {
            var loaded = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(assembly.ToArray()));
            loaded.EntryPoint!.Invoke(null, loaded.EntryPoint.GetParameters().Length == 0 ? null : [Array.Empty<string>()]);
        }
        finally
        {
            Game.StartForTests = null;
        }
        return new Player(started ?? throw new InvalidOperationException("O jogo não chamou game.Start."), view);
    }

    private static void Photo(Player player, string name)
    {
        var window = new GameWindow(player.Game);
        window.Show(player.Screen);
        DesignedSceneTests.Render(window, $"csharplab-jogada-{name}.png");
        window.Close();
    }

    private static void OnSta(Action test)
    {
        Exception? failure = null;
        var imageRoots = Theme.ImageRoots;
        var thread = new Thread(() =>
        {
            try { test(); }
            catch (Exception ex) { failure = ex; }
            finally { Theme.ImageRoots = imageRoots; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw new Exception("Falhou jogando: " + failure.Message, failure);
    }

    [Fact]
    public void A_Coroa_Perdida_do_comeco_ao_fim() => OnSta(() =>
    {
        var p = Load("RpgAdventure");
        Assert.Equal("Title", p.Scene);
        Photo(p, "01-titulo");

        p.Answer("NameField", "Henrique");
        Assert.Equal("ChooseClass", p.Scene);
        Assert.Contains("Henrique", p.Text("Subtitle"));
        // As classes vêm da lista do código: um cartão para cada uma.
        Assert.Equal(["Guerreiro", "Maga", "Ladino"], p.Cards("Classes").Select(c => c.Find("Name").Text));
        Assert.Contains("Golpe brutal", p.Cards("Classes")[0].Find("Stats").Text);
        Photo(p, "02-classes");

        p.ClickCard("Classes", 0, "Pick");
        Assert.Equal("Village", p.Scene);
        Assert.Equal("Henrique · Guerreiro · Nível 1", p.Text("PlayerInfo"));
        Assert.Equal((130, 130), (p.Piece("Health").Value, p.Piece("Health").Max));
        // Sem a chave, a torre fica apagada.
        Assert.Equal("🔒 Trancada", p.Text("Tower"));
        Assert.False(p.Enabled("Tower"));
        Photo(p, "03-vila");

        // Loja: a Lista mostra as 5 coisas; compra uma poção; a espada é cara demais por enquanto.
        p.Click("Shop");
        Assert.Equal(5, p.Cards("Goods").Count);
        p.ClickCard("Goods", 0, "Buy");
        Assert.Equal("🧪 2", p.Text("Potions"));
        Assert.Equal("💰 7", p.Text("Gold"));
        var sword = p.Cards("Goods")[3];
        Assert.Equal(("Espada de aço", "Falta ouro", false), (sword.Find("Name").Text, sword.Find("Buy").Text, sword.Find("Buy").Enabled));
        Assert.Contains(p.News, m => m.Contains("Você comprou: Poção de vida"));
        Photo(p, "04-loja");
        p.Click("Back");

        // Caverna: erra o enigma, depois acerta e pega a chave.
        p.Click("Cave");
        p.Answer("Riddle", "uma pedra");
        Assert.Equal(122, p.Piece("Health").Value);
        Photo(p, "05-caverna-erro");
        p.Answer("Riddle", "Buraco");
        Assert.False(p.Visible("Riddle"));
        Assert.Contains(p.News, m => m.Contains("chave da torre"));
        // A mensagem do erro continua lá, apagadinha (o histórico da cena).
        Assert.Contains(p.Screen.Messages, m => m.IsOld && m.Text.Contains("Errado"));
        p.Click("Back");
        Assert.Equal("Entrar", p.Text("Tower"));
        Assert.True(p.Enabled("Tower"));

        // Floresta: explora até ter visto uma luta e um encontro (curando na taverna quando preciso).
        bool sawFight = false, sawEvent = false;
        p.Click("Forest");
        Photo(p, "05b-floresta");
        for (int step = 0; step < 400 && !(sawFight && sawEvent); step++)
        {
            switch (p.Scene)
            {
                case "Forest":
                    if (p.Piece("Health").Value < 50)
                    {
                        p.Click("Back");
                        if (p.Text("Gold") is var g && int.Parse(g["💰 ".Length..]) >= 5) p.Click("Tavern");
                        p.Click("Forest");
                    }
                    else p.Click("Explore");
                    break;
                case "Fight":
                    if (!sawFight)
                    {
                        sawFight = true;
                        p.Click("Attack");
                        // O herói ataca, o inimigo responde: as duas mensagens ficam no histórico da luta.
                        if (p.Scene == "Fight" && !p.Visible("Continue"))
                        {
                            Assert.Contains(p.Screen.Messages, m => m.IsOld && m.Text.StartsWith("Você causou"));
                            Assert.Contains(p.Screen.Messages, m => m.IsNews && (m.Text.Contains("ataca") || m.Text.Contains("atordoado")));
                            Photo(p, "06-luta");
                        }
                        break;
                    }
                    if (p.Visible("Continue"))
                    {
                        Photo(p, "07-luta-vencida");
                        p.Click("Continue");
                    }
                    else p.Click("Attack");
                    break;
                case "Event":
                    if (!sawEvent) Photo(p, "08-encontro");
                    sawEvent = true;
                    if (p.Visible("Leave")) p.Click("Leave");
                    else if (p.Enabled("ChoiceA")) p.Click("ChoiceA");
                    else p.Click("ChoiceB");
                    break;
                case "GameOver":
                    Photo(p, "09-fim-de-jogo");
                    p.Click("Again");
                    p.ClickCard("Classes", 0, "Pick");
                    p.Click("Forest");
                    break;
                default:
                    throw new InvalidOperationException("Cena inesperada na floresta: " + p.Scene);
            }
        }
        Assert.True(sawFight && sawEvent, "Em 400 passos não apareceu luta e encontro.");

        // O chefe: vai até a torre (pega a chave de novo se o jogo recomeçou) e luta até o fim.
        for (int step = 0; step < 50 && p.Scene != "Village"; step++)
        {
            if (p.Scene == "GameOver")
            {
                p.Click("Again");
                p.ClickCard("Classes", 0, "Pick");
            }
            else if (p.Scene == "Fight") p.Click(p.Visible("Continue") ? "Continue" : "Flee");
            else if (p.Scene == "Event") p.Click(p.Visible("Leave") ? "Leave" : "ChoiceB");
            else p.Click("Back");
        }
        if (p.Text("Tower") != "Entrar")
        {
            p.Click("Cave");
            p.Answer("Riddle", "buraco");
            p.Click("Back");
        }
        p.Click("Tower");
        Photo(p, "10-torre");
        p.Click("Challenge");
        Assert.False(p.Visible("Flee"));   // do chefe não se foge
        Photo(p, "10b-chefe");
        for (int turn = 0; turn < 200 && p.Scene == "Fight"; turn++)
        {
            if (p.Visible("Continue")) p.Click("Continue");
            else if (p.Enabled("Potion") && p.Piece("Health").Value < 40) p.Click("Potion");
            else if (p.Enabled("Skill")) p.Click("Skill");
            else p.Click("Attack");   // sem mana, o botão da habilidade fica apagado
        }
        Assert.Contains(p.Scene, new[] { "Victory", "GameOver" });
        Photo(p, p.Scene == "Victory" ? "11-vitoria" : "11-derrota");
        Assert.Contains("Henrique", p.Text("Story"));
    });
}
