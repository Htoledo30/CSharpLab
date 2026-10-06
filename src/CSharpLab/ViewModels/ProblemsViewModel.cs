using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CSharpLab.Core.Build;
using CSharpLab.Core.Language;

namespace CSharpLab.ViewModels;

public sealed class ProblemItem
{
    public ProblemItem(CodeDiagnostic diagnostic, string fileName)
    {
        Diagnostic = diagnostic;
        FileName = fileName;
    }

    public CodeDiagnostic Diagnostic { get; }
    public string FileName { get; }
    public bool IsError => Diagnostic.Level == DiagnosticLevel.Error;
    public string LevelLabel => IsError ? "Erro" : Diagnostic.Id.StartsWith("DICA", StringComparison.Ordinal) ? "Dica" : "Aviso";
    public string Location => Diagnostic.HasLocation ? $"Linha {Diagnostic.Line}" : "Projeto";

    /// <summary>"Program.cs · Linha 12 — Faltou ";"."</summary>
    public string Display => Diagnostic.HasLocation
        ? $"{FileName} · Linha {Diagnostic.Line} — {Diagnostic.Message}"
        : $"{FileName} — {Diagnostic.Message}";

    public string Details
    {
        get
        {
            var text = $"{Diagnostic.Id}: {Diagnostic.OriginalMessage}";
            if (Diagnostic.HasLocation) text += $"\nLinha {Diagnostic.Line}, coluna {Diagnostic.Column}";
            if (!string.IsNullOrWhiteSpace(Diagnostic.Detail)) text += "\n\n" + Diagnostic.Detail;
            return text;
        }
    }
}

/// <summary>
/// Lista de problemas: análise ao digitar + resultado da última compilação real, sem duplicar.
/// Erros primeiro; avisos num grupo recolhível.
/// </summary>
public sealed partial class ProblemsViewModel : ObservableObject
{
    private IReadOnlyList<CodeDiagnostic> _live = [];
    private List<CodeDiagnostic> _build = [];
    private readonly Func<string?, string> _displayName;

    public ProblemsViewModel(Func<string?, string> displayName) => _displayName = displayName;

    public ObservableCollection<ProblemItem> Errors { get; } = [];
    public ObservableCollection<ProblemItem> Warnings { get; } = [];

    [ObservableProperty]
    public partial bool WarningsExpanded { get; set; }

    public int ErrorCount => Errors.Count;
    public int WarningCount => Warnings.Count;
    public bool IsEmpty => Errors.Count == 0 && Warnings.Count == 0;
    public bool HasWarnings => Warnings.Count > 0;

    public string Summary => (ErrorCount, WarningCount) switch
    {
        (0, 0) => "",
        (_, 0) => ErrorCount.ToString(),
        (0, _) => WarningCount.ToString(),
        _ => $"{ErrorCount} · {WarningCount}",
    };

    public void SetLive(IReadOnlyList<CodeDiagnostic> diagnostics)
    {
        _live = diagnostics;
        Rebuild();
    }

    /// <summary>Resultado de uma compilação real (substitui o anterior).</summary>
    public void SetBuild(BuildResult result)
    {
        _build = result.Diagnostics.Select(d =>
        {
            var message = d.Id == "BUILD"
                ? d.Message
                : DiagnosticTranslator.TranslateBuildMessage(d.Id, d.Message);
            return new CodeDiagnostic(d.Id, d.Severity == BuildSeverity.Error ? DiagnosticLevel.Error : DiagnosticLevel.Warning,
                message, d.Message, d.FilePath != null ? Path.GetFullPath(d.FilePath) : null, d.Line, d.Column, -1, 0, FromBuild: true)
            {
                Detail = d.FilePath == null ? Trim(result.Log) : null,
            };
        }).ToList();

        if (result.Outcome == BuildOutcome.RestoreFailed)
        {
            _build.Insert(0, new CodeDiagnostic("RESTORE", DiagnosticLevel.Error, "Não foi possível restaurar as dependências.",
                "Restore failed", null, 0, 0, -1, 0, FromBuild: true) { Detail = Trim(result.Log) });
        }
        Rebuild();
    }

    public void ClearBuild()
    {
        if (_build.Count == 0) return;
        _build.Clear();
        Rebuild();
    }

    /// <summary>Um arquivo foi editado: os erros da compilação anterior para ele deixam de valer.</summary>
    public void InvalidateBuildFor(string key)
    {
        if (_build.RemoveAll(d => d.FilePath != null && string.Equals(d.FilePath, key, StringComparison.OrdinalIgnoreCase)) > 0)
            Rebuild();
    }

    private static string Trim(string log) => log.Length > 4000 ? log[..4000] + "\n…" : log;

    private void Rebuild()
    {
        var all = new List<CodeDiagnostic>(_live);
        var seen = _live.Select(d => (d.Id, File: d.FilePath?.ToLowerInvariant(), d.Line)).ToHashSet();
        foreach (var d in _build)
        {
            if (seen.Add((d.Id, d.FilePath?.ToLowerInvariant(), d.Line)))
                all.Add(d);
        }

        var ordered = all
            .OrderBy(d => d.FilePath == null ? 0 : 1)
            .ThenBy(d => d.FilePath ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Line)
            .ThenBy(d => d.Column)
            .Select(d => new ProblemItem(d, d.FilePath == null ? "Projeto" : _displayName(d.FilePath)))
            .ToList();

        Replace(Errors, ordered.Where(p => p.IsError));
        Replace(Warnings, ordered.Where(p => !p.IsError));
        OnPropertyChanged(nameof(ErrorCount));
        OnPropertyChanged(nameof(WarningCount));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasWarnings));
        OnPropertyChanged(nameof(Summary));
    }

    private static void Replace(ObservableCollection<ProblemItem> target, IEnumerable<ProblemItem> items)
    {
        var list = items.ToList();
        bool same = list.Count == target.Count &&
                    list.Zip(target).All(p => p.First.Display == p.Second.Display && p.First.Diagnostic.Start == p.Second.Diagnostic.Start);
        if (same) return;
        target.Clear();
        foreach (var i in list) target.Add(i);
    }

    public IEnumerable<CodeDiagnostic> ForFile(string key) =>
        Errors.Concat(Warnings).Select(p => p.Diagnostic)
            .Where(d => d.FilePath != null && string.Equals(d.FilePath, key, StringComparison.OrdinalIgnoreCase));

    public static string FileNameOf(string? path) =>
        path == null ? "Projeto" : path.StartsWith("untitled:", StringComparison.Ordinal) ? "Sem título.cs" : Path.GetFileName(path);
}
