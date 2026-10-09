using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using WpfImage = System.Windows.Controls.Image;

namespace CSharpLab.GameEngine;

/// <summary>
/// Uma linha da peça Mensagens (o que o game.Write escreveu). <see cref="IsNews"/>: do último clique (destacada);
/// <see cref="IsOld"/>: de cliques anteriores (apagada).
/// </summary>
internal sealed record MessageLine(string Text, Color? Color, bool IsNews, bool IsOld = false);

/// <summary>
/// Como desenhar: no jogo (as peças respondem a cliques) ou no editor visual (só a aparência,
/// com textos de exemplo onde o jogo ainda vai escrever).
/// </summary>
/// <summary>O tamanho que a peça precisaria para o texto caber inteiro.</summary>
internal sealed record TextFit(double Width, double Height);

internal sealed class RenderContext
{
    public bool Live { get; init; }
    public Action<Piece>? Click { get; init; }
    public Action<Piece, string>? Answer { get; init; }
    public IReadOnlyList<MessageLine> Messages { get; init; } = [];
    /// <summary>Peças que respondem a clique (só as com OnClick no código ficam com a mãozinha).</summary>
    public Func<Piece, bool>? IsClickable { get; init; }
    /// <summary>No jogo: os cartões que o Show pôs em cada Lista.</summary>
    public Func<Piece, IReadOnlyList<Card>>? Cards { get; init; }
    /// <summary>No editor: as peças do cartão modelo de cada Lista (para a prévia dos outros cartões).</summary>
    public Func<Piece, IReadOnlyList<Piece>>? Members { get; init; }
    /// <summary>Cada peça desenhada (soltas e de cartões), para a janela achar quem treme ou pisca.</summary>
    public Action<Piece, FrameworkElement>? Created { get; init; }
    /// <summary>Onde estava a rolagem de cada Lista, para ela não voltar para o topo a cada clique.</summary>
    public Dictionary<string, double>? ScrollOffsets { get; init; }
}

