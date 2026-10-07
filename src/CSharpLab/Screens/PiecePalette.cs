using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CSharpLab.GameEngine;
using WpfColor = System.Windows.Media.Color;

namespace CSharpLab.Screens;

/// <summary>
/// As peças que dá para colocar na tela. Clique: a peça aparece no meio do palco.
/// Arraste: ela vai para onde o mouse soltar.
/// </summary>
public sealed class PiecePalette : Border
{
    private readonly ScreenDesignerModel _model;
    private readonly List<FrameworkElement> _wideOnly = [];
    private readonly StackPanel _list;
    private bool _compact;

    /// <summary>Uma peça foi colocada por clique (o palco recebe o teclado, para as setas e o Del valerem para ela).</summary>
    public event Action? PieceAdded;

    private static readonly (PieceType Type, string Hint)[] Entries =
    [
        (PieceType.Text, "Um texto. No código: game.Find(\"Nome\").Text = \"...\";"),
        (PieceType.Button, "O jogador clica e o código roda: game.Find(\"Nome\").OnClick(() => { });"),
        (PieceType.Bar, "Vida, mana, energia: game.Find(\"Nome\").Value = health;"),
        (PieceType.Image, "Uma imagem da pasta Assets do projeto."),
        (PieceType.Box, "Um fundo colorido para agrupar outras peças."),
        (PieceType.Input, "Uma pergunta com campo para o jogador escrever: OnAnswer(answer => { });"),
        (PieceType.Messages, "Onde aparecem os textos do game.Write."),
    ];

