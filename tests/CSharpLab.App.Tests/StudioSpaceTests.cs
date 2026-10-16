using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CSharpLab.Core.Files;
using CSharpLab.Core.Projects;
using CSharpLab.Core.Settings;
using CSharpLab.GameEngine;
using CSharpLab.Screens;
using CSharpLab.ViewModels;
using CSharpLab.Views;
using PanelTab = CSharpLab.Screens.ScreenPropertiesPanel.PanelTab;

namespace CSharpLab.App.Tests;

/// <summary>Espaço para montar a tela: zoom no palco, esconder o código do Estúdio e o painel em abas.</summary>
public sealed class StudioSpaceTests
{
    private const string Json = """
        { "pieces": [
          { "type": "Text", "name": "Title", "x": 40, "y": 20, "width": 400, "height": 50, "text": "Título" },
          { "type": "Button", "name": "Attack", "x": 40, "y": 90, "width": 200, "height": 52, "text": "Atacar" },
          { "type": "Bar", "name": "Health", "x": 40, "y": 160, "width": 300, "height": 44, "text": "Vida" }
        ] }
        """;

    private const string Program =
        "var game = new Game(\"T\");\n\n" +
        "game.Scene(\"Fight\", () =>\n{\n    game.Find(\"Attack\").OnClick(() => game.Write(\"Pá!\"));\n});\n\n" +
        "game.Start(\"Fight\");\n";

