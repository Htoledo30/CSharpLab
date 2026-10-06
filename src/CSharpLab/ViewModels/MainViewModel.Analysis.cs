using System.IO;
using CommunityToolkit.Mvvm.Input;
using CSharpLab.Core.Language;
using CSharpLab.Core.Settings;

namespace CSharpLab.ViewModels;

// Análise do código (erros e avisos ao vivo) e navegação entre arquivos.
public sealed partial class MainViewModel
{
    private CancellationTokenSource? _diagnosticsCts;

    public void ScheduleDiagnostics()
    {
        if (_disposed) return;
        _diagnosticsTimer.Stop();
        _diagnosticsTimer.Start();
    }

    private async Task RunDiagnosticsAsync()
    {
        var ls = _ls;
        if (_disposed || ls == null) return;
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

    /// <summary>Aba do documento (pela chave do Roslyn), abrindo o arquivo se necessário.</summary>
    public DocumentViewModel? FindOrOpen(string key, string? path) =>
        FindByKey(key) ?? (path != null && File.Exists(path) ? OpenFile(path, activate: false) : null);

    public void NavigateTo(DocumentViewModel doc, int line, int column, int? offset)
    {
        ActiveDocument = doc;
        GoToRequested?.Invoke(doc, line, column, offset);
    }

    /// <summary>
    /// Aplica um renomear em todas as abas afetadas (abrindo as que faltam). Cada arquivo vira
    /// uma única operação de desfazer; nada é salvo sozinho.
    /// </summary>
    public bool ApplyRename(IReadOnlyList<RenameEdit> edits, IReadOnlyDictionary<string, int> versionsBefore)
    {
        // Algum arquivo mudou enquanto o Roslyn calculava: as posições já não valem.
        foreach (var (key, version) in versionsBefore)
        {
            if (FindByKey(key) is { } open && open.Version != version) return false;
        }
        foreach (var edit in edits)
        {
            var doc = FindOrOpen(edit.DocumentKey, edit.FilePath);
            if (doc == null) continue;
            using (doc.Document.RunUpdate())
            {
                foreach (var change in edit.Changes.OrderByDescending(c => c.Span.Start))
                    doc.Document.Replace(change.Span.Start, change.Span.Length, change.NewText ?? "");
            }
        }
        return true;
    }

    public IReadOnlyDictionary<string, int> DocumentVersions() =>
        Documents.ToDictionary(d => d.LanguageKey, d => d.Version, StringComparer.OrdinalIgnoreCase);

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
}
