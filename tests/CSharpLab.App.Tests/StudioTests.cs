using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CSharpLab.Core.Files;
using CSharpLab.Core.Projects;
using CSharpLab.Core.Settings;
using CSharpLab.GameEngine;
using CSharpLab.Screens;
using CSharpLab.ViewModels;
using CSharpLab.Views;

namespace CSharpLab.App.Tests;

/// <summary>O Estúdio do jogo: cenas, camadas, tela e código lado a lado, sem abas.</summary>
public sealed class StudioTests
{
    private const string Program =
        "var game = new Game(\"T\");\n\n" +
        "game.Scene(\"Fight\", () =>\n{\n    game.Find(\"Continue\").OnClick(() => game.GoTo(\"Shop\"));\n});\n\n" +
        "game.Scene(\"Shop\", () => game.Write(\"Loja\"));\n\n" +
        "game.Start(\"Fight\");\n";

    /// <summary>A imagem do Estúdio fica em %TEMP%\csharplab-estudio.png para conferir o visual.</summary>
    [Fact]
    public void Estudio_mostra_cenas_camadas_tela_e_codigo_juntos() => Ui.Run(async () =>
    {
        var dialogs = new FakeDialogs();
        using var vm = new MainViewModel(new AppSettings()) { Dialogs = dialogs, Terminal = new FakeTerminal() };
        await vm.InitializeAsync();
        var created = ProjectCreator.CreateGameProject(Ui.NewFolder("jogos"), "Torre", Program);
        Directory.CreateDirectory(Path.Combine(created.Directory, "Screens"));
        File.WriteAllText(GameScreens.PathOf(created.Directory, "Fight"), ScreenFile.Serialize(ScreenLayout.CreateDefault("Fight")));
        File.WriteAllText(GameScreens.PathOf(created.Directory, "Cave"), ScreenFile.Serialize(ScreenLayout.CreateDefault("Cave")));
        await vm.OpenFolderAsync(created.Directory, created.ProjectPath, promptForUnsaved: false);
        await Ui.WaitUntil(() => vm.Projects.Count > 0, 60_000, "projetos");

        var studio = new StudioView();
        studio.Bind(vm);
        var window = new Window { Content = studio, Width = 1600, Height = 860, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Left = -10000, Top = -10000 };
        window.Show();
        try
        {
            vm.OpenStudio();
            studio.Open(null);
            await Settle(window);

            // Começa na cena do game.Start, com a lista de todas as cenas.
            Assert.Equal("Fight", studio.CurrentScene);
            var rows = studio.SceneRows.ToList();
            Assert.Equal(4, rows.Count);
            Assert.Contains("Mapa do jogo", rows[0]);
            Assert.Contains(rows, r => r.Contains("Fight") && r.Contains("começa aqui"));
            Assert.Contains(rows, r => r.Contains("Shop") && r.Contains("só código"));
            Assert.Contains(rows, r => r.Contains("Cave") && r.Contains("falta no código"));

            // Tela no meio, camadas da frente para trás, e o código já no game.Scene da cena.
            Assert.NotNull(studio.Screen);
            Assert.Equal(["Continue", "Title"], studio.Layers.Names);
            Assert.Equal(created.ProgramPath, studio.Code?.Doc.FilePath);
            Assert.Equal(Program.IndexOf("game.Scene(\"Fight\"", StringComparison.Ordinal), studio.SceneSpan?.Start);
            Assert.False(studio.ShowsMissingScene);

            // Peça escolhida na tela: o código marca onde ela é usada.
            studio.Screen!.Model.Select("Continue");
            Assert.Equal(1, studio.MarkCount);
            studio.Screen.Model.Select("Title");
            Assert.Equal(0, studio.MarkCount);

            // A tela vive no Estúdio: sem aba própria.
            var screenDoc = vm.FindDocument(GameScreens.PathOf(created.Directory, "Fight"))!;
            Assert.True(screenDoc.LivesInStudio);
            Assert.False(vm.FindDocument(created.ProgramPath)!.LivesInStudio);
            SaveImage(window, "csharplab-estudio.png");

            // Camadas: mandar o título para a frente muda a ordem no arquivo (um passo no desfazer).
            Assert.True(studio.Screen!.Model.MoveLayer("Title", "Continue", inFront: true));
            Assert.Equal(["Title", "Continue"], studio.Layers.Names);
            studio.Screen.Model.Undo();
            Assert.Equal(["Continue", "Title"], studio.Layers.Names);

            // Cena só com código: o cartão para desenhar a tela, e o código vai para ela.
            studio.ShowScene("Shop");
            await Settle(window);
            Assert.True(studio.ShowsNoScreen);
            Assert.Empty(studio.Layers.Names);
            Assert.Equal(Program.IndexOf("game.Scene(\"Shop\"", StringComparison.Ordinal), studio.SceneSpan?.Start);

            // Tela sem cena no código: o aviso oferece escrever a cena.
            studio.ShowScene("Cave");
            await Settle(window);
            Assert.False(studio.ShowsNoScreen);
            Assert.True(studio.ShowsMissingScene);
            Assert.Null(studio.SceneSpan);
            vm.GoToSceneCode(created.Directory, "Cave");   // FakeDialogs confirma
            await Settle(window);
            Assert.False(studio.ShowsMissingScene);
            Assert.Contains("game.Scene(\"Cave\"", studio.Code!.Document.Text);
            Assert.True(vm.IsStudioOpen);

            // O cursor do código entra em outra cena: a tela do meio vai junto.
            var code = studio.Code!;
            code.CaretOffset = code.Document.Text.IndexOf("game.Find(\"Continue\")", StringComparison.Ordinal);
            await Ui.WaitUntil(() => studio.CurrentScene == "Fight", 5000, "a tela seguir o cursor");
            Assert.NotNull(studio.Screen);
            // ...e a peça do game.Find onde o cursor está fica selecionada na tela.
            await Ui.WaitUntil(() => studio.Screen!.Model.SelectedName == "Continue", 5000, "a peça do cursor ser selecionada");
            Assert.Equal(1, studio.MarkCount);

            // Mapa: começa na cena do game.Start; cada game.GoTo vira uma seta; quem ninguém chama fica no fim.
            studio.ShowMap();
            await Settle(window);
            Assert.True(studio.ShowsMap);
            Assert.Equal([("Fight", 0), ("Shop", 1), ("Cave", 2)], studio.Map.Placed);
            Assert.Equal([("Fight", "Shop")], studio.Map.Arrows);
            SaveImage(window, "csharplab-mapa.png");
            studio.ShowScene("Shop");
            Assert.False(studio.ShowsMap);

            // Abrir um arquivo de fora do jogo fecha o Estúdio (para ele aparecer no editor com abas).
            var outside = Path.Combine(Ui.NewFolder("solto"), "Notas.cs");
            File.WriteAllText(outside, "// notas\n");
            vm.OpenFile(outside);
            Assert.False(vm.IsStudioOpen);
        }
        finally
        {
            window.Close();
        }
    }, timeoutSeconds: 180);

