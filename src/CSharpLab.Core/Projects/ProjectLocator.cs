namespace CSharpLab.Core.Projects;

public static class ProjectLocator
{
    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", ".git", ".vs", ".vscode", ".idea", "node_modules", "packages", "TestResults",
    };

    /// <summary>Pastas geradas ou de ferramentas que o explorador recolhe e a análise ignora.</summary>
    public static bool IsSkippedDirectory(string name) => SkippedDirectories.Contains(name);

    public static bool IsInsideSkippedDirectory(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        if (relative.StartsWith("..", StringComparison.Ordinal)) return false;
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (IsSkippedDirectory(parts[i])) return true;
        }
        return false;
    }

    /// <summary>Procura .csproj na pasta e em subpastas próximas, sem entrar em bin/obj.</summary>
    public static IReadOnlyList<ProjectFile> FindProjects(string folder, int maxDepth = 3)
    {
        var result = new List<ProjectFile>();
        var queue = new Queue<(string Dir, int Depth)>();
        queue.Enqueue((folder, 0));
        while (queue.Count > 0)
        {
            var (dir, depth) = queue.Dequeue();
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*.csproj"))
                    result.Add(ProjectFile.Read(file));
                if (depth >= maxDepth) continue;
                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    var name = Path.GetFileName(sub);
                    if (IsSkippedDirectory(name) || name.StartsWith('.')) continue;
                    var attrs = File.GetAttributes(sub);
                    if (attrs.HasFlag(FileAttributes.ReparsePoint) || attrs.HasFlag(FileAttributes.Hidden)) continue;
                    queue.Enqueue((sub, depth + 1));
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }
        return result.OrderBy(p => p.Path.Count(c => c == '\\')).ThenBy(p => p.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Projetos que o botão Executar roda: console e jogos (janela).</summary>
    public static IReadOnlyList<ProjectFile> RunnableProjects(IEnumerable<ProjectFile> projects) =>
        projects.Where(p => p.Error == null && p.IsRunnable).ToList();

    /// <summary>Projeto mais próximo que contém o arquivo, subindo as pastas até <paramref name="stopAt"/>.</summary>
    public static ProjectFile? FindOwningProject(string filePath, string? stopAt = null)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
        while (!string.IsNullOrEmpty(dir))
        {
            try
            {
                var projects = Directory.GetFiles(dir, "*.csproj");
                if (projects.Length == 1) return ProjectFile.Read(projects[0]);
                if (projects.Length > 1) return null;
            }
            catch
            {
                return null;
            }
            if (stopAt != null && string.Equals(dir.TrimEnd('\\'), stopAt.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                break;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    /// <summary>Arquivos .cs que o SDK compilaria por padrão (sem bin/obj nem subprojetos).</summary>
    public static List<string> DefaultCompileFiles(string projectDirectory)
    {
        var files = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Walk(projectDirectory, isRoot: true);
        return files;

        void Walk(string dir, bool isRoot)
        {
            try
            {
                if (!visited.Add(Path.GetFullPath(dir))) return;
                if (!isRoot && File.GetAttributes(dir).HasFlag(FileAttributes.ReparsePoint)) return;
                if (!isRoot && Directory.EnumerateFiles(dir, "*.csproj").Any()) return;
                foreach (var f in Directory.EnumerateFiles(dir, "*.cs"))
                {
                    if (!Files.TextFileIO.IsTempName(Path.GetFileName(f)))
                        files.Add(Path.GetFullPath(f));
                }
                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    var name = Path.GetFileName(sub);
                    if (IsSkippedDirectory(name) || name.StartsWith('.')) continue;
                    Walk(sub, false);
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }
    }
}
