using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CSharpLab.GameEngine;
using WpfColor = System.Windows.Media.Color;

namespace CSharpLab.Screens;

/// <summary>
/// O palco da aba Tela: mostra a tela do jogo (com o mesmo desenho do jogo) e deixa selecionar,
/// arrastar e redimensionar as peças. Durante o arraste só a peça na tela se mexe; o arquivo é
/// gravado uma vez, ao soltar o mouse. Por isso o movimento fica leve.
/// </summary>
public sealed class ScreenStage : Grid
{
    public const string PieceDragFormat = "CSharpLab.PieceType";

    private const double Padding = 28;
    private const double HandleSize = 8;     // em pixels da tela, qualquer que seja o zoom
    private const double DragThreshold = 3;  // pixels antes de começar a arrastar (um clique não move)

    private static readonly Brush SelectionBrush = Freeze(WpfColor.FromRgb(0x7C, 0x8C, 0xF8));
    private static readonly Brush HoverBrush = Freeze(WpfColor.FromArgb(0x99, 0x7C, 0x8C, 0xF8));
    private static readonly Brush GuideBrush = Freeze(WpfColor.FromRgb(0xF2, 0x4E, 0x8B));
    private static readonly Brush HiddenBrush = Freeze(WpfColor.FromArgb(0xAA, 0x8C, 0x93, 0xA0));
    private static readonly Brush LabelBack = Freeze(WpfColor.FromRgb(0x7C, 0x8C, 0xF8));
    private static readonly Brush LabelText = Freeze(WpfColor.FromRgb(0x10, 0x12, 0x25));

    private readonly ScreenDesignerModel _model;
    private readonly Canvas _host = new() { ClipToBounds = true };
    private readonly Border _frame = new() { BorderThickness = new Thickness(1), SnapsToDevicePixels = true };
    private readonly Grid _stage = new() { Width = ScreenLayout.Width, Height = ScreenLayout.Height };
    private readonly Canvas _background = new();
    private readonly Canvas _pieces = new();
    private readonly Canvas _overlay = new() { IsHitTestVisible = false };
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly TextBlock _emptyHint;
    private readonly Dictionary<string, FrameworkElement> _elements = new(StringComparer.OrdinalIgnoreCase);

    // Peças da seleção (recriadas a cada mudança de zoom ou seleção)
    private readonly Rectangle _hover = new() { StrokeThickness = 1, Visibility = Visibility.Collapsed };
    private readonly Rectangle _selection = new() { Visibility = Visibility.Collapsed };
    private readonly Rectangle[] _handles = new Rectangle[8];
    private readonly Border _nameTag;
    private readonly TextBlock _nameText = new() { FontSize = 11, Foreground = LabelText, FontWeight = FontWeights.SemiBold };
    private readonly Border _sizeTag;
    private readonly TextBlock _sizeText = new() { FontSize = 11, Foreground = Brushes.White };
    private readonly Rectangle _dropGhost = new() { Visibility = Visibility.Collapsed, StrokeDashArray = [4, 3], Fill = Freeze(WpfColor.FromArgb(0x22, 0x7C, 0x8C, 0xF8)) };
    private readonly List<Line> _guides = [];

    private double _zoom = 1;

    /// <summary>Marca o quadro tracejado das peças que começam escondidas.</summary>
    private static readonly object HiddenHolder = new();

    // Arraste em andamento
    private enum Drag { None, Pending, Moving, Resizing }
    private Drag _drag;
    private string? _dragName;
    private Point _dragStart;      // ponto do palco onde o mouse desceu
    private Rect _dragOrigin;      // posição da peça quando o arraste começou
    private Rect _dragCurrent;
    private Edges _dragEdges;
    private Snapper? _snapper;
    private bool _altDuringDrag;   // o Alt solto depois do arraste não deve abrir o menu da janela

