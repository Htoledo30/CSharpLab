using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace CSharpLab.GameEngine;

// game.Pause, game.Read e game.Choose: a janela mostra o pedido por cima da tela e espera o jogador,
// sem travar (os desenhos e animações continuam). O clique do código continua quando ele responde.
internal sealed partial class GameWindow
{
    private Grid? _promptLayer;
    private DispatcherFrame? _promptFrame;
    private int _promptChoice;
    private string _promptText = "";
    private Action<int, string>? _submit;

    public void WaitForPlayer(string button)
    {
        var label = new TextBlock { Text = button + "  ▸", FontFamily = Theme.BodyFamily, FontSize = 18, Foreground = Brushes.White };
        var go = Theme.MakeButton(label, Color.Gold);
        go.HorizontalAlignment = HorizontalAlignment.Right;
        go.VerticalAlignment = VerticalAlignment.Bottom;
        go.Margin = new Thickness(0, 0, 28, 24);
        go.MinWidth = 170;
        go.Height = 50;
        go.Effect = Theme.DropShadow;
        go.Click += (_, _) => Submit(0, "");
        go.ToolTip = "Enter ou Espaço também continuam";

        // Clicar em qualquer lugar da tela também continua (e os botões do jogo não recebem o clique).
        var layer = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand };
        layer.MouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource == layer) Submit(0, "");
        };
        layer.Children.Add(go);
        go.Loaded += (_, _) => go.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        Prompt(layer, key => key is Key.Enter or Key.Space ? 0 : null);
    }

    public string ReadAnswer(string question)
    {
        var box = new TextBox
        {
            FontFamily = Theme.BodyFamily,
            FontSize = 19,
            Padding = new Thickness(10, 7, 10, 7),
            Background = Theme.Background,
            Foreground = Theme.Text,
            CaretBrush = Theme.Text,
            BorderBrush = Theme.PanelBorder,
            MaxLength = 200,
        };
        var ok = Theme.MakeButton(new TextBlock { Text = "OK", FontFamily = Theme.BodyFamily, FontSize = 17, Foreground = Brushes.White });
        ok.Margin = new Thickness(10, 0, 0, 0);
        void Send()
        {
            var answer = box.Text.Trim();
            if (answer.Length == 0)
            {
                box.Focus();
                return;
            }
            Submit(0, answer);
        }
        ok.Click += (_, _) => Send();
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            Send();
        };
        var row = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        DockPanel.SetDock(ok, Dock.Right);
        row.Children.Add(ok);
        row.Children.Add(box);

        var content = new StackPanel();
        content.Children.Add(Question(question));
        content.Children.Add(row);
        box.Loaded += (_, _) => Dispatcher.BeginInvoke(() => box.Focus(), DispatcherPriority.Input);
        Prompt(Card(content), _ => null);
        return _promptText;
    }

    public int ChooseOption(string question, IReadOnlyList<string> options)
    {
        var content = new StackPanel();
        content.Children.Add(Question(question));
        var buttons = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        for (int i = 0; i < options.Count; i++)
        {
            int index = i;
            var label = new DockPanel();
            var number = new Border
            {
                Background = Theme.Freeze(System.Windows.Media.Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(7, 0, 7, 1),
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), FontSize = 13, Foreground = Brushes.White },
            };
            DockPanel.SetDock(number, Dock.Left);
            label.Children.Add(number);
            label.Children.Add(new TextBlock { Text = options[i], FontFamily = Theme.BodyFamily, FontSize = 17, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            var button = Theme.MakeButton(label);
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Margin = new Thickness(0, 0, 0, 8);
            button.Height = 46;
            button.Click += (_, _) => Submit(index, options[index]);
            buttons.Children.Add(button);
        }
        content.Children.Add(buttons);
        Prompt(Card(content), key =>
        {
            int n = key switch
            {
                >= Key.D1 and <= Key.D9 => key - Key.D1,
                >= Key.NumPad1 and <= Key.NumPad9 => key - Key.NumPad1,
                _ => -1,
            };
            return n >= 0 && n < options.Count ? n : null;
        });
        return _promptChoice;
    }

    public void CloseGame()
    {
        // Fecha depois do clique atual sair do caminho (fechar a janela no meio dele confunde o WPF).
        Dispatcher.BeginInvoke(() =>
        {
            if (IsLoaded) Close();
        });
        if (_promptFrame != null) _promptFrame.Continue = false;
    }

    // ------------------------------------------------------------------ por dentro

    /// <summary>Para os testes responderem um pedido aberto, como o jogador faria.</summary>
    internal bool HasPrompt => _promptFrame != null;

    internal void SubmitPrompt(int choice, string text) => Submit(choice, text);

    private void Submit(int choice, string text) => _submit?.Invoke(choice, text);

    /// <summary>Mostra o pedido por cima da tela e espera (sem travar a janela) até o jogador responder ou fechar.</summary>
    private void Prompt(FrameworkElement layer, Func<Key, int?> keyChoice)
    {
        _promptChoice = 0;
        _promptText = "";
        if (!IsLoaded) return;
        EnsurePromptLayer();
        _promptLayer!.Children.Clear();
        _promptLayer.Children.Add(layer);
        _promptLayer.Visibility = Visibility.Visible;

        var frame = _promptFrame = new DispatcherFrame();
        _submit = (choice, text) =>
        {
            _promptChoice = choice;
            _promptText = text;
            frame.Continue = false;
        };
        void Keys(object sender, KeyEventArgs e)
        {
            if (e.IsRepeat || Keyboard.FocusedElement is TextBox) return;
            if (keyChoice(e.Key) is { } choice)
            {
                e.Handled = true;
                Submit(choice, "");
            }
        }
        void Closing(object? sender, EventArgs e) => frame.Continue = false;
        // Recebe as teclas mesmo que a janela já tenha usado (os números dos botões, por exemplo).
        var keys = new KeyEventHandler(Keys);
        AddHandler(PreviewKeyDownEvent, keys, handledEventsToo: true);
        Closed += Closing;
        Dispatcher.BeginInvoke(() => Keyboard.Focus(this), DispatcherPriority.Input);
        try
        {
            Dispatcher.PushFrame(frame);
        }
        finally
        {
            RemoveHandler(PreviewKeyDownEvent, keys);
            Closed -= Closing;
            _submit = null;
            _promptFrame = null;
            _promptLayer.Children.Clear();
            _promptLayer.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>A camada dos pedidos fica por cima das peças e por baixo da troca de cena.</summary>
    private void EnsurePromptLayer()
    {
        if (_promptLayer != null) return;
        _promptLayer = new Grid { Width = StageWidth, Height = StageHeight, Visibility = Visibility.Collapsed };
        _root.Children.Insert(_root.Children.IndexOf(_snapshot), _promptLayer);
    }

    /// <summary>Um cartão no meio da tela, com o resto escurecido atrás.</summary>
    private static FrameworkElement Card(FrameworkElement content)
    {
        var card = new Border
        {
            Width = 540,
            Padding = new Thickness(28, 24, 28, 18),
            CornerRadius = new CornerRadius(Math.Max(8, Theme.PanelRadius)),
            Background = Theme.Panel,
            BorderBrush = Theme.PanelBorder,
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = Theme.DropShadow,
            Child = content,
        };
        var scrim = new Grid { Background = Theme.Freeze(System.Windows.Media.Color.FromArgb(0x99, 0, 0, 0)) };
        scrim.Children.Add(card);
        scrim.Loaded += (_, _) => scrim.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
        return scrim;
    }

    private static TextBlock Question(string text) => new()
    {
        Text = text,
        FontFamily = Theme.BodyFamily,
        FontSize = 21,
        Foreground = Theme.Text,
        TextWrapping = TextWrapping.Wrap,
    };
}
