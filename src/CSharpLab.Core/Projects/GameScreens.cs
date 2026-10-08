using System.Collections.Concurrent;
using System.Text.Json;

namespace CSharpLab.Core.Projects;

/// <summary>Uma peça de uma tela desenhada, como o editor de código precisa dela (nome, tipo e a Lista de que faz parte).</summary>
public sealed record ScreenPiece(string Name, string Type, string? List = null);

/// <summary>
/// Lê os nomes das peças das telas desenhadas (Screens/&lt;Cena&gt;.json), para o editor sugerir e conferir
/// os nomes do game.Find. Telas abertas e ainda não salvas valem pelo texto da aba, não pelo disco.
/// </summary>
public static class GameScreens
{
    public const string Folder = "Screens";

    /// <summary>Texto das telas abertas no editor (caminho completo → texto), atualizado pela interface.</summary>
    public static ConcurrentDictionary<string, string> OpenTexts { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static string PathOf(string projectDirectory, string scene) => Path.Combine(projectDirectory, Folder, scene + ".json");

    /// <summary>As cenas que têm tela desenhada.</summary>
    public static IReadOnlyList<string> Scenes(string projectDirectory)
    {
        var dir = Path.Combine(projectDirectory, Folder);
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (Directory.Exists(dir))
                foreach (var file in Directory.EnumerateFiles(dir, "*.json")) names.Add(Path.GetFileNameWithoutExtension(file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        foreach (var open in OpenTexts.Keys)
        {
            if (string.Equals(Path.GetDirectoryName(open), dir, StringComparison.OrdinalIgnoreCase))
                names.Add(Path.GetFileNameWithoutExtension(open));
        }
        return names.ToList();
    }

    /// <summary>As peças da tela da cena, ou null se a cena não tem tela (ou o arquivo está com erro).</summary>
    public static IReadOnlyList<ScreenPiece>? Pieces(string projectDirectory, string scene)
    {
        var path = PathOf(projectDirectory, scene);
        string? text = OpenTexts.TryGetValue(path, out var open) ? open : null;
        if (text == null)
        {
            try
            {
                if (!File.Exists(path)) return null;
                text = File.ReadAllText(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
        return Parse(text);
    }

    internal static IReadOnlyList<ScreenPiece>? Parse(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            JsonElement pieces = default;
            foreach (var p in doc.RootElement.EnumerateObject())
                if (string.Equals(p.Name, "pieces", StringComparison.OrdinalIgnoreCase)) pieces = p.Value;
            if (pieces.ValueKind != JsonValueKind.Array) return null;
            var list = new List<ScreenPiece>();
            foreach (var piece in pieces.EnumerateArray())
            {
                if (piece.ValueKind != JsonValueKind.Object) continue;
                string? name = null, type = null, owner = null;
                foreach (var p in piece.EnumerateObject())
                {
                    if (p.Value.ValueKind != JsonValueKind.String) continue;
                    if (string.Equals(p.Name, "name", StringComparison.OrdinalIgnoreCase)) name = p.Value.GetString();
                    else if (string.Equals(p.Name, "type", StringComparison.OrdinalIgnoreCase)) type = p.Value.GetString();
                    else if (string.Equals(p.Name, "list", StringComparison.OrdinalIgnoreCase)) owner = p.Value.GetString();
                }
                if (!string.IsNullOrWhiteSpace(name))
                    list.Add(new ScreenPiece(name.Trim(), type ?? "", string.IsNullOrWhiteSpace(owner) ? null : owner.Trim()));
            }
            return list;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Nome da peça em português, como na aba Tela.</summary>
    public static string Describe(string type) => type.ToLowerInvariant() switch
    {
        "text" => "Texto",
        "button" => "Botão",
        "bar" => "Barra",
        "image" => "Imagem",
        "box" => "Caixa",
        "input" => "Campo de escrita",
        "messages" => "Mensagens",
        "list" => "Lista",
        _ => type,
    };
}
