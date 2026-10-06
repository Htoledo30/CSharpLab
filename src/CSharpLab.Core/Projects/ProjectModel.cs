namespace CSharpLab.Core.Projects;

public sealed record GlobalUsingItem(string Namespace, bool Static = false, string? Alias = null);

/// <summary>Tudo o que os serviços de linguagem e a execução precisam saber sobre um projeto.</summary>
public sealed record ProjectModel
{
    /// <summary>Null para rascunhos e arquivos sem projeto.</summary>
    public string? ProjectPath { get; init; }
    public required string Name { get; init; }
    public required string Directory { get; init; }
    public IReadOnlyList<string> CompileFiles { get; init; } = [];
    /// <summary>Vazio significa "usar as referências padrão do .NET".</summary>
    public IReadOnlyList<string> References { get; init; } = [];
    public IReadOnlyList<GlobalUsingItem> Usings { get; init; } = [];
    public string? TargetFramework { get; init; }
    public string? LangVersion { get; init; }
    public string? Nullable { get; init; }
    public string? OutputType { get; init; }
    public string? AssemblyName { get; init; }
    public bool AllowUnsafeBlocks { get; init; }
    public IReadOnlyList<string> DefineConstants { get; init; } = [];
    public IReadOnlyList<string> NoWarn { get; init; } = [];
    public bool TreatWarningsAsErrors { get; init; }
    public int WarningLevel { get; init; } = 10;
    public string? RunCommand { get; init; }
    public string? RunArguments { get; init; }
    public string? RunWorkingDirectory { get; init; }
    public string? TargetPath { get; init; }
    /// <summary>True quando veio da avaliação real do MSBuild; false quando é uma estimativa rápida.</summary>
    public bool IsEvaluated { get; init; }

    public bool IsConsole => string.Equals(OutputType, "Exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Verdadeiro se os dois modelos produzem a mesma compilação (arquivos, referências, opções).
    /// Usado para não reconstruir o projeto do Roslyn — e perder o que já estava calculado — sem necessidade.
    /// </summary>
    public bool HasSameAnalysisAs(ProjectModel? other)
    {
        if (other == null) return false;
        static bool Same(IEnumerable<string> a, IEnumerable<string> b) =>
            a.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(b);
        return string.Equals(ProjectPath, other.ProjectPath, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(Directory, other.Directory, StringComparison.OrdinalIgnoreCase) &&
               Name == other.Name && AssemblyName == other.AssemblyName &&
               TargetFramework == other.TargetFramework && LangVersion == other.LangVersion &&
               Nullable == other.Nullable && OutputType == other.OutputType &&
               AllowUnsafeBlocks == other.AllowUnsafeBlocks && TreatWarningsAsErrors == other.TreatWarningsAsErrors &&
               WarningLevel == other.WarningLevel &&
               Same(CompileFiles, other.CompileFiles) && Same(References, other.References) &&
               Same(DefineConstants, other.DefineConstants) && Same(NoWarn, other.NoWarn) &&
               Usings.ToHashSet().SetEquals(other.Usings);
    }

    public static readonly IReadOnlyList<GlobalUsingItem> ImplicitConsoleUsings =
    [
        new("System"),
        new("System.Collections.Generic"),
        new("System.IO"),
        new("System.Linq"),
        new("System.Net.Http"),
        new("System.Threading"),
        new("System.Threading.Tasks"),
    ];

    /// <summary>Contexto em memória para rascunhos e arquivos soltos: console .NET 10 padrão.</summary>
    public static ProjectModel Loose(string name, string directory) => new()
    {
        Name = name,
        Directory = directory,
        TargetFramework = ProjectCreator.TargetFramework,
        OutputType = "Exe",
        Nullable = "enable",
        Usings = ImplicitConsoleUsings,
        DefineConstants = ["DEBUG", "TRACE", .. ImplicitDefines(ProjectCreator.TargetFramework)],
    };

    /// <summary>Símbolos que o SDK define automaticamente para o framework (NET, NET10_0, NET10_0_OR_GREATER…).</summary>
    public static IReadOnlyList<string> ImplicitDefines(string? targetFramework)
    {
        var major = SdkLocator.RequiredMajorFor(targetFramework);
        if (major < 5) return [];
        var list = new List<string> { "NET", $"NET{major}_0", "NETCOREAPP" };
        for (int v = 5; v <= major; v++) list.Add($"NET{v}_0_OR_GREATER");
        list.AddRange(["NETCOREAPP1_0_OR_GREATER", "NETCOREAPP1_1_OR_GREATER", "NETCOREAPP2_0_OR_GREATER",
            "NETCOREAPP2_1_OR_GREATER", "NETCOREAPP2_2_OR_GREATER", "NETCOREAPP3_0_OR_GREATER", "NETCOREAPP3_1_OR_GREATER"]);
        return list;
    }
}
