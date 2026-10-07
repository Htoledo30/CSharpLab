using System.Windows;

namespace CSharpLab.GameEngine;

/// <summary>
/// Game = jogo. A janela do jogo, com cenas, textos, barras e botões.
/// </summary>
/// <example>
/// <code>
/// var game = new Game("A Torre");
///
/// game.Scene("Start", () =>
/// {
///     game.Say("Você acorda na frente de uma torre.");
///     game.Button("Entrar", () => game.GoTo("Hall"));
/// });
///
/// game.Run("Start");
/// </code>
/// </example>
public sealed class Game
{
    private const int MaxRedirects = 20;

    private readonly Dictionary<string, Action> _scenes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string Text, GameColor? Color)> _news = [];
    private Screen? _building;
    private string? _current;
    private string? _pendingScene;
    private bool _inAction;
    private bool _started;
    private IGameView? _view;

    /// <summary>Cria o jogo. O título aparece no alto da janela.</summary>
    /// <param name="title">Nome do jogo. Exemplo: "A Torre".</param>
    public Game(string title)
    {
        WindowTitle = string.IsNullOrWhiteSpace(title) ? "Meu jogo" : title.Trim();
    }

    /// <summary>O nome do jogo, mostrado no alto da janela.</summary>
    public string WindowTitle { get; }

    /// <summary>CurrentScene = cena atual. O nome da cena que está na tela agora.</summary>
    public string? CurrentScene => _current;

    /// <summary>
    /// Scene = cena. Cria uma tela do jogo. Tudo que está dentro das chaves é desenhado
    /// de novo sempre que o jogador clica, então a tela mostra os valores atuais das variáveis.
    /// </summary>
    /// <param name="name">Nome da cena, para usar no GoTo. Exemplo: "Forest".</param>
    /// <param name="build">O que aparece na cena: game.Say, game.Button, game.Bar…</param>
    /// <example>
    /// <code>
    /// game.Scene("Forest", () =>
    /// {
    ///     game.Say("Árvores por todo lado.");
    ///     game.Button("Voltar", () => game.GoTo("Start"));
    /// });
    /// </code>
    /// </example>
    public void Scene(string name, Action build)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new GameException("A cena precisa de um nome. Exemplo: game.Scene(\"Start\", () => { ... });");
        ArgumentNullException.ThrowIfNull(build);
        name = name.Trim();
        if (_building != null)
            throw new GameException($"game.Scene(\"{name}\", ...) está dentro de outra cena. Crie cada cena separada, fora das chaves das outras.");
        if (!_scenes.TryAdd(name, build))
            throw new GameException($"Já existe uma cena chamada \"{name}\". Dê outro nome para esta.");
    }

    /// <summary>GoTo = ir para. Troca para outra cena.</summary>
    /// <param name="name">Nome da cena. Exemplo: game.GoTo("Forest");</param>
    public void GoTo(string name)
    {
        if (!_started)
            throw new GameException("game.GoTo só funciona com o jogo rodando. Para escolher a primeira cena, use game.Run(\"" + (name ?? "") + "\").");
        var scene = FindScene(name);
        if (_inAction || _building != null)
        {
            // Depois do clique (ou desta cena) a tela é desenhada de novo, já na cena nova.
            _pendingScene = scene;
            return;
        }
        _current = scene;
        Redraw();
    }

    /// <summary>Title = título. Texto grande no alto da cena.</summary>
    /// <param name="text">Exemplo: "Capítulo 1".</param>
    public void Title(string text) => Building(nameof(Title)).Title = text ?? "";

    /// <summary>
    /// Say = dizer. Mostra um texto na tela. Dentro de um botão, o texto aparece destacado
    /// depois do clique (bom para "Você causou 7 de dano!").
    /// </summary>
    /// <param name="text">O texto. Use $"..." para mostrar variáveis: $"Ouro: {gold}".</param>
    /// <param name="color">Cor opcional. Exemplo: GameColor.Red.</param>
    public void Say(string text, GameColor? color = null)
    {
        text ??= "";
        if (_building != null) _building.Items.Add(new TextItem(text, color, IsNews: false));
        else _news.Add((text, color));
    }

    /// <summary>Button = botão. Cria um botão; o código entre as chaves roda quando o jogador clica.</summary>
    /// <param name="text">O que está escrito no botão. Exemplo: "Atacar".</param>
    /// <param name="onClick">O que acontece no clique: () => { ... }</param>
    /// <example>
    /// <code>
    /// game.Button("Beber poção", () =>
    /// {
    ///     health += 20;
    ///     game.Say("Você se sente melhor.");
    /// });
    /// </code>
    /// </example>
    public void Button(string text, Action onClick)
    {
        var screen = Building(nameof(Button));
        if (onClick == null)
            throw new GameException($"O botão \"{text}\" precisa dizer o que faz. Exemplo: game.Button(\"{text}\", () => game.GoTo(\"Start\"));");
        screen.Buttons.Add(new ButtonItem(text ?? "", onClick));
    }

    /// <summary>Bar = barra. Mostra uma barra de vida, mana, energia…</summary>
    /// <param name="label">Nome da barra. Exemplo: "Vida".</param>
    /// <param name="value">Quanto tem agora. Exemplo: health.</param>
    /// <param name="max">O máximo. Exemplo: 100.</param>
    /// <param name="color">Cor da barra. Exemplo: GameColor.Red.</param>
    public void Bar(string label, int value, int max, GameColor color = GameColor.Green)
    {
        var screen = Building(nameof(Bar));
        if (max <= 0)
            throw new GameException($"A barra \"{label}\" precisa de um máximo maior que 0 (veio {max}).");
        screen.Bars.Add(new BarItem(label ?? "", value, max, color));
    }

    /// <summary>
    /// Ask = perguntar. Mostra uma pergunta com um campo para o jogador escrever.
    /// O código entre as chaves recebe a resposta quando ele aperta OK ou Enter.
    /// </summary>
    /// <param name="question">Exemplo: "Qual é o seu nome?".</param>
    /// <param name="onAnswer">O que fazer com a resposta: answer => { name = answer; }</param>
    /// <example>
    /// <code>
    /// game.Ask("Qual é o seu nome?", answer =>
    /// {
    ///     playerName = answer;
    ///     game.GoTo("Start");
    /// });
    /// </code>
    /// </example>
    public void Ask(string question, Action<string> onAnswer)
    {
        var screen = Building(nameof(Ask));
        if (onAnswer == null)
            throw new GameException("O game.Ask precisa dizer o que fazer com a resposta. Exemplo: game.Ask(\"Seu nome?\", answer => { name = answer; });");
        screen.Items.Add(new AskItem(question ?? "", onAnswer));
    }

    /// <summary>Image = imagem. Mostra uma imagem (png ou jpg) da pasta Assets do projeto.</summary>
    /// <param name="path">Nome do arquivo. Exemplo: "goblin.png" (dentro da pasta Assets).</param>
    public void Image(string path) => Building(nameof(Image)).Items.Add(new ImageItem(path ?? ""));

    /// <summary>
    /// Run = começar. Abre a janela do jogo na cena escolhida. Fica sempre no fim do arquivo:
    /// o programa continua aqui até o jogador fechar a janela.
    /// </summary>
    /// <param name="firstScene">A cena que aparece primeiro. Exemplo: "Start".</param>
    public void Run(string firstScene)
    {
        Start(firstScene);
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            RunWindow();
            return;
        }
        // Janelas do Windows precisam de uma thread STA; o código de cima do arquivo roda em outra.
        var thread = new Thread(RunWindow) { Name = "Game", IsBackground = false };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    // ------------------------------------------------------------------ por dentro

    /// <summary>Confere tudo antes de abrir a janela, para o erro apontar a linha do Run.</summary>
    internal void Start(string firstScene, IGameView? view = null)
    {
        if (_started)
            throw new GameException("game.Run só pode ser chamado uma vez, no fim do arquivo.");
        if (_scenes.Count == 0)
            throw new GameException("O jogo não tem nenhuma cena. Crie uma antes do Run: game.Scene(\"Start\", () => { game.Say(\"Olá!\"); });");
        _current = FindScene(firstScene);
        _started = true;
        if (view != null)
        {
            _view = view;
            Redraw();
        }
    }

    private void RunWindow()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        var window = new GameWindow(this);
        _view = window;
        Redraw();
        app.Run(window);
    }

    /// <summary>O jogador clicou num botão ou respondeu uma pergunta: roda o código dele e redesenha.</summary>
    internal void Act(Action action)
    {
        _news.Clear();
        _pendingScene = null;
        _inAction = true;
        try
        {
            action();
        }
        finally
        {
            _inAction = false;
        }
        if (_pendingScene != null)
        {
            _current = _pendingScene;
            _pendingScene = null;
        }
        Redraw();
    }

    private void Redraw()
    {
        if (_view == null || _current == null) return;
        Screen screen;
        var visited = new List<string>();
        while (true)
        {
            screen = new Screen();
            _building = screen;
            _pendingScene = null;
            try
            {
                _scenes[_current]();
            }
            finally
            {
                _building = null;
            }
            if (_pendingScene == null) break;
            // A cena mandou ir para outra (ex.: if (health <= 0) game.GoTo("GameOver")).
            visited.Add(_current);
            _current = _pendingScene;
            _pendingScene = null;
            if (visited.Count >= MaxRedirects)
                throw new GameException($"As cenas ficam mandando uma para a outra sem parar ({string.Join(" → ", visited.Distinct())}). Confira os game.GoTo dentro das cenas.");
        }
        foreach (var (text, color) in _news)
            screen.Items.Add(new TextItem(text, color, IsNews: true));
        _view.Show(screen);
    }

    private Screen Building(string method) =>
        _building ?? throw new GameException(
            $"game.{method} precisa ficar dentro de uma cena: game.Scene(\"Start\", () => {{ game.{method}(...); }});");

    private string FindScene(string? name)
    {
        name = name?.Trim() ?? "";
        if (_scenes.ContainsKey(name))
            return _scenes.Keys.First(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
        var known = string.Join(", ", _scenes.Keys.Select(k => $"\"{k}\""));
        var guess = _scenes.Keys.Where(k => Distance(k.ToLowerInvariant(), name.ToLowerInvariant()) <= 2).OrderBy(k => Distance(k, name)).FirstOrDefault();
        var hint = guess != null ? $" Você quis dizer \"{guess}\"?" : "";
        return name.Length == 0
            ? throw new GameException($"Faltou o nome da cena. Cenas do jogo: {known}.")
            : throw new GameException($"A cena \"{name}\" não existe.{hint} Cenas do jogo: {known}.");
    }

    /// <summary>Quantas letras mudam de um nome para o outro (para sugerir o nome certo).</summary>
    private static int Distance(string a, string b)
    {
        var row = Enumerable.Range(0, b.Length + 1).ToArray();
        for (int i = 1; i <= a.Length; i++)
        {
            int previous = row[0];
            row[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int temp = row[j];
                row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), previous + (a[i - 1] == b[j - 1] ? 0 : 1));
                previous = temp;
            }
        }
        return row[b.Length];
    }
}
