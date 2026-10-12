using System.IO;
using CommunityToolkit.Mvvm.Input;
using CSharpLab.Core.Build;
using CSharpLab.Core.Files;
using CSharpLab.Core.Language;
using CSharpLab.Core.Projects;
using CSharpLab.Core.Settings;

namespace CSharpLab.ViewModels;

// Registro da sessão (sessao.jsonl): o que aconteceu, para quem acompanha de fora. Ver SessionLog.
public sealed partial class MainViewModel
{
    /// <summary>Quantos erros entram em cada linha do registro (o resto vira só a contagem).</summary>
    private const int LoggedErrorsLimit = 15;

    private static object DescribeDiagnostic(CodeDiagnostic d) => new
    {
        file = d.FilePath != null ? Path.GetFileName(d.FilePath) : null,
        line = d.HasLocation ? d.Line : (int?)null,
        id = d.Id,
        message = d.Message,
        original = d.OriginalMessage != d.Message ? d.OriginalMessage : null,
    };

    private static object DescribeProject(ProjectFile project) => new
    {
        name = project.Name,
        kind = project.IsWindowApp ? "game" : "console",
        folder = project.Directory,
    };

    private static void LogFolderOpened(string folder) =>
        SessionLog.Write("folder-opened", new { folder });

    /// <summary>Arquivo salvo, com os erros que o editor mostra nele agora.</summary>
    private void LogSaved(DocumentViewModel doc, string path)
    {
        var found = doc.IsCSharp ? Problems.ForFile(doc.LanguageKey).ToList() : [];
        var errors = found.Where(d => d.Level == DiagnosticLevel.Error).ToList();
        SessionLog.Write("saved", new
        {
            file = path,
            errorCount = errors.Count,
            warningCount = found.Count - errors.Count,
            errors = errors.Take(LoggedErrorsLimit).Select(DescribeDiagnostic),
        });
    }

    private static void LogBuild(ProjectFile project, BuildResult result)
    {
        var errors = result.Diagnostics.Where(d => d.Severity == BuildSeverity.Error).ToList();
        SessionLog.Write("build", new
        {
            project = DescribeProject(project),
            outcome = result.Outcome.ToString(),
            seconds = Math.Round(result.Elapsed.TotalSeconds, 1),
            errorCount = errors.Count,
            warningCount = result.Diagnostics.Count - errors.Count,
            errors = errors.Take(LoggedErrorsLimit).Select(d => new
            {
                file = d.FilePath != null ? Path.GetFileName(d.FilePath) : null,
                line = d.Line > 0 ? d.Line : (int?)null,
                id = d.Id,
                message = d.Id == "BUILD" ? d.Message : DiagnosticTranslator.TranslateBuildMessage(d.Id, d.Message),
                original = d.Message,
            }),
        });
    }

    private void LogRunStart(ProjectFile project) =>
        SessionLog.Write("run-start", new
        {
            project = DescribeProject(project),
            reusedBuild = LastRunReusedBuild,
            startScene = _playFrom,
        });

    private void LogRunEnd(ProjectFile project, int exitCode, bool crashed) =>
        SessionLog.Write("run-end", new
        {
            project = DescribeProject(project),
            exitCode,
            stoppedByUser = _stopRequested,
            crashed,
        });

    /// <summary>
    /// O programa parou com uma exceção. inEngine = o erro nasceu dentro do motor dos jogos
    /// (CSharpLab.GameEngine). Com type GameException é o motor avisando de um uso errado (erro de
    /// quem programa); com outro tipo pode ser um problema no próprio motor.
    /// </summary>
    private static void LogCrash(ProjectFile project, RuntimeCrash crash, RuntimeFrame? userFrame, RuntimeExplanation explanation) =>
        SessionLog.Write("crash", new
        {
            project = DescribeProject(project),
            type = crash.Type,
            message = crash.Message,
            explanation = explanation.Message,
            tip = explanation.Tip,
            file = userFrame != null ? Path.GetFileName(userFrame.File) : null,
            line = userFrame?.Line,
            inEngine = crash.Methods.FirstOrDefault()?.StartsWith("CSharpLab.GameEngine.", StringComparison.Ordinal) == true,
            methods = crash.Methods.Take(8),
            frames = crash.Frames.Take(8).Select(f => $"{f.Method} — {Path.GetFileName(f.File)}:{f.Line}"),
        });

    /// <summary>
    /// "Terminei": salva o que mudou na pasta aberta e registra um pedido de revisão com o estado
    /// atual (arquivo aberto, erros no painel Problemas). Quem acompanha o registro é avisado.
    /// </summary>
    [RelayCommand]
    private void RequestReview()
    {
        if (CurrentFolder != null)
        {
            foreach (var doc in Documents.Where(d => d.IsDirty && d.FilePath != null && FileOperations.IsSameOrInside(d.FilePath, CurrentFolder)).ToList())
            {
                if (!Save(doc))
                {
                    NotifyError("Pedido de revisão cancelado: um arquivo não foi salvo.");
                    return;
                }
            }
        }

        var errors = Problems.Errors.Select(p => p.Diagnostic).ToList();
        SessionLog.Write("review-request", new
        {
            folder = CurrentFolder,
            project = RunProject != null ? DescribeProject(RunProject) : null,
            activeFile = ActiveDocument?.FilePath,
            unsavedOutside = Documents.Count(d => d.IsDirty),
            errorCount = errors.Count,
            warningCount = Problems.WarningCount,
            errors = errors.Take(LoggedErrorsLimit).Select(DescribeDiagnostic),
        });
        NotifyInfo("Pedido de revisão anotado. Se o Claude estiver acompanhando, ele já foi avisado.");
    }
}
