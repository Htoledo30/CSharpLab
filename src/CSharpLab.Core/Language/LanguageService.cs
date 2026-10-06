using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text;
using CSharpLab.Core.Projects;
using CSharpLab.Core.Settings;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;

namespace CSharpLab.Core.Language;

/// <summary>
/// Serviços de linguagem do Roslyn sobre um workspace em memória que espelha o projeto real:
/// arquivos do projeto, referências, usings implícitos, nullable e textos ainda não salvos.
/// Todas as operações pesadas rodam fora da thread da interface sobre snapshots imutáveis.
/// </summary>
public sealed partial class LanguageService : IDisposable
{
    private const string GlobalUsingsName = "CSharpLab.GlobalUsings.g.cs";

    private static readonly ConcurrentDictionary<string, MetadataReference> ReferenceCache = new(StringComparer.OrdinalIgnoreCase);

    private readonly AdhocWorkspace _workspace;
    private readonly object _gate = new();
    private Solution _solution;
    private ProjectId? _mainProject;
    private ProjectModel? _mainModel;
    private string _mainEditorConfigs = "";
    private DocumentId? _mainGlobalUsings;

    /// <summary>Documento de cada chave (caminho completo ou "untitled:…").</summary>
    private readonly Dictionary<string, DocumentId> _documents = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Projetos em memória de rascunhos e arquivos fora do projeto principal.</summary>
    private readonly Dictionary<string, ProjectId> _looseProjects = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Textos abertos no editor (podem diferir do disco) e suas versões.</summary>
    private readonly Dictionary<string, (SourceText Text, int Version, string? Path)> _open = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<DocumentId> _generated = [];

    public event Action? ProjectChanged;

    public LanguageService()
    {
        var assemblies = MefHostServices.DefaultAssemblies
            .Concat([typeof(CompletionService).Assembly, typeof(CSharpCompilation).Assembly])
            .Distinct();
        var host = MefHostServices.Create(assemblies);
        _workspace = new AdhocWorkspace(host);
        _solution = _workspace.CurrentSolution;
    }

    public static string UntitledKey(string id) => "untitled:" + id;

    public static string KeyFor(string path) => Path.GetFullPath(path);

    public ProjectModel? MainModel
    {
        get { lock (_gate) return _mainModel; }
    }

    // ---------------------------------------------------------------- projeto

    /// <summary>Troca o contexto principal (projeto real, pasta sem projeto ou nenhum).</summary>
    public void LoadProject(ProjectModel? model)
    {
        lock (_gate)
        {
            // Mesma compilação de antes (ex.: reavaliação depois de salvar): mantém o projeto e tudo
            // o que o Roslyn já calculou, para sugestões e erros continuarem instantâneos.
            var editorConfigs = model != null ? EditorConfigStamp(model.Directory) : "";
            if (model != null && _mainProject != null && model.HasSameAnalysisAs(_mainModel) && editorConfigs == _mainEditorConfigs)
            {
                _mainModel = model;
                return;
            }
            _mainEditorConfigs = editorConfigs;

            var solution = _solution;
            if (_mainProject != null)
            {
                solution = solution.RemoveProject(_mainProject);
                foreach (var key in _documents.Where(d => d.Value.ProjectId == _mainProject).Select(d => d.Key).ToList())
                    _documents.Remove(key);
                _generated.RemoveWhere(d => d.ProjectId == _mainProject);
                _mainProject = null;
                _mainGlobalUsings = null;
            }
            _mainModel = model;

            if (model != null)
            {
                var (info, docKeys, globalUsingsId) = CreateProjectInfo(model, model.CompileFiles);
                solution = solution.AddProject(info);
                _mainProject = info.Id;
                _mainGlobalUsings = globalUsingsId;
                _generated.Add(globalUsingsId);
                foreach (var (key, id) in docKeys) _documents[key] = id;
            }

            // Reposiciona os documentos abertos: os que pertencem ao novo projeto saem dos projetos soltos.
            foreach (var (key, open) in _open.ToList())
            {
                if (_documents.TryGetValue(key, out var id) && id.ProjectId == _mainProject)
                {
                    solution = RemoveLoose(solution, key);
                    solution = solution.WithDocumentText(id, open.Text, PreservationMode.PreserveIdentity);
                }
                else if (!_looseProjects.ContainsKey(key))
                {
                    _documents.Remove(key);
                    solution = AddLoose(solution, key, open.Path, open.Text);
                }
            }
            _solution = solution;
        }
        ProjectChanged?.Invoke();
    }

