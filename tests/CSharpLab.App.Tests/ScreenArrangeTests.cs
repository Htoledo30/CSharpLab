using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CSharpLab.Core.Files;
using CSharpLab.Core.Settings;
using CSharpLab.GameEngine;
using CSharpLab.Screens;
using CSharpLab.ViewModels;
using Color = CSharpLab.GameEngine.Color;

namespace CSharpLab.App.Tests;

/// <summary>Arrumar a tela no Estúdio: alinhar, espaço igual, mesmo tamanho, copiar estilo e soltar imagens.</summary>
public sealed class ScreenArrangeTests
{
    private const string Menu = """
        { "pieces": [
          { "type": "Button", "name": "Attack", "x": 40, "y": 100, "width": 180, "height": 52, "text": "Atacar", "color": "Red", "font": "Retro", "size": 22, "corner": "Square", "style": "Outline", "textShadow": true },
          { "type": "Button", "name": "Defend", "x": 75, "y": 180, "width": 140, "height": 40, "text": "Defender" },
          { "type": "Button", "name": "Flee", "x": 52, "y": 400, "width": 200, "height": 60, "text": "Fugir" },
          { "type": "Text", "name": "Title", "x": 300, "y": 20, "width": 300, "height": 50, "text": "Luta" },
          { "type": "Box", "name": "Panel", "x": 600, "y": 300, "width": 200, "height": 100 },
          { "type": "List", "name": "Bag", "x": 400, "y": 200, "width": 400, "height": 300, "cardWidth": 200, "cardHeight": 100 },
          { "type": "Text", "name": "ItemName", "list": "Bag", "x": 10, "y": 10, "width": 100, "height": 30, "text": "Item" }
        ] }
        """;

    private static (DocumentViewModel Doc, ScreenDesignerModel Model) Open(string dir, string scene = "Fight", string json = Menu)
    {
        var screens = Path.Combine(dir, "Screens");
        Directory.CreateDirectory(screens);
        var path = Path.Combine(screens, scene + ".json");
        File.WriteAllText(path, json);
        var doc = new DocumentViewModel(json, path, TextFileIO.Utf8NoBom, null);
        return (doc, new ScreenDesignerModel(doc));
    }

    private static Rect At(ScreenDesignerModel model, string name) => model.BoundsOf(model.Layout!.Find(name)!);

    [Fact]
    public void Alinhar_varias_e_uma_so_na_tela() => Ui.Run(() =>
    {
        var (_, model) = Open(Ui.NewFolder("jogo"));
        model.SelectMany(["Attack", "Defend", "Flee"]);
        Assert.True(model.Align(AlignEdge.Left));
        Assert.All(new[] { "Attack", "Defend", "Flee" }, n => Assert.Equal(40, At(model, n).X));

        // Pelo meio: o meio de todas fica no meio do grupo (40 até 240 → 140).
        Assert.True(model.Align(AlignEdge.CenterX));
        Assert.All(new[] { "Attack", "Defend", "Flee" }, n => Assert.Equal(140, At(model, n).X + At(model, n).Width / 2));
        Assert.False(model.Align(AlignEdge.CenterX));   // já estavam: nada muda

        model.Undo();
        Assert.All(new[] { "Attack", "Defend", "Flee" }, n => Assert.Equal(40, At(model, n).X));   // um passo só

        // Uma peça só: alinha com a tela.
        model.Select("Title");
        model.Align(AlignEdge.CenterX);
        Assert.Equal((ScreenLayout.Width - 300) / 2, At(model, "Title").X);
        model.Align(AlignEdge.Bottom);
        Assert.Equal(ScreenLayout.Height - 50, At(model, "Title").Y);

        // Uma peça de cartão: alinha com o cartão, e continua no cartão.
        model.Select("ItemName");
        model.Align(AlignEdge.CenterX);
        var item = model.Layout!.Find("ItemName")!;
        Assert.Equal(("Bag", 50d), (item.List, item.X));
        return Task.CompletedTask;
    });

