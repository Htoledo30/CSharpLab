using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.QuickInfo;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;

namespace CSharpLab.Core.Language;

/// <summary>Onde um símbolo foi declarado: num arquivo do projeto ou no próprio .NET.</summary>
public sealed record DefinitionResult(DocumentId? DocumentId, string? FilePath, int Offset, int Line, int Column, string? ExternalName)
{
    /// <summary>Chave da aba no editor (preenchida pelo LanguageService).</summary>
    public string? DocumentKey { get; init; }
}

public sealed record RenameTarget(string Name, int Start, int Length);

/// <summary>Alterações de um renomear, por documento.</summary>
public sealed record RenameResult(IReadOnlyDictionary<DocumentId, IReadOnlyList<TextChange>> Changes, int Occurrences);

/// <summary>Alterações de um renomear, por arquivo do editor.</summary>
public sealed record RenameEdit(string DocumentKey, string? FilePath, IReadOnlyList<TextChange> Changes);

/// <summary>
/// Ajudas sobre o código: o que é um nome (dica do mouse), onde foi declarado, renomear em todos
/// os arquivos e qual "using" falta para um tipo não encontrado.
/// </summary>
/// <summary>Como completar um nome de método: com "()" e onde deixar o cursor.</summary>
public enum CallShape
{
    /// <summary>Não é chamada (não é método, ou o nome é usado como valor: += Atacar, nameof, Action a = Atacar).</summary>
    None,
    NoParameters,
    HasParameters,
}

