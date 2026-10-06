using CSharpLab.Core.Files;

namespace CSharpLab.Core.Projects;

/// <summary>Um evento do monitor da pasta. <see cref="Kind"/> = All significa "perdi eventos, releia tudo".</summary>
public readonly record struct FolderEvent(WatcherChangeTypes Kind, string Path, string? OldPath = null);

/// <summary>O que um lote de eventos da pasta exige do editor.</summary>
public sealed class FolderChanges
{
    /// <summary>Pastas cujo conteúdo mudou (o explorador relê só essas).</summary>
    public HashSet<string> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Arquivos que existem agora (criados, alterados ou destino de um renomear).</summary>
    public List<string> Present { get; } = [];

    /// <summary>Arquivos que não existem mais.</summary>
    public List<string> Gone { get; } = [];

    /// <summary>O monitor perdeu eventos: o explorador e os projetos são relidos por inteiro.</summary>
    public bool FullRefresh { get; private set; }

    /// <summary>Mudou a definição de um projeto (.csproj, .props, global.json…): descobre os projetos de novo.</summary>
    public bool ReloadProjects { get; private set; }

    /// <summary>A lista de arquivos ou as configurações do projeto mudaram: o SDK reavalia o projeto atual.</summary>
    public bool Reevaluate { get; private set; }

    private static readonly string[] ProjectDefinitionExtensions = [".csproj", ".props", ".targets"];
    private static readonly string[] ProjectDefinitionNames = ["global.json", "nuget.config", "packages.lock.json"];

    /// <summary>
    /// Descarta cedo o que acontece em bin/obj/.git etc. — exceto o project.assets.json, que indica um restore.
    /// </summary>
    public static bool ShouldTrack(FolderEvent e, string folder) =>
        e.Kind == WatcherChangeTypes.All ||
        !ProjectLocator.IsInsideSkippedDirectory(e.Path, folder) ||
        IsAssetsFile(e.Path) ||
        (e.OldPath != null && !ProjectLocator.IsInsideSkippedDirectory(e.OldPath, folder));

    /// <param name="compileFiles">Arquivos .cs do projeto analisado agora (para saber se um .cs novo já é conhecido).</param>
    /// <param name="evaluationRunning">Uma avaliação em andamento faz o próprio restore; o resultado dela já inclui isso.</param>
    public static FolderChanges Analyze(IEnumerable<FolderEvent> events, string folder, IEnumerable<string>? compileFiles, bool evaluationRunning)
    {
        var result = new FolderChanges();
        HashSet<string>? known = null;
        foreach (var e in events)
        {
            if (!FileOperations.IsSameOrInside(e.Path, folder)) continue;
            if (e.Kind == WatcherChangeTypes.All)
            {
                result.FullRefresh = true;
                continue;
            }
            if (e.Kind == WatcherChangeTypes.Renamed && e.OldPath != null) result.Add(e.OldPath, e.Kind, folder, compileFiles, ref known, evaluationRunning);
            result.Add(e.Path, e.Kind, folder, compileFiles, ref known, evaluationRunning);
        }
        return result;
    }

    private void Add(string path, WatcherChangeTypes kind, string folder, IEnumerable<string>? compileFiles,
        ref HashSet<string>? known, bool evaluationRunning)
    {
        var name = System.IO.Path.GetFileName(path);
        if (TextFileIO.IsTempName(name)) return;
        if (IsAssetsFile(path) && !evaluationRunning) Reevaluate = true;
        if (ProjectLocator.IsInsideSkippedDirectory(path, folder)) return;

        if (kind != WatcherChangeTypes.Changed || !Directory.Exists(path))
            Directories.Add(System.IO.Path.GetDirectoryName(path) ?? folder);
        if (ProjectDefinitionExtensions.Any(x => name.EndsWith(x, StringComparison.OrdinalIgnoreCase)) ||
            ProjectDefinitionNames.Any(x => name.Equals(x, StringComparison.OrdinalIgnoreCase)))
        {
            ReloadProjects = true;
        }
        if (name.Equals(".editorconfig", StringComparison.OrdinalIgnoreCase)) Reevaluate = true;

        // Decide pelo estado final no disco: salvar grava um temporário e substitui o
        // arquivo, o que gera "apagado"/"renomeado" para um arquivo que continua existindo.
        bool exists = File.Exists(path);
        if (kind != WatcherChangeTypes.Changed && name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            known ??= new HashSet<string>(compileFiles ?? [], StringComparer.OrdinalIgnoreCase);
            if (exists != known.Contains(path)) Reevaluate = true;
        }

        if (exists) Present.Add(path);
        else if (!Directory.Exists(path)) Gone.Add(path);
    }

    private static bool IsAssetsFile(string path) =>
        System.IO.Path.GetFileName(path).Equals("project.assets.json", StringComparison.OrdinalIgnoreCase);
}
