using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CSharpLab.GameEngine;
using CSharpLab.ViewModels;
using WpfColor = System.Windows.Media.Color;

namespace CSharpLab.Views;

/// <summary>
/// O mapa do jogo, no Estúdio: cada cena é um cartão com a miniatura da tela, e as setas mostram para onde
/// cada cena leva (os game.GoTo do código). Começa na cena do game.Start, à esquerda; clicar abre a cena.
/// </summary>
public sealed class SceneMapView : Grid
{
    private const double CardWidth = 176;
    private const double ThumbHeight = 99;   // 16:9, como o palco
    private const double CardHeight = ThumbHeight + 44;
    private const double ColumnGap = 70;
    private const double RowGap = 28;
    private const double Margin0 = 36;

    private static readonly Brush ArrowBrush = Frozen(WpfColor.FromArgb(0x90, 0x8C, 0x93, 0xA0));
    private static readonly Brush ArrowHot = Frozen(WpfColor.FromRgb(0x7C, 0x8C, 0xF8));

    private readonly Canvas _canvas = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBlock _hint = new() { FontSize = 12, Margin = new Thickness(16, 10, 16, 0), TextWrapping = TextWrapping.Wrap };

    /// <summary>Um cartão foi clicado: abrir a cena.</summary>
    public event Action<string>? SceneChosen;

    public SceneMapView()
    {
        SetResourceReference(BackgroundProperty, "BgBase");
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _hint.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        Children.Add(_hint);
        var scroll = new ScrollViewer
        {
            Content = _canvas,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Focusable = false,
        };
        SetRow(scroll, 1);
        Children.Add(scroll);
    }

    /// <summary>Os nomes na ordem em que ficaram no mapa, com a coluna de cada um (para os testes).</summary>
    internal IReadOnlyList<(string Scene, int Column)> Placed { get; private set; } = [];

    internal IReadOnlyList<(string From, string To)> Arrows { get; private set; } = [];

    /// <summary>Desenha o mapa de novo com o que o código e as telas dizem agora.</summary>
    public void Show(MainViewModel vm, string projectDirectory, string? current)
    {
        _canvas.Children.Clear();
        var scenes = vm.GameScenes();
        var names = scenes.Select(s => s.Name).ToList();
        var links = vm.GameSceneLinks()
            .Where(l => names.Contains(l.From, StringComparer.OrdinalIgnoreCase) && names.Contains(l.To, StringComparer.OrdinalIgnoreCase))
            .Select(l => (From: Canonical(names, l.From), To: Canonical(names, l.To)))
            .Distinct()
            .ToList();
        Arrows = links;
        _hint.Text = scenes.Count == 0
            ? "O jogo ainda não tem cenas."
            : "Cada seta é um game.GoTo(\"…\") no código. Clique numa cena para abrir. As cenas sem seta chegando ficam na última coluna.";

        // Colunas: a distância (em idas) desde a cena do começo. Quem não é alcançado fica numa coluna no fim.
        var start = scenes.FirstOrDefault(s => s.IsStart)?.Name ?? names.FirstOrDefault();
        var column = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (start != null)
        {
            column[start] = 0;
            var queue = new Queue<string>([start]);
            while (queue.Count > 0)
            {
                var from = queue.Dequeue();
                foreach (var (_, to) in links.Where(l => string.Equals(l.From, from, StringComparison.OrdinalIgnoreCase)))
                {
                    if (column.ContainsKey(to)) continue;
                    column[to] = column[from] + 1;
                    queue.Enqueue(to);
                }
            }
        }
        int lonely = column.Count == 0 ? 0 : column.Values.Max() + 1;
        foreach (var name in names.Where(n => !column.ContainsKey(n))) column[name] = lonely;

        var positions = new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase);
        var rows = new Dictionary<int, int>();
        var placed = new List<(string, int)>();
        foreach (var name in names)
        {
            int c = column[name];
            int r = rows.GetValueOrDefault(c);
            rows[c] = r + 1;
            positions[name] = new Point(Margin0 + c * (CardWidth + ColumnGap), Margin0 + r * (CardHeight + RowGap));
            placed.Add((name, c));
        }
        Placed = placed;

        // Setas primeiro (ficam embaixo dos cartões).
        foreach (var (from, to) in links)
        {
            bool hot = string.Equals(from, current, StringComparison.OrdinalIgnoreCase) || string.Equals(to, current, StringComparison.OrdinalIgnoreCase);
            Arrow(positions[from], positions[to], from == to, hot);
        }

