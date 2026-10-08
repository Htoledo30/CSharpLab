using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using WpfImage = System.Windows.Controls.Image;

namespace CSharpLab.GameEngine;

/// <summary>
/// A janela do jogo: palco fixo de 960×540 que cresce e diminui com a janela (sem distorcer).
/// Cena automática: título em cima, textos no meio, barras à direita e botões embaixo (teclas 1 a 9 apertam os botões).
/// Cena desenhada: as peças da aba Tela, cada uma no seu lugar.
/// Ao trocar de cena, a tela escurece e clareia sozinha.
/// </summary>
internal sealed class GameWindow : Window, IGameView
{
    public const double StageWidth = ScreenLayout.Width, StageHeight = ScreenLayout.Height;

    private readonly Game _game;

    // Cena automática
    private readonly Border _auto;
    private readonly TextBlock _title = new() { FontSize = 30, FontWeight = FontWeights.SemiBold, Foreground = Theme.Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
    private readonly StackPanel _items = new();
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly StackPanel _bars = new();
    private readonly Border _barsPanel;
    private readonly WrapPanel _buttons = new() { Margin = new Thickness(0, 16, 0, 0) };
    private readonly List<ButtonItem> _buttonItems = [];

    // Cena desenhada
    private readonly Canvas _designed = new() { Width = StageWidth, Height = StageHeight, ClipToBounds = true };

    private TextBox? _answerBox;

    // Troca de cena: a tela antiga (uma foto dela) escurece, e a nova aparece clareando.
    private readonly Grid _root;
    private readonly WpfImage _snapshot = new() { Width = StageWidth, Height = StageHeight, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    private readonly Rectangle _curtain = new() { Fill = Brushes.Black, Opacity = 0, IsHitTestVisible = false };
    private string? _shownScene;
    private bool _transition;

    /// <summary>Rolagem de cada Lista da cena atual (volta para o topo só ao entrar de novo na cena).</summary>
    private readonly Dictionary<string, double> _scrollOffsets = new(StringComparer.OrdinalIgnoreCase);

    public GameWindow(Game game)
    {
        _game = game;
        Title = game.WindowTitle;
        Background = Theme.Background;
        FontFamily = Theme.BodyFamily;
        UseLayoutRounding = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        MinWidth = 480;
        MinHeight = 300;

        _scroll.Content = _items;
        _barsPanel = new Border
        {
            Background = Theme.Panel,
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
        stage.Children.Add(_title);
        Grid.SetRow(middle, 1);
        stage.Children.Add(middle);
        Grid.SetRow(_buttons, 2);
        stage.Children.Add(_buttons);
        _auto = new Border { Padding = new Thickness(36, 28, 36, 28), Child = stage };

        var root = _root = new Grid { Width = StageWidth, Height = StageHeight, Background = Theme.Background };
        root.Children.Add(_auto);
        root.Children.Add(_designed);
        root.Children.Add(_snapshot);
        root.Children.Add(_curtain);
        Content = new Viewbox { Stretch = Stretch.Uniform, Child = root };

        // Abre com 960×540 e depois deixa o jogador redimensionar à vontade.
        ContentRendered += (_, _) => SizeToContent = SizeToContent.Manual;
        PreviewKeyDown += OnKey;
    }

    public void Show(Screen screen)
    {
        _answerBox = null;
        bool newScene = screen.SceneName != null && !string.Equals(screen.SceneName, _shownScene, StringComparison.OrdinalIgnoreCase);
        if (newScene)
        {
            _scrollOffsets.Clear();
            if (_shownScene != null && IsLoaded) BeginTransition();
        }
        _shownScene = screen.SceneName ?? _shownScene;

        if (screen.Designed != null)
        {
            _auto.Visibility = Visibility.Collapsed;
            _designed.Visibility = Visibility.Visible;
            _buttonItems.Clear();
            ShowDesigned(screen);
        }
        else
        {
            _designed.Visibility = Visibility.Collapsed;
            _designed.Children.Clear();
            _auto.Visibility = Visibility.Visible;
            ShowAutomatic(screen);
        }

        if (_answerBox != null)
            Dispatcher.BeginInvoke(() => _answerBox?.Focus(), DispatcherPriority.Input);
        else
            Dispatcher.BeginInvoke(() => Keyboard.Focus(this), DispatcherPriority.Input);
    }

    /// <summary>
    /// game.Wait: a tela fica como está por um tempinho (sem cliques do jogador) e depois o clique continua.
    /// Os desenhos e animações continuam rodando durante a pausa.
    /// </summary>
    public void Pause(double seconds)
    {
        if (seconds <= 0 || !IsLoaded) return;
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = TimeSpan.FromSeconds(seconds) };
        void Stop(object? sender, EventArgs e)
        {
            timer.Stop();
            frame.Continue = false;
        }
        timer.Tick += Stop;
        Closed += Stop;
        timer.Start();
        try
        {
            Dispatcher.PushFrame(frame);
        }
        finally
        {
            Closed -= Stop;
            timer.Stop();
        }
    }

    // ------------------------------------------------------------------ troca de cena

    private const double FadeOut = 130, FadeIn = 210;

    /// <summary>A foto da tela antiga fica por cima e escurece; depois a cortina sai e a tela nova aparece.</summary>
    private void BeginTransition()
    {
        EndTransition();
        RenderTargetBitmap shot;
        try
        {
            shot = new RenderTargetBitmap((int)StageWidth, (int)StageHeight, 96, 96, PixelFormats.Pbgra32);
            shot.Render(_root);
            shot.Freeze();
        }
        catch (Exception)
        {
            return; // sem foto, a troca acontece sem o escurecer
        }
        _snapshot.Source = shot;
        _snapshot.Visibility = Visibility.Visible;
        _transition = true;
        _curtain.IsHitTestVisible = true;   // nada de cliques no meio da troca

        var dark = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(FadeOut));
        dark.Completed += (_, _) =>
        {
            if (!_transition) return;
            _snapshot.Visibility = Visibility.Collapsed;
            _snapshot.Source = null;
            var light = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(FadeIn));
            light.Completed += (_, _) => EndTransition();
            _curtain.BeginAnimation(OpacityProperty, light);
        };
        _curtain.BeginAnimation(OpacityProperty, dark);
    }

