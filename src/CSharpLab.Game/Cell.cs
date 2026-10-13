namespace CSharpLab.GameEngine;

/// <summary>
/// Cell = casa. Uma casa da Grade, pega com game.Find("Board").Cell(x, y). x é a coluna (0 é a da esquerda) e y é a
/// linha (0 é a de cima). Mude o texto e a cor dela com =.
/// </summary>
/// <example>
/// <code>
/// var cell = game.Find("Board").Cell(1, 2);
/// if (cell.IsEmpty)
/// {
///     cell.Text = "X";
///     cell.Color = Color.Blue;
/// }
/// </code>
/// </example>
public sealed class Cell
{
    private readonly Piece _grid;

    internal Cell(Piece grid, int x, int y)
    {
        _grid = grid;
        X = x;
        Y = y;
    }

    /// <summary>X = a coluna da casa (0 é a da esquerda).</summary>
    public int X { get; }

    /// <summary>Y = a linha da casa (0 é a de cima).</summary>
    public int Y { get; }

    /// <summary>Text = texto. O que aparece na casa ("X", "O", "💣", um número…). Vazio ("") apaga.</summary>
    /// <example><code>game.Find("Board").Cell(x, y).Text = "X";</code></example>
    public string Text
    {
        get => _grid.CellAt(X, Y).Text;
        set => Save(value ?? "", Color);
    }

    /// <summary>Color = cor. A cor da casa (null volta para a cor da Grade).</summary>
    /// <example><code>game.Find("Board").Cell(x, y).Color = Color.Green;</code></example>
    public Color? Color
    {
        get => _grid.CellAt(X, Y).Color;
        set => Save(Text, value);
    }

    /// <summary>IsEmpty = está vazia. true quando a casa não tem texto.</summary>
    /// <example><code>if (game.Find("Board").Cell(x, y).IsEmpty) { ... }</code></example>
    public bool IsEmpty => Text.Length == 0;

    private void Save(string text, Color? color)
    {
        _grid.Cells ??= new();
        if (text.Length == 0 && color == null) _grid.Cells.Remove((X, Y));
        else _grid.Cells[(X, Y)] = new CellData(text, color);
    }

    /// <summary>Mostra "Casa (1, 2): X".</summary>
    public override string ToString() => $"Casa ({X}, {Y}): {(IsEmpty ? "vazia" : Text)}";
}
