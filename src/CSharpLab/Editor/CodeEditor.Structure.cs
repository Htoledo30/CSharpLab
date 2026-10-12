using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CSharpLab.Core.Language;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Rendering;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharpLab.Editor;

/// <summary>
/// Enxergar a estrutura do código: linhas-guia de recuo, recolher blocos (o "−" ao lado dos números),
/// destaque das outras ocorrências do nome sob o cursor e atalhos de linhas (Ctrl+Enter, Ctrl+] …).
/// </summary>
public sealed partial class CodeEditor
{
    private IndentGuideRenderer _guides = null!;
    private readonly OccurrenceRenderer _occurrences = new();
    private FoldingManager? _foldings;
    private DispatcherTimer _structureTimer = null!;
    private DispatcherTimer _occurrenceTimer = null!;
    private CancellationTokenSource? _occurrenceCts;

    private void InitializeStructure()
    {
        _guides = new IndentGuideRenderer(() => Options.IndentationSize, () => _indentation.TabSize);
        TextArea.TextView.BackgroundRenderers.Insert(0, _guides);
        TextArea.TextView.BackgroundRenderers.Add(_occurrences);

        _foldings = FoldingManager.Install(TextArea);
        FoldingElementGenerator.TextBrush = SyntaxTheme.FoldedText;
        foreach (var margin in TextArea.LeftMargins.OfType<FoldingMargin>())
        {
            margin.FoldingMarkerBrush = SyntaxTheme.FoldingMarker;
            margin.FoldingMarkerBackgroundBrush = SyntaxTheme.Background;
            margin.SelectedFoldingMarkerBrush = SyntaxTheme.FoldingMarkerHover;
            margin.SelectedFoldingMarkerBackgroundBrush = SyntaxTheme.Background;
            margin.Margin = new Thickness(0, 0, 6, 0);
        }

        // Os blocos mudam com o texto, mas não precisam acompanhar cada tecla.
        _structureTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(350) };
        _structureTimer.Tick += (_, _) =>
        {
            _structureTimer.Stop();
            UpdateFoldings();
        };
        _occurrenceTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        _occurrenceTimer.Tick += (_, _) =>
        {
            _occurrenceTimer.Stop();
            RefreshOccurrences();
        };
        Document.Changed += OnStructureTextChanged;
        UpdateFoldings();
    }

    private void DetachStructure()
    {
        Document.Changed -= OnStructureTextChanged;
        _structureTimer.Stop();
        _occurrenceTimer.Stop();
        _occurrenceCts?.Cancel();
        if (_foldings != null) FoldingManager.Uninstall(_foldings);
        _foldings = null;
    }

    private void OnStructureTextChanged(object? sender, DocumentChangeEventArgs e)
    {
        _structureTimer.Stop();
        _structureTimer.Start();
        // As posições destacadas mudaram de lugar: some agora e volta quando o Roslyn responder.
        if (_occurrences.Clear()) TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
        _occurrenceTimer.Stop();
        _occurrenceTimer.Start();
    }

    /// <summary>Cursor mudou: guia do bloco, blocos recolhidos que ele abriu e as ocorrências.</summary>
    private void UpdateStructureForCaret()
    {
        int caret = CaretOffset;
        if (_foldings != null)
        {
            // Ir para dentro de um bloco recolhido (F12, busca, erro clicado) abre o bloco.
            foreach (var f in _foldings.GetFoldingsContaining(caret))
            {
                if (f.IsFolded && caret > f.StartOffset && caret < f.EndOffset) f.IsFolded = false;
            }
        }

        (int, int, int)? guide = null;
        if (Doc.IsCSharp)
        {
            try
            {
                if (SyntaxContext.EnclosingBlock(Syntax.Root, caret) is { } block)
                    guide = IndentGuideRenderer.BlockGuide(Document, block.Open, block.Close, Options.IndentationSize, _indentation.TabSize);
            }
            catch
            {
            }
        }
        if (_guides.SetActive(guide)) TextArea.TextView.InvalidateLayer(KnownLayer.Background);

        if (!_occurrences.Contains(caret) && _occurrences.Clear()) TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
        _occurrenceTimer.Stop();
        _occurrenceTimer.Start();
    }

    // ================================================================ recolher blocos

    /// <summary>Um "−" para cada bloco { … } de mais de uma linha; recolhido, mostra "{ … }" no fim da linha de cima.</summary>
    private void UpdateFoldings()
    {
        if (_foldings == null || IsDetached) return;
        var foldings = new List<NewFolding>();
        if (Doc.IsCSharp)
        {
            try
            {
                foreach (var (open, close) in SyntaxContext.BraceBlocks(Syntax.Root))
                {
                    if (close >= Document.TextLength) continue;
                    var openLine = Document.GetLineByOffset(open);
                    var closeLine = Document.GetLineByOffset(close);
                    if (closeLine.LineNumber == openLine.LineNumber) continue;
                    // "{" sozinho na linha: o bloco recolhe junto com a linha de cima (if, método, game.Scene…).
                    bool braceAlone = Document.GetText(openLine.Offset, open - openLine.Offset).Trim().Length == 0;
                    int start = braceAlone && openLine.PreviousLine is { } header && header.Length > 0 ? header.EndOffset : open;
                    foldings.Add(new NewFolding(start, close + 1) { Name = braceAlone ? " { … }" : "{ … }" });
                }
            }
            catch
            {
                return;
            }
        }
        foldings.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        _foldings.UpdateFoldings(foldings, -1);
    }

    /// <summary>Recolhe o bloco mais interno onde está o cursor (Ctrl+Shift+[).</summary>
    public void FoldAtCaret()
    {
        if (_foldings == null) return;
        int caret = CaretOffset;
        var line = Document.GetLineByOffset(caret);
        // Primeiro o bloco que começa na linha do cursor (o if, o método, o game.Scene); senão, o mais interno em volta.
        var section = _foldings.AllFoldings.FirstOrDefault(f => !f.IsFolded && f.StartOffset >= line.Offset && f.StartOffset <= line.EndOffset)
            ?? _foldings.GetFoldingsContaining(caret).Where(f => !f.IsFolded).OrderByDescending(f => f.StartOffset).FirstOrDefault();
        if (section == null) return;
        TextArea.ClearSelection();
        CaretOffset = section.StartOffset;
        section.IsFolded = true;
    }

    /// <summary>Abre o bloco recolhido da linha do cursor (Ctrl+Shift+]).</summary>
    public void UnfoldAtCaret()
    {
        if (_foldings == null) return;
        var line = Document.GetLineByOffset(CaretOffset);
        foreach (var f in _foldings.AllFoldings.Where(f => f.IsFolded && f.StartOffset >= line.Offset && f.StartOffset <= line.EndOffset))
            f.IsFolded = false;
    }

    /// <summary>
    /// Recolhe os blocos de fora: cada método, cada game.Scene, cada if solto do programa. Classes e namespaces
    /// ficam abertos (senão sumiria tudo), e os blocos de dentro também, para abrir um método e ver ele inteiro.
    /// </summary>
    public void FoldAll()
    {
        if (_foldings == null || !Doc.IsCSharp) return;
        UpdateFoldings();
        Microsoft.CodeAnalysis.SyntaxNode root;
        try { root = Syntax.Root; }
        catch { return; }
        var candidates = _foldings.AllFoldings.Where(f => !IsTypeBlock(root, f.EndOffset - 1)).ToList();
        foreach (var f in candidates)
        {
            if (candidates.Any(o => o != f && o.StartOffset <= f.StartOffset && o.EndOffset >= f.EndOffset)) continue;
            if (CaretOffset > f.StartOffset && CaretOffset < f.EndOffset)
            {
                TextArea.ClearSelection();
                CaretOffset = f.StartOffset;
            }
            f.IsFolded = true;
        }
    }

    public void UnfoldAll()
    {
        if (_foldings == null) return;
        foreach (var f in _foldings.AllFoldings) f.IsFolded = false;
    }

    internal IEnumerable<(int Start, int End, bool Folded)> Foldings =>
        _foldings?.AllFoldings.Select(f => (f.StartOffset, f.EndOffset, f.IsFolded)) ?? [];

    private static bool IsTypeBlock(Microsoft.CodeAnalysis.SyntaxNode root, int closeBrace)
    {
        var token = root.FindToken(closeBrace);
        return token.Parent is BaseTypeDeclarationSyntax or BaseNamespaceDeclarationSyntax;
    }

    // ================================================================ ocorrências

    internal IReadOnlyList<(int Start, int Length)> Occurrences => _occurrences.Spans;

    private async void RefreshOccurrences()
    {
        var ls = LanguageServices;
        if (IsDetached || ls == null || !Doc.IsCSharp) return;
        _occurrenceCts?.Cancel();
        var cts = _occurrenceCts = new CancellationTokenSource();
        int caret = CaretOffset, version = Doc.Version;
        string key = Doc.LanguageKey;
        IReadOnlyList<Microsoft.CodeAnalysis.Text.TextSpan> spans;
        try
        {
            spans = await Task.Run(() => ls.FindOccurrencesAsync(key, caret, cts.Token), cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            Core.Settings.AppPaths.Log(ex, "Ocorrências");
            return;
        }
        if (cts.IsCancellationRequested || IsDetached || version != Doc.Version || caret != CaretOffset || key != Doc.LanguageKey) return;
        if (_occurrences.Set(spans.Select(s => (s.Start, s.Length)).ToList()))
            TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
    }

    /// <summary>
    /// Vai para a próxima (+1) ou a anterior (-1) ocorrência do nome sob o cursor, ou do texto selecionado,
    /// e seleciona ela (Ctrl+Shift+↓ / Ctrl+Shift+↑).
    /// </summary>
    public void GoToOccurrence(int direction)
    {
        var targets = _occurrences.Contains(CaretOffset) ? _occurrences.Spans.ToList() : TextOccurrences();
        if (targets.Count < 2) return;
        int caret = TextArea.Selection.IsEmpty ? CaretOffset : TextArea.Selection.SurroundingSegment.Offset;
        int current = targets.FindLastIndex(t => t.Start <= caret);
        int index = current < 0
            ? (direction > 0 ? 0 : targets.Count - 1)
            : ((current + direction) % targets.Count + targets.Count) % targets.Count;
        var (start, length) = targets[index];
        Select(start, length);
        var location = Document.GetLocation(start);
        ScrollTo(location.Line, location.Column);
    }

    /// <summary>Sem o Roslyn: o texto selecionado, ou a palavra sob o cursor, como palavra inteira.</summary>
    private List<(int Start, int Length)> TextOccurrences()
    {
        string word;
        bool wholeWord;
        if (!TextArea.Selection.IsEmpty && !TextArea.Selection.IsMultiline)
        {
            word = TextArea.Selection.GetText();
            wholeWord = word.All(CompletionController.IsIdentifierChar);
        }
        else
        {
            int start = CaretOffset, end = CaretOffset;
            while (start > 0 && CompletionController.IsIdentifierChar(Document.GetCharAt(start - 1))) start--;
            while (end < Document.TextLength && CompletionController.IsIdentifierChar(Document.GetCharAt(end))) end++;
            word = Document.GetText(start, end - start);
            wholeWord = true;
        }
        var result = new List<(int, int)>();
        if (word.Length == 0) return result;
        var text = Document.Text;
        for (int at = text.IndexOf(word, StringComparison.Ordinal); at >= 0; at = text.IndexOf(word, at + word.Length, StringComparison.Ordinal))
        {
            bool isolated = !wholeWord ||
                            (at == 0 || !CompletionController.IsIdentifierChar(text[at - 1])) &&
                            (at + word.Length >= text.Length || !CompletionController.IsIdentifierChar(text[at + word.Length]));
            if (isolated) result.Add((at, word.Length));
        }
        return result;
    }

    // ================================================================ linhas

    /// <summary>Linha nova abaixo da atual, já no recuo certo, sem quebrar a linha onde o cursor está (Ctrl+Enter).</summary>
    public void InsertLineBelow()
    {
        TextArea.ClearSelection();
        CaretOffset = Document.GetLineByOffset(CaretOffset).EndOffset;
        TextArea.PerformTextInput("\n");
    }

    /// <summary>Linha nova acima da atual (Ctrl+Shift+Enter).</summary>
    public void InsertLineAbove()
    {
        TextArea.ClearSelection();
        var line = Document.GetLineByOffset(CaretOffset);
        if (line.PreviousLine is { } previous)
        {
            CaretOffset = previous.EndOffset;
            TextArea.PerformTextInput("\n");
            return;
        }
        var indent = Document.GetText(line.Offset, TextUtilities.GetLeadingWhitespace(Document, line).Length);
        Document.Insert(0, indent + TextUtilities.GetNewLineFromDocument(Document, 1));
        CaretOffset = indent.Length;
    }

    /// <summary>Um nível a mais nas linhas da seleção, ou na linha do cursor (Ctrl+]).</summary>
    public void IndentLines()
    {
        var (first, last) = SelectedLines();
        IndentLineRange(first, last);
    }

    /// <summary>Um nível a menos (Ctrl+[), igual ao Shift+Tab.</summary>
    public void OutdentLines() => EditingCommands.TabBackward.Execute(null, TextArea);

    /// <summary>O caractere que a tecla escreve no teclado em uso (no ABNT2, "[" e "]" ficam em outras teclas).</summary>
    private static char KeyChar(Key key)
    {
        int vk = KeyInterop.VirtualKeyFromKey(key);
        uint mapped = MapVirtualKey((uint)vk, 2);   // MAPVK_VK_TO_CHAR
        return (char)(mapped & 0x7FFF);
    }

    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    private static extern uint MapVirtualKey(uint code, uint mapType);
}

