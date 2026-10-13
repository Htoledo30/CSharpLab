using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CSharpLab.GameEngine;

// As peças de escolher e de sorte: Escolha (uma opção entre várias), Contador (− 3 +), Roleta (gira e para numa
// fatia) e Grade (casas em linhas e colunas, para tabuleiros e jogos de memória). Desenhadas igual no jogo e no Estúdio.
internal static partial class ScreenRenderer
{
    /// <summary>Espaço entre as opções da Escolha e entre as casas da Grade.</summary>
    private const double Spacing = 6;

    /// <summary>As cores das fatias da Roleta sem cor escolhida: um arco-íris com as cores do tema.</summary>
    private static readonly Color[] WheelColors = [Color.Red, Color.Orange, Color.Gold, Color.Green, Color.Cyan, Color.Blue, Color.Purple, Color.Pink];

    /// <summary>Uma linha de texto com as opções de letra da peça (fonte, tamanho, negrito, itálico, sombra).</summary>
    private static TextBlock OneLine(Piece piece, string text, Brush foreground)
    {
        var label = Label(piece, text, foreground);
        label.TextWrapping = TextWrapping.NoWrap;
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        label.FontWeight = piece.IsBold ? FontWeights.Bold : FontWeights.Normal;
        label.FontStyle = piece.Italic == true ? FontStyles.Italic : FontStyles.Normal;
        label.TextAlignment = TextAlignmentOf(piece.LetterAlign);
        if (piece.HasLetterShadow) label.Effect = TextShadow;
        return label;
    }

