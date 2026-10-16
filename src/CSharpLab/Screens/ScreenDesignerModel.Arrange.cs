using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using CSharpLab.GameEngine;

namespace CSharpLab.Screens;

/// <summary>Como alinhar as peças (pelas bordas ou pelo meio).</summary>
public enum AlignEdge { Left, CenterX, Right, Top, CenterY, Bottom }

// Arrumar a tela: alinhar, distribuir, mesmo tamanho, copiar o estilo de uma peça para outras e
// soltar imagens do Windows no palco. Cada ação é um passo só no desfazer.
public sealed partial class ScreenDesignerModel
{
    /// <summary>O maior lado de uma imagem solta no palco (as grandes chegam reduzidas, sem distorcer).</summary>
    public const double DroppedImageMax = 320;

    /// <summary>A aparência copiada (Ctrl+Shift+C). Fica no programa todo: copiar na Vila e colar na Floresta funciona.</summary>
    private static Piece? _style;

    /// <summary>As selecionadas que se mexem sozinhas (as do cartão de uma Lista selecionada vão junto com ela).</summary>
    private static List<Piece> Movers(ScreenLayout layout, IReadOnlyCollection<string> names)
    {
        var set = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        return layout.Pieces.Where(p => set.Contains(p.Name) && !(p.List != null && set.Contains(p.List))).ToList();
    }

    /// <summary>
    /// Alinha as selecionadas pela borda (ou pelo meio) do grupo. Com uma peça só, alinha com a tela
    /// (ou com o cartão, se ela é de uma Lista): "Centralizar na tela". False se nada mudou.
    /// </summary>
    public bool Align(AlignEdge edge)
    {
        if (Layout == null || _selected.Count == 0) return false;
        var layout = Layout.Clone();
        var pieces = Movers(layout, _selected);
        if (pieces.Count == 0) return false;
        var area = pieces.Count > 1
            ? pieces.Select(p => AbsoluteBounds(layout, p)).Aggregate(Rect.Union)
            : layout.ListOf(pieces[0]) is { } list ? CardSlot(list) : new Rect(0, 0, ScreenLayout.Width, ScreenLayout.Height);
        foreach (var piece in pieces)
        {
            var r = AbsoluteBounds(layout, piece);
            switch (edge)
            {
                case AlignEdge.Left: r.X = area.Left; break;
                case AlignEdge.CenterX: r.X = area.Left + (area.Width - r.Width) / 2; break;
                case AlignEdge.Right: r.X = area.Right - r.Width; break;
                case AlignEdge.Top: r.Y = area.Top; break;
                case AlignEdge.CenterY: r.Y = area.Top + (area.Height - r.Height) / 2; break;
                case AlignEdge.Bottom: r.Y = area.Bottom - r.Height; break;
            }
            Place(layout, piece, ClampInside(r));
        }
        foreach (var piece in pieces) Reparent(layout, piece);
        return CommitIfChanged(layout);
    }

    /// <summary>
    /// Espaço igual entre as selecionadas (3 ou mais), na horizontal ou na vertical. A primeira e a última
    /// ficam onde estão. False se nada mudou.
    /// </summary>
    public bool Distribute(bool horizontal)
    {
        if (Layout == null) return false;
        var layout = Layout.Clone();
        var pieces = Movers(layout, _selected);
        if (pieces.Count < 3) return false;
        var ordered = pieces.Select(p => (Piece: p, Rect: AbsoluteBounds(layout, p)))
            .OrderBy(t => horizontal ? t.Rect.X + t.Rect.Width / 2 : t.Rect.Y + t.Rect.Height / 2)
            .ToList();
        double start = horizontal ? ordered[0].Rect.Left : ordered[0].Rect.Top;
        double end = horizontal ? ordered.Max(t => t.Rect.Right) : ordered.Max(t => t.Rect.Bottom);
        double gap = (end - start - ordered.Sum(t => horizontal ? t.Rect.Width : t.Rect.Height)) / (ordered.Count - 1);
        double at = start;
        foreach (var (piece, rect) in ordered)
        {
            var r = rect;
            if (horizontal) r.X = at; else r.Y = at;
            at += (horizontal ? r.Width : r.Height) + gap;
            Place(layout, piece, ClampInside(r));
        }
        foreach (var (piece, _) in ordered) Reparent(layout, piece);
        return CommitIfChanged(layout);
    }

