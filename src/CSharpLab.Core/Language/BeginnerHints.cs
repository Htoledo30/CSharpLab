using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharpLab.Core.Language;

/// <summary>
/// Dicas para armadilhas comuns que o compilador aceita sem avisar. Aparecem como avisos
/// ("dica"), nunca impedem a execução.
/// </summary>
public static class BeginnerHints
{
    public const string PrintCollectionId = "DICA01";
    public const string IntegerDivisionId = "DICA02";
    public const string ParseInputId = "DICA03";
    public const string EndlessLoopId = "DICA04";
    public const string CaseSensitiveInputId = "DICA05";

    public static IEnumerable<(string Id, string Message, Location Location)> Analyze(SemanticModel model, CancellationToken ct)
    {
        var root = model.SyntaxTree.GetRoot(ct);
        foreach (var node in root.DescendantNodes())
        {
            ct.ThrowIfCancellationRequested();
            switch (node)
            {
                case InvocationExpressionSyntax invocation when PrintsCollection(model, invocation, ct) is { } argument:
                    var name = argument.ToString();
                    yield return (PrintCollectionId,
                        $"Imprimir \"{name}\" direto mostra o nome do tipo, não os itens. Para ver os itens, use string.Join(\", \", {name}).",
                        argument.GetLocation());
                    break;
                case InvocationExpressionSyntax invocation when ParsesInputDirectly(model, invocation, ct) is { } method:
                    yield return (ParseInputId,
                        $"Se a pessoa digitar algo que não é número, {method} para o programa com erro. " +
                        $"Para conferir antes: if ({TryParseOf(method)}(Console.ReadLine(), out var number)) {{ ... }}",
                        invocation.GetLocation());
                    break;
                case BinaryExpressionSyntax { RawKind: (int)SyntaxKind.DivideExpression } division when TruncatesIntoDecimal(model, division, ct):
                    yield return (IntegerDivisionId,
                        "Divisão entre inteiros descarta as casas decimais (7 / 2 dá 3). Para ter 3.5, converta um dos lados, ex.: (double)a / b.",
                        division.GetLocation());
                    break;
                case BinaryExpressionSyntax { RawKind: (int)SyntaxKind.EqualsExpression or (int)SyntaxKind.NotEqualsExpression } comparison
                    when ComparesTypedTextWithWord(model, comparison, ct) is { } word:
                    yield return (CaseSensitiveInputId,
                        $"A comparação diferencia maiúsculas: quem digitar \"{Capitalize(word)}\" não vai ser igual a \"{word}\". " +
                        "Para aceitar os dois, compare com .ToLower() ou use string.Equals(text, \"...\", StringComparison.OrdinalIgnoreCase).",
                        comparison.GetLocation());
                    break;
                case WhileStatementSyntax { Condition: LiteralExpressionSyntax { RawKind: (int)SyntaxKind.TrueLiteralExpression } } loop
                    when !CanLeave(loop.Statement):
                    yield return (EndlessLoopId,
                        "Este loop nunca termina: não há break, return nem throw dentro dele. Se for de propósito (um jogo, por exemplo), " +
                        "o botão Parar encerra o programa.",
                        loop.WhileKeyword.GetLocation());
                    break;
            }
        }
    }

