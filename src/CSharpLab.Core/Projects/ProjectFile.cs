using System.Xml.Linq;

namespace CSharpLab.Core.Projects;

/// <summary>
/// Leitura rápida e somente leitura de um .csproj (sem MSBuild), usada para decisões
/// imediatas. Os valores definitivos vêm da avaliação real pelo SDK.
/// </summary>
public sealed class ProjectFile
{
    public required string Path { get; init; }
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
    public string Directory => System.IO.Path.GetDirectoryName(Path)!;
    public string? Sdk { get; init; }
    public string? TargetFramework { get; init; }
    public string? TargetFrameworks { get; init; }
    public string? OutputType { get; init; }
    public string? AssemblyName { get; init; }
    public string? ImplicitUsings { get; init; }
    public string? Nullable { get; init; }
    public string? LangVersion { get; init; }
    public string? Error { get; init; }

    public bool IsSdkStyle => !string.IsNullOrEmpty(Sdk);
    public bool IsConsole => string.Equals(OutputType, "Exe", StringComparison.OrdinalIgnoreCase);
    /// <summary>Programa de janela (ex.: um jogo com botões). Roda como o console, mas abre uma janela.</summary>
    public bool IsWindowApp => string.Equals(OutputType, "WinExe", StringComparison.OrdinalIgnoreCase);
    /// <summary>O botão Executar consegue rodar: console ou janela.</summary>
    public bool IsRunnable => IsConsole || IsWindowApp;
    public bool IsMultiTarget => string.IsNullOrEmpty(TargetFramework) && !string.IsNullOrEmpty(TargetFrameworks);
    public string? EffectiveTargetFramework => TargetFramework ?? TargetFrameworks?.Split(';', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

    public static ProjectFile Read(string path)
    {
        try
        {
            var doc = XDocument.Load(path);
            var root = doc.Root!;
            string? Prop(string name) => root.Descendants()
                .Where(e => e.Name.LocalName == name && e.Parent?.Name.LocalName == "PropertyGroup" &&
                            !e.AncestorsAndSelf().Any(a => a.Attribute("Condition") != null))
                .Select(e => e.Value.Trim())
                .LastOrDefault(v => v.Length > 0);

            var sdk = root.Attribute("Sdk")?.Value
                      ?? root.Elements().FirstOrDefault(e => e.Name.LocalName == "Sdk")?.Attribute("Name")?.Value;

            return new ProjectFile
            {
                Path = System.IO.Path.GetFullPath(path),
                Sdk = sdk,
                TargetFramework = Prop("TargetFramework"),
                TargetFrameworks = Prop("TargetFrameworks"),
                OutputType = Prop("OutputType"),
                AssemblyName = Prop("AssemblyName"),
                ImplicitUsings = Prop("ImplicitUsings"),
                Nullable = Prop("Nullable"),
                LangVersion = Prop("LangVersion"),
            };
        }
        catch (Exception ex)
        {
            return new ProjectFile { Path = System.IO.Path.GetFullPath(path), Error = ex.Message };
        }
    }

    /// <summary>Lê também propriedades importadas e condições pela avaliação do SDK, sem executar targets.</summary>
    public static async Task<ProjectFile> ReadEvaluatedAsync(string path, CancellationToken ct)
    {
        var quick = Read(path);
        if (quick.Error != null || Dotnet.FindExecutable() == null) return quick;
        try
        {
            var result = await Dotnet.RunAsync(
                ["msbuild", quick.Path, "-nologo", "-v:q", "-tl:off",
                 "-getProperty:TargetFramework,TargetFrameworks,OutputType,AssemblyName,ImplicitUsings,Nullable,LangVersion"],
                quick.Directory, ct).ConfigureAwait(false);
            var start = result.StandardOutput.IndexOf('{');
            if (result.ExitCode != 0 || start < 0) return quick;
            using var json = System.Text.Json.JsonDocument.Parse(result.StandardOutput[start..]);
            var properties = json.RootElement.GetProperty("Properties");
            string? Prop(string name) => properties.TryGetProperty(name, out var value) &&
                value.GetString() is { Length: > 0 } text ? text : null;
            return new ProjectFile
            {
                Path = quick.Path, Sdk = quick.Sdk,
                TargetFramework = Prop("TargetFramework"), TargetFrameworks = Prop("TargetFrameworks"),
                OutputType = Prop("OutputType"), AssemblyName = Prop("AssemblyName"),
                ImplicitUsings = Prop("ImplicitUsings"), Nullable = Prop("Nullable"), LangVersion = Prop("LangVersion"),
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Settings.AppPaths.Log(ex, "Lendo propriedades do projeto");
            return quick;
        }
    }
}
