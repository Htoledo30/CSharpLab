using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CSharpLab.GameEngine;

/// <summary>Arquivo atual, cópia anterior e histórico das ações de recuperação/novo jogo.</summary>
internal sealed class GameSaveStore(string folder, string legacyPath)
{
    private static readonly UTF8Encoding Utf8 = new(false, throwOnInvalidBytes: true);
    internal string Path => System.IO.Path.Combine(folder, "save.json");
    internal string BackupPath => System.IO.Path.Combine(folder, "save.anterior.json");

    private sealed record Snapshot(byte[] Bytes, JsonObject Data);

    public JsonObject Read() => !Directory.Exists(folder) ? ReadCurrent()?.Data ?? [] : Locked(() => ReadCurrent()?.Data ?? []);

    public void Save(string name, JsonNode? value) => Locked(() =>
    {
        var previous = ReadCurrent(); // Um save inválido nunca vira um jogo novo silenciosamente.
        if (previous != null && previous.Data.ContainsKey(name) && JsonNode.DeepEquals(previous.Data[name], value))
            return true; // Não gastar uma cópia anterior nem escrever no disco se nada mudou.
        var data = previous?.Data ?? [];
        data[name] = value;
        var bytes = Utf8.GetBytes(data.ToJsonString(Game.SaveOptions));
        CheckWritable(Path);
        if (previous != null)
        {
            PreserveDamagedBackup();
            CheckWritable(BackupPath);
        }
        // O Windows troca o save e guarda o original como backup na mesma operação.
        WriteAtomic(Path, bytes, previous != null ? BackupPath : null);
        return true;
    });

    public bool Restore() => Locked(() =>
    {
        if (!File.Exists(BackupPath)) return false;
        var previous = ReadSnapshot(BackupPath, "A cópia anterior do save");
        CheckWritable(Path);
        ArchiveCurrent("restauracao");
        // Não usar Save aqui: ele trocaria o backup bom pelo arquivo danificado.
        WriteAtomic(Path, previous.Bytes);
        return true;
    });

    public void Reset() => Locked(() =>
    {
        Snapshot? current = null;
        if (File.Exists(Path))
        {
            try { current = ReadSnapshot(Path, "O save atual"); }
            catch (GameException) { /* Será preservado no histórico antes de limpar. */ }
        }
        if (current?.Data.Count == 0) return true; // Novo jogo repetido não apaga a cópia para desfazer.
        CheckWritable(Path);
        ArchiveCurrent("novo-jogo", includeBackup: true);
        // Um objeto vazio significa "novo jogo". A troca é atômica e o legado não volta sozinho.
        if (current != null) CheckWritable(BackupPath);
        WriteAtomic(Path, Utf8.GetBytes("{}"), current != null ? BackupPath : null);
        return true;
    });

    public bool ImportLegacy() => Locked(() =>
    {
        if (File.Exists(Path) || File.Exists(BackupPath) || !File.Exists(legacyPath)) return false;
        var legacy = ReadSnapshot(legacyPath, "O save antigo");
        WriteAtomic(Path, legacy.Bytes);
        return true; // O arquivo antigo permanece intacto, pois pode ter sido compartilhado por outro jogo.
    });

    private Snapshot? ReadCurrent()
    {
        if (File.Exists(Path)) return ReadSnapshot(Path, "O save atual");
        if (File.Exists(BackupPath))
            throw new GameException($"O save atual está ausente, mas existe uma cópia anterior. Use game.RestoreSave() para recuperá-la e depois carregue os valores de novo. Nenhum arquivo foi substituído.\nCópia: {BackupPath}");
        if (File.Exists(legacyPath))
            throw new GameException($"Encontrei um save antigo com o título deste jogo. Confira se esse progresso pertence a este projeto e use game.ImportLegacySave() antes de game.Load para copiá-lo. Se preferir começar de novo, use game.DeleteSave(). O arquivo antigo será preservado.\nArquivo antigo: {legacyPath}");
        return null;
    }

    private static Snapshot ReadSnapshot(string path, string label)
    {
        var bytes = File.ReadAllBytes(path);
        try
        {
            var text = Utf8.GetString(bytes).TrimStart('\uFEFF');
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("O arquivo precisa ser um objeto JSON.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
                if (!names.Add(property.Name)) throw new JsonException($"O nome \"{property.Name}\" aparece mais de uma vez.");
            return new(bytes, (JsonObject)JsonNode.Parse(text)!);
        }
        catch (Exception ex) when (ex is JsonException or DecoderFallbackException)
        {
            throw new GameException($"{label} está danificado ou não tem o formato esperado. Ele foi preservado e não será sobrescrito pelo game.Save. Use game.RestoreSave() se houver uma cópia anterior, ou game.DeleteSave() para arquivar o progresso e começar de novo.\nArquivo: {path}\nDetalhe: {ex.Message}");
        }
    }

    private void PreserveDamagedBackup()
    {
        if (!File.Exists(BackupPath)) return;
        try { ReadSnapshot(BackupPath, "A cópia anterior do save"); }
        catch (GameException)
        {
            var history = NewHistoryFolder("backup-danificado");
            WriteAtomic(System.IO.Path.Combine(history, "save.anterior.json"), File.ReadAllBytes(BackupPath));
        }
    }

    private void ArchiveCurrent(string reason, bool includeBackup = false)
    {
        if (!File.Exists(Path) && (!includeBackup || !File.Exists(BackupPath))) return;
        var history = NewHistoryFolder(reason);
        if (File.Exists(Path)) WriteAtomic(System.IO.Path.Combine(history, "save.json"), File.ReadAllBytes(Path));
        if (includeBackup && File.Exists(BackupPath))
            WriteAtomic(System.IO.Path.Combine(history, "save.anterior.json"), File.ReadAllBytes(BackupPath));
    }

    private string NewHistoryFolder(string reason)
    {
        var history = System.IO.Path.Combine(folder, "Historico", DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss") + "_UTC_" + reason + "_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(history);
        return history;
    }

    private T Locked<T>(Func<T> action)
    {
        Directory.CreateDirectory(folder);
        FileStream saveLock;
        try { saveLock = new FileStream(System.IO.Path.Combine(folder, "save.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex) when ((ex.HResult & 0xFFFF) is 32 or 33)
        { throw new GameException("Outra janela está lendo ou salvando este jogo. Espere um instante e tente novamente."); }
        using (saveLock) return action();
    }

    private static void CheckWritable(string path)
    {
        if (File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly))
            throw new UnauthorizedAccessException($"O arquivo {path} está marcado como somente leitura.");
    }

    private static void WriteAtomic(string path, byte[] bytes, string? backup = null)
    {
        CheckWritable(path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var temp = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, ".save-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path)) File.Replace(temp, path, backup);
            else File.Move(temp, path);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* O save original não é removido. */ }
        }
    }
}
