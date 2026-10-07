using System.IO;

namespace CSharpLab.GameEngine;

/// <summary>
/// Encontra a tela desenhada de cada cena: Screens/&lt;Cena&gt;.json, ao lado do jogo (copiada na compilação)
/// ou na pasta de onde ele foi aberto. Relê o arquivo só quando ele muda.
/// </summary>
internal sealed class ScreenLibrary
{
    public const string Folder = "Screens";

    private readonly Dictionary<string, (DateTime Stamp, ScreenLayout Layout)> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Pastas onde procurar (os testes trocam por uma pasta própria).</summary>
    public IReadOnlyList<string> Roots { get; init; } = [AppContext.BaseDirectory, Environment.CurrentDirectory];

    /// <summary>A tela da cena (uma cópia nova, que o jogo pode mudar à vontade) ou null se a cena não foi desenhada.</summary>
    public ScreenLayout? Load(string sceneName)
    {
        var file = FindFile(sceneName);
        if (file == null) return null;
        var stamp = File.GetLastWriteTimeUtc(file);
        if (_cache.TryGetValue(file, out var cached) && cached.Stamp == stamp) return cached.Layout.Clone();

        string text;
        try
        {
            text = File.ReadAllText(file);
        }
        catch (IOException ex)
        {
            throw new GameException($"Não deu para ler {Folder}/{Path.GetFileName(file)}: {ex.Message}");
        }
        var result = ScreenFile.Parse(text);
        if (result.Layout == null)
        {
            var line = result.ErrorLine != null ? $" (linha {result.ErrorLine})" : "";
            throw new GameException($"A tela {Folder}/{Path.GetFileName(file)} tem um erro{line}: {result.Error}");
        }
        _cache[file] = (stamp, result.Layout);
        return result.Layout.Clone();
    }

    private string? FindFile(string sceneName)
    {
        foreach (var root in Roots)
        {
            var file = Path.Combine(root, Folder, sceneName + ".json");
            if (File.Exists(file)) return file;
        }
        return null;
    }
}
