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
    public static readonly Brush Background = Freeze(WpfColor.FromRgb(0x17, 0x19, 0x1F));
    public static readonly WpfColor PanelColor = WpfColor.FromRgb(0x21, 0x24, 0x2C);
    public static readonly Brush Panel = Freeze(PanelColor);
    public static readonly Brush PanelBorder = Freeze(WpfColor.FromRgb(0x2E, 0x32, 0x3C));
    public static readonly Brush Track = Freeze(WpfColor.FromRgb(0x2E, 0x32, 0x3C));
    public static readonly Brush Text = Freeze(WpfColor.FromRgb(0xDD, 0xE1, 0xE8));
    public static readonly Brush Muted = Freeze(WpfColor.FromRgb(0x8C, 0x93, 0xA0));
    public static readonly FontFamily DefaultFont = new("Segoe UI");

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

    private static readonly Dictionary<Color, WpfColor> Colors = new()
    {
        [Color.White] = WpfColor.FromRgb(0xDD, 0xE1, 0xE8),
        [Color.Gray] = WpfColor.FromRgb(0x8C, 0x93, 0xA0),
        [Color.Red] = WpfColor.FromRgb(0xF2, 0x6B, 0x6B),
        [Color.Green] = WpfColor.FromRgb(0x5C, 0xCB, 0x7A),
        [Color.Blue] = WpfColor.FromRgb(0x5E, 0xA8, 0xFF),
        [Color.Gold] = WpfColor.FromRgb(0xF2, 0xC1, 0x4E),
        [Color.Purple] = WpfColor.FromRgb(0xB4, 0x8C, 0xFF),
        [Color.Orange] = WpfColor.FromRgb(0xF2, 0x9A, 0x4E),
    };

    /// <summary>Cor dos botões quando a tela não escolhe outra.</summary>
    private static readonly WpfColor ButtonBlue = WpfColor.FromRgb(0x3D, 0x7B, 0xE8);

    private static readonly Dictionary<Color, Brush> Brushes = Colors.ToDictionary(p => p.Key, p => Freeze(p.Value));

    public static WpfColor Rgb(Color color) => Colors[color];

    public static WpfColor Rgb(Color color, Shade? shade) => Tone(Colors[color], shade);

    public static Brush Brush(Color color) => Brushes[color];

    public static Brush Brush(Color color, Shade? shade) => shade is null or Shade.Normal ? Brushes[color] : Freeze(Rgb(color, shade));

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

    private static readonly Dictionary<Color, Brush> NewsBacks = Colors.ToDictionary(
        p => p.Key, p => Freeze(WpfColor.FromArgb(0x2C, p.Value.R, p.Value.G, p.Value.B)));

    /// <summary>Fundo da mensagem nova (game.Write depois de um clique): a cor dela bem clarinha (dourado se não tiver cor).</summary>
    public static Brush NewsBack(Color? color) => NewsBacks[color ?? Color.Gold];

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
        // Contorno e só texto: a cor fica na letra (e na borda), um pouco mais clara para ler bem no fundo escuro.
        var accent = color is { } c and not Color.White
            ? Tone(Lighten(Colors[c], 0.12), shade)
            : Tone(Lighten(ButtonBlue, 0.35), shade);
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
        return button;
    }

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
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
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
