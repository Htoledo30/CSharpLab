using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Text;

namespace CSharpLab.Core.Language;

/// <summary>
/// Formata só um trecho do código, na hora (ao digitar ";" ou "}" e ao colar), com as opções do arquivo
/// (.editorconfig). Trabalha sobre a árvore que o editor já tem, sem esperar o Roslyn em segundo plano.
/// </summary>
public sealed class SpanFormatter
{
    private readonly Workspace _workspace;
    private readonly OptionSet _options;

    internal SpanFormatter(Workspace workspace, OptionSet options)
    {
        _workspace = workspace;
        _options = options;
    }

    /// <summary>Alterações (só espaços e quebras de linha) para deixar o trecho formatado.</summary>
    public IReadOnlyList<TextChange> Format(SyntaxNode root, TextSpan span, string newLine)
    {
        if (span.IsEmpty || span.End > root.FullSpan.End) return [];
#pragma warning disable CS0618 // OptionSet continua sendo a forma pública de ajustar opções por chamada.
        var options = _options.WithChangedOption(FormattingOptions.NewLine, LanguageNames.CSharp, newLine);
        return [.. Formatter.GetFormattedTextChanges(root, span, _workspace, options)];
#pragma warning restore CS0618
    }

    /// <summary>
    /// O que formatar depois de digitar ";" ou "}" na posição <paramref name="offset"/>: a instrução que o ";"
    /// termina, ou o bloco inteiro (com o if/for/método/lambda dele) que o "}" fecha. Null se não é o caso
    /// (";" dentro do cabeçalho do for, em comentário ou texto, "}" de uma interpolação…).
    /// </summary>
    public static TextSpan? SpanToFormatAfter(SyntaxNode root, int offset)
    {
        if (offset < 0 || offset >= root.FullSpan.End) return null;
        var token = root.FindToken(offset);
        if (token.SpanStart != offset || token.IsMissing) return null;
        SyntaxNode? node = token.Parent;
        if (node == null) return null;

        if (token.IsKind(SyntaxKind.SemicolonToken))
        {
            // O ";" precisa terminar o nó: no for (int i = 0; i < 10; i++) ele separa partes do cabeçalho.
            if (node.GetLastToken() != token || node is ForStatementSyntax) return null;
            return TextSpan.FromBounds(node.SpanStart, token.Span.End);
        }
        if (token.IsKind(SyntaxKind.CloseBraceToken))
        {
            if (node is InterpolationSyntax) return null;
            // Sobe enquanto o "}" é o fim do nó: o bloco de um if vira o if inteiro; o de um método, o método.
            while (node.Parent is { } parent && parent.GetLastToken() == token &&
                   parent is not (CompilationUnitSyntax or GlobalStatementSyntax or BaseNamespaceDeclarationSyntax))
            {
                node = parent;
            }
            return TextSpan.FromBounds(node.SpanStart, token.Span.End);
        }
        return null;
    }
}
