using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using CSharpLab.ViewModels;

namespace CSharpLab.Views;

/// <summary>Onde a pessoa está agora: a tela de uma cena, ou o código dentro de um game.Scene.</summary>
public sealed record SceneContext(string Scene, bool OnScreen);

/// <summary>
/// O menu do botão Cenas (ao lado do Executar): trocar entre a tela e o código da cena atual
/// e abrir qualquer cena do jogo.
/// </summary>
public static class ScenesMenu
{
    private const string ScreenGlyph = "";
    private const string CodeGlyph = "";
    private const string AddGlyph = "";

    public static ContextMenu Build(MainViewModel vm, SceneContext? current)
    {
        var menu = new ContextMenu();
        var scenes = vm.GameScenes();

        // Troca rápida: da tela para o código da mesma cena, e do código para a tela.
        if (current != null && vm.GameDirectoryOf(vm.ActiveDocument?.FilePath) is { } dir)
        {
            var scene = current.Scene;
            bool hasScreen = vm.HasScreen(dir, scene);
            var item = current.OnScreen
                ? Item(CodeGlyph, $"Ver o código da cena {scene}", null, () => vm.GoToSceneCode(dir, scene))
                : Item(ScreenGlyph, hasScreen ? $"Ver a tela da cena {scene}" : $"Desenhar a tela da cena {scene}…", null,
                    () => vm.OpenSceneScreen(dir, scene));
            item.FontWeight = FontWeights.SemiBold;
            menu.Items.Add(item);
            menu.Items.Add(new Separator());
        }

        menu.Items.Add(new MenuItem
        {
            Header = new TextBlock { Text = "CENAS DO JOGO", FontSize = 11, FontWeight = FontWeights.SemiBold },
            IsEnabled = false,
        });
        if (scenes.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "Nenhuma cena ainda: escreva game.Scene(\"Nome\", …)", IsEnabled = false });
        }
        foreach (var scene in scenes)
        {
            var note = scene.IsStart ? "começa aqui"
                : !scene.InCode ? "falta no código"
                : !scene.HasScreen ? "só código"
                : null;
            var name = scene.Name;
            var item = Item(scene.HasScreen ? ScreenGlyph : CodeGlyph, name, note, () => vm.OpenGameScene(name));
            item.ToolTip = scene.HasScreen
                ? $"Abrir a tela desenhada da cena \"{name}\""
                : $"A cena \"{name}\" é feita só com código: abre o game.Scene dela";
            if (string.Equals(current?.Scene, name, StringComparison.OrdinalIgnoreCase))
                item.SetResourceReference(Control.ForegroundProperty, "Accent");
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        var add = Item(AddGlyph, "Nova tela do jogo…", null, () => vm.NewScreenCommand.Execute(null));
        menu.Items.Add(add);
        return menu;
    }

    private static MenuItem Item(string glyph, string text, string? note, Action click)
    {
        var icon = new TextBlock { Text = glyph, FontSize = 12, Width = 18, Margin = new Thickness(0, 1, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        icon.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(icon);
        // TextBlock (não texto solto): o "_" de um nome não vira tecla de atalho.
        header.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        if (note != null)
        {
            var hint = new TextBlock { Text = note, FontSize = 11.5, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
            header.Children.Add(hint);
        }
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => click();
        return item;
    }

    /// <summary>Abre o menu logo abaixo do botão.</summary>
    public static void Show(ContextMenu menu, FrameworkElement button)
    {
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.VerticalOffset = 4;
        menu.IsOpen = true;
    }
}
