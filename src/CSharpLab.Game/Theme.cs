using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfColor = System.Windows.Media.Color;

namespace CSharpLab.GameEngine;

/// <summary>
/// O visual do motor: cores, botões e imagens. A janela do jogo e a aba Tela do CSharp Lab usam
/// o mesmo código, então o que aparece no editor é exatamente o que aparece no jogo.
/// </summary>
internal static class Theme
{
    /// <summary>
    /// O tema em uso (GameStyle.json). Cada thread tem o seu: a janela do jogo, a aba Tela do editor e
    /// cada teste desenham com o tema que carregaram, sem atrapalhar uns aos outros.
    /// </summary>
    [ThreadStatic] private static Look? _current;

    public static Look Current
    {
        get => _current ?? Look.Classic;
        set => _current = value;
    }

    /// <summary>Usa o tema do GameStyle.json da primeira pasta que tiver um (Clássico se não tiver). Retorna o erro do arquivo, se houver.</summary>
    public static string? UseProject(IEnumerable<string> folders)
    {
        Current = GameStyle.Load(folders, out var error);
        return error;
    }

    public static Brush Background => Current.BackgroundBrush;
    public static WpfColor PanelColor => Current.Panel;
    public static Brush Panel => Solid(Current.Panel);
    public static Brush PanelBorder => Solid(Current.PanelBorder);
    public static Brush Track => Solid(Current.Track);
    public static Brush Text => Solid(Current.Text);
    public static Brush Muted => Solid(Current.Muted);
    /// <summary>Cantos das caixas, das Mensagens e dos painéis.</summary>
    public static double PanelRadius => Current.PanelRadius;

    public static readonly FontFamily DefaultFont = new("Segoe UI");

    /// <summary>A letra dos textos sem fonte escolhida (rótulos das barras, avisos, "OK"…), conforme o tema.</summary>
    public static FontFamily BodyFamily => FontOf(Current.BodyFont);

