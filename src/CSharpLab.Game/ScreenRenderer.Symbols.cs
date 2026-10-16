using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using WpfColor = System.Windows.Media.Color;

namespace CSharpLab.GameEngine;

// O Ícone (desenhos prontos) e o Dado.
internal static partial class ScreenRenderer
{
    // ------------------------------------------------------------------ ícone

    /// <summary>O desenho do ícone, justo no espaço que recebe, numa cor só.</summary>
    internal static Path IconShape(Icon icon, Brush fill) => new()
    {
        Data = IconSet.Shape(icon),
        Fill = fill,
        Stretch = Stretch.Uniform,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        SnapsToDevicePixels = false,
    };

    /// <summary>A cor do ícone: a escolhida, ou a das letras do tema.</summary>
    private static WpfColor IconColor(Piece piece) => piece.Color is { } color ? Theme.Rgb(color, piece.Shade) : Theme.Current.Text;

    /// <summary>
    /// Ícone: o desenho, sozinho ou numa moldura (círculo ou quadrado). A moldura, sem cor escolhida, é a cor do
    /// ícone bem escurinha, com um aro dela: fica bonita com qualquer cor.
    /// </summary>
    private static FrameworkElement IconPiece(Piece piece)
    {
        var grid = new Grid();
        var color = IconColor(piece);
        var frame = piece.Frame ?? IconFrame.None;
        double side = Math.Max(1, Math.Min(piece.Width, piece.Height));
        if (frame != IconFrame.None)
        {
            var back = piece.Back is { } chosen ? Theme.Freeze(Theme.Rgb(chosen)) : Theme.Freeze(Theme.WithAlpha(Theme.Darken(color, 0.45), 0xD8));
            var ring = Theme.Freeze(Theme.WithAlpha(color, 0x70));
            Shape shape = frame == IconFrame.Circle
                ? new Ellipse { Width = side, Height = side }
                : new Rectangle { Width = side, Height = side, RadiusX = side * 0.2, RadiusY = side * 0.2 };
            shape.Fill = back;
            shape.Stroke = ring;
            shape.StrokeThickness = Math.Max(1.5, side * 0.025);
            if (piece.Shadow == true) shape.Effect = Theme.DropShadow;
            grid.Children.Add(shape);
        }
        var drawing = IconShape(piece.Icon ?? Icon.Star, Theme.Freeze(color));
        if (frame != IconFrame.None)
        {
            // Dentro da moldura, o desenho fica no meio, com folga em volta.
            drawing.Width = drawing.Height = side * (frame == IconFrame.Circle ? 0.56 : 0.62);
        }
        else
        {
            drawing.Margin = new Thickness(Math.Min(piece.Width, piece.Height) * 0.04);
            if (piece.Shadow == true) drawing.Effect = Theme.DropShadow;
        }
        grid.Children.Add(drawing);
        if (!piece.Enabled) grid.Opacity = 0.4;
        return grid;
    }

    /// <summary>O conteúdo do botão: o ícone (se tiver) antes do texto, na cor das letras.</summary>
    private static object ButtonContent(Piece piece, Brush letters)
    {
        var label = ButtonLabel(piece, letters);
        if (piece.Icon is not { } icon) return label;
        var shape = IconShape(icon, letters);
        shape.Height = shape.Width = Math.Round(piece.FontSize * 1.15);
        if (piece.HasLetterShadow) shape.Effect = TextShadow;
        if (string.IsNullOrEmpty(piece.Text)) return shape;
        shape.Margin = new Thickness(0, 0, Math.Round(piece.FontSize * 0.45), 0);
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(shape);
        row.Children.Add(label);
        return row;
    }

    /// <summary>Quanto o ícone ocupa antes do texto do botão (para saber se o texto cabe).</summary>
    private static double ButtonIconWidth(Piece piece) =>
        piece.Icon == null ? 0 : Math.Round(piece.FontSize * 1.15) + (string.IsNullOrEmpty(piece.Text) ? 0 : Math.Round(piece.FontSize * 0.45));