/// <summary>
/// Desenha cada peça de uma tela desenhada. Usado pela janela do jogo e pelo Estúdio do CSharp Lab:
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
            PieceType.List => ListPiece(piece, context),
            _ => new Border(),
        };
        element.Width = Math.Max(1, piece.Width);
        element.Height = Math.Max(1, piece.Height);
        if (!context.Live || IsDecoration(piece, context)) element.IsHitTestVisible = false;
        context.Created?.Invoke(piece, element);
        return element;
    }

    /// <summary>
    /// Peça que só enfeita (Texto, Caixa, Barra, Imagem sem OnClick) deixa o clique passar:
    /// um Texto ou um ícone por cima de um Botão não atrapalha clicar nele.
    /// </summary>
    private static bool IsDecoration(Piece piece, RenderContext context) => piece.Type switch
    {
        PieceType.Text => piece.Scroll != true,
        PieceType.Box or PieceType.Bar => true,
        PieceType.Image => !(context.Click != null && piece.Enabled && context.IsClickable?.Invoke(piece) == true),
        _ => false,
    };

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
        FontFamily = Theme.FontOf(Theme.FontFor(piece)),
        FontSize = piece.FontSize * Theme.FontScale(Theme.FontFor(piece)),
        TextWrapping = TextWrapping.Wrap,
        Foreground = foreground ?? Theme.Text,
    };

    /// <summary>Sombra escura atrás das letras: o texto fica legível em cima de qualquer fundo.</summary>
    /// <summary>Num tema claro (Livro), a sombra é clara: um brilho de papel atrás da tinta, em vez de uma mancha escura.</summary>
    private static DropShadowEffect TextShadow => Theme.Current.IsLight ? LightShadow : DarkShadow;

    private static readonly DropShadowEffect DarkShadow = CreateShadow(Colors.Black, 0.9);
    private static readonly DropShadowEffect LightShadow = CreateShadow(Colors.White, 0.8);

    private static DropShadowEffect CreateShadow(System.Windows.Media.Color color, double opacity)
    {
        var shadow = new DropShadowEffect { Color = color, ShadowDepth = 2, BlurRadius = 6, Opacity = opacity, Direction = 300 };
        shadow.Freeze();
        return shadow;
    }

    private static FrameworkElement TextPiece(Piece piece)
    {
        var block = TextBlockOf(piece);
        if (piece.Scroll == true)
        {
            // Texto comprido: rola (roda do mouse ou a barra fina à direita) em vez de ser cortado.
            var scroll = Scroller(block);
            scroll.Padding = new Thickness(0, 0, 8, 0);
            return new Border { Child = scroll, Background = Brushes.Transparent };
        }
        return new Border { Child = block, ClipToBounds = piece.Shadow != true, Background = Brushes.Transparent };
    }

    /// <summary>As letras de um Texto, como o jogo desenha (o editor também usa, para medir se cabe).</summary>
    private static TextBlock TextBlockOf(Piece piece)
    {
        var block = Label(piece, piece.Text ?? "", piece.Color is { } c ? Theme.Brush(c, piece.Shade) : Theme.Text);
        block.FontWeight = piece.Bold == true ? FontWeights.SemiBold : FontWeights.Normal;
        block.FontStyle = piece.Italic == true ? FontStyles.Italic : FontStyles.Normal;
        block.TextAlignment = piece.Align switch
        {
            TextAlign.Center => TextAlignment.Center,
            TextAlign.Right => TextAlignment.Right,
            _ => TextAlignment.Left,
        };
        bool fantasy = Theme.FontFor(piece) == Font.Fantasy;
        block.LineHeight = Math.Round(block.FontSize * (fantasy ? 1.1 : 1.4));
        if (fantasy) block.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        if (piece.Shadow == true) block.Effect = TextShadow;
        return block;
    }

    /// <summary>Espaço dos lados do texto dentro do botão (o modelo do botão tem 18 de cada lado).</summary>
    private const double ButtonPadding = 36;

    /// <summary>
    /// O texto cabe na peça? Para um Texto, a altura que ele precisa; para um Botão, a largura (o texto do
    /// botão fica numa linha só e, sem espaço, termina em "…"). Null quando cabe, ou quando o Texto tem rolagem.
    /// </summary>
    public static TextFit? Overflow(Piece piece)
    {
        if (string.IsNullOrEmpty(piece.Text)) return null;
        switch (piece.Type)
        {
            case PieceType.Text when piece.Scroll != true:
            {
                var block = TextBlockOf(piece);
                block.Measure(new Size(Math.Max(1, piece.Width), double.PositiveInfinity));
                double needed = Math.Ceiling(block.DesiredSize.Height);
                // Folga de meia letra: o espaço entre as linhas pode passar um pouco da peça sem cortar nada.
                return needed > piece.Height + Math.Max(2, piece.FontSize * 0.5) ? new TextFit(piece.Width, needed) : null;
            }
            case PieceType.Button:
            {
                var label = ButtonLabel(piece, Theme.Text);
                label.TextWrapping = TextWrapping.NoWrap;
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double needed = Math.Ceiling(label.DesiredSize.Width + ButtonPadding + 2);
                return needed > piece.Width + 1 ? new TextFit(needed, piece.Height) : null;
            }
            default:
                return null;
        }
    }

    private static TextBlock ButtonLabel(Piece piece, Brush foreground)
    {
        var label = Label(piece, piece.Text ?? "", foreground);
        label.TextAlignment = TextAlignment.Center;
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        if (piece.Bold == true) label.FontWeight = FontWeights.Bold;
        return label;
    }

    private static FrameworkElement ButtonPiece(Piece piece, RenderContext context)
    {
        var look = Theme.LookOf(piece.Color, piece.Shade, piece.Style, piece.TextColor);
        var button = Theme.MakeButton(ButtonLabel(piece, look.Text), piece.Color, piece.Shade, piece.Style, piece.TextColor,
            Theme.ButtonCorners(piece.Corner, piece.Height));
        if (piece.Shadow == true) button.Effect = Theme.DropShadow;
        button.IsEnabled = piece.Enabled;
        if (context.Live && context.Click != null)
            button.Click += (_, _) => context.Click(piece);
        return button;
    }

    // ------------------------------------------------------------------ barras

    private static FrameworkElement BarPiece(Piece piece)
    {
        int max = piece.BarMax, value = piece.BarValue;
        double fraction = Math.Clamp((double)value / max, 0, 1);
        var style = piece.BarStyle ?? BarStyle.Smooth;
        var place = piece.BarText ?? BarText.Above;
        var color = Theme.Rgb(piece.Color ?? Color.Green, piece.Shade);
        var font = Theme.FontOf(piece.Font ?? Theme.Current.BodyFont);
        var size = Math.Min(piece.FontSize, 16) * Theme.FontScale(piece.Font ?? Theme.Current.BodyFont);

        // Em cima: o nome e o número numa linha, a barra embaixo. Dentro ou sem texto: a barra ocupa a peça toda.
        double trackHeight = place == BarText.Above ? Math.Clamp(piece.Height - 27, 6, 28) : Math.Max(4, piece.Height);
        var corners = piece.Corner == Corner.Square ? new CornerRadius(2) : new CornerRadius(trackHeight / 2);
        var track = style == BarStyle.Blocks ? BlocksTrack(piece, fraction, color, trackHeight) : FillTrack(style, fraction, color, trackHeight, corners);

        if (place == BarText.Above)
        {
            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 5), LastChildFill = true };
            var amount = new TextBlock { Text = $"{value} / {max}", FontFamily = font, FontSize = size - 1, Foreground = Theme.Muted };
            DockPanel.SetDock(amount, Dock.Right);
            header.Children.Add(amount);
            header.Children.Add(new TextBlock
            {
                Text = piece.Text ?? "",
                FontFamily = font,
                FontSize = size,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.Text,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 8, 0),
            });
            track.VerticalAlignment = VerticalAlignment.Top;
            var panel = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(header, Dock.Top);
            panel.Children.Add(header);
            panel.Children.Add(track);
            return panel;
        }
        if (place == BarText.None) return track;

        // Dentro: letra branca com sombra por cima da barra, como nos jogos de luta.
        var inside = new Grid();
        inside.Children.Add(track);
        var line = new DockPanel { Margin = new Thickness(Math.Max(8, corners.TopLeft * 0.6), 0, Math.Max(8, corners.TopLeft * 0.6), 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        var insideSize = Math.Clamp(trackHeight * 0.5, 10, Math.Max(10, piece.FontSize));
        var number = new TextBlock { Text = $"{value} / {max}", FontFamily = font, FontSize = insideSize, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Effect = DarkShadow };
        DockPanel.SetDock(number, Dock.Right);
        line.Children.Add(number);
        line.Children.Add(new TextBlock
        {
            Text = piece.Text ?? "",
            FontFamily = font,
            FontSize = insideSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            Effect = DarkShadow,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 8, 0),
        });
        inside.Children.Add(line);
        return inside;
    }

    /// <summary>A barra inteira: o fundo e a parte cheia por cima (lisa ou com brilho).</summary>
    private static FrameworkElement FillTrack(BarStyle style, double fraction, System.Windows.Media.Color color, double height, CornerRadius corners)
    {
        bool shine = style == BarStyle.Shine;
        var fill = new Border
        {
            Background = shine ? Theme.Vertical(Theme.Lighten(color, 0.35), Theme.Darken(color, 0.72)) : Theme.Freeze(color),
            CornerRadius = corners,
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = 0,
        };
        var content = new Grid();
        content.Children.Add(fill);
        if (shine)
        {
            // O brilho: uma faixa clarinha na metade de cima.
            var gloss = new Border
            {
                Background = Theme.Vertical(System.Windows.Media.Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF), System.Windows.Media.Color.FromArgb(0x08, 0xFF, 0xFF, 0xFF)),
                CornerRadius = corners,
                Height = Math.Max(2, height * 0.45),
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(2, 1, 0, 0),
                Width = 0,
            };
            content.Children.Add(gloss);
            fill.SizeChanged += (_, e) => gloss.Width = Math.Max(0, e.NewSize.Width - 4);
        }
        var track = new Border
        {
            // Lisa: o fundo do tema. Com brilho: a própria cor bem escura, com uma borda da cor.
            Background = shine ? Theme.Freeze(Theme.WithAlpha(Theme.Darken(color, 0.3), 0xE0)) : Theme.Track,
            BorderBrush = shine ? Theme.Freeze(Theme.WithAlpha(color, 0x90)) : null,
            BorderThickness = new Thickness(shine ? 1 : 0),
            CornerRadius = corners,
            Height = height,
            Child = content,
            ClipToBounds = false,
        };
        track.SizeChanged += (_, e) => fill.Width = Math.Max(0, (e.NewSize.Width - (shine ? 2 : 0)) * fraction);
        return track;
    }

    /// <summary>Em blocos: um pedaço por ponto (até 20; acima disso, 10 pedaços), o último pela metade se for o caso.</summary>
    private static FrameworkElement BlocksTrack(Piece piece, double fraction, System.Windows.Media.Color color, double height)
    {
        int count = piece.BarMax <= 20 ? piece.BarMax : 10;
        double filled = fraction * count;
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Columns = count, Height = height };
        double radius = piece.Corner == Corner.Square ? 1 : Math.Min(4, height / 2);
        var empty = Theme.Freeze(Theme.WithAlpha(Theme.Darken(color, 0.35), 0xB0));
        var full = Theme.Vertical(Theme.Lighten(color, 0.18), Theme.Darken(color, 0.85));
        for (int i = 0; i < count; i++)
        {
            double part = Math.Clamp(filled - i, 0, 1);
            var block = new Grid { Margin = new Thickness(i == 0 ? 0 : 1.5, 0, i == count - 1 ? 0 : 1.5, 0) };
            block.Children.Add(new Border { Background = empty, CornerRadius = new CornerRadius(radius) });
            if (part > 0)
            {
                var piecePart = new Border { Background = full, CornerRadius = new CornerRadius(radius), HorizontalAlignment = HorizontalAlignment.Left };
                block.SizeChanged += (_, e) => piecePart.Width = e.NewSize.Width * part;
                block.Children.Add(piecePart);
            }
            grid.Children.Add(block);
        }
        return grid;
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
            frame.Children.Add(new Rectangle
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
                FontFamily = Theme.BodyFamily,
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
        if (!piece.Enabled) host.Opacity = 0.4;
        if (context.Live && context.Click != null && piece.Enabled && context.IsClickable?.Invoke(piece) == true)
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
        // Sem cor: o cinza dos painéis. Com cor: a cor no tom escolhido.
        var rgb = piece.Color is { } color ? Theme.Rgb(color, piece.Shade) : Theme.Tone(Theme.PanelColor, piece.Shade);
        var fill = Theme.Freeze(Theme.WithAlpha(rgb, (byte)Math.Round(piece.BoxOpacity * 2.55)));
        Brush? stroke = null;
        // Sem escolher, a borda segue o tema (no Fantasia e no Livro, as caixas sem cor têm borda).
        if (piece.Border ?? (piece.Color != null || Theme.Current.PanelBorders))
        {
            // Borda bem visível: a própria cor numa caixa clarinha, um pouco mais clara numa caixa cheia.
            stroke = piece.Color == null
                ? Theme.PanelBorder
                : Theme.Freeze(piece.BoxOpacity <= 50 ? Theme.WithAlpha(rgb, 0xA0) : Theme.Lighten(rgb, 0.3));
        }

        if (piece.Corner == Corner.Circle)
        {
            var grid = new Grid();
            grid.Children.Add(new Ellipse { Fill = fill, Stroke = stroke, StrokeThickness = stroke != null ? 1.5 : 0, Effect = piece.Shadow == true ? Theme.DropShadow : null });
            return grid;
        }
        return new Border
        {
            Background = fill,
            BorderBrush = stroke,
            BorderThickness = new Thickness(stroke != null ? 1 : 0),
            CornerRadius = new CornerRadius(piece.Corner == Corner.Square ? 0 : Theme.PanelRadius),
            Effect = piece.Shadow == true ? Theme.DropShadow : null,
        };
    }

    private static FrameworkElement InputPiece(Piece piece, RenderContext context)
    {
        var question = Label(piece, piece.Text ?? "");
        question.Margin = new Thickness(0, 0, 0, 8);
        question.TextTrimming = TextTrimming.CharacterEllipsis;

        var box = new TextBox
        {
            FontFamily = Theme.FontOf(Theme.FontFor(piece)),
            FontSize = (piece.FontSize + 1) * Theme.FontScale(Theme.FontFor(piece)),
            Padding = new Thickness(8, 5, 8, 5),
            Background = Theme.Panel,
            Foreground = Theme.Text,
            CaretBrush = Theme.Text,
            BorderBrush = Theme.PanelBorder,
            VerticalContentAlignment = VerticalAlignment.Center,
            IsEnabled = piece.Enabled,
        };
        var okLabel = new TextBlock { Text = "OK", FontFamily = Theme.BodyFamily, FontSize = piece.FontSize, Foreground = Brushes.White };
        var ok = Theme.MakeButton(okLabel);
        ok.Margin = new Thickness(10, 0, 0, 0);
        ok.IsEnabled = piece.Enabled;

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
        if (!piece.Enabled) panel.Opacity = 0.5;
        panel.Tag = piece.Enabled ? box : null; // a janela do jogo põe o cursor aqui
        return panel;
    }

    private static FrameworkElement MessagesPiece(Piece piece, RenderContext context)
    {
        var list = new StackPanel();
        IReadOnlyList<MessageLine> lines = context.Live
            ? context.Messages
            :
            [
                new("As mensagens do game.Write aparecem aqui; as antigas ficam apagadas.", Color.Gray, false, IsOld: true),
                new("Exemplo: Você causou 7 de dano!", null, true),
            ];
        foreach (var line in lines)
            list.Children.Add(MessageView(line, piece.FontSize, Theme.FontFor(piece)));

        // As mais novas ficam embaixo e à vista; as antigas, rolando para cima.
        var scroll = Scroller(list);
        scroll.Margin = new Thickness(14, 10, 8, 10);
        if (context.Live)
        {
            scroll.Loaded += (_, _) => scroll.ScrollToEnd();
            list.SizeChanged += (_, _) => scroll.ScrollToEnd();
        }
        else
        {
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            scroll.Loaded += (_, _) => scroll.ScrollToEnd();
        }
        return new Border
        {
            Background = Theme.Panel,
            CornerRadius = new CornerRadius(Theme.PanelRadius),
            BorderBrush = Theme.PanelBorder,
            BorderThickness = new Thickness(Theme.Current.PanelBorders ? 1 : 0),
            Child = scroll,
        };
    }

    /// <summary>Uma mensagem: normal, destacada (do último clique) ou apagada (de cliques anteriores).</summary>
    public static FrameworkElement MessageView(MessageLine line, double fontSize, Font? font = null)
    {
        var block = new TextBlock
        {
            Text = line.Text,
            FontFamily = Theme.FontOf(font),
            FontSize = fontSize * Theme.FontScale(font),
            LineHeight = Math.Round(fontSize * 1.45),
            TextWrapping = TextWrapping.Wrap,
            Foreground = line.Color is { } c ? Theme.Brush(c) : Theme.Text,
        };
        if (!line.IsNews)
        {
            block.Margin = new Thickness(0, 0, 0, 6);
            if (line.IsOld) block.Opacity = 0.5;
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

    // ------------------------------------------------------------------ lista

    /// <summary>
    /// A Lista: um cartão para cada item, da esquerda para a direita e em fileiras, com rolagem se não couber.
    /// No editor: o cartão modelo marcado e a prévia dos outros cartões, apagadinha.
    /// </summary>
    private static FrameworkElement ListPiece(Piece piece, RenderContext context)
    {
        return context.Live ? LiveList(piece, context) : PreviewList(piece, context);
    }

    private static FrameworkElement LiveList(Piece piece, RenderContext context)
    {
        var cards = context.Cards?.Invoke(piece) ?? [];
        var root = new Grid { Background = Brushes.Transparent };
        if (cards.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(piece.Text))
            {
                root.Children.Add(new TextBlock
                {
                    Text = piece.Text,
                    FontFamily = Theme.BodyFamily,
                    FontSize = 16,
                    FontStyle = FontStyles.Italic,
                    Foreground = Theme.Muted,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12),
                });
            }
            return root;
        }

        var (columns, height) = ScreenLayout.CardGrid(piece, cards.Count);
        var canvas = new Canvas { Width = Math.Max(piece.Width, 1), Height = height, HorizontalAlignment = HorizontalAlignment.Left };
        for (int i = 0; i < cards.Count; i++)
        {
            var card = CardView(piece, cards[i].Items.Select(item => item.Piece), context, ghost: false);
            var at = ScreenLayout.CardPosition(piece, i, columns);
            Canvas.SetLeft(card, at.X);
            Canvas.SetTop(card, at.Y);
            canvas.Children.Add(card);
        }

        var scroll = Scroller(canvas);
        // A rolagem continua onde estava depois de cada clique (comprar o 6º item não volta a lista para o topo).
        if (context.ScrollOffsets is { } offsets)
        {
            scroll.Loaded += (_, _) =>
            {
                if (offsets.TryGetValue(piece.Name, out var offset)) scroll.ScrollToVerticalOffset(offset);
                scroll.ScrollChanged += (_, _) => offsets[piece.Name] = scroll.VerticalOffset;
            };
        }
        root.Children.Add(scroll);
        return root;
    }

    private static FrameworkElement PreviewList(Piece piece, RenderContext context)
    {
        var members = context.Members?.Invoke(piece) ?? [];
        var root = new Canvas { ClipToBounds = true, Background = Theme.Freeze(System.Windows.Media.Color.FromArgb(0x0C, 0xFF, 0xFF, 0xFF)) };
        root.Children.Add(new Rectangle
        {
            Width = piece.Width,
            Height = piece.Height,
            Stroke = Theme.Freeze(System.Windows.Media.Color.FromArgb(0x55, 0x8C, 0x93, 0xA0)),
            StrokeThickness = 1,
            StrokeDashArray = [2, 3],
            RadiusX = 6,
            RadiusY = 6,
        });

        // Quantos cartões cabem na área da Lista: o primeiro é o modelo, os outros são a prévia.
        var (columns, _) = ScreenLayout.CardGrid(piece, 1);
        int rows = Math.Max(1, (int)Math.Ceiling((piece.Height + piece.CardGap) / (piece.CardH + piece.CardGap)));
        int count = Math.Min(columns * rows, 24);
        for (int i = 1; i < count; i++)
        {
            var at = ScreenLayout.CardPosition(piece, i, columns);
            var ghost = CardView(piece, members, new RenderContext { Live = false }, ghost: true);
            Canvas.SetLeft(ghost, at.X);
            Canvas.SetTop(ghost, at.Y);
            root.Children.Add(ghost);
        }

        // O cartão modelo: as peças dele são desenhadas por cima (são peças da tela, editáveis).
        var model = new Rectangle
        {
            Width = piece.CardW,
            Height = piece.CardH,
            Stroke = Theme.Freeze(System.Windows.Media.Color.FromRgb(0x7C, 0x8C, 0xF8)),
            StrokeThickness = 1.5,
            StrokeDashArray = [4, 3],
            RadiusX = 8,
            RadiusY = 8,
        };
        root.Children.Add(model);
        if (members.Count == 0)
        {
            var hint = new TextBlock
            {
                Text = "Cartão modelo\nPonha aqui dentro as peças de cada item (nome, preço, botão…)",
                FontFamily = Theme.BodyFamily,
                FontSize = 13,
                Foreground = Theme.Muted,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Width = Math.Max(20, piece.CardW - 20),
            };
            Canvas.SetLeft(hint, 10);
            Canvas.SetTop(hint, Math.Max(6, piece.CardH / 2 - 30));
            root.Children.Add(hint);
        }
        return root;
    }

    /// <summary>Um cartão: as peças do cartão nas posições delas (contadas a partir do canto do cartão).</summary>
    private static FrameworkElement CardView(Piece list, IEnumerable<Piece> pieces, RenderContext context, bool ghost)
    {
        var card = new Canvas { Width = list.CardW, Height = list.CardH, ClipToBounds = true, Background = Brushes.Transparent };
        foreach (var piece in pieces)
        {
            if (!piece.Visible || piece.Type is PieceType.List) continue;
            var element = Create(piece, context);
            Canvas.SetLeft(element, piece.X);
            Canvas.SetTop(element, piece.Y);
            card.Children.Add(element);
        }
        if (ghost)
        {
            card.Opacity = 0.32;
            card.IsHitTestVisible = false;
        }
        return card;
    }

    // ------------------------------------------------------------------ rolagem

    /// <summary>Rolagem com uma barrinha fina por cima do conteúdo (a barra padrão do Windows destoa do jogo).</summary>
    private static ScrollViewer Scroller(UIElement content) => new()
    {
        Content = content,
        Template = ScrollTemplate,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Focusable = false,
        PanningMode = PanningMode.VerticalOnly,
    };

    private static readonly ControlTemplate ScrollTemplate = Sealed((ControlTemplate)XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ScrollViewer">
          <Grid Background="{TemplateBinding Background}">
            <ScrollContentPresenter x:Name="PART_ScrollContentPresenter" CanContentScroll="{TemplateBinding CanContentScroll}" />
            <ScrollBar x:Name="PART_VerticalScrollBar" HorizontalAlignment="Right" Width="4" MinWidth="0" Margin="0,2,-6,2" Cursor="Arrow"
                       Minimum="0" Maximum="{TemplateBinding ScrollableHeight}" ViewportSize="{TemplateBinding ViewportHeight}"
                       Value="{Binding VerticalOffset, Mode=OneWay, RelativeSource={RelativeSource TemplatedParent}}"
                       Visibility="{TemplateBinding ComputedVerticalScrollBarVisibility}">
              <ScrollBar.Template>
                <ControlTemplate TargetType="ScrollBar">
                  <Track x:Name="PART_Track" IsDirectionReversed="True">
                    <Track.Thumb>
                      <Thumb>
                        <Thumb.Template>
                          <ControlTemplate TargetType="Thumb">
                            <Border Background="#66FFFFFF" CornerRadius="2" />
                          </ControlTemplate>
                        </Thumb.Template>
                      </Thumb>
                    </Track.Thumb>
                  </Track>
                </ControlTemplate>
              </ScrollBar.Template>
            </ScrollBar>
          </Grid>
        </ControlTemplate>
        """));

    /// <summary>Selado, o modelo pode ser usado por qualquer janela (cada jogo, cada teste, tem a sua thread).</summary>
    private static ControlTemplate Sealed(ControlTemplate template)
    {
        template.Seal();
        return template;
    }
}
