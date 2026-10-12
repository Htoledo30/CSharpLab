using CSharpLab.Core.Language;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Microsoft.CodeAnalysis.Text;

namespace CSharpLab.Editor;

public sealed class SemanticSegment : TextSegment
{
    public TokenKind Kind { get; init; }
}

/// <summary>
/// Pinta cada linha visível: primeiro pela árvore sintática (imediato, a cada tecla),
/// depois aplica por cima a classificação semântica do Roslyn (tipos, métodos, variáveis).
/// </summary>
public sealed class CSharpColorizer : DocumentColorizingTransformer, IDisposable
{
    private readonly SyntaxCache _syntax;
    private readonly TextDocument _document;
    private readonly List<ClassifiedRange> _scratch = new(64);

    public CSharpColorizer(SyntaxCache syntax, TextDocument document)
    {
        _syntax = syntax;
        _document = document;
        Semantic = new TextSegmentCollection<SemanticSegment>(document);
        document.Changed += OnChanged;
    }

    private void OnChanged(object? sender, DocumentChangeEventArgs e)
    {
        // Classificação semântica do trecho editado fica obsoleta até a próxima análise.
        var start = Math.Max(0, e.Offset - 1);
        foreach (var s in Semantic.FindOverlappingSegments(start, e.InsertionLength + 2).ToList())
            Semantic.Remove(s);
        // Comentário de bloco, aspas, @, $, # ou \ podem mudar a cor de outras linhas além da editada.
        if (!NeedsFullRedraw && (ChangesOtherLines(e.InsertedText) || ChangesOtherLines(e.RemovedText)))
            NeedsFullRedraw = true;
    }

    private static bool ChangesOtherLines(ITextSource text)
    {
        for (int i = 0; i < text.TextLength; i++)
        {
            if (text.GetCharAt(i) is '/' or '*' or '"' or '\'' or '@' or '$' or '#' or '\\') return true;
        }
        return false;
    }

    /// <summary>Alguma edição desde a última classificação pode ter mudado a cor de outras linhas.</summary>
    public bool NeedsFullRedraw { get; set; }

    public void Dispose() => _document.Changed -= OnChanged;

    public TextSegmentCollection<SemanticSegment> Semantic { get; }

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Troca a classificação semântica. Devolve o trecho cujas cores mudaram (null se nada mudou),
    /// para redesenhar só ele: depois de uma tecla, em geral é só a linha editada.
    /// </summary>
    public (int Start, int End)? SetSemantic(IReadOnlyList<ClassifiedRange> ranges, int documentLength)
    {
        var changed = new HashSet<(int, int, TokenKind)>(Semantic.Select(s => (s.StartOffset, s.Length, s.Kind)));
        var fresh = new HashSet<(int, int, TokenKind)>();
        Semantic.Clear();
        foreach (var r in ranges)
        {
            if (r.Start + r.Length > documentLength) continue;
            Semantic.Add(new SemanticSegment { StartOffset = r.Start, Length = r.Length, Kind = r.Kind });
            fresh.Add((r.Start, r.Length, r.Kind));
        }
        changed.SymmetricExceptWith(fresh);
        if (changed.Count == 0) return null;
        return (changed.Min(c => c.Item1), changed.Max(c => c.Item1 + c.Item2));
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (!Enabled || line.Length == 0) return;

        _scratch.Clear();
        try
        {
            TokenClassifier.ClassifyLexical(_syntax.Root, new TextSpan(line.Offset, line.Length), _scratch);
        }
        catch
        {
            return;
        }

        foreach (var r in _scratch)
            Paint(r.Start, r.Start + r.Length, r.Kind);

        foreach (var s in Semantic.FindOverlappingSegments(line.Offset, line.Length))
        {
            // Palavras-chave semânticas (ex.: var) só substituem texto comum.
            if (s.Kind is TokenKind.Keyword or TokenKind.ControlKeyword && _scratch.Any(r => r.Start <= s.StartOffset && r.Start + r.Length > s.StartOffset))
                continue;
            Paint(Math.Max(s.StartOffset, line.Offset), Math.Min(s.EndOffset, line.EndOffset), s.Kind);
        }
    }

    private void Paint(int start, int end, TokenKind kind)
    {
        if (end <= start || kind == TokenKind.Plain) return;
        var brush = SyntaxTheme.For(kind);
        ChangeLinePart(start, end, el => el.TextRunProperties.SetForegroundBrush(brush));
    }
}
