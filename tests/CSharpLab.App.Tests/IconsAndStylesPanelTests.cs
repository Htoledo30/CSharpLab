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
using PanelTab = CSharpLab.Screens.ScreenPropertiesPanel.PanelTab;

namespace CSharpLab.App.Tests;

/// <summary>No Estúdio: escolher o desenho do Ícone (e do Botão), os lados do Dado e Meus estilos.</summary>
public sealed class IconsAndStylesPanelTests
{
    private const string Json = """
        { "pieces": [
          { "type": "Icon", "name": "EnemyIcon", "x": 380, "y": 30, "width": 160, "height": 160, "icon": "Goblin", "color": "Green", "frame": "Circle" },
          { "type": "Dice", "name": "D20", "x": 420, "y": 260, "width": 120, "height": 120, "sides": 20, "color": "Purple" },
          { "type": "Button", "name": "Attack", "x": 40, "y": 430, "width": 200, "height": 56, "text": "Atacar", "color": "Red", "style": "Gradient", "corner": "Square", "font": "Retro" },
          { "type": "Button", "name": "Defend", "x": 260, "y": 430, "width": 200, "height": 56, "text": "Defender" },
          { "type": "Button", "name": "Flee", "x": 480, "y": 430, "width": 200, "height": 56, "text": "Fugir" }
        ] }
        """;

    [Fact]
    public void Icone_dado_e_meus_estilos_no_painel() => Ui.Run(async () =>
    {
        foreach (var old in MyStyles.Load()) MyStyles.Remove(old.Title);
        var dir = Path.Combine(Ui.NewFolder("jogo"), "Screens");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "Fight.json");
        File.WriteAllText(path, Json);
        var doc = new DocumentViewModel(Json, path, TextFileIO.Utf8NoBom, null);
        var dialogs = new FakeDialogs();
        using var vm = new MainViewModel(new AppSettings()) { Dialogs = dialogs, Terminal = new FakeTerminal() };
        var view = new ScreenEditorView(doc, vm);
        var window = new Window { Content = view, Width = 1280, Height = 900, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Left = -10000, Top = -10000 };
        window.Show();
        var panel = view.Properties;
        try
        {
            List<string> Texts() => Descendants(panel).OfType<TextBlock>().Where(t => t.IsVisible).Select(t => t.Text).ToList();
            Button Picker() => Descendants(panel).OfType<Button>().First(b => b.IsVisible && System.Windows.Automation.AutomationProperties.GetName(b) == "Escolher o ícone");

            // Ícone: o desenho mostra o nome em português e o do código; a lista acha pela busca.
            panel.ShowTab(PanelTab.Piece);
            view.Model.Select("EnemyIcon");
            await Settle(window);
            Assert.Contains("DESENHO", Texts());
            Assert.Contains("Goblin (Icon.Goblin)", Texts());
            var picker = Picker();
            picker.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            await Settle(window);
            var popup = (Popup)Descendants(panel).OfType<Button>().Select(b => b).First(b => b == picker).Tag!;
            Assert.True(popup.IsOpen);
            var chooser = (FrameworkElement)popup.Child;
            var search = Descendants(chooser).OfType<TextBox>().First();
            search.Text = "dragao";
            await Settle(window);
            var dragon = Descendants(chooser).OfType<Border>().First(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Dragão (Icon.Dragon)");
            dragon.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
            await Settle(window);
            Assert.False(popup.IsOpen);
            Assert.Contains("\"icon\": \"Dragon\"", doc.Document.Text);
            view.Model.Undo();
            Assert.Contains("\"icon\": \"Goblin\"", doc.Document.Text);

            panel.ShowTab(PanelTab.Look);
            view.Model.Select("EnemyIcon");
            await Settle(window);
            Assert.Contains("MOLDURA", Texts());
            Assert.Contains("Cor da moldura", Texts());
            Assert.Contains("COR DO DESENHO", Texts());

            // Dado: os lados e o "▶ Rolar".
            panel.ShowTab(PanelTab.Piece);
            view.Model.Select("D20");
            await Settle(window);
            Assert.Contains("LADOS", Texts());
            Assert.Contains("d20", Texts());
            var d6 = Descendants(panel).OfType<ToggleButton>().First(b => b.IsVisible && Equals(b.Content, "d6"));
            d6.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            await Settle(window);
            Assert.Contains("\"sides\": 6", doc.Document.Text);
            Assert.Contains(Descendants(panel).OfType<Button>(), b => b.IsVisible && Equals(b.Content, "▶ Rolar"));

            // Botão: ícone antes do texto, na aba Aparência.
            panel.ShowTab(PanelTab.Look);
            view.Model.Select("Defend");
            await Settle(window);
            Assert.Contains("ÍCONE ANTES DO TEXTO", Texts());
            Assert.Contains("Sem ícone", Texts());

            // Meus estilos: guardar o estilo do Atacar e usar no Defender e no Fugir (de uma vez).
            view.Model.Select("Attack");
            await Settle(window);
            Assert.Contains("MEUS ESTILOS", Texts());
            dialogs.TextAnswer = "Botão de luta";
            Descendants(panel).OfType<Button>().First(b => b.IsVisible && Equals(b.Content, "＋  Guardar o estilo desta peça"))
                .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            await Settle(window);
            var saved = Assert.Single(MyStyles.Load());
            Assert.Equal("Botão de luta", saved.Title);
            Assert.Equal(ButtonStyle.Gradient, saved.Piece.Style);
            Assert.Contains("Botão de luta", Texts());
            SaveImage(window, "csharplab-meus-estilos.png");

            view.Model.SelectMany(["Defend", "Flee"]);
            await Settle(window);
            var chip = Descendants(panel).OfType<Border>().First(b => b.IsVisible && System.Windows.Automation.AutomationProperties.GetName(b) == "Estilo Botão de luta");
            chip.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
            await Settle(window);
            var layout = ScreenFile.Parse(doc.Document.Text).Layout!;
            foreach (var name in new[] { "Defend", "Flee" })
            {
                var piece = layout.Find(name)!;
                Assert.Equal((ButtonStyle.Gradient, GameEngine.Font.Retro, Corner.Square), (piece.Style!.Value, piece.Font!.Value, piece.Corner!.Value));
            }
            Assert.Equal("Defender", layout.Find("Defend")!.Text);   // o texto fica

            // Um estilo de Botão não aparece numa peça de outro tipo.
            view.Model.Select("D20");
            panel.ShowTab(PanelTab.Look);
            await Settle(window);
            Assert.DoesNotContain("Botão de luta", Texts());
        }
        finally
        {
            panel.ShowTab(PanelTab.Piece);
            foreach (var old in MyStyles.Load()) MyStyles.Remove(old.Title);
            window.Close();
        }
    });

    private static async Task Settle(Window window)
    {
        await Task.Delay(120);
        window.UpdateLayout();
        await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
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

    private static void SaveImage(Window window, string name)
    {
        var element = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(Path.GetTempPath(), name));
        encoder.Save(file);
    }
}
