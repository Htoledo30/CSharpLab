using CSharpLab.Core.Settings;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;


namespace CSharpLab.Core.Language;

// Consultas sobre o código: sugestões, assinatura, dica do mouse, definição, renomear, usings,
// formatação, cores semânticas e diagnósticos. Todas leem snapshots imutáveis da solução.
public sealed partial class LanguageService
{
    public Document? GetDocument(string key)
    {
        lock (_gate)
            return _documents.TryGetValue(key, out var id) ? _solution.GetDocument(id) : null;
    }

    public async Task<CompletionResult?> GetCompletionsAsync(string key, int position, char? typedChar, CancellationToken ct)
    {
        var document = GetDocument(key);
        if (document == null) return null;
        var service = CompletionService.GetService(document);
        if (service == null) return null;

        var trigger = typedChar is { } c ? CompletionTrigger.CreateInsertionTrigger(c) : CompletionTrigger.Invoke;
        if (typedChar != null)
        {
            var text = await document.GetTextAsync(ct).ConfigureAwait(false);
            if (!service.ShouldTriggerCompletion(text, position, trigger))
                return null;
        }

        var list = await service.GetCompletionsAsync(document, position, trigger, cancellationToken: ct).ConfigureAwait(false);
        if (list.ItemsList.Count == 0) return null;
        IReadOnlyList<CompletionItem> items = list.ItemsList;
        // Em "game." (e nas peças e cores do motor), Equals, GetHashCode, GetType e ToString só atrapalham quem está começando.
        if (await IsGameEngineMemberAccessAsync(document, list.Span.Start, ct).ConfigureAwait(false))
            items = items.Where(i => !ObjectMembers.Contains(i.DisplayText)).ToList();
        if (items.Count == 0) return null;
        return new CompletionResult(document, service, items, list.Span, list.SuggestionModeItem != null);
    }

