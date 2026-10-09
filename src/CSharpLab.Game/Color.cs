using System.Globalization;

namespace CSharpLab.GameEngine;

/// <summary>
/// Color = cor. As cores com nome (Color.Red, Color.Gold…) combinam com o tema do jogo; para qualquer outra cor,
/// use Color.Hex("#8A2BE2") ou Color.Rgb(138, 43, 226). Exemplo: <c>game.Write("Cuidado!", Color.Red);</c>
/// </summary>
public readonly struct Color : IEquatable<Color>
{
    private readonly string? _name;
    private readonly int _rgb;

    private Color(string name)
    {
        _name = name;
        _rgb = 0;
    }

    private Color(int red, int green, int blue)
    {
        _name = null;
        _rgb = red << 16 | green << 8 | blue;
    }

    /// <summary>Branco (a cor normal dos textos).</summary>
    public static Color White => new("White");
    /// <summary>Cinza: bom para detalhes e dicas.</summary>
    public static Color Gray => new("Gray");
    /// <summary>Preto: sombras, molduras escuras, letras num fundo claro.</summary>
    public static Color Black => new("Black");
    /// <summary>Vermelho: perigo, dano, inimigo.</summary>
    public static Color Red => new("Red");
    /// <summary>Rosa: fadas, doces, coisas fofas.</summary>
    public static Color Pink => new("Pink");
    /// <summary>Laranja: fogo, alerta.</summary>
    public static Color Orange => new("Orange");
    /// <summary>Dourado: ouro, prêmios, coisas raras.</summary>
    public static Color Gold => new("Gold");
    /// <summary>Verde: vida, cura, sucesso.</summary>
    public static Color Green => new("Green");
    /// <summary>Ciano (azul-piscina): gelo, energia, tecnologia.</summary>
    public static Color Cyan => new("Cyan");
    /// <summary>Azul: mana, água, magia.</summary>
    public static Color Blue => new("Blue");
    /// <summary>Roxo: mistério, veneno.</summary>
    public static Color Purple => new("Purple");
    /// <summary>Marrom: madeira, terra, couro.</summary>
    public static Color Brown => new("Brown");

    /// <summary>
    /// Hex = qualquer cor pelo código dela, como nos sites de cores: "#RRGGBB" (vermelho, verde e azul de 00 a FF).
    /// </summary>
    /// <example><code>game.Find("Mana").Color = Color.Hex("#3FA9F5");</code></example>
    public static Color Hex(string hex)
    {
        if (TryParseHex(hex, out var color)) return color;
        throw new GameException($"Color.Hex(\"{hex}\") não é uma cor. Use # e seis letras ou números de 0 a 9 e A a F, como Color.Hex(\"#8A2BE2\").");
    }

    /// <summary>Rgb = qualquer cor misturando vermelho, verde e azul, cada um de 0 a 255.</summary>
    /// <example><code>game.Find("Potion").Color = Color.Rgb(255, 102, 204);</code></example>
    public static Color Rgb(int red, int green, int blue)
    {
        if (red is < 0 or > 255 || green is < 0 or > 255 || blue is < 0 or > 255)
            throw new GameException($"Color.Rgb({red}, {green}, {blue}): cada número vai de 0 a 255.");
        return new Color(red, green, blue);
    }

    /// <summary>As cores com nome, na ordem da paleta do Estúdio.</summary>
    internal static IReadOnlyList<Color> Named { get; } = [White, Gray, Black, Red, Pink, Orange, Gold, Green, Cyan, Blue, Purple, Brown];

    internal static string NamedList => string.Join(", ", Named.Select(c => c.Name));

    /// <summary>O nome (cores do tema) ou null (cor própria, do Hex ou do Rgb).</summary>
    internal string? Name => _name;

    internal bool IsNamed => _name != null;

    internal byte R => (byte)(_rgb >> 16 & 0xFF);
    internal byte G => (byte)(_rgb >> 8 & 0xFF);
    internal byte B => (byte)(_rgb & 0xFF);

    /// <summary>Lê "Red" (sem diferença de maiúsculas) ou "#RRGGBB".</summary>
    internal static bool TryParse(string? text, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();
        if (text.StartsWith('#')) return TryParseHex(text, out color);
        foreach (var named in Named)
        {
            if (string.Equals(named._name, text, StringComparison.OrdinalIgnoreCase))
            {
                color = named;
                return true;
            }
        }
        return false;
    }

    private static bool TryParseHex(string? hex, out Color color)
    {
        color = default;
        var digits = hex?.Trim().TrimStart('#') ?? "";
        if (digits.Length == 3) digits = string.Concat(digits.Select(d => new string(d, 2)));
        if (digits.Length != 6 || !int.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)) return false;
        color = new Color(rgb >> 16 & 0xFF, rgb >> 8 & 0xFF, rgb & 0xFF);
        return true;
    }

    /// <summary>O nome da cor ("Red") ou o código dela ("#8A2BE2").</summary>
    public override string ToString() => _name ?? "#" + _rgb.ToString("X6", CultureInfo.InvariantCulture);

    /// <summary>Equals = é igual. A mesma cor (o mesmo nome, ou o mesmo código).</summary>
    public bool Equals(Color other) => _name != null || other._name != null
        ? string.Equals(_name, other._name, StringComparison.Ordinal)
        : _rgb == other._rgb;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Color other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => _name != null ? StringComparer.Ordinal.GetHashCode(_name) : _rgb;

    /// <summary>As duas cores são a mesma.</summary>
    public static bool operator ==(Color left, Color right) => left.Equals(right);

    /// <summary>As duas cores são diferentes.</summary>
    public static bool operator !=(Color left, Color right) => !left.Equals(right);
}
