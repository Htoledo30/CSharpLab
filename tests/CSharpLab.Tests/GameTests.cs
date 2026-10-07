using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CSharpLab.Core.Build;
using CSharpLab.Core.Files;
using CSharpLab.Core.Language;
using CSharpLab.Core.Projects;
using CSharpLab.GameEngine;
using Microsoft.CodeAnalysis.Text;

namespace CSharpLab.Tests;

/// <summary>O motor dos jogos com botões: cenas, cliques, trocas de cena e erros claros.</summary>
public sealed class GameEngineTests
{
    private sealed class FakeView : IGameView
    {
        public Screen Last { get; private set; } = new();
        public int Draws { get; private set; }

        public void Show(Screen screen)
        {
            Last = screen;
            Draws++;
        }

        public IEnumerable<string> Texts => Last.Items.OfType<TextItem>().Select(t => t.Text);
        public IEnumerable<string> News => Last.Items.OfType<TextItem>().Where(t => t.IsNews).Select(t => t.Text);
        public ButtonItem Button(string text) => Last.Buttons.Single(b => b.Text == text);
    }

    private static (Game Game, FakeView View) Started(Action<Game> setup, string first = "Start")
    {
        var game = new Game("Teste");
        setup(game);
        var view = new FakeView();
        game.Start(first, view);
        return (game, view);
    }

    [Fact]
    public void Clique_muda_variaveis_e_a_cena_e_desenhada_de_novo()
    {
        int gold = 0;
        var (game, view) = Started(g => g.Scene("Start", () =>
        {
            g.Title("Vila");
            g.Say($"Ouro: {gold}");
            g.Bar("Vida", 50, 100, GameColor.Red);
            g.Button("Procurar", () => gold += 5);
        }));

        Assert.Equal("Vila", view.Last.Title);
        Assert.Contains("Ouro: 0", view.Texts);
        Assert.Equal(new BarItem("Vida", 50, 100, GameColor.Red), view.Last.Bars.Single());

        game.Act(view.Button("Procurar").OnClick);
        Assert.Equal(5, gold);
        Assert.Contains("Ouro: 5", view.Texts);
        Assert.Equal(2, view.Draws);
    }

    [Fact]
    public void Say_dentro_do_botao_vira_aviso_que_some_no_proximo_clique()
    {
        var (game, view) = Started(g => g.Scene("Start", () =>
        {
            g.Say("Descrição");
            g.Button("Atacar", () => g.Say("7 de dano!"));
            g.Button("Esperar", () => { });
        }));

        game.Act(view.Button("Atacar").OnClick);
        Assert.Equal(["Descrição", "7 de dano!"], view.Texts);
        Assert.Equal(["7 de dano!"], view.News);

        game.Act(view.Button("Esperar").OnClick);
        Assert.Empty(view.News);
    }

    [Fact]
    public void GoTo_no_botao_troca_de_cena_e_leva_o_aviso_junto()
    {
        var (game, view) = Started(g =>
        {
            g.Scene("Start", () => g.Button("Entrar", () =>
            {
                g.Say("A porta range.");
                g.GoTo("hall"); // maiúsculas não importam
            }));
            g.Scene("Hall", () => g.Title("Salão"));
        });

        game.Act(view.Button("Entrar").OnClick);
        Assert.Equal("Hall", game.CurrentScene);
        Assert.Equal("Salão", view.Last.Title);
        Assert.Equal(["A porta range."], view.News);
    }

    [Fact]
    public void GoTo_dentro_da_cena_redireciona()
    {
        int health = 0;
        var (game, view) = Started(g =>
        {
            g.Scene("Fight", () =>
            {
                if (health <= 0) g.GoTo("GameOver");
                g.Title("Luta");
            });
            g.Scene("GameOver", () => g.Title("Fim de jogo"));
        }, first: "Fight");

        Assert.Equal("GameOver", game.CurrentScene);
        Assert.Equal("Fim de jogo", view.Last.Title);
    }

    [Fact]
    public void Cenas_que_se_mandam_sem_parar_dao_erro_claro()
    {
        var error = Assert.Throws<GameException>(() => Started(g =>
        {
            g.Scene("A", () => g.GoTo("B"));
            g.Scene("B", () => g.GoTo("A"));
        }, first: "A"));
        Assert.Contains("sem parar", error.Message);
    }

    [Fact]
    public void Cena_com_nome_errado_sugere_o_certo()
    {
        var (game, view) = Started(g =>
        {
            g.Scene("Start", () => g.Button("Ir", () => g.GoTo("Florest")));
            g.Scene("Forest", () => { });
        });
        var error = Assert.Throws<GameException>(() => game.Act(view.Button("Ir").OnClick));
        Assert.Contains("\"Florest\" não existe", error.Message);
        Assert.Contains("Você quis dizer \"Forest\"?", error.Message);
    }