public static class CodeAssist
{
    /// <summary>O nome que termina em <paramref name="position"/> é uma chamada de método? Com ou sem parâmetros?</summary>
    public static async Task<CallShape> GetCallShapeAsync(Document document, int position, CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
        if (root == null || model == null || position <= 0) return CallShape.None;
        if (root.FindToken(position - 1).Parent is not SimpleNameSyntax name || name.Span.End != position) return CallShape.None;

        ExpressionSyntax expression = name.Parent switch
        {
            MemberAccessExpressionSyntax access when access.Name == name => access,
            MemberBindingExpressionSyntax binding when binding.Name == name => binding,
            _ => name,
        };
        switch (expression.Parent)
        {
            case InvocationExpressionSyntax invocation when invocation.Expression == expression:
            case AssignmentExpressionSyntax assignment when assignment.Right == expression &&
                assignment.Kind() is SyntaxKind.AddAssignmentExpression or SyntaxKind.SubtractAssignmentExpression:
                return CallShape.None;
            case ArgumentSyntax { Parent.Parent: InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" } } }:
                return CallShape.None;
        }
        // O nome vai para um delegate (Action a = Atacar; lista.ForEach(Console.WriteLine)).
        if (model.GetTypeInfo(expression, ct).ConvertedType is { TypeKind: TypeKind.Delegate }) return CallShape.None;

        var methods = model.GetMemberGroup(expression, ct).OfType<IMethodSymbol>().ToList();
        if (methods.Count == 0)
        {
            var info = model.GetSymbolInfo(expression, ct);
            methods = (info.Symbol != null ? [info.Symbol] : info.CandidateSymbols.ToList()).OfType<IMethodSymbol>().ToList();
        }
        if (methods.Count == 0) return CallShape.None;
        return methods.All(m => m.Parameters.Length == 0) ? CallShape.NoParameters : CallShape.HasParameters;
    }

    public static async Task<string?> GetQuickInfoAsync(Document document, int position, CancellationToken ct)
    {
        var service = QuickInfoService.GetService(document);
        if (service == null) return null;
        var item = await service.GetQuickInfoAsync(document, position, ct).ConfigureAwait(false);
        if (item == null) return null;
        var lines = item.Sections
            .Where(s => s.Kind is QuickInfoSectionKinds.Description or "NullabilityAnalysis"
                or QuickInfoSectionKinds.TypeParameters)
            .Select(s => s.Text.Trim())
            .Where(t => t.Length > 0)
            .ToList();
        return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
    }

    private static async Task<ISymbol?> SymbolAtAsync(Document document, int position, CancellationToken ct)
    {
        var symbol = await SymbolFinder.FindSymbolAtPositionAsync(document, position, ct).ConfigureAwait(false);
        if (symbol == null && position > 0)
            symbol = await SymbolFinder.FindSymbolAtPositionAsync(document, position - 1, ct).ConfigureAwait(false);
        return symbol switch
        {
            IAliasSymbol alias => alias.Target,
            IMethodSymbol { MethodKind: MethodKind.Constructor, IsImplicitlyDeclared: true } ctor => ctor.ContainingType,
            IMethodSymbol { ReducedFrom: { } reduced } => reduced,
            _ => symbol,
        };
    }

    public static async Task<DefinitionResult?> FindDefinitionAsync(Document document, int position, CancellationToken ct)
    {
        var symbol = await SymbolAtAsync(document, position, ct).ConfigureAwait(false);
        if (symbol == null) return null;
        var location = symbol.Locations.FirstOrDefault(l => l.IsInSource);
        if (location?.SourceTree is { } tree)
        {
            var docId = document.Project.Solution.GetDocumentId(tree);
            var span = location.GetLineSpan();
            return new DefinitionResult(docId, tree.FilePath is { Length: > 0 } fp ? fp : null,
                location.SourceSpan.Start, span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1, null);
        }
        var name = (symbol is IMethodSymbol or IPropertySymbol or IFieldSymbol or IEventSymbol) && symbol.ContainingType != null
            ? symbol.ContainingType.ToDisplayString() + "." + symbol.Name
            : symbol.ToDisplayString();
        return new DefinitionResult(null, null, 0, 0, 0, name);
    }

    /// <summary>Nome que pode ser renomeado na posição (variável, método, classe… declarados no projeto).</summary>
    public static async Task<RenameTarget?> GetRenameTargetAsync(Document document, int position, CancellationToken ct)
    {
        var symbol = await SymbolAtAsync(document, position, ct).ConfigureAwait(false);
        if (symbol == null || !symbol.Locations.Any(l => l.IsInSource) || symbol.IsImplicitlyDeclared) return null;
        if (symbol is IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.Destructor or MethodKind.StaticConstructor } m)
            symbol = m.ContainingType;
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        var token = root!.FindToken(Math.Max(0, position));
        if (!token.IsKind(SyntaxKind.IdentifierToken) && position > 0) token = root.FindToken(position - 1);
        if (!token.IsKind(SyntaxKind.IdentifierToken)) return null;
        return new RenameTarget(symbol.Name, token.SpanStart, token.Span.Length);
    }

    public static async Task<(RenameResult? Result, string? Error)> RenameAsync(Document document, int position, string newName, CancellationToken ct)
    {
        newName = newName.Trim();
        if (!SyntaxFacts.IsValidIdentifier(newName) || SyntaxFacts.GetKeywordKind(newName) != SyntaxKind.None)
            return (null, $"\"{newName}\" não é um nome válido em C#.");
        var symbol = await SymbolAtAsync(document, position, ct).ConfigureAwait(false);
        if (symbol == null || !symbol.Locations.Any(l => l.IsInSource))
            return (null, "Aqui não há um nome do seu código para renomear.");
        if (symbol is IMethodSymbol { MethodKind: MethodKind.Constructor } ctor) symbol = ctor.ContainingType;
        if (symbol.Name == newName) return (new RenameResult(new Dictionary<DocumentId, IReadOnlyList<TextChange>>(), 0), null);

        var solution = document.Project.Solution;
        var renamed = await Renamer.RenameSymbolAsync(solution, symbol, new SymbolRenameOptions(), newName, ct).ConfigureAwait(false);
        var changes = new Dictionary<DocumentId, IReadOnlyList<TextChange>>();
        int occurrences = 0;
        foreach (var projectChanges in renamed.GetChanges(solution).GetProjectChanges())
        {
            foreach (var docId in projectChanges.GetChangedDocuments())
            {
                var before = solution.GetDocument(docId);
                var after = renamed.GetDocument(docId);
                if (before == null || after == null) continue;
                var list = (await after.GetTextChangesAsync(before, ct).ConfigureAwait(false)).ToList();
                if (list.Count == 0) continue;
                changes[docId] = list;
                occurrences += list.Count;
            }
        }
        return (new RenameResult(changes, occurrences), null);
    }

