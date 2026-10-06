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
                case BinaryExpressionSyntax { RawKind: (int)SyntaxKind.DivideExpression } division when TruncatesIntoDecimal(model, division, ct):
                    yield return (IntegerDivisionId,
                        "Divisão entre inteiros descarta as casas decimais (7 / 2 dá 3). Para ter 3.5, converta um dos lados, ex.: (double)a / b.",
                        division.GetLocation());
                    break;
            }
        }
    }

    /// <summary>Console.Write/WriteLine com um único argumento que é coleção (não string).</summary>
    private static ExpressionSyntax? PrintsCollection(SemanticModel model, InvocationExpressionSyntax invocation, CancellationToken ct)
    {
        if (invocation.ArgumentList.Arguments.Count != 1) return null;
        if (invocation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "WriteLine" or "Write" }) return null;
        if (model.GetSymbolInfo(invocation, ct).Symbol is not IMethodSymbol { ContainingType: { Name: "Console", ContainingNamespace.Name: "System" } })
            return null;
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
