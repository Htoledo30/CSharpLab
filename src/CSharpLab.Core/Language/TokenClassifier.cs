using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CSharpLab.Core.Language;

public enum TokenKind : byte
{
    Plain,
    Keyword,
    ControlKeyword,
    String,
    StringEscape,
    Number,
    Comment,
    XmlDoc,
    Preprocessor,
    Disabled,
    Type,
    Struct,
    Interface,
    Enum,
    TypeParameter,
    Method,
    Local,
    Parameter,
    Field,
    Property,
    Constant,
    EnumMember,
    Namespace,
    Label,
}

public readonly record struct ClassifiedRange(int Start, int Length, TokenKind Kind);

/// <summary>
/// Classificação léxica feita direto na árvore sintática (rápida, a cada tecla) e conversão
/// da classificação semântica do Roslyn (tipos, métodos, variáveis), aplicada por cima.
/// </summary>
public static class TokenClassifier
{
    public static void ClassifyLexical(SyntaxNode root, TextSpan span, List<ClassifiedRange> output)
    {
        if (span.Length == 0 || root.FullSpan.Length == 0) return;
        var token = root.FindToken(Math.Min(span.Start, Math.Max(0, root.FullSpan.End - 1)));
        while (!token.IsKind(SyntaxKind.None) && token.FullSpan.Start < span.End)
        {
            foreach (var trivia in token.LeadingTrivia) AddTrivia(trivia, span, output);
            if (token.Span.Length > 0 && token.Span.IntersectsWith(span))
            {
                var kind = Classify(token);
                if (kind != TokenKind.Plain) Add(token.Span, kind, span, output);
            }
            foreach (var trivia in token.TrailingTrivia) AddTrivia(trivia, span, output);
            if (token.FullSpan.End >= span.End) break;
            token = token.GetNextToken(includeZeroWidth: false, includeSkipped: true, includeDirectives: true, includeDocumentationComments: true);
        }
    }

    private static void AddTrivia(SyntaxTrivia trivia, TextSpan span, List<ClassifiedRange> output)
    {
        if (!trivia.Span.IntersectsWith(span) || trivia.Span.Length == 0) return;
        switch (trivia.Kind())
        {
            case SyntaxKind.SingleLineCommentTrivia:
            case SyntaxKind.MultiLineCommentTrivia:
                Add(trivia.Span, TokenKind.Comment, span, output);
                break;
            case SyntaxKind.SingleLineDocumentationCommentTrivia:
            case SyntaxKind.MultiLineDocumentationCommentTrivia:
                Add(trivia.Span, TokenKind.XmlDoc, span, output);
                break;
            case SyntaxKind.DisabledTextTrivia:
                Add(trivia.Span, TokenKind.Disabled, span, output);
                break;
            case SyntaxKind.SkippedTokensTrivia:
                break;
            default:
                if (trivia.IsDirective)
                    Add(trivia.Span, TokenKind.Preprocessor, span, output);
                break;
        }
    }

    private static void Add(TextSpan range, TokenKind kind, TextSpan clip, List<ClassifiedRange> output)
    {
        var start = Math.Max(range.Start, clip.Start);
        var end = Math.Min(range.End, clip.End);
        if (end > start) output.Add(new ClassifiedRange(start, end - start, kind));
    }

    public static TokenKind Classify(SyntaxToken token)
    {
        var kind = token.Kind();
        switch (kind)
        {
            case SyntaxKind.StringLiteralToken:
            case SyntaxKind.CharacterLiteralToken:
            case SyntaxKind.Utf8StringLiteralToken:
            case SyntaxKind.SingleLineRawStringLiteralToken:
            case SyntaxKind.MultiLineRawStringLiteralToken:
            case SyntaxKind.Utf8SingleLineRawStringLiteralToken:
            case SyntaxKind.Utf8MultiLineRawStringLiteralToken:
            case SyntaxKind.InterpolatedStringStartToken:
            case SyntaxKind.InterpolatedStringEndToken:
            case SyntaxKind.InterpolatedStringTextToken:
            case SyntaxKind.InterpolatedSingleLineRawStringStartToken:
            case SyntaxKind.InterpolatedMultiLineRawStringStartToken:
            case SyntaxKind.InterpolatedRawStringEndToken:
                return TokenKind.String;
            case SyntaxKind.NumericLiteralToken:
                return TokenKind.Number;
            case SyntaxKind.IfKeyword:
            case SyntaxKind.ElseKeyword:
            case SyntaxKind.SwitchKeyword:
            case SyntaxKind.CaseKeyword:
            case SyntaxKind.ForKeyword:
            case SyntaxKind.ForEachKeyword:
            case SyntaxKind.WhileKeyword:
            case SyntaxKind.DoKeyword:
            case SyntaxKind.BreakKeyword:
            case SyntaxKind.ContinueKeyword:
            case SyntaxKind.ReturnKeyword:
            case SyntaxKind.GotoKeyword:
            case SyntaxKind.ThrowKeyword:
            case SyntaxKind.TryKeyword:
            case SyntaxKind.CatchKeyword:
            case SyntaxKind.FinallyKeyword:
            case SyntaxKind.YieldKeyword:
            case SyntaxKind.AwaitKeyword:
                return TokenKind.ControlKeyword;
            case SyntaxKind.DefaultKeyword when token.Parent is DefaultSwitchLabelSyntax:
                return TokenKind.ControlKeyword;
            case SyntaxKind.IdentifierToken:
                if (token.Parent is IdentifierNameSyntax { IsVar: true } || token.Text is "nameof" && token.Parent?.Parent is InvocationExpressionSyntax)
                    return TokenKind.Keyword;
                return TokenKind.Plain;
        }
        if (SyntaxFacts.IsKeywordKind(kind) || SyntaxFacts.IsContextualKeyword(kind) || SyntaxFacts.IsPreprocessorKeyword(kind))
            return TokenKind.Keyword;
        return TokenKind.Plain;
    }

    public static TokenKind? FromClassification(string type) => type switch
    {
        ClassificationTypeNames.ClassName or ClassificationTypeNames.RecordClassName or ClassificationTypeNames.DelegateName => TokenKind.Type,
        ClassificationTypeNames.StructName or ClassificationTypeNames.RecordStructName => TokenKind.Struct,
        ClassificationTypeNames.InterfaceName => TokenKind.Interface,
        ClassificationTypeNames.EnumName => TokenKind.Enum,
        ClassificationTypeNames.TypeParameterName => TokenKind.TypeParameter,
        ClassificationTypeNames.MethodName or ClassificationTypeNames.ExtensionMethodName => TokenKind.Method,
        ClassificationTypeNames.LocalName => TokenKind.Local,
        ClassificationTypeNames.ParameterName => TokenKind.Parameter,
        ClassificationTypeNames.FieldName or ClassificationTypeNames.EventName => TokenKind.Field,
        ClassificationTypeNames.PropertyName => TokenKind.Property,
        ClassificationTypeNames.ConstantName => TokenKind.Constant,
        ClassificationTypeNames.EnumMemberName => TokenKind.EnumMember,
        ClassificationTypeNames.NamespaceName => TokenKind.Namespace,
        ClassificationTypeNames.LabelName => TokenKind.Label,
        ClassificationTypeNames.StringEscapeCharacter => TokenKind.StringEscape,
        ClassificationTypeNames.Keyword => TokenKind.Keyword,
        ClassificationTypeNames.ControlKeyword => TokenKind.ControlKeyword,
        _ => null,
    };
}
