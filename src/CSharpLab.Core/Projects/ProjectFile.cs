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
    public bool IsMultiTarget => string.IsNullOrEmpty(TargetFramework) && !string.IsNullOrEmpty(TargetFrameworks);
    public string? EffectiveTargetFramework => TargetFramework ?? TargetFrameworks?.Split(';', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

    public static ProjectFile Read(string path)
    {
        try
        {
            var doc = XDocument.Load(path);
            var root = doc.Root!;
            string? Prop(string name) => root.Descendants()
                .Where(e => e.Name.LocalName == name && e.Parent?.Name.LocalName == "PropertyGroup" && e.Attribute("Condition") == null)
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
}
