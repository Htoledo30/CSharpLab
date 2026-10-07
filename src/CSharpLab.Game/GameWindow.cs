using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfImage = System.Windows.Controls.Image;

namespace CSharpLab.GameEngine;

/// <summary>
/// A janela do jogo: área fixa de 960×540 que cresce e diminui com a janela (sem distorcer).
/// Título em cima, textos no meio, barras à direita e botões embaixo. Teclas 1 a 9 apertam os botões.
/// </summary>
internal sealed class GameWindow : Window, IGameView
{
    public const double StageWidth = 960, StageHeight = 540;

    private static readonly Brush Background1 = Freeze(new SolidColorBrush(Color.FromRgb(0x17, 0x19, 0x1F)));
    private static readonly Brush PanelBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x21, 0x24, 0x2C)));
    private static readonly Brush Track = Freeze(new SolidColorBrush(Color.FromRgb(0x2E, 0x32, 0x3C)));
    private static readonly Brush TextBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xDD, 0xE1, 0xE8)));
    private static readonly Brush Muted = Freeze(new SolidColorBrush(Color.FromRgb(0x8C, 0x93, 0xA0)));
    private static readonly Brush Accent = Freeze(new SolidColorBrush(Color.FromRgb(0x3D, 0x7B, 0xE8)));
    private static readonly Brush AccentHover = Freeze(new SolidColorBrush(Color.FromRgb(0x55, 0x8D, 0xF0)));
    private static readonly Brush AccentPressed = Freeze(new SolidColorBrush(Color.FromRgb(0x2F, 0x66, 0xC9)));
    private static readonly Brush NewsBack = Freeze(new SolidColorBrush(Color.FromArgb(0x30, 0xF2, 0xC1, 0x4E)));

    private readonly Game _game;
    private readonly TextBlock _title = new() { FontSize = 30, FontWeight = FontWeights.SemiBold, Foreground = TextBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
    private readonly StackPanel _items = new();
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly StackPanel _bars = new();
    private readonly Border _barsPanel;
    private readonly WrapPanel _buttons = new() { Margin = new Thickness(0, 16, 0, 0) };
    private readonly List<ButtonItem> _buttonItems = [];
    private TextBox? _answerBox;

    public GameWindow(Game game)
    {
        _game = game;
        Title = game.WindowTitle;
        Background = Background1;
        FontFamily = new FontFamily("Segoe UI");
        UseLayoutRounding = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        MinWidth = 480;
        MinHeight = 300;

        _scroll.Content = _items;
        _barsPanel = new Border
        {
            Background = PanelBrush,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 14, 16, 6),
            Margin = new Thickness(20, 0, 0, 0),
            Width = 250,
            VerticalAlignment = VerticalAlignment.Top,
            Child = _bars,
        };

        var middle = new Grid();
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        middle.Children.Add(_scroll);
        Grid.SetColumn(_barsPanel, 1);
        middle.Children.Add(_barsPanel);

        var stage = new Grid();
        stage.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        stage.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        stage.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var padded = new Border { Padding = new Thickness(36, 28, 36, 28), Child = stage, Width = StageWidth, Height = StageHeight };
        stage.Children.Add(_title);
        Grid.SetRow(middle, 1);
        stage.Children.Add(middle);
        Grid.SetRow(_buttons, 2);
        stage.Children.Add(_buttons);

        Content = new Viewbox { Stretch = Stretch.Uniform, Child = padded };

        // Abre com 960×540 e depois deixa o jogador redimensionar à vontade.
        ContentRendered += (_, _) => SizeToContent = SizeToContent.Manual;
        PreviewKeyDown += OnKey;
    }

    public void Show(Screen screen)
    {
        _title.Text = screen.Title ?? "";
        _title.Visibility = string.IsNullOrEmpty(screen.Title) ? Visibility.Collapsed : Visibility.Visible;

        _items.Children.Clear();
        _answerBox = null;
        bool hasNews = false;
        foreach (var item in screen.Items)
        {
            switch (item)
            {
                case TextItem text:
                    hasNews |= text.IsNews;
                    _items.Children.Add(TextView(text));
                    break;
                case ImageItem image:
                    _items.Children.Add(ImageView(image.Path));
                    break;
                case AskItem ask:
                    _items.Children.Add(AskView(ask));
                    break;
            }
        }

        _bars.Children.Clear();
        foreach (var bar in screen.Bars)
            _bars.Children.Add(BarView(bar));
        _barsPanel.Visibility = screen.Bars.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        _buttons.Children.Clear();
        _buttonItems.Clear();
        for (int i = 0; i < screen.Buttons.Count; i++)
        {
            var item = screen.Buttons[i];
            _buttonItems.Add(item);
            _buttons.Children.Add(ButtonView(item, i < 9 ? i + 1 : null));
        }

        if (hasNews) Dispatcher.BeginInvoke(_scroll.ScrollToEnd, System.Windows.Threading.DispatcherPriority.Loaded);
        else _scroll.ScrollToHome();

        if (_answerBox != null)
            Dispatcher.BeginInvoke(() => _answerBox?.Focus(), System.Windows.Threading.DispatcherPriority.Input);
        else
            Dispatcher.BeginInvoke(() => Keyboard.Focus(this), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox) return;
        int number = e.Key switch
        {
            >= Key.D1 and <= Key.D9 => e.Key - Key.D1,
            >= Key.NumPad1 and <= Key.NumPad9 => e.Key - Key.NumPad1,
            _ => -1,
        };
        if (number < 0 || number >= _buttonItems.Count) return;
        e.Handled = true;
        _game.Act(_buttonItems[number].OnClick);
    }

    // ------------------------------------------------------------------ peças da tela

    private static FrameworkElement TextView(TextItem item)
    {
        var block = new TextBlock
        {
            Text = item.Text,
            FontSize = 19,
            LineHeight = 28,
            TextWrapping = TextWrapping.Wrap,
            Foreground = item.Color is { } c ? ColorBrush(c) : TextBrush,
        };
        if (!item.IsNews)
        {
            block.Margin = new Thickness(0, 0, 0, 10);
            return block;
        }
        return new Border
        {
            Background = NewsBack,
            BorderBrush = ColorBrush(item.Color ?? GameColor.Gold),
            BorderThickness = new Thickness(3, 0, 0, 0),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 4, 0, 6),
            Child = block,
        };
    }

    private static FrameworkElement ImageView(string path)
    {
        var file = FindImage(path);
        if (file == null)
        {
            return new TextBlock
            {
                Text = $"[imagem não encontrada: {path} — coloque o arquivo na pasta Assets do projeto]",
                FontSize = 15, FontStyle = FontStyles.Italic, Foreground = Muted,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
            };
        }
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(file);
            bitmap.EndInit();
            bitmap.Freeze();
            return new WpfImage { Source = bitmap, MaxHeight = 240, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12) };
        }
        catch (Exception)
        {
            return new TextBlock { Text = $"[não deu para abrir a imagem: {path}]", FontSize = 15, Foreground = Muted, Margin = new Thickness(0, 0, 0, 10) };
        }
    }

    /// <summary>Procura na pasta Assets e na pasta do jogo (onde o .exe está e onde ele foi aberto).</summary>
    internal static string? FindImage(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (Path.IsPathRooted(path)) return File.Exists(path) ? path : null;
        foreach (var root in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            foreach (var candidate in new[] { Path.Combine(root, "Assets", path), Path.Combine(root, path) })
            {
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
        }
        return null;
    }

    private FrameworkElement AskView(AskItem item)
    {
        var question = new TextBlock { Text = item.Question, FontSize = 19, TextWrapping = TextWrapping.Wrap, Foreground = TextBrush, Margin = new Thickness(0, 4, 0, 8) };
        var box = new TextBox
        {
            FontSize = 18,
            Padding = new Thickness(8, 6, 8, 6),
            Background = PanelBrush,
            Foreground = TextBrush,
            CaretBrush = TextBrush,
            BorderBrush = Track,
            MinWidth = 320,
        };
        _answerBox ??= box;
        void Submit()
        {
            var answer = box.Text.Trim();
            if (answer.Length == 0)
            {
                box.Focus();
                return;
            }
            _game.Act(() => item.OnAnswer(answer));
        }
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            Submit();
        };
        var ok = MakeButton("OK", null);
        ok.Margin = new Thickness(10, 0, 0, 0);
        ok.Click += (_, _) => Submit();

        var row = new DockPanel { LastChildFill = true, HorizontalAlignment = HorizontalAlignment.Left };
        DockPanel.SetDock(ok, Dock.Right);
        row.Children.Add(ok);
        row.Children.Add(box);

        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        panel.Children.Add(question);
        panel.Children.Add(row);
        return panel;
    }

    private static FrameworkElement BarView(BarItem bar)
    {
        double fraction = Math.Clamp((double)bar.Value / bar.Max, 0, 1);
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 5) };
        var amount = new TextBlock { Text = $"{bar.Value} / {bar.Max}", FontSize = 15, Foreground = Muted };
        DockPanel.SetDock(amount, Dock.Right);
        header.Children.Add(amount);
        header.Children.Add(new TextBlock { Text = bar.Label, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = TextBrush, TextTrimming = TextTrimming.CharacterEllipsis });

        var fill = new Border { Background = ColorBrush(bar.Color), CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Left };
        var track = new Border { Background = Track, CornerRadius = new CornerRadius(6), Height = 12, Child = fill };
        track.SizeChanged += (_, e) => fill.Width = e.NewSize.Width * fraction;

        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        panel.Children.Add(header);
        panel.Children.Add(track);
        return panel;
    }

    private Button ButtonView(ButtonItem item, int? number)
    {
        var button = MakeButton(item.Text, number);
        button.Margin = new Thickness(0, 0, 10, 10);
        button.Click += (_, _) => _game.Act(item.OnClick);
        return button;
    }

    private static Button MakeButton(string text, int? number)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        if (number != null)
        {
            content.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 0, 6, 0),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = number.ToString(), FontSize = 13, Foreground = Brushes.White },
            });
        }
        content.Children.Add(new TextBlock { Text = text, FontSize = 17, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
        return new Button { Content = content, Template = ButtonTemplate, Cursor = Cursors.Hand, Focusable = false };
    }

    private static readonly ControlTemplate ButtonTemplate = CreateButtonTemplate();

    private static ControlTemplate CreateButtonTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border), "Back");
        border.SetValue(Border.BackgroundProperty, Accent);
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetValue(Border.PaddingProperty, new Thickness(18, 10, 18, 10));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);

        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, AccentHover, "Back"));
        var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Border.BackgroundProperty, AccentPressed, "Back"));
        template.Triggers.Add(hover);
        template.Triggers.Add(pressed);
        template.Seal();
        return template;
    }

    internal static Brush ColorBrush(GameColor color) => color switch
    {
        GameColor.Gray => Muted,
        GameColor.Red => Palette[0],
        GameColor.Green => Palette[1],
        GameColor.Blue => Palette[2],
        GameColor.Gold => Palette[3],
        GameColor.Purple => Palette[4],
        GameColor.Orange => Palette[5],
        _ => TextBrush,
    };

    private static readonly Brush[] Palette =
    [
        Freeze(new SolidColorBrush(Color.FromRgb(0xF2, 0x6B, 0x6B))),
        Freeze(new SolidColorBrush(Color.FromRgb(0x5C, 0xCB, 0x7A))),
        Freeze(new SolidColorBrush(Color.FromRgb(0x5E, 0xA8, 0xFF))),
        Freeze(new SolidColorBrush(Color.FromRgb(0xF2, 0xC1, 0x4E))),
        Freeze(new SolidColorBrush(Color.FromRgb(0xB4, 0x8C, 0xFF))),
        Freeze(new SolidColorBrush(Color.FromRgb(0xF2, 0x9A, 0x4E))),
    ];

    private static Brush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}
