using System.IO;
using System.Text.Json;
using System.Windows.Media;
using WpfColor = System.Windows.Media.Color;

namespace CSharpLab.GameEngine;

/// <summary>O tema do jogo inteiro (arquivo GameStyle.json na pasta do projeto).</summary>
internal enum ThemeName
{
    Classic,
    Fantasy,
    Book,
    Modern,
}

/// <summary>
/// Um tema: as cores, as fontes e os cantos que as peças usam quando a tela não escolhe outra coisa.
/// O que a peça escolheu (cor, fonte, tom…) sempre vale mais que o tema.
/// </summary>
internal sealed class Look
{
    public required ThemeName Name { get; init; }
    /// <summary>Nome em português, como aparece no editor.</summary>
    public required string Title { get; init; }
    public required string Description { get; init; }

    public required WpfColor Background { get; init; }
    /// <summary>Cor de baixo do fundo (um degradê de cima para baixo); null deixa o fundo liso.</summary>
    public WpfColor? BackgroundBottom { get; init; }
    public required WpfColor Panel { get; init; }
    public required WpfColor PanelBorder { get; init; }
    public required WpfColor Track { get; init; }
    public required WpfColor Text { get; init; }
    public required WpfColor Muted { get; init; }
    /// <summary>Botão sem cor escolhida.</summary>
    public required WpfColor ButtonDefault { get; init; }
    /// <summary>As 8 cores do jogo neste tema (num tema claro, mais escuras, para ler no papel).</summary>
    public required IReadOnlyDictionary<Color, WpfColor> Colors { get; init; }
    /// <summary>Fundo claro: botões de contorno escurecem a cor em vez de clarear.</summary>
    public bool IsLight { get; init; }

    public double ButtonRadius { get; init; } = 8;
    public double PanelRadius { get; init; } = 10;
    /// <summary>Caixas e Mensagens sem cor ganham uma borda fina.</summary>
    public bool PanelBorders { get; init; }

    /// <summary>Fonte dos títulos (Texto com letra 28 ou maior) quando a peça não escolhe outra.</summary>
    public Font TitleFont { get; init; } = Font.Normal;
    public Font BodyFont { get; init; } = Font.Normal;
    public Font ButtonFont { get; init; } = Font.Normal;

    private Brush? _background;

    /// <summary>O fundo pronto para desenhar (liso ou em degradê).</summary>
    public Brush BackgroundBrush => _background ??= CreateBackground();

    private Brush CreateBackground()
    {
        if (BackgroundBottom is not { } bottom) return Theme.Freeze(Background);
        var brush = new LinearGradientBrush(Background, bottom, 90);
        brush.Freeze();
        return brush;
    }

    // ------------------------------------------------------------------ os quatro temas

    private static readonly IReadOnlyDictionary<Color, WpfColor> DarkColors = new Dictionary<Color, WpfColor>
    {
        [Color.White] = WpfColor.FromRgb(0xDD, 0xE1, 0xE8),
        [Color.Gray] = WpfColor.FromRgb(0x8C, 0x93, 0xA0),
        [Color.Red] = WpfColor.FromRgb(0xF2, 0x6B, 0x6B),
        [Color.Green] = WpfColor.FromRgb(0x5C, 0xCB, 0x7A),
        [Color.Blue] = WpfColor.FromRgb(0x5E, 0xA8, 0xFF),
        [Color.Gold] = WpfColor.FromRgb(0xF2, 0xC1, 0x4E),
        [Color.Purple] = WpfColor.FromRgb(0xB4, 0x8C, 0xFF),
        [Color.Orange] = WpfColor.FromRgb(0xF2, 0x9A, 0x4E),
        [Color.Black] = WpfColor.FromRgb(0x12, 0x14, 0x18),
        [Color.Pink] = WpfColor.FromRgb(0xF2, 0x7B, 0xB5),
        [Color.Cyan] = WpfColor.FromRgb(0x4E, 0xD6, 0xE0),
        [Color.Brown] = WpfColor.FromRgb(0xB0, 0x7A, 0x4E),
    };

    /// <summary>Clássico: o visual de sempre, escuro e limpo.</summary>
    public static readonly Look Classic = new()
    {
        Name = ThemeName.Classic,
        Title = "Clássico",
        Description = "Escuro e limpo, o visual de sempre.",
        Background = WpfColor.FromRgb(0x17, 0x19, 0x1F),
        Panel = WpfColor.FromRgb(0x21, 0x24, 0x2C),
        PanelBorder = WpfColor.FromRgb(0x2E, 0x32, 0x3C),
        Track = WpfColor.FromRgb(0x2E, 0x32, 0x3C),
        Text = WpfColor.FromRgb(0xDD, 0xE1, 0xE8),
        Muted = WpfColor.FromRgb(0x8C, 0x93, 0xA0),
        ButtonDefault = WpfColor.FromRgb(0x3D, 0x7B, 0xE8),
        Colors = DarkColors,
    };

