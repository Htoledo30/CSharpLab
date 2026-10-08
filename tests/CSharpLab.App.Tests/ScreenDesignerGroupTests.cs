using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CSharpLab.Core.Files;
using CSharpLab.Core.Settings;
using CSharpLab.GameEngine;
using CSharpLab.Screens;
using CSharpLab.ViewModels;

namespace CSharpLab.App.Tests;

/// <summary>
/// A aba Tela com várias peças de uma vez: seleção múltipla, copiar e colar entre telas,
/// e a Lista (peças que entram e saem do cartão modelo).
/// </summary>
public sealed class ScreenDesignerGroupTests
{
    private static (DocumentViewModel Doc, ScreenDesignerModel Model) Open(string dir, string scene, string json)
    {
        var screens = Path.Combine(dir, "Screens");
        Directory.CreateDirectory(screens);
        var path = Path.Combine(screens, scene + ".json");
        File.WriteAllText(path, json);
        var doc = new DocumentViewModel(json, path, TextFileIO.Utf8NoBom, null);
        return (doc, new ScreenDesignerModel(doc));
    }

    private const string Village = """
        { "pieces": [
          { "type": "Box", "name": "StatusBar", "x": 16, "y": 12, "width": 928, "height": 72 },
          { "type": "Bar", "name": "Health", "x": 268, "y": 24, "width": 200, "height": 48, "text": "Vida", "value": 100, "max": 100 },
          { "type": "Text", "name": "Gold", "x": 836, "y": 22, "width": 100, "height": 26, "text": "💰 15" },
          { "type": "Text", "name": "Title", "x": 32, "y": 96, "width": 560, "height": 46, "text": "Vila" }
        ] }
        """;

    private const string Forest = """
        { "pieces": [
          { "type": "Text", "name": "Title", "x": 32, "y": 300, "width": 560, "height": 46, "text": "Floresta" }
        ] }
        """;

    [Fact]
    public void Varias_pecas_andam_apagam_e_duplicam_juntas() => Ui.Run(() =>
    {
        var (doc, model) = Open(Ui.NewFolder("jogo"), "Village", Village);
        model.Select("Health");
        model.ToggleSelect("Gold");
        Assert.Equal(["Health", "Gold"], model.SelectedNames);
        Assert.Null(model.SelectedName);               // o painel mostra "2 peças", não uma

        model.Nudge(10, 0);
        model.Nudge(10, 0);
        model.EndMerge();
        Assert.Equal((288d, 856d), (model.Layout!.Find("Health")!.X, model.Layout.Find("Gold")!.X));
        Assert.Equal(16, model.Layout.Find("StatusBar")!.X);   // a que não estava selecionada ficou
        model.Undo();
        Assert.Equal((268d, 836d), (model.Layout!.Find("Health")!.X, model.Layout.Find("Gold")!.X));   // um passo só

        var copy = model.Duplicate()!;
        Assert.Equal(["Health2", "Gold2"], model.SelectedNames);
        Assert.Equal(model.Layout!.Find("Health")!.X + 16, copy.X);

        model.Delete();
        Assert.Null(model.Layout!.Find("Health2"));
        Assert.Null(model.Layout.Find("Gold2"));
        Assert.Empty(model.SelectedNames);

        // Shift+clique de novo tira da seleção; Ctrl+A pega tudo.
        model.SelectMany(["Health", "Gold"]);
        model.ToggleSelect("Gold");
        Assert.Equal("Health", model.SelectedName);
        model.SelectAll();
        Assert.Equal(4, model.SelectedNames.Count);
        return Task.CompletedTask;
    });