    private void EndTransition()
    {
        _transition = false;
        _curtain.BeginAnimation(OpacityProperty, null);
        _curtain.Opacity = 0;
        _curtain.IsHitTestVisible = false;
        _snapshot.Visibility = Visibility.Collapsed;
        _snapshot.Source = null;
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox || _transition) return;
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

    // ------------------------------------------------------------------ cena desenhada

    private void ShowDesigned(Screen screen)
    {
        var scene = screen.Designed!;
        var messages = screen.Messages;
        var elements = new Dictionary<Piece, FrameworkElement>(ReferenceEqualityComparer.Instance);
        var context = new RenderContext
        {
            Live = true,
            Click = _game.ClickPiece,
            Answer = _game.AnswerPiece,
            Messages = messages,
            IsClickable = _game.IsClickable,
            Cards = _game.CardsOf,
            Created = (piece, element) => elements[piece] = element,
            ScrollOffsets = _scrollOffsets,
        };

        _designed.Children.Clear();
        if (ScreenRenderer.Background(scene.Layout) is { } background)
            _designed.Children.Add(background);

        bool hasMessagesPiece = false;
        foreach (var piece in scene.Layout.Pieces)
        {
            // As peças do cartão modelo aparecem dentro dos cartões da Lista.
            if (!piece.Visible || piece.List != null) continue;
            hasMessagesPiece |= piece.Type == PieceType.Messages;
            var element = ScreenRenderer.Create(piece, context);
            Canvas.SetLeft(element, piece.X);
            Canvas.SetTop(element, piece.Y);
            _designed.Children.Add(element);
            if (_answerBox == null && element.Tag is TextBox box) _answerBox = box;
        }

        // Sem a peça Mensagens, o que o game.Write escreveu aparece num aviso por cima da tela,
        // num lugar que não cobre os botões.
        var toast = screen.Toast ?? messages;
        if (!hasMessagesPiece && toast.Count > 0)
        {
            var avoid = scene.Layout.Pieces
                .Where(p => p.Visible && p.List == null && p.Type is PieceType.Button or PieceType.Input)
                .Select(p => new Rect(p.X, p.Y, p.Width, p.Height))
                .ToList();
            _designed.Children.Add(Toast(toast, avoid));
        }

        foreach (var (piece, effect) in screen.Effects)
        {
            if (elements.TryGetValue(piece, out var element)) Play(element, effect);
        }
    }

