using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CSharpLab.Core.Build;
using CSharpLab.Core.Language;
using CSharpLab.Core.Projects;
using CSharpLab.Core.Settings;
using CSharpLab.GameEngine;
using CSharpLab.Screens;
using CSharpLab.ViewModels;

namespace CSharpLab.App.Tests;

/// <summary>Modelos de tela: cada um nasce bonito, sem texto cortado, e o código dele compila sem avisos.</summary>
public sealed class ScreenTemplateTests
{
    [Fact]
    public void Modelos_nao_cortam_texto_e_o_arquivo_le_igual() => Ui.Run(() =>
    {
        Theme.Current = Look.Classic;
        foreach (var template in ScreenTemplates.All)
        {
            var layout = template.Layout("Cena");
            var text = ScreenFile.Serialize(layout);
            var parsed = ScreenFile.Parse(text);
            Assert.True(parsed.Success, template.Id + ": " + parsed.Error);
            Assert.Empty(parsed.Warnings);
            Assert.Equal(text, ScreenFile.Serialize(parsed.Layout!));
            foreach (var piece in layout.Pieces)
            {
                Assert.True(ScreenRenderer.Overflow(piece) == null, $"{template.Id}: o texto de \"{piece.Name}\" não cabe");
                Assert.True(piece.X >= 0 && piece.Y >= 0 && piece.X + piece.Width <= ScreenLayout.Width && piece.Y + piece.Height <= ScreenLayout.Height
                            || piece.List != null, $"{template.Id}: \"{piece.Name}\" sai do palco");
            }
        }

        // Todos lado a lado, para conferir o visual: %TEMP%\csharplab-modelos.png
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Background = Brushes.Black };
        foreach (var template in ScreenTemplates.All)
            grid.Children.Add(new Border { Width = 480, Height = 270, Margin = new Thickness(4), Child = ScreenThumbnail.Create(template.Layout("Cena")) });
        grid.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        grid.Arrange(new Rect(grid.DesiredSize));
        grid.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)grid.ActualWidth, (int)grid.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(grid);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(Path.GetTempPath(), "csharplab-modelos.png"));
        encoder.Save(file);
        return Task.CompletedTask;
    });

    [Fact]
    public void Cena_nova_de_cada_modelo_compila_sem_avisos() => Ui.Run(async () =>
    {
        var dialogs = new FakeDialogs();
        using var vm = new MainViewModel(new AppSettings()) { Dialogs = dialogs, Terminal = new FakeTerminal() };
        await vm.InitializeAsync();
        var created = ProjectCreator.CreateGameProject(Ui.NewFolder("modelos"), "Modelos");
        await vm.OpenFolderAsync(created.Directory, created.ProjectPath, promptForUnsaved: false);
        await Ui.WaitUntil(() => vm.Projects.Count > 0, 60_000, "projetos");

        foreach (var template in ScreenTemplates.All)
        {
            dialogs.TextAnswer = template.Id + "Scene";
            dialogs.TemplateAnswer = template.Id;
            vm.NewScreenCommand.Execute(null);
            var screen = File.ReadAllText(GameScreens.PathOf(created.Directory, template.Id + "Scene"));
            Assert.Equal(ScreenFile.Serialize(template.Layout(template.Id + "Scene")).ReplaceLineEndings(), screen.ReplaceLineEndings());
        }
        // Cancelar o modelo não cria nada.
        dialogs.TextAnswer = "Nope";
        dialogs.TemplateAnswer = null;
        vm.NewScreenCommand.Execute(null);
        Assert.False(File.Exists(GameScreens.PathOf(created.Directory, "Nope")));

        vm.SaveAllCommand.Execute(null);
        var code = File.ReadAllText(created.ProgramPath);
        Assert.Contains("game.Find(\"Attack\").OnClick(() =>", code);
        Assert.Contains("game.Find(\"Play\").OnClick(() =>", code);
        var root = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(code).GetRoot();
        Assert.Empty(GameAssist.CheckFindNames(root, created.Directory).Select(h => h.Message));

        var build = await BuildService.BuildAsync(created.ProjectPath, null, CancellationToken.None);
        Assert.True(build.Success, string.Join("\n", build.Diagnostics.Select(d => $"{d.Id} {d.Message}")) + "\n" + build.Log);
        Assert.Empty(build.Diagnostics.Where(d => d.Severity == BuildSeverity.Warning).Select(d => $"{d.Id} {d.Message}"));
    }, timeoutSeconds: 300);
}
