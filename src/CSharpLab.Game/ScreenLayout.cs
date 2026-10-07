namespace CSharpLab.GameEngine;

// Uma tela desenhada na aba Tela (arquivo Screens/<Cena>.json). O jogo e o editor visual usam este modelo.

internal enum PieceType { Text, Button, Bar, Image, Box, Input, Messages }

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

    /// <summary>O que cada tipo de peça aceita, para o código e o editor mostrarem só o que faz sentido.</summary>
    public static bool Supports(PieceType type, string property) => property switch
    {
        nameof(Text) => type is PieceType.Text or PieceType.Button or PieceType.Bar or PieceType.Input,
        nameof(Size) => type is PieceType.Text or PieceType.Button or PieceType.Input or PieceType.Messages,
        nameof(Bold) or nameof(Align) => type is PieceType.Text,
        nameof(Color) => type is not (PieceType.Image or PieceType.Input or PieceType.Messages),
        nameof(Value) or nameof(Max) => type is PieceType.Bar,
        nameof(Image) => type is PieceType.Image,
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
