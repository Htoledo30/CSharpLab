using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CSharpLab.Core.Language;
using CSharpLab.Core.Projects;
using CSharpLab.Core.Settings;
using CSharpLab.Core.Updates;

namespace CSharpLab.ViewModels;

public enum RunState
{
    Idle,
    Preparing,
    Building,
    Running,
    Stopping,
}

/// <summary>
/// Estado e comandos da janela principal. Dividido por assunto em arquivos parciais:
/// Documents (abas e salvar), Folders (pasta e projetos), FileSystem (mudanças no disco),
/// Analysis (erros ao vivo e navegação), Run (compilar e executar) e Updates (atualização).
/// Aqui ficam o estado compartilhado, a inicialização, os painéis, os avisos e o encerramento.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly Task<LanguageService> _languageTask;
    private LanguageService? _ls;
    private ProjectModel? _model;
    private bool _disposed;
    private readonly DispatcherTimer _diagnosticsTimer;
    private readonly DispatcherTimer _recoveryTimer;
    private readonly DispatcherTimer _noticeTimer;
    private readonly DispatcherTimer _fsTimer;
    private readonly DispatcherTimer _settingsTimer;

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
        IsPanelOpen = settings.PanelOpen;
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
    [NotifyPropertyChangedFor(nameof(IsBusy), nameof(IsRunning), nameof(RunButtonText), nameof(RunButtonToolTip),
        nameof(RunTargetName), nameof(ShowTerminalHint))]
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

    /// <summary>Nome do projeto que o Executar vai rodar, mostrado no próprio botão.</summary>
    public string? RunTargetName => IsBusy ? null : RunProject?.Name;

    public string RunButtonToolTip =>
        IsBusy ? "Parar a execução (Shift+F5)"
        : RunProject != null ? $"Salvar, compilar e executar o projeto \"{RunProject.Name}\" (F5)"
        : CurrentFolder == null ? "Executar este código. Na primeira vez, ele vira um projeto. (F5)"
        : HasMultipleProjects ? "Escolher qual projeto executar (F5)"
        : "Salvar os arquivos do projeto, compilar e executar (F5)";

    /// <summary>Dica discreta no terminal nas primeiras execuções: a saída aparece ali e dá para digitar.</summary>
    public bool ShowTerminalHint => IsRunning && PanelTab == "terminal" && Settings.TerminalHintRuns < 3;

    public string WindowTitle => CurrentFolder != null ? $"{Path.GetFileName(CurrentFolder.TrimEnd('\\'))} — CSharp Lab" : "CSharp Lab";

    public string ProjectLabel => RunProject?.Name ?? (CurrentFolder != null ? Path.GetFileName(CurrentFolder.TrimEnd('\\')) : "Sem projeto");

    public bool HasMultipleProjects => ProjectLocator.RunnableProjects(Projects).Count > 1;

    partial void OnCurrentFolderChanged(string? value)
    {
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(ProjectLabel));
        OnPropertyChanged(nameof(RunButtonToolTip));
    }

    partial void OnRunProjectChanged(ProjectFile? value)
    {
        OnPropertyChanged(nameof(ProjectLabel));
        OnPropertyChanged(nameof(RunTargetName));
        OnPropertyChanged(nameof(RunButtonToolTip));
    }

    partial void OnProjectsChanged(IReadOnlyList<ProjectFile> value)
    {
        OnPropertyChanged(nameof(HasMultipleProjects));
        OnPropertyChanged(nameof(RunButtonToolTip));
    }

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
    partial void OnPanelTabChanged(string value)
    {
        OnPropertyChanged(nameof(ShowTerminalHint));
        ScheduleSettingsSave();
    }

    // ================================================================ inicialização

    public async Task InitializeAsync()
    {
        if (_disposed) return;
        var session = Settings.Session;
        if (session?.Folder != null)
        {
            // A pasta abre na hora; o projeto termina de carregar em segundo plano, sem segurar as abas.
            if (Directory.Exists(session.Folder))
                await OpenFolderAsync(session.Folder, null, promptForUnsaved: false, waitForProject: false);
            else
                NotifyInfo($"A pasta \"{session.Folder}\" não existe mais.");
        }

        if (_disposed) return;
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
            var language = await _languageTask;
            if (_disposed) return;
            _ls = language;
            _ls.ProjectChanged += OnLanguageProjectChanged;
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

        if (_disposed) return;
        _ = CheckSdkInBackgroundAsync();
        AnnounceVersionChange();
        if (Settings.CheckForUpdates) _ = CheckForUpdatesAsync(manual: false);
    }

    // ================================================================ painéis

    [RelayCommand]
    private void ToggleExplorer() => IsExplorerVisible = !IsExplorerVisible;

    [RelayCommand]
    private void TogglePanel() => IsPanelOpen = !IsPanelOpen;

    /// <summary>Painel inferior ocupando quase toda a altura (bom para jogos no terminal). Não é salvo.</summary>
    [ObservableProperty]
    public partial bool IsPanelMaximized { get; set; }

    partial void OnIsPanelOpenChanged(bool oldValue, bool newValue)
    {
        if (!newValue) IsPanelMaximized = false;
    }

    [RelayCommand]
    private void TogglePanelMaximized()
    {
        IsPanelMaximized = !IsPanelMaximized;
        if (IsPanelMaximized) IsPanelOpen = true;
    }

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
        if (_disposed) return;
        NoticeIsError = error;
        Notice = message;
        _noticeTimer.Stop();
        _noticeTimer.Start();
    }

    [RelayCommand]
    private void DismissNotice() => Notice = null;

    // ================================================================ preferências e encerramento

    public void ScheduleSettingsSave()
    {
        if (_disposed) return;
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
        _recovery.Flush(TimeSpan.FromSeconds(3));
        Dispose();

        // Versão nova já baixada: troca os arquivos assim que este processo terminar.
        if (_stagedUpdate is { } staged)
        {
            try
            {
                UpdateService.LaunchApplier(staged.AppDir, staged.Version, _relaunchAfterUpdate);
            }
            catch (Exception ex)
            {
                AppPaths.Log(ex, "Iniciando a atualização");
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _projectLoadVersion++;
        _stopRequested = true;
        _runCts?.Cancel();
        _updatesCts.Cancel();
        _diagnosticsTimer.Stop();
        _recoveryTimer.Stop();
        _noticeTimer.Stop();
        _fsTimer.Stop();
        _settingsTimer.Stop();
        _watcher?.Dispose();
        _fsEvents.Clear();
        _diagnosticsCts?.Cancel();
        _evaluationCts?.Cancel();
        _projectScanCts?.Cancel();
        _session?.Dispose();
        foreach (var doc in Documents)
        {
            doc.TextChanged -= OnDocumentTextChanged;
            doc.PropertyChanged -= OnDocumentPropertyChanged;
        }
        if (_ls != null)
        {
            _ls.ProjectChanged -= OnLanguageProjectChanged;
            _ls.Dispose();
        }
        else _ = DisposeLanguageWhenReadyAsync();
    }

    private void OnLanguageProjectChanged() => Application.Current?.Dispatcher.BeginInvoke(ScheduleDiagnostics);

    private async Task DisposeLanguageWhenReadyAsync()
    {
        try { (await _languageTask.ConfigureAwait(false)).Dispose(); }
        catch (Exception ex) { AppPaths.Log(ex, "Encerrando serviços de linguagem"); }
    }
}
