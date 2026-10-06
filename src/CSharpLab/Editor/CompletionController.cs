using System.Collections.Immutable;
using System.Windows.Media;
using CSharpLab.Core.Language;
using ICSharpCode.AvalonEdit.Document;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Tags;

namespace CSharpLab.Editor;

/// <summary>Uma linha da lista de sugestões: símbolo real do Roslyn ou snippet (sempre identificado como tal).</summary>
public sealed class CompletionEntry
{
    private string? _detail;
    private bool _detailRequested;

    public CompletionEntry(CompletionItem item, CompletionResult result)
    {
        Item = item;
        Result = result;
        Text = string.IsNullOrEmpty(item.FilterText) ? item.DisplayText : item.FilterText;
        DisplayText = item.DisplayTextPrefix + item.DisplayText + item.DisplayTextSuffix;
        (Glyph, GlyphBrush, KindName) = GlyphFor(item.Tags);
        Priority = item.Rules.MatchPriority;
    }

    public CompletionEntry(SnippetDefinition snippet)
    {
        Snippet = snippet;
        Text = snippet.Shortcut;
        DisplayText = snippet.Shortcut;
        Glyph = "⋯";
        GlyphBrush = SyntaxTheme.Freeze("#9AA6FA");
        KindName = "snippet";
        _detail = snippet.Description;
        _detailRequested = true;
        // Em empate (ex.: "for"), o snippet vem antes da palavra-chave homônima.
        Priority = 10;
    }

    public CompletionItem? Item { get; }
    public CompletionResult? Result { get; }
    public SnippetDefinition? Snippet { get; }

    public string Text { get; }
    public string DisplayText { get; }
    public string Glyph { get; }
    public Brush GlyphBrush { get; }
    public string KindName { get; }
    public double Priority { get; }

    /// <summary>Notificado quando a assinatura chega do Roslyn.</summary>
    public Action<CompletionEntry>? DetailLoaded { get; set; }

    /// <summary>Assinatura curta do item, carregada sob demanda.</summary>
    public string? Detail
    {
        get
        {
            if (!_detailRequested && Item != null && Result != null)
            {
                _detailRequested = true;
                _ = LoadDetailAsync();
            }
            return _detail;
        }
    }

    private async Task LoadDetailAsync()
    {
        string? text = null;
        try
        {
            text = await Task.Run(() => LanguageService.GetDescriptionAsync(Result!, Item!, CancellationToken.None));
        }
        catch
        {
        }
        _detail = text ?? DisplayText;
        DetailLoaded?.Invoke(this);
    }

    private static readonly Dictionary<string, (string, Brush, string)> Glyphs = new()
    {
        [WellKnownTags.Class] = ("C", SyntaxTheme.For(TokenKind.Type), "classe"),
        [WellKnownTags.Structure] = ("S", SyntaxTheme.For(TokenKind.Struct), "struct"),
        [WellKnownTags.Interface] = ("I", SyntaxTheme.For(TokenKind.Interface), "interface"),
        [WellKnownTags.Enum] = ("E", SyntaxTheme.For(TokenKind.Enum), "enum"),
        [WellKnownTags.EnumMember] = ("e", SyntaxTheme.For(TokenKind.EnumMember), "valor"),
        [WellKnownTags.Delegate] = ("D", SyntaxTheme.For(TokenKind.Type), "delegate"),
        [WellKnownTags.Method] = ("M", SyntaxTheme.For(TokenKind.Method), "método"),
        [WellKnownTags.ExtensionMethod] = ("M", SyntaxTheme.For(TokenKind.Method), "método"),
        [WellKnownTags.Property] = ("P", SyntaxTheme.For(TokenKind.Property), "propriedade"),
        [WellKnownTags.Field] = ("F", SyntaxTheme.For(TokenKind.Field), "campo"),
        [WellKnownTags.Event] = ("Ev", SyntaxTheme.For(TokenKind.Field), "evento"),
        [WellKnownTags.Local] = ("v", SyntaxTheme.For(TokenKind.Local), "variável"),
        [WellKnownTags.Parameter] = ("p", SyntaxTheme.For(TokenKind.Parameter), "parâmetro"),
        [WellKnownTags.RangeVariable] = ("v", SyntaxTheme.For(TokenKind.Local), "variável"),
        [WellKnownTags.Constant] = ("c", SyntaxTheme.For(TokenKind.Constant), "constante"),
        [WellKnownTags.Keyword] = ("k", SyntaxTheme.For(TokenKind.Keyword), "palavra-chave"),
        [WellKnownTags.Namespace] = ("{}", SyntaxTheme.For(TokenKind.Namespace), "namespace"),
        [WellKnownTags.TypeParameter] = ("T", SyntaxTheme.For(TokenKind.TypeParameter), "tipo genérico"),
        [WellKnownTags.Label] = ("L", SyntaxTheme.For(TokenKind.Plain), "rótulo"),
    };

    private static (string, Brush, string) GlyphFor(ImmutableArray<string> tags)
    {
        foreach (var tag in tags)
        {
            if (Glyphs.TryGetValue(tag, out var g)) return g;
        }
        return ("·", SyntaxTheme.For(TokenKind.Plain), "");
    }
}

