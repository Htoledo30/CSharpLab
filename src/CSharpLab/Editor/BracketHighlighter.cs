using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace CSharpLab.Editor;

/// <summary>Destaque discreto do par de delimitadores junto ao cursor.</summary>
public sealed class BracketHighlighter : IBackgroundRenderer
{
    private (int Open, int Close)? _pair;

    public KnownLayer Layer => KnownLayer.Selection;

    public bool SetPair((int Open, int Close)? pair)
    {
        if (_pair == pair) return false;
        _pair = pair;
        return true;
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_pair is not { } pair || textView.Document == null) return;
        foreach (var offset in new[] { pair.Open, pair.Close })
        {
            if (offset < 0 || offset >= textView.Document.TextLength) continue;
            var builder = new BackgroundGeometryBuilder { CornerRadius = 2, AlignToWholePixels = true };
            builder.AddSegment(textView, new TextSegment { StartOffset = offset, Length = 1 });
            var geometry = builder.CreateGeometry();
            if (geometry != null)
                drawingContext.DrawGeometry(SyntaxTheme.BracketMatch, SyntaxTheme.BracketPen, geometry);
        }
    }
}

/// <summary>Destaque das ocorrências da busca.</summary>
public sealed class SearchHighlighter : IBackgroundRenderer
{
    public List<(int Start, int Length)> Matches { get; } = [];
    public int Current { get; set; } = -1;

    public KnownLayer Layer => KnownLayer.Selection;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (Matches.Count == 0 || !textView.VisualLinesValid || textView.VisualLines.Count == 0) return;
        int viewStart = textView.VisualLines[0].FirstDocumentLine.Offset;
        int viewEnd = textView.VisualLines[^1].LastDocumentLine.EndOffset;
        for (int i = 0; i < Matches.Count; i++)
        {
            var (start, length) = Matches[i];
            if (start + length < viewStart || start > viewEnd) continue;
            var builder = new BackgroundGeometryBuilder { CornerRadius = 2, AlignToWholePixels = true };
            builder.AddSegment(textView, new TextSegment { StartOffset = start, Length = Math.Max(1, length) });
            var geometry = builder.CreateGeometry();
            if (geometry != null)
                drawingContext.DrawGeometry(i == Current ? SyntaxTheme.SearchCurrent : SyntaxTheme.SearchMatch, null, geometry);
        }
    }
}
