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
            PieceType.List => ListPiece(piece, context),
            _ => new Border(),
        };
        element.Width = Math.Max(1, piece.Width);
        element.Height = Math.Max(1, piece.Height);
        if (!context.Live) element.IsHitTestVisible = false;
        context.Created?.Invoke(piece, element);
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
        FontFamily = Theme.FontOf(piece.Font),
        FontSize = piece.FontSize * Theme.FontScale(piece.Font),
        TextWrapping = TextWrapping.Wrap,
        Foreground = foreground ?? Theme.Text,
    };

    /// <summary>Sombra escura atrás das letras: o texto fica legível em cima de qualquer fundo.</summary>
    private static readonly DropShadowEffect TextShadow = CreateShadow();

    private static DropShadowEffect CreateShadow()
    {
        var shadow = new DropShadowEffect { Color = Colors.Black, ShadowDepth = 2, BlurRadius = 6, Opacity = 0.9, Direction = 300 };
        shadow.Freeze();
        return shadow;
    }

    private static FrameworkElement TextPiece(Piece piece)
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
        block.LineHeight = Math.Round(block.FontSize * (piece.Font == Font.Fantasy ? 1.1 : 1.4));
        if (piece.Font == Font.Fantasy) block.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        if (piece.Shadow == true) block.Effect = TextShadow;
        return new Border { Child = block, ClipToBounds = piece.Shadow != true, Background = Brushes.Transparent };
    }

    private static FrameworkElement ButtonPiece(Piece piece, RenderContext context)
    {
        var look = Theme.LookOf(piece.Color, piece.Shade, piece.Style);
        var label = Label(piece, piece.Text ?? "", look.Text);
        label.TextAlignment = TextAlignment.Center;
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        var button = Theme.MakeButton(label, piece.Color, piece.Shade, piece.Style);
        button.IsEnabled = piece.Enabled;
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
        var amount = new TextBlock { Text = $"{value} / {max}", FontFamily = Theme.DefaultFont, FontSize = size - 1, Foreground = Theme.Muted };
        DockPanel.SetDock(amount, Dock.Right);
        header.Children.Add(amount);
        header.Children.Add(new TextBlock
        {
            Text = piece.Text ?? "",
            FontFamily = Theme.DefaultFont,
            FontSize = size,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.Text,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 8, 0),
        });

        double trackHeight = Math.Clamp(piece.Height - 27, 6, 28);
        var fill = new Border
        {
            Background = Theme.Brush(piece.Color ?? Color.Green, piece.Shade),
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
                FontFamily = Theme.DefaultFont,
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
        if (piece.HasBorder)
        {
            // Borda bem visível: a própria cor numa caixa clarinha, um pouco mais clara numa caixa cheia.
            stroke = piece.Color == null
                ? Theme.Freeze(Theme.Lighten(rgb, 0.12))
                : Theme.Freeze(piece.BoxOpacity <= 50 ? Theme.WithAlpha(rgb, 0xA0) : Theme.Lighten(rgb, 0.3));
        }

        if (piece.Corner == Corner.Circle)
        {
            var grid = new Grid();
            grid.Children.Add(new Ellipse { Fill = fill, Stroke = stroke, StrokeThickness = stroke != null ? 1.5 : 0 });
            return grid;
        }
        return new Border
        {
            Background = fill,
            BorderBrush = stroke,
            BorderThickness = new Thickness(stroke != null ? 1 : 0),
            CornerRadius = new CornerRadius(piece.Corner == Corner.Square ? 0 : 10),
        };
    }

    private static FrameworkElement InputPiece(Piece piece, RenderContext context)
    {
        var question = Label(piece, piece.Text ?? "");
        question.Margin = new Thickness(0, 0, 0, 8);
        question.TextTrimming = TextTrimming.CharacterEllipsis;

        var box = new TextBox
        {
            FontFamily = Theme.FontOf(piece.Font),
            FontSize = (piece.FontSize + 1) * Theme.FontScale(piece.Font),
            Padding = new Thickness(8, 5, 8, 5),
            Background = Theme.Panel,
            Foreground = Theme.Text,
            CaretBrush = Theme.Text,
            BorderBrush = Theme.PanelBorder,
            VerticalContentAlignment = VerticalAlignment.Center,
            IsEnabled = piece.Enabled,
        };
        var okLabel = new TextBlock { Text = "OK", FontFamily = Theme.DefaultFont, FontSize = piece.FontSize, Foreground = Brushes.White };
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
            list.Children.Add(MessageView(line, piece.FontSize, piece.Font));

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
        return new Border { Background = Theme.Panel, CornerRadius = new CornerRadius(10), Child = scroll };
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
                    FontFamily = Theme.DefaultFont,
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
                FontFamily = Theme.DefaultFont,
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
