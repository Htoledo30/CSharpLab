using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CSharpLab.Core.Language;

[Flags]
public enum CodeContext
{
    None = 0,
    /// <summary>Dentro de um bloco de método: instruções.</summary>
    Statement = 1,
    /// <summary>Corpo de uma classe/struct: membros.</summary>
    Member = 2,
    /// <summary>Onde uma declaração de tipo é permitida.</summary>
    Type = 4,
    /// <summary>Onde uma expressão é esperada.</summary>
    Expression = 8,
}

/// <summary>Consultas rápidas sobre a árvore sintática, usadas a cada tecla.</summary>
public static class SyntaxContext
{
    /// <summary>Verdadeiro se a posição está dentro de um comentário, string, char ou diretiva.</summary>
    public static bool IsInCommentOrString(SyntaxNode root, int position)
    {
        if (position <= 0) return false;
        var trivia = root.FindTrivia(position - 1 < 0 ? 0 : position - 1, findInsideTrivia: true);
        if (IsCommentTrivia(trivia) && position > trivia.SpanStart)
        {
            // Depois do fim de um comentário de linha ainda é comentário até a quebra.
            if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) || trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia))
                return true;
            if (position < trivia.Span.End || !trivia.ToString().EndsWith("*/", StringComparison.Ordinal))
                return true;
        }

        var token = root.FindToken(position, findInsideTrivia: true);
        if (token.Parent is { } p && p.FirstAncestorOrSelf<StructuredTriviaSyntax>() is { } st &&
            (st is DocumentationCommentTriviaSyntax || st is DirectiveTriviaSyntax))
        {
            return position > st.SpanStart;
        }

        if (IsStringLike(token) && position > token.SpanStart && (position < token.Span.End || !IsClosed(token)))
            return true;

        // Texto literal dentro de uma string interpolada: $"abc|def".
        if (token.IsKind(SyntaxKind.InterpolatedStringTextToken) && position >= token.SpanStart && position <= token.Span.End)
            return true;
        if (token.IsKind(SyntaxKind.InterpolatedStringEndToken) && position == token.SpanStart &&
            token.Parent is InterpolatedStringExpressionSyntax)
        {
            return true;
        }
        var left = root.FindToken(Math.Max(0, position - 1));
        if (left.IsKind(SyntaxKind.InterpolatedStringTextToken) && position <= left.Span.End)
            return true;
        if (left.IsKind(SyntaxKind.InterpolatedStringStartToken) && position >= left.Span.End)
            return true;
        return false;
    }

    private static bool IsCommentTrivia(SyntaxTrivia t) =>
        t.IsKind(SyntaxKind.SingleLineCommentTrivia) || t.IsKind(SyntaxKind.MultiLineCommentTrivia) ||
        t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) || t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia) ||
        t.IsKind(SyntaxKind.DisabledTextTrivia);

    private static bool IsStringLike(SyntaxToken t) =>
        t.IsKind(SyntaxKind.StringLiteralToken) || t.IsKind(SyntaxKind.CharacterLiteralToken) ||
        t.IsKind(SyntaxKind.Utf8StringLiteralToken) || t.IsKind(SyntaxKind.SingleLineRawStringLiteralToken) ||
        t.IsKind(SyntaxKind.MultiLineRawStringLiteralToken) || t.IsKind(SyntaxKind.Utf8SingleLineRawStringLiteralToken) ||
        t.IsKind(SyntaxKind.Utf8MultiLineRawStringLiteralToken);

    private static bool IsClosed(SyntaxToken t)
    {
        var text = t.Text;
        if (t.IsKind(SyntaxKind.CharacterLiteralToken))
            return text.Length >= 2 && text.EndsWith('\'') && !(text.Length == 2 && text == "'\\'");
        if (text.Length < 2 || !text.EndsWith('"')) return false;
        if (t.IsKind(SyntaxKind.StringLiteralToken) && !text.StartsWith('@'))
        {
            // "abc\" não está fechada: a última aspa foi escapada.
            int backslashes = 0;
            for (int i = text.Length - 2; i >= 1 && text[i] == '\\'; i--) backslashes++;
            return backslashes % 2 == 0;
        }
        return !t.ContainsDiagnostics;
    }

    /// <summary>Que tipo de código cabe na posição em que uma palavra começa.</summary>
    public static CodeContext GetContext(SyntaxNode root, int wordStart)
    {
        if (IsInCommentOrString(root, wordStart)) return CodeContext.None;

        var prev = TokenLeftOf(root, wordStart);
        if (prev.IsKind(SyntaxKind.None) || prev.Span.End > wordStart)
            return TopLevelOf(root);

        switch (prev.Kind())
        {
            case SyntaxKind.OpenBraceToken:
                return AfterOpenBrace(prev.Parent);
            case SyntaxKind.CloseBraceToken:
                return AfterNode(prev.Parent);
            case SyntaxKind.SemicolonToken when prev.Parent is ForStatementSyntax:
                return CodeContext.Expression;
            case SyntaxKind.SemicolonToken:
                return AfterNode(prev.Parent);
            case SyntaxKind.CloseParenToken when prev.Parent is IfStatementSyntax or WhileStatementSyntax or ForStatementSyntax
                or CommonForEachStatementSyntax or UsingStatementSyntax or LockStatementSyntax or FixedStatementSyntax:
                return CodeContext.Statement;
            case SyntaxKind.ElseKeyword:
            case SyntaxKind.DoKeyword:
                return CodeContext.Statement;
            case SyntaxKind.ColonToken when prev.Parent is SwitchLabelSyntax or LabeledStatementSyntax:
                return CodeContext.Statement;
            case SyntaxKind.CloseBracketToken when prev.Parent is AttributeListSyntax:
                return CodeContext.Member | CodeContext.Type;
            case SyntaxKind.EqualsToken:
            case SyntaxKind.OpenParenToken:
            case SyntaxKind.CommaToken:
            case SyntaxKind.ReturnKeyword:
            case SyntaxKind.EqualsGreaterThanToken:
            case SyntaxKind.PlusToken:
            case SyntaxKind.MinusToken:
            case SyntaxKind.AsteriskToken:
            case SyntaxKind.SlashToken:
            case SyntaxKind.PercentToken:
            case SyntaxKind.QuestionToken:
            case SyntaxKind.ColonToken:
            case SyntaxKind.OpenBracketToken:
            case SyntaxKind.ExclamationToken:
            case SyntaxKind.AmpersandAmpersandToken:
            case SyntaxKind.BarBarToken:
            case SyntaxKind.EqualsEqualsToken:
            case SyntaxKind.ExclamationEqualsToken:
            case SyntaxKind.LessThanEqualsToken:
            case SyntaxKind.GreaterThanEqualsToken:
            case SyntaxKind.PlusEqualsToken:
            case SyntaxKind.MinusEqualsToken:
            case SyntaxKind.QuestionQuestionToken:
            case SyntaxKind.InKeyword:
            case SyntaxKind.AwaitKeyword:
                return CodeContext.Expression;
            case SyntaxKind.LessThanToken when prev.Parent is BinaryExpressionSyntax:
            case SyntaxKind.GreaterThanToken when prev.Parent is BinaryExpressionSyntax:
                return CodeContext.Expression;
        }
        return CodeContext.None;
    }

    /// <summary>Último token que termina antes de <paramref name="position"/>.</summary>
    public static SyntaxToken TokenLeftOf(SyntaxNode root, int position)
    {
        if (position <= 0) return default;
        var token = root.FindToken(Math.Min(position, root.FullSpan.End - 1 < 0 ? 0 : root.FullSpan.End - 1));
        if (token.SpanStart >= position || token.IsKind(SyntaxKind.EndOfFileToken))
            token = token.GetPreviousToken();
        return token;
    }

    private static CodeContext TopLevelOf(SyntaxNode root)
    {
        var cu = root as CompilationUnitSyntax;
        bool hasNamespace = cu?.Members.Any(m => m is BaseNamespaceDeclarationSyntax) == true;
        return hasNamespace ? CodeContext.Type : CodeContext.Statement | CodeContext.Type;
    }

    private static CodeContext AfterOpenBrace(SyntaxNode? owner) => owner switch
    {
        BlockSyntax => CodeContext.Statement,
        SwitchSectionSyntax => CodeContext.Statement,
        TypeDeclarationSyntax => CodeContext.Member | CodeContext.Type,
        BaseNamespaceDeclarationSyntax => CodeContext.Type,
        InitializerExpressionSyntax or AnonymousObjectCreationExpressionSyntax => CodeContext.Expression,
        InterpolationSyntax => CodeContext.Expression,
        _ => CodeContext.None,
    };

    /// <summary>Contexto logo depois do fim de <paramref name="node"/> (fechado por ";" ou "}").</summary>
    private static CodeContext AfterNode(SyntaxNode? node)
    {
        while (node != null)
        {
            switch (node)
            {
                case BlockSyntax block when block.Parent is BlockSyntax or SwitchSectionSyntax:
                    return CodeContext.Statement;
                case StatementSyntax when node.Parent is BlockSyntax or SwitchSectionSyntax or LabeledStatementSyntax:
                    return CodeContext.Statement;
                case StatementSyntax when node.Parent is GlobalStatementSyntax:
                    return CodeContext.Statement | CodeContext.Type;
                case StatementSyntax when node.Parent is IfStatementSyntax or ElseClauseSyntax or WhileStatementSyntax
                    or ForStatementSyntax or CommonForEachStatementSyntax or UsingStatementSyntax or LockStatementSyntax:
                    node = node.Parent;
                    continue;
                case MemberDeclarationSyntax member when member.Parent is TypeDeclarationSyntax:
                    return CodeContext.Member | CodeContext.Type;
                case MemberDeclarationSyntax member when member.Parent is BaseNamespaceDeclarationSyntax:
                    return CodeContext.Type;
                case MemberDeclarationSyntax member when member.Parent is CompilationUnitSyntax:
                    // Depois de um tipo, instruções de nível superior não são mais permitidas.
                    return member is GlobalStatementSyntax ? CodeContext.Statement | CodeContext.Type : CodeContext.Type;
                case UsingDirectiveSyntax or ExternAliasDirectiveSyntax:
                    return TopLevelOf(node.SyntaxTree.GetRoot());
                case FileScopedNamespaceDeclarationSyntax:
                    return CodeContext.Type;
                case AccessorDeclarationSyntax:
                case AccessorListSyntax:
                    node = node.Parent;
                    continue;
                case ExpressionSyntax or ArgumentListSyntax or VariableDeclarationSyntax or EqualsValueClauseSyntax:
                    node = node.Parent;
                    continue;
            }
            node = node.Parent;
        }
        return CodeContext.None;
    }

    /// <summary>Posição do delimitador que faz par com o que está em <paramref name="offset"/>, se houver.</summary>
    public static (int Open, int Close)? FindBracePair(SyntaxNode root, int offset)
    {
        foreach (var pos in new[] { offset, offset - 1 })
        {
            if (pos < 0 || pos >= root.FullSpan.End) continue;
            var token = root.FindToken(pos);
            if (token.SpanStart != pos || token.Span.Length != 1) continue;
            var kind = token.Kind();
            var (open, close) = kind switch
            {
                SyntaxKind.OpenBraceToken or SyntaxKind.CloseBraceToken => (SyntaxKind.OpenBraceToken, SyntaxKind.CloseBraceToken),
                SyntaxKind.OpenParenToken or SyntaxKind.CloseParenToken => (SyntaxKind.OpenParenToken, SyntaxKind.CloseParenToken),
                SyntaxKind.OpenBracketToken or SyntaxKind.CloseBracketToken => (SyntaxKind.OpenBracketToken, SyntaxKind.CloseBracketToken),
                _ => (SyntaxKind.None, SyntaxKind.None),
            };
            if (open == SyntaxKind.None || token.Parent == null) continue;

            var siblings = token.Parent.ChildTokens().ToList();
            var other = kind == open
                ? siblings.FirstOrDefault(t => t.IsKind(close) && t.SpanStart > token.SpanStart)
                : siblings.LastOrDefault(t => t.IsKind(open) && t.SpanStart < token.SpanStart);
            if (other.IsKind(SyntaxKind.None) || other.IsMissing) continue;
            return kind == open ? (token.SpanStart, other.SpanStart) : (other.SpanStart, token.SpanStart);
        }
        return null;
    }

    /// <summary>Indentação (em colunas) da linha onde está o "{" que corresponde ao "}" em <paramref name="closeBrace"/>.</summary>
    public static int? OpeningLineIndent(SyntaxNode root, SourceText text, int closeBrace)
    {
        var pair = FindBracePair(root, closeBrace);
        if (pair == null) return null;
        var line = text.Lines.GetLineFromPosition(pair.Value.Open);
        int col = 0;
        for (int i = line.Start; i < line.End; i++)
        {
            var c = text[i];
            if (c == ' ') col++;
            else if (c == '\t') col += 4 - col % 4;
            else break;
        }
        return col;
    }
}