        Theme.ImageRoots = [projectDirectory];
        Theme.UseProject([projectDirectory]);
        foreach (var scene in scenes)
        {
            var card = Card(vm, projectDirectory, scene, string.Equals(scene.Name, current, StringComparison.OrdinalIgnoreCase));
            Canvas.SetLeft(card, positions[scene.Name].X);
            Canvas.SetTop(card, positions[scene.Name].Y);
            _canvas.Children.Add(card);
        }
        _canvas.Width = positions.Count == 0 ? 0 : positions.Values.Max(p => p.X) + CardWidth + Margin0;
        _canvas.Height = positions.Count == 0 ? 0 : positions.Values.Max(p => p.Y) + CardHeight + Margin0 + 40;
    }

    private static string Canonical(List<string> names, string name) =>
        names.First(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));

    // ------------------------------------------------------------------ cartões

    private Border Card(MainViewModel vm, string dir, GameSceneInfo scene, bool current)
    {
        var stack = new StackPanel();
        stack.Children.Add(Thumbnail(vm, dir, scene));
        var name = new TextBlock { Text = scene.Name, FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 7, 10, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        stack.Children.Add(name);
        var note = scene.IsStart ? "▶ começa aqui" : !scene.InCode ? "falta no código" : !scene.HasScreen ? "só código" : " ";
        var detail = new TextBlock { Text = note, FontSize = 11, Margin = new Thickness(10, 1, 10, 0) };
        detail.SetResourceReference(TextBlock.ForegroundProperty, scene.IsStart ? "Accent" : "TextMuted");
        stack.Children.Add(detail);

        var card = new Border
        {
            Child = stack,
            Width = CardWidth,
            Height = CardHeight,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(current ? 2 : 1),
            Cursor = Cursors.Hand,
            ToolTip = $"Abrir a cena \"{scene.Name}\"",
            ClipToBounds = true,
        };
        card.SetResourceReference(Border.BackgroundProperty, "BgElevated");
        card.SetResourceReference(Border.BorderBrushProperty, current ? "Accent" : "BorderStrong");
        card.MouseEnter += (_, _) => { if (!current) card.SetResourceReference(Border.BorderBrushProperty, "TextMuted"); };
        card.MouseLeave += (_, _) => { if (!current) card.SetResourceReference(Border.BorderBrushProperty, "BorderStrong"); };
        card.MouseLeftButtonUp += (_, e) =>
        {
            SceneChosen?.Invoke(scene.Name);
            e.Handled = true;
        };
        System.Windows.Automation.AutomationProperties.SetName(card, "Cena " + scene.Name);
        return card;
    }

    /// <summary>A tela da cena em miniatura (as peças como o jogo desenha), ou um aviso para cena só de código.</summary>
    private static FrameworkElement Thumbnail(MainViewModel vm, string dir, GameSceneInfo scene)
    {
        var host = new Border { Height = ThumbHeight, CornerRadius = new CornerRadius(7, 7, 0, 0), ClipToBounds = true };
        var layout = vm.ScreenText(dir, scene.Name) is { } text ? ScreenFile.Parse(text).Layout : null;
        if (layout == null)
        {
            host.SetResourceReference(Border.BackgroundProperty, "BgBase");
            var label = new TextBlock
            {
                Text = scene.HasScreen ? "A tela tem um erro" : "{ }  feita só com código",
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
            host.Child = label;
            return host;
        }
        host.Child = Screens.ScreenThumbnail.Create(layout);
        return host;
    }

    // ------------------------------------------------------------------ setas

    private void Arrow(Point from, Point to, bool self, bool hot)
    {
        var brush = hot ? ArrowHot : ArrowBrush;
        double thickness = hot ? 2.2 : 1.6;
        Point start, end, c1, c2;
        if (self)
        {
            // Volta para a mesma cena: um laço em cima do cartão.
            start = new Point(from.X + CardWidth * 0.62, from.Y);
            end = new Point(from.X + CardWidth * 0.38, from.Y);
            c1 = new Point(start.X + 20, start.Y - 34);
            c2 = new Point(end.X - 20, end.Y - 34);
        }
        else if (to.X > from.X)
        {
            // Para a frente: do lado direito de um ao lado esquerdo do outro.
            start = new Point(from.X + CardWidth, from.Y + CardHeight / 2);
            end = new Point(to.X - 4, to.Y + CardHeight / 2);
            double bend = (end.X - start.X) / 2;
            c1 = new Point(start.X + bend, start.Y);
            c2 = new Point(end.X - bend, end.Y);
        }
        else
        {
            // Para trás (ou na mesma coluna): por baixo dos cartões, para não cruzar por cima deles.
            start = new Point(from.X + CardWidth / 2 + 12, from.Y + CardHeight);
            end = new Point(to.X + CardWidth / 2 - 12, to.Y + CardHeight + 4);
            double depth = 46 + Math.Abs(from.X - to.X) * 0.08;
            c1 = new Point(start.X, Math.Max(start.Y, end.Y) + depth);
            c2 = new Point(end.X, Math.Max(start.Y, end.Y) + depth);
        }
        var figure = new PathFigure { StartPoint = start };
        figure.Segments.Add(new BezierSegment(c1, c2, end, true));
        var geometry = new PathGeometry([figure]);
        _canvas.Children.Add(new Path { Data = geometry, Stroke = brush, StrokeThickness = thickness, IsHitTestVisible = false });

        // A ponta da seta, na direção em que a curva chega.
        var direction = end - c2;
        if (direction.Length < 0.01) direction = end - start;
        direction.Normalize();
        var normal = new Vector(-direction.Y, direction.X);
        const double size = 9;
        var tip = new Polygon
        {
            Points = [end, end - direction * size + normal * size * 0.55, end - direction * size - normal * size * 0.55],
            Fill = brush,
            IsHitTestVisible = false,
        };
        _canvas.Children.Add(tip);
    }

    private static Brush Frozen(WpfColor color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