/// <summary>
/// Autocomplete com o Roslyn: abre ao digitar um identificador, após "." ou com Ctrl+Space.
/// Aceita só com Tab ou Enter; espaço e pontuação fecham a lista sem inserir nada.
/// </summary>
public sealed class CompletionController
{
    private readonly CodeEditor _editor;
    private readonly CompletionPopup _popup;
    private CancellationTokenSource? _cts;

    public CompletionController(CodeEditor editor)
    {
        _editor = editor;
        _popup = new CompletionPopup(editor);
    }

    public bool IsOpen => _popup.IsOpen;

    public void Close()
    {
        _cts?.Cancel();
        if (_popup.IsOpen) _popup.Close();
    }

    public void Update() => _popup.Update();

    public static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    public static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_' || c == '@';

    public async void Request(char? typedChar)
    {
        var ls = _editor.LanguageServices;
        if (ls == null || !_editor.Doc.IsCSharp) return;
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var doc = _editor.Doc;
        int caret = _editor.CaretOffset;
        var key = doc.LanguageKey;

        CompletionResult? result;
        try
        {
            result = await Task.Run(() => ls.GetCompletionsAsync(key, caret, typedChar, cts.Token), cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            Core.Settings.AppPaths.Log(ex, "Autocomplete");
            return;
        }
        if (cts.IsCancellationRequested || _popup.IsOpen) return;

        var document = _editor.Document;
        int nowCaret = _editor.CaretOffset;
        int start = result?.Span.Start ?? WordStart(document, nowCaret);
        if (start > nowCaret || start < 0 || start > document.TextLength) return;
        // Entre o pedido e a resposta o usuário só pode ter digitado mais letras da mesma palavra.
        var typed = document.GetText(start, nowCaret - start);
        if (typed.Any(c => !IsIdentifierChar(c) && c != '@')) return;
        if (result != null && result.IsSuggestionMode && typedChar != null) return;

        var entries = new List<CompletionEntry>();
        if (result != null)
            // Os snippets do próprio Roslyn ficam de fora: os do editor são os definidos para o curso.
            entries.AddRange(result.Items.Where(i => !i.Tags.Contains(WellKnownTags.Snippet)).Select(i => new CompletionEntry(i, result)));

        // Snippets só onde fazem sentido (nunca após ".", nem em comentários/strings).
        if (typedChar != '.')
        {
            var context = SyntaxContext.GetContext(_editor.Syntax.Root, start);
            foreach (var s in Snippets.All)
            {
                if (Snippets.Fits(s, context)) entries.Add(new CompletionEntry(s));
            }
        }
        if (entries.Count == 0) return;
        _popup.Show(entries, start);
    }

    private static int WordStart(TextDocument document, int caret)
    {
        int start = caret;
        while (start > 0 && IsIdentifierChar(document.GetCharAt(start - 1))) start--;
        return start;
    }

    /// <summary>Aplica a sugestão escolhida no trecho [start, start+length).</summary>
    public async void Commit(CompletionEntry entry, int start, int length)
    {
        var textArea = _editor.TextArea;
        var document = textArea.Document;
        if (entry.Snippet != null)
        {
            SnippetExpander.Expand(textArea, entry.Snippet, start, length);
            return;
        }
        if (entry.Item == null || entry.Result == null) return;

        // Âncoras mantêm o trecho certo mesmo se o usuário digitar enquanto o Roslyn responde.
        var startAnchor = document.CreateAnchor(start);
        startAnchor.MovementType = AnchorMovementType.BeforeInsertion;
        var endAnchor = document.CreateAnchor(start + length);
        endAnchor.MovementType = AnchorMovementType.BeforeInsertion;
        int originalStart = entry.Result.Span.Start;
        string typed = document.GetText(start, length);
        string key = _editor.Doc.LanguageKey;

        CompletionChangeResult? change;
        try
        {
            change = await Task.Run(() => LanguageService.GetCompletionChangeAsync(entry.Result, entry.Item, CancellationToken.None));
        }
        catch (Exception ex)
        {
            Core.Settings.AppPaths.Log(ex, "Aplicando sugestão");
            return;
        }
        // Digitar depois da palavra (ex.: "(" logo após o Tab) não cancela a sugestão;
        // só desiste se a própria palavra mudou enquanto o Roslyn respondia.
        if (change == null || startAnchor.IsDeleted || endAnchor.IsDeleted ||
            key != _editor.Doc.LanguageKey || _editor.IsDetached ||
            endAnchor.Offset - startAnchor.Offset != length ||
            document.GetText(startAnchor.Offset, length) != typed) return;

        int delta = startAnchor.Offset - originalStart;
        int replaceStart = change.ReplaceStart + delta;
        int replaceEnd = endAnchor.Offset + change.ReplaceStart + change.ReplaceLength - entry.Result.Span.End;
        if (replaceStart < 0 || replaceEnd < replaceStart || replaceEnd > document.TextLength) return;
        bool caretInWord = textArea.Caret.Offset >= startAnchor.Offset && textArea.Caret.Offset <= endAnchor.Offset;
        document.Replace(replaceStart, replaceEnd - replaceStart, change.NewText);
        if (caretInWord) textArea.Caret.Offset = Math.Min(document.TextLength, replaceStart + (change.CaretOffsetInNewText ?? change.NewText.Length));
    }
}
