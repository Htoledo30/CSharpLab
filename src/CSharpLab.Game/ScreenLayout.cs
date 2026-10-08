using System.Windows;

namespace CSharpLab.GameEngine;

// Uma tela desenhada na aba Tela (arquivo Screens/<Cena>.json). O jogo e o editor visual usam este modelo.

internal enum PieceType { Text, Button, Bar, Image, Box, Input, Messages, List }

internal enum TextAlign { Left, Center, Right }

/// <summary>Uma peça da tela. As posições são no palco de 960×540.</summary>
internal sealed class Piece
{
    public PieceType Type { get; set; }
    public string Name { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    /// <summary>Texto (Text, Button), rótulo (Bar) ou pergunta (Input).</summary>
    public string? Text { get; set; }
    public double? Size { get; set; }
    public bool? Bold { get; set; }
    public TextAlign? Align { get; set; }
    public Color? Color { get; set; }
    public int? Value { get; set; }
    public int? Max { get; set; }
    public string? Image { get; set; }
    public bool Visible { get; set; } = true;

    // Estilos (null: o normal de cada peça).
    public Font? Font { get; set; }
    public bool? Italic { get; set; }
    public bool? Shadow { get; set; }
    /// <summary>Texto comprido: em vez de cortar o que não cabe, a peça ganha uma barra de rolagem.</summary>
    public bool? Scroll { get; set; }
    public Shade? Shade { get; set; }
    /// <summary>Preenchimento da Caixa, de 0 (invisível) a 100 (cheia).</summary>
    public int? Opacity { get; set; }
    public bool? Border { get; set; }
    public Corner? Corner { get; set; }
    public ButtonStyle? Style { get; set; }
    /// <summary>false: o botão (ou campo, ou imagem) fica apagado e não responde.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Tecla que executa o OnClick do botão (null: só o clique).</summary>
    public string? Shortcut { get; set; }

    /// <summary>
    /// Nome da Lista de que a peça faz parte (null: peça solta). Peças de lista formam o cartão modelo,
    /// repetido para cada item; X e Y delas contam a partir do canto do cartão.
    /// </summary>
    public string? List { get; set; }

    // Só a Lista: tamanho de cada cartão e o espaço entre eles.
    public double? CardWidth { get; set; }
    public double? CardHeight { get; set; }
    public double? Gap { get; set; }

    public Piece Clone() => (Piece)MemberwiseClone();

    // Valores que valem quando o arquivo não diz nada.
    public double FontSize => Size ?? Type switch
    {
        PieceType.Button => 17,
        PieceType.Bar or PieceType.Input or PieceType.Messages => 16,
        _ => 20,
    };

    public int BarValue => Value ?? 100;
    public int BarMax => Max is > 0 ? Max.Value : 100;

    /// <summary>Caixa sem cor: fundo cheio, sem borda. Com cor: a cor bem clarinha, com borda.</summary>
    public int BoxOpacity => Math.Clamp(Opacity ?? (Color != null ? 25 : 100), 0, 100);
    public bool HasBorder => Border ?? Color != null;

    public double CardW => CardWidth is > 0 ? CardWidth.Value : 180;
    public double CardH => CardHeight is > 0 ? CardHeight.Value : 220;
    public double CardGap => Gap is >= 0 ? Gap.Value : 16;

    /// <summary>O que cada tipo de peça aceita, para o código e o editor mostrarem só o que faz sentido.</summary>
    public static bool Supports(PieceType type, string property) => property switch
    {
        nameof(Text) => type is PieceType.Text or PieceType.Button or PieceType.Bar or PieceType.Input or PieceType.List,
        nameof(Size) => type is PieceType.Text or PieceType.Button or PieceType.Input or PieceType.Messages,
        nameof(Bold) or nameof(Align) or nameof(Italic) or nameof(Shadow) or nameof(Scroll) => type is PieceType.Text,
        nameof(Font) => type is PieceType.Text or PieceType.Button or PieceType.Input or PieceType.Messages,
        nameof(Color) or nameof(Shade) => type is not (PieceType.Image or PieceType.Input or PieceType.Messages or PieceType.List),
        nameof(Opacity) or nameof(Border) or nameof(Corner) => type is PieceType.Box,
        nameof(Style) => type is PieceType.Button,
        nameof(Shortcut) => type is PieceType.Button,
        nameof(Enabled) => type is PieceType.Button or PieceType.Input or PieceType.Image,
        nameof(Value) or nameof(Max) => type is PieceType.Bar,
        nameof(Image) => type is PieceType.Image,
        nameof(CardWidth) or nameof(CardHeight) or nameof(Gap) => type is PieceType.List,
        nameof(List) => type is not (PieceType.List or PieceType.Messages),
        _ => true,
    };

    /// <summary>Peça nova, com tamanho e textos de exemplo bons para começar.</summary>
    public static Piece CreateDefault(PieceType type, string name, double x, double y)
    {
        var piece = new Piece { Type = type, Name = name, X = x, Y = y };
        switch (type)
        {
            case PieceType.Text:
                (piece.Width, piece.Height, piece.Text) = (320, 48, "Texto");
                break;
            case PieceType.Button:
                (piece.Width, piece.Height, piece.Text) = (180, 52, "Botão");
                break;
            case PieceType.Bar:
                (piece.Width, piece.Height, piece.Text, piece.Value, piece.Max, piece.Color) = (280, 44, "Vida", 100, 100, GameEngine.Color.Green);
                break;
            case PieceType.Image:
                (piece.Width, piece.Height) = (200, 200);
                break;
            case PieceType.Box:
                (piece.Width, piece.Height) = (320, 200);
                break;
            case PieceType.Input:
                (piece.Width, piece.Height, piece.Text) = (420, 88, "Qual é o seu nome?");
                break;
            case PieceType.Messages:
                (piece.Width, piece.Height) = (520, 180);
                break;
            case PieceType.List:
                (piece.Width, piece.Height, piece.CardWidth, piece.CardHeight, piece.Gap) = (592, 240, 180, 240, 16);
                break;
        }
        return piece;
    }

    /// <summary>Nome da peça em português, para mensagens e para o editor.</summary>
    public static string Describe(PieceType type) => type switch
    {
        PieceType.Text => "Texto",
        PieceType.Button => "Botão",
        PieceType.Bar => "Barra",
        PieceType.Image => "Imagem",
        PieceType.Box => "Caixa",
        PieceType.Input => "Campo de escrita",
        PieceType.Messages => "Mensagens",
        PieceType.List => "Lista",
        _ => type.ToString(),
    };
}

/// <summary>A tela inteira: fundo opcional e as peças, na ordem de desenho (a última fica por cima).</summary>
internal sealed class ScreenLayout
{
    public const int CurrentFormat = 1;
    public const double Width = 960, Height = 540;

