using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using CSharpLab.Core.Files;
using CSharpLab.Core.Settings;
using CSharpLab.GameEngine;

namespace CSharpLab.Screens;

/// <summary>Um estilo guardado: o nome que a pessoa deu e a peça de onde ele veio (as cores, a letra, os cantos…).</summary>
internal sealed record SavedStyle(string Title, Piece Piece)
{
    public PieceType Type => Piece.Type;
}

/// <summary>
/// Meus estilos: aparências de peças guardadas com um nome, para usar de novo em qualquer tela e em qualquer jogo.
/// Ficam num arquivo só da pessoa (%LocalAppData%\CSharpLab\meus-estilos.json). Usar um estilo copia a aparência
/// para a peça (como o Colar estilo): a tela continua sendo um arquivo que se explica sozinho, sem depender dele.
/// </summary>
internal static class MyStyles
{
    private static readonly JsonSerializerOptions TextOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string FilePath => Path.Combine(AppPaths.Root, "meus-estilos.json");

    /// <summary>Os estilos guardados, na ordem em que foram criados. Lista vazia se o arquivo não existe ou está estragado.</summary>
    public static IReadOnlyList<SavedStyle> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return [];
            using var doc = JsonDocument.Parse(File.ReadAllText(FilePath), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (!doc.RootElement.TryGetProperty("styles", out var styles) || styles.ValueKind != JsonValueKind.Array) return [];
            var list = new List<SavedStyle>();
            foreach (var style in styles.EnumerateArray())
            {
                if (style.ValueKind != JsonValueKind.Object ||
                    !style.TryGetProperty("title", out var title) || title.ValueKind != JsonValueKind.String ||
                    !style.TryGetProperty("piece", out var piece) || piece.ValueKind != JsonValueKind.Object) continue;
                var parsed = ScreenFile.Parse("{ \"pieces\": [" + piece.GetRawText() + "] }");
                if (parsed.Layout is { Pieces: [var one] } && title.GetString() is { Length: > 0 } name) list.Add(new SavedStyle(name, one));
            }
            return list;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            AppPaths.Log(ex, "Lendo Meus estilos");
            return [];
        }
    }

    /// <summary>Guarda (ou troca, se já existe um com esse nome) o estilo da peça.</summary>
    public static void Save(string title, Piece piece)
    {
        var list = Load().ToList();
        var style = new SavedStyle(title.Trim(), Clean(piece));
        int index = list.FindIndex(s => string.Equals(s.Title, style.Title, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) list[index] = style; else list.Add(style);
        Write(list);
    }

    public static void Remove(string title) =>
        Write(Load().Where(s => !string.Equals(s.Title, title, StringComparison.OrdinalIgnoreCase)).ToList());

    public static void Rename(string title, string newTitle) =>
        Write(Load().Select(s => string.Equals(s.Title, title, StringComparison.OrdinalIgnoreCase) ? s with { Title = newTitle.Trim() } : s).ToList());

    public static bool Exists(string title) => Load().Any(s => string.Equals(s.Title, title.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Um nome livre para um estilo novo deste tipo: "Botão 1", "Botão 2"…</summary>
    public static string SuggestTitle(PieceType type)
    {
        var used = Load().Select(s => s.Title).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; ; i++)
        {
            var title = $"{Piece.Describe(type)} {i}";
            if (!used.Contains(title)) return title;
        }
    }

    /// <summary>O que um nome de estilo não pode ser (null: pode).</summary>
    public static string? TitleProblem(string title)
    {
        title = title.Trim();
        if (title.Length == 0) return "Dê um nome para o estilo.";
        if (title.Length > 40) return "Um nome mais curto (até 40 letras).";
        return null;
    }

    /// <summary>Só a aparência importa: o lugar, o nome e o que é do jogo (lista, atalho, valores) ficam de fora.</summary>
    private static Piece Clean(Piece piece)
    {
        var copy = piece.Clone();
        copy.Name = "Style";
        (copy.X, copy.Y) = (0, 0);
        copy.List = null;
        copy.Visible = true;
        copy.Locked = false;
        copy.Shortcut = null;
        copy.Cells = null;
        return copy;
    }

    private static void Write(IReadOnlyList<SavedStyle> styles)
    {
        var nl = Environment.NewLine;
        var sb = new StringBuilder();
        sb.Append('{').Append(nl).Append("  \"format\": 1,").Append(nl).Append("  \"styles\": [").Append(nl);
        for (int i = 0; i < styles.Count; i++)
        {
            sb.Append("    { \"title\": ").Append(JsonSerializer.Serialize(styles[i].Title, TextOptions))
              .Append(", \"piece\": ").Append(ScreenFile.SerializeOne(styles[i].Piece)).Append(" }");
            if (i < styles.Count - 1) sb.Append(',');
            sb.Append(nl);
        }
        sb.Append("  ]").Append(nl).Append('}').Append(nl);
        TextFileIO.Save(FilePath, sb.ToString(), TextFileIO.Utf8NoBom);
    }
}
