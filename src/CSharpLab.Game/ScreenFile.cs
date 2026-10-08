using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace CSharpLab.GameEngine;

/// <summary>Resultado da leitura de uma tela: a tela, ou o erro em português (com a linha, quando dá para saber).</summary>
internal sealed record ScreenParseResult(ScreenLayout? Layout, string? Error, int? ErrorLine, IReadOnlyList<string> Warnings)
{
    public bool Success => Layout != null;
}

/// <summary>
/// Lê e grava Screens/&lt;Cena&gt;.json. O formato é pensado para ser lido e escrito por pessoas e IAs:
/// chaves em inglês, textos em português, uma peça por linha e erros que dizem o que corrigir.
/// </summary>
internal static class ScreenFile
{
    private static readonly JsonSerializerOptions StringOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly string[] PieceKeys =
        ["type", "name", "list", "x", "y", "width", "height", "text", "size", "bold", "italic", "shadow", "scroll", "font", "align", "color", "shade",
         "opacity", "border", "corner", "style", "value", "max", "image", "cardwidth", "cardheight", "gap", "enabled", "visible"];

    public static string ValidColors => string.Join(", ", Enum.GetNames<Color>());

    public static string ValidTypes => string.Join(", ", Enum.GetNames<PieceType>());

    private static string Names<T>() where T : struct, Enum
    {
        var names = Enum.GetNames<T>();
        return string.Join(", ", names[..^1]) + " ou " + names[^1];
    }

    // ------------------------------------------------------------------ leitura

