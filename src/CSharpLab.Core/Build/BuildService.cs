using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using CSharpLab.Core.Projects;
using CSharpLab.Core.Settings;
using CSharpLab.Core.Terminal;

namespace CSharpLab.Core.Build;

public enum BuildOutcome
{
    Succeeded,
    Failed,
    RestoreFailed,
    Cancelled,
}

public enum BuildSeverity
{
    Error,
    Warning,
}

public sealed record BuildDiagnostic(
    string Id,
    BuildSeverity Severity,
    string Message,
    string? FilePath,
    int Line,
    int Column,
    int EndLine,
    int EndColumn);

public sealed record BuildResult(BuildOutcome Outcome, IReadOnlyList<BuildDiagnostic> Diagnostics, string Log, TimeSpan Elapsed)
{
    public bool Success => Outcome == BuildOutcome.Succeeded;
}

/// <summary>Compila com o SDK real. O resultado desta compilação decide se o programa pode rodar.</summary>
public static partial class BuildService
{
    [GeneratedRegex(@"^\s*(?<file>.+?)\((?<line>\d+),(?<col>\d+)(?:,(?<eline>\d+),(?<ecol>\d+))?\)\s*:\s*(?<sev>error|warning)\s+(?<code>[A-Za-z]+\d+)\s*:\s*(?<msg>.*?)(?:\s+\[(?<proj>[^\[\]]+)\])?\s*$")]
    private static partial Regex LocatedLine();

    [GeneratedRegex(@"^\s*(?:(?<file>.+?)\s*:\s*)?(?<sev>error|warning)\s+(?<code>[A-Za-z]+\d+)\s*:\s*(?<msg>.*?)(?:\s+\[(?<proj>[^\[\]]+)\])?\s*$")]
    private static partial Regex UnlocatedLine();

    public static async Task<BuildResult> BuildAsync(string projectPath, Action<string>? progress, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var dir = Path.GetDirectoryName(projectPath)!;
        try
        {
            if (ProjectEvaluator.NeedsRestore(projectPath))
            {
                progress?.Invoke("Restaurando dependências…");
                var restore = await ProjectEvaluator.RestoreAsync(projectPath, ct).ConfigureAwait(false);
                if (restore.ExitCode != 0)
                {
                    var diags = ParseText(restore.Combined, dir);
                    return new BuildResult(BuildOutcome.RestoreFailed, diags, restore.Combined.Trim(), sw.Elapsed);
                }
            }

            progress?.Invoke("Compilando…");
            var sarif = Path.Combine(AppPaths.BuildDir, Guid.NewGuid().ToString("N") + ".sarif");
            var args = new List<string>
            {
                "build", projectPath, "--no-restore", "-nologo", "-v:q", "-tl:off", "-clp:NoSummary",
                "-p:ErrorLog=" + EscapeMsBuild(sarif) + "%2Cversion%3D2.1",
            };
            ProcessResult result;
            try
            {
                result = await Dotnet.RunAsync(args, dir, ct).ConfigureAwait(false);
                var diagnostics = MergeDiagnostics(ReadSarif(sarif), ParseText(result.Combined, dir));
                bool ok = result.ExitCode == 0 && diagnostics.All(d => d.Severity != BuildSeverity.Error);
                if (result.ExitCode != 0 && diagnostics.All(d => d.Severity != BuildSeverity.Error))
                {
                    // Falhou sem um erro reconhecível: mostra o log como erro do projeto.
                    diagnostics = [.. diagnostics, new BuildDiagnostic("BUILD", BuildSeverity.Error,
                        "A compilação falhou. Veja os detalhes.", null, 0, 0, 0, 0)];
                }
                return new BuildResult(ok ? BuildOutcome.Succeeded : BuildOutcome.Failed, diagnostics, result.Combined.Trim(), sw.Elapsed);
            }
            finally
            {
                try { File.Delete(sarif); } catch { }
            }
        }
        catch (OperationCanceledException)
        {
            return new BuildResult(BuildOutcome.Cancelled, [], "", sw.Elapsed);
        }
    }

    /// <summary>Escapa caracteres especiais do MSBuild num valor de propriedade.</summary>
    public static string EscapeMsBuild(string value) =>
        value.Replace("%", "%25").Replace(";", "%3B").Replace(",", "%2C").Replace("$", "%24")
             .Replace("@", "%40").Replace("'", "%27").Replace("=", "%3D");

