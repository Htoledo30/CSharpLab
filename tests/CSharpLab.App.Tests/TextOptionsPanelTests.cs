using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CSharpLab.Core.Files;
using CSharpLab.Core.Settings;
using CSharpLab.GameEngine;
using CSharpLab.Screens;
using CSharpLab.ViewModels;

namespace CSharpLab.App.Tests;

/// <summary>O painel mostra a mesma seção LETRA (completa) em toda peça que tem texto.</summary>
public sealed class TextOptionsPanelTests
{
    private const string Json = """
        { "pieces": [
          { "type": "Text", "name": "Title", "x": 40, "y": 20, "width": 400, "height": 50, "text": "Título" },
          { "type": "Button", "name": "Attack", "x": 40, "y": 90, "width": 200, "height": 52, "text": "Atacar" },
          { "type": "Bar", "name": "Health", "x": 40, "y": 160, "width": 300, "height": 44, "text": "Vida" },
          { "type": "Input", "name": "Answer", "x": 40, "y": 220, "width": 400, "height": 90, "text": "Senha?" },
          { "type": "Messages", "name": "Log", "x": 500, "y": 20, "width": 400, "height": 200 },
          { "type": "List", "name": "Bag", "x": 500, "y": 240, "width": 400, "height": 200, "text": "Vazia" },
          { "type": "Box", "name": "Panel", "x": 40, "y": 340, "width": 300, "height": 100 }
        ] }
        """;

    [Fact]
    public void Toda_peca_com_texto_tem_a_secao_LETRA_completa() => Ui.Run(async () =>
    {
        var dir = Path.Combine(Ui.NewFolder("jogo"), "Screens");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "Room.json");
        File.WriteAllText(path, Json);
        var doc = new DocumentViewModel(Json, path, TextFileIO.Utf8NoBom, null);
        using var vm = new MainViewModel(new AppSettings()) { Dialogs = new FakeDialogs(), Terminal = new FakeTerminal() };
        var view = new ScreenEditorView(doc, vm);
        var window = new Window { Content = view, Width = 1280, Height = 1400, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Left = -10000, Top = -10000 };
        window.Show();
        try
        {
            List<string> Texts() => Descendants(view.Properties).OfType<System.Windows.Controls.TextBlock>()
                .Where(t => t.IsVisible).Select(t => t.Text).ToList();
            ToggleButton Toggle(string text) => Descendants(view.Properties).OfType<ToggleButton>()
                .First(b => b.IsVisible && Equals(b.Content, text));

            foreach (var (name, section) in new[]
                     {
                         ("Title", "LETRA"), ("Attack", "LETRA"), ("Health", "LETRA (NOME E NÚMERO)"),
                         ("Answer", "LETRA (PERGUNTA)"), ("Log", "LETRA"), ("Bag", "LETRA (QUANDO VAZIA)"),
                     })
            {
                view.Model.Select(name);
                await Task.Delay(30);
                window.UpdateLayout();
                var texts = Texts();
                Assert.Contains(section, texts);
                Assert.Contains("Sombra nas letras", texts);
                // A cor da letra: no Texto é a seção COR DA LETRA; nas outras, dentro da seção LETRA.
                Assert.Contains(name == "Title" ? "COR DA LETRA" : "Cor da letra", texts);
                Assert.NotNull(Toggle("B"));
                Assert.NotNull(Toggle("I"));
            }

            // Uma caixa não tem letras.
            view.Model.Select("Panel");
            window.UpdateLayout();
            Assert.DoesNotContain("Sombra nas letras", Texts());

            // Itálico numa barra vai para o arquivo; o negrito dela começa ligado e pode ser tirado.
            view.Model.Select("Health");
            window.UpdateLayout();
            Assert.True(Toggle("B").IsChecked);
            Toggle("I").IsChecked = true;   // como o clique do mouse: liga e avisa
            Toggle("I").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Toggle("B").IsChecked = false;
            Toggle("B").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            var health = view.Model.Layout!.Find("Health")!;
            Assert.Equal((true, false), (health.Italic == true, health.IsBold));
            Assert.Contains("\"italic\": true", doc.Document.Text);
            Assert.Contains("\"bold\": false", doc.Document.Text);

            view.Model.Select("Attack");
            await Task.Delay(30);
            window.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(view);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(Path.GetTempPath(), "csharplab-letra-painel.png"));
            encoder.Save(file);
        }
        finally
        {
            window.Close();
            view.Detach();
        }
    });

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
