using System.Text;
using System.Text.Json;

namespace CSharpLab.Core.Settings;

/// <summary>
/// Cópia de segurança de um documento com alterações não salvas. Não substitui o arquivo
/// original: serve apenas para recuperar o texto após um encerramento inesperado.
/// </summary>
public sealed record RecoveryEntry(
    string Id,
    string? OriginalPath,
    string? UntitledName,
    string Text,
    int CodePage,
    bool HasBom,
    DateTime SavedUtc,
    int Order)
{
    public Encoding GetEncoding()
    {
        try
        {
            if (CodePage == 65001) return new UTF8Encoding(HasBom);
            if (CodePage == 1200) return new UnicodeEncoding(false, HasBom);
            if (CodePage == 1201) return new UnicodeEncoding(true, HasBom);
            return Encoding.GetEncoding(CodePage);
        }
        catch
        {
            return Files.TextFileIO.Utf8NoBom;
        }
    }
}

public sealed class RecoveryStore
{
    private readonly string _dir;

    public RecoveryStore(string? dir = null)
    {
        _dir = dir ?? AppPaths.RecoveryDir;
        Directory.CreateDirectory(_dir);
    }

    private string FileFor(string id) => Path.Combine(_dir, id + ".json");

    public void Save(RecoveryEntry entry)
    {
        try
        {
            AppPaths.WriteAllTextAtomic(FileFor(entry.Id), JsonSerializer.Serialize(entry));
        }
        catch (Exception ex)
        {
            AppPaths.Log(ex, "Gravando recuperação");
        }
    }

    public void Delete(string id)
    {
        try
        {
            var file = FileFor(id);
            if (File.Exists(file)) File.Delete(file);
        }
        catch (Exception ex)
        {
            AppPaths.Log(ex, "Removendo recuperação");
        }
    }

    public IReadOnlyList<RecoveryEntry> LoadAll()
    {
        var list = new List<RecoveryEntry>();
        foreach (var file in Directory.EnumerateFiles(_dir, "*.json"))
        {
            try
            {
                var entry = JsonSerializer.Deserialize<RecoveryEntry>(File.ReadAllText(file));
                if (entry != null) list.Add(entry);
            }
            catch (Exception ex)
            {
                AppPaths.Log(ex, "Lendo recuperação " + file);
            }
        }
        return list.OrderBy(e => e.Order).ThenBy(e => e.SavedUtc).ToList();
    }

    public static RecoveryEntry Create(string id, string? originalPath, string? untitledName, string text, Encoding encoding, int order) =>
        new(id, originalPath, untitledName, text, encoding.CodePage, encoding.GetPreamble().Length > 0, DateTime.UtcNow, order);
}
