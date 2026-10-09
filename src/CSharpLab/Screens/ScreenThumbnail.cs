using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CSharpLab.GameEngine;

namespace CSharpLab.Screens;

/// <summary>Uma tela inteira em miniatura, desenhada como o jogo desenha (mapa do jogo, modelos de tela).</summary>
internal static class ScreenThumbnail
{
    /// <summary>As peças visíveis no começo, no tema atual (Theme.Current), encolhidas para caber.</summary>
    public static FrameworkElement Create(ScreenLayout layout)
    {
        var stage = new Canvas { Width = ScreenLayout.Width, Height = ScreenLayout.Height, Background = Theme.Background, ClipToBounds = true };
        if (ScreenRenderer.Background(layout) is { } background) stage.Children.Add(background);
        var context = new RenderContext { Live = false, Members = list => layout.MembersOf(list.Name) };
        foreach (var piece in layout.Pieces)
        {
            if (!piece.Visible || piece.List != null && layout.ListOf(piece) != null) continue;
            Add(stage, piece, piece.X, piece.Y, context);
            if (piece.Type != PieceType.List) continue;
            foreach (var member in layout.MembersOf(piece.Name).Where(m => m.Visible))
                Add(stage, member, piece.X + member.X, piece.Y + member.Y, context);
        }
        return new Viewbox { Child = stage, Stretch = Stretch.UniformToFill };
    }

    private static void Add(Canvas stage, Piece piece, double x, double y, RenderContext context)
    {
        try
        {
            var element = ScreenRenderer.Create(piece, context);
            Canvas.SetLeft(element, x);
            Canvas.SetTop(element, y);
            stage.Children.Add(element);
        }
        catch (Exception ex)
        {
            Core.Settings.AppPaths.Log(ex, "Miniatura da tela");
        }
    }
}