    // ------------------------------------------------------------------ dado

    /// <summary>
    /// O jeito de mudar a face de um dado já desenhado (a animação do Roll troca os números enquanto ele rola).
    /// Fica no Tag do desenho do dado.
    /// </summary>
    internal sealed class DiceFace(Action<int> show)
    {
        public void Show(int value) => show(value);
    }

    /// <summary>
    /// Dado: o formato de cada um (d4 triângulo, d6 quadrado com bolinhas, d8 losango, d10 pipa, d12 pentágono,
    /// d20 hexágono com as faces, d100 decágono), da cor da peça, com o número no meio.
    /// </summary>
    private static FrameworkElement DicePiece(Piece piece)
    {
        int sides = piece.DiceSides;
        var color = piece.Color is { } chosen ? Theme.Rgb(chosen, piece.Shade) : Theme.Rgb(Color.Red);
        double size = Math.Max(10, Math.Min(piece.Width, piece.Height));
        var body = new Grid { Width = size, Height = size };

        var outline = DiceOutline(sides, size);
        var fill = new LinearGradientBrush(Theme.Lighten(color, 0.22), Theme.Darken(color, 0.72), new Point(0.2, 0), new Point(0.8, 1));
        fill.Freeze();
        var stroke = Theme.Freeze(Theme.Darken(color, 0.5));
        body.Children.Add(new Path
        {
            Data = outline,
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = Math.Max(1.5, size * 0.025),
            StrokeLineJoin = PenLineJoin.Round,
            Effect = piece.Shadow == true ? Theme.DropShadow : null,
        });
        if (DiceFacets(sides, size) is { } facets)
        {
            body.Children.Add(new Path
            {
                Data = facets,
                Stroke = Theme.Freeze(Theme.WithAlpha(Theme.Lighten(color, 0.55), 0x80)),
                StrokeThickness = Math.Max(1, size * 0.012),
                StrokeLineJoin = PenLineJoin.Round,
            });
        }

        var ink = Theme.IsLight(color) ? Theme.Freeze(WpfColor.FromRgb(0x17, 0x19, 0x1F)) : Brushes.White;
        var number = new TextBlock
        {
            FontFamily = Theme.FontOf(piece.Font ?? Font.Strong),
            FontSize = Math.Max(8, size * (sides == 100 ? 0.24 : sides == 4 ? 0.26 : 0.3)),
            Foreground = ink,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = ink == Brushes.White ? DarkHalo : LightHalo,
            // O d4 tem o meio mais embaixo (é um triângulo).
            Margin = new Thickness(0, sides == 4 ? size * 0.22 : 0, 0, 0),
        };
        var pips = new Canvas { Width = size, Height = size, IsHitTestVisible = false };
        body.Children.Add(pips);
        body.Children.Add(number);

        void Show(int value)
        {
            pips.Children.Clear();
            if (sides == 6)
            {
                // O d6 mostra bolinhas, como um dado de verdade.
                number.Visibility = Visibility.Collapsed;
                double dot = size * 0.15;
                foreach (var (x, y) in Pips(Math.Clamp(value, 1, 6)))
                {
                    var pip = new Ellipse { Width = dot, Height = dot, Fill = ink };
                    Canvas.SetLeft(pip, size * x - dot / 2);
                    Canvas.SetTop(pip, size * y - dot / 2);
                    pips.Children.Add(pip);
                }
                return;
            }
            number.Text = value.ToString(CultureInfo.InvariantCulture);
        }
        Show(piece.DiceValue);
        body.Tag = new DiceFace(Show);

        var host = new Grid();
        host.Children.Add(body);
        return host;
    }

    /// <summary>Onde ficam as bolinhas do d6 (de 0 a 1 no quadrado).</summary>
    private static IEnumerable<(double X, double Y)> Pips(int value)
    {
        const double a = 0.29, m = 0.5, b = 0.71;
        return value switch
        {
            1 => [(m, m)],
            2 => [(a, a), (b, b)],
            3 => [(a, a), (m, m), (b, b)],
            4 => [(a, a), (b, a), (a, b), (b, b)],
            5 => [(a, a), (b, a), (m, m), (a, b), (b, b)],
            _ => [(a, a), (b, a), (a, m), (b, m), (a, b), (b, b)],
        };
    }

