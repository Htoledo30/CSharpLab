using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using CSharpLab.Core.Files;
using CSharpLab.Core.Projects;
using CSharpLab.Core.Settings;

namespace CSharpLab.ViewModels;

// Mudanças na pasta: ações do explorador (renomear/excluir) e eventos do disco vindos de outros programas.
public sealed partial class MainViewModel
{
    private FileSystemWatcher? _watcher;
    private readonly ConcurrentQueue<FolderEvent> _fsEvents = new();

    /// <summary>
    /// Confirma o envio para a Lixeira. Se houver abas com alterações não salvas dentro do caminho,
    /// avisa que elas serão perdidas (a Lixeira guarda só a versão do disco).
    /// </summary>
    public bool ConfirmDelete(string path, bool isDirectory)
    {
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
        var what = isDirectory ? $"a pasta \"{name}\" e todo o seu conteúdo" : $"\"{name}\"";
        var dirty = Documents.Where(d => d.IsDirty && d.FilePath != null && FileOperations.IsSameOrInside(d.FilePath, path)).ToList();
        if (dirty.Count == 0)
            return Dialogs.Confirm("Enviar para a Lixeira", $"Enviar {what} para a Lixeira?", "Enviar para a Lixeira", danger: true);

        var files = string.Join(", ", dirty.Take(3).Select(d => $"\"{d.Title}\"")) + (dirty.Count > 3 ? $" e mais {dirty.Count - 3}" : "");
        return Dialogs.Confirm("Alterações não salvas serão perdidas",
            $"{files} {(dirty.Count == 1 ? "tem" : "têm")} alterações não salvas. A Lixeira guarda só a versão do disco; " +
            $"as alterações feitas aqui serão perdidas. Enviar {what} para a Lixeira mesmo assim?",
            "Enviar e perder alterações", danger: true);
    }

    public void OnPathRenamed(string oldPath, string newPath)
    {
        foreach (var doc in Documents.Where(d => d.FilePath != null && FileOperations.IsSameOrInside(d.FilePath, oldPath)).ToList())
        {
            var oldKey = doc.LanguageKey;
            var wasCSharp = doc.IsCSharp;
            var relative = Path.GetRelativePath(oldPath, doc.FilePath!);
            var updated = relative == "." ? newPath : Path.Combine(newPath, relative);
            doc.FilePath = Path.GetFullPath(updated);
            doc.DiskStamp = FileStamp.Of(doc.FilePath);
            _recoveryVersions.Remove(doc.RecoveryId);
            if (wasCSharp && doc.IsCSharp) _ls?.MoveDocument(oldKey, doc.LanguageKey, doc.FilePath);
            else if (wasCSharp) _ls?.CloseDocument(oldKey);
            else AttachLanguage(doc);
        }
        _ls?.OnFileDeleted(oldPath);
        if (Directory.Exists(newPath))
        {
            foreach (var f in ProjectLocator.DefaultCompileFiles(newPath)) _ls?.OnFileCreatedOrChanged(f);
        }
        else
        {
            _ls?.OnFileCreatedOrChanged(newPath);
        }
        if (newPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || oldPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            _ = LoadProjectsAsync();
        UpdateHints();
        ScheduleDiagnostics();
        ScheduleRecovery();
    }

    public void OnPathDeleted(string path)
    {
        foreach (var doc in Documents.Where(d => d.FilePath != null && FileOperations.IsSameOrInside(d.FilePath, path)).ToList())
            RemoveDocument(doc);
        _ls?.OnFileDeleted(path);
        if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || (RunProject != null && !File.Exists(RunProject.Path)))
            _ = LoadProjectsAsync();
        ScheduleDiagnostics();
    }

    private void StartWatcher(string folder)
    {
        try
        {
            var w = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024,
            };
            w.Created += (_, e) => Enqueue(new FolderEvent(WatcherChangeTypes.Created, e.FullPath));
            w.Changed += (_, e) => Enqueue(new FolderEvent(WatcherChangeTypes.Changed, e.FullPath));
            w.Deleted += (_, e) => Enqueue(new FolderEvent(WatcherChangeTypes.Deleted, e.FullPath));
            w.Renamed += (_, e) => Enqueue(new FolderEvent(WatcherChangeTypes.Renamed, e.FullPath, e.OldFullPath));
            w.Error += (_, _) => Enqueue(new FolderEvent(WatcherChangeTypes.All, folder));
            w.EnableRaisingEvents = true;
            _watcher = w;
        }
        catch (Exception ex)
        {
            AppPaths.Log(ex, "Observando a pasta");
        }
    }

    /// <summary>Chamado na thread do monitor; o processamento acontece em lote na thread da interface.</summary>
    private void Enqueue(FolderEvent e)
    {
        if (_disposed || CurrentFolder is not { } folder || !FolderChanges.ShouldTrack(e, folder)) return;
        _fsEvents.Enqueue(e);
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (!_disposed && CurrentFolder != null && !_fsTimer.IsEnabled) _fsTimer.Start();
        });
    }

    private void ProcessFileSystemEvents()
    {
        var folder = CurrentFolder;
        var events = new List<FolderEvent>();
        while (_fsEvents.TryDequeue(out var e)) events.Add(e);
        if (_disposed || folder == null) return;

        var changes = FolderChanges.Analyze(events, folder, _model?.CompileFiles, evaluationRunning: _evaluationsInFlight > 0);
        foreach (var path in changes.Present) _ls?.OnFileCreatedOrChanged(path);
        foreach (var path in changes.Gone) _ls?.OnFileDeleted(path);

        if (changes.FullRefresh) Explorer.Root?.Refresh();
        else Explorer.RefreshPaths(changes.Directories);

        CheckAllExternalChanges();
        if (changes.ReloadProjects || changes.FullRefresh)
        {
            _runCache.Clear();
            _ = LoadProjectsAsync(useCache: false);
        }
        else if (changes.Reevaluate && _model?.ProjectPath is { } analyzed) _ = ReevaluateAsync(analyzed);
        else if (changes.Reevaluate) _ = LoadProjectsAsync(useCache: false);
        ScheduleDiagnostics();
    }

    private async Task ReevaluateAsync(string projectPath)
    {
        if (_disposed) return;
        _evaluationCts?.Cancel();
        var cts = _evaluationCts = new CancellationTokenSource();
        _evaluationsInFlight++;
        try
        {
            var model = await Task.Run(() => ProjectEvaluator.EvaluateAsync(projectPath, allowRestore: true, cts.Token));
            if (!cts.IsCancellationRequested) ApplyModel(model);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) { AppPaths.Log(ex, "Reavaliando projeto"); }
        finally { _evaluationsInFlight--; }
    }
}
