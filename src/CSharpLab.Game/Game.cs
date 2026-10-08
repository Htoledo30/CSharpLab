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
///     game.Write("Você acorda na frente de uma torre.");
///     game.Button("Entrar", () => game.GoTo("Hall"));
/// });
///
/// game.Start("Start");
/// </code>
/// </example>
public sealed class Game
{
    private const int MaxRedirects = 20;

    private readonly Dictionary<string, Action> _scenes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string Text, Color? Color)> _news = [];
    private int _flushed;            // quantas mensagens do _news já foram para o histórico da cena
    private int _batch;              // cada clique (ou game.Wait) que escreveu algo é um lote de mensagens
    private int _highlight = -1;     // o lote destacado na peça Mensagens: o último do clique atual
    private int _actionFirstBatch;   // primeiro lote do clique atual (o aviso por cima da tela mostra o clique todo)
    private bool _closed;            // a janela fechou no meio de um game.Wait
    private bool _arriving;          // este desenho da cena é a chegada nela: o OnEnter roda
    private bool _inEnter;           // rodando um OnEnter
    private Screen? _building;
    private string? _current;
    private string? _pendingScene;
    private bool _inAction;
    private bool _started;
    private bool _entering;
    private DesignedScene? _designed;
    private IGameView? _view;

    /// <summary>De onde vêm as telas desenhadas (os testes trocam a pasta).</summary>
    internal ScreenLibrary Screens { get; set; } = new();

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
    /// <param name="build">O que aparece na cena: game.Write, game.Button, game.Bar…</param>
    /// <example>
    /// <code>
    /// game.Scene("Forest", () =>
    /// {
    ///     game.Write("Árvores por todo lado.");
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
            throw new GameException("game.GoTo só funciona com o jogo rodando. Para escolher a primeira cena, use game.Start(\"" + (name ?? "") + "\").");
        var scene = FindScene(name);
        if (_inAction || _building != null)
        {
            // Depois do clique (ou desta cena) a tela é desenhada de novo, já na cena nova.
            _pendingScene = scene;
            return;
        }
        _current = scene;
        _entering = true;
        Redraw();
    }

    /// <summary>
    /// Find = encontrar. Pega uma peça desenhada na aba Tela pelo nome, para mudar ela pelo código.
    /// </summary>
    /// <param name="name">O nome da peça na aba Tela. Exemplo: "Attack".</param>
    /// <example>
    /// <code>
    /// game.Scene("Fight", () =>
    /// {
    ///     game.Find("PlayerHealth").Value = health;
    ///     game.Find("Attack").OnClick(() => enemyHealth -= 10);
    /// });
    /// </code>
    /// </example>
    public Item Find(string name)
    {
        if (!_started)
            throw new GameException("game.Find funciona dentro das cenas: game.Scene(\"Fight\", () => { game.Find(\"Attack\").OnClick(...); });");
        var scene = _designed ?? throw new GameException(
            $"A cena \"{_current}\" não foi desenhada na aba Tela, então não tem peças para o game.Find. " +
            $"Crie a tela em Arquivo → Nova tela… (o arquivo {ScreenLibrary.Folder}/{_current}.json) ou use game.Write e game.Button.");
        name = name?.Trim() ?? "";
        if (scene.Find(name) is { } item) return item;
        if (scene.CardPiece(name) is { } member)
            throw new GameException(
                $"A peça \"{member.Name}\" faz parte do cartão da lista \"{member.List}\", que se repete para cada item. " +
                $"Mude ela dentro do Show: game.Find(\"{member.List}\").Show(items, (card, item) => {{ card.Find(\"{member.Name}\").Text = ...; }});");

        var names = scene.Layout.Pieces.Where(p => p.List == null).Select(p => p.Name).ToList();
        var guess = Guess(names, name);
        var hint = guess != null ? $" Você quis dizer \"{guess}\"?" : "";
        var list = names.Count > 0 ? $" Peças da tela: {string.Join(", ", names.Select(n => $"\"{n}\""))}." : " A tela ainda não tem peças.";
        throw new GameException(name.Length == 0
            ? $"Faltou o nome da peça no game.Find.{list}"
            : $"A peça \"{name}\" não existe na tela \"{scene.SceneName}\".{hint}{list}");
    }

    /// <summary>
    /// Background = fundo. A imagem de fundo da tela desenhada (da pasta Assets). Começa com a escolhida
    /// na aba Tela; mude pelo código para a mesma tela servir a lugares diferentes.
    /// </summary>
    /// <example>
    /// <code>
    /// game.Scene("Fight", () =>
    /// {
    ///     game.Background = enemy.IsBoss ? "tower.png" : "forest.png";
    /// });
    /// </code>
    /// </example>
    public string? Background
    {
        get => _designed?.Layout.Background;
        set
        {
            var scene = _designed ?? throw new GameException(
                "game.Background é o fundo de uma tela desenhada. Esta cena não tem tela: crie em Arquivo → Nova tela do jogo….");
            scene.Layout.Background = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }

    /// <summary>
    /// Wait = esperar. Dentro de um clique, mostra a tela como está e espera um pouco antes de continuar:
    /// você ataca, a tela mostra o golpe e o inimigo responde logo depois. O jogador não clica durante a pausa.
    /// </summary>
    /// <param name="seconds">Quanto esperar, em segundos (até 5). Exemplo: 0.6.</param>
    /// <example>
    /// <code>
    /// game.Find("Attack").OnClick(() =>
    /// {
    ///     enemyHealth -= 7;
    ///     game.Write("Você causou 7 de dano!");
    ///     game.Wait(0.6);
    ///     health -= 4;
    ///     game.Write("O goblin revida: -4 de vida.", Color.Red);
    /// });
    /// </code>
    /// </example>
    public void Wait(double seconds)
    {
        if (!_started || _building != null || !_inAction)
            throw new GameException(
                "game.Wait funciona dentro de um clique (OnClick, OnAnswer ou game.Button), para fazer uma pausa no meio dele: " +
                "game.Find(\"Attack\").OnClick(() => { ...; game.Wait(0.6); ... });");
        if (double.IsNaN(seconds) || seconds < 0 || seconds > MaxWait)
            throw new GameException($"game.Wait espera de 0 a {MaxWait} segundos (veio {seconds}). Para meio segundo: game.Wait(0.5);");
        // Mostra o que o clique já fez (inclusive a troca de cena) antes da pausa.
        if (_pendingScene != null)
        {
            _current = _pendingScene;
            _pendingScene = null;
            _entering = true;
        }
        Redraw();
        _view?.Pause(seconds);
        if (_closed) throw new GameClosedException();
    }

    private const double MaxWait = 5;

    /// <summary>A janela fechou durante um game.Wait: o resto do clique não roda.</summary>
    private sealed class GameClosedException : Exception;

    /// <summary>Title = título. Texto grande no alto da cena.</summary>
    /// <param name="text">Exemplo: "Capítulo 1".</param>
    public void Title(string text) => Building(nameof(Title)).Title = text ?? "";

    /// <summary>
    /// Write = escrever. Mostra um texto na tela. Dentro de um botão, o texto aparece destacado
    /// depois do clique (bom para "Você causou 7 de dano!").
    /// </summary>
    /// <param name="text">O texto. Use $"..." para mostrar variáveis: $"Ouro: {gold}".</param>
    /// <param name="color">Cor opcional. Exemplo: Color.Red.</param>
    public void Write(string text, Color? color = null)
    {
        text ??= "";
        // No OnEnter, o que se escreve é novidade da chegada (como num clique), não um texto fixo da cena.
        if (_building != null && !_inEnter) _building.Items.Add(new TextItem(text, color, IsNews: false));
        else _news.Add((text, color));
    }

    /// <summary>
    /// OnEnter = ao entrar. O código entre as chaves roda uma vez cada vez que o jogador chega na cena,
    /// e não nos cliques dentro dela. Use para preparar a visita: sortear um inimigo, dar a
    /// recompensa da chegada, contar quantas vezes ele veio. O resto da cena continua rodando a cada clique.
    /// </summary>
    /// <param name="prepare">O que fazer ao chegar: () => { ... }</param>
    /// <example>
    /// <code>
    /// game.Scene("Fight", () =>
    /// {
    ///     game.OnEnter(() =>
    ///     {
    ///         enemyHealth = 30;
    ///         game.Write("Um goblin aparece!");
    ///     });
    ///     game.Find("EnemyHealth").Value = enemyHealth;
    /// });
    /// </code>
    /// </example>
    public void OnEnter(Action prepare)
    {
        if (_building == null || _inEnter)
            throw new GameException(
                "game.OnEnter fica direto dentro da cena, fora dos botões: " +
                "game.Scene(\"Fight\", () => { game.OnEnter(() => { enemyHealth = 30; }); ... });");
        if (prepare == null)
            throw new GameException("O game.OnEnter precisa dizer o que fazer ao chegar. Exemplo: game.OnEnter(() => { enemyHealth = 30; });");
        if (!_arriving) return;
        _inEnter = true;
        try
        {
            prepare();
        }
        finally
        {
            _inEnter = false;
        }
    }

    /// <summary>Button = botão. Cria um botão; o código entre as chaves roda quando o jogador clica.</summary>
    /// <param name="text">O que está escrito no botão. Exemplo: "Atacar".</param>
    /// <param name="onClick">O que acontece no clique: () => { ... }</param>
    /// <example>
    /// <code>
    /// game.Button("Beber poção", () =>
    /// {
    ///     health += 20;
    ///     game.Write("Você se sente melhor.");
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
    /// <param name="color">Cor da barra. Exemplo: Color.Red.</param>
    public void Bar(string label, int value, int max, Color color = Color.Green)
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
    /// Start = começar. Abre a janela do jogo na cena escolhida. Fica sempre no fim do arquivo:
    /// o programa continua aqui até o jogador fechar a janela.
    /// </summary>
    /// <param name="firstScene">A cena que aparece primeiro. Exemplo: "Start".</param>
    public void Start(string firstScene)
    {
        if (StartForTests is { } play)
        {
            play(this, firstScene);
            return;
        }
        Begin(firstScene);
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

    /// <summary>
    /// Para os testes jogarem um programa de verdade sem abrir janela: no lugar da janela, o Start
    /// entrega o jogo para o teste (que chama <see cref="Begin"/> com uma tela falsa).
    /// </summary>
    internal static Action<Game, string>? StartForTests { get; set; }

    /// <summary>Confere tudo antes de abrir a janela, para o erro apontar a linha do Start.</summary>
    internal void Begin(string firstScene, IGameView? view = null)
    {
        if (_started)
            throw new GameException("game.Start só pode ser chamado uma vez, no fim do arquivo.");
        if (_scenes.Count == 0)
            throw new GameException("O jogo não tem nenhuma cena. Crie uma antes do Start: game.Scene(\"Start\", () => { game.Write(\"Olá!\"); });");
        _current = FindScene(firstScene);
        _started = true;
        _entering = true;
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
        window.Closed += (_, _) => Close();   // um game.Wait em andamento para junto
        _view = window;
        Redraw();
        app.Run(window);
    }

    /// <summary>O jogador clicou num botão ou respondeu uma pergunta: roda o código dele e redesenha.</summary>
    internal void Act(Action action)
    {
        // Durante um game.Wait (ou a troca de cena), cliques e teclas esperam o clique atual terminar.
        if (_inAction || _closed) return;
        _news.Clear();
        _flushed = 0;
        _highlight = -1;
        _actionFirstBatch = _batch + 1;
        _pendingScene = null;
        _inAction = true;
        try
        {
            action();
        }
        catch (GameClosedException)
        {
            return;
        }
        finally
        {
            _inAction = false;
        }
        if (_pendingScene != null)
        {
            _current = _pendingScene;
            _pendingScene = null;
            _entering = true;
        }
        Redraw();
    }

    /// <summary>Clique numa peça desenhada (solta ou de um cartão): roda o OnClick dela, ou explica que ele ainda falta.</summary>
    internal void ClickPiece(Piece piece)
    {
        if (!piece.Enabled) return;
        var item = ItemFor(piece);
        if (item?.Click is { } click)
        {
            Act(click);
            return;
        }
        var find = item?.CardOf != null ? "card.Find" : "game.Find";
        Act(() => Write($"O botão \"{piece.Name}\" ainda não faz nada. Na cena, escreva: {find}(\"{piece.Name}\").OnClick(() => {{ ... }});", Color.Gray));
    }

    /// <summary>Resposta num Campo de escrita desenhado.</summary>
    internal void AnswerPiece(Piece piece, string answer)
    {
        if (!piece.Enabled) return;
        var item = ItemFor(piece);
        if (item?.Answer is { } onAnswer)
        {
            Act(() => onAnswer(answer));
            return;
        }
        var find = item?.CardOf != null ? "card.Find" : "game.Find";
        Act(() => Write($"O campo \"{piece.Name}\" ainda não faz nada com a resposta. Na cena, escreva: {find}(\"{piece.Name}\").OnAnswer(answer => {{ ... }});", Color.Gray));
    }

    /// <summary>A peça tem OnClick no código (só essas ganham a mãozinha do mouse, no caso das imagens).</summary>
    internal bool IsClickable(Piece piece) => ItemFor(piece)?.Click != null;

    /// <summary>Os cartões que o Show pôs na Lista (para desenhar).</summary>
    internal IReadOnlyList<Card> CardsOf(Piece list) => _designed?.CardsOf(list) ?? [];

    private Item? ItemFor(Piece piece) => _designed?.ItemFor(piece) ?? _designed?.Find(piece.Name);

    /// <summary>A janela fechou: um game.Wait em andamento para, e nada mais roda.</summary>
    internal void Close() => _closed = true;

    private void Redraw()
    {
        if (_view == null || _current == null) return;
        Screen screen;
        var visited = new List<string>();
        while (true)
        {
            screen = new Screen();
            // Ao entrar na cena, as peças desenhadas começam como no arquivo; depois guardam as mudanças do código.
            if (_entering || _designed?.SceneName != _current)
            {
                var layout = Screens.Load(_current);
                _designed = layout != null ? new DesignedScene(_current, layout) : null;
            }
            // Chegou agora (game.Start, GoTo ou redirecionamento): o OnEnter roda neste desenho, e só nele.
            _arriving = _entering;
            _entering = false;
            _designed?.ClearHandlers();
            screen.Designed = _designed;
            _building = screen;
            _pendingScene = null;
            try
            {
                _scenes[_current]();
            }
            finally
            {
                _building = null;
                _arriving = false;
            }
            if (_pendingScene == null) break;
            // A cena mandou ir para outra (ex.: if (health <= 0) game.GoTo("GameOver")).
            visited.Add(_current);
            _current = _pendingScene;
            _pendingScene = null;
            _entering = true;
            if (visited.Count >= MaxRedirects)
                throw new GameException($"As cenas ficam mandando uma para a outra sem parar ({string.Join(" → ", visited.Distinct())}). Confira os game.GoTo dentro das cenas.");
        }
        screen.SceneName = _current;
        if (_designed is { } designed)
        {
            // As mensagens novas entram no histórico da cena (cada clique ou game.Wait é um lote).
            if (_flushed < _news.Count)
            {
                _batch++;
                _highlight = _batch;
                designed.AddHistory(_news.Skip(_flushed), _batch);
                _flushed = _news.Count;
            }
            var lines = screen.Items.OfType<TextItem>().Select(t => new MessageLine(t.Text, t.Color, false)).ToList();
            foreach (var (line, batch) in designed.History)
                lines.Add(line with { IsNews = batch == _highlight, IsOld = batch != _highlight });
            screen.Messages = lines;
            screen.Toast = designed.History.Where(h => h.Batch >= _actionFirstBatch).Select(h => h.Line with { IsNews = true }).ToList();
            screen.Effects.AddRange(designed.TakeEffects());
        }
        else
        {
            foreach (var (text, color) in _news)
                screen.Items.Add(new TextItem(text, color, IsNews: true));
        }
        _view.Show(screen);
    }

    private Screen Building(string method)
    {
        var screen = _building ?? throw new GameException(
            $"game.{method} precisa ficar dentro de uma cena: game.Scene(\"Start\", () => {{ game.{method}(...); }});");
        if (screen.Designed != null)
            throw new GameException(
                $"A cena \"{_current}\" foi desenhada na aba Tela, então o game.{method} não funciona nela. " +
                "Desenhe a peça na Tela e mude ela pelo código com game.Find(\"Nome\").");
        return screen;
    }

    private string FindScene(string? name)
    {
        name = name?.Trim() ?? "";
        if (_scenes.ContainsKey(name))
            return _scenes.Keys.First(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
        var known = string.Join(", ", _scenes.Keys.Select(k => $"\"{k}\""));
        var guess = Guess(_scenes.Keys, name);
        var hint = guess != null ? $" Você quis dizer \"{guess}\"?" : "";
        return name.Length == 0
            ? throw new GameException($"Faltou o nome da cena. Cenas do jogo: {known}.")
            : throw new GameException($"A cena \"{name}\" não existe.{hint} Cenas do jogo: {known}.");
    }

    /// <summary>O nome parecido (até 2 letras diferentes), para o "Você quis dizer…?".</summary>
    internal static string? Guess(IEnumerable<string> names, string name) =>
        names.Where(n => Distance(n.ToLowerInvariant(), name.ToLowerInvariant()) <= 2)
            .OrderBy(n => Distance(n.ToLowerInvariant(), name.ToLowerInvariant()))
            .FirstOrDefault();

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
