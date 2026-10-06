using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CSharpLab.Core.Settings;

namespace CSharpLab.Core.Projects;

/// <summary>
/// Obtém do próprio SDK (dotnet msbuild -getProperty/-getItem) os arquivos, referências e
/// opções reais do projeto. Há também uma estimativa imediata, usada enquanto o SDK responde
/// ou quando ele não está disponível.
/// </summary>
public static class ProjectEvaluator
{
    private static readonly string[] Properties =
    [
        "TargetFramework", "LangVersion", "Nullable", "OutputType", "AssemblyName", "AllowUnsafeBlocks",
        "DefineConstants", "NoWarn", "TreatWarningsAsErrors", "ImplicitUsings", "RunCommand", "RunArguments",
        "RunWorkingDirectory", "TargetPath", "WarningLevel",
    ];

    private static readonly JsonSerializerOptions CacheJson = new() { WriteIndented = false };

    /// <summary>Estimativa instantânea a partir do .csproj, sem chamar o SDK.</summary>
    public static ProjectModel Estimate(string projectPath)
    {
        var file = ProjectFile.Read(projectPath);
        var implicitUsings = string.Equals(file.ImplicitUsings, "enable", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(file.ImplicitUsings, "true", StringComparison.OrdinalIgnoreCase);
        var tfm = file.EffectiveTargetFramework ?? ProjectCreator.TargetFramework;
        return new ProjectModel
        {
            ProjectPath = file.Path,
            Name = file.Name,
            Directory = file.Directory,
            CompileFiles = ProjectLocator.DefaultCompileFiles(file.Directory),
            References = ReferencePacks.Find(tfm),
            Usings = implicitUsings ? ProjectModel.ImplicitConsoleUsings : [],
            TargetFramework = tfm,
            LangVersion = file.LangVersion,
            Nullable = file.Nullable,
            OutputType = file.OutputType,
            AssemblyName = file.AssemblyName ?? file.Name,
            DefineConstants = ["DEBUG", "TRACE", .. ProjectModel.ImplicitDefines(tfm)],
        };
    }

    /// <summary>Lê o resultado da última avaliação, se o projeto não mudou desde então.</summary>
    public static ProjectModel? TryLoadCached(string projectPath)
    {
        try
        {
            var cacheFile = CacheFile(projectPath);
            if (!File.Exists(cacheFile)) return null;
            var cached = JsonSerializer.Deserialize<CachedModel>(File.ReadAllText(cacheFile), CacheJson);
            if (cached == null || cached.Key != CacheKey(projectPath)) return null;
            if (cached.Model.References.Any(r => !File.Exists(r))) return null;
            // Arquivos .cs criados ou apagados desde a avaliação: a lista do MSBuild pode ter mudado
            // (globs, <Compile Remove>), então o cache não serve; o projeto é avaliado de novo.
            var onDisk = ProjectLocator.DefaultCompileFiles(cached.Model.Directory);
            if (!onDisk.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(cached.DiskFiles)) return null;
            return cached.Model;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Restore é necessário se não há project.assets.json ou se algum arquivo que define
    /// dependências (o .csproj, Directory.Build.*, Directory.Packages.props, NuGet.config,
    /// global.json…) mudou depois dele.
    /// </summary>
    public static bool NeedsRestore(string projectPath)
    {
        var assets = Path.Combine(Path.GetDirectoryName(projectPath)!, "obj", "project.assets.json");
        if (!File.Exists(assets)) return true;
        var assetsTime = File.GetLastWriteTimeUtc(assets);
        return RestoreInputs(projectPath).Any(f => File.GetLastWriteTimeUtc(f) > assetsTime);
    }

    private static readonly string[] AncestorInputs =
    [
        "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props",
        "NuGet.config", "nuget.config", "NuGet.Config", "global.json",
    ];

    /// <summary>O .csproj e os arquivos acima dele que influenciam restore e avaliação.</summary>
    public static IReadOnlyList<string> RestoreInputs(string projectPath)
    {
        var list = new List<string> { projectPath };
        var dir = Path.GetDirectoryName(projectPath)!;
        var lockFile = Path.Combine(dir, "packages.lock.json");
        if (File.Exists(lockFile)) list.Add(lockFile);
        for (var d = dir; !string.IsNullOrEmpty(d); d = Path.GetDirectoryName(d))
        {
            foreach (var name in AncestorInputs)
            {
                var f = Path.Combine(d, name);
                if (File.Exists(f) && !list.Contains(f, StringComparer.OrdinalIgnoreCase)) list.Add(f);
            }
        }
        return list;
    }

    public static async Task<ProcessResult> RestoreAsync(string projectPath, CancellationToken ct) =>
        await Dotnet.RunAsync(["restore", projectPath, "-nologo", "-v:q", "-tl:off"], Path.GetDirectoryName(projectPath), ct)
            .ConfigureAwait(false);

    /// <summary>Avaliação real pelo SDK. Se não for possível, devolve a estimativa.</summary>
    public static async Task<ProjectModel> EvaluateAsync(string projectPath, bool allowRestore, CancellationToken ct)
    {
        projectPath = Path.GetFullPath(projectPath);
        if (Dotnet.FindExecutable() == null)
            return Estimate(projectPath);

        bool restored = !NeedsRestore(projectPath);
        if (!restored && allowRestore)
        {
            try
            {
                var restore = await RestoreAsync(projectPath, ct).ConfigureAwait(false);
                restored = restore.ExitCode == 0;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppPaths.Log(ex, "Restore para análise");
            }
        }

        var args = new List<string> { "msbuild", projectPath, "-nologo", "-v:q", "-tl:off" };
        if (restored)
            args.Add("-t:ResolveAssemblyReferences");
        args.AddRange(Properties.Select(p => "-getProperty:" + p));
        args.AddRange(["-getItem:Compile", "-getItem:Using"]);
        if (restored)
            args.Add("-getItem:ReferencePath");

        try
        {
            var result = await Dotnet.RunAsync(args, Path.GetDirectoryName(projectPath), ct).ConfigureAwait(false);
            var json = result.StandardOutput;
            int start = json.IndexOf('{');
            if (result.ExitCode != 0 || start < 0)
                return Estimate(projectPath);

            var model = Parse(projectPath, json[start..], restored);
            if (restored)
                SaveCache(projectPath, model);
            return model;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppPaths.Log(ex, "Avaliação do projeto");
            return Estimate(projectPath);
        }
    }

    internal static ProjectModel Parse(string projectPath, string json, bool hasReferences)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var props = root.TryGetProperty("Properties", out var p) ? p : default;
        string? Prop(string name) =>
            props.ValueKind == JsonValueKind.Object && props.TryGetProperty(name, out var v) && v.GetString() is { Length: > 0 } s ? s : null;

        var items = root.TryGetProperty("Items", out var i) ? i : default;
        IEnumerable<JsonElement> Items(string name) =>
            items.ValueKind == JsonValueKind.Object && items.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array
                ? arr.EnumerateArray()
                : [];
        static string? Meta(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;

        var dir = Path.GetDirectoryName(projectPath)!;
        var compile = Items("Compile")
            .Select(e => Meta(e, "FullPath"))
            .OfType<string>()
            .Where(f => !ProjectLocator.IsInsideSkippedDirectory(f, dir))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var usings = Items("Using")
            .Select(e => new GlobalUsingItem(
                Meta(e, "Identity") ?? "",
                string.Equals(Meta(e, "Static"), "true", StringComparison.OrdinalIgnoreCase),
                Meta(e, "Alias")))
            .Where(u => u.Namespace.Length > 0)
            .ToList();

        var tfm = Prop("TargetFramework");
        var references = hasReferences
            ? Items("ReferencePath").Select(e => Meta(e, "FullPath")).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : ReferencePacks.Find(tfm);

        static IReadOnlyList<string> SplitList(string? s) =>
            (s ?? "").Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new ProjectModel
        {
            ProjectPath = projectPath,
            Name = Path.GetFileNameWithoutExtension(projectPath),
            Directory = dir,
            CompileFiles = compile,
            References = references,
            Usings = usings,
            TargetFramework = tfm,
            LangVersion = Prop("LangVersion"),
            Nullable = Prop("Nullable"),
            OutputType = Prop("OutputType"),
            AssemblyName = Prop("AssemblyName"),
            AllowUnsafeBlocks = string.Equals(Prop("AllowUnsafeBlocks"), "true", StringComparison.OrdinalIgnoreCase),
            DefineConstants = [.. SplitList(Prop("DefineConstants")), .. ProjectModel.ImplicitDefines(tfm)],
            WarningLevel = int.TryParse(Prop("WarningLevel"), out var wl) ? wl : 4,
            NoWarn = SplitList(Prop("NoWarn")),
            TreatWarningsAsErrors = string.Equals(Prop("TreatWarningsAsErrors"), "true", StringComparison.OrdinalIgnoreCase),
            RunCommand = Prop("RunCommand"),
            RunArguments = Prop("RunArguments"),
            RunWorkingDirectory = Prop("RunWorkingDirectory"),
            TargetPath = Prop("TargetPath"),
            IsEvaluated = true,
        };
    }

    private sealed record CachedModel(string Key, ProjectModel Model, IReadOnlyList<string> DiskFiles);

    private static string CacheFile(string projectPath)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(projectPath.ToLowerInvariant())))[..16];
        return Path.Combine(AppPaths.CacheDir, "proj-" + hash + ".json");
    }