    [Fact]
    public void Copiar_numa_tela_e_colar_em_outra_mantem_nome_e_lugar() => Ui.Run(() =>
    {
        var dir = Ui.NewFolder("jogo");
        var (_, village) = Open(dir, "Village", Village);
        var (forestDoc, forest) = Open(dir, "Forest", Forest);

        village.SelectMany(["StatusBar", "Health", "Gold", "Title"]);
        village.Copy();
        Assert.True(forest.CanPaste);   // o Ctrl+C vale para qualquer tela aberta

        forest.Paste();
        var layout = forest.Layout!;
        // Mesmo lugar e mesmos nomes: o mesmo ShowStatus() serve às duas telas.
        Assert.Equal((268d, 24d), (layout.Find("Health")!.X, layout.Find("Health")!.Y));
        Assert.Equal((836d, 22d), (layout.Find("Gold")!.X, layout.Find("Gold")!.Y));
        // "Title" já existia na Floresta: a cópia ganha outro nome e a original fica.
        Assert.Equal("Floresta", layout.Find("Title")!.Text);
        Assert.Equal("Vila", layout.Find("Title2")!.Text);
        Assert.Equal(["StatusBar", "Health", "Gold", "Title2"], forest.SelectedNames);
        Assert.Contains("\"name\": \"Health\"", forestDoc.Document.Text);

        // Colar de novo na mesma tela: o lugar já está ocupado, então vai um pouco para o lado.
        forest.Paste();
        Assert.Equal(268 + 16, forest.Layout!.Find("Health2")!.X);
        return Task.CompletedTask;
    });

    private const string Shop = """
        { "pieces": [
          { "type": "List", "name": "Weapons", "x": 40, "y": 100, "width": 600, "height": 260, "cardWidth": 180, "cardHeight": 240, "gap": 16 },
          { "type": "Text", "name": "Name", "list": "Weapons", "x": 10, "y": 10, "width": 160, "height": 30, "text": "Espada" },
          { "type": "Text", "name": "Title", "x": 40, "y": 30, "width": 400, "height": 50, "text": "Loja" }
        ] }
        """;

    [Fact]
    public void Peca_dentro_do_cartao_entra_na_lista_e_sai_ao_arrastar_para_fora() => Ui.Run(() =>
    {
        var (doc, model) = Open(Ui.NewFolder("jogo"), "Shop", Shop);
        var layout = model.Layout!;
        Assert.Equal(new Rect(50, 110, 160, 30), model.BoundsOf(layout.Find("Name")!));   // no palco: Lista + posição no cartão
        Assert.Equal(["Weapons", "Name", "Title"], model.DrawOrder().Select(p => p.Name));

        // Um botão solto dentro do cartão modelo vira parte dele (posição contada do canto do cartão).
        var buy = model.Add(PieceType.Button, new Point(130, 200))!;
        Assert.Equal("Weapons", buy.List);
        Assert.Equal((0d, 76d), (buy.X, buy.Y));
        Assert.Contains("\"list\": \"Weapons\"", doc.Document.Text);
        // Um texto largo demais para o cartão fica solto.
        Assert.Null(model.Add(PieceType.Text, new Point(130, 200))!.List);

        // Mover a Lista leva o cartão junto.
        model.SetBounds("Weapons", new Rect(100, 120, 600, 260));
        Assert.Equal(new Rect(110, 130, 160, 30), model.BoundsOf(model.Layout!.Find("Name")!));

        // Arrastar a peça para fora do cartão: volta a ser solta, no mesmo lugar do palco. E para dentro de novo.
        model.SetBounds("Name", new Rect(700, 400, 160, 30));
        var name = model.Layout!.Find("Name")!;
        Assert.Null(name.List);
        Assert.Equal((700d, 400d), (name.X, name.Y));
        model.SetBounds("Name", new Rect(110, 130, 160, 30));
        Assert.Equal(("Weapons", 10d, 10d), (model.Layout!.Find("Name")!.List, model.Layout.Find("Name")!.X, model.Layout.Find("Name")!.Y));

        // Copiar a Lista leva o cartão junto, para qualquer tela.
        var (_, other) = Open(Path.GetDirectoryName(Path.GetDirectoryName(doc.FilePath))!, "Inventory", "{ \"pieces\": [] }");
        model.Select("Weapons");
        model.Copy();
        other.Paste();
        Assert.Equal(["Weapons"], other.SelectedNames);
        Assert.Equal(["Name", "Button1"], other.Layout!.MembersOf("Weapons").Select(p => p.Name));
        Assert.Equal((10d, 10d), (other.Layout.Find("Name")!.X, other.Layout.Find("Name")!.Y));

        // Renomear a Lista: o cartão acompanha. Apagar: o cartão vai junto.
        Assert.Null(model.Rename("Items"));
        Assert.Equal(["Name", "Button1"], model.Layout!.MembersOf("Items").Select(p => p.Name));
        model.Delete();
        Assert.Null(model.Layout!.Find("Name"));
        Assert.Null(model.Layout.Find("Button1"));
        Assert.NotNull(model.Layout.Find("Title"));
        return Task.CompletedTask;
    });