    public static ScreenParseResult Parse(string json)
    {
        var warnings = new List<string>();
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException ex)
        {
            int? line = ex.LineNumber is { } l ? (int)l + 1 : null;
            return Fail($"O arquivo não está escrito certo{(line != null ? $" perto da linha {line}" : "")}: confira vírgulas, aspas e chaves {{ }}.", line);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Fail("O arquivo da tela precisa começar com { e ter a lista \"pieces\".", 1);

            var layout = new ScreenLayout();
            foreach (var property in root.EnumerateObject())
            {
                switch (property.Name.ToLowerInvariant())
                {
                    case "format":
                        if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out var format))
                            return Fail("\"format\" precisa ser um número (1).", LineOf(json, "\"format\""));
                        if (format > ScreenLayout.CurrentFormat)
                            return Fail($"Esta tela foi feita numa versão mais nova do CSharp Lab (formato {format}). Atualize o CSharp Lab.", LineOf(json, "\"format\""));
                        break;
                    case "background":
                        if (property.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                            return Fail("\"background\" precisa ser o nome de uma imagem da pasta Assets, entre aspas.", LineOf(json, "\"background\""));
                        layout.Background = property.Value.GetString() is { Length: > 0 } bg ? bg : null;
                        break;
                    case "pieces":
                        if (property.Value.ValueKind != JsonValueKind.Array)
                            return Fail("\"pieces\" precisa ser uma lista: \"pieces\": [ ... ]", LineOf(json, "\"pieces\""));
                        int index = 0;
                        foreach (var element in property.Value.EnumerateArray())
                        {
                            index++;
                            var (piece, error) = ReadPiece(element, index, warnings);
                            if (piece == null)
                            {
                                var name = element.ValueKind == JsonValueKind.Object && element.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                                return Fail(error!, name != null ? LineOf(json, $"\"{name}\"") : null);
                            }
                            if (layout.Find(piece.Name) != null)
                                return Fail($"Duas peças se chamam \"{piece.Name}\". Cada peça precisa de um nome diferente.", LineOfLast(json, $"\"{piece.Name}\""));
                            layout.Pieces.Add(piece);
                        }
                        if (CheckLists(layout) is { } listError)
                            return Fail(listError.Message, LineOf(json, $"\"{listError.Piece}\""));
                        break;
                    default:
                        warnings.Add($"\"{property.Name}\" não é usado pela tela e foi ignorado.");
                        break;
                }
            }
            return new ScreenParseResult(layout, null, null, warnings);
        }

        ScreenParseResult Fail(string message, int? line) => new(null, message, line, warnings);
    }

    private static (Piece? Piece, string? Error) ReadPiece(JsonElement element, int index, List<string> warnings)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return (null, $"A peça {index} precisa estar entre {{ }}.");

        var values = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in element.EnumerateObject()) values[p.Name] = p.Value;

        string? name = values.TryGetValue("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String ? nameElement.GetString()?.Trim() : null;
        var label = string.IsNullOrEmpty(name) ? $"A peça {index}" : $"A peça \"{name}\"";

        if (!values.TryGetValue("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String ||
            !Enum.TryParse<PieceType>(typeElement.GetString(), ignoreCase: true, out var type) || !Enum.IsDefined(type))
            return (null, $"{label} precisa de um \"type\" válido: {ValidTypes}.");
        if (string.IsNullOrEmpty(name))
            return (null, $"{label} ({Piece.Describe(type)}) precisa de um \"name\", por exemplo \"{type}{index}\". É o nome usado no game.Find.");
        if (NameProblem(name) is { } nameProblem)
            return (null, $"{label}: {nameProblem}");

        var piece = Piece.CreateDefault(type, name, 0, 0);
        piece.Text = null;
        piece.Value = null;
        piece.Max = null;
        piece.Color = null;
        foreach (var (key, value) in values)
        {
            var lower = key.ToLowerInvariant();
            if (Array.IndexOf(PieceKeys, lower) < 0)
            {
                warnings.Add($"{label}: \"{key}\" não existe e foi ignorado.");
                continue;
            }
            string? error = lower switch
            {
                "type" or "name" => null,
                "x" => Number(value, label, key, v => piece.X = v),
                "y" => Number(value, label, key, v => piece.Y = v),
                "width" => Number(value, label, key, v => piece.Width = v, positive: true),
                "height" => Number(value, label, key, v => piece.Height = v, positive: true),
                "size" => Number(value, label, key, v => piece.Size = Math.Clamp(v, 6, 200), positive: true),
                "value" => Integer(value, label, key, v => piece.Value = v),
                "max" => Integer(value, label, key, v => piece.Max = v, positive: true),
                "text" => Text(value, label, key, v => piece.Text = v),
                "image" => Text(value, label, key, v => piece.Image = v),
                "bold" => Boolean(value, label, key, v => piece.Bold = v),
                "italic" => Boolean(value, label, key, v => piece.Italic = v),
                "shadow" => Boolean(value, label, key, v => piece.Shadow = v),
                "scroll" => Boolean(value, label, key, v => piece.Scroll = v),
                "border" => Boolean(value, label, key, v => piece.Border = v),
                "visible" => Boolean(value, label, key, v => piece.Visible = v),
                "enabled" => Boolean(value, label, key, v => piece.Enabled = v),
                "opacity" => Integer(value, label, key, v => piece.Opacity = v, range: (0, 100)),
                "cardwidth" => Number(value, label, key, v => piece.CardWidth = v, positive: true),
                "cardheight" => Number(value, label, key, v => piece.CardHeight = v, positive: true),
                "gap" => Number(value, label, key, v => piece.Gap = Math.Max(0, v)),
                "list" => Text(value, label, key, v => piece.List = string.IsNullOrWhiteSpace(v) ? null : v.Trim()),
                "color" => ReadColor(value, label, v => piece.Color = v),
                "font" => ReadEnum<Font>(value, label, key, v => piece.Font = v),
                "shade" => ReadEnum<Shade>(value, label, key, v => piece.Shade = v),
                "corner" => ReadEnum<Corner>(value, label, key, v => piece.Corner = v),
                "style" => ReadEnum<ButtonStyle>(value, label, key, v => piece.Style = v),
                "align" => value.ValueKind == JsonValueKind.String && Enum.TryParse<TextAlign>(value.GetString(), true, out var align) && Enum.IsDefined(align)
                    ? Set(() => piece.Align = align)
                    : $"{label}: \"align\" precisa ser Left, Center ou Right.",
                _ => null,
            };
            if (error != null) return (null, error);
        }
        return (piece, null);
    }

    /// <summary>Por que o nome não serve (ou null se serve). Mesmas regras de um nome de variável simples.</summary>
    public static string? NameProblem(string name)
    {
        if (name.Length == 0) return "o nome não pode ficar vazio.";
        if (!(char.IsAsciiLetter(name[0]) || name[0] == '_') || !name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            return $"o nome \"{name}\" só pode ter letras sem acento, números e _, começando por uma letra (ex.: Attack, PlayerHealth).";
        if (name.Length > 40) return "o nome é comprido demais (até 40 letras).";
        return null;
    }

    private static string? Set(Action apply)
    {
        apply();
        return null;
    }

    private static string? Number(JsonElement value, string label, string key, Action<double> apply, bool positive = false)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) || double.IsNaN(number) || double.IsInfinity(number))
            return $"{label}: \"{key}\" precisa ser um número, sem aspas.";
        if (positive && number <= 0) return $"{label}: \"{key}\" precisa ser maior que 0.";
        apply(number);
        return null;
    }

    private static string? Integer(JsonElement value, string label, string key, Action<int> apply, bool positive = false, (int Min, int Max)? range = null)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number))
            return $"{label}: \"{key}\" precisa ser um número inteiro, sem aspas.";
        if (positive && number <= 0) return $"{label}: \"{key}\" precisa ser maior que 0.";
        if (range is { } r && (number < r.Min || number > r.Max)) return $"{label}: \"{key}\" vai de {r.Min} a {r.Max}.";
        apply(number);
        return null;
    }

    private static string? ReadEnum<T>(JsonElement value, string label, string key, Action<T?> apply) where T : struct, Enum
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            apply(null);
            return null;
        }
        if (value.ValueKind == JsonValueKind.String && Enum.TryParse<T>(value.GetString(), true, out var parsed) && Enum.IsDefined(parsed))
        {
            apply(parsed);
            return null;
        }
        return $"{label}: \"{key}\" precisa ser {Names<T>()}.";
    }

    /// <summary>Cada peça com "list" precisa apontar para uma Lista da tela (e Lista não fica dentro de Lista).</summary>
    private static (string Message, string Piece)? CheckLists(ScreenLayout layout)
    {
        foreach (var piece in layout.Pieces)
        {
            if (piece.List == null) continue;
            if (!Piece.Supports(piece.Type, nameof(Piece.List)))
                return ($"A peça \"{piece.Name}\" ({Piece.Describe(piece.Type)}) não pode ficar dentro de uma Lista. Tire o \"list\" dela.", piece.Name);
            var list = layout.Find(piece.List);
            if (list == null)
                return ($"A peça \"{piece.Name}\" diz \"list\": \"{piece.List}\", mas a tela não tem uma Lista com esse nome.", piece.Name);
            if (list.Type != PieceType.List)
                return ($"A peça \"{piece.Name}\" diz \"list\": \"{piece.List}\", mas \"{list.Name}\" é {Piece.Describe(list.Type)}, não Lista.", piece.Name);
            piece.List = list.Name;
        }
        return null;
    }

    private static string? Text(JsonElement value, string label, string key, Action<string?> apply)
    {
        if (value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            return $"{label}: \"{key}\" precisa ser um texto entre aspas.";
        apply(value.GetString());
        return null;
    }

    private static string? Boolean(JsonElement value, string label, string key, Action<bool> apply)
    {
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return $"{label}: \"{key}\" precisa ser true ou false, sem aspas.";
        apply(value.GetBoolean());
        return null;
    }

    private static string? ReadColor(JsonElement value, string label, Action<Color?> apply)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            apply(null);
            return null;
        }
        if (value.ValueKind == JsonValueKind.String && Enum.TryParse<Color>(value.GetString(), true, out var color) && Enum.IsDefined(color))
        {
            apply(color);
            return null;
        }
        return $"{label}: \"color\" precisa ser uma destas cores: {ValidColors}.";
    }

    private static int? LineOf(string text, string needle)
    {
        int at = text.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        return at < 0 ? null : 1 + text.AsSpan(0, at).Count('\n');
    }

    private static int? LineOfLast(string text, string needle)
    {
        int at = text.LastIndexOf(needle, StringComparison.OrdinalIgnoreCase);
        return at < 0 ? null : 1 + text.AsSpan(0, at).Count('\n');
    }

    // ------------------------------------------------------------------ gravação

    /// <summary>Texto do arquivo: sempre o mesmo formato, uma peça por linha, só com o que importa.</summary>
    public static string Serialize(ScreenLayout layout)
    {
        var nl = Environment.NewLine;
        var sb = new StringBuilder();
        sb.Append('{').Append(nl);
        sb.Append("  \"format\": ").Append(ScreenLayout.CurrentFormat).Append(',').Append(nl);
        if (layout.Background != null)
            sb.Append("  \"background\": ").Append(Quote(layout.Background)).Append(',').Append(nl);
        if (layout.Pieces.Count == 0)
        {
            sb.Append("  \"pieces\": []").Append(nl);
        }
        else
        {
            sb.Append("  \"pieces\": [").Append(nl);
            for (int i = 0; i < layout.Pieces.Count; i++)
            {
                sb.Append("    ").Append(SerializePiece(layout.Pieces[i]));
                if (i < layout.Pieces.Count - 1) sb.Append(',');
                sb.Append(nl);
            }
            sb.Append("  ]").Append(nl);
        }
        sb.Append('}').Append(nl);
        return sb.ToString();
    }

    private static string SerializePiece(Piece p)
    {
        var parts = new List<string>
        {
            $"\"type\": \"{p.Type}\"",
            $"\"name\": {Quote(p.Name)}",
        };
        if (p.List != null && Piece.Supports(p.Type, nameof(Piece.List))) parts.Add($"\"list\": {Quote(p.List)}");
        parts.AddRange(
        [
            $"\"x\": {Round(p.X)}",
            $"\"y\": {Round(p.Y)}",
            $"\"width\": {Round(p.Width)}",
            $"\"height\": {Round(p.Height)}",
        ]);
        if (p.Text != null && Piece.Supports(p.Type, nameof(Piece.Text))) parts.Add($"\"text\": {Quote(p.Text)}");
        if (p.Size != null && Piece.Supports(p.Type, nameof(Piece.Size))) parts.Add($"\"size\": {Round(p.Size.Value)}");
        if (p.Bold == true && Piece.Supports(p.Type, nameof(Piece.Bold))) parts.Add("\"bold\": true");
        if (p.Italic == true && Piece.Supports(p.Type, nameof(Piece.Italic))) parts.Add("\"italic\": true");
        if (p.Shadow == true && Piece.Supports(p.Type, nameof(Piece.Shadow))) parts.Add("\"shadow\": true");
        if (p.Scroll == true && Piece.Supports(p.Type, nameof(Piece.Scroll))) parts.Add("\"scroll\": true");
        if (p.Font is { } font and not Font.Normal && Piece.Supports(p.Type, nameof(Piece.Font))) parts.Add($"\"font\": \"{font}\"");
        if (p.Align is { } align and not TextAlign.Left && Piece.Supports(p.Type, nameof(Piece.Align))) parts.Add($"\"align\": \"{align}\"");
        if (p.Color is { } color && Piece.Supports(p.Type, nameof(Piece.Color))) parts.Add($"\"color\": \"{color}\"");
        if (p.Shade is { } shade and not Shade.Normal && Piece.Supports(p.Type, nameof(Piece.Shade))) parts.Add($"\"shade\": \"{shade}\"");
        if (p.Opacity is { } opacity && Piece.Supports(p.Type, nameof(Piece.Opacity))) parts.Add($"\"opacity\": {opacity.ToString(CultureInfo.InvariantCulture)}");
        if (p.Border is { } border && Piece.Supports(p.Type, nameof(Piece.Border))) parts.Add($"\"border\": {(border ? "true" : "false")}");
        if (p.Corner is { } corner and not Corner.Round && Piece.Supports(p.Type, nameof(Piece.Corner))) parts.Add($"\"corner\": \"{corner}\"");
        if (p.Style is { } style and not ButtonStyle.Filled && Piece.Supports(p.Type, nameof(Piece.Style))) parts.Add($"\"style\": \"{style}\"");
        if (p.Value != null && p.Type == PieceType.Bar) parts.Add($"\"value\": {p.Value.Value.ToString(CultureInfo.InvariantCulture)}");
        if (p.Max != null && p.Type == PieceType.Bar) parts.Add($"\"max\": {p.Max.Value.ToString(CultureInfo.InvariantCulture)}");
        if (p.Image != null && p.Type == PieceType.Image) parts.Add($"\"image\": {Quote(p.Image)}");
        if (p.Type == PieceType.List)
        {
            parts.Add($"\"cardWidth\": {Round(p.CardW)}");
            parts.Add($"\"cardHeight\": {Round(p.CardH)}");
            parts.Add($"\"gap\": {Round(p.CardGap)}");
        }
        if (!p.Enabled && Piece.Supports(p.Type, nameof(Piece.Enabled))) parts.Add("\"enabled\": false");
        if (!p.Visible) parts.Add("\"visible\": false");
        return "{ " + string.Join(", ", parts) + " }";
    }

    private static string Round(double value) => Math.Round(value).ToString(CultureInfo.InvariantCulture);

    private static string Quote(string text) => JsonSerializer.Serialize(text, StringOptions);
}