    public ScreenStage(ScreenDesignerModel model)
    {
        _model = model;
        Focusable = true;
        FocusVisualStyle = null;
        AllowDrop = true;
        ClipToBounds = true;
        SetResourceReference(BackgroundProperty, "BgBase");
        _frame.SetResourceReference(Border.BorderBrushProperty, "BorderStrong");

        _stage.RenderTransform = _scale;
        _stage.Background = Theme.Background;
        // Dentro do palco valem os estilos do jogo, não os do editor: assim a tela fica igual à do jogo.
        foreach (var type in new[] { typeof(TextBlock), typeof(TextBox), typeof(ScrollViewer), typeof(ScrollBar), typeof(Button) })
            _stage.Resources[type] = new Style(type);
        _stage.Children.Add(_background);
        _stage.Children.Add(CreateGridLayer());
        _stage.Children.Add(_pieces);
        _stage.Children.Add(_overlay);

        _host.Children.Add(_frame);
        _host.Children.Add(_stage);
        Children.Add(_host);

        _emptyHint = new TextBlock
        {
            Text = "Arraste uma peça da esquerda para cá (ou clique nela).",
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Foreground = Theme.Muted,
        };
        Children.Add(_emptyHint);

        _hover.Stroke = HoverBrush;
        _selection.Stroke = SelectionBrush;
        _overlay.Children.Add(_hover);
        _overlay.Children.Add(_selection);
        for (int i = 0; i < _handles.Length; i++)
        {
            _handles[i] = new Rectangle { Fill = Brushes.White, Stroke = SelectionBrush, Visibility = Visibility.Collapsed };
            _overlay.Children.Add(_handles[i]);
        }
        _dropGhost.Stroke = SelectionBrush;
        _overlay.Children.Add(_dropGhost);
        _nameTag = MakeTag(_nameText, LabelBack);
        _sizeTag = MakeTag(_sizeText, Freeze(WpfColor.FromArgb(0xE6, 0x25, 0x26, 0x2A)));
        _overlay.Children.Add(_nameTag);
        _overlay.Children.Add(_sizeTag);

        SizeChanged += (_, _) => Fit();
        model.Changed += Rebuild;
        model.SelectionChanged += UpdateOverlay;
        Rebuild();
    }

    /// <summary>Dois cliques numa peça: o painel de propriedades põe o cursor no texto dela.</summary>
    public event Action? EditTextRequested;

    public double Zoom => _zoom;

    public void Detach()
    {
        _model.Changed -= Rebuild;
        _model.SelectionChanged -= UpdateOverlay;
    }