    [Fact]
    public void Espaco_igual_e_mesmo_tamanho() => Ui.Run(() =>
    {
        var (_, model) = Open(Ui.NewFolder("jogo"));
        model.SelectMany(["Flee", "Attack", "Defend"]);   // a ordem da seleção não importa: vale a da tela
        Assert.True(model.Distribute(horizontal: false));
        // De 100 até 460, com 52 + 40 + 60 de altura: sobram 208, 104 entre cada uma.
        Assert.Equal((100d, 256d, 400d), (At(model, "Attack").Y, At(model, "Defend").Y, At(model, "Flee").Y));

        model.SelectMany(["Attack", "Defend"]);
        Assert.False(model.Distribute(horizontal: true));   // precisa de 3

        // Mesmo tamanho: o da primeira selecionada.
        model.SelectMany(["Defend", "Attack", "Flee"]);
        Assert.True(model.SameSize(width: true, height: false));
        Assert.Equal((140d, 140d, 140d), (At(model, "Attack").Width, At(model, "Defend").Width, At(model, "Flee").Width));
        Assert.Equal(60, At(model, "Flee").Height);
        model.SameSize(width: false, height: true);
        Assert.Equal(40, At(model, "Flee").Height);
        return Task.CompletedTask;
    });

    [Fact]
    public void Copiar_estilo_leva_a_aparencia_e_nao_o_texto_nem_o_lugar() => Ui.Run(() =>
    {
        var dir = Ui.NewFolder("jogo");
        var (doc, model) = Open(dir);
        model.Select("Attack");
        Assert.True(model.CopyStyle());
        model.SelectMany(["Defend", "Flee"]);
        Assert.Equal(2, model.PasteStyle());
        var defend = model.Layout!.Find("Defend")!;
        Assert.Equal((Color.Red, Font.Retro, 22d, Corner.Square, ButtonStyle.Outline, true),
            (defend.Color, defend.Font, defend.Size, defend.Corner, defend.Style, defend.TextShadow));
        Assert.Equal(("Defend", "Defender", 75d, 140d), (defend.Name, defend.Text, defend.X, defend.Width));
        model.Undo();
        Assert.Null(model.Layout!.Find("Flee")!.Color);   // as duas voltam num passo só
        Assert.Null(model.Layout.Find("Defend")!.Color);

        // Do botão para outros tipos: o texto ganha a letra (a cor da letra do botão vira a cor do Texto);
        // a caixa ganha o preenchimento e os cantos.
        model.Edit("Attack", p => p.TextColor = Color.Gold);
        model.Select("Attack");
        model.CopyStyle();
        model.SelectMany(["Title", "Panel"]);
        model.PasteStyle();
        var title = model.Layout!.Find("Title")!;
        Assert.Equal((Font.Retro, 22d, Color.Gold, true, (bool?)null), (title.Font, title.Size, title.Color, title.Shadow, title.Bold));
        var panel = model.Layout.Find("Panel")!;
        Assert.Equal((Color.Red, Corner.Square), (panel.Color, panel.Corner));

        // O estilo copiado vale em outra tela.
        var (_, other) = Open(dir, "Shop", """{ "pieces": [ { "type": "Button", "name": "Buy", "x": 10, "y": 10, "width": 120, "height": 40, "text": "Comprar" } ] }""");
        other.Select("Buy");
        Assert.True(other.HasStyle);
        other.PasteStyle();
        Assert.Equal(Font.Retro, other.Layout!.Find("Buy")!.Font);
        Assert.Contains("\"font\": \"Retro\"", doc.Document.Text);
        return Task.CompletedTask;
    });

    [Fact]
    public void Imagens_soltas_vao_para_Assets_com_o_formato_delas() => Ui.Run(() =>
    {
        var dir = Ui.NewFolder("jogo");
        var (_, model) = Open(dir);
        var downloads = Ui.NewFolder("downloads");
        var wide = Png(Path.Combine(downloads, "dragao.png"), 800, 400);
        var small = Png(Path.Combine(downloads, "moeda.png"), 64, 64);

        Assert.Equal(new Size(800, 400), ScreenDesignerModel.ImageSize(wide));
        Assert.Null(ScreenDesignerModel.ImageSize(Path.Combine(downloads, "nada.png")));
        Assert.True(ScreenDesignerModel.IsImageFile("x.JPG"));
        Assert.False(ScreenDesignerModel.IsImageFile("notas.txt"));

        var images = new[] { wide, small }.Select(f => (model.ImportImage(f), ScreenDesignerModel.ImageSize(f))).ToList();
        var added = model.AddImages(images, new Point(480, 270));
        Assert.Equal(2, added.Count);
        Assert.Equal(("dragao.png", 320d, 160d), (added[0].Image, added[0].Width, added[0].Height));   // grande: reduzida sem distorcer
        Assert.Equal(("moeda.png", 64d, 64d), (added[1].Image, added[1].Width, added[1].Height));     // pequena: do tamanho dela
        Assert.Equal(added.Select(p => p.Name), model.SelectedNames);
        Assert.True(File.Exists(Path.Combine(dir, "Assets", "dragao.png")));

        // Soltar a mesma imagem de novo não cria "dragao-2.png"; uma diferente com o mesmo nome, sim.
        Assert.Equal("dragao.png", model.ImportImage(wide));
        var other = Png(Path.Combine(Ui.NewFolder("outra"), "dragao.png"), 10, 10);
        Assert.Equal("dragao-2.png", model.ImportImage(other));
        // Uma imagem que já está numa pasta dentro de Assets fica com o nome que a tela entende.
        var nested = Png(Path.Combine(dir, "Assets", "enemies", "orc.png"), 20, 20);
        Assert.Equal("enemies/orc.png", model.ImportImage(nested));

        model.Undo();
        Assert.DoesNotContain(model.Layout!.Pieces, p => p.Type == PieceType.Image);   // as duas num passo só
        return Task.CompletedTask;
    });

