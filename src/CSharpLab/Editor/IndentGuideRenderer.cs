using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace CSharpLab.Editor;

/// <summary>
/// Linhas-guia de recuo: um traço vertical fino em cada nível do recuo, para ver onde cada bloco começa e
/// termina. O bloco onde está o cursor ganha a guia mais clara. Linhas vazias seguem o recuo das vizinhas
/// (a guia não se quebra numa linha em branco dentro do bloco).
/// </summary>
public sealed class IndentGuideRenderer(Func<int> indentSize, Func<int> tabSize) : IBackgroundRenderer
{
    private (int FirstLine, int LastLine, int Column)? _active;

    public KnownLayer Layer => KnownLayer.Background;

    /// <summary>A guia do bloco do cursor: linhas (de-até) e coluna. True se mudou (precisa redesenhar).</summary>
    public bool SetActive((int FirstLine, int LastLine, int Column)? active)
    {
        if (_active == active) return false;
        _active = active;
        return true;
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        var document = textView.Document;
        if (document == null || !textView.VisualLinesValid || textView.VisualLines.Count == 0) return;
        int size = Math.Max(1, indentSize());
        double charWidth = textView.WideSpaceWidth;
        double offsetX = textView.ScrollOffset.X;
        double offsetY = textView.ScrollOffset.Y;
        double pixel = PixelSize(textView);
        // Um pixel de verdade em qualquer escala do Windows (125%, 150%…), sem borrar.
        var normal = new Pen(SyntaxTheme.IndentGuide, pixel);
        var highlighted = new Pen(SyntaxTheme.IndentGuideActive, pixel);
        normal.Freeze();
        highlighted.Freeze();

        foreach (var visual in textView.VisualLines)
        {
            var line = visual.FirstDocumentLine;
            int indent = IndentOf(document, line);
            double top = visual.VisualTop - offsetY;
            double bottom = top + visual.Height;
            for (int column = 0; column < indent; column += size)
            {
                double x = Math.Round((column * charWidth - offsetX) / pixel) * pixel + pixel / 2;
                if (x < 0) continue;
                bool active = _active is { } a && a.Column == column && line.LineNumber >= a.FirstLine && line.LineNumber <= a.LastLine;
                drawingContext.DrawLine(active ? highlighted : normal, new Point(x, top), new Point(x, bottom));
            }
        }
    }

    private static double PixelSize(Visual visual) =>
        PresentationSource.FromVisual(visual)?.CompositionTarget is { } target ? 1 / target.TransformToDevice.M11 : 1;

    /// <summary>Recuo (em colunas) com o que a linha mostra; linha vazia segue as vizinhas, como no VS Code.</summary>
    private int IndentOf(TextDocument document, DocumentLine line)
    {
        if (Columns(document, line) is { } own) return own;
        int above = -1, below = -1;
        int steps = 0;
        for (var l = line.PreviousLine; l != null && steps++ < 300; l = l.PreviousLine)
        {
            if (Columns(document, l) is { } c) { above = c; break; }
        }
        steps = 0;
        for (var l = line.NextLine; l != null && steps++ < 300; l = l.NextLine)
        {
            if (Columns(document, l) is { } c) { below = c; break; }
        }
        if (above < 0 || below < 0) return 0;
        int size = Math.Max(1, indentSize());
        return above == below ? above : Math.Min(above, below) + size;
    }

    /// <summary>Colunas do recuo de uma linha com texto; null se a linha está vazia (ou só tem espaços).</summary>
    private int? Columns(TextDocument document, DocumentLine line)
    {
        int column = 0, tab = Math.Max(1, tabSize());
        for (int i = line.Offset; i < line.EndOffset; i++)
        {
            char c = document.GetCharAt(i);
            if (c == ' ') column++;
            else if (c == '\t') column += tab - column % tab;
            else return column;
        }
        return null;
    }

    /// <summary>Guia do bloco entre um "{" e o "}" dele: da linha seguinte ao "{" até a anterior ao "}".</summary>
    public static (int FirstLine, int LastLine, int Column)? BlockGuide(TextDocument document, int open, int close, int indentSize, int tabSize)
    {
        var openLine = document.GetLineByOffset(open);
        var closeLine = document.GetLineByOffset(close);
        if (closeLine.LineNumber - openLine.LineNumber < 2) return null;
        int column = 0;
        for (int i = openLine.Offset; i < openLine.EndOffset; i++)
        {
            char c = document.GetCharAt(i);
            if (c == ' ') column++;
            else if (c == '\t') column += tabSize - column % tabSize;
            else break;
        }
        // A guia fica na coluna do recuo da linha do "{" (o conteúdo do bloco está um nível depois dela).
        return (openLine.LineNumber + 1, closeLine.LineNumber - 1, column - column % Math.Max(1, indentSize));
    }
}
