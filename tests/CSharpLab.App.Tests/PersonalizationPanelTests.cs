using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CSharpLab.Core.Files;
using CSharpLab.Core.Settings;
using CSharpLab.GameEngine;
using CSharpLab.Screens;
using CSharpLab.ViewModels;

namespace CSharpLab.App.Tests;

/// <summary>O painel de propriedades com as opções novas: cor própria, estilos de botão e de barra, fontes.</summary>
public sealed class PersonalizationPanelTests
{
    private const string Json = """
        { "pieces": [
          { "type": "Button", "name": "Attack", "x": 40, "y": 460, "width": 170, "height": 52, "text": "Atacar", "color": "Red", "style": "Gradient" },
          { "type": "Bar", "name": "Mana", "x": 640, "y": 40, "width": 276, "height": 44, "text": "Mana", "value": 40, "max": 100, "color": "#3FA9F5", "barStyle": "Shine" }
        ] }
        """;

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var inner in Descendants(child)) yield return inner;
        }
    }

    /// <summary>As imagens ficam em %TEMP%\csharplab-painel-botao.png e csharplab-painel-barra.png.</summary>
    [Fact]
    public void Painel_mostra_estilos_cores_e_fontes_e_muda_a_tela() => Ui.Run(async () =>
    {
        var dir = Ui.NewFolder("jogo");
        var path = Path.Combine(dir, "Screens", "Fight.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Json);
        var doc = new DocumentViewModel(Json, path, TextFileIO.Utf8NoBom, null);
        using var vm = new MainViewModel(new AppSettings()) { Dialogs = new FakeDialogs(), Terminal = new FakeTerminal() };
        var view = new ScreenEditorView(doc, vm);
        var window = new Window { Content = view, Width = 1280, Height = 2000, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Left = -10000, Top = -10000 };
        window.Show();
        try
        {
            string PanelText() => string.Join(" | ", Descendants(view.Properties).OfType<System.Windows.Controls.TextBlock>().Where(t => t.IsVisible).Select(t => t.Text));

            view.Model.Select("Attack");
            await Settle(window);
            var text = PanelText();
            foreach (var expected in new[] { "ESTILO", "Degradê", "Suave", "Contorno", "CANTOS", "Pílula", "Sombra embaixo", "COR DO BOTÃO", "LETRA", "Cor da letra", "Sombra nas letras", "Forte", "Retrô", "Técnica" })
                Assert.Contains(expected, text);
            Save(view.Properties, "csharplab-painel-botao.png");

            view.Model.Select("Mana");
            await Settle(window);
            text = PanelText();
            foreach (var expected in new[] { "ESTILO DA BARRA", "Lisa", "Brilhante", "Em blocos", "NOME E NÚMERO", "Dentro", "COR DA BARRA" })
                Assert.Contains(expected, text);
            Save(view.Properties, "csharplab-painel-barra.png");

            // Mudar pelo modelo (como o painel faz) grava no arquivo.
            view.Model.Edit("Mana", p => p.BarText = BarText.Inside);
            view.Model.Edit("Attack", p => p.TextColor = GameEngine.Color.Hex("#FFD21A"));
            Assert.Contains("\"barText\": \"Inside\"", doc.Document.Text);
            Assert.Contains("\"textColor\": \"#FFD21A\"", doc.Document.Text);
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

    /// <summary>Desenha a janela toda e recorta o painel (desenhar só o painel sai em branco, fora do lugar).</summary>
    private static void Save(FrameworkElement element, string name)
    {
        var root = (FrameworkElement)Window.GetWindow(element)!.Content;
        var bitmap = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var at = element.TranslatePoint(new Point(0, 0), root);
        var crop = new CroppedBitmap(bitmap, new Int32Rect((int)at.X, (int)at.Y,
            (int)Math.Min(element.ActualWidth, root.ActualWidth - at.X), (int)Math.Min(element.ActualHeight, root.ActualHeight - at.Y)));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(crop));
        using var file = File.Create(Path.Combine(Path.GetTempPath(), name));
        encoder.Save(file);
    }
}
