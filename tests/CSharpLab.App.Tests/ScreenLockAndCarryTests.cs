using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CSharpLab.Core.Files;
using CSharpLab.Core.Settings;
using CSharpLab.GameEngine;
using CSharpLab.Screens;
using CSharpLab.ViewModels;

namespace CSharpLab.App.Tests;

/// <summary>
/// Mexer na tela sem brigar com ela: o cadeado (travar), o olho (esconder só no Estúdio), a Caixa que leva o que
/// está em cima dela, e as opções novas do painel (degradê, borda, fundo da tela, barra animada).
/// </summary>
public sealed class ScreenLockAndCarryTests
{
    // A caixa de ações do combate: fundo, título e texto em cima dela; um botão metade para fora; um texto atrás.
    private const string Fight = """
        { "pieces": [
          { "type": "Text", "name": "Behind", "x": 60, "y": 70, "width": 100, "height": 30, "text": "atrás" },
          { "type": "Box", "name": "Panel", "x": 40, "y": 56, "width": 320, "height": 292 },
          { "type": "Text", "name": "Title", "x": 48, "y": 64, "width": 120, "height": 32, "text": "SUAS AÇÕES" },
          { "type": "Box", "name": "Inner", "x": 48, "y": 120, "width": 200, "height": 120, "color": "Red" },
          { "type": "Text", "name": "Log", "x": 56, "y": 130, "width": 180, "height": 90, "text": "" },
          { "type": "Button", "name": "HalfOut", "x": 300, "y": 300, "width": 120, "height": 40, "text": "Fora" },
          { "type": "Bar", "name": "Health", "x": 600, "y": 30, "width": 300, "height": 44, "text": "Vida", "value": 20, "max": 20 }
        ] }
        """;

