using System.Text.RegularExpressions;

namespace CSharpLab.Core.Projects;

public sealed record SdkStatus(string? DotnetPath, IReadOnlyList<Version> Sdks, Version? Resolved, string? Problem, string? Detail = null)
{
    public bool CanBuild => Problem == null;
    public bool IsMissingSdk { get; init; }
}

public static partial class SdkLocator
{
    public const int DefaultMajor = 10;
    public const string DownloadUrl = "https://dotnet.microsoft.com/pt-br/download/dotnet/10.0";
    public const string MissingMessage = "Instale o SDK .NET 10 para executar C#";

    [GeneratedRegex(@"^(?<v>\d+\.\d+\.\d+)(?<pre>-\S+)?\s+\[(?<path>.+)\]\s*$", RegexOptions.Multiline)]
    private static partial Regex SdkLine();

    [GeneratedRegex(@"^\s*(?<v>\d+\.\d+\.\d+)(?<pre>-\S+)?\s*$", RegexOptions.Multiline)]
    private static partial Regex VersionLine();

    /// <summary>
    /// Verifica se existe um SDK capaz de compilar um projeto que exige o major informado,
    /// considerando o global.json que afeta a pasta do projeto.
    /// </summary>
    public static async Task<SdkStatus> CheckAsync(string? projectDirectory, int requiredMajor = DefaultMajor, CancellationToken ct = default)
    {
        Dotnet.ResetCache();
        var exe = Dotnet.FindExecutable();
        if (exe == null)
            return new SdkStatus(null, [], null, MissingMessage) { IsMissingSdk = true };

        ProcessResult list;
        try
        {
            list = await Dotnet.RunAsync(["--list-sdks"], projectDirectory, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new SdkStatus(exe, [], null, MissingMessage, ex.Message) { IsMissingSdk = true };
        }

        var sdks = SdkLine().Matches(list.StandardOutput)
            .Where(m => !m.Groups["pre"].Success)
            .Select(m => Version.Parse(m.Groups["v"].Value))
            .OrderByDescending(v => v)
            .ToList();
        if (sdks.Count == 0)
            return new SdkStatus(exe, sdks, null, MissingMessage) { IsMissingSdk = true };

        if (requiredMajor > 0 && sdks.All(v => v.Major < requiredMajor))
        {
            var message = requiredMajor == DefaultMajor
                ? MissingMessage
                : $"Instale o SDK .NET {requiredMajor} para executar este projeto";
            return new SdkStatus(exe, sdks, null, message) { IsMissingSdk = true };
        }

        // "dotnet --version" respeita o global.json da pasta.
        var version = await Dotnet.RunAsync(["--version"], projectDirectory, ct).ConfigureAwait(false);
        var match = VersionLine().Match(version.StandardOutput);
        if (version.ExitCode != 0 || !match.Success)
        {
            return new SdkStatus(exe, sdks, null,
                "O global.json desta pasta pede uma versão do SDK que não está instalada.",
                version.Combined.Trim());
        }

        var resolved = Version.Parse(match.Groups["v"].Value);
        if (requiredMajor > 0 && resolved.Major < requiredMajor)
        {
            return new SdkStatus(exe, sdks, resolved,
                $"Este projeto precisa do .NET {requiredMajor}, mas o global.json seleciona o SDK {resolved}.");
        }

        return new SdkStatus(exe, sdks, resolved, null);
    }

    /// <summary>"net10.0" → 10; "netcoreapp3.1" → 3; demais → 0 (sem exigência conhecida).</summary>
    public static int RequiredMajorFor(string? targetFramework)
    {
        if (string.IsNullOrWhiteSpace(targetFramework)) return DefaultMajor;
        var tfm = targetFramework.Trim().ToLowerInvariant();
        var m = Regex.Match(tfm, @"^net(coreapp)?(?<major>\d+)\.\d+");
        if (m.Success && int.TryParse(m.Groups["major"].Value, out var major) && (major >= 5 || m.Groups[1].Success))
            return major;
        return 0;
    }
}
