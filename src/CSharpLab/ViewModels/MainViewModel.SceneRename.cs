using System.IO;
using CSharpLab.Core.Files;
using CSharpLab.Core.Language;
using CSharpLab.Core.Projects;
using CSharpLab.GameEngine;
using Microsoft.CodeAnalysis.Text;

namespace CSharpLab.ViewModels;

public sealed partial class MainViewModel
{
    /// <summary>A lista, o mapa e a cena aberta acompanham também o desfazer/refazer.</summary>
    public event Action<string, string>? SceneRenamed;

    public void AskRenameScene(string scene)
    {
        if (GameDirectory is not { } directory) return;
        var name = Dialogs.AskText("Renomear cena",
            $"Novo nome para a cena \"{scene}\" (em inglês, sem espaços). Atualiza game.Scene, game.GoTo, game.Start e o arquivo da tela.\n\n" +
            "Os textos visíveis ao jogador continuam como você escreveu. Ctrl+Z desfaz a renomeação inteira; Ctrl+Y refaz. Salve tudo para gravar o código.",
            scene, value => SceneNameProblem(directory, scene, value.Trim()));
        if (name == null || name.Trim() == scene) return;
        if (RenameGameScene(directory, scene, name.Trim()) is { } error) NotifyError(error);
        else NotifyInfo($"Cena \"{scene}\" renomeada para \"{name.Trim()}\". Salve tudo para gravar o código. Ctrl+Z desfaz a renomeação inteira.");
    }

    private string? SceneNameProblem(string directory, string oldName, string newName)
    {
        if (ScreenFile.NameProblem(newName) is { } problem) return char.ToUpperInvariant(problem[0]) + problem[1..];
        if (FileOperations.ValidateName(newName + ".json") is { } fileProblem) return fileProblem;
        if (newName == oldName) return null;
        if (GameScenes().Any(s => !string.Equals(s.Name, oldName, StringComparison.OrdinalIgnoreCase) &&
                                 string.Equals(s.Name, newName, StringComparison.OrdinalIgnoreCase)))
            return $"Já existe uma cena chamada \"{newName}\".";
        var target = GameScreens.PathOf(directory, newName);
        var source = GameScreens.PathOf(directory, oldName);
        if (!string.Equals(source, target, StringComparison.OrdinalIgnoreCase) && (File.Exists(target) || Directory.Exists(target) || FindDocument(target) != null))
            return $"Já existe \"{newName}.json\" na pasta Screens.";
        return null;
    }

    /// <summary>Retorna um problema sem aplicar parte da operação. Usa as alterações ainda não salvas das abas.</summary>
    public string? RenameGameScene(string directory, string oldName, string newName)
    {
        newName = newName.Trim();
        if (IsBusy) return "Espere o jogo ou a exportação terminar para renomear a cena.";
        if (!string.Equals(GameDirectory, Path.GetFullPath(directory), StringComparison.OrdinalIgnoreCase))
            return "Esta cena não pertence ao jogo aberto.";
        if (ScreenFile.NameProblem(oldName) != null)
            return "O nome atual desta cena não pode ser usado como arquivo de tela. Ajuste esse nome diretamente no seu C# antes de usar a renomeação do Estúdio.";
        if (SceneNameProblem(directory, oldName, newName) is { } problem) return problem;
        if (oldName == newName) return null;
        try
        {
            var files = RenameCodeFiles(directory);
            var plan = GameRename.Scene(files, oldName, newName, typeof(Game).Assembly.Location);
            if (plan.UnresolvedFiles.Count > 0)
                return "O editor não conseguiu analisar as chamadas de cena em " + string.Join(", ", plan.UnresolvedFiles.Select(Path.GetFileName)) +
                       ". Corrija os erros dessas chamadas no C# antes de renomear. Nenhum nome foi alterado.";
            if (plan.IndirectFiles.Count > 0)
                return "Há nomes de cena escritos com variáveis, constantes ou expressões em " +
                       string.Join(", ", plan.IndirectFiles.Select(Path.GetFileName)) +
                       ". Confira esses destinos no seu C# antes de renomear; o editor só consegue atualizar com segurança nomes escritos diretamente entre aspas.";
            var changes = OpenRenameDocuments(files, plan.Changes);
            var source = GameScreens.PathOf(directory, oldName);
            var target = GameScreens.PathOf(directory, newName);
            var screen = HasScreen(directory, oldName) ? OpenFile(source, activate: false) : null;
            if (HasScreen(directory, oldName) && screen == null) return "Não foi possível abrir a tela da cena. Nenhum nome foi alterado.";
            if (changes.Count == 0 && screen == null) return $"A cena \"{oldName}\" não foi encontrada no jogo.";
            var docs = changes.Select(c => c.Doc).ToList();
            if (screen != null) docs.Add(screen);
            Func<bool, string?>? pathProblem = screen == null ? null : redo =>
            {
                if (IsBusy) return "Espere o jogo ou a exportação terminar para desfazer ou refazer esta renomeação.";
                var from = redo ? source : target;
                var to = redo ? target : source;
                if (!string.Equals(screen.FilePath, from, StringComparison.OrdinalIgnoreCase))
                    return "O caminho desta tela mudou depois da renomeação. O histórico não vai mover outro arquivo no lugar dela.";
                if (!File.Exists(from)) return $"\"{Path.GetFileName(from)}\" não está mais no disco. A renomeação não foi alterada.";
                if (FileStamp.Of(from) != screen.DiskStamp)
                    return $"\"{Path.GetFileName(from)}\" mudou fora do editor. Confira essa alteração antes de renomear ou desfazer.";
                if (!string.Equals(from, to, StringComparison.OrdinalIgnoreCase) && (File.Exists(to) || Directory.Exists(to) || FindDocument(to) is { } other && other != screen))
                    return $"Já existe \"{Path.GetFileName(to)}\". A renomeação não sobrescreve esse arquivo.";
                return null;
            };
            if (pathProblem?.Invoke(true) is { } pathError) return pathError;
            LinkedDocumentEdit.Apply(docs, () => ApplyTextChanges(changes), Documents.Contains, pathProblem,
                screen == null ? null : redo =>
                {
                    var from = redo ? source : target;
                    var to = redo ? target : source;
                    FileOperations.Rename(from, Path.GetFileName(to));
                    GameScreens.OpenTexts.TryRemove(Path.GetFullPath(from), out _);
                    OnPathRenamed(from, to);
                    SyncScreenText(screen);
                    Explorer.RefreshPaths([Path.GetDirectoryName(to)!]);
                },
                redo =>
                {
                    var from = redo ? oldName : newName;
                    var to = redo ? newName : oldName;
                    if (string.Equals(StudioScene, from, StringComparison.OrdinalIgnoreCase)) StudioScene = to;
                    SceneRenamed?.Invoke(from, to);
                });
            if (ActiveDocument == null || !docs.Contains(ActiveDocument)) ActiveDocument = docs[0];
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return FileErrors.Describe(ex, oldName);
        }
    }

