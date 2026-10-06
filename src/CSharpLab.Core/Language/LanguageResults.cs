using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Text;

namespace CSharpLab.Core.Language;

/// <summary>Resultado de um pedido de completion, preso à versão do documento que o gerou.</summary>
public sealed class CompletionResult
{
    internal CompletionResult(Document document, CompletionService service, IReadOnlyList<CompletionItem> items, TextSpan span, bool isSuggestionMode)
    {
        Document = document;
        Service = service;
        Items = items;
        Span = span;
        IsSuggestionMode = isSuggestionMode;
    }

    internal Document Document { get; }
    internal CompletionService Service { get; }
    public IReadOnlyList<CompletionItem> Items { get; }
    /// <summary>Trecho (a palavra sendo digitada) que a sugestão substitui.</summary>
    public TextSpan Span { get; }
    /// <summary>Roslyn sugere nomes novos aqui (ex.: nome de variável); não abrir a lista sozinho.</summary>
    public bool IsSuggestionMode { get; }
}

public sealed record CompletionChangeResult(int ReplaceStart, int ReplaceLength, string NewText, int? CaretOffsetInNewText);

public sealed record EditorOptions(bool UseTabs, int IndentationSize, int TabSize);
