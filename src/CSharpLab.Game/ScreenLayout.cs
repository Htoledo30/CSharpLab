using System.Windows;

namespace CSharpLab.GameEngine;

// Uma tela desenhada no Estúdio (arquivo Screens/<Cena>.json). O jogo e o editor visual usam este modelo.

internal enum PieceType { Text, Button, Bar, Image, Box, Input, Messages, List, Choice, Counter, Wheel, Grid, Icon, Dice }

/// <summary>O que uma casa da Grade mostra (só no jogo: o código escreve com game.Find("Board").Cell(x, y)).</summary>
internal sealed record CellData(string Text, Color? Color);

internal enum TextAlign { Left, Center, Right }

/// <summary>Uma peça da tela. As posições são no palco de 960×540.</summary>
internal sealed class Piece
{
    /// <summary>Identidade no jogo, preservada quando o Show redesenha o mesmo item da lista.</summary>
    public object RuntimeId { get; set; } = new();
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
    /// <summary>Só o Botão: sombra atrás das letras (o Shadow dele é a sombra do botão inteiro).</summary>
    public bool? TextShadow { get; set; }
    /// <summary>Texto comprido: em vez de cortar o que não cabe, a peça ganha uma barra de rolagem.</summary>
    public bool? Scroll { get; set; }
    public Shade? Shade { get; set; }
    /// <summary>Preenchimento da Caixa, de 0 (invisível) a 100 (cheia).</summary>
    public int? Opacity { get; set; }
    public bool? Border { get; set; }
    /// <summary>Só a Caixa: cor da borda (null: a automática, que combina com a cor da caixa).</summary>
    public Color? BorderColor { get; set; }
    /// <summary>Só a Caixa: grossura da borda, de 1 a 20 (null: fininha).</summary>
    public int? BorderWidth { get; set; }
    /// <summary>Só a Caixa: a segunda cor do degradê (null: uma cor só).</summary>
    public Color? Gradient { get; set; }
    public GradientDirection? GradientDirection { get; set; }
    public Corner? Corner { get; set; }
    public ButtonStyle? Style { get; set; }
    /// <summary>Cor da letra do botão (null: automática, a que aparece bem em cima do fundo).</summary>
    public Color? TextColor { get; set; }
    public BarStyle? BarStyle { get; set; }
    public BarText? BarText { get; set; }
    /// <summary>Só a Barra: quando o valor muda, ela desliza até o novo e deixa um rastro do que perdeu.</summary>
    public bool? Animate { get; set; }
    /// <summary>
    /// Só no Estúdio: travada, o mouse não pega nem arrasta a peça no palco (bom para fundos e molduras).
    /// O jogo ignora.
    /// </summary>
    public bool Locked { get; set; }
    /// <summary>false: o botão (ou campo, ou imagem) fica apagado e não responde.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Só Mensagens: false, a caixa não recebe o game.Write (só o que for escrito nela, com game.Find("Nome").Write).</summary>
    public bool GameWrite { get; set; } = true;

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

    /// <summary>Escolha e Roleta: as opções (na Roleta, cada uma é uma fatia).</summary>
    public List<string>? Options { get; set; }
    /// <summary>Escolha: a opção escolhida (null: nenhuma). Roleta: onde a seta aponta.</summary>
    public string? Selected { get; set; }
    /// <summary>Só o Contador: o menor valor e de quanto em quanto ele anda.</summary>
    public int? Min { get; set; }
    public int? Step { get; set; }
    /// <summary>Escolha: quantas opções por linha (null: automático). Grade: quantas casas por linha.</summary>
    public int? Columns { get; set; }
    /// <summary>Só a Grade: quantas linhas.</summary>
    public int? Rows { get; set; }
    /// <summary>Ícone: o desenho. Botão: o desenho antes do texto (null: sem ícone).</summary>
    public Icon? Icon { get; set; }
    /// <summary>Só o Ícone: a moldura atrás do desenho (null: sem moldura).</summary>
    public IconFrame? Frame { get; set; }
    /// <summary>Só o Ícone: a cor da moldura (null: a cor do ícone, escurinha).</summary>
    public Color? Back { get; set; }
    /// <summary>Só o Dado: quantos lados ele tem (4, 6, 8, 10, 12, 20 ou 100).</summary>
    public int? Sides { get; set; }
    /// <summary>Só no jogo: o que o código pôs em cada casa da Grade (x, y).</summary>
    public Dictionary<(int X, int Y), CellData>? Cells { get; set; }

    public Piece Clone()
    {
        var copy = (Piece)MemberwiseClone();
        copy.RuntimeId = new object();
        copy.Options = Options?.ToList();
        copy.Cells = Cells != null ? new Dictionary<(int X, int Y), CellData>(Cells) : null;
        return copy;
    }

