using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;

namespace CSharpLab.Views;

/// <summary>Janela de diálogo no tema da aplicação: título, conteúdo e botões à direita.</summary>
public class DialogWindow : Window
{
    private readonly StackPanel _buttons;
    private readonly ContentControl _body;
    private readonly TextBlock _title;

    public DialogWindow(string title)
    {
        Title = title;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = 460;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)Application.Current.FindResource("BgElevated");
        Foreground = (Brush)Application.Current.FindResource("TextPrimary");
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
        FontSize = 13;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 40,
            ResizeBorderThickness = new Thickness(0),
            GlassFrameThickness = new Thickness(0, 0, 0, 1),
            UseAeroCaptionButtons = false,
        });
        Owner = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault(w => w.IsVisible);
        if (Owner == null) WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _title = new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(20, 16, 20, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _body = new ContentControl { Margin = new Thickness(20, 10, 20, 0), Focusable = false };
        _buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(20, 18, 20, 18),
        };

        var root = new DockPanel();
        DockPanel.SetDock(_title, Dock.Top);
        DockPanel.SetDock(_buttons, Dock.Bottom);
        root.Children.Add(_title);
        root.Children.Add(_buttons);
        root.Children.Add(_body);
        Content = new Border
        {
            BorderBrush = (Brush)Application.Current.FindResource("BorderStrong"),
            BorderThickness = new Thickness(1),
            Child = root,
        };

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                e.Handled = true;
            }
        };
        Loaded += (_, _) =>
        {
            var focus = InitialFocus ?? _buttons.Children.OfType<Button>().FirstOrDefault(b => b.IsDefault);
            focus?.Focus();
        };
    }

    public object? Body
    {
        get => _body.Content;
        set => _body.Content = value;
    }

    public UIElement? InitialFocus { get; set; }

    public object? Result { get; protected set; }

    public Button AddButton(string text, object? result, string style = "DialogButton", bool isDefault = false, bool isCancel = false)
    {
        var button = new Button
        {
            Content = text,
            Style = (Style)Application.Current.FindResource(style),
            IsDefault = isDefault,
            IsCancel = isCancel,
        };
        button.Click += (_, _) =>
        {
            if (!OnButton(result)) return;
            Result = result;
            DialogResult = !isCancel;
        };
        _buttons.Children.Add(button);
        return button;
    }

    /// <summary>Permite validar antes de fechar. Retorne false para manter o diálogo aberto.</summary>
    protected virtual bool OnButton(object? result) => true;

    public static TextBlock Paragraph(string text, bool muted = false) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Foreground = (Brush)Application.Current.FindResource(muted ? "TextSecondary" : "TextPrimary"),
        LineHeight = 19,
    };
}
