using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CSharpLab.Core.Settings;

namespace CSharpLab.Core.Updates;

public sealed record UpdateInfo(Version Version, string Tag, string PackageUrl, string? Sha256, string? Notes);

/// <summary>
/// Atualizações pelas Releases do GitHub: consulta a última versão, baixa o pacote, confere o
/// SHA-256 e prepara uma cópia nova do aplicativo. A troca dos arquivos é feita por um script
/// separado, depois que o editor fecha (os arquivos em uso não podem ser substituídos).
/// </summary>
public static class UpdateService
{
    public const string PackageName = "CSharpLab-win-x64.zip";
    public const string ExeName = "CSharpLab.exe";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CSharpLab", CurrentVersion.ToString(3)));
        http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2026-03-10");
        return http;
    }

    public static Version CurrentVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version is { } v ? new Version(v.Major, v.Minor, Math.Max(0, v.Build)) : new Version(1, 0, 0);

    /// <summary>Repositório "usuario/repositorio" gravado no executável na publicação (vazio = desligado).</summary>
    public static string? Repository
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("CSHARPLAB_UPDATE_REPO");
            if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
            var meta = Assembly.GetEntryAssembly()?.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "UpdateRepository")?.Value;
            return string.IsNullOrWhiteSpace(meta) ? null : meta.Trim();
        }
    }

    /// <summary>Endereço consultado: a API do GitHub, ou um arquivo JSON local (para testes).</summary>
    public static string? FeedUrl =>
        Environment.GetEnvironmentVariable("CSHARPLAB_UPDATE_FEED") is { Length: > 0 } feed
            ? feed
            : Repository is { } repo ? $"https://api.github.com/repos/{repo}/releases/latest" : null;

    public static bool IsConfigured => FeedUrl != null;

    public static string UpdatesDir => Path.Combine(AppPaths.Root, "Updates");

    /// <summary>Retorna a versão nova, ou null se já está na mais recente.</summary>
    public static async Task<UpdateInfo?> CheckAsync(Version current, CancellationToken ct)
    {
        var feed = FeedUrl;
        if (feed == null) return null;
        string json;
        try { json = await ReadTextAsync(feed, ct).ConfigureAwait(false); }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { return null; }
        var info = ParseRelease(json);
        if (info == null || info.Version <= current) return null;
        if (info.Sha256 == null)
        {
            // Sem "digest" na API: usa o arquivo .sha256 publicado junto.
            var shaUrl = FindAssetUrl(json, PackageName + ".sha256");
            if (shaUrl != null)
            {
                var text = await ReadTextAsync(shaUrl, ct).ConfigureAwait(false);
                info = info with { Sha256 = text.Split((char[])[' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() };
            }
        }
        return info;
    }

    internal static UpdateInfo? ParseRelease(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) return null;
        if (root.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True) return null;
        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
        if (!TryParseVersion(tag, out var version)) return null;

        string? url = null, sha = null;
        if (root.TryGetProperty("assets", out var assets))
        {
            foreach (var a in assets.EnumerateArray())
            {
                if (a.TryGetProperty("name", out var n) && n.GetString() == PackageName)
                {
                    url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                    if (a.TryGetProperty("digest", out var d) && d.GetString() is { } digest &&
                        digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                    {
                        sha = digest[7..];
                    }
                }
            }
        }
        if (url == null) return null;
        var notes = root.TryGetProperty("body", out var b) ? b.GetString() : null;
        return new UpdateInfo(version, tag, url, sha, notes);
    }

    private static string? FindAssetUrl(string json, string name)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("assets", out var assets)) return null;
        foreach (var a in assets.EnumerateArray())
        {
            if (a.TryGetProperty("name", out var n) && n.GetString() == name && a.TryGetProperty("browser_download_url", out var u))
                return u.GetString();
        }
        return null;
    }

    public static bool TryParseVersion(string tag, out Version version)
    {
        var text = tag.Trim();
        if (text.StartsWith('v') || text.StartsWith('V')) text = text[1..];
        var plus = text.IndexOf('+');
        if (plus >= 0) text = text[..plus];
        if (!text.Contains('-') && Version.TryParse(text, out var v) && v.Build >= 0 && v.Revision < 0)
        {
            version = new Version(v.Major, v.Minor, Math.Max(0, v.Build));
            return true;
        }
        version = new Version(0, 0, 0);
        return false;
    }

    /// <summary>
    /// Baixa e confere o pacote e extrai a pasta "app". Retorna o caminho da cópia preparada.
    /// Pacote sem SHA-256 ou com SHA-256 diferente é recusado.
    /// </summary>
    public static async Task<string> DownloadAndStageAsync(UpdateInfo info, IProgress<double>? progress, CancellationToken ct)
    {
        if (info.Sha256 is not { Length: 64 } || !info.Sha256.All(Uri.IsHexDigit))
            throw new InvalidOperationException("A versão publicada não tem SHA-256; por segurança ela não será instalada.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        ct = timeout.Token;
        Directory.CreateDirectory(UpdatesDir);
        var destination = OwnedDirectory(info.Version.ToString(3));
        var root = OwnedDirectory("download-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var zip = Path.Combine(root, PackageName);
        try
        {
            await using (var source = await OpenReadAsync(info.PackageUrl, ct).ConfigureAwait(false))
            await using (var target = File.Create(zip))
            {
                var buffer = new byte[81920];
                long total = source.Length, done = 0;
                int read;
                while ((read = await source.Stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    done += read;
                    if (total > 0) progress?.Report((double)done / total);
                }
            }

            string actual;
            await using (var fs = File.OpenRead(zip))
                actual = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct).ConfigureAwait(false));
            if (!actual.Equals(info.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(zip);
                throw new InvalidDataException("O pacote baixado não confere com o SHA-256 publicado. Nada foi instalado.");
            }

            var extracted = Path.Combine(root, "pacote");
            ct.ThrowIfCancellationRequested();
            ZipFile.ExtractToDirectory(zip, extracted);
            ct.ThrowIfCancellationRequested();
            File.Delete(zip);
            var app = Directory.EnumerateFiles(extracted, ExeName, SearchOption.AllDirectories)
                .Select(Path.GetDirectoryName)
                .FirstOrDefault(d => d != null && Path.GetFileName(d).Equals("app", StringComparison.OrdinalIgnoreCase));
            if (app == null)
                throw new InvalidDataException("O pacote baixado não tem a pasta do aplicativo.");
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.EnumerateFiles(app, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                await using var stream = File.OpenRead(file);
                files.Add(Path.GetRelativePath(app, file), Convert.ToHexString(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false)));
            }
            var manifest = new StagedManifest(info.Version.ToString(3), Path.GetRelativePath(root, app), files);
            AppPaths.WriteAllTextAtomic(Path.Combine(root, "pronto.json"), JsonSerializer.Serialize(manifest));
            ct.ThrowIfCancellationRequested();
            if (Directory.Exists(destination)) DeleteOwnedDirectory(destination);
            Directory.Move(root, destination);
            return Path.Combine(destination, manifest.AppDirectory);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                try { DeleteOwnedDirectory(root); }
                catch (Exception ex) { AppPaths.Log(ex, "Limpando download incompleto"); }
            }
        }
    }

    private sealed record StagedManifest(string Version, string AppDirectory, Dictionary<string, string> Files);

    private static string OwnedDirectory(string name)
    {
        var parent = Path.GetFullPath(UpdatesDir);
        if (Directory.Exists(parent) && File.GetAttributes(parent).HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("A pasta de atualizações não pode ser um link.");
        var path = Path.GetFullPath(Path.Combine(parent, name));
        if (!string.Equals(Path.GetDirectoryName(path), parent, StringComparison.OrdinalIgnoreCase) ||
            Directory.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("Caminho inválido de atualização.");
        return path;
    }

    private static void DeleteOwnedDirectory(string path) => Directory.Delete(OwnedDirectory(Path.GetFileName(path)), recursive: true);

    private static string Inside(string root, string relative)
    {
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (Path.IsPathRooted(relative) || !Files.FileOperations.IsSameOrInside(path, root) || path == root)
            throw new InvalidDataException("Caminho inválido no pacote de atualização.");
        return path;
    }

    private static bool TryReadStaged(string dir, out string? app)
    {
        app = null;
        try
        {
            dir = OwnedDirectory(Path.GetFileName(dir));
            var marker = Path.Combine(dir, "pronto.json");
            if (!File.Exists(marker)) return false;
            var manifest = JsonSerializer.Deserialize<StagedManifest>(File.ReadAllText(marker));
            if (manifest == null || manifest.Version != Path.GetFileName(dir) || !manifest.Files.ContainsKey(ExeName)) return false;
            var candidate = Inside(dir, manifest.AppDirectory);
            if (!Directory.Exists(candidate) || !Path.GetFileName(candidate).Equals("app", StringComparison.OrdinalIgnoreCase)) return false;
            var diskFiles = SafeFiles(candidate).ToList();
            if (diskFiles.Count != manifest.Files.Count) return false;
            foreach (var file in diskFiles)
            {
                if (!manifest.Files.TryGetValue(Path.GetRelativePath(candidate, file), out var expected)) return false;
                using var stream = File.OpenRead(file);
                if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(expected, StringComparison.OrdinalIgnoreCase)) return false;
            }
            app = candidate;
            return true;
        }
        catch (Exception ex)
        {
            AppPaths.Log(ex, "Conferindo atualização preparada");
            return false;
        }
    }

    private static IEnumerable<string> SafeFiles(string dir)
    {
        if (File.GetAttributes(dir).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Link dentro da atualização.");
        foreach (var file in Directory.EnumerateFiles(dir))
        {
            if (File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Link dentro da atualização.");
            yield return file;
        }
        foreach (var sub in Directory.EnumerateDirectories(dir))
            foreach (var file in SafeFiles(sub)) yield return file;
    }

    /// <summary>Versão já baixada e conferida, esperando para ser aplicada.</summary>
    public static (Version Version, string AppDir)? FindStaged(Version current)
    {
        try
        {
            if (!Directory.Exists(UpdatesDir)) return null;
            foreach (var dir in Directory.EnumerateDirectories(UpdatesDir)
                         .Where(d => TryParseVersion(Path.GetFileName(d), out var v) && Path.GetFileName(d) == v.ToString(3))
                         .OrderByDescending(d => Version.Parse(Path.GetFileName(d))))
            {
                if (!TryParseVersion(Path.GetFileName(dir), out var v) || v <= current) continue;
                if (TryReadStaged(dir, out var app)) return (v, app!);
            }
        }
        catch (Exception ex)
        {
            AppPaths.Log(ex, "Procurando atualização baixada");
        }
        return null;
    }

    /// <summary>Remove downloads antigos (versões já instaladas ou incompletas).</summary>
    public static void CleanUp(Version current)
    {
        try
        {
            if (!Directory.Exists(UpdatesDir)) return;
            foreach (var dir in Directory.EnumerateDirectories(UpdatesDir))
            {
                var name = Path.GetFileName(dir);
                if (TryParseVersion(name, out var v) && name == v.ToString(3))
                {
                    if (v <= current || !TryReadStaged(dir, out _)) DeleteOwnedDirectory(dir);
                }
                else if (name.StartsWith("download-", StringComparison.Ordinal) && Guid.TryParseExact(name[9..], "N", out _))
                    DeleteOwnedDirectory(dir);
            }
        }
        catch (Exception ex)
        {
            AppPaths.Log(ex, "Limpando atualizações");
        }
    }

    public static string? TakeApplyFailure()
    {
        var path = Path.Combine(UpdatesDir, "falha.json");
        try
        {
            if (!File.Exists(path)) return null;
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            var message = json.RootElement.GetProperty("Message").GetString();
            File.Delete(path);
            return message;
        }
        catch (Exception ex) { AppPaths.Log(ex, "Lendo resultado da atualização"); return null; }
    }

    /// <summary>
    /// Inicia o script que espera este processo terminar, troca a pasta do aplicativo pela nova
    /// (voltando a antiga se algo falhar) e, se pedido, abre o editor de novo.
    /// </summary>
    public static void LaunchApplier(string stagedAppDir, Version version, bool relaunch, int? processId = null)
    {
        using var process = Process.Start(CreateApplierStartInfo(stagedAppDir, version, relaunch,
            processId ?? Environment.ProcessId, AppContext.BaseDirectory))
            ?? throw new IOException("Não foi possível iniciar a atualização.");
    }

    internal static ProcessStartInfo CreateApplierStartInfo(string stagedAppDir, Version version, bool relaunch, int processId, string target)
    {
        var script = Path.Combine(UpdatesDir, "aplicar-" + Guid.NewGuid().ToString("N") + ".ps1");
        Directory.CreateDirectory(UpdatesDir);
        File.WriteAllText(script, ApplierScript, new UTF8Encoding(true));

        var psi = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        // Aberto a partir do PowerShell 7 (ex.: terminal do VS Code), o PSModulePath herdado aponta
        // para módulos que o Windows PowerShell 5.1 não carrega (Get-FileHash, ConvertFrom-Json…).
        psi.Environment.Remove("PSModulePath");
        foreach (var arg in new[]
                 {
                     "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", script,
                     "-ProcessId", processId.ToString(),
                     "-Source", stagedAppDir, "-Target", target, "-Version", version.ToString(3),
                     "-Log", Path.Combine(UpdatesDir, "atualizacao.log"),
                     "-UpdatesRoot", Path.GetFullPath(UpdatesDir),
                 })
        {
            psi.ArgumentList.Add(arg);
        }
        if (relaunch) psi.ArgumentList.Add("-Relaunch");
        return psi;
    }

    internal static string ApplierScript
    {
        get
        {
            using var stream = typeof(UpdateService).Assembly.GetManifestResourceStream("CSharpLab.Core.Updates.ApplyUpdate.ps1")
                ?? throw new InvalidOperationException("Script de atualização ausente.");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }
    }

    private sealed class Download(Stream stream, long length, HttpResponseMessage? response = null) : IAsyncDisposable
    {
        public Stream Stream { get; } = stream;
        public long Length { get; } = length;
        public async ValueTask DisposeAsync()
        {
            await Stream.DisposeAsync().ConfigureAwait(false);
            response?.Dispose();
        }
    }

    private static async Task<Download> OpenReadAsync(string url, CancellationToken ct)
    {
        if (IsLocal(url, out var path))
        {
            var fs = File.OpenRead(path);
            return new Download(fs, fs.Length);
        }
        var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        try
        {
            response.EnsureSuccessStatusCode();
            var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return new Download(stream, response.Content.Headers.ContentLength ?? -1, response);
        }
        catch { response.Dispose(); throw; }
    }

    private static async Task<string> ReadTextAsync(string url, CancellationToken ct)
    {
        if (IsLocal(url, out var path)) return await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await Http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    private static bool IsLocal(string url, out string path)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            path = uri.LocalPath;
            return true;
        }
        path = url;
        return Path.IsPathRooted(url) && !url.StartsWith("http", StringComparison.OrdinalIgnoreCase);
    }
}
