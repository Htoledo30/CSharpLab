namespace CSharpLab.Core.Settings;

/// <summary>Pastas locais do aplicativo, todas em %LocalAppData%\CSharpLab.</summary>
public static class AppPaths
{
    private static string? _root;

    public static string Root
    {
        get
        {
            if (_root == null)
            {
                var overrideRoot = Environment.GetEnvironmentVariable("CSHARPLAB_DATA");
                _root = string.IsNullOrWhiteSpace(overrideRoot)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CSharpLab")
                    : overrideRoot;
                Directory.CreateDirectory(_root);
            }
            return _root;
        }
        set => _root = value;
    }

    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string RecoveryDir => Ensure(Path.Combine(Root, "Recovery"));
    public static string CacheDir => Ensure(Path.Combine(Root, "Cache"));
    public static string BuildDir => Ensure(Path.Combine(Root, "Build"));
    public static string LogFile => Path.Combine(Root, "erros.log");

    private static string Ensure(string dir)
    {
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Grava texto de forma atômica (temporário + substituição).</summary>
    public static void WriteAllTextAtomic(string path, string text)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(fs, Files.TextFileIO.Utf8NoBom))
            {
                writer.Write(text);
                writer.Flush();
                fs.Flush(true);
            }
            if (File.Exists(path))
                File.Replace(temp, path, null, ignoreMetadataErrors: true);
            else
                File.Move(temp, path);
        }
        catch
        {
            try { File.Delete(temp); } catch { }
            throw;
        }
    }

    public static void Log(Exception ex, string context)
    {
        try
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}: {ex}\n";
            var info = new FileInfo(LogFile);
            if (info.Exists && info.Length > 1_000_000)
                info.Delete();
            File.AppendAllText(LogFile, line);
        }
        catch
        {
        }
    }
}