    /// <summary>As outras selecionadas ficam com a largura e/ou a altura da primeira que foi selecionada. False se nada mudou.</summary>
    public bool SameSize(bool width, bool height)
    {
        if (Layout == null || _selected.Count < 2 || Layout.Find(_selected[0]) is not { } model) return false;
        var layout = Layout.Clone();
        foreach (var name in _selected.Skip(1))
        {
            if (layout.Find(name) is not { } piece) continue;
            if (width) piece.Width = model.Width;
            if (height) piece.Height = model.Height;
        }
        return CommitIfChanged(layout);
    }

    private bool CommitIfChanged(ScreenLayout layout)
    {
        if (ScreenFile.Serialize(layout) == Doc.Document.Text) return false;
        Commit(layout);
        return true;
    }

    // ------------------------------------------------------------------ estilo

    /// <summary>Já tem uma aparência copiada para colar.</summary>
    public bool HasStyle => _style != null;

    /// <summary>De que peça é a aparência copiada (para o painel dizer), ou null.</summary>
    public string? CopiedStyleName => _style?.Name;

    /// <summary>Copia a aparência da peça selecionada (cores, letra, sombra, cantos…), sem o texto, o nome e o lugar.</summary>
    public bool CopyStyle()
    {
        if (Selected is not { } piece) return false;
        _style = piece.Clone();
        return true;
    }

    /// <summary>Cola a aparência copiada em todas as selecionadas. Retorna quantas mudaram.</summary>
    public int PasteStyle() => _style is { } style ? UseStyle(style) : 0;

    /// <summary>Passa a aparência de <paramref name="style"/> (um estilo copiado ou um de Meus estilos) para as selecionadas. Retorna quantas mudaram.</summary>
    internal int UseStyle(Piece style)
    {
        if (Layout == null || _selected.Count == 0) return 0;
        var layout = Layout.Clone();
        int changed = 0;
        foreach (var name in _selected)
        {
            if (layout.Find(name) is not { } piece) continue;
            var before = ScreenFile.Serialize(new ScreenLayout { Pieces = [piece] });
            ApplyStyle(style, piece);
            if (ScreenFile.Serialize(new ScreenLayout { Pieces = [piece] }) != before) changed++;
        }
        if (changed > 0) Commit(layout);
        return changed;
    }

    /// <summary>
    /// Passa a aparência de uma peça para outra. Do mesmo tipo, vai tudo; de tipos diferentes, só o que quer
    /// dizer a mesma coisa nos dois: a letra (fonte, tamanho, negrito, itálico, alinhamento, sombra e cor da
    /// letra), o preenchimento (cor e tom, entre botão, barra e caixa), a sombra embaixo e os cantos.
    /// </summary>
    internal static void ApplyStyle(Piece from, Piece to)
    {
        if (from.Type == to.Type)
        {
            (to.Size, to.Bold, to.Align, to.Font, to.Italic, to.Shadow, to.TextShadow, to.TextColor) =
                (from.Size, from.Bold, from.Align, from.Font, from.Italic, from.Shadow, from.TextShadow, from.TextColor);
            (to.Color, to.Shade, to.Opacity, to.Border, to.Corner, to.Style, to.BarStyle, to.BarText) =
                (from.Color, from.Shade, from.Opacity, from.Border, from.Corner, from.Style, from.BarStyle, from.BarText);
            (to.BorderColor, to.BorderWidth, to.Gradient, to.GradientDirection, to.Animate) =
                (from.BorderColor, from.BorderWidth, from.Gradient, from.GradientDirection, from.Animate);
            // No Ícone, a moldura é aparência; o desenho (goblin, espada) é o que a peça mostra e fica.
            (to.Frame, to.Back) = (from.Frame, from.Back);
            return;
        }
        if (Piece.HasLetters(from.Type) && Piece.HasLetters(to.Type))
        {
            to.Font = from.Font;
            to.Italic = from.Italic;
            to.Size = from.FontSize == new Piece { Type = to.Type }.FontSize ? null : from.FontSize;
            to.Bold = from.IsBold == Piece.DefaultBold(to.Type) ? null : from.IsBold;
            to.Align = from.LetterAlign == Piece.DefaultAlign(to.Type) ? null : from.LetterAlign;
            bool? shadow = from.HasLetterShadow ? true : null;
            if (to.Type == PieceType.Button) to.TextShadow = shadow; else to.Shadow = shadow;
            var letters = from.Type == PieceType.Text ? from.Color : from.TextColor;
            if (to.Type == PieceType.Text) to.Color = letters; else to.TextColor = letters;
        }
        if (Fills(from.Type) && Fills(to.Type)) (to.Color, to.Shade) = (from.Color, from.Shade);
        if (DropsShadow(from.Type) && DropsShadow(to.Type)) to.Shadow = from.Shadow;
        if (Piece.Supports(from.Type, nameof(Piece.Corner)) && Piece.Supports(to.Type, nameof(Piece.Corner))) to.Corner = from.Corner;

        static bool Fills(PieceType type) => type is PieceType.Button or PieceType.Bar or PieceType.Box;
        static bool DropsShadow(PieceType type) => type is PieceType.Button or PieceType.Box;
    }

