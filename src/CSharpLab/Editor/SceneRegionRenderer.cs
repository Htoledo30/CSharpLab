using System.Windows;
using System.Windows.Media;
using CSharpLab.Core.Language;
using ICSharpCode.AvalonEdit.Rendering;

namespace CSharpLab.Editor;

/// <summary>
/// No Estúdio: um fundo bem leve e uma faixa na esquerda marcam o game.Scene da cena aberta, para ver
/// de relance onde começa e onde termina o código dela.
/// </summary>
public sealed class SceneRegionRenderer : IBackgroundRenderer
{
    private static readonly Brush Fill = Frozen(new SolidColorBrush(Color.FromArgb(0x0E, 0x7C, 0x8C, 0xF8)));
    private static readonly Brush Band = Frozen(new SolidColorBrush(Color.FromArgb(0xB0, 0x7C, 0x8C, 0xF8)));
    private static readonly Brush MarkFill = Frozen(new SolidColorBrush(Color.FromArgb(0x55, 0xF2, 0xC1, 0x4E)));
    private static readonly Pen MarkPen = FrozenPen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0xCC, 0xF2, 0xC1, 0x4E))), 1));

    private readonly CodeEditor _editor;
    private int _version = -1;
    private (int Start, int End)? _span;

    public SceneRegionRenderer(CodeEditor editor) => _editor = editor;

    public KnownLayer Layer => KnownLayer.Background;

    /// <summary>O nome da cena marcada (null: nenhuma).</summary>
    public string? Scene { get; private set; }

    public void SetScene(string? scene)
    {
        if (Scene == scene) return;
        Scene = scene;
        _version = -1;
        _editor.TextArea.TextView.InvalidateLayer(Layer);
    }

    /// <summary>Onde está o game.Scene da cena marcada (o trecho inteiro, com o corpo).</summary>
    public (int Start, int End)? Span
    {
        get
        {
            if (Scene == null) return null;
            if (_version != _editor.Doc.Version)
            {
                _version = _editor.Doc.Version;
                _span = null;
                try
                {
                    var call = GameAssist.FindScenes(_editor.Syntax.Root).FirstOrDefault(s => s.Name == Scene)
                               ?? GameAssist.FindScenes(_editor.Syntax.Root).FirstOrDefault(s => string.Equals(s.Name, Scene, StringComparison.OrdinalIgnoreCase));
                    if (call != null) _span = (call.Span.Start, call.Span.End);
                }
                catch
                {
                    // Texto no meio da digitação: fica sem a marca até a próxima mudança.
                }
            }
            return _span;
        }
    }

    /// <summary>Os nomes da peça selecionada na tela, dentro dos game.Find (marcados em dourado).</summary>
    public IReadOnlyList<(int Start, int Length)> Marks { get; private set; } = [];

    public void SetMarks(IReadOnlyList<(int Start, int Length)> marks)
    {
        Marks = marks;
        _editor.TextArea.TextView.InvalidateLayer(Layer);
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        DrawMarks(textView, drawingContext);
        if (Span is not { } span || !textView.VisualLinesValid || textView.Document == null) return;
        var document = textView.Document;
        int firstLine = document.GetLineByOffset(Math.Min(span.Start, document.TextLength)).LineNumber;
        int lastLine = document.GetLineByOffset(Math.Min(span.End, document.TextLength)).LineNumber;
        double width = textView.ActualWidth + textView.HorizontalOffset;
        foreach (var line in textView.VisualLines)
        {
            int number = line.FirstDocumentLine.LineNumber;
            if (number < firstLine || number > lastLine) continue;
            double top = line.VisualTop - textView.VerticalOffset;
            drawingContext.DrawRectangle(Fill, null, new Rect(0, top, width, line.Height));
            drawingContext.DrawRectangle(Band, null, new Rect(0, top, 3, line.Height));
        }
    }

    private void DrawMarks(TextView textView, DrawingContext drawingContext)
    {
        if (Marks.Count == 0 || textView.Document == null || !textView.VisualLinesValid) return;
        foreach (var (start, length) in Marks)
        {
            if (start < 0 || start + length > textView.Document.TextLength) continue;
            var builder = new BackgroundGeometryBuilder { CornerRadius = 3, AlignToWholePixels = true };
            builder.AddSegment(textView, new ICSharpCode.AvalonEdit.Document.TextSegment { StartOffset = start, Length = Math.Max(1, length) });
            if (builder.CreateGeometry() is { } geometry) drawingContext.DrawGeometry(MarkFill, MarkPen, geometry);
        }
    }

    private static Pen FrozenPen(Pen pen)
    {
        pen.Freeze();
        return pen;
    }

    private static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
