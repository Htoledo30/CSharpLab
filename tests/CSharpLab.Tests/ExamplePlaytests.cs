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

        public void Choose(string name, string option) => game.ChoosePiece(Designed.Layout.Pieces.Single(p => p.Name == name), option);
        public void Step(string name, int direction) => game.StepPiece(Designed.Layout.Pieces.Single(p => p.Name == name), direction);
        public void Cell(string name, int x, int y) => game.ClickCell(Designed.Layout.Pieces.Single(p => p.Name == name), x, y);

        public void Answer(string name, string text) =>
            game.AnswerPiece(Designed.Layout.Pieces.Single(p => p.Name == name), text);
    }

    /// <summary>Cria o exemplo, compila o Program.cs dele e começa o jogo com a tela falsa.</summary>
    private Player Load(string id) => LoadFolder(Examples.Create(Examples.All.Single(e => e.Id == id), _dir).Directory);

    /// <summary>Compila o Program.cs da pasta e começa o jogo (playFrom: o "Jogar esta cena", Ctrl+F5).</summary>
    private static Player LoadFolder(string directory, string? playFrom = null)
    {
        var trees = new List<SyntaxTree>
        {
            CSharpSyntaxTree.ParseText(
                "global using System; global using System.IO; global using System.Linq; global using System.Collections.Generic; " +
                "global using System.Threading; global using System.Threading.Tasks; global using CSharpLab.GameEngine;"),
        };
        trees.AddRange(Directory.EnumerateFiles(directory, "*.cs").Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f), path: f)));
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
            game.Screens = new ScreenLibrary { Roots = [directory] };
            game.PlayFrom = playFrom;
            game.SaveFolder = Path.Combine(directory, "saves-de-teste");   // o templo salva aqui, não em Documentos
            Theme.ImageRoots = [directory];   // as imagens da pasta Assets do jogo, para as fotos
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

    /// <summary>O jogo novo: a floresta conta as visitas no OnEnter, e o Ctrl+F5 nela usa o game.Test.</summary>
    [Fact]
    public void Jogo_novo_tres_momentos_e_jogar_esta_cena() => OnSta(() =>
    {
        var folder = Path.Combine(_dir, "novo");
        Directory.CreateDirectory(Path.Combine(folder, "Screens"));
        File.WriteAllText(Path.Combine(folder, "Program.cs"), GameKit.StarterProgram);
        foreach (var (name, text) in GameKit.StarterScreens) File.WriteAllText(Path.Combine(folder, "Screens", name), text);

        var p = LoadFolder(folder);
        Assert.Equal("Start", p.Scene);
        p.Click("Forest");
        Assert.EndsWith("(visita 1)", p.Text("Story"));
        p.Click("Back");
        p.Click("Forest");
        Assert.EndsWith("(visita 2)", p.Text("Story"));

        var test = LoadFolder(folder, playFrom: "Forest");
        Assert.Equal("Forest", test.Scene);
        Assert.EndsWith("(visita 10)", test.Text("Story"));
    });

    /// <summary>
    /// A Coroa Perdida (23 cenas) jogada do começo ao fim: passa por todas as cenas e mexe em todas as peças
    /// (Lista, Escolha, Contador, Roleta, Grade, Dado, Ícone, Campo de escrita, salvar no templo).
    /// </summary>
    [Fact]
    public void A_Coroa_Perdida_do_comeco_ao_fim() => OnSta(() =>
    {
        var p = Load("RpgAdventure");
        Assert.Equal("Title", p.Scene);
        Assert.False(p.Visible("Continue"));   // sem jogo salvo, sem "Continuar"
        Photo(p, "01-titulo");

        p.Answer("NameField", "Henrique");
        Assert.Equal("ChooseClass", p.Scene);
        Assert.Contains("Henrique", p.Text("Subtitle"));
        Assert.Equal(["Guerreiro", "Maga", "Ladino"], p.Cards("Classes").Select(c => c.Find("Name").Text));
        Assert.Equal(Icon.Mage, p.Cards("Classes")[1].Find("Icon").Icon);
        Photo(p, "02-classes");

        p.ClickCard("Classes", 0, "Pick");
        Assert.Equal("Village", p.Scene);
        Assert.Equal("Henrique\nGuerreiro · Nível 1", p.Text("PlayerInfo"));
        Assert.Equal((130, 130), (p.Piece("Health").Value, p.Piece("Health").Max));
        Photo(p, "03-vila");

        // Loja: 6 coisas; compra uma poção; a espada é cara demais por enquanto.
        p.Click("Shop");
        Assert.Equal(6, p.Cards("Goods").Count);
        p.ClickCard("Goods", 0, "Buy");
        Assert.Equal(("2", "7"), (p.Text("Potions"), p.Text("Gold")));
        var sword = p.Cards("Goods")[4];
        Assert.Equal(("Espada de aço", "Falta ouro", false), (sword.Find("Name").Text, sword.Find("Buy").Text, sword.Find("Buy").Enabled));
        Photo(p, "04-loja");
        p.Click("Back");

        // Taverna: aposta no Contador, rola os dados, vai à cigana e gira a roleta.
        p.Click("Tavern");
        p.Step("Bet", +1);
        Assert.Equal(6, p.Piece("Bet").Value);
        p.Click("Play");
        Assert.Contains(p.News, m => m.Contains("Bartô tirou") || m.Contains("Os dois tiraram"));
        Photo(p, "05-taverna");
        p.Click("Fortune");
        Assert.Equal("Fortune", p.Scene);
        if (p.Enabled("Spin"))
        {
            p.Click("Spin");
            Assert.Contains(p.Piece("Wheel").Selected, p.Piece("Wheel").Options);
        }
        Photo(p, "06-roleta");
        p.Click("Back");
        p.Click("Back");

        // Ferreiro: trocar a Escolha muda o texto e o preço.
        p.Click("Smith");
        p.Choose("Upgrade", "Escudo de ferro");
        Assert.Contains("2 a menos", p.Text("Details"));
        Assert.Equal("30 de ouro", p.Text("Price"));
        Photo(p, "07-ferreiro");
        p.Click("Back");

        // Templo: rezar (game.Pause) e salvar; o título passa a mostrar o "Continuar".
        p.Click("Temple");
        p.Click("Pray");
        p.Click("Save");
        Assert.True(p.Game.HasSave);
        Photo(p, "08-templo");
        p.Click("Back");

        p.Click("Quests");
        Assert.Equal(4, p.Cards("Board").Count);
        Photo(p, "09-missoes");
        p.Click("Back");

        p.Click("Bag");
        Assert.Equal("Poção de vida", p.Cards("Items")[0].Find("Name").Text);
        Photo(p, "10-mochila");
        p.Click("Back");
        p.Click("Sheet");
        Assert.Equal("Henrique", p.Text("Name"));
        Photo(p, "11-ficha");
        p.Click("Back");

        // Mapa: a caverna e a torre começam trancadas.
        p.Click("Gate");
        Assert.Equal("Map", p.Scene);
        Assert.False(p.Enabled("Cave"));
        Assert.False(p.Enabled("Tower"));
        Photo(p, "12-mapa");

        // Floresta: explora até ver uma luta e um encontro.
        p.Click("Forest");
        bool sawFight = false, sawEvent = false;
        for (int step = 0; step < 400 && !(sawFight && sawEvent); step++)
        {
            switch (p.Scene)
            {
                case "Forest":
                    p.Click("Explore");
                    break;
                case "Fight":
                    if (!sawFight)
                    {
                        sawFight = true;
                        p.Click("Attack");
                        Photo(p, "13-luta");
                    }
                    Fight(p);
                    break;
                case "Event":
                    if (!sawEvent) Photo(p, "14-encontro");
                    sawEvent = true;
                    if (p.Visible("Leave")) p.Click("Leave");
                    else p.Click(p.Enabled("ChoiceA") ? "ChoiceA" : "ChoiceB");
                    break;
                case "GameOver":
                    Revive(p);
                    p.Click("Back");
                    p.Click("Gate");
                    p.Click("Forest");
                    break;
                default:
                    throw new InvalidOperationException("Cena inesperada na floresta: " + p.Scene);
            }
        }
        Assert.True(sawFight && sawEvent, "Em 400 passos não apareceu luta e encontro.");
        ToMap(p);

        // Pântano: cava a Grade até achar o baú (ou a cobra sair da lama).
        p.Click("Swamp");
        for (int y = 0; y < 4 && p.Scene == "Swamp"; y++)
            for (int x = 0; x < 6 && p.Scene == "Swamp" && p.Text("Digs") != "Você achou o baú!"; x++)
                p.Cell("Mud", x, y);
        if (p.Scene == "Swamp") Photo(p, "15-pantano");
        else Fight(p);
        ToMap(p);

        // Acampamento: dormir é um d20; ouvir a noite usa game.Choose.
        p.Click("Camp");
        p.Click("Sleep");
        if (p.Scene == "Camp")
        {
            Assert.Contains(p.News, m => m.StartsWith("Tirou "));   // noite tranquila (8 ou mais no d20)
            Photo(p, "16-acampamento");
        }
        else Fight(p);   // tirou menos de 8: um lobo atacou
        ToMap(p);

        // Ponte: as três respostas certas abrem a caverna.
        p.Click("Bridge");
        Photo(p, "17-ponte");
        foreach (var answer in new[] { "12", "Pente", "8" })
        {
            p.Choose("Answers", answer);
            p.Click("Reply");
        }
        Assert.False(p.Visible("Answers"));
        p.Click("Back");
        Assert.True(p.Enabled("Cave"));

        // Caverna: erra o enigma, depois acerta e pega a chave.
        p.Click("Cave");
        p.Answer("Riddle", "uma pedra");
        Assert.Contains(p.News, m => m.Contains("Errado"));
        p.Answer("Riddle", "Buraco");
        Assert.True(p.Visible("KeyIcon"));
        Photo(p, "18-caverna");
        p.Click("Back");
        Assert.True(p.Enabled("Tower"));

        // Torre: as três armadilhas (d20) e o chefe.
        p.Click("Tower");
        p.Click("Enter");
        Assert.Equal("TowerHall", p.Scene);
        for (int i = 0; i < 3; i++) p.Click("Jump");
        Assert.Equal(3, p.Piece("Steps").Value);
        Photo(p, "19-armadilhas");
        p.Click("Throne");
        Assert.Equal("Fight", p.Scene);
        Assert.False(p.Visible("Flee"));   // do chefe não se foge
        Photo(p, "20-chefe");
        Fight(p);
        Assert.Contains(p.Scene, new[] { "Victory", "GameOver" });
        Photo(p, p.Scene == "Victory" ? "21-vitoria" : "21-derrota");
        Assert.Contains("Henrique", p.Text("Story"));

        static void Fight(Player p)
        {
            for (int turn = 0; turn < 200 && p.Scene == "Fight" && !p.Visible("Continue"); turn++)
            {
                if (p.Enabled("Potion") && p.Piece("Health").Value * 3 < p.Piece("Health").Max) p.Click("Potion");
                else if (p.Enabled("Skill")) p.Click("Skill");
                else p.Click("Attack");
            }
            if (p.Scene == "Fight") p.Click("Continue");
        }

        static void Revive(Player p)
        {
            Assert.True(p.Visible("Load"));   // salvou no templo: volta de lá
            p.Click("Load");
            Assert.Equal("Temple", p.Scene);
        }

        static void ToMap(Player p)
        {
            if (p.Scene == "Fight") Fight(p);
            if (p.Scene == "Event") p.Click(p.Visible("Leave") ? "Leave" : "ChoiceB");   // volta para a floresta
            if (p.Scene == "GameOver")
            {
                Revive(p);
                p.Click("Back");
                p.Click("Gate");
            }
            if (p.Scene is "Forest" or "Swamp" or "Camp" or "Bridge" or "Cave") p.Click("Back");
            Assert.Equal("Map", p.Scene);
        }
    });
}