    // ------------------------------------------------------------------ imagens soltas no palco

    /// <summary>
    /// Imagens que já estão na pasta Assets viram peças Imagem, a partir deste ponto (o meio da primeira),
    /// com o formato da imagem. Todas num passo só do desfazer, já selecionadas.
    /// </summary>
    internal IReadOnlyList<Piece> AddImages(IReadOnlyList<(string Image, Size? Natural)> images, Point at)
    {
        if (Layout == null || images.Count == 0) return [];
        var layout = Layout.Clone();
        var added = new List<Piece>();
        foreach (var (image, natural) in images)
        {
            var piece = Piece.CreateDefault(PieceType.Image, layout.NewName(PieceType.Image), 0, 0);
            piece.Image = image;
            if (natural is { Width: > 0, Height: > 0 } size)
            {
                double scale = Math.Min(1, DroppedImageMax / Math.Max(size.Width, size.Height));
                piece.Width = Math.Max(MinSize, Math.Round(size.Width * scale));
                piece.Height = Math.Max(MinSize, Math.Round(size.Height * scale));
            }
            piece.X = Math.Round(at.X - piece.Width / 2 + added.Count * 24);
            piece.Y = Math.Round(at.Y - piece.Height / 2 + added.Count * 24);
            ClampInside(piece);
            layout.Pieces.Add(piece);
            Reparent(layout, piece);
            added.Add(piece);
        }
        Commit(layout);
        SelectMany(added.Select(p => p.Name));
        return added.Select(p => Layout!.Find(p.Name)!).ToList();
    }

    /// <summary>É uma imagem que a tela sabe mostrar (png, jpg, gif, bmp)?</summary>
    internal static bool IsImageFile(string path) => ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>Largura e altura da imagem em pixels, sem carregar ela inteira (null se não deu para ler).</summary>
    internal static Size? ImageSize(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            return new Size(frame.PixelWidth, frame.PixelHeight);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or ArgumentException)
        {
            return null;
        }
    }
}

/// <summary>Os nomes das opções de alinhar, iguais no menu e no painel.</summary>
internal static class ScreenArrange
{
    public static IReadOnlyList<(string Text, AlignEdge Edge)> Edges(bool many) => many
        ?
        [
            ("Pela esquerda", AlignEdge.Left), ("Pelo meio, na horizontal", AlignEdge.CenterX), ("Pela direita", AlignEdge.Right),
            ("Pelo topo", AlignEdge.Top), ("Pelo meio, na vertical", AlignEdge.CenterY), ("Pela base", AlignEdge.Bottom),
        ]
        :
        [
            ("Na esquerda", AlignEdge.Left), ("No centro, na horizontal", AlignEdge.CenterX), ("Na direita", AlignEdge.Right),
            ("No alto", AlignEdge.Top), ("No centro, na vertical", AlignEdge.CenterY), ("Embaixo", AlignEdge.Bottom),
        ];
}
