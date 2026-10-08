using System.Text;
using CSharpLab.Core.Files;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharpLab.Core.Projects;

public sealed record CreatedProject(string ProjectPath, string ProgramPath)
{
    public string Directory => Path.GetDirectoryName(ProjectPath)!;
}

/// <summary>Cria projetos console equivalentes a "dotnet new console --framework net10.0".</summary>
public static class ProjectCreator
{
    public const string TargetFramework = "net10.0";
    public static readonly string DefaultProgram = "Console.WriteLine(\"Olá, mundo!\");" + Environment.NewLine;

    public static string? ValidateProjectName(string name)
    {
        var basic = FileOperations.ValidateName(name);
        if (basic != null) return basic;
        if (name.StartsWith('.') || name.StartsWith('-'))
            return "O nome deve começar com uma letra, número ou _.";
        foreach (var c in name)
        {
            if (!(char.IsLetterOrDigit(c) || c is '_' or '-' or '.' or ' '))
                return "Use apenas letras, números, espaço, _, - ou ponto.";
        }
        return null;
    }

    public static string CsprojContent(string projectName)
    {
        var rootNamespace = ToIdentifier(projectName);
        var sb = new StringBuilder();
        sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
        sb.AppendLine();
        sb.AppendLine("  <PropertyGroup>");
        sb.AppendLine("    <OutputType>Exe</OutputType>");
        sb.AppendLine($"    <TargetFramework>{TargetFramework}</TargetFramework>");
        if (rootNamespace != projectName)
            sb.AppendLine($"    <RootNamespace>{rootNamespace}</RootNamespace>");
        sb.AppendLine("    <ImplicitUsings>enable</ImplicitUsings>");
        sb.AppendLine("    <Nullable>enable</Nullable>");
        sb.AppendLine("  </PropertyGroup>");
        sb.AppendLine();
        sb.AppendLine("</Project>");
        return sb.ToString().Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
    }

    /// <summary>Converte um nome de projeto num namespace válido (A Torre → A_Torre).</summary>
    public static string ToIdentifier(string name)
    {
        var parts = name.Split('.', StringSplitOptions.RemoveEmptyEntries).Select(part =>
        {
            var sb = new StringBuilder();
            foreach (var c in part)
                sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
            var id = sb.ToString();
            if (id.Length == 0 || char.IsDigit(id[0])) id = "_" + id;
            if (SyntaxFacts.GetKeywordKind(id) != SyntaxKind.None) id = "_" + id;
            return id;
        });
        return string.Join('.', parts);
    }

    /// <summary>
    /// Cria &lt;parent&gt;\&lt;name&gt;\ com o .csproj e Program.cs. Nunca usa uma pasta que já tenha arquivos.
    /// Se falhar no meio, remove apenas o que foi criado aqui.
    /// </summary>
    public static CreatedProject CreateConsoleProject(string parentFolder, string name, string? programText = null) =>
        CreateProject(parentFolder, name, CsprojContent, programText ?? DefaultProgram, null);

    /// <summary>
    /// Cria um jogo com botões: .csproj de janela, Program.cs inicial, o motor em lib\, a pasta Assets
    /// e o guia do motor para IAs (AGENTS.md e CLAUDE.md).
    /// </summary>
    public static CreatedProject CreateGameProject(string parentFolder, string name, string? programText = null) =>
        CreateProject(parentFolder, name, GameKit.CsprojContent, programText ?? GameKit.StarterProgram, (dir, created) =>
        {
            created.AddRange(GameKit.CopyLibrary(dir));
            var assets = Path.Combine(dir, GameKit.AssetsFolder);
            Directory.CreateDirectory(assets);
            created.Add(assets);
            foreach (var (file, text) in new[] { ("AGENTS.md", GameKit.AgentsGuide), ("CLAUDE.md", GameKit.ClaudeGuide) })
            {
                var path = Path.Combine(dir, file);
                WriteNew(path, text);
                created.Add(path);
            }
            // O jogo inicial já vem com as telas desenhadas das cenas dele.
            if (programText == null)
            {
                var screens = Path.Combine(dir, GameKit.ScreensFolder);
                Directory.CreateDirectory(screens);
                created.Add(screens);
                foreach (var (file, text) in GameKit.StarterScreens)
                {
                    var path = Path.Combine(screens, file);
                    WriteNew(path, text);
                    created.Add(path);
                }
            }
        });

