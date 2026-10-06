using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using CSharpLab.Core.Language;
using CSharpLab.ViewModels;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using Microsoft.CodeAnalysis.Text;

namespace CSharpLab.Editor;

/// <summary>Editor de um documento, com os recursos de C# ligados ao Roslyn.</summary>
public sealed class CodeEditor : TextEditor
{
    private readonly MainViewModel _vm;
    private readonly CSharpColorizer? _colorizer;
    private readonly DiagnosticRenderer _diagnostics;
    private readonly BracketHighlighter _brackets = new();
    private readonly SmartIndentation _indentation = new(4);
    private readonly CompletionController _completion;
    private readonly SignatureHelpPopup _signature;
    private readonly DispatcherTimer _semanticTimer;
    private readonly List<TextAnchor> _autoClosers = [];
    private CancellationTokenSource? _semanticCts;
    private ToolTip? _hoverTip;

    public CodeEditor(DocumentViewModel doc, MainViewModel vm)
    {
        Doc = doc;
        _vm = vm;
        Syntax = new SyntaxCache(doc);
        Document = doc.Document;

        FontFamily = (FontFamily)Application.Current.FindResource("CodeFont");
        SetBinding(FontSizeProperty, new System.Windows.Data.Binding(nameof(MainViewModel.EditorFontSize)) { Source = vm });
        Background = SyntaxTheme.Background;
        Foreground = SyntaxTheme.Foreground;
        ShowLineNumbers = true;
        LineNumbersForeground = SyntaxTheme.LineNumbers;
        Padding = new Thickness(0);
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        FocusVisualStyle = null;

        Options.ConvertTabsToSpaces = true;
        Options.IndentationSize = 4;
        Options.HighlightCurrentLine = true;
        Options.EnableHyperlinks = false;
        Options.EnableEmailHyperlinks = false;
        Options.EnableRectangularSelection = true;
        Options.CutCopyWholeLine = true;
        Options.AllowScrollBelowDocument = true;
        Options.ShowBoxForControlCharacters = true;

        var area = TextArea;
        area.SelectionBrush = SyntaxTheme.Selection;
        area.SelectionForeground = null;
        area.SelectionBorder = null;
        area.SelectionCornerRadius = 2;
        area.Caret.CaretBrush = SyntaxTheme.Foreground;
        area.TextView.CurrentLineBackground = SyntaxTheme.CurrentLine;
        area.TextView.CurrentLineBorder = new Pen(Brushes.Transparent, 0);
        area.FocusVisualStyle = null;
        StyleMargins(area);

        _diagnostics = new DiagnosticRenderer(doc.Document);
        area.TextView.BackgroundRenderers.Add(_brackets);
        area.TextView.BackgroundRenderers.Add(Search);
        // Por último, para o sublinhado ficar por cima dos destaques.
        area.TextView.BackgroundRenderers.Add(_diagnostics);

        if (doc.IsCSharp)
        {
            area.IndentationStrategy = _indentation;
            _colorizer = new CSharpColorizer(Syntax, doc.Document);
            area.TextView.LineTransformers.Add(_colorizer);
        }

        _completion = new CompletionController(this);
        _signature = new SignatureHelpPopup(this);
        _semanticTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(220) };
        _semanticTimer.Tick += (_, _) =>
        {
            _semanticTimer.Stop();
            RefreshSemantic();
        };

        area.TextEntering += OnTextEntering;
        area.TextEntered += OnTextEntered;
        area.PreviewKeyDown += OnPreviewKeyDown;
        area.Caret.PositionChanged += OnCaretMoved;
        area.TextView.MouseHover += OnMouseHover;
        area.TextView.MouseHoverStopped += (_, _) => CloseHover();
        area.TextView.ScrollOffsetChanged += (_, _) =>
        {
            CloseHover();
            _completion.Close();
            if (Doc.Document.TextLength > SemanticWholeDocumentLimit) ScheduleSemantic();
        };
        PreviewMouseWheel += OnPreviewMouseWheel;
        LostKeyboardFocus += (_, e) =>
        {
            if (e.NewFocus is not DependencyObject d || !IsAncestorOf(d))
            {
                _signature.Close();
                _completion.Close();
            }
        };
        doc.TextChanged += OnDocTextChanged;
        doc.PropertyChanged += OnDocPropertyChanged;
        vm.LanguageReady += OnLanguageReady;
        if (vm.Language != null) OnLanguageReady();

