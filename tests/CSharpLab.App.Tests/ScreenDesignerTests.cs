using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CSharpLab.Core.Files;
using CSharpLab.Core.Projects;
using CSharpLab.Core.Settings;
using CSharpLab.GameEngine;
using CSharpLab.Screens;
using CSharpLab.ViewModels;

namespace CSharpLab.App.Tests;

/// <summary>A aba Tela: cada edição grava o JSON no documento, com desfazer de um passo por ação.</summary>
public sealed class ScreenDesignerTests
{
    private static (DocumentViewModel Doc, ScreenDesignerModel Model, string Dir) Open(string? json = null)
    {
        var dir = Ui.NewFolder("jogo");
        var screens = Path.Combine(dir, "Screens");
        Directory.CreateDirectory(screens);
        var path = Path.Combine(screens, "Fight.json");
        json ??= ScreenFile.Serialize(ScreenLayout.CreateDefault("Fight"));
        File.WriteAllText(path, json);
        var doc = new DocumentViewModel(json, path, TextFileIO.Utf8NoBom, null);
        return (doc, new ScreenDesignerModel(doc), dir);
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

    /// <summary>
    /// Tema do jogo: o painel da tela mostra os quatro, escolher um grava o GameStyle.json e o palco muda na hora.
    /// A aba com o tema Fantasia fica em %TEMP%\csharplab-aba-tema.png para conferir o visual.
    /// </summary>
    [Fact]
    public void Escolher_tema_grava_o_arquivo_e_o_palco_muda() => Ui.Run(async () =>
    {
        var (doc, _, dir) = Open();
        using var vm = new MainViewModel(new AppSettings()) { Dialogs = new FakeDialogs(), Terminal = new FakeTerminal() };
        var view = new ScreenEditorView(doc, vm);
        var window = new Window { Content = view, Width = 1280, Height = 760, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Left = -10000, Top = -10000 };
        window.Show();
        try
        {
            await Task.Delay(200);
            window.UpdateLayout();
            string PanelText() => string.Join(" | ", Descendants(view.Properties).OfType<System.Windows.Controls.TextBlock>().Where(t => t.IsVisible).Select(t => t.Text));
            Assert.Contains("TEMA DO JOGO", PanelText());
            foreach (var name in new[] { "Clássico", "Fantasia", "Livro", "Moderno" }) Assert.Contains(name, PanelText());
            Assert.Equal(ThemeName.Classic, view.Model.Look.Name);

            Assert.Null(view.Model.SetTheme(ThemeName.Fantasy));
            window.UpdateLayout();
            var file = Path.Combine(dir, GameStyle.FileName);
            Assert.Equal(ThemeName.Fantasy, GameStyle.Parse(File.ReadAllText(file)).Theme);
            Assert.Equal(ThemeName.Fantasy, view.Model.Look.Name);
            Assert.Same(Look.Fantasy.BackgroundBrush, Theme.Background);   // o palco já desenha com o tema novo

            var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(view);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var png = File.Create(Path.Combine(Path.GetTempPath(), "csharplab-aba-tema.png"))) encoder.Save(png);

            // Arquivo com erro: a tela usa o Clássico e o painel explica.
            File.WriteAllText(file, """{ "theme": "Pirata" }""");
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddSeconds(5));
            view.Model.Select(null);
            view.Model.Refresh();
            view.Stage.InvalidateVisual();
            view.Model.ApplyTheme();
            Assert.Equal(ThemeName.Classic, view.Model.Look.Name);
            Assert.Contains("Pirata", view.Model.ThemeError);
        }
        finally
        {
            window.Close();
            view.Detach();
            Theme.Current = Look.Classic;
        }
    });

    /// <summary>
    /// Texto que não cabe: aviso laranja no palco e no painel, e "Ajustar ao texto" conserta num passo só.
    /// A aba com o aviso fica em %TEMP%\csharplab-texto-cortado.png para conferir o visual.
    /// </summary>
    [Fact]
    public void Texto_que_nao_cabe_ganha_aviso_e_se_ajusta_num_passo() => Ui.Run(async () =>
    {
        var json = """
            { "pieces": [
              { "type": "Text", "name": "Story", "x": 40, "y": 40, "width": 420, "height": 40, "text": "Encostado numa árvore, um velho segura a perna machucada. Uma bolsa pesada está ao lado dele, e o vento traz o cheiro de chuva.", "size": 18 },
              { "type": "Button", "name": "Buy", "x": 40, "y": 300, "width": 120, "height": 48, "text": "Comprar a espada de aço" },
              { "type": "Text", "name": "Short", "x": 520, "y": 40, "width": 300, "height": 40, "text": "Cabe." }
            ] }
            """;
        var (doc, _, _) = Open(json);
        using var vm = new MainViewModel(new AppSettings()) { Dialogs = new FakeDialogs(), Terminal = new FakeTerminal() };
        var view = new ScreenEditorView(doc, vm);
        var window = new Window { Content = view, Width = 1280, Height = 760, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Left = -10000, Top = -10000 };
        window.Show();
        try
        {
            string PanelText() => string.Join(" | ", Descendants(view.Properties).OfType<System.Windows.Controls.TextBlock>().Where(t => t.IsVisible).Select(t => t.Text));
            view.Model.Select("Story");
            await Task.Delay(200);
            window.UpdateLayout();

            Assert.True(view.Stage.ShowsOverflow("Story"));
            Assert.True(view.Stage.ShowsOverflow("Buy"));
            Assert.False(view.Stage.ShowsOverflow("Short"));
            Assert.Contains("O texto não cabe", PanelText());
            var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(view);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(Path.GetTempPath(), "csharplab-texto-cortado.png"))) encoder.Save(file);

            // "Ajustar a altura ao texto": cabe, o aviso some, e é um passo só no desfazer.
            int before = doc.Document.UndoStack.CanUndo ? 1 : 0;
            Assert.True(view.Model.FitToText("Story"));
            window.UpdateLayout();
            var story = view.Model.Layout!.Find("Story")!;
            Assert.True(story.Height > 40);
            Assert.Null(ScreenRenderer.Overflow(story));
            Assert.False(view.Stage.ShowsOverflow("Story"));
            Assert.DoesNotContain("O texto não cabe", PanelText());
            Assert.False(view.Model.FitToText("Story"));   // já cabe: nada a fazer

            // Botão: a largura cresce.
            Assert.True(view.Model.FitToText("Buy"));
            Assert.True(view.Model.Layout!.Find("Buy")!.Width > 120);
            Assert.False(view.Stage.ShowsOverflow("Buy"));
            Assert.Equal(before + 2, UndoSteps(doc));
        }
        finally
        {
            window.Close();
            view.Detach();
        }
    });

    [Fact]
    public void Nova_peca_aparece_no_meio_selecionada_e_gravada() => Ui.Run(() =>
    {
        var (doc, model, dir) = Open();
        Assert.True(doc.IsScreen);
        Assert.Equal(dir, model.ProjectDirectory);
        Assert.Equal("Fight", model.SceneName);

        var piece = model.Add(PieceType.Button)!;
        Assert.Equal("Button1", piece.Name);
        Assert.Equal("Button1", model.SelectedName);
        Assert.Equal(0, piece.X % 8);
        Assert.Contains("\"name\": \"Button1\"", doc.Document.Text);
        Assert.True(doc.IsDirty);

        var second = model.Add(PieceType.Button)!;
        Assert.NotEqual((piece.X, piece.Y), (second.X, second.Y)); // não fica exatamente em cima da outra
        return Task.CompletedTask;
    });

    [Fact]
    public void Arrastar_e_um_passo_so_no_desfazer() => Ui.Run(() =>
    {
        var (doc, model, _) = Open();
        model.SetBounds("Continue", new Rect(100.4, 200.6, 150, 60));
        var moved = model.Layout!.Find("Continue")!;
        Assert.Equal((100d, 201d, 150d, 60d), (moved.X, moved.Y, moved.Width, moved.Height));

        model.Undo();
        Assert.Equal(48, model.Layout!.Find("Continue")!.X);
        model.Redo();
        Assert.Equal(100, model.Layout!.Find("Continue")!.X);
        return Task.CompletedTask;
    });

    [Fact]
    public void Setas_e_digitacao_juntam_os_passos_do_desfazer() => Ui.Run(() =>
    {
        var (doc, model, _) = Open();
        model.Select("Continue");
        model.Nudge(1, 0);
        model.Nudge(1, 0);
        model.Nudge(0, 10);
        model.EndMerge();
        foreach (var text in new[] { "J", "Jo", "Jog", "Jogar" })
            model.Edit("Continue", p => p.Text = text, "text:Continue");
        Assert.Equal((50d, 450d, "Jogar"), (model.Layout!.Find("Continue")!.X, model.Layout.Find("Continue")!.Y, model.Layout.Find("Continue")!.Text));
        Assert.Equal(2, UndoSteps(doc)); // um passo para as setas, um para o texto
        return Task.CompletedTask;
    });

    [Fact]
    public void Renomear_duplicar_apagar_e_ordem() => Ui.Run(() =>
    {
        var (doc, model, _) = Open();
        model.Select("Continue");
        Assert.Contains("letras sem acento", model.Rename("Atacar já"));
        Assert.Contains("Já existe", model.Rename("title"));
        Assert.Null(model.Rename("Attack"));
        Assert.Equal("Attack", model.SelectedName);

        var copy = model.Duplicate()!;
        Assert.Equal("Attack2", copy.Name);
        Assert.Equal((model.Layout!.Find("Attack")!.X + 16, model.Layout.Find("Attack")!.Y + 16), (copy.X, copy.Y));

        model.Select("Title");
        model.BringToFront();
        Assert.Equal("Title", model.Layout!.Pieces[^1].Name);
        model.SendToBack();
        Assert.Equal("Title", model.Layout!.Pieces[0].Name);

        model.Delete();
        Assert.Null(model.Layout!.Find("Title"));
        Assert.Null(model.SelectedName);
        return Task.CompletedTask;
    });

    [Fact]
    public void Texto_mudado_fora_atualiza_a_tela_e_erro_aparece() => Ui.Run(() =>
    {
        var (doc, model, _) = Open();
        int changes = 0;
        model.Changed += () => changes++;
        var text = doc.Document.Text.Replace("\"Continuar\"", "\"Seguir\"");
        doc.Document.Replace(0, doc.Document.TextLength, text);
        Assert.Equal("Seguir", model.Layout!.Find("Continue")!.Text);
        Assert.Equal(1, changes);

        doc.Document.Insert(0, "oops");
        Assert.Null(model.Layout);
        Assert.Contains("não está escrito certo", model.Error);

        // Na aba Texto, a tela só é lida de novo ao voltar.
        model.IsLive = false;
        doc.Document.Remove(0, 4);
        Assert.Null(model.Layout);
        model.IsLive = true;
        model.Refresh();
        Assert.NotNull(model.Layout);
        return Task.CompletedTask;
    });

    [Fact]
    public void Peca_nunca_some_do_palco() => Ui.Run(() =>
    {
        var (_, model, _) = Open();
        model.SetBounds("Continue", new Rect(5000, -900, 4, 4));
        var piece = model.Layout!.Find("Continue")!;
        Assert.Equal(ScreenDesignerModel.MinSize, piece.Width);
        Assert.True(piece.X <= ScreenLayout.Width - ScreenDesignerModel.KeepInside);
        Assert.True(piece.Y + piece.Height >= ScreenDesignerModel.KeepInside);
        return Task.CompletedTask;
    });

    [Fact]
    public void Imagem_importada_vai_para_Assets_sem_sobrescrever() => Ui.Run(() =>
    {
        var (_, model, dir) = Open();
        var source = Path.Combine(Ui.NewFolder("fotos"), "goblin.png");
        File.WriteAllBytes(source, [1, 2, 3]);
        Assert.Equal("goblin.png", model.ImportImage(source));
        Assert.Equal("goblin-2.png", model.ImportImage(source));
        Assert.Equal(["goblin-2.png", "goblin.png"], model.ImageFiles());
        Assert.True(File.Exists(Path.Combine(dir, "Assets", "goblin.png")));
        return Task.CompletedTask;
    });

    [Fact]
    public void Codigo_de_exemplo_de_cada_peca()
    {
        Assert.Equal("game.Find(\"Attack\").OnClick(() =>\n{\n    \n});",
            ScreenDesignerModel.CodeExample(Piece.CreateDefault(PieceType.Button, "Attack", 0, 0)));
        Assert.Equal("game.Find(\"Hp\").Value = health;", ScreenDesignerModel.CodeExample(Piece.CreateDefault(PieceType.Bar, "Hp", 0, 0)));
    }

    [Fact]
    public void Ima_gruda_nas_bordas_das_outras_pecas_com_guia()
    {
        var snapper = new Snapper([new Rect(100, 100, 200, 50)]) { Threshold = 6 };
        var (rect, guides) = snapper.Move(new Rect(104, 300, 80, 40), snap: true);
        Assert.Equal(100, rect.X);                                   // grudou na esquerda da outra
        Assert.Contains(guides, g => g.Vertical && g.Position == 100);
        Assert.Equal(304, rect.Y);                                   // sem nada perto: grade de 8 (300 vira 304)

        (rect, _) = snapper.Move(new Rect(104, 300, 80, 40), snap: false);
        Assert.Equal(104, rect.X);

        (rect, guides) = snapper.Resize(new Rect(100, 300, 197, 40), Edges.Right, snap: true);
        Assert.Equal(300, rect.Right);                               // direita encaixou na direita da outra
        Assert.Single(guides);
    }

    [Fact]
    public void Nova_tela_cria_o_arquivo_abre_na_aba_Tela_e_escreve_a_cena() => Ui.Run(async () =>
    {
        var dialogs = new FakeDialogs { TextAnswer = "Fight" };
        using var vm = new MainViewModel(new AppSettings()) { Dialogs = dialogs, Terminal = new FakeTerminal() };
        await vm.InitializeAsync();
        var created = ProjectCreator.CreateGameProject(Ui.NewFolder("jogos"), "Torre");
        await vm.OpenFolderAsync(created.Directory, created.ProjectPath, promptForUnsaved: false);
        await Ui.WaitUntil(() => vm.Projects.Count > 0, 60_000, "projetos");

        vm.NewScreenCommand.Execute(null);
        var screen = Path.Combine(created.Directory, "Screens", "Fight.json");
        Assert.True(File.Exists(screen));
        Assert.True(vm.ActiveDocument?.IsScreen);
        var program = vm.FindDocument(created.ProgramPath)!;
        var code = program.Document.Text;
        Assert.True(code.IndexOf("game.Scene(\"Fight\"", StringComparison.Ordinal) < code.IndexOf("game.Start(", StringComparison.Ordinal));
        Assert.Contains("game.Find(\"Continue\").OnClick", code);
        Assert.True(program.IsDirty);

        // De novo: o nome sugerido não repete uma tela que já existe.
        dialogs.TextAnswer = null;
        vm.NewScreenCommand.Execute(null);
    });

    [Fact]
    public void Renomear_na_Tela_atualiza_o_codigo_so_daquela_cena() => Ui.Run(async () =>
    {
        using var vm = new MainViewModel(new AppSettings()) { Dialogs = new FakeDialogs(), Terminal = new FakeTerminal() };
        await vm.InitializeAsync();
        var program = "var game = new Game(\"T\");\n" +
                      "game.Scene(\"Fight\", () => game.Find(\"Continue\").OnClick(() => { }));\n" +
                      "game.Scene(\"Shop\", () => game.Find(\"Continue\").OnClick(() => { }));\n" +
                      "game.Start(\"Fight\");\n";
        var created = ProjectCreator.CreateGameProject(Ui.NewFolder("jogos"), "Torre", program);
        Directory.CreateDirectory(Path.Combine(created.Directory, "Screens"));
        var screen = Path.Combine(created.Directory, "Screens", "Fight.json");
        File.WriteAllText(screen, ScreenFile.Serialize(ScreenLayout.CreateDefault("Fight")));
        await vm.OpenFolderAsync(created.Directory, created.ProjectPath, promptForUnsaved: false);

        var view = new ScreenEditorView(vm.OpenFile(screen)!, vm);
        try
        {
            view.Model.Select("Continue");
            Assert.Null(view.Model.Rename("Attack"));
            var code = vm.FindDocument(created.ProgramPath)!.Document.Text;
            Assert.Contains("game.Scene(\"Fight\", () => game.Find(\"Attack\")", code);
            Assert.Contains("game.Scene(\"Shop\", () => game.Find(\"Continue\")", code); // outra cena: não muda
        }
        finally
        {
            view.Detach();
        }
    });

    [Fact]
    public void Tela_e_codigo_da_cena_levam_um_ao_outro() => Ui.Run(async () =>
    {
        var dialogs = new FakeDialogs();
        using var vm = new MainViewModel(new AppSettings()) { Dialogs = dialogs, Terminal = new FakeTerminal() };
        await vm.InitializeAsync();
        var program = "var game = new Game(\"T\");\n" +
                      "game.Scene(\"Fight\", () =>\n{\n    game.Find(\"Continue\").OnClick(() => game.GoTo(\"Shop\"));\n});\n" +
                      "game.Scene(\"Shop\", () => game.Write(\"Loja\"));\n" +
                      "game.Start(\"Fight\");\n";
        var created = ProjectCreator.CreateGameProject(Ui.NewFolder("jogos"), "Torre", program);
        Directory.CreateDirectory(Path.Combine(created.Directory, "Screens"));
        var screen = Path.Combine(created.Directory, "Screens", "Fight.json");
        File.WriteAllText(screen, ScreenFile.Serialize(ScreenLayout.CreateDefault("Fight")));
        await vm.OpenFolderAsync(created.Directory, created.ProjectPath, promptForUnsaved: false);
        await Ui.WaitUntil(() => vm.Projects.Count > 0, 60_000, "projetos");

        Assert.Equal(created.Directory, vm.GameDirectoryOf(created.ProgramPath));
        Assert.Equal(created.Directory, vm.GameDirectoryOf(screen));
        Assert.Null(vm.GameDirectoryOf(Path.Combine(Path.GetTempPath(), "solto.cs")));
        Assert.True(vm.HasScreen(created.Directory, "Fight"));
        Assert.False(vm.HasScreen(created.Directory, "Shop"));

        // Tela → código: o cursor vai para o game.Scene("Fight", …).
        DocumentViewModel? target = null;
        int? offset = null;
        vm.GoToRequested += (doc, _, _, o) => (target, offset) = (doc, o);
        vm.GoToSceneCode(created.Directory, "Fight");
        Assert.Equal(created.ProgramPath, target?.FilePath);
        Assert.Equal(program.IndexOf("game.Scene(\"Fight\"", StringComparison.Ordinal), offset);
        Assert.Same(target, vm.ActiveDocument);

        // Código → tela: abre a aba da tela, pedindo o modo Tela.
        DocumentViewModel? design = null;
        vm.ScreenDesignRequested += doc => design = doc;
        vm.OpenSceneScreen(created.Directory, "Fight");
        Assert.Equal(screen, design?.FilePath);
        Assert.True(vm.ActiveDocument?.IsScreen);
        Assert.Empty(dialogs.Confirms);

        // Cena sem tela: pergunta antes; "não" não cria nada, "sim" cria e abre.
        dialogs.ConfirmAnswer = (_, _) => false;
        vm.OpenSceneScreen(created.Directory, "Shop");
        Assert.Single(dialogs.Confirms);
        Assert.False(File.Exists(Path.Combine(created.Directory, "Screens", "Shop.json")));
        dialogs.ConfirmAnswer = (_, _) => true;
        vm.OpenSceneScreen(created.Directory, "Shop");
        Assert.True(File.Exists(Path.Combine(created.Directory, "Screens", "Shop.json")));
        Assert.Equal("Shop", Path.GetFileNameWithoutExtension(design?.FilePath));

        // Tela de uma cena que o código ainda não tem: oferece escrever a cena e vai até ela.
        File.WriteAllText(Path.Combine(created.Directory, "Screens", "Cave.json"), ScreenFile.Serialize(ScreenLayout.CreateDefault("Cave")));
        vm.GoToSceneCode(created.Directory, "Cave");
        var code = vm.FindDocument(created.ProgramPath)!.Document.Text;
        Assert.Equal(code.IndexOf("game.Scene(\"Cave\"", StringComparison.Ordinal), offset);
        Assert.True(offset < code.IndexOf("game.Start(", StringComparison.Ordinal));
    });

    [Fact]
    public void Painel_do_botao_mostra_o_que_ele_faz_e_escreve_o_OnClick() => Ui.Run(async () =>
    {
        var dialogs = new FakeDialogs();
        using var vm = new MainViewModel(new AppSettings()) { Dialogs = dialogs, Terminal = new FakeTerminal() };
        await vm.InitializeAsync();
        var program = "var game = new Game(\"T\");\n" +
                      "game.Scene(\"Fight\", () =>\n{\n    game.Find(\"Continue\").OnClick(() =>\n    {\n        game.Write(\"Oi\");\n    });\n});\n" +
                      "game.Start(\"Fight\");\n";
        var created = ProjectCreator.CreateGameProject(Ui.NewFolder("jogos"), "Torre", program);
        Directory.CreateDirectory(Path.Combine(created.Directory, "Screens"));
        var layout = ScreenLayout.CreateDefault("Fight");
        layout.Pieces.Add(Piece.CreateDefault(PieceType.Button, "Attack", 300, 400));
        var screen = Path.Combine(created.Directory, "Screens", "Fight.json");
        File.WriteAllText(screen, ScreenFile.Serialize(layout));
        await vm.OpenFolderAsync(created.Directory, created.ProjectPath, promptForUnsaved: false);
        await Ui.WaitUntil(() => vm.Projects.Count > 0, 60_000, "projetos");

        // Onde está a cena e o que o código já faz com cada botão.
        var sceneCode = vm.FindSceneCode(created.Directory, "Fight")!;
        Assert.Equal((created.ProgramPath, 2), (sceneCode.File, sceneCode.Line));
        Assert.Null(vm.FindSceneCode(created.Directory, "Nowhere"));
        var cont = vm.FindPieceCode(created.Directory, "Fight", "Continue", "OnClick")!;
        Assert.True(cont.HasHandler);
        Assert.Equal(4, cont.Line);
        Assert.Null(vm.FindPieceCode(created.Directory, "Fight", "Attack", "OnClick"));

        var view = new ScreenEditorView(vm.OpenFile(screen)!, vm);
        var window = new Window { Content = view, Width = 1280, Height = 760, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Left = -10000, Top = -10000 };
        window.Show();
        try
        {
            string PanelText() => string.Join(" | ", Descendants(view.Properties).OfType<System.Windows.Controls.TextBlock>()
                .Where(t => t.IsVisible).Select(t => t.Text));
            void Show() => window.UpdateLayout();
            // Sem peça escolhida: onde a cena está no código.
            Show();
            Assert.Contains("game.Scene(\"Fight\")", PanelText());
            Assert.Contains("Program.cs, linha 2", string.Join(" ", Descendants(view.Properties).OfType<System.Windows.Controls.Button>()
                .Where(b => b.IsVisible).Select(b => b.Content as string)));
            view.Model.Select("Continue");
            Show();
            Assert.Contains("Já faz algo", PanelText());
            view.Model.Select("Attack");
            Show();
            Assert.Contains("Ainda não faz nada", PanelText());

            // "Escrever o que ele faz": a estrutura vazia entra na cena, com o cursor entre as chaves.
            int? caret = null;
            vm.GoToRequested += (_, _, _, o) => caret = o;
            vm.WritePieceHandler(created.Directory, "Fight", "Attack", "OnClick");
            var code = vm.FindDocument(created.ProgramPath)!.Document.Text;
            Assert.Contains("    game.Find(\"Attack\").OnClick(() =>\n    {\n        \n    });\n});", code);
            Assert.Equal(code.IndexOf("game.Find(\"Attack\")", StringComparison.Ordinal) + "game.Find(\"Attack\").OnClick(() =>\n    {\n        ".Length, caret);
            Assert.True(vm.FindPieceCode(created.Directory, "Fight", "Attack", "OnClick")!.HasHandler);
            view.Model.Select("Continue");
            view.Model.Select("Attack");
            Show();
            Assert.Contains("Já faz algo", PanelText());

            // A aba nesse estado fica em %TEMP%\csharplab-ao-clicar.png para conferir o visual.
            var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(view);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(Path.GetTempPath(), "csharplab-ao-clicar.png"));
            encoder.Save(file);
        }
        finally
        {
            window.Close();
            view.Detach();
        }
    });

    /// <summary>
    /// O botão Cenas (ao lado do Executar) lista as cenas do jogo e troca entre a tela e o código da cena atual.
    /// O menu fica em %TEMP%\csharplab-menu-cenas.png para conferir o visual.
    /// </summary>
    [Fact]
    public void Botao_Cenas_lista_as_cenas_e_troca_tela_e_codigo() => Ui.Run(async () =>
    {
        using var vm = new MainViewModel(new AppSettings()) { Dialogs = new FakeDialogs(), Terminal = new FakeTerminal() };
        await vm.InitializeAsync();
        Assert.False(vm.HasGame);
        var program = "var game = new Game(\"T\");\n\n" +
                      "game.Scene(\"Fight\", () =>\n{\n    game.Find(\"Continue\").OnClick(() => game.GoTo(\"Shop\"));\n});\n\n" +
                      "game.Scene(\"Shop\", () => game.Write(\"Loja\"));\n\n" +
                      "game.Start(\"Fight\");\n";
        var created = ProjectCreator.CreateGameProject(Ui.NewFolder("jogos"), "Torre", program);
        Directory.CreateDirectory(Path.Combine(created.Directory, "Screens"));
        File.WriteAllText(Path.Combine(created.Directory, "Screens", "Fight.json"), ScreenFile.Serialize(ScreenLayout.CreateDefault("Fight")));
        File.WriteAllText(Path.Combine(created.Directory, "Screens", "Cave.json"), ScreenFile.Serialize(ScreenLayout.CreateDefault("Cave")));
        await vm.OpenFolderAsync(created.Directory, created.ProjectPath, promptForUnsaved: false);
        await Ui.WaitUntil(() => vm.Projects.Count > 0, 60_000, "projetos");
        Assert.True(vm.HasGame);

        var scenes = vm.GameScenes();
        Assert.Equal(
            [new GameSceneInfo("Fight", true, true, true), new GameSceneInfo("Shop", false, true, false), new GameSceneInfo("Cave", true, false, false)],
            scenes);

        // Escolher uma cena: com tela abre a tela; só código vai para o game.Scene dela.
        DocumentViewModel? design = null;
        int? offset = null;
        vm.ScreenDesignRequested += doc => design = doc;
        vm.GoToRequested += (_, _, _, o) => offset = o;
        vm.OpenGameScene("Fight");
        Assert.Equal("Fight", Path.GetFileNameWithoutExtension(design?.FilePath));
        vm.OpenGameScene("Shop");
        Assert.Equal(program.IndexOf("game.Scene(\"Shop\"", StringComparison.Ordinal), offset);
        Assert.Equal(created.ProgramPath, vm.ActiveDocument?.FilePath);

        // No código, dentro da cena Fight: o primeiro item leva para a tela dela.
        var menu = CSharpLab.Views.ScenesMenu.Build(vm, new CSharpLab.Views.SceneContext("Fight", OnScreen: false));
        var texts = menu.Items.OfType<System.Windows.Controls.MenuItem>()
            .Select(i => string.Join(" ", Descendants(i.Header as DependencyObject ?? new DependencyObject()).Prepend(i.Header as DependencyObject ?? new DependencyObject())
                .OfType<System.Windows.Controls.TextBlock>().Select(t => t.Text)))
            .ToList();
        Assert.Contains("Ver a tela da cena Fight", texts[0]);
        Assert.Contains(texts, t => t.Contains("Fight") && t.Contains("começa aqui"));
        Assert.Contains(texts, t => t.Contains("Shop") && t.Contains("só código"));
        Assert.Contains(texts, t => t.Contains("Cave") && t.Contains("falta no código"));
        Assert.Contains(texts, t => t.Contains("Nova tela do jogo"));

        // Na tela: o primeiro item leva para o código.
        var fromScreen = CSharpLab.Views.ScenesMenu.Build(vm, new CSharpLab.Views.SceneContext("Fight", OnScreen: true));
        var first = (System.Windows.Controls.MenuItem)fromScreen.Items[0];
        Assert.Contains(((System.Windows.Controls.Panel)first.Header).Children.OfType<System.Windows.Controls.TextBlock>(), t => t.Text == "Ver o código da cena Fight");

        menu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        menu.Arrange(new Rect(menu.DesiredSize));
        menu.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(menu.ActualWidth), (int)Math.Ceiling(menu.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(menu);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(Path.GetTempPath(), "csharplab-menu-cenas.png"));
        encoder.Save(file);
    });

    [Fact]
    public void Atalho_do_botao_grava_Espaco_e_desfaz_sem_perder_as_outras_propriedades() => Ui.Run(async () =>
    {
        var (doc, _, _) = Open();
        using var vm = new MainViewModel(new AppSettings()) { Dialogs = new FakeDialogs(), Terminal = new FakeTerminal() };
        var view = new ScreenEditorView(doc, vm);
        view.Model.Select("Continue");
        var window = new Window { Content = view, Width = 1280, Height = 760, WindowStyle = WindowStyle.None,
            ShowInTaskbar = false, ShowActivated = false, Left = -10000, Top = -10000 };
        window.Show();
        try
        {
            await Task.Delay(100);
            var before = doc.Document.Text;
            var picker = Descendants(view.Properties).OfType<System.Windows.Controls.Button>()
                .Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Atalho");
            var space = picker.ContextMenu.Items.OfType<System.Windows.Controls.MenuItem>()
                .Single(i => i.Header is string label && label == "Espaço");
            space.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
            Assert.Contains("\"shortcut\": \"Space\"", doc.Document.Text);
            Assert.Equal("Space", view.Model.Layout!.Find("Continue")!.Shortcut);
            Assert.Contains("Espaço", picker.Content.ToString());
            Assert.Equal("Continuar", view.Model.Layout!.Find("Continue")!.Text);

            doc.Document.UndoStack.Undo();
            Assert.Equal(before, doc.Document.Text);
            Assert.Null(view.Model.Layout!.Find("Continue")!.Shortcut);
            Assert.Contains("Nenhum", picker.Content.ToString());
            doc.Document.UndoStack.Redo();
            Assert.Equal("Space", view.Model.Layout!.Find("Continue")!.Shortcut);

            window.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(view);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(Path.GetTempPath(), "csharplab-atalho.png"))) encoder.Save(file);

            var none = picker.ContextMenu.Items.OfType<System.Windows.Controls.MenuItem>()
                .Single(i => i.Header is string label && label == "Nenhum");
            none.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
            Assert.DoesNotContain("\"shortcut\"", doc.Document.Text);
            view.Model.Select("Title");
            Assert.DoesNotContain(Descendants(view.Properties).OfType<System.Windows.Controls.Button>(),
                b => System.Windows.Automation.AutomationProperties.GetName(b) == "Atalho");
        }
        finally { window.Close(); view.Detach(); }
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

    /// <summary>
    /// A aba Tela inteira desenhada fora da tela (peças, seleção, painel de propriedades).
    /// A imagem fica em %TEMP%\csharplab-aba-tela.png para conferir o visual.
    /// </summary>
    [Fact]
    public void Aba_Tela_desenha_palco_paleta_e_propriedades() => Ui.Run(async () =>
    {
        var json = """
            { "pieces": [
              { "type": "Box", "name": "Panel", "x": 620, "y": 24, "width": 316, "height": 130 },
              { "type": "Text", "name": "Title", "x": 40, "y": 30, "width": 560, "height": 50, "text": "A Torre do Goblin", "size": 32, "bold": true },
              { "type": "Text", "name": "Story", "x": 40, "y": 90, "width": 560, "height": 70, "text": "Um goblin aparece segurando uma faca enferrujada.", "size": 19 },
              { "type": "Bar", "name": "PlayerHealth", "x": 640, "y": 40, "width": 276, "height": 44, "text": "Vida", "value": 72, "max": 100 },
              { "type": "Bar", "name": "EnemyHealth", "x": 640, "y": 96, "width": 276, "height": 44, "text": "Goblin", "value": 9, "max": 30, "color": "Red" },
              { "type": "Image", "name": "Goblin", "x": 660, "y": 180, "width": 240, "height": 200 },
              { "type": "Messages", "name": "Log", "x": 40, "y": 180, "width": 560, "height": 150 },
              { "type": "Button", "name": "Attack", "x": 40, "y": 460, "width": 170, "height": 52, "text": "Atacar" },
              { "type": "Button", "name": "Potion", "x": 224, "y": 460, "width": 200, "height": 52, "text": "Beber poção", "color": "Green", "visible": false }
            ] }
            """;
        var (doc, _, _) = Open(json);
        using var vm = new MainViewModel(new AppSettings()) { Dialogs = new FakeDialogs(), Terminal = new FakeTerminal() };
        var view = new ScreenEditorView(doc, vm);
        view.Model.Select("Attack");
        var window = new Window { Content = view, Width = 1280, Height = 760, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Left = -10000, Top = -10000 };
        window.Show();
        await Task.Delay(300);
        window.UpdateLayout();
        try
        {
            var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(view);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(Path.GetTempPath(), "csharplab-aba-tela.png"));
            encoder.Save(file);
            Assert.True(view.Stage.Zoom > 0.5);
        }
        finally
        {
            window.Close();
            view.Detach();
        }
    });
}
