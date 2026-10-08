using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using CSharpLab.ViewModels;

namespace CSharpLab.Views;

/// <summary>
/// "Seu primeiro jogo em 5 minutos": um guia curto que fica ao lado do editor enquanto a pessoa segue os passos.
/// Ele mostra onde fica cada coisa; quem clica, arrasta e escreve o código é a pessoa.
/// </summary>
public sealed class GuideWindow : Window
{
    /// <summary>Um passo: título, texto (com `código` entre crases) e, se houver, um trecho de código para escrever.</summary>
    internal sealed record Step(string Title, string Text, string? Code = null, string? Then = null);

    internal static readonly Step[] Steps =
    [
        new("Crie o jogo",
            "Em Arquivo → Novo projeto…, escolha Jogo com botões e dê um nome. O jogo já vem com duas cenas: a vila (`Start`) e a floresta (`Forest`)."),
        new("Jogue",
            "Aperte F5 (ou Executar, lá em cima). Clique nos botões do jogo. Para voltar, feche a janela do jogo."),
        new("Mude a tela",
            "Na aba Start.json, arraste o botão \"Procurar moedas\" para outro lugar e puxe os quadradinhos para mudar o tamanho. No painel da direita, troque o texto e a cor. Aperte F5 de novo para ver."),
        new("Crie um botão que faz algo",
            "Em Peças, à esquerda, clique em Botão. No painel, dê o nome `Rest` e o texto \"Descansar\". Em Ao clicar, use Escrever o que ele faz e escreva entre as chaves:",
            "gold -= 1;\ngame.Write(\"Você descansou.\");",
            "O `gold` é a variável do ouro, lá no topo do Program.cs. Aperte F5 e teste."),
        new("Uma cena nova",
            "Use Cenas → Nova tela do jogo… e chame de `Cave`. Volte ao código do botão `Rest` e troque o que está entre as chaves por:",
            "game.GoTo(\"Cave\");",
            "Agora o botão leva para a caverna. Desenhe a tela dela como quiser."),
    ];

    private readonly MainViewModel _vm;

    public GuideWindow(MainViewModel vm)
    {
        _vm = vm;
        Title = "Seu primeiro jogo em 5 minutos";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        Width = 400;
        MinWidth = 320;
        MinHeight = 300;
        ShowInTaskbar = false;
        UseLayoutRounding = true;
        SetResourceReference(BackgroundProperty, "BgElevated");
        SetResourceReference(ForegroundProperty, "TextPrimary");
        SetResourceReference(FontFamilyProperty, "UiFont");
        FontSize = 13;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 44,
            ResizeBorderThickness = new Thickness(5),
            GlassFrameThickness = new Thickness(0, 0, 0, 1),
            UseAeroCaptionButtons = false,
        });
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };

        var steps = new StackPanel { Margin = new Thickness(20, 4, 20, 20) };
        var intro = new TextBlock
        {
            Text = "Siga com o CSharp Lab aberto ao lado. A tela você monta com o mouse; o que acontece, você escreve em C#.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16),
        };
        intro.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        steps.Children.Add(intro);
        for (int i = 0; i < Steps.Length; i++)
        {
            steps.Children.Add(StepView(i + 1, Steps[i]));
            // O primeiro passo tem um atalho para o diálogo (já com "Jogo com botões" escolhido).
            if (i == 0) steps.Children.Add(NewGameButton());
        }
        steps.Children.Add(Finish());

        var scroll = new ScrollViewer { Content = steps, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var root = new DockPanel();
        var header = Header();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(scroll);
        var frame = new Border { Child = root, BorderThickness = new Thickness(1) };
        frame.SetResourceReference(Border.BorderBrushProperty, "BorderStrong");
        frame.SetResourceReference(Border.BackgroundProperty, "BgElevated");
        Content = frame;
    }

    /// <summary>Abre ao lado direito da janela principal, sem cobrir o meio do editor.</summary>
    public void PlaceBeside(Window owner)
    {
        Owner = owner;
        WindowStartupLocation = WindowStartupLocation.Manual;
        var area = owner.WindowState == WindowState.Maximized ? SystemParameters.WorkArea : new Rect(owner.Left, owner.Top, owner.ActualWidth, owner.ActualHeight);
        Height = Math.Max(MinHeight, Math.Min(720, area.Height - 120));
        Left = area.Right - Width - 24;
        Top = area.Top + 72;
    }

    private FrameworkElement Header()
    {
        var title = new TextBlock { Text = Title, FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        var close = new Button { Content = "", ToolTip = "Fechar (Esc)", VerticalAlignment = VerticalAlignment.Center };
        close.SetResourceReference(StyleProperty, "IconButton");
        WindowChrome.SetIsHitTestVisibleInChrome(close, true);
        close.Click += (_, _) => Close();
        var header = new DockPanel { Height = 44, Margin = new Thickness(20, 0, 10, 0) };
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        header.Children.Add(title);
        return header;
    }

    private static FrameworkElement StepView(int number, Step step)
    {
        var badge = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(12),
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = number.ToString(), FontWeight = FontWeights.SemiBold, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        badge.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
        ((TextBlock)badge.Child).SetResourceReference(TextBlock.ForegroundProperty, "Accent");

        var body = new StackPanel();
        var title = new TextBlock { Text = step.Title, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 4) };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        body.Children.Add(title);
        body.Children.Add(Rich(step.Text));
        if (step.Code != null) body.Children.Add(CodeBlock(step.Code));
        if (step.Then != null)
        {
            var then = Rich(step.Then);
            then.Margin = new Thickness(0, 8, 0, 0);
            body.Children.Add(then);
        }

        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
        DockPanel.SetDock(badge, Dock.Left);
        row.Children.Add(badge);
        row.Children.Add(body);
        return row;
    }

    /// <summary>Texto com `código` entre crases, mostrado na fonte de código.</summary>
    internal static TextBlock Rich(string text)
    {
        var block = new TextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 20 };
        block.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        var parts = text.Split('`');
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0) continue;
            var run = new Run(parts[i]);
            if (i % 2 == 1)
            {
                run.SetResourceReference(TextElement.FontFamilyProperty, "CodeFont");
                run.SetResourceReference(TextElement.ForegroundProperty, "Accent");
            }
            block.Inlines.Add(run);
        }
        return block;
    }

    private static FrameworkElement CodeBlock(string code)
    {
        var text = new TextBlock { Text = code, FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
        text.SetResourceReference(TextBlock.FontFamilyProperty, "CodeFont");
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        var box = new Border { Child = text, Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 8, 0, 0), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1) };
        box.SetResourceReference(Border.BackgroundProperty, "BgBase");
        box.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        return box;
    }

    private FrameworkElement NewGameButton()
    {
        var button = new Button { Content = "Criar o jogo agora…", Margin = new Thickness(36, -8, 0, 18), HorizontalAlignment = HorizontalAlignment.Left };
        button.SetResourceReference(StyleProperty, "DialogButton");
        button.Margin = new Thickness(36, -8, 0, 18);
        button.Click += (_, _) => _vm.NewGameCommand.Execute(null);
        return button;
    }

    private static FrameworkElement Finish()
    {
        var text = Rich("Pronto: você já sabe o essencial. A tela fica na aba Tela; o que acontece fica no código, dentro de cada `game.Scene`. Para ir e voltar entre os dois, use o botão Cenas.");
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        var box = new Border { Child = text, Padding = new Thickness(14, 12, 14, 12), CornerRadius = new CornerRadius(8) };
        box.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
        return box;
    }
}