    /// <summary>A peça ainda vazia, no Estúdio: um quadro tracejado com o que falta (no jogo, nada aparece).</summary>
    private static FrameworkElement EmptyHint(string text, RenderContext context)
    {
        if (context.Live) return new Border();
        var frame = new Grid();
        frame.Children.Add(new Rectangle { Stroke = Theme.Muted, StrokeThickness = 1.5, StrokeDashArray = [4, 3], RadiusX = 8, RadiusY = 8 });
        frame.Children.Add(new TextBlock
        {
            Text = text,
            FontFamily = Theme.BodyFamily,
            FontSize = 13,
            Foreground = Theme.Muted,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8),
        });
        return frame;
    }

    // ------------------------------------------------------------------ escolha

    /// <summary>
    /// Escolha: um botão para cada opção, todas numa linha (ou em colunas). A escolhida fica cheia, com a cor da peça;
    /// as outras, só com o contorno. Clicar numa opção escolhe ela.
    /// </summary>
    private static FrameworkElement ChoicePiece(Piece piece, RenderContext context)
    {
        var options = piece.OptionList;
        if (options.Count == 0) return EmptyHint("Escolha sem opções\n(escreva as opções no painel)", context);
        int columns = piece.ChoiceColumns;
        int rows = (options.Count + columns - 1) / columns;
        double optionHeight = (piece.Height + Spacing) / rows - Spacing;
        var grid = new UniformGrid { Columns = columns, Rows = rows, Margin = new Thickness(-Spacing / 2) };
        var selected = piece.SelectedOption;
        foreach (var option in options)
        {
            bool chosen = string.Equals(option, selected, StringComparison.OrdinalIgnoreCase);
            var style = chosen ? ButtonStyle.Filled : ButtonStyle.Outline;
            var look = Theme.LookOf(piece.Color, piece.Shade, style, piece.TextColor);
            var button = Theme.MakeButton(OneLine(piece, option, look.Text), piece.Color, piece.Shade, style, piece.TextColor,
                Theme.ButtonCorners(piece.Corner, optionHeight));
            button.Margin = new Thickness(Spacing / 2);
            button.HorizontalContentAlignment = HorizontalOf(piece.LetterAlign);
            button.IsEnabled = piece.Enabled;
            if (context.Live && context.Choose != null)
                button.Click += (_, _) => context.Choose(piece, option);
            grid.Children.Add(button);
        }
        return grid;
    }

    // ------------------------------------------------------------------ contador

    /// <summary>
    /// Contador: [rótulo]  [−]  3  [+]. O − some (fica apagado) no mínimo e o + no máximo.
    /// </summary>
    private static FrameworkElement CounterPiece(Piece piece, RenderContext context)
    {
        double size = Math.Max(16, piece.Height);
        var corners = Theme.ButtonCorners(piece.Corner, size);
        int value = piece.CounterValue;

        Button Make(string glyph, int direction, bool canGo)
        {
            var look = Theme.LookOf(piece.Color, piece.Shade, ButtonStyle.Filled, piece.TextColor);
            var sign = new TextBlock
            {
                Text = glyph,
                FontFamily = Theme.DefaultFont,
                FontSize = Math.Clamp(size * 0.42, 12, 40),
                FontWeight = FontWeights.Bold,
                Foreground = look.Text,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(-14, -6, -14, -6),   // o botão tem espaço dos lados; o sinal usa o meio todo
            };
            var button = Theme.MakeButton(sign, piece.Color, piece.Shade, ButtonStyle.Filled, piece.TextColor, corners);
            button.Width = size;
            button.IsEnabled = piece.Enabled && canGo;
            if (context.Live && context.Step != null)
                button.Click += (_, _) => context.Step(piece, direction);
            return button;
        }

        var minus = Make("−", -1, value > piece.CounterMin);
        var plus = Make("+", +1, value < piece.CounterMax);
        var number = Label(piece, value.ToString(CultureInfo.InvariantCulture));
        number.FontSize *= 1.2;
        Letters(number, piece, Theme.Text, FontWeights.Bold);
        number.TextAlignment = TextAlignment.Center;
        number.VerticalAlignment = VerticalAlignment.Center;
        number.TextWrapping = TextWrapping.NoWrap;
        var middle = new Border
        {
            Background = Theme.Panel,
            BorderBrush = Theme.PanelBorder,
            BorderThickness = new Thickness(Theme.Current.PanelBorders ? 1 : 0),
            CornerRadius = corners,
            Margin = new Thickness(Spacing, 0, Spacing, 0),
            MinWidth = size,
            Child = number,
        };

        var row = new DockPanel { LastChildFill = true };
        if (!string.IsNullOrWhiteSpace(piece.Text))
        {
            // O rótulo ("Quantidade") à esquerda, na letra da peça.
            var label = Letters(Label(piece, piece.Text), piece, Theme.Text);
            label.TextWrapping = TextWrapping.NoWrap;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            label.VerticalAlignment = VerticalAlignment.Center;
            label.Margin = new Thickness(0, 0, 12, 0);
            label.MaxWidth = Math.Max(20, piece.Width - size * 3);
            DockPanel.SetDock(label, Dock.Left);
            row.Children.Add(label);
        }
        DockPanel.SetDock(minus, Dock.Left);
        DockPanel.SetDock(plus, Dock.Right);
        row.Children.Add(minus);
        row.Children.Add(plus);
        row.Children.Add(middle);
        return row;
    }

    // ------------------------------------------------------------------ roleta

    /// <summary>
    /// Quanto a roleta está girada para a fatia desta opção ficar embaixo da seta (lá em cima). Sem opção, a primeira.
    /// As fatias vão no sentido do relógio, começando em cima.
    /// </summary>
    public static double WheelAngle(Piece piece, string? option)
    {
        var options = piece.OptionList;
        if (options.Count == 0) return 0;
        int index = option == null ? 0 : Math.Max(0, options.ToList().FindIndex(o => string.Equals(o, option, StringComparison.OrdinalIgnoreCase)));
        double slice = 360.0 / options.Count;
        return -(index * slice + slice / 2);
    }

    /// <summary>
    /// Roleta: um disco com uma fatia para cada opção e uma seta em cima. O disco é girado para a opção escolhida
    /// (Selected) ficar embaixo da seta; o giro animado é feito pelo Spin, no jogo. O Tag guarda o giro do disco.
    /// </summary>
    private static FrameworkElement WheelPiece(Piece piece)
    {
        var options = piece.OptionList;
        double d = Math.Max(24, Math.Min(piece.Width, piece.Height));
        double r = d / 2;
        var root = new Grid();
        var disc = new Canvas { Width = d, Height = d, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new Point(0.5, 0.5) };
        var rotate = new RotateTransform(WheelAngle(piece, piece.SelectedOption));
        disc.RenderTransform = rotate;
        root.Tag = rotate;

        if (options.Count == 0)
        {
            disc.Children.Add(new Ellipse { Width = d, Height = d, Fill = Theme.Panel, Stroke = Theme.Muted, StrokeThickness = 1.5, StrokeDashArray = [4, 3] });
            root.Children.Add(disc);
            root.Children.Add(new TextBlock { Text = "Roleta sem opções\n(escreva no painel)", Foreground = Theme.Muted, FontFamily = Theme.BodyFamily, FontSize = 13, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            return root;
        }

        double slice = 360.0 / options.Count;
        var edge = Theme.Freeze(System.Windows.Media.Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF));
        for (int i = 0; i < options.Count; i++)
        {
            var fill = Theme.Freeze(SliceColor(piece, i, options.Count));
            if (options.Count == 1)
                disc.Children.Add(new Ellipse { Width = d, Height = d, Fill = fill });
            else
                disc.Children.Add(new Path { Data = Slice(r, i * slice, (i + 1) * slice), Fill = fill, Stroke = edge, StrokeThickness = 1.2 });

            // O texto da fatia fica no meio dela, na direção do centro para a borda.
            double mid = i * slice + slice / 2;
            var label = OneLine(piece, options[i], piece.TextColor is { } textColor ? Theme.Brush(textColor) : Brushes.White);
            label.Effect = DarkShadow;
            label.Width = r * 0.66;
            label.TextAlignment = TextAlignment.Center;
            label.FontSize = Math.Min(label.FontSize, Math.Max(8, r * 0.16));
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double radians = mid * Math.PI / 180, at = r * 0.6;
            double cx = r + at * Math.Sin(radians), cy = r - at * Math.Cos(radians);
            Canvas.SetLeft(label, cx - label.Width / 2);
            Canvas.SetTop(label, cy - label.DesiredSize.Height / 2);
            label.RenderTransformOrigin = new Point(0.5, 0.5);
            // Na metade da esquerda, o texto vira 180° para não ficar de cabeça para baixo.
            label.RenderTransform = new RotateTransform(mid > 180 ? mid + 90 : mid - 90);
            disc.Children.Add(label);
        }
        root.Children.Add(disc);

        // A borda, o miolo e a seta não giram.
        var still = new Canvas { Width = d, Height = d, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        still.Children.Add(new Ellipse { Width = d, Height = d, Stroke = Theme.Freeze(Theme.Darken(Theme.Rgb(Color.Gold), 0.75)), StrokeThickness = Math.Max(2, d * 0.02) });
        double hub = Math.Max(10, d * 0.12);
        var center = new Ellipse { Width = hub, Height = hub, Fill = Theme.Freeze(Theme.Rgb(Color.Gold)), Stroke = Theme.Freeze(Theme.Darken(Theme.Rgb(Color.Gold), 0.6)), StrokeThickness = 2 };
        Canvas.SetLeft(center, r - hub / 2);
        Canvas.SetTop(center, r - hub / 2);
        still.Children.Add(center);
        double arrow = Math.Max(10, d * 0.07);
        still.Children.Add(new Polygon
        {
            Points = [new(r - arrow, -2), new(r + arrow, -2), new(r, arrow * 1.9)],
            Fill = Theme.Freeze(Theme.Rgb(Color.Gold)),
            Stroke = Theme.Freeze(Theme.Darken(Theme.Rgb(Color.Gold), 0.5)),
            StrokeThickness = 2,
            Effect = Theme.DropShadow,
        });
        root.Children.Add(still);
        return root;
    }

    /// <summary>A cor da fatia: o arco-íris do tema, ou a cor da peça alternando claro e escuro.</summary>
    private static System.Windows.Media.Color SliceColor(Piece piece, int index, int count)
    {
        if (piece.Color is { } color)
        {
            var main = Theme.Rgb(color, piece.Shade);
            return index % 2 == 0 ? main : Theme.Darken(main, 0.68);
        }
        // Com um número ímpar de fatias, a última não repete a cor da primeira (que fica do lado dela).
        int at = index % WheelColors.Length;
        if (index == count - 1 && count > 1 && at == 0) at = 3;
        return Theme.Rgb(WheelColors[at], piece.Shade);
    }

    /// <summary>Uma fatia da roleta: do centro até a borda, entre dois ângulos (0 = em cima, no sentido do relógio).</summary>
    private static Geometry Slice(double r, double from, double to)
    {
        Point At(double angle) => new(r + r * Math.Sin(angle * Math.PI / 180), r - r * Math.Cos(angle * Math.PI / 180));
        var figure = new PathFigure { StartPoint = new Point(r, r), IsClosed = true };
        figure.Segments.Add(new LineSegment(At(from), true));
        figure.Segments.Add(new ArcSegment(At(to), new Size(r, r), 0, to - from > 180, SweepDirection.Clockwise, true));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }

    // ------------------------------------------------------------------ grade

    /// <summary>
    /// Grade: casas em linhas e colunas. Cada casa mostra o texto e a cor que o código pôs nela (Cell(x, y)); a casa
    /// (0, 0) é a de cima à esquerda. No Estúdio, as casas vazias mostram o x e o y delas, para ajudar no código.
    /// </summary>
    private static FrameworkElement GridPiece(Piece piece, RenderContext context)
    {
        int columns = piece.GridColumns, rows = piece.GridRows;
        var grid = new UniformGrid { Columns = columns, Rows = rows, Margin = new Thickness(-Spacing / 2) };
        bool clickable = context.Live && context.CellClick != null && piece.Enabled && context.IsClickable?.Invoke(piece) == true;
        double cellHeight = (piece.Height + Spacing) / rows - Spacing;
        double radius = piece.Corner switch
        {
            Corner.Square => 0,
            Corner.Circle => Math.Max(0, cellHeight / 2),
            _ => Math.Min(Theme.PanelRadius, 8),
        };
        var normal = piece.Color is { } color ? Theme.Rgb(color, piece.Shade) : Theme.PanelColor;
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                var data = piece.CellAt(x, y);
                var back = data.Color is { } paint ? Theme.Rgb(paint) : normal;
                var cell = new Border
                {
                    Margin = new Thickness(Spacing / 2),
                    CornerRadius = new CornerRadius(radius),
                    Background = Theme.Freeze(back),
                    BorderBrush = Theme.PanelBorder,
                    BorderThickness = new Thickness(1),
                };
                var foreground = piece.TextColor is { } textColor ? Theme.Brush(textColor)
                    : Theme.IsLight(back) ? Theme.Freeze(System.Windows.Media.Color.FromRgb(0x17, 0x19, 0x1F)) : Brushes.White;
                if (data.Text.Length > 0)
                {
                    var text = OneLine(piece, data.Text, foreground);
                    text.FontSize = Math.Min(text.FontSize, Math.Max(8, cellHeight * 0.7));
                    text.VerticalAlignment = VerticalAlignment.Center;
                    text.HorizontalAlignment = HorizontalOf(piece.LetterAlign);
                    text.Margin = new Thickness(4, 0, 4, 0);
                    cell.Child = text;
                }
                else if (!context.Live)
                {
                    cell.Child = new TextBlock
                    {
                        Text = $"{x},{y}",
                        FontFamily = Theme.BodyFamily,
                        FontSize = Math.Clamp(cellHeight * 0.22, 8, 13),
                        Foreground = Theme.Muted,
                        Opacity = 0.75,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                }
                if (clickable)
                {
                    int cx = x, cy = y;
                    var hover = Theme.Freeze(Theme.Lighten(back, 0.18));
                    var rest = cell.Background;
                    cell.Cursor = Cursors.Hand;
                    cell.MouseEnter += (_, _) => cell.Background = hover;
                    cell.MouseLeave += (_, _) => cell.Background = rest;
                    cell.MouseLeftButtonUp += (_, e) =>
                    {
                        e.Handled = true;
                        context.CellClick!(piece, cx, cy);
                    };
                }
                grid.Children.Add(cell);
            }
        }
        if (!piece.Enabled) grid.Opacity = 0.5;
        return grid;
    }
}