        _diagnostics.SetDiagnostics(doc.Diagnostics);
    }

    private const int SemanticWholeDocumentLimit = 200_000;

    public DocumentViewModel Doc { get; }
    public SyntaxCache Syntax { get; }
    public SearchHighlighter Search { get; } = new();
    public LanguageService? LanguageServices => _vm.Language;
    public CompletionController CompletionController => _completion;

    private void StyleMargins(TextArea area)
    {
        foreach (var margin in area.LeftMargins)
        {
            if (margin is LineNumberMargin ln)
            {
                ln.Margin = new Thickness(14, 0, 0, 0);
            }
            else if (margin is Line line)
            {
                // Substitui a linha pontilhada por um espaçamento limpo.
                line.Stroke = Brushes.Transparent;
                line.Margin = new Thickness(0, 0, 14, 0);
            }
        }
    }

    public void Detach()
    {
        Doc.TextChanged -= OnDocTextChanged;
        Doc.PropertyChanged -= OnDocPropertyChanged;
        _vm.LanguageReady -= OnLanguageReady;
        if (LanguageServices != null) LanguageServices.ProjectChanged -= OnProjectChanged;
        _semanticTimer.Stop();
        _semanticCts?.Cancel();
        _completion.Close();
        _signature.Close();
        CloseHover();
    }

    private void OnLanguageReady()
    {
        if (!Doc.IsCSharp) return;
        if (LanguageServices?.GetDocument(Doc.LanguageKey)?.Project.ParseOptions is Microsoft.CodeAnalysis.CSharp.CSharpParseOptions options)
            Syntax.SetOptions(options);
        LanguageServices!.ProjectChanged -= OnProjectChanged;
        LanguageServices.ProjectChanged += OnProjectChanged;
        ScheduleSemantic();
    }

    private void OnProjectChanged() => Dispatcher.BeginInvoke(ScheduleSemantic);

    private void OnDocPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentViewModel.Diagnostics))
        {
            _diagnostics.SetDiagnostics(Doc.Diagnostics);
            TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
        }
        else if (e.PropertyName == nameof(DocumentViewModel.FilePath))
        {
            ScheduleSemantic();
        }
    }

    private void OnDocTextChanged(DocumentViewModel _) => ScheduleSemantic();

    private void ScheduleSemantic()
    {
        if (_colorizer == null) return;
        _semanticTimer.Stop();
        _semanticTimer.Start();
    }

    private async void RefreshSemantic()
    {
        var ls = LanguageServices;
        if (ls == null || _colorizer == null) return;
        _semanticCts?.Cancel();
        var cts = _semanticCts = new CancellationTokenSource();
        int version = Doc.Version;
        int length = Doc.Document.TextLength;
        TextSpan span;
        if (length <= SemanticWholeDocumentLimit)
        {
            span = new TextSpan(0, length);
        }
        else
        {
            var tv = TextArea.TextView;
            if (!tv.VisualLinesValid || tv.VisualLines.Count == 0) return;
            int start = Math.Max(0, tv.VisualLines[0].FirstDocumentLine.Offset - 4000);
            int end = Math.Min(length, tv.VisualLines[^1].LastDocumentLine.EndOffset + 4000);
            span = TextSpan.FromBounds(start, end);
        }
        try
        {
            var key = Doc.LanguageKey;
            var ranges = await Task.Run(() => ls.ClassifySemanticAsync(key, span, cts.Token), cts.Token);
            if (cts.IsCancellationRequested || version != Doc.Version) return;
            _colorizer.SetSemantic(ranges, Doc.Document.TextLength);
            TextArea.TextView.Redraw();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Core.Settings.AppPaths.Log(ex, "Classificação");
        }
    }

    // ================================================================ digitação

    private bool InCommentOrString(int offset)
    {
        try
        {
            return SyntaxContext.IsInCommentOrString(Syntax.Root, offset);
        }
        catch
        {
            return false;
        }
    }

    private void OnTextEntering(object? sender, TextCompositionEventArgs e)
    {
        if (e.Text.Length != 1) return;
        char c = e.Text[0];

        if (_completion.IsOpen && !CompletionController.IsIdentifierChar(c))
            _completion.Close();

        if (!Doc.IsCSharp) return;
        var document = Document;
        int caret = CaretOffset;

        // Digitar o fechamento que foi inserido automaticamente só avança o cursor.
        if (c is ')' or ']' or '}' or '"' or '\'' && caret < document.TextLength && document.GetCharAt(caret) == c &&
            TakeAutoCloser(caret))
        {
            e.Handled = true;
            CaretOffset = caret + 1;
            AfterCharTyped(c);
            return;
        }

        if (c is '(' or '[' or '{' or '"' or '\'' && TextArea.Selection.IsEmpty && !TextArea.OverstrikeMode)
        {
            if (InCommentOrString(caret)) return;
            char next = caret < document.TextLength ? document.GetCharAt(caret) : '\0';
            if (!(next == '\0' || char.IsWhiteSpace(next) || next is ')' or ']' or '}' or ';' or ',')) return;
            if (c is '"' or '\'')
            {
                char prev = caret > 0 ? document.GetCharAt(caret - 1) : '\0';
                if (char.IsLetterOrDigit(prev) || prev == '_' || prev == c) return;
            }
            var closer = c switch { '(' => ')', '[' => ']', '{' => '}', _ => c };
            e.Handled = true;
            using (document.RunUpdate())
            {
                document.Insert(caret, new string([c, closer]));
                if (c == '{')
                {
                    // O recuo da linha pode mudar; a âncora acompanha a posição entre as chaves.
                    var inside = document.CreateAnchor(caret + 1);
                    _indentation.AlignOpeningBrace(document, caret);
                    caret = inside.Offset - 1;
                }
            }
            CaretOffset = caret + 1;
            var anchor = document.CreateAnchor(caret + 1);
            anchor.MovementType = AnchorMovementType.AfterInsertion;
            _autoClosers.Add(anchor);
            if (_autoClosers.Count > 64) _autoClosers.RemoveAt(0);
            AfterCharTyped(c);
        }
    }

    private bool TakeAutoCloser(int offset)
    {
        _autoClosers.RemoveAll(a => a.IsDeleted);
        var anchor = _autoClosers.FirstOrDefault(a => a.Offset == offset);
        if (anchor == null) return false;
        _autoClosers.Remove(anchor);
        return true;
    }

    private void OnTextEntered(object? sender, TextCompositionEventArgs e)
    {
        if (e.Text.Length != 1 || !Doc.IsCSharp) return;
        AfterCharTyped(e.Text[0]);
    }

    private void AfterCharTyped(char c)
    {
        int caret = CaretOffset;
        if (c == '}') ReindentClosingBrace(caret - 1);
        else if (c == '{') _indentation.AlignOpeningBrace(Document, caret - 1);

        if (SignatureHelp.IsTriggerCharacter(c))
        {
            if (!InCommentOrString(caret)) _signature.Update();
        }
        else if (c == ')' && _signature.IsOpen)
        {
            _signature.Update();
        }

        if (_completion.IsOpen) return;
        if (c == '.')
        {
            if (!InCommentOrString(caret)) _completion.Request('.');
        }
        else if (CompletionController.IsIdentifierStart(c))
        {
            char before = caret >= 2 ? Document.GetCharAt(caret - 2) : ' ';
            if (!CompletionController.IsIdentifierChar(before) && !InCommentOrString(caret))
                _completion.Request(c);
        }
    }

    /// <summary>"}" digitado numa linha vazia volta ao recuo da linha do "{" correspondente.</summary>
    private void ReindentClosingBrace(int braceOffset)
    {
        if (braceOffset < 0 || braceOffset >= Document.TextLength) return;
        var line = Document.GetLineByOffset(braceOffset);
        var before = Document.GetText(line.Offset, braceOffset - line.Offset);
        if (before.Trim().Length != 0) return;
        int? indent;
        try
        {
            indent = SyntaxContext.OpeningLineIndent(Syntax.Root, Doc.SourceText, braceOffset);
        }
        catch
        {
            return;
        }
        if (indent == null || indent.Value == before.Length && !before.Contains('\t')) return;
        Document.Replace(line.Offset, before.Length, new string(' ', indent.Value));
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        switch (e.Key)
        {
            case Key.Space when mods == ModifierKeys.Control:
                _completion.Close();
                _completion.Request(null);
                e.Handled = true;
                break;
            case Key.Escape when _signature.IsOpen:
                _signature.Close();
                e.Handled = true;
                break;
            case Key.Up when mods == ModifierKeys.None && _signature.IsOpen && _signature.HasOverloads && !_completion.IsOpen:
                _signature.Cycle(-1);
                e.Handled = true;
                break;
            case Key.Down when mods == ModifierKeys.None && _signature.IsOpen && _signature.HasOverloads && !_completion.IsOpen:
                _signature.Cycle(1);
                e.Handled = true;
                break;
            case Key.Tab when mods == ModifierKeys.None && Doc.IsCSharp:
                e.Handled = TryExpandSnippetAtCaret();
                break;
            case Key.Enter when mods == ModifierKeys.None && Doc.IsCSharp:
                e.Handled = TryEnterBetweenBraces();
                break;
            case Key.Back when mods == ModifierKeys.None && Doc.IsCSharp:
                e.Handled = TryDeletePair();
                break;
        }
    }

    /// <summary>Tab logo após um atalho de snippet válido no contexto: expande. Caso contrário, Tab normal.</summary>
    private bool TryExpandSnippetAtCaret()
    {
        if (!TextArea.Selection.IsEmpty) return false;
        int caret = CaretOffset;
        int start = caret;
        while (start > 0 && CompletionController.IsIdentifierChar(Document.GetCharAt(start - 1))) start--;
        if (start == caret) return false;
        if (start > 0 && Document.GetCharAt(start - 1) is '.' or '@') return false;
        var word = Document.GetText(start, caret - start);
        var snippet = Snippets.Find(word);
        if (snippet == null) return false;
        CodeContext context;
        try
        {
            context = SyntaxContext.GetContext(Syntax.Root, start);
        }
        catch
        {
            return false;
        }
        if (!Snippets.Fits(snippet, context)) return false;
        SnippetExpander.Expand(TextArea, snippet, start, caret - start);
        return true;
    }

    private bool TryEnterBetweenBraces()
    {
        if (!TextArea.Selection.IsEmpty) return false;
        int caret = CaretOffset;
        int left = caret - 1;
        while (left >= 0 && Document.GetCharAt(left) is ' ' or '\t') left--;
        int right = caret;
        while (right < Document.TextLength && Document.GetCharAt(right) is ' ' or '\t') right++;
        if (left < 0 || right >= Document.TextLength || Document.GetCharAt(left) != '{' || Document.GetCharAt(right) != '}') return false;

        var line = Document.GetLineByOffset(left);
        var indent = TextUtilities.GetLeadingWhitespace(Document, line).Length is var n ? Document.GetText(line.Offset, n) : "";
        var newline = TextUtilities.GetNewLineFromDocument(Document, line.LineNumber);
        var inner = indent + new string(' ', Options.IndentationSize);
        using (Document.RunUpdate())
        {
            Document.Replace(left + 1, right - (left + 1), newline + inner + newline + indent);
        }
        CaretOffset = left + 1 + newline.Length + inner.Length;
        return true;
    }

    private bool TryDeletePair()
    {
        if (!TextArea.Selection.IsEmpty) return false;
        int caret = CaretOffset;
        if (caret <= 0 || caret >= Document.TextLength) return false;
        char open = Document.GetCharAt(caret - 1), close = Document.GetCharAt(caret);
        bool pair = (open, close) is ('(', ')') or ('[', ']') or ('{', '}') or ('"', '"') or ('\'', '\'');
        if (!pair || !_autoClosers.Any(a => !a.IsDeleted && a.Offset == caret)) return false;
        TakeAutoCloser(caret);
        Document.Remove(caret - 1, 2);
        return true;
    }

    public void ReportCaret()
    {
        var pos = TextArea.Caret.Position;
        _vm.CaretText = $"Ln {pos.Line}, Col {pos.Column}";
    }

    private void OnCaretMoved(object? sender, EventArgs e)
    {
        ReportCaret();
        if (Doc.IsCSharp)
        {
            (int, int)? pair = null;
            try
            {
                pair = SyntaxContext.FindBracePair(Syntax.Root, CaretOffset);
            }
            catch
            {
            }
            if (_brackets.SetPair(pair)) TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
            if (_signature.IsOpen) _signature.Update();
        }
        if (_completion.IsOpen) Dispatcher.BeginInvoke(_completion.Update, DispatcherPriority.Input);
    }

    public void ShowCompletion()
    {
        _completion.Close();
        _completion.Request(null);
    }

    // ================================================================ dicas de erro

    private void OnMouseHover(object? sender, MouseEventArgs e)
    {
        var pos = TextArea.TextView.GetPositionFloor(e.GetPosition(TextArea.TextView) + TextArea.TextView.ScrollOffset);
        if (pos == null) return;
        int offset = Document.GetOffset(pos.Value.Location);
        var markers = _diagnostics.At(offset).Take(3).ToList();
        if (markers.Count == 0) return;

        var panel = new StackPanel();
        foreach (var m in markers)
        {
            var d = m.Diagnostic;
            panel.Children.Add(new TextBlock
            {
                Text = $"Linha {d.Line} — {d.Message}",
                Foreground = (Brush)Application.Current.FindResource("TextPrimary"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, panel.Children.Count > 0 ? 6 : 0, 0, 0),
            });
            panel.Children.Add(new TextBlock
            {
                Text = $"{d.Id} · {d.OriginalMessage}",
                Foreground = (Brush)Application.Current.FindResource("TextMuted"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0),
            });
        }
        CloseHover();
        _hoverTip = new ToolTip { Content = panel, PlacementTarget = this, Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse, IsOpen = true };
        e.Handled = true;
    }

    private void CloseHover()
    {
        if (_hoverTip != null)
        {
            _hoverTip.IsOpen = false;
            _hoverTip = null;
        }
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        _vm.EditorFontSize = Math.Clamp(_vm.EditorFontSize + (e.Delta > 0 ? 1 : -1), 9, 32);
        e.Handled = true;
    }

    // ================================================================ comandos

    public async Task FormatAsync()
    {
        var ls = LanguageServices;
        if (ls == null || !Doc.IsCSharp) return;
        int version = Doc.Version;
        IReadOnlyList<Microsoft.CodeAnalysis.Text.TextChange> changes;
        try
        {
            changes = await Task.Run(() => ls.FormatAsync(Doc.LanguageKey, CancellationToken.None));
        }
        catch (Exception ex)
        {
            Core.Settings.AppPaths.Log(ex, "Formatação");
            return;
        }
        if (version != Doc.Version || changes.Count == 0) return;

        // Uma única operação de desfazer; aplicada de trás para frente para manter as posições.
        using (Document.RunUpdate())
        {
            foreach (var change in changes.OrderByDescending(c => c.Span.Start))
                Document.Replace(change.Span.Start, change.Span.Length, change.NewText ?? "");
        }
    }

    public void GoTo(int line, int column, int? offset)
    {
        if (offset is { } o && o <= Document.TextLength)
        {
            CaretOffset = o;
        }
        else
        {
            line = Math.Clamp(line, 1, Document.LineCount);
            var docLine = Document.GetLineByNumber(line);
            CaretOffset = docLine.Offset + Math.Clamp(column - 1, 0, docLine.Length);
        }
        TextArea.ClearSelection();
        var loc = Document.GetLocation(CaretOffset);
        ScrollTo(loc.Line, loc.Column);
        Focus();
        TextArea.Focus();
    }

    public void ClosePopups()
    {
        _completion.Close();
        _signature.Close();
        CloseHover();
    }
}
