using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CSharpLab.GameEngine;

namespace CSharpLab.Screens;

/// <summary>
/// Camadas: todas as peças da tela, da frente (em cima) para trás (embaixo). Clicar seleciona, mesmo uma
/// peça escondida atrás de outra; arrastar uma linha muda a ordem (quem fica na frente de quem).
/// </summary>
public sealed class LayersPanel : DockPanel
{
    private const double DragThreshold = 4;

    private readonly StackPanel _rows = new();
    private readonly Canvas _overlay = new() { IsHitTestVisible = false };
    private readonly Border _dropLine = new() { Height = 2, Visibility = Visibility.Collapsed, CornerRadius = new CornerRadius(1) };
    private readonly TextBlock _empty = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(14, 6, 10, 0) };
    private readonly Button _forward;
    private readonly Button _backward;
    private readonly List<(Border Row, Piece Piece)> _shown = [];
    private ScreenDesignerModel? _model;

    // Arrastar uma linha
    private string? _pressed;
    private Point _pressPoint;
    private bool _dragging;
    private (string Target, bool InFront)? _drop;

    /// <summary>Uma peça foi escolhida na lista (o palco recebe o teclado, para as setas e o Del valerem para ela).</summary>
    public event Action? PieceChosen;

    public LayersPanel()
    {
        var header = new DockPanel { Margin = new Thickness(14, 10, 8, 4) };
        _backward = StepButton("\uE74B", "Um passo para trás", -1);
        _forward = StepButton("\uE74A", "Um passo para a frente", +1);
        DockPanel.SetDock(_backward, Dock.Right);
        DockPanel.SetDock(_forward, Dock.Right);
        header.Children.Add(_backward);
        header.Children.Add(_forward);
        var title = new TextBlock
        {
            Text = "CAMADAS",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "A peça de cima da lista fica na frente das outras no jogo. Arraste uma linha para mudar a ordem.",
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        header.Children.Add(title);
        SetDock(header, Dock.Top);
        Children.Add(header);

        var hint = new TextBlock { Text = "Em cima = na frente. Arraste para mudar.", FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(14, 0, 10, 6) };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        SetDock(hint, Dock.Top);
        Children.Add(hint);

        _empty.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        _dropLine.SetResourceReference(Border.BackgroundProperty, "Accent");
        _overlay.Children.Add(_dropLine);
        var content = new Grid { Margin = new Thickness(6, 0, 6, 8) };
        content.Children.Add(_rows);
        content.Children.Add(_overlay);
        var body = new Grid();
        body.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false });
        body.Children.Add(_empty);
        Children.Add(body);

        PreviewMouseMove += OnMouseMove;
        PreviewMouseLeftButtonUp += OnMouseUp;
        LostMouseCapture += (_, _) => EndDrag();
        Update();
    }

    /// <summary>O aviso quando não há tela (cena só de código, ou o mapa aberto).</summary>
    public string EmptyMessage
    {
        get => _emptyMessage;
        set
        {
            _emptyMessage = value;
            Update();
        }
    }

    private string _emptyMessage = "Esta cena ainda não tem tela desenhada.";

    /// <summary>A tela mostrada (null: a cena não tem tela desenhada).</summary>
    public ScreenDesignerModel? Model
    {
        get => _model;
        set
        {
            if (_model == value) return;
            if (_model != null)
            {
                _model.Changed -= Update;
                _model.SelectionChanged -= UpdateSelection;
            }
            _model = value;
            if (_model != null)
            {
                _model.Changed += Update;
                _model.SelectionChanged += UpdateSelection;
            }
            Update();
        }
    }

    private Button StepButton(string glyph, string tip, int direction)
    {
        var button = new Button { Content = glyph, ToolTip = tip, Width = 24, Height = 22, FontSize = 11, Focusable = false };
        button.SetResourceReference(StyleProperty, "IconButton");
        button.Click += (_, _) =>
        {
            if (_model?.SelectedName is { } name) _model.MoveLayerStep(name, direction);
        };
        return button;
    }

    private void Update()
    {
        EndDrag();
        _rows.Children.Clear();
        _shown.Clear();
        var layers = _model?.Layers() ?? [];
        foreach (var (piece, depth) in layers)
        {
            var row = Row(piece, depth);
            _shown.Add((row, piece));
            _rows.Children.Add(row);
        }
        _empty.Text = _model == null ? EmptyMessage
            : _model.Layout == null ? "A tela tem um erro: corrija para ver as camadas."
            : "Nenhuma peça ainda. Escolha uma em PEÇAS.";
        _empty.Visibility = layers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateSelection();
    }

    private Border Row(Piece piece, int depth)
    {
        var icon = PiecePalette.Icon(piece.Type);
        icon.Margin = new Thickness(0, 0, 8, 0);
        icon.VerticalAlignment = VerticalAlignment.Center;
        var line = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(icon, Dock.Left);
        line.Children.Add(icon);
        if (!piece.Visible)
        {
            var hidden = new TextBlock { Text = "\uED1A", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), ToolTip = "Começa escondida no jogo (Visible = false)" };
            hidden.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            hidden.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
            DockPanel.SetDock(hidden, Dock.Right);
            line.Children.Add(hidden);
        }
        var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var name = new System.Windows.Documents.Run(piece.Name);
        name.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextPrimary");
        text.Inlines.Add(name);
        var detail = Detail(piece);
        var muted = new System.Windows.Documents.Run("  " + detail) { FontSize = 11.5 };
        muted.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextMuted");
        text.Inlines.Add(muted);
        line.Children.Add(text);

        var row = new Border
        {
            Child = line,
            Height = 28,
            Padding = new Thickness(8 + depth * 16, 0, 6, 0),
            CornerRadius = new CornerRadius(5),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = $"{Piece.Describe(piece.Type)} \"{piece.Name}\"" + (depth > 0 ? $" (no cartão da Lista \"{piece.List}\")" : ""),
        };
        System.Windows.Automation.AutomationProperties.SetName(row, $"Camada {piece.Name}");
        row.MouseEnter += (_, _) => { if (!_dragging) Paint(row, piece, hover: true); };
        row.MouseLeave += (_, _) => Paint(row, piece, hover: false);
        row.MouseLeftButtonDown += (_, e) =>
        {
            if (_model == null) return;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) || Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                _model.ToggleSelect(piece.Name);
            }
            else
            {
                if (!_model.IsSelected(piece.Name)) _model.Select(piece.Name);
                _pressed = piece.Name;
                _pressPoint = e.GetPosition(this);
                CaptureMouse();
            }
            e.Handled = true;
        };
        return row;
    }

    /// <summary>O texto da peça (para achar "qual é qual"), ou o tipo dela.</summary>
    private static string Detail(Piece piece)
    {
        var text = piece.Text?.ReplaceLineEndings(" ").Trim();
        if (string.IsNullOrEmpty(text)) return Piece.Describe(piece.Type).ToLowerInvariant();
        return text.Length > 28 ? "\"" + text[..27] + "…\"" : "\"" + text + "\"";
    }

    private void Paint(Border row, Piece piece, bool hover)
    {
        if (_model?.IsSelected(piece.Name) == true) row.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
        else if (hover) row.SetResourceReference(Border.BackgroundProperty, "BgHover");
        else row.Background = Brushes.Transparent;
    }

    private void UpdateSelection()
    {
        foreach (var (row, piece) in _shown) Paint(row, piece, row.IsMouseOver);
        bool one = _model?.SelectedName != null;
        _forward.IsEnabled = one;
        _backward.IsEnabled = one;
        if (_model?.SelectedName is { } name && _shown.FirstOrDefault(s => string.Equals(s.Piece.Name, name, StringComparison.OrdinalIgnoreCase)).Row is { } selected)
            selected.BringIntoView();
    }

    // ------------------------------------------------------------------ arrastar para mudar a ordem

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_pressed == null || e.LeftButton != MouseButtonState.Pressed) return;
        var now = e.GetPosition(this);
        if (!_dragging)
        {
            if (Math.Abs(now.Y - _pressPoint.Y) < DragThreshold) return;
            _dragging = true;
        }
        ShowDrop(e.GetPosition(_rows));
    }

    /// <summary>Onde a peça cairia: entre duas linhas do mesmo grupo (soltas, ou do mesmo cartão).</summary>
    private void ShowDrop(Point point)
    {
        _drop = null;
        _dropLine.Visibility = Visibility.Collapsed;
        if (_pressed == null || _model == null) return;
        foreach (var (row, piece) in _shown)
        {
            var top = row.TranslatePoint(new Point(0, 0), _rows).Y;
            if (point.Y < top || point.Y > top + row.ActualHeight) continue;
            if (string.Equals(piece.Name, _pressed, StringComparison.OrdinalIgnoreCase) || !_model.SameLayerGroup(_pressed, piece.Name)) return;
            // Metade de cima da linha: fica na frente dela (acima na lista).
            bool inFront = point.Y < top + row.ActualHeight / 2;
            _drop = (piece.Name, inFront);
            _dropLine.Width = Math.Max(0, _rows.ActualWidth - row.Padding.Left + 6);
            Canvas.SetLeft(_dropLine, row.Padding.Left - 6);
            Canvas.SetTop(_dropLine, (inFront ? top : top + row.ActualHeight) - 1);
            _dropLine.Visibility = Visibility.Visible;
            return;
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_pressed == null) return;
        var name = _pressed;
        var drop = _drop;
        bool dragged = _dragging;
        EndDrag();
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (dragged && drop is { } target) _model?.MoveLayer(name, target.Target, target.InFront);
        else if (!dragged) _model?.Select(name);
        PieceChosen?.Invoke();
        e.Handled = true;
    }

    private void EndDrag()
    {
        _pressed = null;
        _dragging = false;
        _drop = null;
        _dropLine.Visibility = Visibility.Collapsed;
    }

    /// <summary>Os nomes na ordem da lista (para os testes).</summary>
    internal IReadOnlyList<string> Names => _shown.Select(s => s.Piece.Name).ToList();

    public void Detach() => Model = null;
}
