using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharpLab.Core.Language;

/// <summary>Uma assinatura pronta para exibir, com a posição de cada parâmetro no texto.</summary>
public sealed record SignatureDisplay(string Text, IReadOnlyList<(int Start, int Length)> Parameters, bool HasParamsArray);

public sealed record SignatureHelpResult(
    IReadOnlyList<SignatureDisplay> Signatures,
    int ActiveSignature,
    int ActiveParameter,
    int ArgumentListStart,
    IReadOnlyList<int>? ActiveParameters = null);

/// <summary>Assinatura do método sendo chamado e destaque do parâmetro atual.</summary>
public static class SignatureHelp
{
    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.MinimallyQualifiedFormat
        .WithMiscellaneousOptions(SymbolDisplayFormat.MinimallyQualifiedFormat.MiscellaneousOptions
                                  | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private static readonly SymbolDisplayFormat ParameterFormat = new(
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType | SymbolDisplayParameterOptions.IncludeName |
                          SymbolDisplayParameterOptions.IncludeParamsRefOut | SymbolDisplayParameterOptions.IncludeDefaultValue |
                          SymbolDisplayParameterOptions.IncludeModifiers,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
                              SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier |
                              SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    public static async Task<SignatureHelpResult?> GetAsync(Document document, int position, CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root == null || position <= 0) return null;
        var token = root.FindToken(position - 1);

        SyntaxNode? call = null;
        ArgumentListSyntax? args = null;
        for (var node = token.Parent; node != null; node = node.Parent)
        {
            if (node is ArgumentListSyntax al &&
                al.OpenParenToken.Span.End <= position &&
                (al.CloseParenToken.IsMissing || position <= al.CloseParenToken.SpanStart) &&
                al.Parent is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax or ConstructorInitializerSyntax)
            {
                call = al.Parent;
                args = al;
                break;
            }
            if (node is StatementSyntax or MemberDeclarationSyntax or LambdaExpressionSyntax) break;
        }
        if (call == null || args == null) return null;

        var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
        if (model == null) return null;

        List<IMethodSymbol> methods;
        switch (call)
        {
            case InvocationExpressionSyntax inv:
                methods = model.GetMemberGroup(inv.Expression, ct).OfType<IMethodSymbol>().ToList();
                break;
            case BaseObjectCreationExpressionSyntax creation:
                var type = model.GetTypeInfo(creation, ct).Type as INamedTypeSymbol;
                methods = type?.InstanceConstructors
                    .Where(c => !c.IsImplicitlyDeclared || c.Parameters.Length > 0 || type.InstanceConstructors.Length == 1)
                    .Where(c => model.IsAccessible(position, c))
                    .ToList() ?? [];
                break;
            case ConstructorInitializerSyntax init:
                methods = model.GetMemberGroup(init, ct).OfType<IMethodSymbol>().ToList();
                break;
            default:
                return null;
        }
        if (methods.Count == 0) return null;
        methods = methods.Distinct<IMethodSymbol>(SymbolEqualityComparer.Default)
            .OrderBy(m => m.Parameters.Length)
            .ToList();

        int argIndex = args.Arguments.GetSeparators().Count(s => s.SpanStart < position);

        var chosen = model.GetSymbolInfo(call, ct) is var info && info.Symbol is IMethodSymbol s ? s : null;
        int active = chosen != null ? methods.FindIndex(m => SymbolEqualityComparer.Default.Equals(m, chosen)) : -1;
        var namedArguments = args.Arguments.Where(a => a.NameColon != null)
            .Select(a => a.NameColon!.Name.Identifier.ValueText).ToList();
        if (active < 0)
            active = methods.FindIndex(m => (m.Parameters.Length > argIndex || m.Parameters.LastOrDefault()?.IsParams == true) &&
                namedArguments.All(name => m.Parameters.Any(p => p.Name == name)));
        if (active < 0) active = 0;

        var displays = methods.Select(Display).ToList();
        var argument = args.Arguments.ElementAtOrDefault(argIndex);
        var activeParameters = methods.Select(m => argument?.NameColon is { } named
            ? m.Parameters.IndexOf(m.Parameters.FirstOrDefault(p => p.Name == named.Name.Identifier.ValueText)!)
            : argIndex).ToList();
        return new SignatureHelpResult(displays, active, activeParameters[active], args.OpenParenToken.SpanStart, activeParameters);
    }

    private static SignatureDisplay Display(IMethodSymbol method)
    {
        var sb = new StringBuilder();
        if (method.MethodKind == MethodKind.Constructor)
        {
            sb.Append(method.ContainingType.ToDisplayString(TypeFormat));
        }
        else
        {
            sb.Append(method.ReturnsVoid ? "void" : method.ReturnType.ToDisplayString(TypeFormat)).Append(' ');
            if (method.ContainingType != null && method.MethodKind != MethodKind.LocalFunction &&
                !method.ContainingType.IsImplicitClass && method.ContainingType.Name != "Program")
            {
                sb.Append(method.ContainingType.ToDisplayString(TypeFormat)).Append('.');
            }
            sb.Append(method.Name);
            if (method.IsGenericMethod)
                sb.Append('<').Append(string.Join(", ", method.TypeArguments.Select(t => t.ToDisplayString(TypeFormat)))).Append('>');
        }

        sb.Append('(');
        var spans = new List<(int, int)>();
        for (int i = 0; i < method.Parameters.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            var text = method.Parameters[i].ToDisplayString(ParameterFormat);
            spans.Add((sb.Length, text.Length));
            sb.Append(text);
        }
        sb.Append(')');
        return new SignatureDisplay(sb.ToString(), spans, method.Parameters.LastOrDefault()?.IsParams == true);
    }

    /// <summary>Verdadeiro se digitar este caractere deve abrir ou atualizar a assinatura.</summary>
    public static bool IsTriggerCharacter(char c) => c is '(' or ',';
}