    internal static List<BuildDiagnostic> ReadSarif(string path)
    {
        var list = new List<BuildDiagnostic>();
        if (!File.Exists(path)) return list;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var run in doc.RootElement.GetProperty("runs").EnumerateArray())
            {
                if (!run.TryGetProperty("results", out var results)) continue;
                foreach (var r in results.EnumerateArray())
                {
                    if (r.TryGetProperty("suppressions", out var sup) && sup.GetArrayLength() > 0) continue;
                    var level = r.TryGetProperty("level", out var lv) ? lv.GetString() : "warning";
                    if (level is not ("error" or "warning")) continue;
                    var id = r.TryGetProperty("ruleId", out var rid) ? rid.GetString() ?? "" : "";
                    var message = r.TryGetProperty("message", out var m) && m.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
                    string? file = null;
                    int line = 0, col = 0, eline = 0, ecol = 0;
                    if (r.TryGetProperty("locations", out var locs) && locs.GetArrayLength() > 0 &&
                        locs[0].TryGetProperty("physicalLocation", out var phys))
                    {
                        if (phys.TryGetProperty("artifactLocation", out var art) && art.TryGetProperty("uri", out var uri) &&
                            Uri.TryCreate(uri.GetString(), UriKind.Absolute, out var u) && u.IsFile)
                        {
                            file = u.LocalPath;
                        }
                        if (phys.TryGetProperty("region", out var region))
                        {
                            line = Int(region, "startLine");
                            col = Int(region, "startColumn");
                            eline = Int(region, "endLine");
                            ecol = Int(region, "endColumn");
                        }
                    }
                    list.Add(new BuildDiagnostic(id, level == "error" ? BuildSeverity.Error : BuildSeverity.Warning,
                        message, file, line, col, eline == 0 ? line : eline, ecol == 0 ? col : ecol));
                }
            }
        }
        catch (Exception ex)
        {
            AppPaths.Log(ex, "Lendo SARIF");
        }
        return list;

        static int Int(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : 0;
    }

    internal static List<BuildDiagnostic> ParseText(string output, string projectDirectory)
    {
        var list = new List<BuildDiagnostic>();
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;
            var m = LocatedLine().Match(line);
            if (m.Success)
            {
                var file = m.Groups["file"].Value.Trim();
                if (!Path.IsPathRooted(file)) file = Path.GetFullPath(Path.Combine(projectDirectory, file));
                int l = int.Parse(m.Groups["line"].Value), c = int.Parse(m.Groups["col"].Value);
                list.Add(new BuildDiagnostic(m.Groups["code"].Value, Sev(m), m.Groups["msg"].Value.Trim(), file, l, c,
                    m.Groups["eline"].Success ? int.Parse(m.Groups["eline"].Value) : l,
                    m.Groups["ecol"].Success ? int.Parse(m.Groups["ecol"].Value) : c));
                continue;
            }
            m = UnlocatedLine().Match(line);
            if (m.Success)
            {
                list.Add(new BuildDiagnostic(m.Groups["code"].Value, Sev(m), m.Groups["msg"].Value.Trim(), null, 0, 0, 0, 0));
            }
        }
        return list.Distinct().ToList();

        static BuildSeverity Sev(Match m) => m.Groups["sev"].Value == "error" ? BuildSeverity.Error : BuildSeverity.Warning;
    }

    /// <summary>SARIF (estruturado) tem prioridade; do texto entram só os diagnósticos que ele não trouxe.</summary>
    internal static List<BuildDiagnostic> MergeDiagnostics(List<BuildDiagnostic> sarif, List<BuildDiagnostic> text)
    {
        var result = new List<BuildDiagnostic>(sarif);
        var seen = sarif.Select(d => (d.Id, File: d.FilePath?.ToLowerInvariant(), d.Line)).ToHashSet();
        foreach (var d in text)
        {
            if (seen.Add((d.Id, d.FilePath?.ToLowerInvariant(), d.Line)))
                result.Add(d);
        }
        return result
            .OrderBy(d => d.Severity)
            .ThenBy(d => d.FilePath ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Line)
            .ThenBy(d => d.Column)
            .ToList();
    }

    /// <summary>Comando para iniciar o programa já compilado, sem compilar de novo.</summary>
    public static async Task<ProcessLaunch> GetLaunchAsync(ProjectModel model, CancellationToken ct)
    {
        var projectPath = model.ProjectPath!;
        var dir = Path.GetDirectoryName(projectPath)!;
        string? command = model.RunCommand, arguments = model.RunArguments, workDir = model.RunWorkingDirectory;

        if (!model.IsEvaluated || string.IsNullOrEmpty(command))
        {
            var result = await Dotnet.RunAsync(
                ["msbuild", projectPath, "-nologo", "-v:q", "-tl:off",
                 "-getProperty:RunCommand", "-getProperty:RunArguments", "-getProperty:RunWorkingDirectory"],
                dir, ct).ConfigureAwait(false);
            var start = result.StandardOutput.IndexOf('{');
            if (result.ExitCode != 0 || start < 0)
                throw new InvalidOperationException("Não foi possível descobrir como iniciar o programa compilado.");
            using var doc = JsonDocument.Parse(result.StandardOutput[start..]);
            var props = doc.RootElement.GetProperty("Properties");
            command = props.TryGetProperty("RunCommand", out var c) ? c.GetString() : null;
            arguments = props.TryGetProperty("RunArguments", out var a) ? a.GetString() : null;
            workDir = props.TryGetProperty("RunWorkingDirectory", out var w) ? w.GetString() : null;
        }

        if (string.IsNullOrWhiteSpace(command))
            throw new InvalidOperationException("O projeto não informa um programa executável.");
        if (!Path.IsPathRooted(command))
            command = command.Equals("dotnet", StringComparison.OrdinalIgnoreCase) ? Dotnet.FindExecutable()! : Path.GetFullPath(Path.Combine(dir, command));

        // A pasta de trabalho é a do projeto, salvo se o próprio projeto definir outra.
        var workingDirectory = string.IsNullOrWhiteSpace(workDir) ? dir : Path.GetFullPath(Path.Combine(dir, workDir));
        return new ProcessLaunch(command, SplitArguments(arguments), workingDirectory);
    }

    public static IReadOnlyList<string> SplitArguments(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return [];
        // argv[0] tem regras próprias; um nome fictício garante as regras normais para o resto.
        var argv = CommandLineToArgvW("x " + commandLine, out int count);
        if (argv == IntPtr.Zero) return [commandLine];
        try
        {
            var result = new string[Math.Max(0, count - 1)];
            for (int i = 1; i < count; i++)
                result[i - 1] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size))!;
            return result;
        }
        finally
        {
            LocalFree(argv);
        }
    }

    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CommandLineToArgvW(string lpCmdLine, out int pNumArgs);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);
}
