namespace CSharpLab.GameEngine;

// O que a cena montou, separado de como é desenhado: a janela WPF lê isto, e os testes também.

internal abstract record ScreenItem;

/// <summary>Texto da cena. <see cref="IsNews"/>: dito depois de um clique (destacado, some no próximo clique).</summary>
internal sealed record TextItem(string Text, Color? Color, bool IsNews) : ScreenItem;

internal sealed record ImageItem(string Path) : ScreenItem;

internal sealed record AskItem(string Question, Action<string> OnAnswer) : ScreenItem;

internal sealed record ButtonItem(string Text, Action OnClick);

internal sealed record BarItem(string Label, int Value, int Max, Color Color);

internal sealed class Screen
{
    public string? Title { get; set; }
    public List<ScreenItem> Items { get; } = [];
    public List<BarItem> Bars { get; } = [];
    public List<ButtonItem> Buttons { get; } = [];

    /// <summary>Cena desenhada na aba Tela (null: a cena monta a tela sozinha, com Write, Button…).</summary>
    public DesignedScene? Designed { get; set; }

    /// <summary>Numa cena desenhada, o que o game.Write escreveu (vai para a peça Mensagens).</summary>
    public IReadOnlyList<MessageLine> Messages =>
        Items.OfType<TextItem>().Select(t => new MessageLine(t.Text, t.Color, t.IsNews)).ToList();
}

/// <summary>
/// Uma visita a uma cena desenhada: as peças começam como no arquivo e guardam as mudanças
/// do código (texto, valor, visível…) até o jogador sair da cena.
/// </summary>
internal sealed class DesignedScene
{
    private readonly Dictionary<string, Item> _items = new(StringComparer.OrdinalIgnoreCase);

    public DesignedScene(string sceneName, ScreenLayout layout)
    {
        SceneName = sceneName;
        Layout = layout;
        foreach (var piece in layout.Pieces) _items[piece.Name] = new Item(piece, sceneName);
    }

    public string SceneName { get; }
    public ScreenLayout Layout { get; }

    public Item? Find(string name) => _items.GetValueOrDefault(name);

    public Item ItemOf(Piece piece) => _items[piece.Name];

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
}