    private (ProjectInfo Info, List<(string Key, DocumentId Id)> Docs, DocumentId GlobalUsings) CreateProjectInfo(ProjectModel model, IEnumerable<string> files)
    {
        var id = ProjectId.CreateNewId(model.Name);
        var docs = new List<DocumentInfo>();
        var keys = new List<(string, DocumentId)>();
        foreach (var file in files.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var key = KeyFor(file);
            var docId = DocumentId.CreateNewId(id, key);
            docs.Add(DocumentInfo.Create(docId, Path.GetFileName(file), loader: new FileTextLoader(file, Encoding.UTF8), filePath: file));
            keys.Add((key, docId));
        }

        var globalUsingsId = DocumentId.CreateNewId(id, GlobalUsingsName);
        docs.Add(DocumentInfo.Create(globalUsingsId, GlobalUsingsName,
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From(GlobalUsingsText(model.Usings)), VersionStamp.Create())),
            filePath: Path.Combine(model.Directory, "obj", GlobalUsingsName), isGenerated: true));

        var info = ProjectInfo.Create(
                id, VersionStamp.Create(), model.Name, model.AssemblyName ?? model.Name, LanguageNames.CSharp,
                filePath: model.ProjectPath,
                compilationOptions: CompilationOptions(model),
                parseOptions: ParseOptions(model),
                documents: docs,
                metadataReferences: References(model))
            .WithAnalyzerConfigDocuments(EditorConfigs(id, model.Directory));
        return (info, keys, globalUsingsId);
    }

    private static string GlobalUsingsText(IEnumerable<GlobalUsingItem> usings)
    {
        var sb = new StringBuilder();
        foreach (var u in usings)
        {
            if (u.Alias != null) sb.Append("global using ").Append(u.Alias).Append(" = global::").Append(u.Namespace).AppendLine(";");
            else if (u.Static) sb.Append("global using static global::").Append(u.Namespace).AppendLine(";");
            else sb.Append("global using global::").Append(u.Namespace).AppendLine(";");
        }
        return sb.ToString();
    }

    private static CSharpParseOptions ParseOptions(ProjectModel model)
    {
        var version = LanguageVersion.Default;
        if (!string.IsNullOrWhiteSpace(model.LangVersion) && LanguageVersionFacts.TryParse(model.LangVersion, out var parsed))
            version = parsed;
        return new CSharpParseOptions(version, DocumentationMode.Parse, SourceCodeKind.Regular, model.DefineConstants);
    }

    private static CSharpCompilationOptions CompilationOptions(ProjectModel model)
    {
        var nullable = (model.Nullable ?? "").ToLowerInvariant() switch
        {
            "enable" => NullableContextOptions.Enable,
            "warnings" => NullableContextOptions.Warnings,
            "annotations" => NullableContextOptions.Annotations,
            _ => NullableContextOptions.Disable,
        };
        var outputKind = (model.OutputType ?? "").ToLowerInvariant() switch
        {
            "exe" => OutputKind.ConsoleApplication,
            "winexe" => OutputKind.WindowsApplication,
            _ => OutputKind.DynamicallyLinkedLibrary,
        };
        var noWarn = model.NoWarn
            .Select(w => w.StartsWith("CS", StringComparison.OrdinalIgnoreCase) || !int.TryParse(w, out _) ? w.ToUpperInvariant() : "CS" + int.Parse(w).ToString("0000"))
            .Distinct()
            .Select(w => new KeyValuePair<string, ReportDiagnostic>(w, ReportDiagnostic.Suppress));
        return new CSharpCompilationOptions(
            outputKind,
            nullableContextOptions: nullable,
            allowUnsafe: model.AllowUnsafeBlocks,
            warningLevel: model.WarningLevel,
            generalDiagnosticOption: model.TreatWarningsAsErrors ? ReportDiagnostic.Error : ReportDiagnostic.Default,
            specificDiagnosticOptions: noWarn,
            concurrentBuild: true);
    }

    private static IEnumerable<MetadataReference> References(ProjectModel model)
    {
        var paths = model.References.Count > 0 ? model.References : ReferencePacks.Find(model.TargetFramework);
        if (paths.Count == 0)
            return Basic.Reference.Assemblies.Net100.References.All;

        var list = new List<MetadataReference>(paths.Count);
        foreach (var path in paths)
        {
            try
            {
                var stamp = File.GetLastWriteTimeUtc(path).Ticks;
                list.Add(ReferenceCache.GetOrAdd(path + "|" + stamp, _ => MetadataReference.CreateFromFile(path)));
            }
            catch (Exception ex)
            {
                AppPaths.Log(ex, "Referência " + path);
            }
        }
        return list;
    }

    /// <summary>Caminho e data de cada .editorconfig que vale para a pasta.</summary>
    private static string EditorConfigStamp(string directory)
    {
        var parts = new List<string>();
        try
        {
            for (var dir = directory; !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
            {
                var file = Path.Combine(dir, ".editorconfig");
                if (File.Exists(file)) parts.Add(file + "|" + File.GetLastWriteTimeUtc(file).Ticks);
            }
        }
        catch
        {
        }
        return string.Join(";", parts);
    }

    private static IEnumerable<DocumentInfo> EditorConfigs(ProjectId projectId, string directory)
    {
        var list = new List<DocumentInfo>();
        try
        {
            for (var dir = directory; !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
            {
                var file = Path.Combine(dir, ".editorconfig");
                if (!File.Exists(file)) continue;
                list.Add(DocumentInfo.Create(DocumentId.CreateNewId(projectId), ".editorconfig",
                    loader: new FileTextLoader(file, Encoding.UTF8), filePath: file));
                if (File.ReadLines(file).Any(l => l.Replace(" ", "").Equals("root=true", StringComparison.OrdinalIgnoreCase)))
                    break;
            }
        }
        catch (Exception ex)
        {
            AppPaths.Log(ex, "Lendo .editorconfig");
        }
        return list;
    }

    private bool BelongsToMain(string key) =>
        _mainModel != null && key.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
        (_mainModel.IsEvaluated
            ? _mainModel.CompileFiles.Contains(key, StringComparer.OrdinalIgnoreCase)
            : Files.FileOperations.IsSameOrInside(key, _mainModel.Directory) &&
              !ProjectLocator.IsInsideSkippedDirectory(key, _mainModel.Directory));

    private Solution AddLoose(Solution solution, string key, string? path, SourceText text)
    {
        var dir = path != null ? Path.GetDirectoryName(path)! : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var name = path != null ? Path.GetFileNameWithoutExtension(path) : "Rascunho";
        var model = ProjectModel.Loose(name, dir);
        var (info, _, _) = CreateProjectInfo(model, []);
        var docId = DocumentId.CreateNewId(info.Id, key);
        info = info.WithDocuments(info.Documents.Append(DocumentInfo.Create(docId, path != null ? Path.GetFileName(path) : "Sem título.cs",
            loader: TextLoader.From(TextAndVersion.Create(text, VersionStamp.Create())), filePath: path)));
        foreach (var d in info.Documents.Where(d => d.IsGenerated)) _generated.Add(d.Id);
        _looseProjects[key] = info.Id;
        _documents[key] = docId;
        return solution.AddProject(info);
    }

    private Solution RemoveLoose(Solution solution, string key)
    {
        if (!_looseProjects.Remove(key, out var projectId)) return solution;
        _generated.RemoveWhere(d => d.ProjectId == projectId);
        return solution.RemoveProject(projectId);
    }

    // ---------------------------------------------------------------- documentos

    public void OpenDocument(string key, string? path, SourceText text, int version)
    {
        lock (_gate)
        {
            _open[key] = (text, version, path);
            if (_documents.TryGetValue(key, out var id))
            {
                _solution = _solution.WithDocumentText(id, text, PreservationMode.PreserveIdentity);
                return;
            }
            if (path != null && BelongsToMain(key) && _mainProject != null)
            {
                // Arquivo novo dentro do projeto que ainda não estava na lista.
                var docId = DocumentId.CreateNewId(_mainProject, key);
                _solution = _solution.AddDocument(DocumentInfo.Create(docId, Path.GetFileName(path),
                    loader: TextLoader.From(TextAndVersion.Create(text, VersionStamp.Create())), filePath: path));
                _documents[key] = docId;
                return;
            }
            _solution = AddLoose(_solution, key, path, text);
        }
    }

    public void UpdateDocument(string key, SourceText text, int version)
    {
        lock (_gate)
        {
            if (!_open.TryGetValue(key, out var open)) return;
            _open[key] = (text, version, open.Path);
            if (_documents.TryGetValue(key, out var id) && _solution.ContainsDocument(id))
                _solution = _solution.WithDocumentText(id, text, PreservationMode.PreserveIdentity);
        }
    }

    public void CloseDocument(string key)
    {
        lock (_gate)
        {
            _open.Remove(key);
            if (_looseProjects.ContainsKey(key))
            {
                _solution = RemoveLoose(_solution, key);
                _documents.Remove(key);
            }
            else if (_documents.TryGetValue(key, out var id) && File.Exists(key))
            {
                // Volta a usar o conteúdo do disco.
                _solution = _solution.WithDocumentTextLoader(id, new FileTextLoader(key, Encoding.UTF8), PreservationMode.PreserveValue);
            }
        }
    }

    /// <summary>Arquivo aberto renomeado ou salvo com outro nome.</summary>
    public void MoveDocument(string oldKey, string newKey, string newPath)
    {
        lock (_gate)
        {
            if (!_open.Remove(oldKey, out var open)) return;
            RemoveDocumentCore(oldKey);
            OpenDocument(newKey, newPath, open.Text, open.Version);
        }
    }

    private void RemoveDocumentCore(string key)
    {
        if (_looseProjects.ContainsKey(key))
        {
            _solution = RemoveLoose(_solution, key);
            _documents.Remove(key);
        }
        else if (_documents.Remove(key, out var id) && _solution.ContainsDocument(id))
        {
            _solution = _solution.RemoveDocument(id);
        }
    }

    // ---------------------------------------------------------------- mudanças no disco

    public void OnFileCreatedOrChanged(string path)
    {
        if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) return;
        var key = KeyFor(path);
        lock (_gate)
        {
            if (_open.ContainsKey(key) || _mainProject == null || !BelongsToMain(key)) return;
            if (_documents.TryGetValue(key, out var id))
            {
                _solution = _solution.WithDocumentTextLoader(id, new FileTextLoader(key, Encoding.UTF8), PreservationMode.PreserveValue);
            }
            else if (File.Exists(key))
            {
                var docId = DocumentId.CreateNewId(_mainProject, key);
                _solution = _solution.AddDocument(DocumentInfo.Create(docId, Path.GetFileName(key),
                    loader: new FileTextLoader(key, Encoding.UTF8), filePath: key));
                _documents[key] = docId;
            }
        }
    }

    public void OnFileDeleted(string path)
    {
        var key = KeyFor(path);
        lock (_gate)
        {
            // Pasta removida: remove todos os documentos dentro dela.
            var affected = _documents.Keys
                .Where(k => !k.StartsWith("untitled:", StringComparison.Ordinal) && Files.FileOperations.IsSameOrInside(k, key))
                .ToList();
            foreach (var k in affected)
            {
                if (_open.ContainsKey(k) || _looseProjects.ContainsKey(k)) continue;
                if (_documents.Remove(k, out var id) && _solution.ContainsDocument(id))
                    _solution = _solution.RemoveDocument(id);
            }
        }
    }

    public void Dispose() => _workspace.Dispose();
}