/// <summary>Fundo discreto nas outras ocorrências do nome sob o cursor.</summary>
public sealed class OccurrenceRenderer : IBackgroundRenderer
{
    private List<(int Start, int Length)> _spans = [];

    public KnownLayer Layer => KnownLayer.Selection;

    public IReadOnlyList<(int Start, int Length)> Spans => _spans;

    public bool Contains(int offset) => _spans.Any(s => offset >= s.Start && offset <= s.Start + s.Length);

    public bool Set(List<(int Start, int Length)> spans)
    {
        if (_spans.SequenceEqual(spans)) return false;
        _spans = spans;
        return true;
    }

    public bool Clear()
    {
        if (_spans.Count == 0) return false;
        _spans = [];
        return true;
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_spans.Count == 0 || textView.Document == null || !textView.VisualLinesValid || textView.VisualLines.Count == 0) return;
        int viewStart = textView.VisualLines[0].FirstDocumentLine.Offset;
        int viewEnd = textView.VisualLines[^1].LastDocumentLine.EndOffset;
        foreach (var (start, length) in _spans)
        {
            if (start + length < viewStart || start > viewEnd || start + length > textView.Document.TextLength) continue;
            var builder = new BackgroundGeometryBuilder { CornerRadius = 2, AlignToWholePixels = true };
            builder.AddSegment(textView, new TextSegment { StartOffset = start, Length = length });
            if (builder.CreateGeometry() is { } geometry)
                drawingContext.DrawGeometry(SyntaxTheme.Occurrence, SyntaxTheme.OccurrencePen, geometry);
        }
    }
}