    /// <summary>Um polígono regular com <paramref name="count"/> pontas, a primeira em <paramref name="startDegrees"/>.</summary>
    private static Point[] Polygon(int count, double size, double startDegrees, double scale = 0.47)
    {
        var points = new Point[count];
        for (int i = 0; i < count; i++)
        {
            double angle = (startDegrees + 360.0 * i / count) * Math.PI / 180;
            points[i] = new Point(size / 2 + Math.Cos(angle) * size * scale, size / 2 + Math.Sin(angle) * size * scale);
        }
        return points;
    }

    private static Geometry Shape(params Point[] points)
    {
        var figure = new PathFigure { StartPoint = points[0], IsClosed = true, IsFilled = true };
        foreach (var point in points.Skip(1)) figure.Segments.Add(new LineSegment(point, true) { IsSmoothJoin = true });
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }

    private static Geometry DiceOutline(int sides, double size)
    {
        switch (sides)
        {
            case 4:
                return Shape(Polygon(3, size, -90, 0.52).Select(p => new Point(p.X, p.Y + size * 0.08)).ToArray());
            case 6:
            {
                double inset = size * 0.08;
                var square = new RectangleGeometry(new Rect(inset, inset, size - inset * 2, size - inset * 2), size * 0.16, size * 0.16);
                square.Freeze();
                return square;
            }
            case 8:
                return Shape(Polygon(4, size, -90, 0.49));
            case 10:
                return Shape(new Point(size * 0.5, size * 0.03), new Point(size * 0.95, size * 0.45), new Point(size * 0.5, size * 0.97), new Point(size * 0.05, size * 0.45));
            case 12:
                return Shape(Polygon(5, size, -90, 0.49));
            case 100:
                return Shape(Polygon(10, size, -90, 0.48));
            default:
                return Shape(Polygon(6, size, -90, 0.49));
        }
    }

    /// <summary>As arestas de dentro (as faces): no d20, o triângulo do meio ligado às pontas; no d8 e no d10, a linha do meio.</summary>
    private static Geometry? DiceFacets(int sides, double size)
    {
        var group = new GeometryGroup();
        switch (sides)
        {
            case 20:
            {
                var outer = Polygon(6, size, -90, 0.49);
                var inner = Polygon(3, size, -90, 0.3);
                group.Children.Add(Shape(inner));
                // Cada ponta do triângulo vai até as pontas de fora mais perto dela.
                int[][] links = [[0, 1, 5], [1, 2, 3], [3, 4, 5]];
                for (int i = 0; i < 3; i++)
                    foreach (var o in links[i])
                        group.Children.Add(new LineGeometry(inner[i], outer[o]));
                break;
            }
            case 8:
            {
                var outer = Polygon(4, size, -90, 0.49);
                group.Children.Add(new LineGeometry(outer[3], outer[1]));
                break;
            }
            case 10:
                group.Children.Add(new LineGeometry(new Point(size * 0.05, size * 0.45), new Point(size * 0.5, size * 0.62)));
                group.Children.Add(new LineGeometry(new Point(size * 0.95, size * 0.45), new Point(size * 0.5, size * 0.62)));
                group.Children.Add(new LineGeometry(new Point(size * 0.5, size * 0.62), new Point(size * 0.5, size * 0.97)));
                break;
            case 12:
            {
                var inner = Polygon(5, size, -90, 0.27);
                var outer = Polygon(5, size, -90, 0.49);
                group.Children.Add(Shape(inner));
                for (int i = 0; i < 5; i++) group.Children.Add(new LineGeometry(inner[i], outer[i]));
                break;
            }
            default:
                return null;
        }
        group.Freeze();
        return group;
    }
}