    /// <summary>Tremer (vai e volta para os lados) ou piscar (some e volta duas vezes).</summary>
    private static void Play(FrameworkElement element, Effect effect)
    {
        if (effect == GameEngine.Effect.Shake)
        {
            var move = new TranslateTransform();
            element.RenderTransform = move;
            var shake = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(420) };
            double[] steps = [0, -9, 9, -7, 7, -4, 4, -2, 0];
            for (int i = 0; i < steps.Length; i++)
                shake.KeyFrames.Add(new LinearDoubleKeyFrame(steps[i], KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(420.0 * i / (steps.Length - 1)))));
            element.Loaded += (_, _) => move.BeginAnimation(TranslateTransform.XProperty, shake);
            return;
        }
        var flash = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(460) };
        double[] values = [1, 0.15, 1, 0.15, 1];
        for (int i = 0; i < values.Length; i++)
            flash.KeyFrames.Add(new LinearDoubleKeyFrame(values[i], KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(460.0 * i / (values.Length - 1)))));
        var opacity = element.Opacity;
        flash.Completed += (_, _) =>
        {
            element.BeginAnimation(OpacityProperty, null);
            element.Opacity = opacity;
        };
        element.Loaded += (_, _) => element.BeginAnimation(OpacityProperty, flash);
    }

    private static FrameworkElement Toast(IReadOnlyList<MessageLine> messages, IReadOnlyList<Rect> avoid)
    {
        var list = new StackPanel();
        foreach (var line in messages.TakeLast(4))
            list.Children.Add(ScreenRenderer.MessageView(line, 17));
        var toast = new Border
        {
            Background = Theme.Freeze(Theme.WithAlpha(Theme.PanelColor, 0xEE)),
            BorderBrush = Theme.PanelBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 10, 14, 4),
            Child = list,
            Width = 620,
            IsHitTestVisible = false,
        };
        double left = (StageWidth - 620) / 2;
        Canvas.SetLeft(toast, left);
        // Embaixo; se cobrir um botão, no meio; se ainda cobrir, em cima.
        toast.Measure(new Size(620, double.PositiveInfinity));
        double height = toast.DesiredSize.Height;
        double[] candidates = [StageHeight - height - 20, (StageHeight - height) / 2, 20];
        double top = candidates.FirstOrDefault(y => !avoid.Any(r => r.IntersectsWith(new Rect(left, y, 620, height))), candidates[1]);
        Canvas.SetTop(toast, top);
        toast.Loaded += (_, _) => toast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        return toast;
    }

    // ------------------------------------------------------------------ cena automática

    private void ShowAutomatic(Screen screen)
    {
        _title.Text = screen.Title ?? "";
        _title.Visibility = string.IsNullOrEmpty(screen.Title) ? Visibility.Collapsed : Visibility.Visible;

        _items.Children.Clear();
        bool hasNews = false;
        foreach (var item in screen.Items)
        {
            switch (item)
            {
                case TextItem text:
                    hasNews |= text.IsNews;
                    var view = ScreenRenderer.MessageView(new MessageLine(text.Text, text.Color, text.IsNews), 19);
                    if (!text.IsNews) view.Margin = new Thickness(0, 0, 0, 10);
                    _items.Children.Add(view);
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

        if (hasNews) Dispatcher.BeginInvoke(_scroll.ScrollToEnd, DispatcherPriority.Loaded);
        else _scroll.ScrollToHome();
    }

    private static FrameworkElement ImageView(string path)
    {
        var image = Theme.LoadImage(path);
        if (image == null)
        {
            return new TextBlock
            {
                Text = $"[imagem não encontrada: {path} — coloque o arquivo na pasta Assets do projeto]",
                FontSize = 15, FontStyle = FontStyles.Italic, Foreground = Theme.Muted,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
            };
        }
        return new WpfImage { Source = image, MaxHeight = 240, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12) };
    }

    private FrameworkElement AskView(AskItem item)
    {
        var question = new TextBlock { Text = item.Question, FontSize = 19, TextWrapping = TextWrapping.Wrap, Foreground = Theme.Text, Margin = new Thickness(0, 4, 0, 8) };
        var box = new TextBox
        {
            FontSize = 18,
            Padding = new Thickness(8, 6, 8, 6),
            Background = Theme.Panel,
            Foreground = Theme.Text,
            CaretBrush = Theme.Text,
            BorderBrush = Theme.PanelBorder,
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
        var ok = Theme.MakeButton(new TextBlock { Text = "OK", FontSize = 17 });
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
        var piece = new Piece { Type = PieceType.Bar, Name = "Bar", Text = bar.Label, Value = bar.Value, Max = bar.Max, Color = bar.Color, Width = 218, Height = 40 };
        var view = ScreenRenderer.Create(piece, new RenderContext { Live = true });
        view.Width = double.NaN; // ocupa a largura do painel
        view.Margin = new Thickness(0, 0, 0, 12);
        return view;
    }

    private Button ButtonView(ButtonItem item, int? number)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        if (number != null)
        {
            content.Children.Add(new Border
            {
                Background = Theme.Freeze(System.Windows.Media.Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 0, 6, 0),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = number.ToString(), FontSize = 13, Foreground = Brushes.White },
            });
        }
        content.Children.Add(new TextBlock { Text = item.Text, FontSize = 17, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
        var button = Theme.MakeButton(content);
        button.Padding = new Thickness(0);
        button.Margin = new Thickness(0, 0, 10, 10);
        button.Click += (_, _) => _game.Act(item.OnClick);
        return button;
    }
}