    // Valores que valem quando o arquivo não diz nada.
    public double FontSize => Size ?? Type switch
    {
        PieceType.Button => 17,
        PieceType.Bar or PieceType.Input or PieceType.Messages or PieceType.List or PieceType.Choice => 16,
        PieceType.Wheel => 15,
        PieceType.Grid => 28,
        PieceType.Dice => 32,
        _ => 20,
    };

    /// <summary>As peças que têm letras: todas ganham as mesmas opções de letra (fonte, tamanho, negrito, itálico, alinhamento, sombra e cor).</summary>
    public static bool HasLetters(PieceType type) =>
        type is PieceType.Text or PieceType.Button or PieceType.Bar or PieceType.Input or PieceType.Messages or PieceType.List
            or PieceType.Choice or PieceType.Counter or PieceType.Wheel or PieceType.Grid;

    /// <summary>Alinhamento quando o arquivo não diz nada: botão, lista vazia e as peças novas no centro, o resto à esquerda.</summary>
    public static TextAlign DefaultAlign(PieceType type) =>
        type is PieceType.Button or PieceType.List or PieceType.Choice or PieceType.Wheel or PieceType.Grid ? TextAlign.Center : TextAlign.Left;

    /// <summary>Negrito quando o arquivo não diz nada: o nome da barra, o número do Contador e as fatias da Roleta já vêm em negrito.</summary>
    public static bool DefaultBold(PieceType type) => type is PieceType.Bar or PieceType.Counter or PieceType.Wheel or PieceType.Grid;

    public TextAlign LetterAlign => Align ?? DefaultAlign(Type);
    public bool IsBold => Bold ?? DefaultBold(Type);
    /// <summary>Sombra atrás das letras (no Botão é o TextShadow; nas outras peças, o Shadow).</summary>
    public bool HasLetterShadow => (Type == PieceType.Button ? TextShadow : Shadow) == true;

    public int BarValue => Value ?? 100;
    public int BarMax => Max is > 0 ? Max.Value : 100;

    /// <summary>Caixa sem cor: fundo cheio, sem borda. Com cor: a cor bem clarinha, com borda.</summary>
    public int BoxOpacity => Math.Clamp(Opacity ?? (Color != null ? 25 : 100), 0, 100);
    public bool HasBorder => Border ?? (Color != null || BorderColor != null);
    public int BorderThickness => Math.Clamp(BorderWidth ?? 1, 1, 20);

    // Contador: valor entre o mínimo e o máximo, andando de Step em Step.
    public int CounterMin => Min ?? 0;
    public int CounterMax => Math.Max(CounterMin, Max ?? 99);
    public int CounterStep => Step is > 0 ? Step.Value : 1;
    public int CounterValue => Math.Clamp(Value ?? CounterMin, CounterMin, CounterMax);

    /// <summary>As opções da Escolha ou da Roleta (vazia se a pessoa apagou todas).</summary>
    public IReadOnlyList<string> OptionList => Options ?? [];

    /// <summary>A opção escolhida, como está escrita na lista (null: nenhuma, ou não está mais na lista).</summary>
    public string? SelectedOption => Selected != null ? OptionList.FirstOrDefault(o => string.Equals(o, Selected, StringComparison.OrdinalIgnoreCase)) : null;

    /// <summary>Escolha: opções por linha. Sem escolher, todas numa linha (peça larga) ou uma embaixo da outra (peça alta).</summary>
    public int ChoiceColumns => Columns is > 0 ? Math.Min(Columns.Value, Math.Max(1, OptionList.Count)) : Width >= Height ? Math.Max(1, OptionList.Count) : 1;

    // Grade: de 1 a 20 casas por linha e por coluna.
    public const int MaxCells = 20;
    public int GridColumns => Math.Clamp(Columns ?? 3, 1, MaxCells);
    public int GridRows => Math.Clamp(Rows ?? 3, 1, MaxCells);

    /// <summary>Os dados que existem (de RPG): d4, d6, d8, d10, d12, d20 e d100.</summary>
    public static readonly int[] DiceSizes = [4, 6, 8, 10, 12, 20, 100];

    public int DiceSides => Sides is { } sides && Array.IndexOf(DiceSizes, sides) >= 0 ? sides : 20;
    /// <summary>A face que o dado mostra (de 1 até o número de lados). Sem escolher, o maior.</summary>
    public int DiceValue => Math.Clamp(Value ?? DiceSides, 1, DiceSides);

    public CellData CellAt(int x, int y) => Cells?.GetValueOrDefault((x, y)) ?? new CellData("", null);

    public double CardW => CardWidth is > 0 ? CardWidth.Value : 180;
    public double CardH => CardHeight is > 0 ? CardHeight.Value : 220;
    public double CardGap => Gap is >= 0 ? Gap.Value : 16;