    /// <summary>Fantasia: noite púrpura, painéis com borda de bronze, títulos de conto de fadas.</summary>
    public static readonly Look Fantasy = new()
    {
        Name = ThemeName.Fantasy,
        Title = "Fantasia",
        Description = "Noite púrpura, bordas de bronze e títulos de conto de fadas.",
        Background = WpfColor.FromRgb(0x26, 0x1A, 0x2C),
        BackgroundBottom = WpfColor.FromRgb(0x10, 0x0B, 0x14),
        Panel = WpfColor.FromRgb(0x2C, 0x21, 0x31),
        PanelBorder = WpfColor.FromRgb(0x7A, 0x5C, 0x3A),
        Track = WpfColor.FromRgb(0x3D, 0x2E, 0x41),
        Text = WpfColor.FromRgb(0xF0, 0xE6, 0xD2),
        Muted = WpfColor.FromRgb(0xAE, 0x9E, 0x88),
        ButtonDefault = WpfColor.FromRgb(0x9A, 0x3A, 0x3A),
        Colors = new Dictionary<Color, WpfColor>(DarkColors)
        {
            [Color.White] = WpfColor.FromRgb(0xF0, 0xE6, 0xD2),
            [Color.Gray] = WpfColor.FromRgb(0xAE, 0x9E, 0x88),
            [Color.Gold] = WpfColor.FromRgb(0xE8, 0xB4, 0x4C),
            [Color.Brown] = WpfColor.FromRgb(0xA8, 0x74, 0x48),
        },
        ButtonRadius = 4,
        PanelRadius = 6,
        PanelBorders = true,
        TitleFont = Font.Fantasy,
        BodyFont = Font.Book,
        ButtonFont = Font.Book,
    };

    /// <summary>Livro: papel claro e tinta escura, como ler um livro antigo.</summary>
    public static readonly Look Book = new()
    {
        Name = ThemeName.Book,
        Title = "Livro",
        Description = "Papel claro e tinta escura, como um livro antigo.",
        Background = WpfColor.FromRgb(0xEF, 0xE4, 0xCC),
        BackgroundBottom = WpfColor.FromRgb(0xE0, 0xCF, 0xAB),
        Panel = WpfColor.FromRgb(0xF8, 0xF0, 0xDE),
        PanelBorder = WpfColor.FromRgb(0xC4, 0xAD, 0x82),
        Track = WpfColor.FromRgb(0xDA, 0xC9, 0xA6),
        Text = WpfColor.FromRgb(0x3A, 0x2E, 0x22),
        Muted = WpfColor.FromRgb(0x7C, 0x6A, 0x52),
        ButtonDefault = WpfColor.FromRgb(0x7A, 0x4E, 0x2D),
        Colors = new Dictionary<Color, WpfColor>
        {
            [Color.White] = WpfColor.FromRgb(0x3A, 0x2E, 0x22),
            [Color.Gray] = WpfColor.FromRgb(0x7C, 0x6A, 0x52),
            [Color.Red] = WpfColor.FromRgb(0xA8, 0x3A, 0x2C),
            [Color.Green] = WpfColor.FromRgb(0x3A, 0x7A, 0x38),
            [Color.Blue] = WpfColor.FromRgb(0x2E, 0x5C, 0x9A),
            [Color.Gold] = WpfColor.FromRgb(0x9E, 0x6C, 0x14),
            [Color.Purple] = WpfColor.FromRgb(0x6C, 0x46, 0x9A),
            [Color.Orange] = WpfColor.FromRgb(0xB0, 0x5C, 0x1A),
            [Color.Black] = WpfColor.FromRgb(0x1F, 0x18, 0x12),
            [Color.Pink] = WpfColor.FromRgb(0xA8, 0x45, 0x6E),
            [Color.Cyan] = WpfColor.FromRgb(0x2E, 0x7F, 0x86),
            [Color.Brown] = WpfColor.FromRgb(0x7A, 0x4E, 0x2D),
        },
        IsLight = true,
        ButtonRadius = 4,
        PanelRadius = 4,
        PanelBorders = true,
        TitleFont = Font.Book,
        BodyFont = Font.Book,
        ButtonFont = Font.Book,
    };

