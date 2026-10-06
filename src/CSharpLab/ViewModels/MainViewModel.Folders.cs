using System.IO;
using CommunityToolkit.Mvvm.Input;
using CSharpLab.Core.Files;
using CSharpLab.Core.Projects;
using CSharpLab.Core.Settings;

namespace CSharpLab.ViewModels;

// Pasta aberta e projetos: abrir, fechar, descobrir os .csproj e carregar o modelo usado pelo Roslyn.
public sealed partial class MainViewModel
{
    private int _projectLoadVersion;
    private CancellationTokenSource? _projectScanCts;
    private CancellationTokenSource? _evaluationCts;
    private int _evaluationsInFlight;

    [RelayCommand]
    private void OpenFileDialog()
    {
        var path = Dialogs.PickFile("Abrir arquivo", "Arquivos C# (*.cs;*.csproj)|*.cs;*.csproj|Todos os arquivos (*.*)|*.*",
            CurrentFolder ?? Settings.LastProjectLocation);
        if (path != null)
        {
            OpenFile(path);
            FocusEditorRequested?.Invoke();
        }
    }

    [RelayCommand]
    private async Task OpenFolderDialog()
    {
        var path = Dialogs.PickFolder("Abrir pasta", CurrentFolder ?? Settings.LastProjectLocation);
        if (path != null) await OpenFolderAsync(path, null, promptForUnsaved: true);
    }

    [RelayCommand]
    private async Task OpenProjectDialog()
    {
        var path = Dialogs.PickFile("Abrir projeto", "Projeto C# (*.csproj)|*.csproj", CurrentFolder ?? Settings.LastProjectLocation);
        if (path == null) return;
        ProjectFile file;
        try { file = await ProjectFile.ReadEvaluatedAsync(path, _updatesCts.Token); }
        catch (OperationCanceledException) when (_disposed) { return; }
        if (_disposed) return;
        if (file.Error != null)
        {
            Dialogs.ShowError("Não foi possível abrir o projeto", "O arquivo .csproj não pôde ser lido.", file.Error);
            return;
        }
        if (!file.IsConsole)
        {
            Dialogs.ShowError("Projeto não suportado", "Este projeto não é um aplicativo console. Esta versão executa apenas projetos console C#.");
            return;
        }
        await OpenFolderAsync(file.Directory, file.Path, promptForUnsaved: true);
    }

    [RelayCommand]
    private async Task OpenRecent(RecentItem? item)
    {
        if (item == null) return;
        if (item.Kind == RecentKind.Project ? !File.Exists(item.Path) : !Directory.Exists(item.Path))
        {
            NotifyError($"\"{item.Path}\" não existe mais.");
            Settings.Recent.Remove(item);
            RecentItems.Remove(item);
            ScheduleSettingsSave();
            return;
        }
        if (item.Kind == RecentKind.Project)
            await OpenFolderAsync(Path.GetDirectoryName(item.Path)!, item.Path, promptForUnsaved: true);
        else
            await OpenFolderAsync(item.Path, null, promptForUnsaved: true);
    }

    [RelayCommand]
    private async Task CloseFolder()
    {
        if (CurrentFolder == null) return;
        if (!ConfirmCloseAll(Documents)) return;
        StopRun();
        foreach (var d in Documents.ToList()) RemoveDocument(d);
        CloseFolderCore();
        _model = null;
        _ls?.LoadProject(null);
        Problems.SetLive([]);
        Problems.ClearBuild();
        NewDraft();
        await Task.CompletedTask;
    }

    private void CloseFolderCore()
    {
        _projectLoadVersion++;
        _projectScanCts?.Cancel();
        _fsTimer.Stop();
        _fsEvents.Clear();
        _model = null;
        _evaluationCts?.Cancel();
        _evaluationCts = null;
        _watcher?.Dispose();
        _watcher = null;
        CurrentFolder = null;
        Projects = [];
        RunProject = null;
        Explorer.Load(null);
        ScheduleSettingsSave();
    }

