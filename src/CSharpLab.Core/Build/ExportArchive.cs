using System.IO.Compression;
using System.Security.Cryptography;

namespace CSharpLab.Core.Build;

/// <summary>ZIP cancelável; a conferência compara cada arquivo descompactado com o original.</summary>
internal static class ExportArchive
{
    internal sealed record Content(long Length, byte[] Hash);

    public static Dictionary<string, Content> Create(string folder, string zip, string root, CancellationToken ct)
    {
        var contents = new Dictionary<string, Content>(StringComparer.Ordinal);
        using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
        foreach (var (file, isDirectory) in Entries(folder, ct))
        {
            var name = root + "/" + Path.GetRelativePath(folder, file).Replace('\\', '/') + (isDirectory ? "/" : "");
            var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
            if (isDirectory)
            {
                contents.Add(name, new(0, SHA256.HashData([])));
                continue;
            }
            entry.LastWriteTime = ClampZipTime(File.GetLastWriteTime(file));
            using var source = File.OpenRead(file);
            using var target = entry.Open();
            contents.Add(name, CopyAndHash(source, target, ct));
        }
        return contents;
    }

    public static void Verify(string zip, IReadOnlyDictionary<string, Content> contents, CancellationToken ct)
    {
        using var archive = ZipFile.OpenRead(zip);
        if (archive.Entries.Count != contents.Count)
            throw new InvalidDataException("O ZIP não contém todos os arquivos do jogo.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (!names.Add(entry.FullName) || !contents.TryGetValue(entry.FullName, out var expected))
                throw new InvalidDataException("O ZIP contém um arquivo inesperado.");
            using var stream = entry.Open();
            var actual = CopyAndHash(stream, Stream.Null, ct);
            if (actual.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(actual.Hash, expected.Hash))
                throw new InvalidDataException($"O arquivo {entry.FullName} ficou incompleto no ZIP.");
        }
    }

    private static Content CopyAndHash(Stream source, Stream target, CancellationToken ct)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        long length = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var read = source.Read(buffer);
            if (read == 0) break;
            target.Write(buffer, 0, read);
            hash.AppendData(buffer, 0, read);
            length += read;
        }
        return new(length, hash.GetHashAndReset());
    }

    private static IEnumerable<(string Path, bool IsDirectory)> Entries(string folder, CancellationToken ct)
    {
        bool empty = true;
        foreach (var path in Directory.EnumerateFileSystemEntries(folder))
        {
            empty = false;
            ct.ThrowIfCancellationRequested();
            var attributes = File.GetAttributes(path);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("A pasta publicada contém um atalho de disco. Exporte arquivos reais para que o pacote seja completo.");
            if (attributes.HasFlag(FileAttributes.Directory))
            {
                foreach (var file in Entries(path, ct)) yield return file;
            }
            else yield return (path, false);
        }
        if (empty) yield return (folder, true);
    }

    private static DateTimeOffset ClampZipTime(DateTime time)
        => time.Year < 1980 ? new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero)
        : time.Year > 2107 ? new DateTimeOffset(2107, 12, 31, 23, 59, 58, TimeSpan.Zero)
        : new DateTimeOffset(time);
}