    private static readonly ConcurrentDictionary<string, Dictionary<string, List<string>>> TypeIndexes = new();

    /// <summary>
    /// Namespaces que têm um tipo público com esse nome nas referências do projeto
    /// (ex.: "StringBuilder" → "System.Text"), sem os que já estão em uso.
    /// </summary>
    public static async Task<IReadOnlyList<string>> FindUsingCandidatesAsync(Document document, string typeName, CancellationToken ct)
    {
        var compilation = await document.Project.GetCompilationAsync(ct).ConfigureAwait(false);
        if (compilation == null) return [];
        var indexKey = string.Join("|", compilation.References.Select(r => r.Display).Order(StringComparer.Ordinal));
        var index = TypeIndexes.GetOrAdd(indexKey, _ => BuildTypeIndex(compilation, ct));
        if (!index.TryGetValue(typeName, out var namespaces)) return [];

        var root = (CompilationUnitSyntax)(await document.GetSyntaxRootAsync(ct).ConfigureAwait(false))!;
        var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
        var imported = model!.GetImportScopes(root.Members.FirstOrDefault()?.SpanStart ?? 0, ct)
            .SelectMany(s => s.Imports)
            .Select(i => i.NamespaceOrType.ToDisplayString())
            .ToHashSet(StringComparer.Ordinal);
        return namespaces.Where(n => !imported.Contains(n)).Order(StringComparer.Ordinal).Take(5).ToList();
    }

    private static Dictionary<string, List<string>> BuildTypeIndex(Compilation compilation, CancellationToken ct)
    {
        var index = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var stack = new Stack<INamespaceSymbol>();
        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol assembly)
                stack.Push(assembly.GlobalNamespace);
        }
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var ns = stack.Pop();
            foreach (var member in ns.GetMembers())
            {
                if (member is INamespaceSymbol child)
                {
                    stack.Push(child);
                }
                else if (member is INamedTypeSymbol { DeclaredAccessibility: Accessibility.Public } type && !ns.IsGlobalNamespace)
                {
                    var nsName = ns.ToDisplayString();
                    if (!index.TryGetValue(type.Name, out var list)) index[type.Name] = list = [];
                    if (!list.Contains(nsName)) list.Add(nsName);
                }
            }
        }
        return index;
    }

    /// <summary>Alteração que acrescenta "using Ns;" junto aos usings do arquivo (ou no topo).</summary>
    public static async Task<TextChange?> AddUsingAsync(Document document, string ns, CancellationToken ct)
    {
        var root = (CompilationUnitSyntax)(await document.GetSyntaxRootAsync(ct).ConfigureAwait(false))!;
        if (root.Usings.Any(u => u.Name?.ToString() == ns)) return null;
        var text = await document.GetTextAsync(ct).ConfigureAwait(false);
        var newLine = text.ToString().Contains("\r\n") ? "\r\n" : "\n";
        var insert = $"using {ns};{newLine}";
        var after = root.Usings.LastOrDefault(u => string.CompareOrdinal(u.Name?.ToString(), ns) < 0);
        int position;
        if (after != null)
        {
            position = text.Lines.GetLineFromPosition(after.Span.End).EndIncludingLineBreak;
        }
        else if (root.Usings.Count > 0)
        {
            position = text.Lines.GetLineFromPosition(root.Usings[0].SpanStart).Start;
        }
        else
        {
            // Sem usings: no topo, separado do código por uma linha em branco.
            position = 0;
            insert += newLine;
        }
        return new TextChange(new TextSpan(position, 0), insert);
    }
}
