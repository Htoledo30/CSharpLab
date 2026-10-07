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
    public static readonly Brush Panel = Freeze(WpfColor.FromRgb(0x21, 0x24, 0x2C));
    public static readonly Brush PanelBorder = Freeze(WpfColor.FromRgb(0x2E, 0x32, 0x3C));
    public static readonly Brush Track = Freeze(WpfColor.FromRgb(0x2E, 0x32, 0x3C));
    public static readonly Brush Text = Freeze(WpfColor.FromRgb(0xDD, 0xE1, 0xE8));
    public static readonly Brush Muted = Freeze(WpfColor.FromRgb(0x8C, 0x93, 0xA0));
    public static readonly FontFamily Font = new("Segoe UI");

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

    public static Brush Brush(Color color) => Brushes[color];

    private static readonly Dictionary<Color, Brush> NewsBacks = Colors.ToDictionary(
        p => p.Key, p => Freeze(WpfColor.FromArgb(0x2C, p.Value.R, p.Value.G, p.Value.B)));

    /// <summary>Fundo da mensagem nova (game.Write depois de um clique): a cor dela bem clarinha (dourado se não tiver cor).</summary>
    public static Brush NewsBack(Color? color) => NewsBacks[color ?? Color.Gold];

    /// <summary>Fundo de botão: azul padrão, ou a cor escolhida um pouco mais escura (o texto branco continua legível).</summary>
    public static WpfColor ButtonColor(Color? color) => color is { } c and not Color.White ? Shade(Colors[c], 0.82) : ButtonBlue;

    public static WpfColor Shade(WpfColor c, double factor) =>
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

    /// <summary>Botão do jogo: cantos arredondados, cor própria, mais claro com o mouse em cima e mais escuro ao apertar.</summary>
    public static Button MakeButton(object content, Color? color = null)
    {
        var baseColor = ButtonColor(color);
        var button = new Button
        {
            Content = content,
            Template = ButtonTemplate,
            Cursor = Cursors.Hand,
            Focusable = false,
            Foreground = System.Windows.Media.Brushes.White,
            Background = Freeze(baseColor),
            BorderBrush = Freeze(Lighten(baseColor, 0.14)),   // com o mouse em cima
            Tag = Freeze(Shade(baseColor, 0.86)),              // apertado
        };
        return button;
    }

    private static readonly ControlTemplate ButtonTemplate = CreateButtonTemplate();

    private static ControlTemplate CreateButtonTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border), "Back");
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetValue(Border.PaddingProperty, new Thickness(18, 8, 18, 8));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        border.AppendChild(presenter);

        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new Binding("BorderBrush") { RelativeSource = RelativeSource.TemplatedParent }, "Back"));
        var pressed = new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Border.BackgroundProperty, new Binding("Tag") { RelativeSource = RelativeSource.TemplatedParent }, "Back"));
        template.Triggers.Add(hover);
        template.Triggers.Add(pressed);
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