    /// <summary>O mapa de A Coroa Perdida (11 cenas). A imagem fica em %TEMP%\csharplab-mapa-coroa.png.</summary>
    [Fact]
    public void Mapa_do_exemplo_grande_liga_todas_as_cenas() => Ui.Run(async () =>
    {
        using var vm = new MainViewModel(new AppSettings()) { Dialogs = new FakeDialogs(), Terminal = new FakeTerminal() };
        await vm.InitializeAsync();
        var created = Examples.Create(Examples.All.First(e => e.Id == "RpgAdventure"), Ui.NewFolder("exemplos"));
        await vm.OpenFolderAsync(created.Directory, created.ProjectPath, promptForUnsaved: false);
        await Ui.WaitUntil(() => vm.Projects.Count > 0, 60_000, "projetos");

        var studio = new StudioView();
        studio.Bind(vm);
        var window = new Window { Content = studio, Width = 1700, Height = 1000, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Left = -10000, Top = -10000 };
        window.Show();
        try
        {
            vm.OpenStudio();
            studio.Open(null);
            studio.ShowMap();
            await Settle(window);
            Assert.Equal(11, studio.Map.Placed.Count);
            Assert.Equal(0, studio.Map.Placed[0].Column);
            Assert.True(studio.Map.Arrows.Count >= 10);
            SaveImage(window, "csharplab-mapa-coroa.png");
        }
        finally
        {
            window.Close();
        }
    }, timeoutSeconds: 180);

    [Fact]
    public void Camadas_so_trocam_dentro_do_mesmo_grupo_e_andam_um_degrau() => Ui.Run(() =>
    {
        var layout = new ScreenLayout();
        layout.Pieces.Add(Piece.CreateDefault(PieceType.Box, "Back", 0, 0));
        var list = Piece.CreateDefault(PieceType.List, "Shop", 100, 100);
        layout.Pieces.Add(list);
        var name = Piece.CreateDefault(PieceType.Text, "ItemName", 4, 4);
        name.List = "Shop";
        var price = Piece.CreateDefault(PieceType.Text, "ItemPrice", 4, 40);
        price.List = "Shop";
        layout.Pieces.Add(name);
        layout.Pieces.Add(price);
        layout.Pieces.Add(Piece.CreateDefault(PieceType.Text, "Title", 0, 0));
        var dir = Ui.NewFolder("camadas");
        var path = Path.Combine(dir, "Screens", "Shop.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = ScreenFile.Serialize(layout);
        var doc = new DocumentViewModel(json, path, TextFileIO.Utf8NoBom, null);
        using var model = new ScreenDesignerModel(doc);

        // Da frente para trás; as peças do cartão logo acima da Lista, um nível para dentro.
        Assert.Equal(["Title", "ItemPrice", "ItemName", "Shop", "Back"], model.Layers().Select(l => l.Piece.Name));
        Assert.Equal([0, 1, 1, 0, 0], model.Layers().Select(l => l.Depth));

        // Peça solta não entra no meio do cartão (e vice-versa).
        Assert.False(model.MoveLayer("Title", "ItemName", inFront: true));
        Assert.False(model.MoveLayer("ItemName", "Back", inFront: false));

        Assert.True(model.MoveLayer("Back", "Title", inFront: true));
        Assert.Equal(["Back", "Title", "ItemPrice", "ItemName", "Shop"], model.Layers().Select(l => l.Piece.Name));
        Assert.True(model.MoveLayerStep("ItemName", +1));
        Assert.Equal(["Back", "Title", "ItemName", "ItemPrice", "Shop"], model.Layers().Select(l => l.Piece.Name));
        Assert.False(model.MoveLayerStep("ItemName", +1));   // já é a da frente no cartão
        Assert.True(model.MoveLayerStep("Back", -1));
        Assert.Equal(["Title", "Back", "ItemName", "ItemPrice", "Shop"], model.Layers().Select(l => l.Piece.Name));

        // Cada mudança é um passo só no desfazer.
        model.Undo();
        model.Undo();
        model.Undo();
        Assert.Equal(json, doc.Document.Text);
        return Task.CompletedTask;
    });

    private static async Task Settle(Window window)
    {
        await Task.Delay(150);
        window.UpdateLayout();
        await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
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
