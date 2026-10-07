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
        ["type", "name", "x", "y", "width", "height", "text", "size", "bold", "align", "color", "value", "max", "image", "visible"];

    public static string ValidColors => string.Join(", ", Enum.GetNames<Color>());

    public static string ValidTypes => string.Join(", ", Enum.GetNames<PieceType>());

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
                "visible" => Boolean(value, label, key, v => piece.Visible = v),
                "color" => ReadColor(value, label, v => piece.Color = v),
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

    private static string? Integer(JsonElement value, string label, string key, Action<int> apply, bool positive = false)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number))
            return $"{label}: \"{key}\" precisa ser um número inteiro, sem aspas.";
        if (positive && number <= 0) return $"{label}: \"{key}\" precisa ser maior que 0.";
        apply(number);
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
            $"\"x\": {Round(p.X)}",
            $"\"y\": {Round(p.Y)}",
            $"\"width\": {Round(p.Width)}",
            $"\"height\": {Round(p.Height)}",
        };
        if (p.Text != null && Piece.Supports(p.Type, nameof(Piece.Text))) parts.Add($"\"text\": {Quote(p.Text)}");
        if (p.Size != null && Piece.Supports(p.Type, nameof(Piece.Size))) parts.Add($"\"size\": {Round(p.Size.Value)}");
        if (p.Bold == true && Piece.Supports(p.Type, nameof(Piece.Bold))) parts.Add("\"bold\": true");
        if (p.Align is { } align and not TextAlign.Left && Piece.Supports(p.Type, nameof(Piece.Align))) parts.Add($"\"align\": \"{align}\"");
        if (p.Color is { } color && Piece.Supports(p.Type, nameof(Piece.Color))) parts.Add($"\"color\": \"{color}\"");
        if (p.Value != null && p.Type == PieceType.Bar) parts.Add($"\"value\": {p.Value.Value.ToString(CultureInfo.InvariantCulture)}");
        if (p.Max != null && p.Type == PieceType.Bar) parts.Add($"\"max\": {p.Max.Value.ToString(CultureInfo.InvariantCulture)}");
        if (p.Image != null && p.Type == PieceType.Image) parts.Add($"\"image\": {Quote(p.Image)}");
        if (!p.Visible) parts.Add("\"visible\": false");
        return "{ " + string.Join(", ", parts) + " }";
    }

    private static string Round(double value) => Math.Round(value).ToString(CultureInfo.InvariantCulture);

    private static string Quote(string text) => JsonSerializer.Serialize(text, StringOptions);
}
