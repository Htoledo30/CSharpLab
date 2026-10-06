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

    // Gravações e exclusões passam por uma fila única, fora da interface e na ordem em que foram
    // pedidas: uma gravação atrasada nunca recria uma recuperação já apagada, nem uma versão
    // antiga sobrescreve uma mais nova.
    private readonly object _queueGate = new();
    private Task _tail = Task.CompletedTask;

    private void Enqueue(Action action)
    {
        lock (_queueGate)
            _tail = _tail.ContinueWith(_ => action(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    /// <summary>Espera as operações pendentes (usado ao fechar o aplicativo e nos testes).</summary>
    public bool Flush(TimeSpan timeout)
    {
        Task tail;
        lock (_queueGate) tail = _tail;
        return tail.Wait(timeout);
    }

    public void Save(RecoveryEntry entry) => Enqueue(() =>
    {
        try
        {
            AppPaths.WriteAllTextAtomic(FileFor(entry.Id), JsonSerializer.Serialize(entry));
        }
        catch (Exception ex)
        {
            AppPaths.Log(ex, "Gravando recuperação");
        }
    });

    public void Delete(string id) => Enqueue(() =>
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
    });

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
