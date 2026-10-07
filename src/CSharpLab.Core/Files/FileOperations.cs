using System.Runtime.InteropServices;

namespace CSharpLab.Core.Files;

public static class FileOperations
{
    private static readonly string[] ReservedNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    /// <summary>Retorna uma mensagem de erro se o nome não puder ser usado, ou null.</summary>
    public static string? ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Digite um nome.";
        if (name != name.Trim())
            return "O nome não pode começar nem terminar com espaço.";
        if (name.EndsWith('.'))
            return "O nome não pode terminar com ponto.";
        var invalid = name.IndexOfAny(Path.GetInvalidFileNameChars());
        if (invalid >= 0)
            return $"O caractere \"{name[invalid]}\" não é permitido em nomes.";
        var stem = name.Split('.')[0];
        if (ReservedNames.Contains(stem, StringComparer.OrdinalIgnoreCase))
            return $"\"{name}\" é um nome reservado do Windows.";
        if (name.Length > 200)
            return "O nome é longo demais.";
        return null;
    }

    /// <summary>Acrescenta ".cs" quando o nome não tem extensão, sem duplicar.</summary>
    public static string WithDefaultExtension(string name, string extension = ".cs")
    {
        name = name.Trim();
        while (name.EndsWith(extension + extension, StringComparison.OrdinalIgnoreCase))
            name = name[..^extension.Length];
        return Path.HasExtension(name) ? name : name + extension;
    }

    public static bool PathExists(string path) => File.Exists(path) || Directory.Exists(path);

    public static bool IsSameOrInside(string path, string folder)
    {
        var p = Path.GetFullPath(path).TrimEnd('\\');
        var f = Path.GetFullPath(folder).TrimEnd('\\');
        return p.Equals(f, StringComparison.OrdinalIgnoreCase) ||
               p.StartsWith(f + "\\", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Renomeia arquivo ou pasta. Permite trocar só maiúsculas/minúsculas.</summary>
    public static string Rename(string path, string newName)
    {
        var error = ValidateName(newName);
        if (error != null) throw new UserFacingException(error);
        path = Path.GetFullPath(path).TrimEnd('\\');
        var parent = Path.GetDirectoryName(path)!;
        var target = Path.Combine(parent, newName);
        if (string.Equals(path, target, StringComparison.Ordinal))
            return target;
        bool onlyCase = string.Equals(path, target, StringComparison.OrdinalIgnoreCase);
        if (!onlyCase && PathExists(target))
            throw new UserFacingException($"Já existe \"{newName}\" nesta pasta.");

        if (Directory.Exists(path))
        {
            if (onlyCase)
            {
                var temp = Path.Combine(parent, newName + "." + Guid.NewGuid().ToString("N")[..6]);
                DiskRetry.Run(() => Directory.Move(path, temp));
                DiskRetry.Run(() => Directory.Move(temp, target));
            }
            else
            {
                DiskRetry.Run(() => Directory.Move(path, target));
            }
        }
        else
        {
            DiskRetry.Run(() => File.Move(path, target));
        }
        return target;
    }

    public static string CreateFile(string folder, string name)
    {
        var error = ValidateName(name);
        if (error != null) throw new UserFacingException(error);
        var path = Path.Combine(folder, name);
        if (PathExists(path)) throw new UserFacingException($"Já existe \"{name}\" nesta pasta.");
        using (new FileStream(path, FileMode.CreateNew, FileAccess.Write)) { }
        return path;
    }

    public static string CreateFolder(string parent, string name)
    {
        var error = ValidateName(name);
        if (error != null) throw new UserFacingException(error);
        var path = Path.Combine(parent, name);
        if (PathExists(path)) throw new UserFacingException($"Já existe \"{name}\" nesta pasta.");
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Envia para a Lixeira do Windows (nunca exclui permanentemente).</summary>
    public static void SendToRecycleBin(string path)
    {
        if (!PathExists(path))
            throw new FileNotFoundException(null, path);

        var op = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = Path.GetFullPath(path).TrimEnd('\\') + "\0\0",
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT | FOF_WANTNUKEWARNING,
        };
        int result = SHFileOperation(ref op);
        if (result != 0 || op.fAnyOperationsAborted)
            throw new UserFacingException($"Não foi possível enviar \"{Path.GetFileName(path)}\" para a Lixeira. Ele pode estar em uso.");
        if (PathExists(path))
            throw new UserFacingException($"\"{Path.GetFileName(path)}\" não foi enviado para a Lixeira.");
    }

    private const uint FO_DELETE = 3;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;
    private const ushort FOF_WANTNUKEWARNING = 0x4000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);
}