    /// <summary>O painel com várias peças mostra ALINHAR e ESPAÇO E TAMANHO; com uma, APARÊNCIA e alinhar na tela.</summary>
    [Fact]
    public void Painel_alinha_e_copia_estilo() => Ui.Run(async () =>
    {
        var dir = Path.Combine(Ui.NewFolder("jogo"), "Screens");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "Fight.json");
        File.WriteAllText(path, Menu);
        var doc = new DocumentViewModel(Menu, path, TextFileIO.Utf8NoBom, null);
        using var vm = new MainViewModel(new AppSettings()) { Dialogs = new FakeDialogs(), Terminal = new FakeTerminal() };
        var view = new ScreenEditorView(doc, vm);
        view.Properties.AllTabs = true;   // confere o painel inteiro, com as três abas juntas
        var window = new Window { Content = view, Width = 1280, Height = 1200, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Left = -10000, Top = -10000 };
        window.Show();
        try
        {
            List<string> Texts() => Descendants(view.Properties).OfType<TextBlock>().Where(t => t.IsVisible).Select(t => t.Text).ToList();
            Button Action(string text) => Descendants(view.Properties).OfType<Button>().First(b => b.IsVisible && Equals(b.Content, text));

            view.Model.SelectMany(["Attack", "Defend", "Flee"]);
            await Task.Delay(30);
            window.UpdateLayout();
            Assert.Contains("ALINHAR", Texts());
            Assert.Contains("ESPAÇO E TAMANHO", Texts());
            Action("⇤  Esquerda").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.All(new[] { "Attack", "Defend", "Flee" }, n => Assert.Equal(40, view.Model.Layout!.Find(n)!.X));
            Assert.True(Action("↕  Espaço igual").IsEnabled);
            Photo(view, "csharplab-alinhar-painel.png");

            view.Model.Select("Attack");
            await Task.Delay(30);
            window.UpdateLayout();
            Assert.Contains("APARÊNCIA", Texts());
            Action("Copiar estilo").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Contains(Texts(), t => t.StartsWith("Estilo copiado: Attack", StringComparison.Ordinal));
            Action("Centro  ↔").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal((ScreenLayout.Width - 180) / 2, view.Model.Layout!.Find("Attack")!.X);

            view.Model.Select("Defend");
            await Task.Delay(30);
            window.UpdateLayout();
            Action("Colar estilo").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal(Font.Retro, view.Model.Layout!.Find("Defend")!.Font);
            await Task.Delay(30);
            window.UpdateLayout();
            // O painel mostra o estilo novo na hora (o Contorno marcado, não o Cheio).
            Assert.True(Descendants(view.Properties).OfType<ToggleButton>().Any(b => b.IsVisible && b.IsChecked == true && Contains(b, "Contorno")));
            Photo(view, "csharplab-estilo-painel.png");
        }
        finally
        {
            window.Close();
            view.Detach();
        }
    });

    private static bool Contains(DependencyObject root, string text) =>
        Descendants(root).OfType<TextBlock>().Any(t => t.Text == text) || root is ContentControl { Content: string s } && s == text;

    private static string Png(string path, int width, int height)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var pixels = new byte[width * height * 4];
        Array.Fill(pixels, (byte)0x80);
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
        return path;
    }

    private static void Photo(FrameworkElement view, string name)
    {
        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(Path.GetTempPath(), name));
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