    public string? Background { get; set; }
    public List<Piece> Pieces { get; set; } = [];

    public ScreenLayout Clone() => new() { Background = Background, Pieces = Pieces.Select(p => p.Clone()).ToList() };

    public Piece? Find(string name) => Pieces.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>As peças do cartão modelo da Lista, na ordem de desenho.</summary>
    public IReadOnlyList<Piece> MembersOf(string listName) =>
        Pieces.Where(p => p.List != null && string.Equals(p.List, listName, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>A Lista de que a peça faz parte (null: peça solta).</summary>
    public Piece? ListOf(Piece piece) => piece.List != null ? Find(piece.List) : null;

    /// <summary>
    /// Onde cada cartão fica dentro da Lista: da esquerda para a direita, e outra fileira quando não cabe mais.
    /// O mesmo cálculo no jogo e no editor.
    /// </summary>
    public static (int Columns, double Height) CardGrid(Piece list, int count)
    {
        double w = list.CardW, gap = list.CardGap;
        int columns = Math.Max(1, (int)Math.Floor((list.Width + gap + 0.5) / (w + gap)));
        int rows = count == 0 ? 0 : (count + columns - 1) / columns;
        return (columns, rows == 0 ? 0 : rows * (list.CardH + gap) - gap);
    }

    public static Point CardPosition(Piece list, int index, int columns) =>
        new(index % columns * (list.CardW + list.CardGap), index / columns * (list.CardH + list.CardGap));

    /// <summary>Nome livre como "Button1", "Button2"… (o editor sugere e a pessoa pode trocar).</summary>
    public string NewName(PieceType type)
    {
        for (int i = 1; ; i++)
        {
            var name = type + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (Find(name) == null) return name;
        }
    }

    /// <summary>Tela inicial de uma cena nova: um título e um botão, para não começar do zero.</summary>
    public static ScreenLayout CreateDefault(string sceneName)
    {
        var title = Piece.CreateDefault(PieceType.Text, "Title", 48, 40);
        (title.Text, title.Size, title.Bold, title.Width, title.Height) = (sceneName, 32, true, 600, 56);
        var button = Piece.CreateDefault(PieceType.Button, "Continue", 48, 440);
        button.Text = "Continuar";
        return new ScreenLayout { Pieces = [title, button] };
    }
}
