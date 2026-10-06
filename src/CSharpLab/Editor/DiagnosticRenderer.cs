using System.Windows;
using System.Windows.Media;
using CSharpLab.Core.Language;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace CSharpLab.Editor;

public sealed class DiagnosticMarker : TextSegment
{
    public required CodeDiagnostic Diagnostic { get; init; }
}

/// <summary>Sublinhado ondulado de erros e avisos, desenhado por baixo do texto (não desloca linhas).</summary>
public sealed class DiagnosticRenderer : IBackgroundRenderer
{
    private static readonly Pen ErrorPen = MakePen(SyntaxTheme.ErrorColor);
    private static readonly Pen WarningPen = MakePen(SyntaxTheme.WarningColor);
    private static readonly Pen HintPen = MakePen((Color)ColorConverter.ConvertFromString("#7C8CF8"));

    private readonly TextDocument _document;

    public DiagnosticRenderer(TextDocument document)
    {
        _document = document;
        Markers = new TextSegmentCollection<DiagnosticMarker>(document);
    }

    public TextSegmentCollection<DiagnosticMarker> Markers { get; }

    public KnownLayer Layer => KnownLayer.Selection;

    private static Pen MakePen(Color color)
    {
        var pen = new Pen(new SolidColorBrush(color), 1.1);
        pen.Freeze();
        return pen;
    }

    public void SetDiagnostics(IEnumerable<CodeDiagnostic> diagnostics)
    {
        Markers.Clear();
        int length = _document.TextLength;
        foreach (var d in diagnostics)
        {
            int start, len;
            if (!d.FromBuild && d.Start >= 0)
            {
                start = d.Start;
                len = d.Length;
            }
            else if (d.Line >= 1 && d.Line <= _document.LineCount)
            {
                var line = _document.GetLineByNumber(d.Line);
                start = line.Offset + Math.Clamp(d.Column - 1, 0, line.Length);
                len = 0;
            }
            else
            {
                continue;
            }

            if (start > length) continue;
            if (len == 0)
            {
                // Erro num ponto (ex.: falta de ";"): marca o caractere anterior.
                if (start > 0 && (start >= length || char.IsWhiteSpace(_document.GetCharAt(start)))) start--;
                len = 1;
            }
            len = Math.Min(len, length - start);
            if (len <= 0) continue;
            Markers.Add(new DiagnosticMarker { StartOffset = start, Length = len, Diagnostic = d });
        }
    }

    public IEnumerable<DiagnosticMarker> At(int offset) =>
        Markers.FindSegmentsContaining(offset).Concat(Markers.FindSegmentsContaining(Math.Max(0, offset - 1))).Distinct()
            .OrderBy(m => m.Diagnostic.Level);

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!textView.VisualLinesValid || Markers.Count == 0) return;
        var visual = textView.VisualLines;
        if (visual.Count == 0) return;
        int viewStart = visual[0].FirstDocumentLine.Offset;
        int viewEnd = visual[^1].LastDocumentLine.EndOffset;

        // Avisos primeiro, erros por cima.
        foreach (var marker in Markers.FindOverlappingSegments(viewStart, viewEnd - viewStart).OrderByDescending(m => m.Diagnostic.Level))
        {
            var pen = marker.Diagnostic.Level == DiagnosticLevel.Error ? ErrorPen
                : marker.Diagnostic.Id.StartsWith("DICA", StringComparison.Ordinal) ? HintPen
                : WarningPen;
            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, marker))
            {
                var geometry = new StreamGeometry();
                using (var ctx = geometry.Open())
                {
                    double y = rect.Bottom - 1.5;
                    double x = rect.Left;
                    double end = Math.Max(rect.Right, rect.Left + 6);
                    ctx.BeginFigure(new Point(x, y), false, false);
                    bool up = true;
                    while (x < end)
                    {
                        x += 2.5;
                        ctx.LineTo(new Point(Math.Min(x, end), up ? y - 2 : y), true, true);
                        up = !up;
                    }
                }
                geometry.Freeze();
                drawingContext.DrawGeometry(null, pen, geometry);
            }
        }
    }
}