    /// <summary>
    /// A aba Tela com uma Lista (cartão modelo e prévia) e o painel de cada tipo de peça.
    /// Fotos em %TEMP%\csharplab-aba-lista-*.png para conferir o visual.
    /// </summary>
    [Fact]
    public void Aba_Tela_desenha_lista_estilos_e_selecao_de_varias() => Ui.Run(async () =>
    {
        const string json = """
            { "pieces": [
              { "type": "Text", "name": "Title", "x": 40, "y": 24, "width": 600, "height": 60, "text": "Loja do Bartolo", "size": 34, "font": "Fantasy", "color": "Gold", "shadow": true },
              { "type": "List", "name": "Weapons", "x": 40, "y": 100, "width": 880, "height": 300, "cardWidth": 200, "cardHeight": 220, "gap": 20, "text": "Nada à venda." },
              { "type": "Box", "name": "CardBack", "list": "Weapons", "x": 0, "y": 0, "width": 200, "height": 220, "color": "Red", "shade": "Dark", "opacity": 40, "border": true },
              { "type": "Text", "name": "Icon", "list": "Weapons", "x": 0, "y": 10, "width": 200, "height": 70, "text": "🗡", "size": 44, "align": "Center", "color": "Red" },
              { "type": "Text", "name": "Name", "list": "Weapons", "x": 10, "y": 86, "width": 180, "height": 30, "text": "Espada", "bold": true, "align": "Center" },
              { "type": "Text", "name": "Price", "list": "Weapons", "x": 10, "y": 118, "width": 180, "height": 28, "text": "💰 30", "align": "Center", "color": "Gold" },
              { "type": "Button", "name": "Buy", "list": "Weapons", "x": 20, "y": 160, "width": 160, "height": 44, "text": "Comprar", "color": "Red" },
              { "type": "Messages", "name": "Log", "x": 40, "y": 420, "width": 560, "height": 100 },
              { "type": "Button", "name": "Back", "x": 760, "y": 460, "width": 160, "height": 52, "text": "↩ Voltar", "style": "Text" }
            ] }
            """;
        var (doc, _) = Open(Ui.NewFolder("jogo"), "Shop", json);
        using var vm = new MainViewModel(new AppSettings()) { Dialogs = new FakeDialogs(), Terminal = new FakeTerminal() };
        var view = new ScreenEditorView(doc, vm);
        var window = new Window { Content = view, Width = 1280, Height = 900, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Left = -10000, Top = -10000 };
        window.Show();
        try
        {
            foreach (var (shot, names) in new (string, string[])[]
                     {
                         ("caixa", ["CardBack"]),
                         ("texto", ["Title"]),
                         ("botao", ["Back"]),
                         ("lista", ["Weapons"]),
                         ("varias", ["Name", "Price", "Buy"]),
                     })
            {
                view.Model.SelectMany(names);
                await Task.Delay(200);
                window.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(view);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(Path.GetTempPath(), $"csharplab-aba-lista-{shot}.png"));
                encoder.Save(file);
            }
        }
        finally
        {
            window.Close();
            view.Detach();
        }
    });
}
