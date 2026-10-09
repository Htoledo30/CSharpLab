namespace CSharpLab.GameEngine;

// O que a cena montou, separado de como é desenhado: a janela WPF lê isto, e os testes também.

internal abstract record ScreenItem;

/// <summary>Texto da cena. <see cref="IsNews"/>: dito depois de um clique (destacado, some no próximo clique).</summary>
internal sealed record TextItem(string Text, Color? Color, bool IsNews) : ScreenItem;

internal sealed record ImageItem(string Path) : ScreenItem;

internal sealed record AskItem(string Question, Action<string> OnAnswer) : ScreenItem;

internal sealed record ButtonItem(string Text, Action OnClick);

internal sealed record BarItem(string Label, int Value, int Max, Color Color);

/// <summary>Um movimento curto de uma peça (tremer ou piscar), tocado uma vez quando a tela aparece.</summary>
internal enum Effect { Shake, Flash }

internal sealed class Screen
{
    private IReadOnlyList<MessageLine>? _messages;

    /// <summary>A cena que montou esta tela (null nos testes que montam a tela na mão).</summary>
    public string? SceneName { get; set; }

    public string? Title { get; set; }
    public List<ScreenItem> Items { get; } = [];
    public List<BarItem> Bars { get; } = [];
    public List<ButtonItem> Buttons { get; } = [];

    /// <summary>Cena desenhada no Estúdio (null: a cena monta a tela sozinha, com Write, Button…).</summary>
    public DesignedScene? Designed { get; set; }

    /// <summary>
    /// Numa cena desenhada, o que vai para a peça Mensagens: o que a cena escreveu e o histórico dos cliques
    /// (as do último clique destacadas, as antigas apagadas). Sem histórico, os textos da cena.
    /// </summary>
    public IReadOnlyList<MessageLine> Messages
    {
        get => _messages ?? Items.OfType<TextItem>().Select(t => new MessageLine(t.Text, t.Color, t.IsNews)).ToList();
        set => _messages = value;
    }

    /// <summary>As mensagens do clique atual, para o aviso por cima da tela quando ela não tem a peça Mensagens.</summary>
    public IReadOnlyList<MessageLine>? Toast { get; set; }

    /// <summary>Peças que tremem ou piscam quando esta tela aparecer.</summary>
    public List<(Piece Piece, Effect Effect)> Effects { get; } = [];
}

/// <summary>
/// Uma visita a uma cena desenhada: as peças começam como no arquivo e guardam as mudanças
/// do código (texto, valor, visível…) até o jogador sair da cena.
/// </summary>
internal sealed class DesignedScene
{
    /// <summary>Quantas mensagens a peça Mensagens guarda (as mais antigas vão saindo).</summary>
    public const int HistoryLimit = 40;

    private readonly Dictionary<string, Item> _items = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Piece, Item> _byPiece = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, Piece> _cardPieces = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(Piece Piece, Effect Effect)> _effects = [];

    public DesignedScene(string sceneName, ScreenLayout layout)
    {
        SceneName = sceneName;
        Layout = layout;
        foreach (var piece in layout.Pieces)
        {
            // As peças do cartão modelo de uma Lista não aparecem sozinhas: viram cartões pelo Show.
            if (piece.List != null)
            {
                _cardPieces[piece.Name] = piece;
                continue;
            }
            var item = new Item(piece, sceneName, this);
            _items[piece.Name] = item;
            _byPiece[piece] = item;
        }
    }

    public string SceneName { get; }
    public ScreenLayout Layout { get; }

    /// <summary>As mensagens dos cliques nesta visita, com o número do lote (cada clique ou game.Wait é um lote).</summary>
    public List<(MessageLine Line, int Batch)> History { get; } = [];

    public Item? Find(string name) => _items.GetValueOrDefault(name);

    /// <summary>A peça faz parte do cartão modelo de uma Lista (para o erro do game.Find explicar).</summary>
    public Piece? CardPiece(string name) => _cardPieces.GetValueOrDefault(name);

    public Item ItemOf(Piece piece) => _byPiece.TryGetValue(piece, out var item) ? item : _items[piece.Name];

    /// <summary>A peça desenhada (solta ou de um cartão) que o jogador clicou.</summary>
    public Item? ItemFor(Piece piece) => _byPiece.GetValueOrDefault(piece);

    /// <summary>Os cartões que o Show criou para a Lista (cada um com as peças já preenchidas).</summary>
    public IReadOnlyList<Card> CardsOf(Piece list) => ItemFor(list)?.Cards ?? [];

    /// <summary>Um cartão novo da Lista: uma cópia de cada peça do cartão modelo.</summary>
    public Card NewCard(Item list, int index)
    {
        var pieces = Layout.MembersOf(list.Name).Select(p => p.Clone()).ToList();
        var items = pieces.Select(p => new Item(p, SceneName, this) { CardOf = list.Name }).ToList();
        return new Card(list.Name, index, items);
    }

    /// <summary>O Show trocou os cartões da Lista: os cliques passam a ir para as peças novas.</summary>
    public void ReplaceCards(IReadOnlyList<Card> old, IReadOnlyList<Card> cards)
    {
        foreach (var item in old.SelectMany(c => c.Items)) _byPiece.Remove(item.Piece);
        foreach (var item in cards.SelectMany(c => c.Items)) _byPiece[item.Piece] = item;
    }

    public void AddEffect(Piece piece, Effect effect) => _effects.Add((piece, effect));

    public List<(Piece Piece, Effect Effect)> TakeEffects()
    {
        var effects = _effects.ToList();
        _effects.Clear();
        return effects;
    }

    public void AddHistory(IEnumerable<(string Text, Color? Color)> lines, int batch)
    {
        foreach (var (text, color) in lines) History.Add((new MessageLine(text, color, false), batch));
        if (History.Count > HistoryLimit) History.RemoveRange(0, History.Count - HistoryLimit);
    }

    /// <summary>Cada desenho da cena liga os cliques de novo: os antigos deixam de valer.</summary>
    public void ClearHandlers()
    {
        foreach (var item in _items.Values)
        {
            item.Click = null;
            item.Answer = null;
        }
    }
}

/// <summary>Quem desenha a tela: a janela do jogo, ou uma falsa nos testes.</summary>
internal interface IGameView
{
    void Show(Screen screen);

    /// <summary>game.Wait: mostra a tela por um tempinho antes do resto do clique (nos testes, não espera).</summary>
    void Pause(double seconds) { }

    /// <summary>game.Pause: espera o jogador apertar o botão (nos testes, continua na hora).</summary>
    void WaitForPlayer(string button) { }

    /// <summary>game.Read: pergunta e espera a resposta (nos testes, uma resposta vazia).</summary>
    string ReadAnswer(string question) => "";

    /// <summary>game.Choose: mostra as opções e devolve a posição da escolhida (nos testes, a primeira).</summary>
    int ChooseOption(string question, IReadOnlyList<string> options) => 0;

    /// <summary>game.Close: fecha a janela.</summary>
    void CloseGame() { }
}
