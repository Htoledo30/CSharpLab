using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WpfImage = System.Windows.Controls.Image;

namespace CSharpLab.GameEngine;

/// <summary>Uma linha da peça Mensagens (o que o game.Write escreveu).</summary>
internal sealed record MessageLine(string Text, Color? Color, bool IsNews);

/// <summary>
/// Como desenhar: no jogo (as peças respondem a cliques) ou no editor visual (só a aparência,
/// com textos de exemplo onde o jogo ainda vai escrever).
/// </summary>
internal sealed class RenderContext
{
    public bool Live { get; init; }
    public Action<Piece>? Click { get; init; }
    public Action<Piece, string>? Answer { get; init; }
    public IReadOnlyList<MessageLine> Messages { get; init; } = [];
    /// <summary>Peças que respondem a clique (só as com OnClick no código ficam com a mãozinha).</summary>
    public Func<Piece, bool>? IsClickable { get; init; }
}

/// <summary>
/// Desenha cada peça de uma tela desenhada. Usado pela janela do jogo e pela aba Tela do CSharp Lab:
/// o mesmo código, o mesmo resultado.
/// </summary>
internal static class ScreenRenderer
{
    /// <summary>A peça desenhada no tamanho dela (quem chama posiciona em X e Y).</summary>
    public static FrameworkElement Create(Piece piece, RenderContext context)
    {
        FrameworkElement element = piece.Type switch
        {
            PieceType.Text => TextPiece(piece),
            PieceType.Button => ButtonPiece(piece, context),
            PieceType.Bar => BarPiece(piece),
            PieceType.Image => ImagePiece(piece, context),
            PieceType.Box => BoxPiece(piece),
            PieceType.Input => InputPiece(piece, context),
            PieceType.Messages => MessagesPiece(piece, context),
            _ => new Border(),
        };
        element.Width = Math.Max(1, piece.Width);
        element.Height = Math.Max(1, piece.Height);
        if (!context.Live) element.IsHitTestVisible = false;
        return element;
    }

    /// <summary>Fundo da tela (imagem da pasta Assets cobrindo o palco inteiro). Null sem fundo.</summary>
    public static FrameworkElement? Background(ScreenLayout layout)
    {
        if (layout.Background == null) return null;
        var image = Theme.LoadImage(layout.Background);
        if (image == null) return null;
        return new WpfImage
        {
            Source = image,
            Stretch = Stretch.UniformToFill,
            Width = ScreenLayout.Width,
            Height = ScreenLayout.Height,
            IsHitTestVisible = false,
        };
    }

    private static TextBlock Label(Piece piece, string text, Brush? foreground = null) => new()
    {
        Text = text,
        FontFamily = Theme.Font,
        FontSize = piece.FontSize,
        TextWrapping = TextWrapping.Wrap,
        Foreground = foreground ?? Theme.Text,
    };

    private static FrameworkElement TextPiece(Piece piece)
    {
        var block = Label(piece, piece.Text ?? "", piece.Color is { } c ? Theme.Brush(c) : Theme.Text);
        block.FontWeight = piece.Bold == true ? FontWeights.SemiBold : FontWeights.Normal;
        block.TextAlignment = piece.Align switch
        {
            TextAlign.Center => TextAlignment.Center,
            TextAlign.Right => TextAlignment.Right,
            _ => TextAlignment.Left,
        };
        block.LineHeight = Math.Round(piece.FontSize * 1.4);
        return new Border { Child = block, ClipToBounds = true, Background = Brushes.Transparent };
    }

    private static FrameworkElement ButtonPiece(Piece piece, RenderContext context)
    {
        var label = Label(piece, piece.Text ?? "", Brushes.White);
        label.TextAlignment = TextAlignment.Center;
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        var button = Theme.MakeButton(label, piece.Color);
        if (context.Live && context.Click != null)
            button.Click += (_, _) => context.Click(piece);
        return button;
    }