    private static string CacheKey(string projectPath)
    {
        var dir = Path.GetDirectoryName(projectPath)!;
        var assets = Path.Combine(dir, "obj", "project.assets.json");
        long Ticks(string f) => File.Exists(f) ? File.GetLastWriteTimeUtc(f).Ticks : 0;
        var parts = new List<string> { Ticks(assets).ToString(), Dotnet.Root ?? "" };
        parts.AddRange(RestoreInputs(projectPath).Select(f => f + Ticks(f)));
        return string.Join('|', parts);
    }

    private static void SaveCache(string projectPath, ProjectModel model)
    {
        try
        {
            AppPaths.WriteAllTextAtomic(CacheFile(projectPath), JsonSerializer.Serialize(
                new CachedModel(CacheKey(projectPath), model, ProjectLocator.DefaultCompileFiles(model.Directory)), CacheJson));
        }
        catch (Exception ex)
        {
            AppPaths.Log(ex, "Cache do projeto");
        }
    }
}

/// <summary>Localiza os assemblies de referência do .NET instalados com o SDK.</summary>
public static class ReferencePacks
{
    private static readonly Dictionary<string, IReadOnlyList<string>> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> Find(string? targetFramework)
    {
        var major = SdkLocator.RequiredMajorFor(targetFramework);
        if (major < 5) major = SdkLocator.DefaultMajor;
        var key = major.ToString();
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var cached)) return cached;
        }

        IReadOnlyList<string> result = [];
        try
        {
            var root = Dotnet.Root;
            var packs = root == null ? null : Path.Combine(root, "packs", "Microsoft.NETCore.App.Ref");
            if (packs != null && Directory.Exists(packs))
            {
                var best = Directory.EnumerateDirectories(packs)
                    .Select(d => (Dir: d, Ok: Version.TryParse(Path.GetFileName(d).Split('-')[0], out var v), Version: v))
                    .Where(x => x.Ok && x.Version!.Major == major)
                    .OrderByDescending(x => x.Version)
                    .FirstOrDefault();
                if (best.Dir != null)
                {
                    var refDir = Path.Combine(best.Dir, "ref", $"net{major}.0");
                    if (Directory.Exists(refDir))
                        result = Directory.GetFiles(refDir, "*.dll");
                }
            }
        }
        catch (Exception ex)
        {
            AppPaths.Log(ex, "Procurando referências do .NET");
        }

        lock (Cache) Cache[key] = result;
        return result;
    }
}
