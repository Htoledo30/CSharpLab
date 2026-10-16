using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CSharpLab.Core.Files;
using CSharpLab.Core.Settings;
using CSharpLab.GameEngine;
using CSharpLab.Screens;
using CSharpLab.ViewModels;

namespace CSharpLab.App.Tests;

/// <summary>As peças novas no Estúdio: estão no painel de peças, nascem prontas e o painel mostra as opções de cada uma.</summary>
public sealed class NewPiecesStudioTests
{
    /// <summary>Fotos em %TEMP%\csharplab-estudio-pecas-novas.png e csharplab-painel-*.png.</summary>
    [Fact]
    public void Pecas_novas_no_estudio() => Ui.Run(async () =>
    {
        var dir = Ui.NewFolder("jogo");
        var path = Path.Combine(dir, "Screens", "Arcade.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = ScreenFile.Serialize(new ScreenLayout());
        File.WriteAllText(path, json);
        var doc = new DocumentViewModel(json, path, TextFileIO.Utf8NoBom, null);
        using var vm = new MainViewModel(new AppSettings()) { Dialogs = new FakeDialogs(), Terminal = new FakeTerminal() };
        var view = new ScreenEditorView(doc, vm);
        view.Properties.AllTabs = true;   // confere o painel inteiro, com as três abas juntas
        var window = new Window { Content = view, Width = 1400, Height = 1600, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Left = -10000, Top = -10000 };
        window.Show();
        try
        {
            await Settle(window);
            var palette = string.Join(" | ", Descendants(window).OfType<PiecePalette>().SelectMany(p => Descendants(p)).OfType<TextBlock>().Select(t => t.Text));
            foreach (var name in new[] { "Escolha", "Contador", "Roleta", "Grade" }) Assert.Contains(name, palette);

            var model = view.Model;
            model.Add(PieceType.Choice, new Point(260, 60));
            model.Add(PieceType.Counter, new Point(140, 140));
            model.Add(PieceType.Wheel, new Point(780, 200));
            model.Add(PieceType.Grid, new Point(200, 360));
            Assert.Contains("\"type\": \"Choice\", \"name\": \"Choice1\"", doc.Document.Text);
            Assert.Contains("\"options\": [\"Fácil\", \"Normal\", \"Difícil\"], \"selected\": \"Normal\"", doc.Document.Text);
            Assert.Contains("\"columns\": 3, \"rows\": 3", doc.Document.Text);

            string PanelText() => string.Join(" | ", Descendants(view.Properties).OfType<TextBlock>().Where(t => t.IsVisible).Select(t => t.Text));
            foreach (var (name, expected) in new (string, string[])[]
                     {
                         ("Choice1", ["OPÇÕES", "Começa escolhida", "Opções por linha", "COR DA ESCOLHIDA", "CANTOS", "LETRA"]),
                         ("Counter1", ["NÚMEROS", "Começa em", "Passo", "Mínimo", "Máximo", "COR DOS BOTÕES"]),
                         ("Wheel1", ["FATIAS", "repita a linha", "COR DAS FATIAS", ".Spin()"]),
                         ("Grid1", ["CASAS", "Colunas", "Linhas", "COR DAS CASAS", "Cell(x, y)"]),
                     })
            {
                model.Select(name);
                await Settle(window);
                var text = PanelText();
                foreach (var part in expected) Assert.Contains(part, text);
                Save(view.Properties, $"csharplab-painel-{name.ToLowerInvariant()}.png");
            }

            // Escrever as opções no painel (uma por linha) vai para o arquivo, sem linhas vazias.
            model.Edit("Choice1", p => p.Options = "Guerreiro\n\nMago\n Arqueiro ".Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList());
            Assert.Contains("\"options\": [\"Guerreiro\", \"Mago\", \"Arqueiro\"]", doc.Document.Text);
            Assert.DoesNotContain("\"selected\"", doc.Document.Text.Split('\n').First(l => l.Contains("Choice1")));   // "Normal" não está mais nas opções

            model.Select(null);
            await Settle(window);
            Save(view, "csharplab-estudio-pecas-novas.png");
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