    [Fact]
    public void Erros_de_uso_explicam_o_que_fazer()
    {
        var game = new Game("Teste");
        Assert.Contains("dentro de uma cena", Assert.Throws<GameException>(() => game.Button("Ok", () => { })).Message);
        Assert.Contains("nenhuma cena", Assert.Throws<GameException>(() => game.Start("Start")).Message);
        Assert.Contains("game.Run", Assert.Throws<GameException>(() => game.GoTo("Start")).Message);

        game.Scene("Start", () => g());
        Assert.Contains("Já existe", Assert.Throws<GameException>(() => game.Scene("start", () => { })).Message);
        game.Start("Start", new FakeView());
        Assert.Contains("uma vez", Assert.Throws<GameException>(() => game.Start("Start", new FakeView())).Message);

        static void g() { }
    }

    [Fact]
    public void Barra_sem_maximo_da_erro_claro()
    {
        var error = Assert.Throws<GameException>(() => Started(g => g.Scene("Start", () => g.Bar("Vida", 10, 0))));
        Assert.Contains("maior que 0", error.Message);
    }

    [Fact]
    public void Pergunta_entrega_a_resposta()
    {
        string name = "";
        var (game, view) = Started(g =>
        {
            g.Scene("Start", () => g.Ask("Seu nome?", answer =>
            {
                name = answer;
                g.GoTo("Hello");
            }));
            g.Scene("Hello", () => g.Say($"Olá, {name}!"));
        });
        var ask = view.Last.Items.OfType<AskItem>().Single();
        Assert.Equal("Seu nome?", ask.Question);
        game.Act(() => ask.OnAnswer("Ana"));
        Assert.Contains("Olá, Ana!", view.Texts);
    }

    /// <summary>
    /// Desenha a janela de verdade (fora da tela) com tudo que o motor oferece, sem erros.
    /// A imagem fica em %TEMP%\csharplab-jogo-previa.png para conferir o visual.
    /// </summary>
    [Fact]
    public void Janela_desenha_todos_os_elementos()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var game = new Game("A Torre");
                var window = new GameWindow(game);
                var screen = new Screen { Title = "Um goblin aparece!" };
                screen.Items.Add(new TextItem("Ele segura uma faca enferrujada e ri de você.", null, false));
                screen.Items.Add(new TextItem("Ouro: 10", GameColor.Gold, false));
                screen.Items.Add(new ImageItem("nao-existe.png"));
                screen.Items.Add(new TextItem("Você causou 7 de dano!", null, true));
                screen.Items.Add(new AskItem("Qual é o seu nome?", _ => { }));
                screen.Bars.Add(new BarItem("Henrique", 72, 100, GameColor.Green));
                screen.Bars.Add(new BarItem("Goblin", 9, 30, GameColor.Red));
                screen.Buttons.Add(new ButtonItem("Atacar", () => { }));
                screen.Buttons.Add(new ButtonItem("Beber poção (1)", () => { }));
                screen.Buttons.Add(new ButtonItem("Fugir", () => { }));
                window.Show(screen);

                var content = (FrameworkElement)window.Content;
                window.Content = null;
                content.Measure(new Size(GameWindow.StageWidth, GameWindow.StageHeight));
                content.Arrange(new Rect(0, 0, GameWindow.StageWidth, GameWindow.StageHeight));
                content.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)GameWindow.StageWidth, (int)GameWindow.StageHeight, 96, 96, PixelFormats.Pbgra32);
                var background = new System.Windows.Shapes.Rectangle { Width = GameWindow.StageWidth, Height = GameWindow.StageHeight, Fill = window.Background };
                background.Measure(new Size(GameWindow.StageWidth, GameWindow.StageHeight));
                background.Arrange(new Rect(0, 0, GameWindow.StageWidth, GameWindow.StageHeight));
                bitmap.Render(background);
                bitmap.Render(content);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(Path.GetTempPath(), "csharplab-jogo-previa.png"));
                encoder.Save(file);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.Null(failure);
    }
}

