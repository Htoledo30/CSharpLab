using CSharpLab.Core.Projects;
using CSharpLab.Core.Terminal;

namespace CSharpLab.ViewModels;

public enum SaveChoice
{
    Save,
    Discard,
    Cancel,
}

public enum ExternalChangeChoice
{
    Reload,
    KeepMine,
}

public sealed record NewProjectRequest(string Name, string Location);

/// <summary>Diálogos nativos e da aplicação. Implementado pela camada de interface.</summary>
public interface IDialogService
{
    SaveChoice AskSaveChanges(IReadOnlyList<string> fileNames);
    bool Confirm(string title, string message, string confirmText, bool danger = false);
    void ShowError(string title, string message, string? details = null);
    ExternalChangeChoice AskExternalChange(string fileName);
    /// <summary>Retorna true para "verificar novamente".</summary>
    bool ShowSdkMissing(string message, string? details);
    string? PickFolder(string title, string? initialDirectory);
    string? PickFile(string title, string filter, string? initialDirectory);
    string? PickSaveFile(string suggestedName, string? initialDirectory);
    NewProjectRequest? AskNewProject(string title, string? explanation, string defaultName, string defaultLocation);
    ProjectFile? SelectProject(IReadOnlyList<ProjectFile> projects, string folder);
    /// <summary>Pede um texto curto (ex.: novo nome). Null se cancelar.</summary>
    string? AskText(string title, string message, string initial, Func<string, string?>? validate = null);
}

/// <summary>O terminal integrado, visto pelo ViewModel.</summary>
public interface ITerminalHost
{
    (short Columns, short Rows) Size { get; }
    /// <summary>Limpa a tela e passa a mostrar a sessão (entrada, saída e redimensionamento).</summary>
    void Attach(PseudoConsoleSession session);
    /// <summary>Escreve uma linha discreta do editor, separada da saída do programa.</summary>
    void WriteNotice(string text, bool isError = false);
    void Clear();
    void FocusTerminal();
}
