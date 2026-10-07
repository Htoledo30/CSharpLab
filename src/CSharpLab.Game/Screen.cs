namespace CSharpLab.GameEngine;

// O que a cena montou, separado de como é desenhado: a janela WPF lê isto, e os testes também.

internal abstract record ScreenItem;

/// <summary>Texto da cena. <see cref="IsNews"/>: dito depois de um clique (destacado, some no próximo clique).</summary>
internal sealed record TextItem(string Text, GameColor? Color, bool IsNews) : ScreenItem;

internal sealed record ImageItem(string Path) : ScreenItem;

internal sealed record AskItem(string Question, Action<string> OnAnswer) : ScreenItem;

internal sealed record ButtonItem(string Text, Action OnClick);

internal sealed record BarItem(string Label, int Value, int Max, GameColor Color);

internal sealed class Screen
{
    public string? Title { get; set; }
    public List<ScreenItem> Items { get; } = [];
    public List<BarItem> Bars { get; } = [];
    public List<ButtonItem> Buttons { get; } = [];
}

/// <summary>Quem desenha a tela: a janela do jogo, ou uma falsa nos testes.</summary>
internal interface IGameView
{
    void Show(Screen screen);
}
