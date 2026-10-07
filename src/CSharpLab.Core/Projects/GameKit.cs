using System.Reflection;
using CSharpLab.Core.Files;

namespace CSharpLab.Core.Projects;

/// <summary>
/// O motor dos jogos com botões (CSharpLab.Game.dll). Cada jogo leva uma cópia em lib\,
/// então continua funcionando se a pasta for movida ou o editor for desinstalado.
/// </summary>
public static class GameKit
{
    public const string LibraryName = "CSharpLab.Game";
    /// <summary>Namespace do motor (diferente do nome da classe Game, para não confundir o compilador).</summary>
    public const string Namespace = "CSharpLab.GameEngine";
    public const string LibraryFolder = "lib";
    public const string AssetsFolder = "Assets";
    public const string TargetFramework = "net10.0-windows";

    /// <summary>O motor que veio com o CSharp Lab (ao lado do programa).</summary>
    public static string LibrarySource => Path.Combine(AppContext.BaseDirectory, LibraryName + ".dll");

    public static string LibraryPath(string projectDirectory) => Path.Combine(projectDirectory, LibraryFolder, LibraryName + ".dll");

    /// <summary>O projeto usa o motor (tem a cópia em lib\).</summary>
    public static bool UsesEngine(string projectDirectory) => File.Exists(LibraryPath(projectDirectory));

    public static string StarterProgram => Resource("Starter.cs");

    /// <summary>Guia do motor para IAs (AGENTS.md), também útil para quem programa.</summary>
    public static string AgentsGuide => Resource("AGENTS.md");

    /// <summary>CLAUDE.md só aponta para o AGENTS.md, para as duas IAs lerem o mesmo guia.</summary>
    public static string ClaudeGuide =>
        "# Instruções para IA" + Environment.NewLine + Environment.NewLine +
        "Siga o guia do motor deste jogo: @AGENTS.md" + Environment.NewLine;

    public static string CsprojContent(string projectName)
    {
        var rootNamespace = ProjectCreator.ToIdentifier(projectName);
        var lines = new List<string>
        {
            "<Project Sdk=\"Microsoft.NET.Sdk\">",
            "",
            "  <PropertyGroup>",
            "    <OutputType>WinExe</OutputType>",
            $"    <TargetFramework>{TargetFramework}</TargetFramework>",
            "    <UseWPF>true</UseWPF>",
        };
        if (rootNamespace != projectName)
            lines.Add($"    <RootNamespace>{rootNamespace}</RootNamespace>");
        lines.AddRange(
        [
            "    <ImplicitUsings>enable</ImplicitUsings>",
            "    <Nullable>enable</Nullable>",
            "  </PropertyGroup>",
            "",
            "  <!-- Motor do jogo do CSharp Lab: game.Say, game.Button, game.Bar... -->",
            "  <ItemGroup>",
            $"    <Reference Include=\"{LibraryName}\">",
            $"      <HintPath>{LibraryFolder}\\{LibraryName}.dll</HintPath>",
            "    </Reference>",
            $"    <Using Include=\"{Namespace}\" />",
            "    <!-- Programas de janela perdem o System.IO automático; aqui ele volta (File, Path...). -->",
            "    <Using Include=\"System.IO\" />",
            "  </ItemGroup>",
            "",
            "  <!-- Imagens do jogo: tudo que estiver na pasta Assets vai junto. -->",
            "  <ItemGroup>",
            $"    <None Include=\"{AssetsFolder}\\**\" CopyToOutputDirectory=\"PreserveNewest\" />",
            "  </ItemGroup>",
            "",
            "</Project>",
        ]);
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    /// <summary>Copia o motor (dll e documentação) para lib\ do projeto. Retorna os arquivos criados.</summary>
    public static List<string> CopyLibrary(string projectDirectory)
    {
        var source = LibrarySource;
        if (!File.Exists(source))
            throw new UserFacingException("O motor de jogos (CSharpLab.Game.dll) não foi encontrado junto do CSharp Lab. Reinstale o programa.");
        var folder = Path.Combine(projectDirectory, LibraryFolder);
        Directory.CreateDirectory(folder);
        var created = new List<string>();
        foreach (var file in LibraryFiles(source))
        {
            var target = Path.Combine(folder, Path.GetFileName(file));
            File.Copy(file, target, overwrite: true);
            created.Add(target);
        }
        return created;
    }

    /// <summary>
    /// Se o CSharp Lab tem um motor mais novo que o do projeto, atualiza a cópia em lib\.
    /// Nunca troca por um mais antigo. Retorna true se atualizou.
    /// </summary>
    public static bool RefreshLibrary(string projectDirectory)
    {
        var target = LibraryPath(projectDirectory);
        var source = LibrarySource;
        if (!File.Exists(target) || !File.Exists(source)) return false;
        try
        {
            var have = AssemblyName.GetAssemblyName(target).Version;
            var available = AssemblyName.GetAssemblyName(source).Version;
            if (have == null || available == null || available <= have) return false;
            CopyLibrary(projectDirectory);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException)
        {
            Settings.AppPaths.Log(ex, "Atualizando o motor do jogo");
            return false;
        }
    }

    private static IEnumerable<string> LibraryFiles(string dll)
    {
        yield return dll;
        var docs = Path.ChangeExtension(dll, ".xml");
        if (File.Exists(docs)) yield return docs;
    }

    private static string Resource(string name)
    {
        var assembly = typeof(GameKit).Assembly;
        using var stream = assembly.GetManifestResourceStream("CSharpLab.Core.GameKit." + name)
            ?? throw new InvalidOperationException("Recurso ausente: " + name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
    }
}