    public async Task<bool> OpenFolderAsync(string folder, string? preferredProject, bool promptForUnsaved, bool partOfRun = false,
        CancellationToken ct = default, bool waitForProject = true)
    {
        if (_disposed) return false;
        ct.ThrowIfCancellationRequested();
        folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (!Directory.Exists(folder))
        {
            NotifyError($"A pasta \"{folder}\" não existe.");
            return false;
        }
        if (promptForUnsaved && !ConfirmCloseAll(Documents)) return false;
        if (!partOfRun) StopRun();
        foreach (var d in Documents.ToList()) RemoveDocument(d);
        CloseFolderCore();

        CurrentFolder = folder;
        Explorer.Load(folder);
        StartWatcher(folder);
        Problems.SetLive([]);
        Problems.ClearBuild();
        Settings.Session ??= new SessionState();
        Settings.Session.Folder = folder;

        if (preferredProject != null)
        {
            Settings.ProjectChoices[folder] = Path.GetFullPath(preferredProject);
            AddRecent(preferredProject, RecentKind.Project);
        }
        else
        {
            AddRecent(folder, RecentKind.Folder);
        }

        var loading = LoadProjectsAsync(ct: ct);
        if (!waitForProject)
        {
            _ = loading.ContinueWith(t => AppPaths.Log(t.Exception!, "Carregando projetos"), TaskContinuationOptions.OnlyOnFaulted);
            ScheduleSettingsSave();
            return true;
        }
        await loading;
        ct.ThrowIfCancellationRequested();
        if (_disposed) return false;
        ScheduleSettingsSave();
        return true;
    }

    private void AddRecent(string path, RecentKind kind)
    {
        Settings.AddRecent(path, kind);
        RecentItems.Clear();
        foreach (var r in Settings.Recent) RecentItems.Add(r);
    }

    private async Task LoadProjectsAsync(bool useCache = true, CancellationToken ct = default)
    {
        var folder = CurrentFolder;
        if (_disposed || folder == null) return;
        var version = ++_projectLoadVersion;
        _projectScanCts?.Cancel();
        var scan = _projectScanCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _evaluationCts?.Cancel();
        var candidates = await Task.Run(() => ProjectLocator.FindProjects(folder));
        if (_disposed || scan.IsCancellationRequested || CurrentFolder != folder || version != _projectLoadVersion) return;

        // Contexto imediato para as sugestões: o projeto provável, do cache (ou estimado), antes
        // de o SDK confirmar as propriedades de cada .csproj (isso leva ~1 s por projeto).
        if (useCache && _model == null)
        {
            var quickConsoles = ProjectLocator.ConsoleProjects(candidates);
            var guess = (Settings.ProjectChoices.TryGetValue(folder, out var remembered)
                            ? candidates.FirstOrDefault(p => string.Equals(p.Path, remembered, StringComparison.OrdinalIgnoreCase))
                            : null)
                        ?? (quickConsoles.Count == 1 ? quickConsoles[0] : null)
                        ?? (candidates.Count == 1 ? candidates[0] : null);
            if (guess != null)
            {
                var quickModel = await Task.Run(() => ProjectEvaluator.TryLoadCached(guess.Path) ?? ProjectEvaluator.Estimate(guess.Path));
                if (_disposed || scan.IsCancellationRequested || CurrentFolder != folder || version != _projectLoadVersion) return;
                ApplyModel(quickModel);
            }
        }

        ProjectFile[] projects;
        try
        {
            using var slots = new SemaphoreSlim(4);
            projects = await Task.WhenAll(candidates.Select(async p =>
            {
                await slots.WaitAsync(scan.Token);
                try { return await ProjectFile.ReadEvaluatedAsync(p.Path, scan.Token); }
                finally { slots.Release(); }
            }));
        }
        catch (OperationCanceledException) { return; }
        if (_disposed || scan.IsCancellationRequested || CurrentFolder != folder || version != _projectLoadVersion) return;
        Projects = projects;
        var consoles = ProjectLocator.ConsoleProjects(projects);

        ProjectFile? chosen = null;
        if (Settings.ProjectChoices.TryGetValue(folder, out var saved))
            chosen = consoles.FirstOrDefault(p => string.Equals(p.Path, saved, StringComparison.OrdinalIgnoreCase));
        chosen ??= consoles.Count == 1 ? consoles[0] : null;
        RunProject = chosen;

        var analysis = chosen ?? consoles.FirstOrDefault() ?? projects.FirstOrDefault(p => p.IsSdkStyle && p.Error == null);
        if (analysis != null)
        {
            await LoadModelAsync(analysis.Path, useCache, scan.Token);
        }
        else
        {
            // Pasta sem projeto: os arquivos .cs são analisados juntos, como um projeto console.
            var files = await Task.Run(() => ProjectLocator.DefaultCompileFiles(folder));
            if (_disposed || scan.IsCancellationRequested || CurrentFolder != folder || version != _projectLoadVersion) return;
            var model = ProjectModel.Loose(Path.GetFileName(folder), folder) with { CompileFiles = files.Count <= 300 ? files : [] };
            ApplyModel(model);
        }
    }