    private static Brush Freeze(WpfColor color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Border MakeTag(TextBlock text, Brush background) => new()
    {
        Background = background,
        CornerRadius = new CornerRadius(3),
        Padding = new Thickness(5, 1, 5, 2),
        Child = text,
        Visibility = Visibility.Collapsed,
    };

    /// <summary>Pontinhos discretos a cada 40, para ajudar a alinhar de olho.</summary>
    private static FrameworkElement CreateGridLayer()
    {
        var dot = new GeometryDrawing(Freeze(WpfColor.FromArgb(0x1C, 0xFF, 0xFF, 0xFF)), null, new RectangleGeometry(new Rect(0, 0, 1.5, 1.5)));
        var brush = new DrawingBrush(dot)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 40, 40),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, 40, 40),
            ViewboxUnits = BrushMappingMode.Absolute,
        };
        brush.Freeze();
        return new Rectangle { Fill = brush, IsHitTestVisible = false };
    }

    // ------------------------------------------------------------------ zoom e desenho

    /// <summary>O palco ocupa o espaço disponível, sem distorcer (como a janela do jogo).</summary>
    private void Fit()
    {
        double width = ActualWidth, height = ActualHeight;
        if (width <= 0 || height <= 0) return;
        _zoom = Math.Clamp(Math.Min((width - Padding * 2) / ScreenLayout.Width, (height - Padding * 2) / ScreenLayout.Height), 0.2, 2);
        _scale.ScaleX = _scale.ScaleY = _zoom;
        double left = Math.Round((width - ScreenLayout.Width * _zoom) / 2);
        double top = Math.Round((height - ScreenLayout.Height * _zoom) / 2);
        Canvas.SetLeft(_stage, left);
        Canvas.SetTop(_stage, top);
        Canvas.SetLeft(_frame, left - 1);
        Canvas.SetTop(_frame, top - 1);
        _frame.Width = ScreenLayout.Width * _zoom + 2;
        _frame.Height = ScreenLayout.Height * _zoom + 2;
        UpdateOverlay();
    }

    /// <summary>Desenha todas as peças de novo (depois de qualquer mudança no arquivo).</summary>
    private void Rebuild()
    {
        _pieces.Children.Clear();
        _background.Children.Clear();
        _elements.Clear();
        var layout = _model.Layout;
        if (layout == null)
        {
            _emptyHint.Visibility = Visibility.Collapsed;
            UpdateOverlay();
            return;
        }
        if (_model.ProjectDirectory != null) Theme.ImageRoots = [_model.ProjectDirectory];

        if (ScreenRenderer.Background(layout) is { } bg) _background.Children.Add(bg);
        var context = new RenderContext { Live = false };
        foreach (var piece in layout.Pieces)
        {
            FrameworkElement element;
            try
            {
                element = ScreenRenderer.Create(piece, context);
            }
            catch (Exception ex)
            {
                Core.Settings.AppPaths.Log(ex, "Desenhando peça");
                element = new Border { Background = Theme.Panel };
            }
            if (!piece.Visible)
            {
                // Escondida no começo do jogo: aparece apagada, com contorno tracejado.
                element.Opacity = 0.35;
                var holder = new Grid { Width = element.Width, Height = element.Height, IsHitTestVisible = false, Tag = HiddenHolder };
                holder.Children.Add(element);
                holder.Children.Add(new Rectangle { Stroke = HiddenBrush, StrokeDashArray = [3, 3], StrokeThickness = 1 });
                element = holder;
            }
            Canvas.SetLeft(element, piece.X);
            Canvas.SetTop(element, piece.Y);
            _pieces.Children.Add(element);
            _elements[piece.Name] = element;
        }
        _emptyHint.Visibility = layout.Pieces.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateOverlay();
    }

    private Rect? SelectedBounds()
    {
        if (_model.Selected is not { } piece) return null;
        if (_drag is Drag.Moving or Drag.Resizing && string.Equals(_dragName, piece.Name, StringComparison.OrdinalIgnoreCase))
            return _dragCurrent;
        return ScreenDesignerModel.BoundsOf(piece);
    }

    /// <summary>Contorno, alças, nome e medidas da peça selecionada, sempre do mesmo tamanho na tela.</summary>
    private void UpdateOverlay()
    {
        double px = 1 / _zoom;
        var bounds = SelectedBounds();
        if (bounds is not { } r)
        {
            _selection.Visibility = Visibility.Collapsed;
            foreach (var h in _handles) h.Visibility = Visibility.Collapsed;
            _nameTag.Visibility = Visibility.Collapsed;
            _sizeTag.Visibility = Visibility.Collapsed;
            return;
        }

        Place(_selection, r);
        _selection.StrokeThickness = 1.5 * px;
        _selection.Visibility = Visibility.Visible;

        bool small = r.Width * _zoom < 36 || r.Height * _zoom < 36;
        var points = HandlePoints(r);
        double size = HandleSize * px;
        for (int i = 0; i < _handles.Length; i++)
        {
            var h = _handles[i];
            bool middle = i % 2 == 1;
            h.Visibility = small && middle ? Visibility.Collapsed : Visibility.Visible;
            h.Width = h.Height = size;
            h.StrokeThickness = px;
            Canvas.SetLeft(h, points[i].X - size / 2);
            Canvas.SetTop(h, points[i].Y - size / 2);
        }

        // Nome da peça em cima (é o nome do game.Find) e medidas embaixo durante o arraste.
        _nameText.Text = _model.Selected!.Name;
        _nameTag.LayoutTransform = new ScaleTransform(px, px);
        _nameTag.Visibility = Visibility.Visible;
        _nameTag.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(_nameTag, r.Left);
        Canvas.SetTop(_nameTag, r.Top - _nameTag.DesiredSize.Height - 4 * px);

        if (_drag is Drag.Moving or Drag.Resizing)
        {
            _sizeText.Text = _drag == Drag.Moving ? $"x {r.X:0}   y {r.Y:0}" : $"{r.Width:0} × {r.Height:0}";
            _sizeTag.LayoutTransform = new ScaleTransform(px, px);
            _sizeTag.Visibility = Visibility.Visible;
            _sizeTag.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(_sizeTag, r.Left + r.Width / 2 - _sizeTag.DesiredSize.Width / 2);
            Canvas.SetTop(_sizeTag, r.Bottom + 6 * px);
        }
        else
        {
            _sizeTag.Visibility = Visibility.Collapsed;
        }
    }

    private static void Place(FrameworkElement element, Rect r)
    {
        Canvas.SetLeft(element, r.X);
        Canvas.SetTop(element, r.Y);
        element.Width = Math.Max(0, r.Width);
        element.Height = Math.Max(0, r.Height);
    }

    /// <summary>As 8 alças: cantos e meios, começando no canto de cima à esquerda, no sentido do relógio.</summary>
    private static Point[] HandlePoints(Rect r) =>
    [
        new(r.Left, r.Top), new(r.Left + r.Width / 2, r.Top), new(r.Right, r.Top), new(r.Right, r.Top + r.Height / 2),
        new(r.Right, r.Bottom), new(r.Left + r.Width / 2, r.Bottom), new(r.Left, r.Bottom), new(r.Left, r.Top + r.Height / 2),
    ];

    private static readonly Edges[] HandleEdges =
    [
        Edges.Left | Edges.Top, Edges.Top, Edges.Right | Edges.Top, Edges.Right,
        Edges.Right | Edges.Bottom, Edges.Bottom, Edges.Left | Edges.Bottom, Edges.Left,
    ];

    private static Cursor CursorFor(Edges edges) => edges switch
    {
        Edges.Left | Edges.Top or Edges.Right | Edges.Bottom => Cursors.SizeNWSE,
        Edges.Right | Edges.Top or Edges.Left | Edges.Bottom => Cursors.SizeNESW,
        Edges.Top or Edges.Bottom => Cursors.SizeNS,
        _ => Cursors.SizeWE,
    };

    private Edges HandleAt(Point p)
    {
        if (SelectedBounds() is not { } r) return Edges.None;
        double reach = (HandleSize / 2 + 3) / _zoom;
        bool small = r.Width * _zoom < 36 || r.Height * _zoom < 36;
        var points = HandlePoints(r);
        for (int i = 0; i < points.Length; i++)
        {
            if (small && i % 2 == 1) continue;
            if (Math.Abs(p.X - points[i].X) <= reach && Math.Abs(p.Y - points[i].Y) <= reach) return HandleEdges[i];
        }
        return Edges.None;
    }

    private void ShowGuides(List<Guide> guides)
    {
        foreach (var line in _guides) _overlay.Children.Remove(line);
        _guides.Clear();
        double px = 1 / _zoom;
        foreach (var g in guides)
        {
            var line = g.Vertical
                ? new Line { X1 = g.Position, X2 = g.Position, Y1 = g.From - 8 * px, Y2 = g.To + 8 * px }
                : new Line { Y1 = g.Position, Y2 = g.Position, X1 = g.From - 8 * px, X2 = g.To + 8 * px };
            line.Stroke = GuideBrush;
            line.StrokeThickness = px;
            line.SnapsToDevicePixels = true;
            _guides.Add(line);
            _overlay.Children.Add(line);
        }
    }

    private void ShowHover(Piece? piece)
    {
        if (piece == null || _drag != Drag.None ||
            string.Equals(piece.Name, _model.SelectedName, StringComparison.OrdinalIgnoreCase))
        {
            _hover.Visibility = Visibility.Collapsed;
            return;
        }
        Place(_hover, ScreenDesignerModel.BoundsOf(piece));
        _hover.StrokeThickness = 1 / _zoom;
        _hover.Visibility = Visibility.Visible;
    }

    // ------------------------------------------------------------------ mouse

    private Point StagePoint(MouseEventArgs e) => e.GetPosition(_stage);
    private Point StagePoint(DragEventArgs e) => e.GetPosition(_stage);

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        if (_model.Layout == null) return;
        var p = StagePoint(e);

        var edges = HandleAt(p);
        if (edges != Edges.None && _model.Selected is { } selected)
        {
            Begin(Drag.Resizing, selected, p);
            _dragEdges = edges;
            e.Handled = true;
            return;
        }

        var hit = _model.HitTest(p, 2 / _zoom);
        _model.Select(hit?.Name);
        if (hit == null)
        {
            e.Handled = true;
            return;
        }
        if (e.ClickCount == 2)
        {
            EditTextRequested?.Invoke();
            e.Handled = true;
            return;
        }
        Begin(Drag.Pending, hit, p);
        e.Handled = true;
    }

    private void Begin(Drag kind, Piece piece, Point at)
    {
        _drag = kind;
        _dragName = piece.Name;
        _dragStart = at;
        _dragOrigin = ScreenDesignerModel.BoundsOf(piece);
        _dragCurrent = _dragOrigin;
        _snapper = new Snapper(_model.Layout!.Pieces
            .Where(o => !string.Equals(o.Name, piece.Name, StringComparison.OrdinalIgnoreCase))
            .Select(ScreenDesignerModel.BoundsOf))
        {
            Threshold = 6 / _zoom,
        };
        _hover.Visibility = Visibility.Collapsed;
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = StagePoint(e);
        if (_drag == Drag.None)
        {
            UpdateCursor(p);
            ShowHover(_model.HitTest(p, 2 / _zoom));
            return;
        }
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            Finish(commit: true);
            return;
        }

        if (_drag == Drag.Pending)
        {
            var delta = (p - _dragStart) * _zoom;
            if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold) return;
            _drag = Drag.Moving;
        }

        // Alt desliga o ímã (para posicionar livremente).
        bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
        _altDuringDrag |= alt;
        bool snap = !alt;
        List<Guide> guides;
        if (_drag == Drag.Moving)
        {
            var moved = _dragOrigin;
            moved.Offset(p.X - _dragStart.X, p.Y - _dragStart.Y);
            (_dragCurrent, guides) = _snapper!.Move(moved, snap);
            _dragCurrent = KeepVisible(_dragCurrent);
        }
        else
        {
            // Com Shift a proporção manda; o ímã ficaria brigando com ela.
            bool keepRatio = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 && IsCorner(_dragEdges);
            (_dragCurrent, guides) = _snapper!.Resize(Resized(p), _dragEdges, snap && !keepRatio);
            _dragCurrent = EnforceMinimum(_dragCurrent);
        }

        if (_elements.TryGetValue(_dragName!, out var element))
        {
            Canvas.SetLeft(element, _dragCurrent.X);
            Canvas.SetTop(element, _dragCurrent.Y);
            if (_drag == Drag.Resizing)
            {
                element.Width = _dragCurrent.Width;
                element.Height = _dragCurrent.Height;
                if (element.Tag == HiddenHolder && element is Grid holder && holder.Children[0] is FrameworkElement inner)
                {
                    inner.Width = _dragCurrent.Width;
                    inner.Height = _dragCurrent.Height;
                }
            }
        }
        ShowGuides(guides);
        UpdateOverlay();
    }

    /// <summary>Novo retângulo puxando as alças. Shift mantém a proporção (bom para imagens).</summary>
    private Rect Resized(Point p)
    {
        double dx = p.X - _dragStart.X, dy = p.Y - _dragStart.Y;
        double left = _dragOrigin.Left, top = _dragOrigin.Top, right = _dragOrigin.Right, bottom = _dragOrigin.Bottom;
        if (_dragEdges.HasFlag(Edges.Left)) left = Math.Min(left + dx, right - ScreenDesignerModel.MinSize);
        if (_dragEdges.HasFlag(Edges.Right)) right = Math.Max(right + dx, left + ScreenDesignerModel.MinSize);
        if (_dragEdges.HasFlag(Edges.Top)) top = Math.Min(top + dy, bottom - ScreenDesignerModel.MinSize);
        if (_dragEdges.HasFlag(Edges.Bottom)) bottom = Math.Max(bottom + dy, top + ScreenDesignerModel.MinSize);

        if (IsCorner(_dragEdges) && (Keyboard.Modifiers & ModifierKeys.Shift) != 0 && _dragOrigin.Height > 0)
        {
            double ratio = _dragOrigin.Width / _dragOrigin.Height;
            double width = right - left, height = bottom - top;
            if (width / ratio > height) height = width / ratio;
            else width = height * ratio;
            if (_dragEdges.HasFlag(Edges.Left)) left = right - width; else right = left + width;
            if (_dragEdges.HasFlag(Edges.Top)) top = bottom - height; else bottom = top + height;
        }
        return new Rect(new Point(left, top), new Point(right, bottom));
    }

    private static bool IsCorner(Edges edges) =>
        edges is (Edges.Left | Edges.Top) or (Edges.Right | Edges.Top) or (Edges.Left | Edges.Bottom) or (Edges.Right | Edges.Bottom);

    private static Rect EnforceMinimum(Rect r) =>
        new(r.X, r.Y, Math.Max(ScreenDesignerModel.MinSize, r.Width), Math.Max(ScreenDesignerModel.MinSize, r.Height));

    /// <summary>Um pedaço da peça sempre fica dentro do palco, para ela nunca se perder.</summary>
    private static Rect KeepVisible(Rect r)
    {
        double keep = ScreenDesignerModel.KeepInside;
        return new Rect(
            Math.Clamp(r.X, keep - r.Width, ScreenLayout.Width - keep),
            Math.Clamp(r.Y, keep - r.Height, ScreenLayout.Height - keep),
            r.Width, r.Height);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_drag != Drag.None)
        {
            Finish(commit: true);
            e.Handled = true;
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        // Perdeu o mouse no meio (ex.: outra janela apareceu): grava o que já foi feito.
        if (_drag != Drag.None) Finish(commit: true);
    }

    private void Finish(bool commit)
    {
        var kind = _drag;
        var name = _dragName;
        var rect = _dragCurrent;
        _drag = Drag.None;
        _dragName = null;
        _snapper = null;
        ShowGuides([]);
        if (IsMouseCaptured) ReleaseMouseCapture();

        if (kind is not (Drag.Moving or Drag.Resizing))
        {
            UpdateOverlay();                       // foi só um clique
            return;
        }
        if (commit && name != null && rect != _dragOrigin)
            _model.SetBounds(name, rect);          // grava e redesenha
        else
            Rebuild();                             // volta a peça para o lugar (Esc ou nada mudou)
    }

    private void UpdateCursor(Point p)
    {
        var edges = HandleAt(p);
        if (edges != Edges.None) Cursor = CursorFor(edges);
        else if (_model.HitTest(p, 2 / _zoom) != null) Cursor = Cursors.SizeAll;
        else Cursor = null;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_drag == Drag.None) _hover.Visibility = Visibility.Collapsed;
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        if (_model.Layout == null) return;
        Focus();
        var hit = _model.HitTest(StagePoint(e), 2 / _zoom);
        _model.Select(hit?.Name);
        ContextMenu = hit != null ? PieceMenu() : StageMenu(StagePoint(e));
        ContextMenu.PlacementTarget = this;
        ContextMenu.IsOpen = true;
        e.Handled = true;
    }

    private ContextMenu PieceMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(Item("Duplicar", "Ctrl+D", () => _model.Duplicate()));
        menu.Items.Add(Item("Copiar", "Ctrl+C", _model.Copy));
        menu.Items.Add(Item("Apagar", "Del", _model.Delete));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Trazer para frente", "Ctrl+]", _model.BringToFront));
        menu.Items.Add(Item("Enviar para trás", "Ctrl+[", _model.SendToBack));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Copiar o código da peça (game.Find)", null, () =>
        {
            if (_model.Selected is { } piece) TrySetClipboard(ScreenDesignerModel.CodeExample(piece).Replace("\n", Environment.NewLine));
        }));
        return menu;
    }

    private ContextMenu StageMenu(Point at)
    {
        var menu = new ContextMenu();
        var paste = Item("Colar", "Ctrl+V", () => _model.Paste());
        paste.IsEnabled = _model.CanPaste;
        menu.Items.Add(paste);
        menu.Items.Add(new Separator());
        foreach (var type in Enum.GetValues<PieceType>())
            menu.Items.Add(Item("Nova peça: " + Piece.Describe(type), null, () => _model.Add(type, at)));
        return menu;
    }

    private static MenuItem Item(string header, string? gesture, Action action)
    {
        var item = new MenuItem { Header = header, InputGestureText = gesture ?? "" };
        item.Click += (_, _) => action();
        return item;
    }

    internal static void TrySetClipboard(string text)
    {
        try { Clipboard.SetText(text); }
        catch (System.Runtime.InteropServices.COMException) { }
    }

    // ------------------------------------------------------------------ soltar peças da paleta

    protected override void OnDragEnter(DragEventArgs e) => OnDragOver(e);

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        if (e.Data.GetData(PieceDragFormat) is not PieceType type || _model.Layout == null)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
        var p = StagePoint(e);
        var sample = Piece.CreateDefault(type, "", 0, 0);
        var ghost = new Rect(p.X - sample.Width / 2, p.Y - sample.Height / 2, sample.Width, sample.Height);
        Place(_dropGhost, ghost);
        _dropGhost.StrokeThickness = 1.5 / _zoom;
        _dropGhost.Visibility = Visibility.Visible;
    }

    protected override void OnDragLeave(DragEventArgs e)
    {
        base.OnDragLeave(e);
        _dropGhost.Visibility = Visibility.Collapsed;
    }

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        _dropGhost.Visibility = Visibility.Collapsed;
        if (e.Data.GetData(PieceDragFormat) is not PieceType type) return;
        _model.Add(type, StagePoint(e));
        Focus();
        e.Handled = true;
    }

    // ------------------------------------------------------------------ teclado

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        var mods = Keyboard.Modifiers;
        bool ctrl = (mods & ModifierKeys.Control) != 0;
        bool shift = (mods & ModifierKeys.Shift) != 0;
        double step = shift ? 10 : 1;
        bool handled = true;
        switch (e.Key)
        {
            case Key.Left: _model.Nudge(-step, 0); break;
            case Key.Right: _model.Nudge(step, 0); break;
            case Key.Up: _model.Nudge(0, -step); break;
            case Key.Down: _model.Nudge(0, step); break;
            case Key.Delete:
            case Key.Back:
                _model.Delete();
                break;
            case Key.Escape:
                if (_drag != Drag.None) Finish(commit: false);
                else _model.Select(null);
                break;
            case Key.Tab:
                SelectNext(shift ? -1 : 1);
                break;
            case Key.D when ctrl: _model.Duplicate(); break;
            case Key.C when ctrl: _model.Copy(); break;
            case Key.V when ctrl: _model.Paste(); break;
            case Key.Z when ctrl && shift: _model.Redo(); break;
            case Key.Z when ctrl: _model.Undo(); break;
            case Key.Y when ctrl: _model.Redo(); break;
            case Key.OemCloseBrackets when ctrl: _model.BringToFront(); break;
            case Key.OemOpenBrackets when ctrl: _model.SendToBack(); break;
            case Key.Enter: EditTextRequested?.Invoke(); break;
            default: handled = false; break;
        }
        e.Handled = handled;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (IsAlt(e) && _drag != Drag.None)
        {
            _altDuringDrag = true;
            e.Handled = true;
        }
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        base.OnPreviewKeyUp(e);
        if (IsAlt(e) && (_drag != Drag.None || _altDuringDrag))
        {
            _altDuringDrag = false;
            e.Handled = true;
        }
    }

    private static bool IsAlt(KeyEventArgs e) => e.Key == Key.System && e.SystemKey is Key.LeftAlt or Key.RightAlt;

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        // Soltar as setas fecha o passo de desfazer: a próxima sequência é outro passo.
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down) _model.EndMerge();
    }

    /// <summary>Tab passa para a próxima peça (na ordem de desenho).</summary>
    private void SelectNext(int direction)
    {
        var pieces = _model.Layout?.Pieces;
        if (pieces == null || pieces.Count == 0) return;
        int index = _model.Selected is { } current ? pieces.FindIndex(p => p.Name == current.Name) : -1;
        index = ((index + direction) % pieces.Count + pieces.Count) % pieces.Count;
        _model.Select(pieces[index].Name);
    }
}
