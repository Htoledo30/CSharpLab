using System.Text.Json;
using System.Text.Json.Serialization;
using CSharpLab.Core.Files;
using CSharpLab.Core.Projects;
using CSharpLab.Core.Settings;

namespace CSharpLab.Core.Build;

/// <summary>Troca recuperável de pasta + ZIP. Nunca apaga uma exportação anterior.</summary>
internal sealed class ExportTransaction : IDisposable
{
    internal const string WorkDirectoryName = ".exportacao";
    internal const string HistoryDirectoryName = "Versões anteriores";
    private readonly FileStream _lock;
    private readonly string _stage;
    private readonly string _journalPath;
    private readonly string _history;
    private Journal? _journal;
    private bool _safeToClean;

    internal sealed record Journal(
        [property: JsonRequired] string BackupName,
        [property: JsonRequired] bool HadFolder,
        [property: JsonRequired] bool HadZip,
        [property: JsonRequired] bool Completed = false);

    public string Output { get; }
    public string Zip { get; }
    public string PreparedFolder => Path.Combine(_stage, "jogo");
    public string PreparedZip => Path.Combine(_stage, "jogo.zip");
    private string Backup => Path.Combine(_history, _journal!.BackupName);
    private string PreviousFolder => Path.Combine(Backup, "jogo");
    private string PreviousZip => Path.Combine(Backup, "jogo.zip");

    private ExportTransaction(string root, string name, FileStream exportLock)
    {
        _lock = exportLock;
        Output = Path.Combine(root, name);
        Zip = Output + ".zip";
        _stage = Path.Combine(root, WorkDirectoryName, name, "pendente");
        _journalPath = Path.Combine(_stage, "troca.json");
        _history = Path.Combine(root, HistoryDirectoryName, name);
    }

