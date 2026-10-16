using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using CSharpLab.Core.Files;
using CSharpLab.Core.Settings;
using CSharpLab.GameEngine;
using CSharpLab.Screens;
using CSharpLab.ViewModels;

namespace CSharpLab.App.Tests;

public sealed class EngineReviewStudioTests
{
    [Fact]
    public void Fonte_normal_e_fonte_do_tema_sao_escolhas_distintas_no_painel() => Ui.Run(async () =>
    {
        var dir = Ui.NewFolder("review-font");
        var path = Path.Combine(dir, "Screens", "Test.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(Path.Combine(dir, "GameStyle.json"), GameStyle.Serialize(ThemeName.Fantasy));
        var doc = new DocumentViewModel("""{"pieces":[{"type":"Text","name":"Title","text":"Título","size":32}]}""",
            path, TextFileIO.Utf8NoBom, null);
        using var vm = new MainViewModel(new AppSettings()) { Dialogs = new FakeDialogs(), Terminal = new FakeTerminal() };
        var view = new ScreenEditorView(doc, vm);
        view.Properties.AllTabs = true;
        var window = new Window { Content = view, Width = 1300, Height = 1000, ShowInTaskbar = false, ShowActivated = false, Left = -10000, Top = -10000 };
        window.Show();
        try
        {
            view.Model.Select("Title");
            await Task.Delay(50);
            window.UpdateLayout();
            ToggleButton FontButton(string text) => Descendants(view.Properties).OfType<ToggleButton>()
                .Single(b => b.Content is TextBlock label && label.Text == text);
            Assert.True(FontButton("Do tema").IsChecked);
            FontButton("Normal").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            var saved = ScreenFile.Parse(doc.Document.Text);
            Assert.Equal(Font.Normal, saved.Layout!.Find("Title")!.Font);
            Assert.Equal(Font.Normal, Theme.FontFor(saved.Layout.Find("Title")!));
            FontButton("Do tema").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            saved = ScreenFile.Parse(doc.Document.Text);
            Assert.Null(saved.Layout!.Find("Title")!.Font);
            Assert.Equal(Font.Fantasy, Theme.FontFor(saved.Layout.Find("Title")!));
        }
        finally { window.Close(); view.Detach(); }
    });

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Contador_editado_no_estudio_reabre_com_maximo_nao_positivo(int max) => Ui.Run(() =>
    {
        var doc = new DocumentViewModel("""{"pieces":[{"type":"Counter","name":"Amount","min":-10,"max":10}]}""",
            Path.Combine(Ui.NewFolder(), "Screens", "Test.json"), TextFileIO.Utf8NoBom, null);
        using var model = new ScreenDesignerModel(doc);
        model.Edit("Amount", piece => piece.Max = max);
        using var reopened = new ScreenDesignerModel(doc);
        Assert.Null(reopened.Error);
        Assert.Equal(max, reopened.Layout!.Find("Amount")!.CounterMax);
        return Task.CompletedTask;
    });

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