    private static (DocumentViewModel Doc, ScreenDesignerModel Model) Open(string json = Fight)
    {
        var dir = Ui.NewFolder("jogo");
        var path = Path.Combine(dir, "Screens", "Fight.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
        var doc = new DocumentViewModel(json, path, TextFileIO.Utf8NoBom, null);
        return (doc, new ScreenDesignerModel(doc));
    }

    private static int UndoSteps(DocumentViewModel doc)
    {
        int steps = 0;
        while (doc.Document.UndoStack.CanUndo)
        {
            doc.Document.UndoStack.Undo();
            steps++;
        }
        return steps;
    }

    [Fact]
    public void Caixa_leva_o_que_esta_inteiro_em_cima_dela() => Ui.Run(() =>
    {
        var (doc, model) = Open();
        // Title e a caixa de dentro (com o Log dela) vão junto; o texto de trás e o botão metade para fora, não.
        Assert.Equal(["Title", "Inner", "Log"], model.Carried(["Panel"]));
        Assert.Equal(["Log"], model.Carried(["Inner"]));
        Assert.Empty(model.Carried(["Title"]));   // só a Caixa leva

        // As setas também levam (um passo só no desfazer).
        model.Select("Panel");
        model.Nudge(10, 0);
        model.Nudge(10, 0);
        var layout = model.Layout!;
        Assert.Equal((60d, 68d, 68d, 76d), (layout.Find("Panel")!.X, layout.Find("Title")!.X, layout.Find("Inner")!.X, layout.Find("Log")!.X));
        Assert.Equal((60d, 300d), (layout.Find("Behind")!.X, layout.Find("HalfOut")!.X));
        Assert.Equal(1, UndoSteps(doc));

        // Travada, a peça fica: a caixa vai sem ela.
        var (_, model2) = Open();
        model2.SetLocked("Title", true);
        Assert.Equal(["Inner", "Log"], model2.Carried(["Panel"]));
        return Task.CompletedTask;
    });

    [Fact]
    public void Cadeado_trava_no_palco_e_vai_para_o_arquivo() => Ui.Run(() =>
    {
        var (doc, model) = Open();
        var inside = new Point(300, 200);   // só a caixa Panel está aqui
        Assert.Equal("Panel", model.HitTest(inside)?.Name);

        model.SetLocked("Panel", true);
        Assert.Contains("\"name\": \"Panel\", \"x\": 40, \"y\": 56, \"width\": 320, \"height\": 292, \"locked\": true }", doc.Document.Text);
        Assert.Null(model.HitTest(inside));                                    // o mouse passa direto
        Assert.Equal("Log", model.HitTest(new Point(100, 150))?.Name);         // as de cima continuam pegáveis
        Assert.DoesNotContain(model.PiecesIn(new Rect(0, 0, 960, 540)), p => p.Name == "Panel");
        model.SelectAll();
        Assert.DoesNotContain("Panel", model.SelectedNames);

        // Selecionada pelas Camadas: as setas não mexem, mas o painel (Edit) muda à vontade.
        model.Select("Panel");
        model.Nudge(10, 10);
        Assert.Equal(40, model.Layout!.Find("Panel")!.X);
        model.Edit("Panel", p => p.X = 50);
        Assert.Equal(50, model.Layout!.Find("Panel")!.X);

        // A cópia nasce destravada (para dar para arrastar).
        var copy = model.Duplicate();
        Assert.False(copy!.Locked);
        model.Delete();   // a cópia (já selecionada) sai, para não ficar por cima do ponto testado abaixo

        // O jogo ignora a trava: a peça aparece e funciona normal.
        Assert.True(ScreenFile.Parse(doc.Document.Text).Success);
        model.SetLocked("Panel", false);
        Assert.Equal("Panel", model.HitTest(inside)?.Name);
        return Task.CompletedTask;
    });

    [Fact]
    public void Olho_esconde_so_no_estudio_e_nao_mexe_no_arquivo() => Ui.Run(() =>
    {
        var (doc, model) = Open();
        var before = doc.Document.Text;
        int changed = 0;
        model.Changed += () => changed++;
        model.Select("Panel");

        model.SetHiddenHere("Panel", true);
        Assert.Equal(before, doc.Document.Text);   // nada no arquivo: no jogo ela aparece
        Assert.False(doc.Document.UndoStack.CanUndo);
        Assert.True(model.IsHiddenHere("Panel"));
        Assert.Empty(model.SelectedNames);        // escondida, sai da seleção
        Assert.Null(model.HitTest(new Point(300, 200)));
        Assert.Equal(1, changed);

        model.SetHiddenHere("Panel", false);
        Assert.Equal("Panel", model.HitTest(new Point(300, 200))?.Name);
        return Task.CompletedTask;
    });

    [Fact]
    public void Fundo_da_tela_com_cor_e_degrade() => Ui.Run(() =>
    {
        var (doc, model) = Open();
        model.SetBackgroundColor(GameEngine.Color.Hex("#1A1030"));
        model.SetBackgroundGradient(GameEngine.Color.Black);
        Assert.Contains("\"backgroundColor\": \"#1A1030\",", doc.Document.Text);
        Assert.Contains("\"backgroundGradient\": \"Black\",", doc.Document.Text);
        model.SetBackgroundGradient(null);
        Assert.DoesNotContain("backgroundGradient", doc.Document.Text);
        Assert.Equal(3, UndoSteps(doc));
        return Task.CompletedTask;
    });

    /// <summary>O painel e as Camadas na tela. Fotos em %TEMP%\csharplab-painel-caixa.png e csharplab-camadas.png.</summary>
    [Fact]
    public void Painel_e_camadas_mostram_as_opcoes_novas() => Ui.Run(async () =>
    {
        var (doc, model) = Open();
        using var vm = new MainViewModel(new AppSettings()) { Dialogs = new FakeDialogs(), Terminal = new FakeTerminal() };
        var view = new ScreenEditorView(doc, vm);
        var layers = new LayersPanel { Model = view.Model, Width = 260 };
        var root = new DockPanel();
        DockPanel.SetDock(layers, Dock.Left);
        root.Children.Add(layers);
        root.Children.Add(view);
        var window = new Window { Content = root, Width = 1500, Height = 2000, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Left = -10000, Top = -10000 };
        window.Show();
        try
        {
            string PanelText() => string.Join(" | ", Descendants(view.Properties).OfType<TextBlock>().Where(t => t.IsVisible).Select(t => t.Text));

            view.Model.Select("Inner");
            await Settle(window);
            var text = PanelText();
            foreach (var expected in new[] { "DEGRADÊ", "PREENCHIMENTO", "Borda", "Cor da borda", "Travada no palco" })
                Assert.Contains(expected, text);
            Assert.DoesNotContain("Para o lado", text);   // a direção só aparece com o degradê ligado
            view.Model.Edit("Inner", p => p.Gradient = GameEngine.Color.Black);
            await Settle(window);
            Assert.Contains("Para o lado", PanelText());
            Save(view.Properties, "csharplab-painel-caixa.png");

            // Sem borda, as opções dela somem.
            view.Model.Edit("Inner", p => p.Border = false);
            await Settle(window);
            Assert.DoesNotContain("Cor da borda", PanelText());

            view.Model.Select("Health");
            await Settle(window);
            Assert.Contains("Animar no jogo", PanelText());

            view.Model.Select(null);
            await Settle(window);
            foreach (var expected in new[] { "FUNDO", "Cor", "Degradê até (embaixo)", "Imagem (fica por cima da cor)" })
                Assert.Contains(expected, PanelText());

            // Camadas: o cadeado de uma linha trava a peça, sem selecionar a linha.
            var row = Descendants(layers).OfType<Border>().First(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Camada Panel");
            var lockButton = Descendants(row).OfType<Border>().First(b => System.Windows.Automation.AutomationProperties.GetName(b).StartsWith("Travar"));
            lockButton.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
            Assert.True(view.Model.Layout!.Find("Panel")!.Locked);
            Assert.Empty(view.Model.SelectedNames);

            await Settle(window);
            row = Descendants(layers).OfType<Border>().First(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Camada Title");
            var eye = Descendants(row).OfType<Border>().First(b => System.Windows.Automation.AutomationProperties.GetName(b).StartsWith("Esconder"));
            eye.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
            Assert.True(view.Model.IsHiddenHere("Title"));
            await Settle(window);
            Save(layers, "csharplab-camadas.png");
            Save(view.Stage, "csharplab-palco-escondida.png");
        }
        finally
        {
            window.Close();
            view.Detach();
        }
    });

    [Fact]
    public void Nome_sugerido_so_para_nome_automatico() => Ui.Run(() =>
    {
        var (doc, model) = Open("""
            { "pieces": [
              { "type": "Text", "name": "Text1", "x": 48, "y": 64, "width": 200, "height": 32, "text": "SUAS AÇÕES" },
              { "type": "Button", "name": "Button1", "x": 48, "y": 200, "width": 200, "height": 46, "text": "Ataque Pesado" },
              { "type": "Bar", "name": "Bar1", "x": 48, "y": 300, "width": 200, "height": 44, "text": "Vida" },
              { "type": "Text", "name": "Health", "x": 300, "y": 300, "width": 200, "height": 44, "text": "Vida" },
              { "type": "Button", "name": "Defend", "x": 300, "y": 200, "width": 200, "height": 46, "text": "Ataque" }
            ] }
            """);
        var layout = model.Layout!;
        Assert.Equal("YourActions", model.SuggestedName(layout.Find("Text1")!));
        Assert.Equal("HeavyAttack", model.SuggestedName(layout.Find("Button1")!));
        Assert.Equal("HealthBar", model.SuggestedName(layout.Find("Bar1")!));   // "Health" já é de outra peça
        Assert.Null(model.SuggestedName(layout.Find("Defend")!));               // nome escolhido por você: não mexe

        model.Select("Button1");
        Assert.Null(model.Rename(model.SuggestedName(model.Selected!)!));
        Assert.Contains("\"name\": \"HeavyAttack\"", doc.Document.Text);
        Assert.Null(model.SuggestedName(model.Selected!));
        return Task.CompletedTask;
    });

    /// <summary>O "▶ Ver": a barra anima no palco e volta sozinha; o Pop aparece e some. Foto em %TEMP%\csharplab-ver-barra.png.</summary>
    [Fact]
    public void Ver_mostra_o_movimento_no_palco_sem_mudar_a_tela() => Ui.Run(async () =>
    {
        var (doc, model) = Open();
        using var vm = new MainViewModel(new AppSettings()) { Dialogs = new FakeDialogs(), Terminal = new FakeTerminal() };
        var view = new ScreenEditorView(doc, vm);
        var window = new Window { Content = view, Width = 1400, Height = 900, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Left = -10000, Top = -10000 };
        window.Show();
        try
        {
            await Settle(window);
            var before = doc.Document.Text;
            view.Model.Preview("Health", PiecePreview.Bar);
            view.Model.Preview("Health", PiecePreview.Pop);
            await Task.Delay(200);
            window.UpdateLayout();
            Assert.Contains(Descendants(view.Stage).OfType<TextBlock>(), t => t.Text == "-7");
            Assert.Contains(Descendants(view.Stage).OfType<TextBlock>(), t => t.Text == "12 / 20");   // a barra perdeu 40% no exemplo
            Save(view.Stage, "csharplab-ver-barra.png");
            Assert.Equal(before, doc.Document.Text);   // só mostra: o arquivo não muda

            await Task.Delay(1900);
            window.UpdateLayout();
            Assert.DoesNotContain(Descendants(view.Stage).OfType<TextBlock>(), t => t.Text == "-7");
            Assert.Contains(Descendants(view.Stage).OfType<TextBlock>(), t => t.Text == "20 / 20");   // voltou a ser a do arquivo

            view.Model.Select("Health");
            await Settle(window);
            var texts = Descendants(view.Properties).OfType<Button>().Where(b => b.IsVisible).Select(b => b.Content as string).ToList();
            foreach (var expected in new[] { "▶ Ver", "▶ Tremer", "▶ Piscar", "▶ Pop" }) Assert.Contains(expected, texts);
        }
        finally
        {
            window.Close();
            view.Detach();
        }
    });

    private static async Task Settle(Window window)
    {
        await Task.Delay(150);
        window.UpdateLayout();
    }

    private static void Save(FrameworkElement element, string fileName)
    {
        // Com o fundo escuro do editor por baixo (os painéis são transparentes).
        double width = Math.Max(1, element.ActualWidth), height = Math.Max(1, element.ActualHeight);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle((Brush)Application.Current.FindResource("BgSidebar"), null, new Rect(0, 0, width, height));
            context.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, width, height));
        }
        var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(Path.GetTempPath(), fileName));
        encoder.Save(file);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var inner in Descendants(child)) yield return inner;
        }
    }
}