    [Fact]
    public void Palco_aumenta_diminui_e_volta_a_caber() => Ui.Run(async () =>
    {
        var (view, window) = OpenScreen();
        try
        {
            await Settle(window);
            var stage = view.Stage;
            double fit = stage.Zoom;
            Assert.True(stage.IsFitting);
            Assert.InRange(fit, ScreenStage.MinZoom, 1.5);

            stage.SetZoom(1);
            Assert.Equal(1, stage.Zoom, 3);
            Assert.False(stage.IsFitting);
            stage.ZoomIn();
            Assert.Equal(1.2, stage.Zoom, 3);
            stage.ZoomOut();
            stage.ZoomOut();
            Assert.True(stage.Zoom < 1);
            stage.SetZoom(99);
            Assert.Equal(ScreenStage.MaxZoom, stage.Zoom, 3);

            stage.ZoomToFit();
            Assert.True(stage.IsFitting);
            Assert.Equal(fit, stage.Zoom, 3);

            // Chegar perto do tamanho que cabe (pelos botões) volta ao "caber", sem sobrar um zoom quase igual.
            stage.SetZoom(fit + 0.005);
            Assert.True(stage.IsFitting);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void Painel_da_peca_tem_abas_que_ficam_ao_trocar_de_peca() => Ui.Run(async () =>
    {
        var (view, window) = OpenScreen();
        try
        {
            var panel = view.Properties;
            List<string> Texts() => Descendants(panel).OfType<TextBlock>().Where(t => t.IsVisible).Select(t => t.Text).ToList();
            ToggleButton Tab(string text) => Descendants(panel).OfType<ToggleButton>().First(b => Equals(b.Content, text));

            panel.ShowTab(PanelTab.Piece);
            view.Model.Select("Attack");
            await Settle(window);
            var texts = Texts();
            Assert.Contains("NOME", texts);
            Assert.Contains("TEXTO", texts);
            Assert.DoesNotContain("ESTILO", texts);
            Assert.DoesNotContain("POSIÇÃO E TAMANHO", texts);

            Tab("Aparência").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            await Settle(window);
            texts = Texts();
            Assert.Contains("ESTILO", texts);
            Assert.Contains("COR DO BOTÃO", texts);
            Assert.Contains("LETRA", texts);
            Assert.DoesNotContain("NOME", texts);

            // Outra peça: continua em Aparência (quem está mexendo nas cores segue nas cores).
            view.Model.Select("Health");
            await Settle(window);
            Assert.Equal(PanelTab.Look, panel.OpenTab);
            Assert.Contains("ESTILO DA BARRA", Texts());
            Assert.True(Tab("Aparência").IsChecked);

            Tab("Posição").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            await Settle(window);
            texts = Texts();
            Assert.Contains("POSIÇÃO E TAMANHO", texts);
            Assert.Contains("Aparece no começo", texts);

            // Dois cliques na peça (editar o texto) voltam para a aba Peça.
            panel.FocusText();
            await Settle(window);
            Assert.Equal(PanelTab.Piece, panel.OpenTab);
            Assert.Contains("RÓTULO", Texts());
            SaveImage(window, "csharplab-painel-abas.png");
        }
        finally
        {
            view.Properties.ShowTab(PanelTab.Piece);
            window.Close();
        }
    });

    [Fact]
    public void Estudio_esconde_o_codigo_e_o_palco_cresce() => Ui.Run(async () =>
    {
        var settings = new AppSettings();
        using var vm = new MainViewModel(settings) { Dialogs = new FakeDialogs(), Terminal = new FakeTerminal() };
        await vm.InitializeAsync();
        var created = ProjectCreator.CreateGameProject(Ui.NewFolder("jogos"), "Torre", Program);
        Directory.CreateDirectory(Path.Combine(created.Directory, "Screens"));
        File.WriteAllText(GameScreens.PathOf(created.Directory, "Fight"), Json);
        await vm.OpenFolderAsync(created.Directory, created.ProjectPath, promptForUnsaved: false);
        await Ui.WaitUntil(() => vm.Projects.Count > 0, 60_000, "projetos");

        var studio = new StudioView();
        studio.Bind(vm);
        var window = new Window { Content = studio, Width = 1600, Height = 900, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Left = -10000, Top = -10000 };
        window.Show();
        try
        {
            vm.OpenStudio();
            studio.Open(null);
            await Settle(window);
            Assert.True(studio.ShowsCode);
            double withCode = studio.Screen!.Stage.Zoom;
            SaveImage(window, "csharplab-estudio-com-codigo.png");

            vm.IsStudioCodeVisible = false;
            await Settle(window);
            Assert.False(studio.ShowsCode);
            Assert.True(studio.Screen!.Stage.Zoom > withCode * 1.3, $"o palco devia crescer: {withCode} → {studio.Screen.Stage.Zoom}");
            Assert.False(studio.Screen.Palette.Compact);   // com espaço, as peças voltam a mostrar o nome
            vm.SaveSettings();
            Assert.False(settings.StudioCodeVisible);
            SaveImage(window, "csharplab-estudio-sem-codigo.png");

            // Ir até uma linha do código (um link do painel) mostra o código de novo.
            vm.GoToSceneCode(created.Directory, "Fight");
            await Settle(window);
            Assert.True(studio.ShowsCode);
            Assert.True(vm.IsStudioCodeVisible);
            Assert.Equal(withCode, studio.Screen!.Stage.Zoom, 2);
        }
        finally
        {
            window.Close();
        }
    });

    private static (ScreenEditorView View, Window Window) OpenScreen()
    {
        var dir = Path.Combine(Ui.NewFolder("jogo"), "Screens");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "Room.json");
        File.WriteAllText(path, Json);
        var doc = new DocumentViewModel(Json, path, TextFileIO.Utf8NoBom, null);
        var vm = new MainViewModel(new AppSettings()) { Dialogs = new FakeDialogs(), Terminal = new FakeTerminal() };
        var view = new ScreenEditorView(doc, vm);
        var window = new Window { Content = view, Width = 1280, Height = 800, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Left = -10000, Top = -10000 };
        window.Closed += (_, _) => vm.Dispose();
        window.Show();
        return (view, window);
    }

    private static async Task Settle(Window window)
    {
        await Task.Delay(150);
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

    /// <summary>A imagem fica em %TEMP% para conferir o visual.</summary>
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
