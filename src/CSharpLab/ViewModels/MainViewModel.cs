using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CSharpLab.Core.Build;
using CSharpLab.Core.Files;
using CSharpLab.Core.Language;
using CSharpLab.Core.Projects;
using CSharpLab.Core.Settings;
using CSharpLab.Core.Terminal;

namespace CSharpLab.ViewModels;

public enum RunState
{
    Idle,
    Preparing,
    Building,
    Running,
    Stopping,
}

/// <summary>Estado e comandos da janela principal: documentos, pasta/projeto, análise e execução.</summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private const int MaxFileBytes = 8 * 1024 * 1024;

    private readonly RecoveryStore _recovery = new();
    private readonly Task<LanguageService> _languageTask;
    private LanguageService? _ls;
    private readonly DispatcherTimer _diagnosticsTimer;
    private readonly DispatcherTimer _recoveryTimer;
    private readonly DispatcherTimer _noticeTimer;
    private readonly DispatcherTimer _fsTimer;
    private readonly DispatcherTimer _settingsTimer;
    private CancellationTokenSource? _diagnosticsCts;
    private CancellationTokenSource? _evaluationCts;
    private CancellationTokenSource? _runCts;
    private PseudoConsoleSession? _session;
    private bool _stopRequested;
    private FileSystemWatcher? _watcher;
    private readonly ConcurrentQueue<(WatcherChangeTypes Type, string Path, string? OldPath)> _fsEvents = new();
    private readonly Dictionary<string, int> _recoveryVersions = [];
    private bool _askingExternalChange;
    private int _untitledCounter;
    private ProjectModel? _model;

    public MainViewModel(AppSettings settings)
    {
        Settings = settings;
        _diagnosticsTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(420) };
        _diagnosticsTimer.Tick += (_, _) => { _diagnosticsTimer.Stop(); _ = RunDiagnosticsAsync(); };
        _recoveryTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(1500) };
        _recoveryTimer.Tick += (_, _) => { _recoveryTimer.Stop(); WriteRecovery(); };
        _noticeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
        _noticeTimer.Tick += (_, _) => { _noticeTimer.Stop(); Notice = null; };
        _fsTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        _fsTimer.Tick += (_, _) => { _fsTimer.Stop(); ProcessFileSystemEvents(); };
        _settingsTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _settingsTimer.Tick += (_, _) => { _settingsTimer.Stop(); SaveSettings(); };

        Explorer = new ExplorerViewModel(this);
        Problems = new ProblemsViewModel(DisplayNameFor);
        RecentItems = new ObservableCollection<RecentItem>(settings.Recent);
        EditorFontSize = settings.FontSize;
        IsExplorerVisible = settings.ExplorerVisible;
        ExplorerWidth = Math.Clamp(settings.ExplorerWidth, 160, 600);
        IsPanelOpen = false;
        PanelHeight = settings.PanelHeight;
        PanelTab = settings.PanelTab == "problems" ? "problems" : "terminal";

        // O Roslyn é carregado em segundo plano: o editor já pode ser usado antes disso.
        _languageTask = Task.Run(() => new LanguageService());
    }

    public AppSettings Settings { get; }
    public IDialogService Dialogs { get; set; } = null!;
    public ITerminalHost? Terminal { get; set; }
    public ExplorerViewModel Explorer { get; }
    public ProblemsViewModel Problems { get; }
    public ObservableCollection<DocumentViewModel> Documents { get; } = [];
    public ObservableCollection<RecentItem> RecentItems { get; }
    public LanguageService? Language => _ls;

    /// <summary>Pedido para a interface posicionar o cursor (documento, linha, coluna, deslocamento opcional).</summary>
    public event Action<DocumentViewModel, int, int, int?>? GoToRequested;
    public event Action? FocusEditorRequested;
    public event Action? LanguageReady;

    [ObservableProperty]
    public partial DocumentViewModel? ActiveDocument { get; set; }

    [ObservableProperty]
    public partial string? CurrentFolder { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<ProjectFile> Projects { get; set; } = [];

    [ObservableProperty]
    public partial ProjectFile? RunProject { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy), nameof(IsRunning), nameof(RunButtonText), nameof(RunButtonToolTip))]
    public partial RunState RunState { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    [ObservableProperty]
    public partial string? LanguageStatus { get; set; } = "Carregando C#…";

    [ObservableProperty]
    public partial string? Notice { get; set; }

    [ObservableProperty]
    public partial bool NoticeIsError { get; set; }

    [ObservableProperty]
    public partial bool SdkMissing { get; set; }

    [ObservableProperty]
    public partial string CaretText { get; set; } = "";

    [ObservableProperty]
    public partial double EditorFontSize { get; set; }

    [ObservableProperty]
    public partial bool IsExplorerVisible { get; set; }

    [ObservableProperty]
    public partial double ExplorerWidth { get; set; }

    [ObservableProperty]
    public partial bool IsPanelOpen { get; set; }

    [ObservableProperty]
    public partial double PanelHeight { get; set; }

    [ObservableProperty]
    public partial string PanelTab { get; set; }

    [ObservableProperty]
    public partial string? RunInfo { get; set; }

    public bool IsBusy => RunState != RunState.Idle;
    public bool IsRunning => RunState == RunState.Running;
    public string RunButtonText => IsBusy ? "Parar" : "Executar";
    public string RunButtonToolTip => IsBusy
        ? "Parar a execução (Shift+F5)"
        : "Salvar os arquivos do projeto, compilar e executar (F5)";

    public string WindowTitle => CurrentFolder != null ? $"{Path.GetFileName(CurrentFolder.TrimEnd('\\'))} — CSharp Lab" : "CSharp Lab";

    public string ProjectLabel => RunProject?.Name ?? (CurrentFolder != null ? Path.GetFileName(CurrentFolder.TrimEnd('\\')) : "Sem projeto");

    public bool HasMultipleProjects => ProjectLocator.ConsoleProjects(Projects).Count > 1;

    partial void OnCurrentFolderChanged(string? value)
    {
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(ProjectLabel));
    }

    partial void OnRunProjectChanged(ProjectFile? value) => OnPropertyChanged(nameof(ProjectLabel));

    partial void OnProjectsChanged(IReadOnlyList<ProjectFile> value) => OnPropertyChanged(nameof(HasMultipleProjects));

    partial void OnActiveDocumentChanged(DocumentViewModel? oldValue, DocumentViewModel? newValue)
    {
        if (oldValue != null) oldValue.IsActive = false;
        if (newValue != null)
        {
            newValue.IsActive = true;
            if (newValue.FilePath != null) Explorer.Reveal(newValue.FilePath);
        }
        else
        {
            CaretText = "";
        }
        ScheduleSettingsSave();
    }

    partial void OnEditorFontSizeChanged(double value) => ScheduleSettingsSave();
    partial void OnIsExplorerVisibleChanged(bool value) => ScheduleSettingsSave();
    partial void OnExplorerWidthChanged(double value) => ScheduleSettingsSave();
    partial void OnPanelHeightChanged(double value) => ScheduleSettingsSave();
    partial void OnIsPanelOpenChanged(bool value) => ScheduleSettingsSave();
    partial void OnPanelTabChanged(string value) => ScheduleSettingsSave();

    // ================================================================ inicialização

    public async Task InitializeAsync()
    {
        var session = Settings.Session;
        if (session?.Folder != null)
        {
            if (Directory.Exists(session.Folder))
                await OpenFolderAsync(session.Folder, null, promptForUnsaved: false);
            else
                NotifyInfo($"A pasta \"{session.Folder}\" não existe mais.");
        }

        RestoreRecoveredDocuments();

        if (session != null)
        {
            foreach (var file in session.Files)
            {
                if (File.Exists(file)) OpenFile(file, activate: false);
            }
            var active = session.ActiveFile != null ? FindDocument(session.ActiveFile) : null;
            if (active != null) ActiveDocument = active;
        }

        if (Documents.Count == 0)
            NewDraft(ProjectCreator.DefaultProgram);
        else ActiveDocument ??= Documents[^1];

        try
        {
            _ls = await _languageTask;
            _ls.ProjectChanged += () => Application.Current?.Dispatcher.BeginInvoke(ScheduleDiagnostics);
            if (_model != null) _ls.LoadProject(_model);
            foreach (var d in Documents) AttachLanguage(d);
            LanguageStatus = null;
            LanguageReady?.Invoke();
            ScheduleDiagnostics();
            _ = Task.Run(() => _ls.WarmUpAsync(ActiveDocument?.LanguageKey));
        }
        catch (Exception ex)
        {
            AppPaths.Log(ex, "Iniciando serviços de linguagem");
            LanguageStatus = "Sugestões indisponíveis";
        }

        _ = CheckSdkInBackgroundAsync();
    }

    private async Task CheckSdkInBackgroundAsync()
    {
        try
        {
            var status = await SdkLocator.CheckAsync(null);
            SdkMissing = status.IsMissingSdk;
        }
        catch (Exception ex)
        {
            AppPaths.Log(ex, "Verificando SDK");
        }
    }

    private void RestoreRecoveredDocuments()
    {
        foreach (var entry in _recovery.LoadAll())
        {
            try
            {
                DocumentViewModel doc;
                if (entry.OriginalPath != null)
                {
                    var existing = FindDocument(entry.OriginalPath);
                    if (existing != null)
                    {
                        _recovery.Delete(entry.Id);
                        continue;
                    }
                    var stamp = FileStamp.Of(entry.OriginalPath);
                    doc = new DocumentViewModel(entry.Text, entry.OriginalPath, entry.GetEncoding(), stamp);
                }
                else
                {
                    doc = new DocumentViewModel(entry.Text, null, entry.GetEncoding(), null, ++_untitledCounter);
                }
                doc.ForceDirty();
                _recovery.Delete(entry.Id);
                AddDocument(doc, activate: true);
                _recoveryVersions[doc.RecoveryId] = -1;
            }
            catch (Exception ex)
            {
                AppPaths.Log(ex, "Restaurando rascunho");
            }
        }
        if (Documents.Any(d => d.IsDirty))
        {
            NotifyInfo("Alterações não salvas da última sessão foram recuperadas.");
            ScheduleRecovery();
        }
    }

    // ================================================================ documentos

    public DocumentViewModel? FindDocument(string path)
    {
        var full = Path.GetFullPath(path);
        return Documents.FirstOrDefault(d => d.FilePath != null && string.Equals(d.FilePath, full, StringComparison.OrdinalIgnoreCase));
    }

    private DocumentViewModel? FindByKey(string key) =>
        Documents.FirstOrDefault(d => string.Equals(d.LanguageKey, key, StringComparison.OrdinalIgnoreCase));

    private string DisplayNameFor(string? key)
    {
        if (key == null) return "Projeto";
        var doc = FindByKey(key);
        if (doc != null) return doc.Hint != null ? $"{doc.Title} ({doc.Hint})" : doc.Title;
        return ProblemsViewModel.FileNameOf(key);
    }

    public DocumentViewModel NewDraft(string text = "")
    {
        var doc = new DocumentViewModel(text, null, TextFileIO.Utf8NoBom, null, ++_untitledCounter);
        AddDocument(doc, activate: true);
        return doc;
    }

    public void FocusEditor() => FocusEditorRequested?.Invoke();

    [RelayCommand]
    private void NewFile()
    {
        NewDraft();
        FocusEditorRequested?.Invoke();
    }

    public DocumentViewModel? OpenFile(string path, bool activate = true)
    {
        path = Path.GetFullPath(path);
        var existing = FindDocument(path);
        if (existing != null)
        {
            if (activate) ActiveDocument = existing;
            return existing;
        }

        try
        {
            var info = new FileInfo(path);
            if (info.Length > MaxFileBytes)
            {
                NotifyError($"\"{info.Name}\" é grande demais para o editor.");
                return null;
            }
            var content = TextFileIO.Read(path);
            if (content.Text.AsSpan(0, Math.Min(8000, content.Text.Length)).Contains('\0'))
            {
                NotifyError($"\"{info.Name}\" não parece ser um arquivo de texto.");
                return null;
            }
            var doc = new DocumentViewModel(content.Text, path, content.Encoding, content.Stamp);
            AddDocument(doc, activate);
            return doc;
        }
        catch (Exception ex)
        {
            NotifyError(FileErrors.Describe(ex, path));
            return null;
        }
    }

    private void AddDocument(DocumentViewModel doc, bool activate)
    {
        // Um rascunho inicial intocado é substituído pelo primeiro arquivo aberto.
        var pristine = Documents.Count == 1 && Documents[0] is { IsUntitled: true, IsDirty: false } only &&
                       only.Document.Text == ProjectCreator.DefaultProgram && !doc.IsUntitled
            ? Documents[0]
            : null;

        Documents.Add(doc);
        doc.TextChanged += OnDocumentTextChanged;
        doc.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DocumentViewModel.IsDirty)) ScheduleRecovery();
        };
        AttachLanguage(doc);
        UpdateHints();
        if (activate) ActiveDocument = doc;
        if (pristine != null) RemoveDocument(pristine);
        ScheduleSettingsSave();
    }

    private void AttachLanguage(DocumentViewModel doc)
    {
        if (_ls != null && doc.IsCSharp)
            _ls.OpenDocument(doc.LanguageKey, doc.FilePath, doc.SourceText, doc.Version);
    }

    private void OnDocumentTextChanged(DocumentViewModel doc)
    {
        if (doc.IsCSharp) _ls?.UpdateDocument(doc.LanguageKey, doc.SourceText, doc.Version);
        Problems.InvalidateBuildFor(doc.LanguageKey);
        ScheduleDiagnostics();
        ScheduleRecovery();
    }

    /// <summary>Distingue abas com o mesmo nome mostrando a pasta.</summary>
    private void UpdateHints()
    {
        foreach (var group in Documents.GroupBy(d => d.Title, StringComparer.OrdinalIgnoreCase))
        {
            var list = group.ToList();
            foreach (var d in list)
            {
                d.Hint = list.Count > 1 && d.FilePath != null
                    ? Path.GetFileName(Path.GetDirectoryName(d.FilePath)) is { Length: > 0 } dir ? dir : null
                    : null;
            }
        }
    }

    [RelayCommand]
    private void CloseTab(DocumentViewModel? doc)
    {
        doc ??= ActiveDocument;
        if (doc == null) return;
        if (doc.IsDirty)
        {
            switch (Dialogs.AskSaveChanges([doc.Title]))
            {
                case SaveChoice.Cancel:
                    return;
                case SaveChoice.Save:
                    if (!Save(doc)) return;
                    break;
            }
        }
        RemoveDocument(doc);
    }

    private void RemoveDocument(DocumentViewModel doc)
    {
        var index = Documents.IndexOf(doc);
        if (index < 0) return;
        doc.TextChanged -= OnDocumentTextChanged;
        if (doc.IsCSharp) _ls?.CloseDocument(doc.LanguageKey);
        _recovery.Delete(doc.RecoveryId);
        _recoveryVersions.Remove(doc.RecoveryId);
        Documents.RemoveAt(index);
        if (ActiveDocument == doc)
            ActiveDocument = Documents.Count == 0 ? null : Documents[Math.Min(index, Documents.Count - 1)];
        UpdateHints();
        ScheduleDiagnostics();
        ScheduleSettingsSave();
    }

    /// <summary>Pergunta sobre alterações não salvas. Retorna false se o usuário cancelar ou se salvar falhar.</summary>
    public bool ConfirmCloseAll(IEnumerable<DocumentViewModel> docs)
    {
        var dirty = docs.Where(d => d.IsDirty).ToList();
        if (dirty.Count == 0) return true;
        switch (Dialogs.AskSaveChanges(dirty.Select(d => d.Title).ToList()))
        {
            case SaveChoice.Cancel:
                return false;
            case SaveChoice.Save:
                foreach (var d in dirty)
                {
                    if (!Save(d)) return false;
                }
                return true;
            default:
                foreach (var d in dirty) _recovery.Delete(d.RecoveryId);
                return true;
        }
    }

    [RelayCommand]
    private void SaveActive()
    {
        if (ActiveDocument != null) Save(ActiveDocument);
    }

    [RelayCommand]
    private void SaveActiveAs()
    {
        if (ActiveDocument != null) SaveAs(ActiveDocument);
    }

    [RelayCommand]
    private void SaveAll()
    {
        foreach (var d in Documents.Where(d => d.IsDirty).ToList())
        {
            if (!Save(d)) return;
        }
    }

    public bool Save(DocumentViewModel doc) => doc.FilePath == null ? SaveAs(doc) : SaveTo(doc, doc.FilePath);

    public bool SaveAs(DocumentViewModel doc)
    {
        var initial = doc.FilePath != null ? Path.GetDirectoryName(doc.FilePath) : CurrentFolder ?? Settings.LastProjectLocation;
        var path = Dialogs.PickSaveFile(doc.FileName, initial);
        if (path == null) return false;
        path = Path.GetFullPath(path);
        var other = FindDocument(path);
        if (other != null && other != doc)
        {
            Dialogs.ShowError("Não foi possível salvar", $"\"{Path.GetFileName(path)}\" já está aberto em outra aba.");
            return false;
        }
        var oldKey = doc.LanguageKey;
        var wasCSharp = doc.IsCSharp;
        if (!SaveTo(doc, path)) return false;
        if (doc.FilePath == null || !string.Equals(doc.FilePath, path, StringComparison.OrdinalIgnoreCase))
        {
            doc.FilePath = path;
            if (wasCSharp && doc.IsCSharp) _ls?.MoveDocument(oldKey, doc.LanguageKey, path);
            else if (wasCSharp) _ls?.CloseDocument(oldKey);
            else AttachLanguage(doc);
            UpdateHints();
            ScheduleDiagnostics();
        }
        return true;
    }

    private bool SaveTo(DocumentViewModel doc, string path)
    {
        try
        {
            var stamp = TextFileIO.Save(path, doc.Document.Text, doc.Encoding);
            doc.MarkSaved(stamp);
            _recovery.Delete(doc.RecoveryId);
            _recoveryVersions.Remove(doc.RecoveryId);
            return true;
        }
        catch (Exception ex)
        {
            Dialogs.ShowError("Não foi possível salvar", FileErrors.Describe(ex, path));
            return false;
        }
    }

    // ================================================================ abrir

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
        var file = ProjectFile.Read(path);
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
        _watcher?.Dispose();
        _watcher = null;
        CurrentFolder = null;
        Projects = [];
        RunProject = null;
        Explorer.Load(null);
        ScheduleSettingsSave();
    }

    public async Task<bool> OpenFolderAsync(string folder, string? preferredProject, bool promptForUnsaved, bool partOfRun = false)
    {
        folder = Path.GetFullPath(folder).TrimEnd('\\');
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

        await LoadProjectsAsync();
        ScheduleSettingsSave();
        return true;
    }

    private void AddRecent(string path, RecentKind kind)
    {
        Settings.AddRecent(path, kind);
        RecentItems.Clear();
        foreach (var r in Settings.Recent) RecentItems.Add(r);
    }

    private async Task LoadProjectsAsync()
    {
        var folder = CurrentFolder;
        if (folder == null) return;
        var projects = await Task.Run(() => ProjectLocator.FindProjects(folder));
        if (CurrentFolder != folder) return;
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
            await LoadModelAsync(analysis.Path);
        }
        else
        {
            // Pasta sem projeto: os arquivos .cs são analisados juntos, como um projeto console.
            var files = await Task.Run(() => ProjectLocator.DefaultCompileFiles(folder));
            var model = ProjectModel.Loose(Path.GetFileName(folder), folder) with { CompileFiles = files.Count <= 300 ? files : [] };
            ApplyModel(model);
        }
    }

    private async Task LoadModelAsync(string projectPath)
    {
        _evaluationCts?.Cancel();
        var cts = _evaluationCts = new CancellationTokenSource();
        var quick = await Task.Run(() => ProjectEvaluator.TryLoadCached(projectPath) ?? ProjectEvaluator.Estimate(projectPath));
        if (cts.IsCancellationRequested) return;
        ApplyModel(quick);
        if (quick.IsEvaluated) return;

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
    }

    private void ApplyModel(ProjectModel model)
    {
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
        RunProject = choice;
        await LoadModelAsync(choice.Path);
        ScheduleSettingsSave();
    }

    // ================================================================ arquivos alterados

    public async Task<bool> PrepareDeleteAsync(string path)
    {
        await Task.Yield();
        return true;
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
            w.Created += (_, e) => Enqueue(WatcherChangeTypes.Created, e.FullPath, null);
            w.Changed += (_, e) => Enqueue(WatcherChangeTypes.Changed, e.FullPath, null);
            w.Deleted += (_, e) => Enqueue(WatcherChangeTypes.Deleted, e.FullPath, null);
            w.Renamed += (_, e) => Enqueue(WatcherChangeTypes.Renamed, e.FullPath, e.OldFullPath);
            w.Error += (_, _) => Enqueue(WatcherChangeTypes.All, folder, null);
            w.EnableRaisingEvents = true;
            _watcher = w;
        }
        catch (Exception ex)
        {
            AppPaths.Log(ex, "Observando a pasta");
        }
    }

    private void Enqueue(WatcherChangeTypes type, string path, string? oldPath)
    {
        _fsEvents.Enqueue((type, path, oldPath));
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _fsTimer.Stop();
            _fsTimer.Start();
        });
    }

    private void ProcessFileSystemEvents()
    {
        var folder = CurrentFolder;
        if (folder == null)
        {
            _fsEvents.Clear();
            return;
        }

        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool reloadProjects = false, reevaluate = false, fullRefresh = false;
        while (_fsEvents.TryDequeue(out var e))
        {
            if (e.Type == WatcherChangeTypes.All)
            {
                fullRefresh = true;
                continue;
            }
            foreach (var (path, deleted) in e.Type == WatcherChangeTypes.Renamed
                         ? new[] { (e.OldPath!, true), (e.Path, false) }
                         : [(e.Path, e.Type == WatcherChangeTypes.Deleted)])
            {
                var name = Path.GetFileName(path);
                if (TextFileIO.IsTempName(name)) continue;
                if (name.Equals("project.assets.json", StringComparison.OrdinalIgnoreCase)) reevaluate = true;
                if (ProjectLocator.IsInsideSkippedDirectory(path, folder)) continue;

                if (e.Type != WatcherChangeTypes.Changed || Directory.Exists(path) == false)
                    dirs.Add(Path.GetDirectoryName(path) ?? folder);
                if (name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || name is "Directory.Build.props" or "global.json")
                {
                    if (e.Type != WatcherChangeTypes.Changed) reloadProjects = true;
                    else reevaluate = true;
                }

                if (deleted) _ls?.OnFileDeleted(path);
                else _ls?.OnFileCreatedOrChanged(path);
            }
        }

        if (fullRefresh) Explorer.Root?.Refresh();
        else Explorer.RefreshPaths(dirs);

        CheckAllExternalChanges();
        if (reloadProjects) _ = LoadProjectsAsync();
        else if (reevaluate && (RunProject ?? Projects.FirstOrDefault()) is { } p) _ = ReevaluateAsync(p.Path);
        ScheduleDiagnostics();
    }

    private async Task ReevaluateAsync(string projectPath)
    {
        try
        {
            var cts = _evaluationCts = new CancellationTokenSource();
            var model = await Task.Run(() => ProjectEvaluator.EvaluateAsync(projectPath, allowRestore: false, cts.Token));
            if (!cts.IsCancellationRequested) ApplyModel(model);
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Compara cada aba com o arquivo no disco (chamado em eventos da pasta e ao ativar a janela).</summary>
    public void CheckAllExternalChanges()
    {
        if (_askingExternalChange) return;
        foreach (var doc in Documents.ToList())
            CheckExternalChange(doc);
    }

    private void CheckExternalChange(DocumentViewModel doc)
    {
        if (doc.FilePath == null) return;
        var stamp = FileStamp.Of(doc.FilePath);
        if (stamp == null)
        {
            if (doc.DiskStamp != null)
            {
                doc.DiskStamp = null;
                doc.ForceDirty();
                NotifyInfo($"\"{doc.Title}\" foi excluído fora do editor. O texto continua aberto; salve para recriá-lo.");
            }
            return;
        }
        if (doc.DiskStamp == stamp) return;

        TextFileContent content;
        try
        {
            content = TextFileIO.Read(doc.FilePath);
        }
        catch
        {
            return; // Provavelmente ainda está sendo gravado; tenta no próximo evento.
        }
        if (content.Text == doc.Document.Text)
        {
            doc.DiskStamp = content.Stamp;
            if (doc.IsDirty) doc.MarkSaved(content.Stamp);
            return;
        }

        if (!doc.IsDirty)
        {
            doc.ReloadFrom(content);
            return;
        }

        _askingExternalChange = true;
        try
        {
            var choice = Dialogs.AskExternalChange(doc.Title);
            if (choice == ExternalChangeChoice.Reload)
            {
                doc.ReloadFrom(content);
                _recovery.Delete(doc.RecoveryId);
            }
            else
            {
                // Mantém o texto do editor; o arquivo só muda quando o usuário salvar.
                doc.DiskStamp = content.Stamp;
            }
        }
        finally
        {
            _askingExternalChange = false;
        }
    }

    // ================================================================ análise

    public void ScheduleDiagnostics()
    {
        _diagnosticsTimer.Stop();
        _diagnosticsTimer.Start();
    }

    private async Task RunDiagnosticsAsync()
    {
        var ls = _ls;
        if (ls == null) return;
        _diagnosticsCts?.Cancel();
        var cts = _diagnosticsCts = new CancellationTokenSource();
        try
        {
            var snapshot = await Task.Run(() => ls.GetDiagnosticsAsync(cts.Token), cts.Token);
            if (cts.IsCancellationRequested) return;
            // Nunca mostra resultado de uma versão anterior do texto.
            foreach (var doc in Documents)
            {
                if (!doc.IsCSharp) continue;
                if (snapshot.DocumentVersions.TryGetValue(doc.LanguageKey, out var v) && v != doc.Version) return;
            }
            Problems.SetLive(snapshot.Diagnostics);
            foreach (var doc in Documents)
                doc.Diagnostics = doc.IsCSharp ? Problems.ForFile(doc.LanguageKey).ToList() : [];
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppPaths.Log(ex, "Análise");
        }
    }

    [RelayCommand]
    private void NavigateToProblem(ProblemItem? item)
    {
        if (item == null || !item.Diagnostic.HasLocation) return;
        var d = item.Diagnostic;
        var doc = FindByKey(d.FilePath!) ?? (File.Exists(d.FilePath) ? OpenFile(d.FilePath!) : null);
        if (doc == null) return;
        ActiveDocument = doc;
        int? offset = !d.FromBuild && d.Start >= 0 ? d.Start : null;
        GoToRequested?.Invoke(doc, d.Line, d.Column, offset);
    }

    // ================================================================ execução

    [RelayCommand]
    private async Task RunOrStop()
    {
        if (IsBusy) StopRun();
        else await RunAsync();
    }

    [RelayCommand]
    private async Task Run()
    {
        if (!IsBusy) await RunAsync();
    }

    [RelayCommand]
    private void Stop() => StopRun();

    public void StopRun()
    {
        switch (RunState)
        {
            case RunState.Preparing:
            case RunState.Building:
                _stopRequested = true;
                _runCts?.Cancel();
                RunState = RunState.Stopping;
                break;
            case RunState.Running:
                _stopRequested = true;
                RunState = RunState.Stopping;
                _session?.Kill();
                break;
        }
    }

    private async Task RunAsync()
    {
        if (IsBusy) return;
        _stopRequested = false;
        RunState = RunState.Preparing;
        StatusText = "Preparando…";
        try
        {
            var project = await ResolveRunTargetAsync();
            if (project == null || _stopRequested)
            {
                StatusText = "";
                return;
            }

            // SDK (considerando global.json e o framework do projeto).
            var file = ProjectFile.Read(project.Path);
            while (true)
            {
                var sdk = await SdkLocator.CheckAsync(file.Directory, SdkLocator.RequiredMajorFor(file.EffectiveTargetFramework));
                SdkMissing = sdk.IsMissingSdk && SdkLocator.RequiredMajorFor(file.EffectiveTargetFramework) == SdkLocator.DefaultMajor;
                if (sdk.CanBuild) break;
                if (sdk.IsMissingSdk)
                {
                    if (!Dialogs.ShowSdkMissing(sdk.Problem!, sdk.Detail)) { StatusText = ""; return; }
                    continue;
                }
                Dialogs.ShowError("Não é possível compilar", sdk.Problem!, sdk.Detail);
                StatusText = "";
                return;
            }

            // Executa sempre a versão salva: salva o que foi alterado neste projeto.
            foreach (var doc in Documents.Where(d => d.IsDirty && d.FilePath != null && FileOperations.IsSameOrInside(d.FilePath, file.Directory)).ToList())
            {
                if (!Save(doc))
                {
                    StatusText = "Execução cancelada: um arquivo não foi salvo.";
                    return;
                }
            }
            if (_stopRequested) return;

            RunState = RunState.Building;
            StatusText = "Compilando…";
            _runCts = new CancellationTokenSource();
            var result = await Task.Run(() => BuildService.BuildAsync(project.Path,
                msg => Application.Current?.Dispatcher.BeginInvoke(() => StatusText = msg), _runCts.Token));

            if (result.Outcome == BuildOutcome.Cancelled || _stopRequested)
            {
                StatusText = "Compilação interrompida.";
                return;
            }

            Problems.SetBuild(result);
            RefreshDocumentDiagnostics();
            if (!result.Success)
            {
                StatusText = result.Outcome == BuildOutcome.RestoreFailed
                    ? "Não foi possível restaurar as dependências."
                    : "A compilação falhou. Veja Problemas.";
                PanelTab = "problems";
                IsPanelOpen = true;
                return;
            }

            var model = _model is { IsEvaluated: true } m && string.Equals(m.ProjectPath, project.Path, StringComparison.OrdinalIgnoreCase)
                ? m
                : await Task.Run(() => ProjectEvaluator.EvaluateAsync(project.Path, allowRestore: false, _runCts.Token));
            var launch = await BuildService.GetLaunchAsync(model, _runCts.Token);
            if (_stopRequested) return;

            PanelTab = "terminal";
            IsPanelOpen = true;
            await Task.Yield();
            var terminal = Terminal ?? throw new InvalidOperationException("Terminal indisponível.");
            var (cols, rows) = terminal.Size;
            var session = PseudoConsoleSession.Start(launch, cols, rows);
            _session = session;
            terminal.Attach(session);
            RunInfo = $"{project.Name} — executando";
            RunState = RunState.Running;
            StatusText = "Executando";
            terminal.FocusTerminal();

            var exitCode = await session.Completion;
            if (_stopRequested)
            {
                terminal.WriteNotice("Execução interrompida.");
                RunInfo = $"{project.Name} — interrompido";
            }
            else if (exitCode == 0)
            {
                terminal.WriteNotice("Programa encerrado (código 0).");
                RunInfo = $"{project.Name} — encerrado";
            }
            else
            {
                terminal.WriteNotice($"O programa encerrou com erro (código {exitCode}).", isError: true);
                RunInfo = $"{project.Name} — encerrou com erro";
            }
            StatusText = "";
        }
        catch (Exception ex)
        {
            AppPaths.Log(ex, "Executando");
            StatusText = "";
            Dialogs.ShowError("Não foi possível executar", ex.Message);
        }
        finally
        {
            var s = _session;
            _session = null;
            s?.Dispose();
            _runCts?.Dispose();
            _runCts = null;
            RunState = RunState.Idle;
        }
    }

    private void RefreshDocumentDiagnostics()
    {
        foreach (var doc in Documents)
            doc.Diagnostics = doc.IsCSharp ? Problems.ForFile(doc.LanguageKey).ToList() : [];
    }

    private async Task<ProjectFile?> ResolveRunTargetAsync()
    {
        if (CurrentFolder != null)
        {
            if (RunProject != null && File.Exists(RunProject.Path)) return RunProject;
            await LoadProjectsAsync();
            var consoles = ProjectLocator.ConsoleProjects(Projects);
            if (consoles.Count == 1) return RunProject = consoles[0];
            if (consoles.Count > 1)
            {
                var choice = Dialogs.SelectProject(consoles, CurrentFolder);
                if (choice == null) return null;
                Settings.ProjectChoices[CurrentFolder] = choice.Path;
                RunProject = choice;
                await LoadModelAsync(choice.Path);
                return choice;
            }
            if (Projects.Count > 0)
            {
                Dialogs.ShowError("Nenhum projeto console", "Esta pasta tem projetos, mas nenhum é um aplicativo console. Esta versão executa apenas projetos console C#.");
                return null;
            }

            var problem = ProjectCreator.CheckFolderForNewProject(CurrentFolder, out var csproj);
            if (problem != null)
            {
                Dialogs.ShowError("Não é possível executar esta pasta", problem);
                return null;
            }
            if (!Dialogs.Confirm("Criar projeto",
                    $"Esta pasta não tem um projeto. Para executar, será criado \"{Path.GetFileName(csproj)}\" aqui, usando os arquivos .cs desta pasta. Nenhum arquivo existente será alterado.",
                    "Criar projeto"))
            {
                return null;
            }
            try
            {
                var created = ProjectCreator.CreateProjectInFolder(CurrentFolder);
                Settings.ProjectChoices[CurrentFolder] = created;
                await LoadProjectsAsync();
                return RunProject;
            }
            catch (Exception ex)
            {
                Dialogs.ShowError("Não foi possível criar o projeto", FileErrors.Describe(ex, CurrentFolder));
                return null;
            }
        }

        var doc = ActiveDocument;
        if (doc == null || !doc.IsCSharp)
        {
            NotifyInfo("Abra ou crie um projeto para executar.");
            return null;
        }

        var request = Dialogs.AskNewProject("Criar projeto para executar",
            "Para executar, este código vira o Program.cs de um projeto console novo.",
            doc.FilePath != null ? ProjectCreator.ToIdentifier(Path.GetFileNameWithoutExtension(doc.FilePath)) : "MeuPrograma",
            DefaultProjectLocation());
        if (request == null) return null;
        try
        {
            var created = ProjectCreator.CreateConsoleProject(request.Location, request.Name, doc.Document.Text);
            Settings.LastProjectLocation = request.Location;
            if (doc.IsUntitled)
            {
                // O conteúdo do rascunho foi transferido para o Program.cs.
                RemoveDocument(doc);
            }
            if (!await OpenFolderAsync(created.Directory, created.ProjectPath, promptForUnsaved: true, partOfRun: true))
            {
                NotifyInfo($"Projeto criado em \"{created.Directory}\".");
                return null;
            }
            OpenFile(created.ProgramPath);
            return RunProject;
        }
        catch (Exception ex)
        {
            Dialogs.ShowError("Não foi possível criar o projeto", FileErrors.Describe(ex, request.Name));
            return null;
        }
    }

    // ================================================================ painéis

    [RelayCommand]
    private void ToggleExplorer() => IsExplorerVisible = !IsExplorerVisible;

    [RelayCommand]
    private void TogglePanel() => IsPanelOpen = !IsPanelOpen;

    [RelayCommand]
    private void ShowPanelTab(string tab)
    {
        if (IsPanelOpen && PanelTab == tab)
        {
            IsPanelOpen = false;
            return;
        }
        PanelTab = tab;
        IsPanelOpen = true;
    }

    [RelayCommand]
    private void ToggleWarnings() => Problems.WarningsExpanded = !Problems.WarningsExpanded;

    // ================================================================ avisos

    public void NotifyInfo(string message) => ShowNotice(message, false);

    public void NotifyError(string message) => ShowNotice(message, true);

    private void ShowNotice(string message, bool error)
    {
        NoticeIsError = error;
        Notice = message;
        _noticeTimer.Stop();
        _noticeTimer.Start();
    }

    [RelayCommand]
    private void DismissNotice() => Notice = null;

    [RelayCommand]
    private void ShowSdkHelp()
    {
        if (Dialogs.ShowSdkMissing(SdkLocator.MissingMessage, null))
            _ = CheckSdkInBackgroundAsync();
    }

    // ================================================================ recuperação e preferências

    private void ScheduleRecovery()
    {
        _recoveryTimer.Stop();
        _recoveryTimer.Start();
    }

    private void WriteRecovery()
    {
        for (int i = 0; i < Documents.Count; i++)
        {
            var doc = Documents[i];
            if (!doc.IsDirty)
            {
                if (_recoveryVersions.Remove(doc.RecoveryId)) _recovery.Delete(doc.RecoveryId);
                continue;
            }
            if (_recoveryVersions.TryGetValue(doc.RecoveryId, out var v) && v == doc.Version) continue;
            var entry = RecoveryStore.Create(doc.RecoveryId, doc.FilePath, doc.IsUntitled ? doc.Title : null, doc.Document.Text, doc.Encoding, i);
            _recoveryVersions[doc.RecoveryId] = doc.Version;
            _ = Task.Run(() => _recovery.Save(entry));
        }
    }

    public void ScheduleSettingsSave()
    {
        _settingsTimer.Stop();
        _settingsTimer.Start();
    }

    public void SaveSettings()
    {
        Settings.FontSize = EditorFontSize;
        Settings.ExplorerVisible = IsExplorerVisible;
        Settings.ExplorerWidth = ExplorerWidth;
        Settings.PanelOpen = IsPanelOpen;
        Settings.PanelHeight = PanelHeight;
        Settings.PanelTab = PanelTab;
        Settings.Session = new SessionState
        {
            Folder = CurrentFolder,
            Files = Documents.Where(d => d.FilePath != null).Select(d => d.FilePath!).ToList(),
            ActiveFile = ActiveDocument?.FilePath,
        };
        SettingsStore.Save(Settings);
    }

    /// <summary>Chamado ao fechar a janela. Retorna false se o usuário cancelar.</summary>
    public bool ConfirmExit() => ConfirmCloseAll(Documents);

    public void Shutdown()
    {
        StopRun();
        _session?.Kill();
        SaveSettings();
        // Saída normal: o que restou foi salvo ou descartado explicitamente.
        foreach (var d in Documents) _recovery.Delete(d.RecoveryId);
        Dispose();
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _diagnosticsCts?.Cancel();
        _evaluationCts?.Cancel();
        _session?.Dispose();
        _ls?.Dispose();
    }
}
