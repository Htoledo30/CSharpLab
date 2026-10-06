namespace CSharpLab.Core.Language;

public enum DiagnosticLevel
{
    Error,
    Warning,
}

/// <summary>Diagnóstico pronto para exibição. Linha e coluna começam em 1.</summary>
public sealed record CodeDiagnostic(
    string Id,
    DiagnosticLevel Level,
    string Message,
    string OriginalMessage,
    string? FilePath,
    int Line,
    int Column,
    int Start,
    int Length,
    bool FromBuild)
{
    public string? Detail { get; init; }
    public bool HasLocation => FilePath != null && Line > 0;
}

/// <summary>Diagnósticos calculados para uma versão específica de cada documento.</summary>
public sealed record DiagnosticsSnapshot(
    IReadOnlyList<CodeDiagnostic> Diagnostics,
    IReadOnlyDictionary<string, int> DocumentVersions);