    /// <summary>O que cada tipo de peça aceita, para o código e o editor mostrarem só o que faz sentido.</summary>
    public static bool Supports(PieceType type, string property) => property switch
    {
        nameof(Text) => type is PieceType.Text or PieceType.Button or PieceType.Bar or PieceType.Input or PieceType.List or PieceType.Counter,
        nameof(Size) or nameof(Align) or nameof(Italic) or nameof(Bold) or nameof(Font) => HasLetters(type),
        nameof(Scroll) => type is PieceType.Text,
        nameof(GameWrite) => type is PieceType.Messages,
        nameof(Shadow) => HasLetters(type) || type is PieceType.Box or PieceType.Icon or PieceType.Dice,
        nameof(TextShadow) => type is PieceType.Button,
        nameof(Color) or nameof(Shade) => type is not (PieceType.Image or PieceType.Input or PieceType.Messages or PieceType.List),
        nameof(Opacity) or nameof(Border) or nameof(BorderColor) or nameof(BorderWidth) or nameof(Gradient) or nameof(GradientDirection) => type is PieceType.Box,
        nameof(Animate) => type is PieceType.Bar,
        nameof(Corner) => type is PieceType.Box or PieceType.Button or PieceType.Bar or PieceType.Choice or PieceType.Counter or PieceType.Grid,
        nameof(Style) => type is PieceType.Button,
        // O Texto usa o Color como cor da letra; as outras peças com letras têm a cor da letra separada.
        nameof(TextColor) => HasLetters(type) && type is not PieceType.Text,
        nameof(BarStyle) or nameof(BarText) => type is PieceType.Bar,
        nameof(Shortcut) => type is PieceType.Button,
        nameof(Enabled) => type is PieceType.Button or PieceType.Input or PieceType.Image or PieceType.Choice or PieceType.Counter or PieceType.Grid,
        nameof(Value) => type is PieceType.Bar or PieceType.Counter or PieceType.Dice,
        nameof(Max) => type is PieceType.Bar or PieceType.Counter,
        nameof(Icon) => type is PieceType.Icon or PieceType.Button,
        nameof(Frame) or nameof(Back) => type is PieceType.Icon,
        nameof(Sides) => type is PieceType.Dice,
        nameof(Min) or nameof(Step) => type is PieceType.Counter,
        nameof(Options) or nameof(Selected) => type is PieceType.Choice or PieceType.Wheel,
        nameof(Columns) => type is PieceType.Choice or PieceType.Grid,
        nameof(Rows) => type is PieceType.Grid,
        nameof(Image) => type is PieceType.Image,
        nameof(CardWidth) or nameof(CardHeight) or nameof(Gap) => type is PieceType.List,
        nameof(List) => type is not (PieceType.List or PieceType.Messages or PieceType.Choice or PieceType.Counter or PieceType.Wheel or PieceType.Grid or PieceType.Dice),
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
            case PieceType.Choice:
                (piece.Width, piece.Height, piece.Options, piece.Selected) = (480, 52, ["Fácil", "Normal", "Difícil"], "Normal");
                break;
            case PieceType.Counter:
                (piece.Width, piece.Height, piece.Value, piece.Min, piece.Max) = (200, 52, 1, 0, 10);
                break;
            case PieceType.Wheel:
                (piece.Width, piece.Height, piece.Options) = (300, 300, ["10 moedas", "Nada", "50 moedas", "Poção", "Nada", "100 moedas"]);
                break;
            case PieceType.Grid:
                (piece.Width, piece.Height, piece.Columns, piece.Rows) = (300, 300, 3, 3);
                break;
            case PieceType.Icon:
                (piece.Width, piece.Height, piece.Icon) = (120, 120, GameEngine.Icon.Star);
                break;
            case PieceType.Dice:
                (piece.Width, piece.Height, piece.Sides, piece.Color) = (120, 120, 20, GameEngine.Color.Red);
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
        PieceType.Choice => "Escolha",
        PieceType.Counter => "Contador",
        PieceType.Wheel => "Roleta",
        PieceType.Grid => "Grade",
        PieceType.Icon => "Ícone",
        PieceType.Dice => "Dado",
        _ => type.ToString(),
    };
}

/// <summary>A tela inteira: fundo opcional e as peças, na ordem de desenho (a última fica por cima).</summary>
internal sealed class ScreenLayout
{
    public const int CurrentFormat = 1;
    public const double Width = 960, Height = 540;

    public string? Background { get; set; }
    /// <summary>Cor do fundo da tela (null: a do tema). Fica atrás da imagem de fundo, se tiver.</summary>
    public Color? BackgroundColor { get; set; }
    /// <summary>Segunda cor do fundo: degradê de cima (BackgroundColor) para baixo (esta).</summary>
    public Color? BackgroundGradient { get; set; }
    public List<Piece> Pieces { get; set; } = [];

    public ScreenLayout Clone() => new()
    {
        Background = Background,
        BackgroundColor = BackgroundColor,
        BackgroundGradient = BackgroundGradient,
        Pieces = Pieces.Select(p => p.Clone()).ToList(),
    };

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
