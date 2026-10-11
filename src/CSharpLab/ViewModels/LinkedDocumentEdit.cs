using ICSharpCode.AvalonEdit.Document;

namespace CSharpLab.ViewModels;

/// <summary>
/// Uma ação em vários arquivos, com os históricos anteriores de cada aba preservados.
/// Marcadores nas pilhas nativas fazem as outras abas voltarem ao mesmo passo, sem substituir suas pilhas.
/// </summary>
internal sealed class LinkedDocumentEdit
{
    private sealed record Member(DocumentViewModel Doc, UndoStack Stack, long Before)
    {
        public long After { get; set; }
        public int BeforeVersion { get; } = Doc.Version;
        public bool TextChanged { get; set; }
        public bool DiskApplied { get; set; }
    }

    private readonly Member[] _members;
    private readonly Func<DocumentViewModel, bool> _isOpen;
    private readonly Func<bool, string?>? _problem;
    private readonly Action<bool>? _move;
    private readonly Action<bool>? _completed;
    private bool _applied = true;
    private bool _playing;
    private bool _prepared;
    private DocumentViewModel? _origin;
    private bool _redoInvalidated;
    private readonly HashSet<DocumentViewModel> _replacedRedo = [];
    private bool _diskApplied;
    private static long _nextSequence;
    internal long Sequence { get; } = Interlocked.Increment(ref _nextSequence);

    private LinkedDocumentEdit(IReadOnlyList<DocumentViewModel> docs, Func<DocumentViewModel, bool> isOpen,
        Func<bool, string?>? problem, Action<bool>? move, Action<bool>? completed)
    {
        _members = docs.Distinct().Select(d => new Member(d, d.Document.UndoStack, d.HistoryRevision)).ToArray();
        _isOpen = isOpen;
        _problem = problem;
        _move = move;
        _completed = completed;
    }

    internal static void Apply(IReadOnlyList<DocumentViewModel> docs, Action change,
        Func<DocumentViewModel, bool> isOpen, Func<bool, string?>? problem = null,
        Action<bool>? move = null, Action<bool>? completed = null)
    {
        if (docs.Count == 0) return;
        var edit = new LinkedDocumentEdit(docs, isOpen, problem, move, completed);
        move?.Invoke(true);
        foreach (var member in edit._members)
        {
            member.Stack.StartUndoGroup();
            member.Stack.Push(new Marker(edit, member.Doc, beginning: true));
        }
        try
        {
            change();
            foreach (var member in edit._members)
            {
                if (member.Doc.HistoryRevision == member.Before) member.Doc.AdvanceHistory();
                member.Stack.Push(new Marker(edit, member.Doc, beginning: false));
            }
        }
        finally
        {
            foreach (var member in edit._members) member.Stack.EndUndoGroup();
        }
        foreach (var member in edit._members)
        {
            member.After = member.Doc.HistoryRevision;
            member.TextChanged = member.Doc.Version != member.BeforeVersion;
            member.Doc.LinkedEdits.Add(edit);
        }
        completed?.Invoke(true);
    }

    internal bool IsNext(DocumentViewModel doc, bool redo) =>
        !(redo && _replacedRedo.Contains(doc)) && _applied != redo &&
        _members.Any(m => m.Doc == doc && doc.HistoryRevision == (redo ? m.Before : m.After));

    internal string? Problem(bool redo)
    {
        if (redo && _redoInvalidated)
            return "Um dos arquivos recebeu uma nova edição depois do desfazer. Não é possível refazer essa renomeação sem perder esse novo histórico.";
        foreach (var member in _members)
        {
            if (!_isOpen(member.Doc))
                return "Esta renomeação envolve um arquivo que foi fechado. O histórico das abas fechadas não está mais disponível.";
            if (member.Doc.HistoryRevision != (redo ? member.Before : member.After))
                return $"\"{member.Doc.Title}\" tem alterações posteriores à renomeação. Desfaça essas alterações nesse arquivo primeiro; depois tente novamente.";
        }
        return _prepared ? null : _problem?.Invoke(redo);
    }

    internal void OnNewEdit(DocumentViewModel doc)
    {
        if (!_applied && !_playing) { _redoInvalidated = true; _replacedRedo.Add(doc); }
    }

    internal void OnSaved()
    {
        var textMembers = _members.Where(m => m.TextChanged).ToArray();
        foreach (var member in textMembers.Where(m => !m.Doc.IsDirty)) member.DiskApplied = _applied;
        if (_move != null && (textMembers.Length > 0 ? textMembers : _members).All(m => !m.Doc.IsDirty)) _diskApplied = _applied;
    }

    internal bool HasPartialSave => _members.Where(m => m.TextChanged).Select(m => m.DiskApplied).Distinct().Skip(1).Any();
    internal bool HasPendingPath => _move != null && _applied != _diskApplied;
    internal bool DiscardNeedsRedo => !_applied && _diskApplied;
    internal bool InvolvesOnly(IReadOnlyCollection<DocumentViewModel> docs) => _members.All(m => docs.Contains(m.Doc));

    internal string? DiscardPath()
    {
        if (!HasPendingPath) return null;
        if (_problem?.Invoke(_diskApplied) is { } problem) return problem;
        try
        {
            _move!(_diskApplied);
            _completed?.Invoke(_diskApplied);
            return null;
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }

    internal string? RestoreLivePath()
    {
        try { _move!(_applied); _completed?.Invoke(_applied); return null; }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { return ex.Message; }
    }

    internal void Prepare(bool redo)
    {
        if (_prepared) return;
        _move?.Invoke(redo);
        _prepared = true;
    }

    private void BeginPlay(DocumentViewModel origin, bool redo)
    {
        if (_playing) return;
        // Os comandos da interface verificam antes de retirar a ação da pilha.
        // A verificação aqui também protege chamadas diretas com histórico incompatível.
        if (Problem(redo) is { } problem) throw new Core.Files.UserFacingException(problem);
        Prepare(redo);
        _playing = true;
        _origin = origin;
        try
        {
            foreach (var member in _members.Where(m => m.Doc != origin))
                if (redo) member.Stack.Redo(); else member.Stack.Undo();
        }
        catch { _playing = false; _prepared = false; _origin = null; throw; }
    }

    private void EndPlay(DocumentViewModel origin, bool redo)
    {
        if (_origin != origin) return;
        _applied = redo;
        _playing = false;
        _prepared = false;
        _origin = null;
        _completed?.Invoke(redo);
    }

    private sealed class Marker(LinkedDocumentEdit edit, DocumentViewModel doc, bool beginning) : IUndoableOperation
    {
        public void Undo()
        {
            if (beginning) edit.EndPlay(doc, redo: false); else edit.BeginPlay(doc, redo: false);
        }
        public void Redo()
        {
            if (beginning) edit.BeginPlay(doc, redo: true); else edit.EndPlay(doc, redo: true);
        }
    }
}