    /// <summary>Um nome visível naquele ponto do código e quase igual ao que foi escrito (até 2 letras de diferença).</summary>
    private static string? SimilarName(SemanticModel model, TextSpan span)
    {
        try
        {
            var written = model.SyntaxTree.GetText().ToString(span);
            if (written.Length < 3) return null;
            int limit = written.Length <= 4 ? 1 : 2;
            return model.LookupSymbols(span.Start)
                .Select(s => s.Name)
                .Where(n => n.Length > 0 && n != written && char.IsLetter(n[0]))
                .Distinct()
                .Select(n => (Name: n, Distance: GameAssist.Distance(n.ToLowerInvariant(), written.ToLowerInvariant())))
                .Where(c => c.Distance <= limit)
                .OrderBy(c => c.Distance)
                .ThenBy(c => c.Name.Length)
                .Select(c => c.Name)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static readonly HashSet<string> ObjectMembers = new(StringComparer.Ordinal)
    {
        "Equals", "GetHashCode", "GetType", "ToString", "ReferenceEquals", "MemberwiseClone",
    };

    /// <summary>A sugestão é de um membro (depois do ".") de algo do motor dos jogos?</summary>
    private static async Task<bool> IsGameEngineMemberAccessAsync(Document document, int start, CancellationToken ct)
    {
        try
        {
            var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            if (root == null || start <= 0) return false;
            var dot = root.FindToken(start - 1);
            if (!dot.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.DotToken) ||
                dot.Parent is not Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax access)
                return false;
            var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
            if (model == null) return false;
            var type = model.GetTypeInfo(access.Expression, ct).Type ?? model.GetSymbolInfo(access.Expression, ct).Symbol as ITypeSymbol;
            return type?.ContainingNamespace?.ToDisplayString() == Projects.GameKit.Namespace;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }

    public static async Task<CompletionChangeResult?> GetCompletionChangeAsync(CompletionResult result, CompletionItem item, CancellationToken ct)
    {
        var change = await result.Service.GetChangeAsync(result.Document, item, commitCharacter: null, ct).ConfigureAwait(false);
        var tc = change.TextChange;
        int? caret = change.NewPosition is { } np ? np - tc.Span.Start : null;
        if (caret is < 0 || caret > (tc.NewText?.Length ?? 0)) caret = null;
        return new CompletionChangeResult(tc.Span.Start, tc.Span.Length, tc.NewText ?? "", caret);
    }

    public static async Task<string?> GetDescriptionAsync(CompletionResult result, CompletionItem item, CancellationToken ct)
    {
        var description = await result.Service.GetDescriptionAsync(result.Document, item, ct).ConfigureAwait(false);
        var text = description?.Text;
        if (string.IsNullOrWhiteSpace(text)) return null;
        // Só a primeira linha (assinatura); documentação extensa não entra.
        var firstLine = text.Split('\n')[0].Trim();
        if (firstLine.Length == 0) return null;
        // Para os métodos comuns e o motor dos jogos, a explicação curta em português embaixo.
        return PortugueseDocs.ForSignature(firstLine) is { } doc ? firstLine + "\n" + doc.Text : firstLine;
    }

    public async Task<SignatureHelpResult?> GetSignatureHelpAsync(string key, int position, CancellationToken ct)
    {
        var document = GetDocument(key);
        return document == null ? null : await SignatureHelp.GetAsync(document, position, ct).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- ajudas sobre o código

    private string? KeyOf(DocumentId id)
    {
        lock (_gate)
        {
            if (_generated.Contains(id)) return null;
            foreach (var (key, docId) in _documents)
            {
                if (docId == id) return key;
            }
            return null;
        }
    }

    public async Task<CallShape> GetCallShapeAsync(string key, int position, CancellationToken ct)
    {
        var document = GetDocument(key);
        return document == null ? CallShape.None : await CodeAssist.GetCallShapeAsync(document, position, ct).ConfigureAwait(false);
    }

    public async Task<QuickInfoResult?> GetQuickInfoAsync(string key, int position, CancellationToken ct)
    {
        var document = GetDocument(key);
        return document == null ? null : await CodeAssist.GetQuickInfoAsync(document, position, ct).ConfigureAwait(false);
    }

    public async Task<DefinitionResult?> FindDefinitionAsync(string key, int position, CancellationToken ct)
    {
        var document = GetDocument(key);
        if (document == null) return null;
        var result = await CodeAssist.FindDefinitionAsync(document, position, ct).ConfigureAwait(false);
        if (result?.DocumentId is { } id)
        {
            var target = KeyOf(id);
            // Declaração num arquivo gerado (ex.: usings implícitos): não há para onde ir.
            if (target == null) return result with { DocumentId = null, ExternalName = "um arquivo gerado pelo projeto" };
            return result with { DocumentKey = target };
        }
        return result;
    }

    public async Task<RenameTarget?> GetRenameTargetAsync(string key, int position, CancellationToken ct)
    {
        var document = GetDocument(key);
        return document == null ? null : await CodeAssist.GetRenameTargetAsync(document, position, ct).ConfigureAwait(false);
    }

    /// <summary>Renomeia o símbolo em todos os arquivos do projeto; as alterações vêm por aba/arquivo.</summary>
    public async Task<(IReadOnlyList<RenameEdit> Edits, int Occurrences, string? Error)> RenameAsync(string key, int position, string newName, CancellationToken ct)
    {
        var document = GetDocument(key);
        if (document == null) return ([], 0, "Os serviços de C# ainda não estão prontos para este arquivo.");
        var (result, error) = await CodeAssist.RenameAsync(document, position, newName, ct).ConfigureAwait(false);
        if (result == null) return ([], 0, error);
        var edits = new List<RenameEdit>();
        foreach (var (id, changes) in result.Changes)
        {
            var target = KeyOf(id);
            if (target == null) continue;
            edits.Add(new RenameEdit(target, document.Project.Solution.GetDocument(id)?.FilePath, changes));
        }
        return (edits, edits.Sum(e => e.Changes.Count), null);
    }

    public async Task<IReadOnlyList<string>> FindUsingCandidatesAsync(string key, string typeName, CancellationToken ct)
    {
        var document = GetDocument(key);
        return document == null ? [] : await CodeAssist.FindUsingCandidatesAsync(document, typeName, ct).ConfigureAwait(false);
    }

    public async Task<TextChange?> AddUsingAsync(string key, string ns, CancellationToken ct)
    {
        var document = GetDocument(key);
        return document == null ? null : await CodeAssist.AddUsingAsync(document, ns, ct).ConfigureAwait(false);
    }

    public async Task<EditorOptions> GetEditorOptionsAsync(string key, CancellationToken ct)
    {
        var document = GetDocument(key);
        if (document == null) return new EditorOptions(false, 4, 4);
#pragma warning disable CS0618
        var options = await document.GetOptionsAsync(ct).ConfigureAwait(false);
        return new EditorOptions(options.GetOption(FormattingOptions.UseTabs, LanguageNames.CSharp),
            Math.Clamp(options.GetOption(FormattingOptions.IndentationSize, LanguageNames.CSharp), 1, 16),
            Math.Clamp(options.GetOption(FormattingOptions.TabSize, LanguageNames.CSharp), 1, 16));
#pragma warning restore CS0618
    }

    /// <summary>Alterações do formatador do Roslyn (respeita .editorconfig e o final de linha do arquivo).</summary>
    public async Task<IReadOnlyList<TextChange>> FormatAsync(string key, CancellationToken ct)
    {
        var document = GetDocument(key);
        if (document == null) return [];
        var text = await document.GetTextAsync(ct).ConfigureAwait(false);
        var newLine = Files.TextFileIO.DetectLineEnding(text.ToString()) == "LF" ? "\n" : "\r\n";

#pragma warning disable CS0618 // OptionSet continua sendo a forma pública de ajustar opções por chamada.
        var options = await document.GetOptionsAsync(ct).ConfigureAwait(false);
        var adjusted = options.WithChangedOption(FormattingOptions.NewLine, LanguageNames.CSharp, newLine);
        var formatted = await Formatter.FormatAsync(document, adjusted, ct).ConfigureAwait(false);
#pragma warning restore CS0618
        var changes = await formatted.GetTextChangesAsync(document, ct).ConfigureAwait(false);
        return changes.ToList();
    }

    public async Task<IReadOnlyList<ClassifiedRange>> ClassifySemanticAsync(string key, TextSpan span, CancellationToken ct)
    {
        var document = GetDocument(key);
        if (document == null) return [];
        var spans = await Classifier.GetClassifiedSpansAsync(document, span, ct).ConfigureAwait(false);
        var result = new List<ClassifiedRange>();
        foreach (var s in spans)
        {
            if (ClassificationTypeNames.AdditiveTypeNames.Contains(s.ClassificationType)) continue;
            if (TokenClassifier.FromClassification(s.ClassificationType) is { } kind)
                result.Add(new ClassifiedRange(s.TextSpan.Start, s.TextSpan.Length, kind));
        }
        return result;
    }

    /// <summary>Diagnósticos do projeto principal e dos documentos soltos abertos.</summary>
    public async Task<DiagnosticsSnapshot> GetDiagnosticsAsync(CancellationToken ct)
    {
        Solution solution;
        Dictionary<string, int> versions;
        Dictionary<DocumentId, string> keysById;
        HashSet<DocumentId> generated;
        List<ProjectId> projects;
        ProjectId? mainProject;
        Projects.ProjectModel? mainModel;
        lock (_gate)
        {
            mainProject = _mainProject;
            mainModel = _mainModel;
            solution = _solution;
            versions = _open.ToDictionary(o => o.Key, o => o.Value.Version, StringComparer.OrdinalIgnoreCase);
            keysById = _documents.ToDictionary(d => d.Value, d => d.Key);
            generated = [.. _generated];
            projects = [.. _looseProjects.Values];
            if (_mainProject != null) projects.Insert(0, _mainProject);
        }

        var result = new List<CodeDiagnostic>();
        foreach (var projectId in projects)
        {
            ct.ThrowIfCancellationRequested();
            var project = solution.GetProject(projectId);
            if (project == null) continue;
            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation == null) continue;

            foreach (var d in compilation.GetDiagnostics(ct))
            {
                if (d.IsSuppressed || d.Severity is not (DiagnosticSeverity.Error or DiagnosticSeverity.Warning)) continue;
                var tree = d.Location.SourceTree;
                string? key = null;
                if (tree != null)
                {
                    var doc = solution.GetDocumentId(tree);
                    if (doc == null || generated.Contains(doc)) continue;
                    keysById.TryGetValue(doc, out key);
                }
                var span = d.Location.GetLineSpan();
                var message = DiagnosticTranslator.Translate(d);
                // Nome que não existe, mas parecido com um que existe ("nome" e "name"): sugere o certo.
                if (d.Id == "CS0103" && tree != null && SimilarName(compilation.GetSemanticModel(tree), d.Location.SourceSpan) is { } guess)
                    message += $" Você quis dizer \"{guess}\"?";
                result.Add(new CodeDiagnostic(
                    d.Id,
                    d.Severity == DiagnosticSeverity.Error ? DiagnosticLevel.Error : DiagnosticLevel.Warning,
                    message,
                    d.GetMessage(System.Globalization.CultureInfo.GetCultureInfo("en-US")),
                    key ?? (tree?.FilePath is { Length: > 0 } fp ? fp : null),
                    tree != null ? span.StartLinePosition.Line + 1 : 0,
                    tree != null ? span.StartLinePosition.Character + 1 : 0,
                    tree != null ? d.Location.SourceSpan.Start : 0,
                    tree != null ? d.Location.SourceSpan.Length : 0,
                    FromBuild: false));
            }

            // Jogo com telas desenhadas: confere os nomes do game.Find com as telas (Screens/*.json).
            var gameDirectory = projectId == mainProject && mainModel is { UsesGameEngine: true } game ? game.Directory : null;

            // Dicas para armadilhas que o compilador aceita (lista impressa direto, divisão inteira…).
            foreach (var tree in compilation.SyntaxTrees)
            {
                var docId = solution.GetDocumentId(tree);
                if (docId == null || generated.Contains(docId) || !keysById.TryGetValue(docId, out var hintKey)) continue;
                var model = compilation.GetSemanticModel(tree);
                var hints = BeginnerHints.Analyze(model, ct);
                if (gameDirectory != null)
                {
                    var root = tree.GetRoot(ct);
                    hints = hints.Concat(GameAssist.CheckFindNames(root, gameDirectory)).Concat(GameAssist.CheckBuildCallsInDrawnScenes(root, gameDirectory));
                }
                foreach (var (id, message, location) in hints)
                {
                    var span = location.GetLineSpan();
                    result.Add(new CodeDiagnostic(id, DiagnosticLevel.Warning, message, "Dica do CSharp Lab", hintKey,
                        span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1,
                        location.SourceSpan.Start, location.SourceSpan.Length, FromBuild: false));
                }
            }
        }

        var ordered = result
            .DistinctBy(d => (d.Id, d.FilePath, d.Start, d.Length))
            .OrderBy(d => d.Level)
            .ThenBy(d => d.FilePath ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Line)
            .ThenBy(d => d.Column)
            .ToList();
        return new DiagnosticsSnapshot(ordered, versions);
    }

    /// <summary>Aquecimento: compila e faz um pedido de completion para carregar caches e JIT.</summary>
    public async Task WarmUpAsync(string? key)
    {
        try
        {
            Document? doc = key != null ? GetDocument(key) : null;
            if (doc == null)
            {
                lock (_gate)
                {
                    var any = _solution.Projects.SelectMany(p => p.Documents).FirstOrDefault(d => !_generated.Contains(d.Id));
                    doc = any;
                }
            }
            if (doc == null) return;
            await doc.Project.GetCompilationAsync().ConfigureAwait(false);
            var service = CompletionService.GetService(doc);
            if (service != null)
                await service.GetCompletionsAsync(doc, 0).ConfigureAwait(false);
            await Classifier.GetClassifiedSpansAsync(doc, new TextSpan(0, Math.Min(200, (await doc.GetTextAsync()).Length))).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppPaths.Log(ex, "Aquecimento");
        }
    }
}