    public PiecePalette(ScreenDesignerModel model)
    {
        _model = model;
        Width = WideWidth;
        BorderThickness = new Thickness(0, 0, 1, 0);
        SetResourceReference(BorderBrushProperty, "BorderBrush");
        SetResourceReference(BackgroundProperty, "BgSidebar");

        var list = _list = new StackPanel { Margin = new Thickness(8, 10, 8, 10) };
        var header = new TextBlock { Text = "PEÇAS", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(6, 0, 0, 8) };
        header.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        list.Children.Add(header);
        _wideOnly.Add(header);
        foreach (var (type, hint) in Entries) list.Children.Add(Entry(type, hint));

        var tip = new TextBlock
        {
            Text = "Clique para pôr no meio, ou arraste até o lugar certo.",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(6, 12, 4, 0),
        };
        tip.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        list.Children.Add(tip);
        _wideOnly.Add(tip);
        Child = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private const double WideWidth = 178;
    private const double CompactWidth = 54;

    /// <summary>Só os desenhos das peças (o nome aparece ao parar o mouse): sobra mais espaço para o palco em telas pequenas.</summary>
    public bool Compact
    {
        get => _compact;
        set
        {
            if (_compact == value) return;
            _compact = value;
            Width = value ? CompactWidth : WideWidth;
            _list.Margin = value ? new Thickness(6, 12, 6, 10) : new Thickness(8, 10, 8, 10);
            foreach (var element in _wideOnly) element.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private FrameworkElement Entry(PieceType type, string hint)
    {
        var label = new TextBlock { Text = Piece.Describe(type), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        _wideOnly.Add(label);
        var row = new DockPanel();
        var icon = Icon(type);
        icon.Margin = new Thickness(0, 0, 10, 0);
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        row.Children.Add(label);
        // Sem o nome (modo compacto), o desenho fica centralizado.
        label.IsVisibleChanged += (_, _) => icon.Margin = label.IsVisible ? new Thickness(0, 0, 10, 0) : new Thickness(0);

        var item = new Border
        {
            Child = row,
            Padding = new Thickness(8, 6, 8, 6),
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 0, 2),
            ToolTip = new ToolTip { Content = TipContent(type, hint) },
        };
        System.Windows.Automation.AutomationProperties.SetName(item, "Nova peça: " + Piece.Describe(type));
        item.MouseEnter += (_, _) => item.SetResourceReference(Border.BackgroundProperty, "BgHover");
        item.MouseLeave += (_, _) => item.Background = Brushes.Transparent;

        Point? down = null;
        item.MouseLeftButtonDown += (_, e) =>
        {
            down = e.GetPosition(item);
            item.CaptureMouse();
            e.Handled = true;
        };
        item.MouseMove += (_, e) =>
        {
            if (down is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
            var now = e.GetPosition(item);
            if (Math.Abs(now.X - start.X) < 4 && Math.Abs(now.Y - start.Y) < 4) return;
            down = null;
            item.ReleaseMouseCapture();
            DragDrop.DoDragDrop(item, new DataObject(ScreenStage.PieceDragFormat, type), DragDropEffects.Copy);
        };
        item.MouseLeftButtonUp += (_, e) =>
        {
            if (down == null) return;
            down = null;
            item.ReleaseMouseCapture();
            _model.Add(type);
            PieceAdded?.Invoke();
            e.Handled = true;
        };
        return item;
    }

    private static FrameworkElement TipContent(PieceType type, string hint)
    {
        var panel = new StackPanel { MaxWidth = 300 };
        panel.Children.Add(new TextBlock { Text = Piece.Describe(type), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
        panel.Children.Add(new TextBlock { Text = hint, TextWrapping = TextWrapping.Wrap });
        return panel;
    }

    private static Brush B(byte r, byte g, byte b, byte a = 0xFF)
    {
        var brush = new SolidColorBrush(WpfColor.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    /// <summary>Um desenhinho de cada peça (sem depender de fontes de ícones).</summary>
    private static FrameworkElement Icon(PieceType type)
    {
        var canvas = new Canvas { Width = 22, Height = 16, SnapsToDevicePixels = true };
        void Add(UIElement e, double x, double y)
        {
            Canvas.SetLeft(e, x);
            Canvas.SetTop(e, y);
            canvas.Children.Add(e);
        }
        var light = B(0xDD, 0xE1, 0xE8);
        var muted = B(0x8C, 0x93, 0xA0);
        switch (type)
        {
            case PieceType.Text:
                Add(new TextBlock { Text = "Aa", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = light }, 1, -2);
                break;
            case PieceType.Button:
                Add(new Rectangle { Width = 22, Height = 12, RadiusX = 4, RadiusY = 4, Fill = B(0x3D, 0x7B, 0xE8) }, 0, 2);
                Add(new Rectangle { Width = 10, Height = 2, Fill = Brushes.White }, 6, 7);
                break;
            case PieceType.Bar:
                Add(new Rectangle { Width = 22, Height = 6, RadiusX = 3, RadiusY = 3, Fill = B(0x2E, 0x32, 0x3C) }, 0, 5);
                Add(new Rectangle { Width = 14, Height = 6, RadiusX = 3, RadiusY = 3, Fill = B(0x5C, 0xCB, 0x7A) }, 0, 5);
                break;
            case PieceType.Image:
                Add(new Rectangle { Width = 20, Height = 15, RadiusX = 2, RadiusY = 2, Stroke = muted, StrokeThickness = 1.2 }, 1, 0.5);
                Add(new Polygon { Points = [new(3, 13), new(8, 7), new(12, 11), new(15, 8), new(19, 13)], Fill = B(0x5E, 0xA8, 0xFF) }, 0, 0);
                Add(new Ellipse { Width = 3, Height = 3, Fill = B(0xF2, 0xC1, 0x4E) }, 14, 3);
                break;
            case PieceType.Box:
                Add(new Rectangle { Width = 20, Height = 14, RadiusX = 3, RadiusY = 3, Fill = B(0x5E, 0xA8, 0xFF, 0x50), Stroke = B(0x5E, 0xA8, 0xFF, 0xB0), StrokeThickness = 1 }, 1, 1);
                break;
            case PieceType.Input:
                Add(new Rectangle { Width = 22, Height = 12, RadiusX = 2, RadiusY = 2, Fill = B(0x21, 0x24, 0x2C), Stroke = muted, StrokeThickness = 1 }, 0, 2);
                Add(new Rectangle { Width = 1.4, Height = 7, Fill = light }, 4, 4.5);
                break;
            case PieceType.Messages:
                Add(new Rectangle { Width = 20, Height = 15, RadiusX = 3, RadiusY = 3, Fill = B(0x21, 0x24, 0x2C), Stroke = muted, StrokeThickness = 1 }, 1, 0.5);
                Add(new Rectangle { Width = 12, Height = 2, Fill = light }, 4, 4);
                Add(new Rectangle { Width = 2, Height = 4, Fill = B(0xF2, 0xC1, 0x4E) }, 4, 8);
                Add(new Rectangle { Width = 9, Height = 2, Fill = muted }, 7, 9);
                break;
        }
        return canvas;
    }
}
