using System.Windows.Input;
using System.Windows.Threading;
using CSharpLab.Core.Language;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Snippets;

namespace CSharpLab.Editor;

/// <summary>
/// Sensação ao digitar: desfazer por palavra (não por letra), Backspace que volta um nível de recuo,
/// Tab que recua blocos e campos de snippet que saem do caminho quando o cursor sai deles.
/// </summary>
public sealed partial class CodeEditor
{
    // ================================================================ desfazer por palavra

    private char _groupKind;
    private int _groupVersion = -1;
    private int _groupCaret = -1;
    private char _lastTyped;
    private bool _groupOpen;

    /// <summary>
    /// Abre um passo de desfazer que continua o anterior quando é a mesma ação (digitar ou apagar)
    /// seguida, no mesmo ponto e sem nada no meio; <paramref name="boundary"/> força um passo novo.
    /// </summary>
    private void BeginEditGroup(char kind, bool boundary)
    {
        CloseEditGroup();
        var undo = Document.UndoStack;
        bool merge = !boundary && kind == _groupKind && _groupVersion == Doc.Version && _groupCaret == CaretOffset && Doc.IsDirty;
        if (merge) undo.StartContinuedUndoGroup(null);
        else undo.StartUndoGroup();
        _groupKind = kind;
        _groupOpen = true;
        // Rede de segurança: nenhum passo fica aberto depois desta tecla.
        Dispatcher.BeginInvoke(CloseEditGroup, DispatcherPriority.Send);
    }

    private void CloseEditGroup()
    {
        if (!_groupOpen) return;
        _groupOpen = false;
        Document.UndoStack.EndUndoGroup();
        _groupVersion = Doc.Version;
        _groupCaret = CaretOffset;
    }

    /// <summary>Cada palavra digitada é um passo de desfazer; Enter começa um passo novo.</summary>
    private void BeginTyping(string text)
    {
        char c = text[^1];
        bool boundary = text.Length != 1 || c is '\n' or '\r' ||
                        !char.IsWhiteSpace(c) && char.IsWhiteSpace(_lastTyped) ||
                        _groupKind != 't';
        BeginEditGroup('t', boundary);
        _lastTyped = c;
    }

    // ================================================================ Backspace

    /// <summary>
    /// Backspace sem seleção: no recuo do começo da linha apaga até o nível anterior (como o Tab, só que
    /// ao contrário); entre um par recém-fechado apaga os dois; fora disso, um caractere.
    /// Apagar seguido vira um passo só de desfazer.
    /// </summary>
    internal bool Backspace()
    {
        // Seleção (inclusive a retangular, que apaga em várias linhas ao mesmo tempo): o AvalonEdit cuida.
        if (!TextArea.Selection.IsEmpty || TextArea.Selection is RectangleSelection || IsReadOnly) return false;
        int caret = CaretOffset;
        if (caret == 0) return false;
        if (Doc.IsCSharp && TryDeletePair()) return true;

        var line = Document.GetLineByOffset(caret);
        int column = caret - line.Offset;
        int start;
        if (column > 0 && Options.ConvertTabsToSpaces && IsOnlySpaces(line.Offset, caret))
        {
            int size = Math.Max(1, Options.IndentationSize);
            start = caret - ((column - 1) % size + 1);
        }
        else if (column == 0)
        {
            start = line.PreviousLine!.EndOffset;   // a quebra de linha inteira ("\r\n" é uma só)
        }
        else
        {
            start = TextUtilities.GetNextCaretPosition(Document, caret, System.Windows.Documents.LogicalDirection.Backward, CaretPositioningMode.Normal);
            if (start < 0 || start >= caret) return false;
        }
        bool newline = start < line.Offset;
        BeginEditGroup('d', boundary: newline);
        try
        {
            Document.Remove(start, caret - start);
        }
        finally
        {
            CloseEditGroup();
        }
        TextArea.Caret.BringCaretToView();
        return true;
    }

    private bool IsOnlySpaces(int from, int to)
    {
        for (int i = from; i < to; i++)
        {
            if (Document.GetCharAt(i) != ' ') return false;
        }
        return true;
    }

    // ================================================================ Tab com várias linhas

    /// <summary>Tab com várias linhas selecionadas: um nível a mais em cada linha com texto (linhas vazias ficam limpas).</summary>
    internal bool IndentSelectedLines()
    {
        var selection = TextArea.Selection;
        if (selection.IsEmpty || !selection.IsMultiline || selection is RectangleSelection) return false;
        var (first, last) = SelectedLines();
        IndentLineRange(first, last);
        return true;
    }

