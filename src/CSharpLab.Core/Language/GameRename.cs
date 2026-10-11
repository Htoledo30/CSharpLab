using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace CSharpLab.Core.Language;

/// <summary>Renomeação de cenas usando os métodos do motor, sem trocar textos do jogo ou métodos de outras classes.</summary>
public static class GameRename
{
    public sealed record Plan(IReadOnlyDictionary<string, IReadOnlyList<TextChange>> Changes,
        IReadOnlyList<string> IndirectFiles, IReadOnlyList<string> UnresolvedFiles);

    public static Plan Scene(IReadOnlyDictionary<string, string> files, string oldName, string newName, string engineAssembly)
        => Build(files, oldName, newName, engineAssembly, scene: null);

    public static Plan Piece(IReadOnlyDictionary<string, string> files, string scene, string oldName, string newName, string engineAssembly)
        => Build(files, oldName, newName, engineAssembly, scene);

    private static Plan Build(IReadOnlyDictionary<string, string> files, string oldName, string newName, string engineAssembly, string? scene)
    {
        var trees = files.Select(f => CSharpSyntaxTree.ParseText(f.Value, path: f.Key)).ToArray();
        var compilation = CSharpCompilation.Create("SceneRename", [.. trees,
            CSharpSyntaxTree.ParseText("global using CSharpLab.GameEngine; global using System;")],
            [.. Basic.Reference.Assemblies.Net100.References.All, MetadataReference.CreateFromFile(engineAssembly)],
            new CSharpCompilationOptions(OutputKind.ConsoleApplication));
        var changes = new Dictionary<string, IReadOnlyList<TextChange>>(StringComparer.OrdinalIgnoreCase);
        var indirect = new List<string>();
        var unresolved = new List<string>();
        foreach (var tree in trees)
        {
            var model = compilation.GetSemanticModel(tree);
            var edits = new List<TextChange>();
            foreach (var call in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (call.Expression is not MemberAccessExpressionSyntax member ||
                    !(scene == null ? member.Name.Identifier.ValueText is "Scene" or "GoTo" or "Start" or "Test" : member.Name.Identifier.ValueText == "Find")) continue;
                var containingScene = scene != null ? GameAssist.SceneOf(call) : null;
                if (scene != null && containingScene != null && !string.Equals(containingScene, scene, StringComparison.OrdinalIgnoreCase)) continue;
                if (model.GetOperation(call) is not IInvocationOperation operation)
                {
                    if (model.GetTypeInfo(member.Expression).Type?.ToDisplayString() == "CSharpLab.GameEngine.Game" && !unresolved.Contains(tree.FilePath))
                        unresolved.Add(tree.FilePath);
                    continue;
                }
                if (operation.TargetMethod.ContainingType.ToDisplayString() != "CSharpLab.GameEngine.Game") continue;
                var argument = operation.Arguments.FirstOrDefault(a => a.Parameter?.Ordinal == 0);
                if (argument?.Syntax is not ArgumentSyntax syntax) continue;
                var value = model.GetConstantValue(syntax.Expression);
                if (scene != null && containingScene == null)
                {
                    // Fora do bloco da cena, não sabemos qual tela usa esta peça (ex.: um método reutilizado).
                    if (!value.HasValue || value.Value is string outsideName && string.Equals(outsideName, oldName, StringComparison.OrdinalIgnoreCase))
                        if (!indirect.Contains(tree.FilePath)) indirect.Add(tree.FilePath);
                    continue;
                }
                if (syntax.Expression is not LiteralExpressionSyntax literal || !literal.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    // Uma variável pode apontar para esta cena. Não inventamos uma substituição no código da pessoa.
                    if (!value.HasValue || value.Value is string name && string.Equals(name, oldName, StringComparison.OrdinalIgnoreCase))
                        if (!indirect.Contains(tree.FilePath)) indirect.Add(tree.FilePath);
                    continue;
                }
                if (string.Equals(literal.Token.ValueText, oldName, StringComparison.OrdinalIgnoreCase))
                    edits.Add(new TextChange(literal.Span, SyntaxFactory.Literal(newName).Text));
            }
            if (edits.Count > 0) changes.Add(tree.FilePath, edits);
        }
        return new Plan(changes, indirect, unresolved);
    }
}