    /// <summary>Moderno: quase preto, cores vivas e cantos bem redondos.</summary>
    public static readonly Look Modern = new()
    {
        Name = ThemeName.Modern,
        Title = "Moderno",
        Description = "Quase preto, cores vivas e cantos bem redondos.",
        Background = WpfColor.FromRgb(0x0E, 0x10, 0x14),
        Panel = WpfColor.FromRgb(0x1A, 0x1E, 0x26),
        PanelBorder = WpfColor.FromRgb(0x2A, 0x30, 0x3B),
        Track = WpfColor.FromRgb(0x26, 0x2B, 0x35),
        Text = WpfColor.FromRgb(0xF3, 0xF5, 0xF9),
        Muted = WpfColor.FromRgb(0x8E, 0x97, 0xA6),
        ButtonDefault = WpfColor.FromRgb(0x4C, 0x6F, 0xFF),
        Colors = new Dictionary<Color, WpfColor>
        {
            [Color.White] = WpfColor.FromRgb(0xF3, 0xF5, 0xF9),
            [Color.Gray] = WpfColor.FromRgb(0x8E, 0x97, 0xA6),
            [Color.Red] = WpfColor.FromRgb(0xFF, 0x5C, 0x6C),
            [Color.Green] = WpfColor.FromRgb(0x3D, 0xDC, 0x97),
            [Color.Blue] = WpfColor.FromRgb(0x4C, 0x9A, 0xFF),
            [Color.Gold] = WpfColor.FromRgb(0xFF, 0xC9, 0x4D),
            [Color.Purple] = WpfColor.FromRgb(0xA7, 0x8B, 0xFA),
            [Color.Orange] = WpfColor.FromRgb(0xFF, 0x9F, 0x5A),
            [Color.Black] = WpfColor.FromRgb(0x0A, 0x0B, 0x0E),
            [Color.Pink] = WpfColor.FromRgb(0xFF, 0x6F, 0xB5),
            [Color.Cyan] = WpfColor.FromRgb(0x3F, 0xE0, 0xF0),
            [Color.Brown] = WpfColor.FromRgb(0xC0, 0x8A, 0x5A),
        },
        ButtonRadius = 14,
        PanelRadius = 16,
    };

    public static IReadOnlyList<Look> All { get; } = [Classic, Fantasy, Book, Modern];

    public static Look Of(ThemeName name) => All.First(l => l.Name == name);
}

/// <summary>
/// Lê e grava o GameStyle.json: <c>{ "format": 1, "theme": "Fantasy" }</c>. Sem o arquivo, o jogo usa o Clássico.
/// O jogo e o editor leem do mesmo jeito, então a tela mostra o tema que o jogo vai ter.
/// </summary>
internal static class GameStyle
{
    public const string FileName = "GameStyle.json";

    /// <summary>O tema do texto do arquivo, ou o problema em português (com a linha, quando der).</summary>
    public static (ThemeName? Theme, string? Error) Parse(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException ex)
        {
            var line = ex.LineNumber is { } l ? $" perto da linha {l + 1}" : "";
            return (null, $"O {FileName} não está escrito certo{line}: confira vírgulas, aspas e chaves {{ }}.");
        }
        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return (null, $"O {FileName} precisa ser assim: {{ \"format\": 1, \"theme\": \"Fantasy\" }}.");
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (!property.Name.Equals("theme", StringComparison.OrdinalIgnoreCase)) continue;
                var names = string.Join(", ", Enum.GetNames<ThemeName>());
                if (property.Value.ValueKind == JsonValueKind.String &&
                    Enum.TryParse<ThemeName>(property.Value.GetString(), ignoreCase: true, out var theme) &&
                    Enum.IsDefined(theme))
                    return (theme, null);
                return (null, $"O tema \"{property.Value}\" do {FileName} não existe. Use um destes: {names}.");
            }
            return (ThemeName.Classic, null);
        }
    }

    public static string Serialize(ThemeName theme) =>
        "{" + Environment.NewLine + "  \"format\": 1," + Environment.NewLine + $"  \"theme\": \"{theme}\"" + Environment.NewLine + "}" + Environment.NewLine;

    /// <summary>
    /// O tema da primeira pasta que tiver um GameStyle.json (Clássico se nenhuma tiver).
    /// <paramref name="error"/> diz o que está errado no arquivo; aí o tema volta para o Clássico.
    /// </summary>
    public static Look Load(IEnumerable<string> folders, out string? error)
    {
        error = null;
        foreach (var folder in folders)
        {
            var file = Path.Combine(folder, FileName);
            if (!File.Exists(file)) continue;
            try
            {
                // O arquivo é pequenininho: lido sempre de novo (guardar pela data falha quando ele muda duas
                // vezes no mesmo instante).
                var result = Parse(File.ReadAllText(file));
                error = result.Error;
                return Look.Of(result.Theme ?? ThemeName.Classic);
            }
            catch (IOException ex)
            {
                error = $"Não deu para ler o {FileName}: {ex.Message}";
                return Look.Classic;
            }
        }
        return Look.Classic;
    }
}