/// <summary>Criar, compilar e analisar um jogo com botões no editor.</summary>
public sealed class GameProjectTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "csharplab-jogos", Guid.NewGuid().ToString("N")[..8]);

    public GameProjectTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public async Task Jogo_novo_tem_motor_guia_para_ia_e_compila_sem_avisos()
    {
        var created = ProjectCreator.CreateGameProject(_dir, "Meu Jogo");
        foreach (var file in new[] { "lib/CSharpLab.Game.dll", "lib/CSharpLab.Game.xml", "AGENTS.md", "CLAUDE.md", "Program.cs" })
            Assert.True(File.Exists(Path.Combine(created.Directory, file)), file);
        Assert.True(Directory.Exists(Path.Combine(created.Directory, "Assets")));
        Assert.Contains("@AGENTS.md", File.ReadAllText(Path.Combine(created.Directory, "CLAUDE.md")));
        Assert.Contains("game.Button", File.ReadAllText(Path.Combine(created.Directory, "AGENTS.md")));

        var project = ProjectFile.Read(created.ProjectPath);
        Assert.True(project.IsWindowApp);
        Assert.True(project.IsRunnable);
        Assert.Single(ProjectLocator.RunnableProjects([project]));

        var result = await BuildService.BuildAsync(created.ProjectPath, null, CancellationToken.None);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => $"{d.Id} {d.Message}")) + "\n" + result.Log);
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == BuildSeverity.Warning).Select(d => $"{d.Id} {d.Message}"));
    }

    [Fact]
    public void Falha_ao_criar_nao_deixa_lixo()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "Ocupado"));
        File.WriteAllText(Path.Combine(_dir, "Ocupado", "x.txt"), "");
        Assert.Throws<UserFacingException>(() => ProjectCreator.CreateGameProject(_dir, "Ocupado"));
        Assert.Single(Directory.EnumerateFileSystemEntries(Path.Combine(_dir, "Ocupado")));
    }

    [Fact]
    public void Motor_da_mesma_versao_nao_e_copiado_de_novo()
    {
        var created = ProjectCreator.CreateGameProject(_dir, "Atualiza");
        Assert.False(GameKit.RefreshLibrary(created.Directory)); // mesma versão
        Assert.True(GameKit.UsesEngine(created.Directory));
    }

    /// <summary>Um erro do motor para o jogo com a mensagem em português e a linha do Program.cs.</summary>
    [Fact]
    public async Task Erro_do_motor_para_o_jogo_com_mensagem_em_portugues()
    {
        var program = "var game = new Game(\"Teste\");\n" +
                      "game.Scene(\"Start\", () =>\n{\n    game.GoTo(\"Nowhere\");\n});\n" +
                      "game.Run(\"Start\");\n";
        var created = ProjectCreator.CreateGameProject(_dir, "Quebra", program);
        var result = await BuildService.BuildAsync(created.ProjectPath, null, CancellationToken.None);
        Assert.True(result.Success, result.Log);

        var model = await ProjectEvaluator.EvaluateAsync(created.ProjectPath, allowRestore: false, CancellationToken.None);
        var launch = await BuildService.GetLaunchAsync(model, CancellationToken.None);
        var start = new ProcessStartInfo(launch.FileName) { RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = launch.WorkingDirectory };
        foreach (var a in launch.Arguments) start.ArgumentList.Add(a);
        using var process = Process.Start(start)!;
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));

        Assert.NotEqual(0, process.ExitCode);
        Assert.Contains("CSharpLab.GameEngine.GameException", stderr);
        // O stderr vem na página de código do console (o "ã" muda); o editor lê o relatório do gancho, em UTF-8.
        Assert.Contains("A cena \"Nowhere\" n", stderr);
        Assert.Contains("Program.cs:line 4", stderr);
        Assert.Equal("A cena \"Nowhere\" não existe. Cenas do jogo: \"Start\".",
            RuntimeErrors.Explain(new RuntimeCrash("CSharpLab.GameEngine.GameException", "A cena \"Nowhere\" não existe. Cenas do jogo: \"Start\".", []), null).Message);
    }

    [Fact]
    public async Task Editor_sugere_e_explica_o_motor_em_portugues()
    {
        var created = ProjectCreator.CreateGameProject(_dir, "Ajuda");
        var model = await ProjectEvaluator.EvaluateAsync(created.ProjectPath, allowRestore: true, CancellationToken.None);
        Assert.True(model.UsesGameEngine);

        using var ls = new LanguageService();
        ls.LoadProject(model);
        var text = "var game = new Game(\"T\");\ngame.Scene(\"Start\", () => game.Say(\"Oi\"));\nConsole.WriteLine(\"x\");\ngame.";
        var key = LanguageService.KeyFor(created.ProgramPath);
        ls.OpenDocument(key, created.ProgramPath, SourceText.From(text), 1);

        var completions = await ls.GetCompletionsAsync(key, text.Length, '.', CancellationToken.None);
        Assert.NotNull(completions);
        var names = completions!.Items.Select(i => i.DisplayText).ToList();
        foreach (var expected in new[] { "Say", "Button", "Bar", "Ask", "GoTo", "Scene", "Run", "Title", "Image" })
            Assert.Contains(expected, names);
        var say = completions.Items.First(i => i.DisplayText == "Say");
        Assert.Contains("Say = dizer", await LanguageService.GetDescriptionAsync(completions, say, CancellationToken.None));

        var info = await ls.GetQuickInfoAsync(key, text.IndexOf("Say", StringComparison.Ordinal) + 1, CancellationToken.None);
        Assert.Contains("Say = dizer", info?.Doc?.Text);

        var diagnostics = await ls.GetDiagnosticsAsync(CancellationToken.None);
        Assert.Contains(diagnostics.Diagnostics, d => d.Id == BeginnerHints.ConsoleInGameId);
    }
}