    /// <summary>
    /// A fonte de uma peça: a que ela escolheu, ou a do tema (títulos, textos e botões podem ter fontes diferentes).
    /// Um título é um Texto com letra 28 ou maior.
    /// </summary>
    public static Font FontFor(Piece piece)
    {
        if (piece.Font is { } chosen) return chosen;
        // Só símbolos e emoji (um retrato 🐺, estrelas ✦): ficam na letra normal, que desenha todos do tamanho certo.
        if (!(piece.Text ?? "").Any(char.IsLetterOrDigit)) return GameEngine.Font.Normal;
        return (piece.Type, piece.FontSize) switch
        {
            (PieceType.Text, >= 28) => Current.TitleFont,
            (PieceType.Button, _) => Current.ButtonFont,
            _ => Current.BodyFont,
        };
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<WpfColor, Brush> SolidBrushes = new();

    /// <summary>Pincel de uma cor só, guardado para reaproveitar.</summary>
    private static Brush Solid(WpfColor color) => SolidBrushes.GetOrAdd(color, Freeze);

    // As fontes do Font: todas vêm com o Windows. Símbolos e emoji que faltam nelas vêm da Segoe UI.
    private static readonly Dictionary<Font, FontFamily> Fonts = new()
    {
        [GameEngine.Font.Normal] = DefaultFont,
        [GameEngine.Font.Fantasy] = new("Gabriola, Segoe UI"),
        [GameEngine.Font.Book] = new("Palatino Linotype, Segoe UI"),
        [GameEngine.Font.Hand] = new("Segoe Print, Segoe UI"),
    };

    public static FontFamily FontOf(Font? font) => Fonts.GetValueOrDefault(font ?? GameEngine.Font.Normal, DefaultFont);

    /// <summary>A Gabriola desenha letras pequenas para o tamanho: ela ganha um pouco mais, para todas parecerem do mesmo tamanho.</summary>
    public static double FontScale(Font? font) => font == GameEngine.Font.Fantasy ? 1.3 : 1;

    /// <summary>As 8 cores do jogo no tema atual.</summary>
    private static IReadOnlyDictionary<Color, WpfColor> Colors => Current.Colors;

    /// <summary>Cor dos botões quando a tela não escolhe outra (depende do tema).</summary>
    private static WpfColor ButtonBlue => Current.ButtonDefault;

    public static WpfColor Rgb(Color color) => Colors[color];

    public static WpfColor Rgb(Color color, Shade? shade) => Tone(Colors[color], shade);

    public static Brush Brush(Color color) => Solid(Colors[color]);

    public static Brush Brush(Color color, Shade? shade) => Solid(Rgb(color, shade));

    /// <summary>Tom da cor: escuro ou claro (Normal deixa como está).</summary>
    public static WpfColor Tone(WpfColor c, Shade? shade) => shade switch
    {
        Shade.Dark => Darken(c, 0.55),
        Shade.Light => Lighten(c, 0.45),
        _ => c,
    };

    public static WpfColor WithAlpha(WpfColor c, byte alpha) => WpfColor.FromArgb(alpha, c.R, c.G, c.B);

    /// <summary>Cor clara o bastante para pedir letra escura em cima.</summary>
    public static bool IsLight(WpfColor c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255 > 0.66;

    /// <summary>Fundo da mensagem nova (game.Write depois de um clique): a cor dela bem clarinha (dourado se não tiver cor).</summary>
    public static Brush NewsBack(Color? color) => Solid(WithAlpha(Colors[color ?? Color.Gold], 0x2C));

    /// <summary>Fundo de botão: azul padrão, ou a cor escolhida um pouco mais escura (o texto branco continua legível).</summary>
    public static WpfColor ButtonColor(Color? color, Shade? shade = null) =>
        Tone(color is { } c and not Color.White ? Darken(Colors[c], 0.82) : ButtonBlue, shade);

    public static WpfColor Darken(WpfColor c, double factor) =>
        WpfColor.FromRgb((byte)Math.Clamp(c.R * factor, 0, 255), (byte)Math.Clamp(c.G * factor, 0, 255), (byte)Math.Clamp(c.B * factor, 0, 255));

    public static WpfColor Lighten(WpfColor c, double amount) =>
        WpfColor.FromRgb((byte)(c.R + (255 - c.R) * amount), (byte)(c.G + (255 - c.G) * amount), (byte)(c.B + (255 - c.B) * amount));

    public static Brush Freeze(WpfColor color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    // ------------------------------------------------------------------ botões

    /// <summary>As cores de um botão: fundo, com o mouse em cima, apertado, borda e letra.</summary>
    internal sealed record ButtonLook(Brush Back, Brush Hover, Brush Pressed, Brush Border, double BorderWidth, Brush Text);

    /// <summary>Cheio (fundo colorido), contorno (só a borda) ou só texto.</summary>
    public static ButtonLook LookOf(Color? color, Shade? shade = null, ButtonStyle? style = null)
    {
        if (style is null or ButtonStyle.Filled)
        {
            var back = ButtonColor(color, shade);
            return new ButtonLook(Freeze(back), Freeze(Lighten(back, 0.14)), Freeze(Darken(back, 0.86)),
                System.Windows.Media.Brushes.Transparent, 0,
                IsLight(back) ? Freeze(WpfColor.FromRgb(0x17, 0x19, 0x1F)) : System.Windows.Media.Brushes.White);
        }
        // Contorno e só texto: a cor fica na letra (e na borda), um pouco mais clara para ler bem no fundo escuro
        // (num tema claro, um pouco mais escura).
        var baseColor = color is { } c and not Color.White ? Colors[c] : ButtonBlue;
        var accent = Tone(Current.IsLight ? Darken(baseColor, 0.85) : Lighten(baseColor, color is { } and not Color.White ? 0.12 : 0.35), shade);
        var outline = style == ButtonStyle.Outline;
        return new ButtonLook(System.Windows.Media.Brushes.Transparent,
            Freeze(WithAlpha(accent, (byte)(outline ? 0x24 : 0x1C))),
            Freeze(WithAlpha(accent, 0x3C)),
            outline ? Freeze(accent) : System.Windows.Media.Brushes.Transparent,
            outline ? 1.5 : 0,
            Freeze(accent));
    }

    /// <summary>Botão do jogo: cantos arredondados, cor própria, mais claro com o mouse em cima e mais escuro ao apertar.</summary>
    public static Button MakeButton(object content, Color? color = null, Shade? shade = null, ButtonStyle? style = null)
    {
        var look = LookOf(color, shade, style);
        var button = new Button
        {
            Content = content,
            Template = ButtonTemplate,
            Cursor = Cursors.Hand,
            Focusable = false,
            Foreground = look.Text,
            Background = look.Back,
            BorderBrush = look.Border,
            BorderThickness = new Thickness(look.BorderWidth),
        };
        button.SetValue(HoverProperty, look.Hover);
        button.SetValue(PressedProperty, look.Pressed);
        button.SetValue(RadiusProperty, new CornerRadius(Current.ButtonRadius));
        return button;
    }

    /// <summary>Cantos do botão (cada tema tem os seus).</summary>
    private static readonly DependencyProperty RadiusProperty =
        DependencyProperty.RegisterAttached("Radius", typeof(CornerRadius), typeof(Theme), new PropertyMetadata(new CornerRadius(8)));

    private static readonly DependencyProperty HoverProperty =
        DependencyProperty.RegisterAttached("Hover", typeof(Brush), typeof(Theme), new PropertyMetadata(null));

    private static readonly DependencyProperty PressedProperty =
        DependencyProperty.RegisterAttached("Pressed", typeof(Brush), typeof(Theme), new PropertyMetadata(null));

    private static readonly ControlTemplate ButtonTemplate = CreateButtonTemplate();

    private static ControlTemplate CreateButtonTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border), "Back");
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        border.SetValue(Border.CornerRadiusProperty, new Binding { Path = new PropertyPath(RadiusProperty), RelativeSource = RelativeSource.TemplatedParent });
        border.SetValue(Border.PaddingProperty, new Thickness(18, 8, 18, 8));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        border.AppendChild(presenter);

        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new Binding { Path = new PropertyPath(HoverProperty), RelativeSource = RelativeSource.TemplatedParent }, "Back"));
        var pressed = new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Border.BackgroundProperty, new Binding { Path = new PropertyPath(PressedProperty), RelativeSource = RelativeSource.TemplatedParent }, "Back"));
        // Desligado (Enabled = false): apagado e sem clique.
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.38, "Back"));
        template.Triggers.Add(hover);
        template.Triggers.Add(pressed);
        template.Triggers.Add(disabled);
        template.Seal();
        return template;
    }

    // ------------------------------------------------------------------ imagens

    /// <summary>Pastas onde as imagens são procuradas. O editor visual troca pela pasta do projeto.</summary>
    internal static IReadOnlyList<string> ImageRoots { get; set; } = [AppContext.BaseDirectory, Environment.CurrentDirectory];

    /// <summary>Procura na pasta Assets e na pasta do jogo (onde o .exe está e onde ele foi aberto).</summary>
    public static string? FindImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (Path.IsPathRooted(path)) return File.Exists(path) ? path : null;
        foreach (var root in ImageRoots)
        {
            foreach (var candidate in new[] { Path.Combine(root, "Assets", path), Path.Combine(root, path) })
            {
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
        }
        return null;
    }

    private static readonly Dictionary<string, (DateTime Stamp, BitmapSource Image)> ImageCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Imagem pronta para desenhar (guardada na memória enquanto o arquivo não mudar). Null se não existe ou não abre.</summary>
    public static BitmapSource? LoadImage(string? path)
    {
        var file = FindImage(path);
        if (file == null) return null;
        try
        {
            var stamp = File.GetLastWriteTimeUtc(file);
            if (ImageCache.TryGetValue(file, out var cached) && cached.Stamp == stamp) return cached.Image;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.UriSource = new Uri(file);
            bitmap.EndInit();
            bitmap.Freeze();
            ImageCache[file] = (stamp, bitmap);
            return bitmap;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
