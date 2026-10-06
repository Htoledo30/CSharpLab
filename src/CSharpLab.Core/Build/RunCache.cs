using CSharpLab.Core.Terminal;

namespace CSharpLab.Core.Build;

/// <summary>
/// O que o F5 já conferiu nesta sessão, para não repetir trabalho: SDKs que servem para cada
/// pasta e a última compilação bem-sucedida (reexecutada direto se nada mudou desde então).
/// </summary>
public sealed class RunCache
{
    private readonly HashSet<string> _sdkOk = new(StringComparer.OrdinalIgnoreCase);
    private (string ProjectPath, string Fingerprint, ProcessLaunch Launch)? _lastBuild;

    public bool IsSdkVerified(string directory, int major) => _sdkOk.Contains(SdkKey(directory, major));

    public void MarkSdkVerified(string directory, int major) => _sdkOk.Add(SdkKey(directory, major));

    /// <summary>O programa da última compilação, se o projeto e suas entradas são os mesmos e o .exe/.dll ainda existe.</summary>
    public ProcessLaunch? TryReuse(string projectPath, string fingerprint) =>
        _lastBuild is { } last &&
        string.Equals(last.ProjectPath, projectPath, StringComparison.OrdinalIgnoreCase) &&
        last.Fingerprint == fingerprint && File.Exists(last.Launch.FileName)
            ? last.Launch
            : null;

    public void RememberBuild(string projectPath, string fingerprint, ProcessLaunch launch) =>
        _lastBuild = (projectPath, fingerprint, launch);

    public void ForgetBuild() => _lastBuild = null;

    /// <summary>A definição do projeto ou o SDK mudou: tudo precisa ser conferido de novo.</summary>
    public void Clear()
    {
        _sdkOk.Clear();
        _lastBuild = null;
    }

    private static string SdkKey(string directory, int major) => directory + "|" + major;
}