    internal string? RenameScreenPiece(DocumentViewModel screen, string scene, string oldName, string newName, string screenText)
    {
        if (GameDirectoryOf(screen.FilePath) is not { } directory) return "Esta tela não pertence a um jogo aberto.";
        try
        {
            var files = RenameCodeFiles(directory);
            var plan = GameRename.Piece(files, scene, oldName, newName, typeof(Game).Assembly.Location);
            if (plan.IndirectFiles.Count > 0 || plan.UnresolvedFiles.Count > 0)
                return "Há game.Find com nomes indiretos, fora do bloco da cena, ou chamadas com erro. Confira esses nomes no C# antes de renomear a peça; o editor atualiza nomes entre aspas dentro da cena que usa esta tela.";
            var changes = OpenRenameDocuments(files, plan.Changes);
            LinkedDocumentEdit.Apply([screen, .. changes.Select(c => c.Doc)], () =>
            {
                screen.Document.Replace(0, screen.Document.TextLength, screenText);
                ApplyTextChanges(changes);
            }, Documents.Contains);
            NotifyInfo($"Peça \"{oldName}\" renomeada para \"{newName}\" na tela e no código desta cena. Ctrl+Z desfaz tudo junto; salve tudo para gravar.");
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return FileErrors.Describe(ex, scene);
        }
    }

    // Ao renomear não usamos o limite de 300 arquivos da visualização, nem ignoramos falhas de leitura.
    private Dictionary<string, string> RenameCodeFiles(string directory) => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
        .Concat(Documents.Where(d => d.IsCSharp && d.FilePath != null).Select(d => d.FilePath!))
        .Where(f => !ProjectLocator.IsInsideSkippedDirectory(f, directory) && string.Equals(GameDirectoryOf(f), directory, StringComparison.OrdinalIgnoreCase))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToDictionary(f => f, f => FindDocument(f)?.Document.Text ?? TextFileIO.Read(f).Text, StringComparer.OrdinalIgnoreCase);

    private List<(DocumentViewModel Doc, IReadOnlyList<TextChange> Changes)> OpenRenameDocuments(
        IReadOnlyDictionary<string, string> files, IReadOnlyDictionary<string, IReadOnlyList<TextChange>> edits)
    {
        var result = new List<(DocumentViewModel, IReadOnlyList<TextChange>)>();
        foreach (var (path, changes) in edits)
        {
            var doc = OpenFile(path, activate: false);
            if (doc == null || doc.Document.Text != files[path])
                throw new UserFacingException($"Não foi possível preparar \"{Path.GetFileName(path)}\". Nenhum nome foi alterado; tente novamente.");
            if (FileStamp.Of(path) != doc.DiskStamp)
                throw new UserFacingException($"\"{Path.GetFileName(path)}\" mudou fora do editor. Confira essa alteração antes de renomear.");
            if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0)
                throw new UserFacingException($"\"{Path.GetFileName(path)}\" está somente para leitura. Libere a gravação desse arquivo antes de renomear.");
            result.Add((doc, changes));
        }
        return result;
    }

    private static void ApplyTextChanges(IEnumerable<(DocumentViewModel Doc, IReadOnlyList<TextChange> Changes)> edits)
    {
        foreach (var (doc, changes) in edits)
            using (doc.Document.RunUpdate())
                foreach (var change in changes.OrderByDescending(c => c.Span.Start))
                    doc.Document.Replace(change.Span.Start, change.Span.Length, change.NewText ?? "");
    }
}