    private async Task LoadModelAsync(string projectPath, bool useCache = true, CancellationToken ct = default)
    {
        if (_disposed || ct.IsCancellationRequested) return;
        _evaluationCts?.Cancel();
        var cts = _evaluationCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var quick = await Task.Run(() => (useCache ? ProjectEvaluator.TryLoadCached(projectPath) : null) ?? ProjectEvaluator.Estimate(projectPath));
        if (_disposed || cts.IsCancellationRequested) return;
        ApplyModel(quick);
        if (quick.IsEvaluated) return;

        _evaluationsInFlight++;
        try
        {
            var evaluated = await Task.Run(() => ProjectEvaluator.EvaluateAsync(projectPath, allowRestore: true, cts.Token));
            if (!cts.IsCancellationRequested && evaluated.IsEvaluated)
                ApplyModel(evaluated);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppPaths.Log(ex, "Avaliando projeto");
        }
        finally
        {
            _evaluationsInFlight--;
        }
    }

    private void ApplyModel(ProjectModel model)
    {
        // Resultado de uma avaliação que terminou depois de a pasta mudar: descarta.
        if (_disposed || CurrentFolder == null || !FileOperations.IsSameOrInside(model.Directory, CurrentFolder)) return;
        _model = model;
        _ls?.LoadProject(model);
        ScheduleDiagnostics();
    }

    [RelayCommand]
    private async Task NewProject()
    {
        var request = Dialogs.AskNewProject("Novo projeto", null, "MeuProjeto", DefaultProjectLocation());
        if (request == null) return;
        if (!ConfirmCloseAll(Documents)) return;
        try
        {
            var created = ProjectCreator.CreateConsoleProject(request.Location, request.Name);
            Settings.LastProjectLocation = request.Location;
            await OpenFolderAsync(created.Directory, created.ProjectPath, promptForUnsaved: false);
            OpenFile(created.ProgramPath);
            FocusEditorRequested?.Invoke();
        }
        catch (Exception ex)
        {
            Dialogs.ShowError("Não foi possível criar o projeto", FileErrors.Describe(ex, request.Name));
        }
    }

    private string DefaultProjectLocation()
    {
        if (Settings.LastProjectLocation is { } last && Directory.Exists(last)) return last;
        if (CurrentFolder != null) return Path.GetDirectoryName(CurrentFolder) ?? CurrentFolder;
        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    [RelayCommand]
    private async Task ChooseRunProject()
    {
        if (CurrentFolder == null) return;
        var consoles = ProjectLocator.ConsoleProjects(Projects);
        if (consoles.Count < 2) return;
        var choice = Dialogs.SelectProject(consoles, CurrentFolder);
        if (choice == null) return;
        Settings.ProjectChoices[CurrentFolder] = choice.Path;
        _projectLoadVersion++;
        _projectScanCts?.Cancel();
        RunProject = choice;
        await LoadModelAsync(choice.Path);
        ScheduleSettingsSave();
    }
}