    private static FrameworkElement BarPiece(Piece piece)
    {
        int max = piece.BarMax, value = piece.BarValue;
        double fraction = Math.Clamp((double)value / max, 0, 1);
        var size = Math.Min(piece.FontSize, 16);

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 5), LastChildFill = true };
        var amount = new TextBlock { Text = $"{value} / {max}", FontFamily = Theme.Font, FontSize = size - 1, Foreground = Theme.Muted };
        DockPanel.SetDock(amount, Dock.Right);
        header.Children.Add(amount);
        header.Children.Add(new TextBlock
        {
            Text = piece.Text ?? "",
            FontFamily = Theme.Font,
            FontSize = size,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.Text,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 8, 0),
        });

        double trackHeight = Math.Clamp(piece.Height - 27, 6, 28);
        var fill = new Border
        {
            Background = Theme.Brush(piece.Color ?? Color.Green),
            CornerRadius = new CornerRadius(trackHeight / 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = 0,
        };
        var track = new Border
        {
            Background = Theme.Track,
            CornerRadius = new CornerRadius(trackHeight / 2),
            Height = trackHeight,
            VerticalAlignment = VerticalAlignment.Top,
            Child = fill,
        };
        track.SizeChanged += (_, e) => fill.Width = e.NewSize.Width * fraction;

        var panel = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);
        panel.Children.Add(track);
        return panel;
    }

    private static FrameworkElement ImagePiece(Piece piece, RenderContext context)
    {
        var image = Theme.LoadImage(piece.Image);
        FrameworkElement content;
        if (image != null)
        {
            content = new WpfImage { Source = image, Stretch = Stretch.Uniform };
        }
        else
        {
            // Sem imagem (ainda): um quadro tracejado com o nome do arquivo esperado.
            var hint = string.IsNullOrWhiteSpace(piece.Image)
                ? "Imagem\n(escolha o arquivo)"
                : $"Imagem não encontrada:\n{piece.Image}";
            var frame = new Grid { Background = Theme.Freeze(System.Windows.Media.Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)) };
            frame.Children.Add(new System.Windows.Shapes.Rectangle
            {
                Stroke = Theme.Muted,
                StrokeThickness = 1.5,
                StrokeDashArray = [4, 3],
                RadiusX = 8,
                RadiusY = 8,
            });
            frame.Children.Add(new TextBlock
            {
                Text = hint,
                FontFamily = Theme.Font,
                FontSize = 13,
                Foreground = Theme.Muted,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8),
            });
            content = frame;
        }

        var host = new Border { Child = content, Background = Brushes.Transparent };
        if (context.Live && context.Click != null && context.IsClickable?.Invoke(piece) == true)
        {
            host.Cursor = Cursors.Hand;
            host.MouseEnter += (_, _) => host.Opacity = 0.88;
            host.MouseLeave += (_, _) => host.Opacity = 1;
            host.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                context.Click(piece);
            };
        }
        return host;
    }

    private static FrameworkElement BoxPiece(Piece piece)
    {
        if (piece.Color is not { } color)
            return new Border { Background = Theme.Panel, CornerRadius = new CornerRadius(10) };
        var rgb = Theme.Rgb(color);
        return new Border
        {
            Background = Theme.Freeze(System.Windows.Media.Color.FromArgb(0x40, rgb.R, rgb.G, rgb.B)),
            BorderBrush = Theme.Freeze(System.Windows.Media.Color.FromArgb(0xA0, rgb.R, rgb.G, rgb.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
        };
    }

    private static FrameworkElement InputPiece(Piece piece, RenderContext context)
    {
        var question = Label(piece, piece.Text ?? "");
        question.Margin = new Thickness(0, 0, 0, 8);
        question.TextTrimming = TextTrimming.CharacterEllipsis;

        var box = new TextBox
        {
            FontFamily = Theme.Font,
            FontSize = piece.FontSize + 1,
            Padding = new Thickness(8, 5, 8, 5),
            Background = Theme.Panel,
            Foreground = Theme.Text,
            CaretBrush = Theme.Text,
            BorderBrush = Theme.PanelBorder,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        var okLabel = new TextBlock { Text = "OK", FontFamily = Theme.Font, FontSize = piece.FontSize, Foreground = Brushes.White };
        var ok = Theme.MakeButton(okLabel);
        ok.Margin = new Thickness(10, 0, 0, 0);

        if (context.Live && context.Answer != null)
        {
            void Submit()
            {
                var answer = box.Text.Trim();
                if (answer.Length == 0)
                {
                    box.Focus();
                    return;
                }
                context.Answer(piece, answer);
            }
            box.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                Submit();
            };
            ok.Click += (_, _) => Submit();
        }

        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(ok, Dock.Right);
        row.Children.Add(ok);
        row.Children.Add(box);

        var panel = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(question, Dock.Top);
        panel.Children.Add(question);
        panel.Children.Add(new Border { Child = row, VerticalAlignment = VerticalAlignment.Top, MaxHeight = 52 });
        panel.Tag = box; // a janela do jogo põe o cursor aqui
        return panel;
    }

    private static FrameworkElement MessagesPiece(Piece piece, RenderContext context)
    {
        var list = new StackPanel();
        IReadOnlyList<MessageLine> lines = context.Live
            ? context.Messages
            : [new("As mensagens do game.Write aparecem aqui.", Color.Gray, false), new("Exemplo: Você causou 7 de dano!", null, true)];
        foreach (var line in lines)
            list.Children.Add(MessageView(line, piece.FontSize));

        // Sem barra de rolagem: se não couber, as mensagens mais novas (embaixo) ficam à vista.
        var viewport = new Grid { ClipToBounds = true, Margin = new Thickness(14, 10, 14, 10) };
        viewport.Children.Add(list);
        list.VerticalAlignment = VerticalAlignment.Top;
        void Settle() => list.VerticalAlignment = list.ActualHeight > viewport.ActualHeight ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        viewport.SizeChanged += (_, _) => Settle();
        list.SizeChanged += (_, _) => Settle();
        return new Border { Background = Theme.Panel, CornerRadius = new CornerRadius(10), Child = viewport };
    }

    /// <summary>Uma mensagem: normal, ou destacada quando veio de um clique.</summary>
    public static FrameworkElement MessageView(MessageLine line, double fontSize)
    {
        var block = new TextBlock
        {
            Text = line.Text,
            FontFamily = Theme.Font,
            FontSize = fontSize,
            LineHeight = Math.Round(fontSize * 1.45),
            TextWrapping = TextWrapping.Wrap,
            Foreground = line.Color is { } c ? Theme.Brush(c) : Theme.Text,
        };
        if (!line.IsNews)
        {
            block.Margin = new Thickness(0, 0, 0, 6);
            return block;
        }
        return new Border
        {
            Background = Theme.NewsBack(line.Color),
            BorderBrush = Theme.Brush(line.Color ?? Color.Gold),
            BorderThickness = new Thickness(3, 0, 0, 0),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 2, 0, 6),
            Child = block,
        };
    }
}