    private static CreatedProject CreateProject(string parentFolder, string name, Func<string, string> csprojContent, string programText,
        Action<string, List<string>>? extras)
    {
        name = name.Trim();
        var error = ValidateProjectName(name);
        if (error != null) throw new UserFacingException(error);
        if (!Directory.Exists(parentFolder))
            throw new UserFacingException("A pasta de destino não existe.");

        var dir = Path.Combine(parentFolder, name);
        if (File.Exists(dir))
            throw new UserFacingException($"Já existe um arquivo chamado \"{name}\" nesse local.");
        bool createdDir = false;
        if (Directory.Exists(dir))
        {
            if (Directory.EnumerateFileSystemEntries(dir).Any())
                throw new UserFacingException($"A pasta \"{name}\" já existe e não está vazia.");
        }
        else
        {
            Directory.CreateDirectory(dir);
            createdDir = true;
        }

        var csproj = Path.Combine(dir, name + ".csproj");
        var program = Path.Combine(dir, "Program.cs");
        var created = new List<string>();
        try
        {
            WriteNew(csproj, csprojContent(name));
            created.Add(csproj);
            WriteNew(program, programText);
            created.Add(program);
            // Os extras anotam cada arquivo assim que o criam: se algo falhar no meio, tudo é desfeito.
            extras?.Invoke(dir, created);
            return new CreatedProject(csproj, program);
        }
        catch
        {
            // De trás para frente: os arquivos antes das pastas que os contêm.
            for (int i = created.Count - 1; i >= 0; i--)
            {
                try
                {
                    if (Directory.Exists(created[i])) Directory.Delete(created[i], recursive: false);
                    else File.Delete(created[i]);
                }
                catch { }
            }
            try
            {
                var lib = Path.Combine(dir, GameKit.LibraryFolder);
                if (Directory.Exists(lib) && !Directory.EnumerateFileSystemEntries(lib).Any()) Directory.Delete(lib);
            }
            catch { }
            if (createdDir)
            {
                try { Directory.Delete(dir, recursive: false); } catch { }
            }
            throw;
        }
    }

    /// <summary>
    /// Cria um .csproj numa pasta que já tem código, sem alterar nenhum arquivo existente.
    /// Retorna o caminho do projeto criado.
    /// </summary>
    public static string CreateProjectInFolder(string folder)
    {
        var problem = CheckFolderForNewProject(folder, out var projectPath);
        if (problem != null) throw new UserFacingException(problem);
        WriteNew(projectPath, CsprojContent(Path.GetFileNameWithoutExtension(projectPath)));
        return projectPath;
    }

    /// <summary>Verifica se é seguro criar um projeto na pasta. Retorna a mensagem do impedimento ou null.</summary>
    public static string? CheckFolderForNewProject(string folder, out string projectPath)
    {
        var folderName = Path.GetFileName(Path.GetFullPath(folder).TrimEnd('\\'));
        var projectName = ValidateProjectName(folderName) == null ? folderName : ToIdentifier(folderName);
        projectPath = Path.Combine(folder, projectName + ".csproj");

        if (Directory.EnumerateFiles(folder, "*.csproj").Any())
            return "Esta pasta já tem um projeto.";
        if (File.Exists(projectPath))
            return $"Já existe \"{Path.GetFileName(projectPath)}\" nesta pasta.";

        var files = ProjectLocator.DefaultCompileFiles(folder);
        if (files.Count > 200)
            return "Esta pasta tem arquivos .cs demais para virar um único projeto console.";

        var withTopLevel = FilesWithTopLevelStatements(files);
        if (withTopLevel.Count > 1)
        {
            var names = string.Join(", ", withTopLevel.Take(4).Select(Path.GetFileName));
            return $"Mais de um arquivo tem instruções fora de uma classe ({names}). Mantenha o código inicial em um só arquivo.";
        }
        return null;
    }

    public static List<string> FilesWithTopLevelStatements(IEnumerable<string> files)
    {
        var result = new List<string>();
        foreach (var file in files)
        {
            try
            {
                var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file));
                if (tree.GetCompilationUnitRoot().Members.OfType<GlobalStatementSyntax>().Any())
                    result.Add(file);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return result;
    }

    private static void WriteNew(string path, string content)
    {
        using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var bytes = TextFileIO.Utf8NoBom.GetBytes(content);
        fs.Write(bytes);
        fs.Flush(true);
    }
}
