using System.Text.Json;
using System.Text.Json.Serialization;

namespace CSharpLab.Core.Settings;

public enum RecentKind
{
    Folder,
    Project,
}

public sealed record RecentItem(string Path, RecentKind Kind)
{
    [JsonIgnore]
    public string Name =>
        (Kind == RecentKind.Project ? System.IO.Path.GetFileNameWithoutExtension(Path) : System.IO.Path.GetFileName(Path.TrimEnd('\\')))
            is { Length: > 0 } n ? n : Path;
}

public sealed class WindowPlacement
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Maximized { get; set; }
}

public sealed class SessionState
{
    public string? Folder { get; set; }
    public List<string> Files { get; set; } = [];
    public string? ActiveFile { get; set; }
}

public sealed class AppSettings
{
    public WindowPlacement? Window { get; set; }
    public double ExplorerWidth { get; set; } = 230;
    public bool ExplorerVisible { get; set; } = true;
    /// <summary>0 significa "25% da altura" na primeira abertura do painel.</summary>
    public double PanelHeight { get; set; }
    public bool PanelOpen { get; set; }
    public string PanelTab { get; set; } = "terminal";
    public double FontSize { get; set; } = 15;
    public List<RecentItem> Recent { get; set; } = [];
    public SessionState? Session { get; set; }
    /// <summary>Projeto escolhido para executar em cada pasta com vários projetos.</summary>
    public Dictionary<string, string> ProjectChoices { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? LastProjectLocation { get; set; }
    /// <summary>Procura uma versão nova nas Releases do GitHub ao abrir.</summary>
    public bool CheckForUpdates { get; set; } = true;
    /// <summary>Versão aberta da última vez (para avisar "atualizado para…").</summary>
    public string? LastRunVersion { get; set; }
    /// <summary>Quantas vezes a dica "a saída aparece aqui" já foi mostrada no terminal.</summary>
    public int TerminalHintRuns { get; set; }

    public const int MaxRecent = 10;

    public void AddRecent(string path, RecentKind kind)
    {
        path = System.IO.Path.GetFullPath(path).TrimEnd('\\');
        Recent.RemoveAll(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
        Recent.Insert(0, new RecentItem(path, kind));
        if (Recent.Count > MaxRecent)
            Recent.RemoveRange(MaxRecent, Recent.Count - MaxRecent);
    }
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppSettings Load(string? path = null)
    {
        path ??= AppPaths.SettingsFile;
        try
        {
            if (File.Exists(path))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options);
                if (settings != null)
                {
                    settings.ProjectChoices = new Dictionary<string, string>(settings.ProjectChoices, StringComparer.OrdinalIgnoreCase);
                    settings.Recent = settings.Recent
                        .Where(r => !string.IsNullOrWhiteSpace(r.Path))
                        .DistinctBy(r => r.Path, StringComparer.OrdinalIgnoreCase)
                        .Take(AppSettings.MaxRecent)
                        .ToList();
                    settings.FontSize = Math.Clamp(settings.FontSize, 9, 32);
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            // Preferências corrompidas não podem impedir a abertura.
            AppPaths.Log(ex, "Lendo preferências");
            try { File.Copy(path, path + ".corrompido", true); } catch { }
        }
        return new AppSettings();
    }

    public static void Save(AppSettings settings, string? path = null)
    {
        path ??= AppPaths.SettingsFile;
        try
        {
            AppPaths.WriteAllTextAtomic(path, JsonSerializer.Serialize(settings, Options));
        }
        catch (Exception ex)
        {
            AppPaths.Log(ex, "Salvando preferências");
        }
    }
}