    public static ExportTransaction Open(ProjectFile project, string destination)
    {
        var root = Path.GetFullPath(destination);
        var name = project.Name;
        if (FileOperations.ValidateName(name) is { } error) throw new GameExportException(error);
        if (name.Equals(WorkDirectoryName, StringComparison.OrdinalIgnoreCase) ||
            name.Equals(HistoryDirectoryName, StringComparison.OrdinalIgnoreCase))
            throw new GameExportException("Esse nome é reservado para as pastas de exportação. Renomeie o projeto antes de exportar.");
        // Não mover a pasta de código caso um projeto esteja dentro da própria exportação.
        var output = Path.Combine(root, name);
        if (FileOperations.IsSameOrInside(project.Directory, output) || FileOperations.IsSameOrInside(root, project.Directory) ||
            FileOperations.IsSameOrInside(project.Directory, Path.Combine(root, WorkDirectoryName)) ||
            FileOperations.IsSameOrInside(project.Directory, Path.Combine(root, HistoryDirectoryName)))
            throw new GameExportException("A pasta de exportação precisa ficar separada da pasta do código do jogo.");
        var work = Path.Combine(root, WorkDirectoryName, name);
        RejectLinkedParents(work);
        RejectLinkedParents(Path.Combine(root, HistoryDirectoryName, name));
        Directory.CreateDirectory(work);
        FileStream exportLock;
        try { exportLock = new FileStream(Path.Combine(work, "exportando.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex) when ((ex.HResult & 0xFFFF) is 32 or 33)
        { throw new GameExportException("Já existe uma exportação deste jogo em andamento. Espere ela terminar e tente novamente.", inner: ex); }
        var transaction = new ExportTransaction(root, name, exportLock);
        try
        {
            transaction.Recover();
            Directory.CreateDirectory(transaction.PreparedFolder);
            transaction._safeToClean = true;
            return transaction;
        }
        catch { transaction.Dispose(); throw; }
    }

    public string? Install()
    {
        if (File.Exists(Output) || Directory.Exists(Zip))
            throw new GameExportException("Há um arquivo no lugar da pasta do jogo, ou uma pasta no lugar do ZIP. Ajuste esses nomes antes de exportar.");
        RejectLinkedParents(Output);
        RejectLinkedParents(Zip);
        _journal = new(DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + "_" + Guid.NewGuid().ToString("N"),
            Directory.Exists(Output), File.Exists(Zip));
        SaveJournal();
        _safeToClean = false;
        try
        {
            if (_journal.HadFolder || _journal.HadZip) Directory.CreateDirectory(Backup);
            if (_journal.HadFolder) Directory.Move(Output, PreviousFolder);
            if (_journal.HadZip) File.Move(Zip, PreviousZip);
            Directory.Move(PreparedFolder, Output);
            File.Move(PreparedZip, Zip);
            _journal = _journal with { Completed = true };
            SaveJournal();
            _safeToClean = true;
            return _journal.HadFolder || _journal.HadZip ? Backup : null;
        }
        catch (Exception failure)
        {
            // O disco, antivírus ou um jogo aberto podem impedir uma das movimentações.
            try { RestorePrevious(); _safeToClean = true; }
            catch (Exception recoveryFailure)
            {
                throw new GameExportException(
                    $"A troca não terminou e não foi possível restaurar tudo agora. Feche o jogo e tente exportar novamente: o editor tentará recuperar. A versão anterior está em: {Backup}",
                    failure.Message + "\nRecuperação: " + recoveryFailure.Message, failure);
            }
            throw new GameExportException("Não foi possível substituir a exportação. A versão anterior foi preservada. Feche o jogo e o ZIP, e tente novamente.", failure.Message, failure);
        }
    }

    private void Recover()
    {
        if (!Directory.Exists(_stage)) return;
        RejectLinkedParents(_stage);
        if (File.Exists(_journalPath))
        {
            try
            {
                _journal = JsonSerializer.Deserialize<Journal>(File.ReadAllText(_journalPath));
                if (_journal == null || FileOperations.ValidateName(_journal.BackupName) != null ||
                    !_journal.BackupName.Contains('_')) throw new InvalidDataException("Registro de troca inválido.");
                RejectLinkedParents(Backup);
                if (!_journal.Completed) RestorePrevious();
            }
            catch (Exception ex)
            {
                throw new GameExportException($"Uma exportação interrompida precisa ser recuperada. Nenhum arquivo foi apagado. Confira as pastas {Output} e {_history} antes de tentar novamente.", ex.Message, ex);
            }
        }
        _safeToClean = true;
        CleanIfSafe();
        // Não reutilizar arquivos de uma tentativa anterior se a limpeza falhou.
        if (Directory.Exists(_stage)) throw new GameExportException("Não foi possível limpar a preparação anterior. Feche os arquivos dessa pasta e tente novamente.");
        _journal = null;
    }

    private void RestorePrevious()
    {
        RestoreFolder();
        RestoreZip();
    }

    private void RestoreFolder()
    {
        if (Directory.Exists(PreviousFolder))
        {
            if (Directory.Exists(Output)) Directory.Move(Output, Path.Combine(_stage, "tentativa-jogo"));
            Directory.Move(PreviousFolder, Output);
        }
        else if (!_journal!.HadFolder && !Directory.Exists(PreparedFolder) && Directory.Exists(Output))
            Directory.Move(Output, PreparedFolder);
    }

    private void RestoreZip()
    {
        if (File.Exists(PreviousZip))
        {
            if (File.Exists(Zip)) File.Move(Zip, Path.Combine(_stage, "tentativa.zip"));
            File.Move(PreviousZip, Zip);
        }
        else if (!_journal!.HadZip && !File.Exists(PreparedZip) && File.Exists(Zip))
            File.Move(Zip, PreparedZip);
    }

    private void SaveJournal() => TextFileIO.Save(_journalPath, JsonSerializer.Serialize(_journal), TextFileIO.Utf8NoBom);

    public void CleanIfSafe()
    {
        if (!_safeToClean || !Directory.Exists(_stage)) return;
        try
        {
            // Somente nossa pasta de preparação, nunca Output ou History.
            RejectLinkedParents(_stage);
            DeletePreparation(_stage);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { AppPaths.Log(ex, "Limpando preparação de exportação"); }
    }

    private static void DeletePreparation(string folder)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(folder))
        {
            var attributes = File.GetAttributes(path);
            // Um link é removido sem atravessar a pasta que ele aponta.
            if (attributes.HasFlag(FileAttributes.Directory))
            {
                if (attributes.HasFlag(FileAttributes.ReparsePoint)) Directory.Delete(path);
                else DeletePreparation(path);
            }
            else File.Delete(path);
        }
        Directory.Delete(folder);
    }

    private static void RejectLinkedParents(string path)
    {
        for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            if (FileOperations.PathExists(current) && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                throw new GameExportException("Use uma pasta de exportação real, sem atalhos de disco, para preservar as versões anteriores.");
    }

    public void Dispose() => _lock.Dispose();
}