    private void IndentLineRange(DocumentLine first, DocumentLine last)
    {
        using (Document.RunUpdate())
        {
            for (var line = first; line != null; line = line.NextLine)
            {
                if (Document.GetText(line).Trim().Length > 0)
                    Document.Replace(line.Offset, 0, Options.IndentationString, OffsetChangeMappingType.KeepAnchorBeforeInsertion);
                if (line == last) break;
            }
        }
    }

    // ================================================================ envolver a seleção

    /// <summary>"(", "[", "{" ou aspas com um trecho selecionado (numa linha só): envolve o trecho em vez de apagá-lo.</summary>
    private bool TrySurroundSelection(char c)
    {
        var selection = TextArea.Selection;
        if (selection.IsEmpty || selection.IsMultiline || selection is RectangleSelection) return false;
        int start = selection.SurroundingSegment.Offset, length = selection.SurroundingSegment.Length;
        char closer = c switch { '(' => ')', '[' => ']', '{' => '}', _ => c };
        using (Document.RunUpdate())
        {
            Document.Insert(start + length, closer.ToString());
            Document.Insert(start, c.ToString());
        }
        // O texto de dentro continua selecionado, pronto para envolver de novo ou digitar por cima.
        Select(start + 1, length);
        return true;
    }

    // ================================================================ snippets

    private InsertionContext? _snippet;
    private TextAnchor? _snippetStart, _snippetEnd;

    /// <summary>Troca o atalho digitado pela estrutura do snippet e liga a edição dos campos.</summary>
    internal void ExpandSnippet(SnippetDefinition definition, int wordStart, int wordLength)
    {
        EndSnippet();
        var context = SnippetExpander.Expand(TextArea, definition, wordStart, wordLength);
        if (!context.ActiveElements.Any(e => e.IsEditable)) return;   // sem campos: já terminou
        _snippet = context;
        _snippetStart = Document.CreateAnchor(context.StartPosition);
        _snippetStart.MovementType = AnchorMovementType.BeforeInsertion;
        _snippetEnd = Document.CreateAnchor(context.InsertionPosition);
        _snippetEnd.MovementType = AnchorMovementType.AfterInsertion;
        context.Deactivated += (_, _) =>
        {
            if (_snippet == context) _snippet = null;
        };
    }

    internal bool InSnippet => _snippet != null;

    /// <summary>Sai da edição dos campos sem mexer no texto nem no cursor.</summary>
    private void EndSnippet()
    {
        var context = _snippet;
        if (context == null) return;
        _snippet = null;
        context.Deactivate(new SnippetEventArgs(DeactivateReason.Unknown));
    }

    /// <summary>Conclui o snippet: o cursor vai para o lugar marcado ($0), sem nada selecionado.</summary>
    private void FinishSnippet()
    {
        var context = _snippet;
        if (context == null) return;
        _snippet = null;
        context.Deactivate(new SnippetEventArgs(DeactivateReason.ReturnPressed));
        TextArea.ClearSelection();
    }

    private IActiveElement? FieldAt(int offset) => _snippet?.ActiveElements.FirstOrDefault(e =>
        e.IsEditable && e.Segment != null && offset >= e.Segment.Offset && offset <= e.Segment.EndOffset);

    /// <summary>
    /// Tab e Enter durante um snippet (antes do AvalonEdit): dentro de um campo, Tab vai ao próximo e,
    /// no último, conclui; Enter conclui. Com o cursor fora dos campos o snippet termina e a tecla age normalmente.
    /// </summary>
    private void OnSnippetKeyDown(object sender, KeyEventArgs e)
    {
        if (_snippet == null || _completion.IsOpen || Keyboard.Modifiers != ModifierKeys.None) return;
        if (e.Key is not (Key.Tab or Key.Enter)) return;
        int caret = CaretOffset;
        if (FieldAt(caret) == null)
        {
            EndSnippet();
            return;
        }
        bool hasNext = _snippet.ActiveElements.Any(f => f.IsEditable && f.Segment != null && f.Segment.Offset > caret);
        if (e.Key == Key.Enter || !hasNext)
        {
            FinishSnippet();
            e.Handled = true;
        }
    }

    /// <summary>Cursor saiu da área do snippet: a edição dos campos acaba (Tab e Enter voltam ao normal).</summary>
    private void CheckSnippetBounds()
    {
        if (_snippet == null || _snippetStart == null || _snippetEnd == null) return;
        int caret = CaretOffset;
        if (_snippetStart.IsDeleted || _snippetEnd.IsDeleted || caret < _snippetStart.Offset || caret > _snippetEnd.Offset)
            EndSnippet();
    }
}
