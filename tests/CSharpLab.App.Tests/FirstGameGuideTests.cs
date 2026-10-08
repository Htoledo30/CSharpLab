using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CSharpLab.Core.Build;
using CSharpLab.Core.Language;
using CSharpLab.Core.Projects;
using CSharpLab.Core.Settings;
using CSharpLab.GameEngine;
using CSharpLab.Screens;
using CSharpLab.ViewModels;
using CSharpLab.Views;

namespace CSharpLab.App.Tests;

/// <summary>
/// O guia "Seu primeiro jogo em 5 minutos" tem que dar certo de verdade: o teste segue os passos dele
/// (criar o jogo, botão Rest com o código do guia, cena Cave) e confere que o jogo compila sem avisos.
/// </summary>
public sealed class FirstGameGuideTests
{
    [Fact]
    public void Os_passos_do_guia_funcionam_de_ponta_a_ponta() => Ui.Run(async () =>
    {
        var dialogs = new FakeDialogs();
        using var vm = new MainViewModel(new AppSettings()) { Dialogs = dialogs, Terminal = new FakeTerminal() };
        await vm.InitializeAsync();

        // 1. Crie o jogo: já vem com Start e Forest.
        var created = ProjectCreator.CreateGameProject(Ui.NewFolder("guia"), "MeuJogo");
        await vm.OpenFolderAsync(created.Directory, created.ProjectPath, promptForUnsaved: false);
        await Ui.WaitUntil(() => vm.Projects.Count > 0, 60_000, "projetos");
        Assert.Equal(["Start", "Forest"], vm.GameScenes().Select(s => s.Name));

        // 4. Botão "Rest" na tela Start, com o código do guia entre as chaves.
        var screen = vm.OpenFile(GameScreens.PathOf(created.Directory, "Start"))!;
        var model = new ScreenDesignerModel(screen);
        try
        {
            Assert.NotNull(model.Add(PieceType.Button));
            Assert.Null(model.Rename("Rest"));
            model.Edit("Rest", p => p.Text = "Descansar");
        }
        finally
        {
            model.Dispose();
        }
        int? caret = null;
        vm.GoToRequested += (_, _, _, o) => caret = o;
        vm.WritePieceHandler(created.Directory, "Start", "Rest", "OnClick");
        var program = vm.FindDocument(created.ProgramPath)!;
        var step4 = GuideWindow.Steps[3].Code!;
        program.Document.Insert(caret!.Value, step4.Replace("\n", "\n        "));
        Assert.Contains("gold -= 1;", program.Document.Text);

        // 5. Cena nova "Cave" e o botão passa a levar até ela.
        dialogs.TextAnswer = "Cave";
        vm.NewScreenCommand.Execute(null);
        Assert.True(File.Exists(GameScreens.PathOf(created.Directory, "Cave")));
        var text = program.Document.Text;
        int start = text.IndexOf(step4.Split('\n')[0], StringComparison.Ordinal);
        int end = text.IndexOf(';', text.IndexOf("game.Write(\"Você descansou", start, StringComparison.Ordinal)) + 1;
        program.Document.Replace(start, end - start, GuideWindow.Steps[4].Code!);

        vm.SaveAllCommand.Execute(null);
        var code = File.ReadAllText(created.ProgramPath);
        Assert.Contains("game.Find(\"Rest\").OnClick(() =>", code);
        Assert.Contains("game.GoTo(\"Cave\");", code);
        Assert.Contains("game.Scene(\"Cave\"", code);
        var root = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(code).GetRoot();
        Assert.Empty(GameAssist.CheckFindNames(root, created.Directory).Select(h => h.Message));
        Assert.Empty(GameAssist.CheckBuildCallsInDrawnScenes(root, created.Directory).Select(h => h.Message));

        var build = await BuildService.BuildAsync(created.ProjectPath, null, CancellationToken.None);
        Assert.True(build.Success, string.Join("\n", build.Diagnostics.Select(d => $"{d.Id} {d.Message}")) + "\n" + build.Log);
        Assert.Empty(build.Diagnostics.Where(d => d.Severity == BuildSeverity.Warning).Select(d => $"{d.Id} {d.Message}"));
    }, timeoutSeconds: 300);

    /// <summary>O guia aberto, fora da tela; a imagem fica em %TEMP%\csharplab-guia.png para conferir o visual.</summary>
    [Fact]
    public void Guia_aparece_com_os_cinco_passos() => Ui.Run(async () =>
    {
        using var vm = new MainViewModel(new AppSettings()) { Dialogs = new FakeDialogs(), Terminal = new FakeTerminal() };
        var guide = new GuideWindow(vm) { ShowActivated = false, Left = -10000, Top = -10000, Height = 900 };
        guide.Show();
        await Task.Delay(200);
        guide.UpdateLayout();
        try
        {
            Assert.Equal(5, GuideWindow.Steps.Length);
            var root = (FrameworkElement)guide.Content;
            var bitmap = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(Path.GetTempPath(), "csharplab-guia.png"));
            encoder.Save(file);
        }
        finally
        {
            guide.Close();
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