    /// <summary>Console.Write/WriteLine com um único argumento que é coleção (não string).</summary>
    private static ExpressionSyntax? PrintsCollection(SemanticModel model, InvocationExpressionSyntax invocation, CancellationToken ct)
    {
        if (invocation.ArgumentList.Arguments.Count != 1) return null;
        if (invocation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "WriteLine" or "Write" }) return null;
        if (!IsConsoleMethod(model, invocation, ct)) return null;
        var argument = invocation.ArgumentList.Arguments[0].Expression;
        var type = model.GetTypeInfo(argument, ct).Type;
        if (type == null || type.SpecialType == SpecialType.System_String) return null;
        if (type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Char }) return null; // char[] imprime o texto
        bool isCollection = type is IArrayTypeSymbol ||
                            type.AllInterfaces.Any(i => i.SpecialType == SpecialType.System_Collections_IEnumerable);
        if (!isCollection) return null;
        // Tipos que mostram algo útil no ToString() próprio não entram (ex.: string já excluída).
        return type.GetMembers("ToString").OfType<IMethodSymbol>().Any(m => m.Parameters.Length == 0 && !m.IsImplicitlyDeclared &&
                                                                          SymbolEqualityComparer.Default.Equals(m.ContainingType, type))
            ? null
            : argument;
    }

    private static bool IsConsoleMethod(SemanticModel model, InvocationExpressionSyntax invocation, CancellationToken ct) =>
        model.GetSymbolInfo(invocation, ct).Symbol is IMethodSymbol { ContainingType: { Name: "Console", ContainingNamespace.Name: "System" } };

    /// <summary>int.Parse(Console.ReadLine()) e parecidos: o número digitado vai direto para a conversão.</summary>
    private static string? ParsesInputDirectly(SemanticModel model, InvocationExpressionSyntax invocation, CancellationToken ct)
    {
        if (invocation.ArgumentList.Arguments.Count != 1) return null;
        if (invocation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Parse" } access) return null;
        if (model.GetSymbolInfo(invocation, ct).Symbol is not IMethodSymbol { ContainingType.SpecialType: var type } ||
            type is not (SpecialType.System_Int32 or SpecialType.System_Int64 or SpecialType.System_Double or SpecialType.System_Single or SpecialType.System_Decimal))
            return null;
        return IsReadLine(model, invocation.ArgumentList.Arguments[0].Expression, ct) ? access.ToString() : null;
    }

    private static string TryParseOf(string parse) => parse[..^"Parse".Length] + "TryParse";

    /// <summary>Console.ReadLine(), também com ! ou ?? "" no fim.</summary>
    private static bool IsReadLine(SemanticModel model, ExpressionSyntax expression, CancellationToken ct)
    {
        while (true)
        {
            switch (expression)
            {
                case PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } bang:
                    expression = bang.Operand;
                    continue;
                case BinaryExpressionSyntax { RawKind: (int)SyntaxKind.CoalesceExpression } coalesce:
                    expression = coalesce.Left;
                    continue;
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    continue;
                case InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "ReadLine" } } call:
                    return IsConsoleMethod(model, call, ct);
                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// resposta == "sim", onde resposta veio do teclado (variável iniciada com Console.ReadLine())
    /// e a palavra tem letras. Já usar ToLower/ToUpper não conta.
    /// </summary>
    private static string? ComparesTypedTextWithWord(SemanticModel model, BinaryExpressionSyntax comparison, CancellationToken ct)
    {
        var (other, literal) = comparison.Right is LiteralExpressionSyntax { RawKind: (int)SyntaxKind.StringLiteralExpression } right
            ? (comparison.Left, right)
            : comparison.Left is LiteralExpressionSyntax { RawKind: (int)SyntaxKind.StringLiteralExpression } left
                ? (comparison.Right, left)
                : (null, null);
        if (other is not IdentifierNameSyntax || literal == null) return null;
        var word = literal.Token.ValueText;
        if (!word.Any(char.IsLetter) || word.ToLowerInvariant() == word.ToUpperInvariant()) return null;
        if (model.GetSymbolInfo(other, ct).Symbol is not ILocalSymbol local) return null;
        foreach (var reference in local.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(ct) is VariableDeclaratorSyntax { Initializer.Value: { } value } && IsReadLine(model, value, ct))
                return word;
        }
        return null;
    }

    private static string Capitalize(string word) =>
        char.IsUpper(word[0]) ? word.ToLowerInvariant() : char.ToUpperInvariant(word[0]) + word[1..];

    /// <summary>Existe um jeito de sair do loop: break dele mesmo, return, throw ou Environment.Exit.</summary>
    private static bool CanLeave(StatementSyntax body)
    {
        foreach (var node in body.DescendantNodesAndSelf(n => n is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)))
        {
            switch (node)
            {
                case ReturnStatementSyntax or ThrowStatementSyntax or ThrowExpressionSyntax or YieldStatementSyntax { RawKind: (int)SyntaxKind.YieldBreakStatement }:
                case GotoStatementSyntax:
                    return true;
                case BreakStatementSyntax brk when BreaksOut(brk, body):
                    return true;
                case InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Exit" } }:
                    return true;
            }
        }
        return false;
    }

    /// <summary>O break sai do loop analisado (e não de um loop ou switch de dentro).</summary>
    private static bool BreaksOut(BreakStatementSyntax brk, StatementSyntax body)
    {
        for (var parent = brk.Parent; parent != null && parent != body.Parent; parent = parent.Parent)
        {
            if (parent == body) return true;
            if (parent is ForStatementSyntax or ForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax or SwitchSectionSyntax)
                return false;
        }
        return true;
    }

    /// <summary>int / int cujo resultado vai para double, float ou decimal.</summary>
    private static bool TruncatesIntoDecimal(SemanticModel model, BinaryExpressionSyntax division, CancellationToken ct)
    {
        static bool IsInteger(ITypeSymbol? t) => t?.SpecialType is SpecialType.System_Int32 or SpecialType.System_Int64 or
            SpecialType.System_Int16 or SpecialType.System_Byte or SpecialType.System_UInt32 or SpecialType.System_UInt64;
        static bool IsDecimal(ITypeSymbol? t) => t?.SpecialType is SpecialType.System_Double or SpecialType.System_Single or SpecialType.System_Decimal;

        if (!IsInteger(model.GetTypeInfo(division.Left, ct).Type) || !IsInteger(model.GetTypeInfo(division.Right, ct).Type)) return false;
        // Só quando o resultado inteiro é convertido logo em seguida para um tipo com casas decimais.
        SyntaxNode outer = division;
        while (outer.Parent is ParenthesizedExpressionSyntax) outer = outer.Parent;
        // (double)(a / b) também perde as casas: a conversão vem depois da divisão.
        if (outer.Parent is CastExpressionSyntax cast) return IsDecimal(model.GetTypeInfo(cast, ct).Type);
        var converted = model.GetTypeInfo(outer, ct).ConvertedType;
        return IsDecimal(converted);
    }
}
