using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using CSharpLab.Core.Build;
using CSharpLab.Core.Files;
using CSharpLab.Core.Language;
using CSharpLab.Core.Projects;
using CSharpLab.Core.Settings;
using CSharpLab.Core.Terminal;

namespace CSharpLab.ViewModels;

// Executar: conferir o SDK, salvar, compilar (ou reaproveitar a compilação) e rodar no terminal.
public sealed partial class MainViewModel
{
    private CancellationTokenSource? _runCts;
    private PseudoConsoleSession? _session;
    private bool _stopRequested;
    private readonly RunCache _runCache = new();

    /// <summary>A última execução reaproveitou a compilação anterior (nada tinha mudado).</summary>
    public bool LastRunReusedBuild { get; private set; }

    /// <summary>
    /// Síncrono de propósito: um comando assíncrono ficaria desabilitado enquanto a execução
    /// estivesse em andamento, e o mesmo botão precisa servir para Parar.
    /// </summary>
    [RelayCommand]
    private void RunOrStop()
    {
        if (IsBusy) StopRun();
        else _ = RunAsync();
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
        if (_disposed || IsBusy) return;
        _stopRequested = false;
        var runCts = _runCts = new CancellationTokenSource();
        var ct = runCts.Token;
        RunState = RunState.Preparing;
        StatusText = "Preparando…";
        try
        {
            // O erro de execução anterior deixa de valer.
            Problems.SetRuntime(null);
            RefreshDocumentDiagnostics();
            var project = await ResolveRunTargetAsync(ct);
            ct.ThrowIfCancellationRequested();
            if (project == null || _stopRequested || !await EnsureSdkAsync(project, ct) || !SaveProjectFiles(project))
            {
                if (StatusText == "Preparando…") StatusText = "";
                return;
            }
            if (_stopRequested) return;

            RunState = RunState.Building;
            var launch = await BuildOrReuseAsync(project, ct);
            if (launch == null) return;
            ct.ThrowIfCancellationRequested();
            await RunInTerminalAsync(project, launch, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (!_disposed) StatusText = "Execução interrompida.";
        }
        catch (Exception ex)
        {
            AppPaths.Log(ex, "Executando");
            StatusText = "";
            if (!_disposed) Dialogs.ShowError("Não foi possível executar", ex.Message);
        }
        finally
        {
            var s = _session;
            _session = null;
            s?.Dispose();
            runCts.Dispose();
            _runCts = null;
            RunState = RunState.Idle;
        }
    }

    /// <summary>
    /// SDK certo para o projeto (considerando global.json e o framework). Uma verificação que deu
    /// certo vale para a sessão inteira. Retorna false se não dá para compilar.
    /// </summary>
    private async Task<bool> EnsureSdkAsync(ProjectFile project, CancellationToken ct)
    {
        var requiredMajor = SdkLocator.RequiredMajorFor(project.EffectiveTargetFramework);
        while (!_runCache.IsSdkVerified(project.Directory, requiredMajor))
        {
            var sdk = await SdkLocator.CheckAsync(project.Directory, requiredMajor, ct);
            ct.ThrowIfCancellationRequested();
            SdkMissing = sdk.IsMissingSdk && requiredMajor == SdkLocator.DefaultMajor;
            if (sdk.CanBuild)
            {
                _runCache.MarkSdkVerified(project.Directory, requiredMajor);
                return true;
            }
            if (sdk.IsMissingSdk)
            {
                // O usuário pode instalar o SDK e pedir para tentar de novo.
                if (!Dialogs.ShowSdkMissing(sdk.Problem!, sdk.Detail)) return false;
                continue;
            }
            Dialogs.ShowError("Não é possível compilar", sdk.Problem!, sdk.Detail);
            return false;
        }
        return true;
    }

    /// <summary>Executa sempre a versão salva: salva o que foi alterado dentro do projeto.</summary>
    private bool SaveProjectFiles(ProjectFile project)
    {
        foreach (var doc in Documents.Where(d => d.IsDirty && d.FilePath != null && FileOperations.IsSameOrInside(d.FilePath, project.Directory)).ToList())
        {
            if (!Save(doc))
            {
                StatusText = "Execução cancelada: um arquivo não foi salvo.";
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Compila e devolve como iniciar o programa. Se nada mudou desde a última compilação
    /// bem-sucedida, reaproveita o mesmo programa. Retorna null se a compilação falhou ou parou.
    /// </summary>
    private async Task<ProcessLaunch?> BuildOrReuseAsync(ProjectFile project, CancellationToken ct)
    {
        // Jogo com botões: se o CSharp Lab trouxe um motor mais novo, o jogo passa a usá-lo.
        if (project.IsWindowApp) await Task.Run(() => GameKit.RefreshLibrary(project.Directory), ct);
        var fingerprint = await Task.Run(() => BuildService.InputFingerprint(project.Path), ct);
        if (_runCache.TryReuse(project.Path, fingerprint) is { } reused)
        {
            LastRunReusedBuild = true;
            return reused;
        }

        LastRunReusedBuild = false;
        StatusText = "Compilando…";
        var result = await Task.Run(() => BuildService.BuildAsync(project.Path,
            msg => Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (!_disposed && !ct.IsCancellationRequested) StatusText = msg;
            }), ct));

        if (result.Outcome == BuildOutcome.Cancelled || _stopRequested)
        {
            StatusText = "Compilação interrompida.";
            return null;
        }

        Problems.SetBuild(result);
        RefreshDocumentDiagnostics();
        if (!result.Success)
        {
            _runCache.ForgetBuild();
            StatusText = result.Outcome == BuildOutcome.RestoreFailed
                ? "Não foi possível restaurar as dependências."
                : "A compilação falhou. Veja Problemas.";
            PanelTab = "problems";
            IsPanelOpen = true;
            return null;
        }

        var model = _model is { IsEvaluated: true } m && string.Equals(m.ProjectPath, project.Path, StringComparison.OrdinalIgnoreCase)
            ? m
            : await Task.Run(() => ProjectEvaluator.EvaluateAsync(project.Path, allowRestore: false, ct));
        var launch = await BuildService.GetLaunchAsync(model, ct);
        _runCache.RememberBuild(project.Path, fingerprint, launch);
        return launch;
    }

    private async Task RunInTerminalAsync(ProjectFile project, ProcessLaunch launch, CancellationToken ct)
    {
        PanelTab = "terminal";
        IsPanelOpen = true;
        await Task.Yield(); // deixa o terminal aparecer e medir o tamanho
        ct.ThrowIfCancellationRequested();
        var terminal = Terminal ?? throw new InvalidOperationException("Terminal indisponível.");
        var (cols, rows) = terminal.Size;
        var crashReport = WithCrashReport(project, ref launch);
        var session = PseudoConsoleSession.Start(launch, cols, rows);
        _session = session;
        terminal.Attach(session);
        RunInfo = $"{project.Name} — executando";
        RunState = RunState.Running;
        StatusText = "Executando";
        if (project.IsWindowApp)
            terminal.WriteNotice("O jogo abre numa janela própria. Para encerrar, feche a janela ou clique em Parar.");
        else
            terminal.FocusTerminal();

        var exitCode = await session.Completion;
        if (Settings.TerminalHintRuns < 3)
        {
            Settings.TerminalHintRuns++;
            ScheduleSettingsSave();
        }
        if (_stopRequested)
        {
            terminal.WriteNotice("Execução interrompida.");
            RunInfo = $"{project.Name} — interrompido";
        }
        else if (exitCode == 0)
        {
            terminal.WriteNotice(project.IsWindowApp ? "Jogo fechado." : "Programa encerrado (código 0).");
            RunInfo = $"{project.Name} — encerrado";
        }
        else if (!ExplainCrash(project, crashReport, exitCode, terminal))
        {
            terminal.WriteNotice($"O programa encerrou com erro (código {exitCode}).", isError: true);
            RunInfo = $"{project.Name} — encerrou com erro";
        }
        if (crashReport != null) TryDelete(crashReport);
        StatusText = "";
    }

    /// <summary>
    /// Liga o gancho que registra onde o programa parou com um erro (DOTNET_STARTUP_HOOKS).
    /// Programas anteriores ao .NET 8 rodam sem ele. Retorna o arquivo do relatório.
    /// </summary>
    private static string? WithCrashReport(ProjectFile project, ref ProcessLaunch launch)
    {
        var hook = Path.Combine(AppContext.BaseDirectory, RuntimeErrors.HookFileName);
        if (!File.Exists(hook) || SdkLocator.RequiredMajorFor(project.EffectiveTargetFramework) < RuntimeErrors.MinimumMajor) return null;
        var report = Path.Combine(Path.GetTempPath(), "csharplab-erro-" + Guid.NewGuid().ToString("N")[..12] + ".txt");
        var env = new Dictionary<string, string?>(launch.Environment ?? new Dictionary<string, string?>(), StringComparer.OrdinalIgnoreCase);
        var existing = env.TryGetValue("DOTNET_STARTUP_HOOKS", out var v) ? v : Environment.GetEnvironmentVariable("DOTNET_STARTUP_HOOKS");
        env["DOTNET_STARTUP_HOOKS"] = string.IsNullOrEmpty(existing) ? hook : hook + Path.PathSeparator + existing;
        env[RuntimeErrors.ReportVariable] = report;
        launch = launch with { Environment = env };
        return report;
    }

    /// <summary>Explica em português o erro que parou o programa e marca a linha. False se não há o que explicar.</summary>
    private bool ExplainCrash(ProjectFile project, string? reportPath, int exitCode, ITerminalHost terminal)
    {
        if (RuntimeErrors.ExplainExitCode(exitCode) is { } exitText)
        {
            terminal.WriteNotice(exitText, isError: true);
            RunInfo = $"{project.Name} — parou com erro";
            return true;
        }
        if (reportPath == null || RuntimeErrors.Read(reportPath) is not { } crash) return false;

        var frame = RuntimeErrors.UserFrame(crash, project.Directory);
        string? sourceLine = null;
        if (frame != null)
        {
            try { sourceLine = File.ReadLines(frame.File).Skip(frame.Line - 1).FirstOrDefault(); }
            catch (IOException) { }
        }
        var explanation = RuntimeErrors.Explain(crash, sourceLine);
        var where = frame != null ? $"Erro na linha {frame.Line} ({Path.GetFileName(frame.File)}): " : "Erro: ";
        terminal.WriteNotice(where + explanation.Message, isError: true);
        if (explanation.Tip != null) terminal.WriteNotice("Dica: " + explanation.Tip);
        RunInfo = frame != null ? $"{project.Name} — parou com erro na linha {frame.Line}" : $"{project.Name} — parou com erro";

        var stack = string.Join("\n", crash.Frames.Select(f => $"  em {f.Method} — {Path.GetFileName(f.File)}, linha {f.Line}"));
        Problems.SetRuntime(new CodeDiagnostic(RuntimeErrors.DiagnosticId, DiagnosticLevel.Error, "Ao executar: " + explanation.Message,
            $"{crash.Type}: {crash.Message}", frame != null ? Path.GetFullPath(frame.File) : null, frame?.Line ?? 0, Math.Max(1, frame?.Column ?? 1),
            -1, 0, FromBuild: true)
        {
            Detail = (explanation.Tip != null ? "Dica: " + explanation.Tip + "\n\n" : "") + stack,
        });
        RefreshDocumentDiagnostics();
        return true;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private void RefreshDocumentDiagnostics()
    {
        foreach (var doc in Documents)
            doc.Diagnostics = doc.IsCSharp ? Problems.ForFile(doc.LanguageKey).ToList() : [];
    }

    private async Task<ProjectFile?> ResolveRunTargetAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (CurrentFolder != null)
        {
            if (RunProject != null && File.Exists(RunProject.Path)) return RunProject;
            await LoadProjectsAsync(ct: ct);
            ct.ThrowIfCancellationRequested();
            var runnable = ProjectLocator.RunnableProjects(Projects);
            if (runnable.Count == 1) return RunProject = runnable[0];
            if (runnable.Count > 1)
            {
                var choice = Dialogs.SelectProject(runnable, CurrentFolder);
                if (choice == null) return null;
                Settings.ProjectChoices[CurrentFolder] = choice.Path;
                RunProject = choice;
                await LoadModelAsync(choice.Path, ct: ct);
                ct.ThrowIfCancellationRequested();
                return choice;
            }
            if (Projects.Count > 0)
            {
                Dialogs.ShowError("Nenhum projeto para executar", "Esta pasta tem projetos, mas nenhum é um programa console ou um jogo com botões, que são os tipos que o CSharp Lab executa.");
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
                await LoadProjectsAsync(ct: ct);
                ct.ThrowIfCancellationRequested();
                return RunProject;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
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
            if (!await OpenFolderAsync(created.Directory, created.ProjectPath, promptForUnsaved: true, partOfRun: true, ct: ct))
            {
                NotifyInfo($"Projeto criado em \"{created.Directory}\".");
                return null;
            }
            OpenFile(created.ProgramPath);
            return RunProject;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            Dialogs.ShowError("Não foi possível criar o projeto", FileErrors.Describe(ex, request.Name));
            return null;
        }
    }

    private async Task CheckSdkInBackgroundAsync()
    {
        try
        {
            var status = await SdkLocator.CheckAsync(null, ct: _updatesCts.Token);
            if (!_disposed) SdkMissing = status.IsMissingSdk;
        }
        catch (OperationCanceledException) when (_disposed) { }
        catch (Exception ex)
        {
            AppPaths.Log(ex, "Verificando SDK");
        }
    }

    [RelayCommand]
    private void ShowSdkHelp()
    {
        if (Dialogs.ShowSdkMissing(SdkLocator.MissingMessage, null))
        {
            _runCache.Clear();
            _ = CheckSdkInBackgroundAsync();
        }
    }
}
