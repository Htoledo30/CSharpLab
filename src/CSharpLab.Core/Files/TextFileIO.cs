using System.Text;

namespace CSharpLab.Core.Files;

/// <summary>Identifica a versão de um arquivo no disco, para detectar alterações externas.</summary>
public readonly record struct FileStamp(DateTime LastWriteUtc, long Length)
{
    public static FileStamp? Of(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? new FileStamp(info.LastWriteTimeUtc, info.Length) : null;
        }
        catch
        {
            return null;
        }
    }
}

public sealed record TextFileContent(string Text, Encoding Encoding, FileStamp Stamp);

/// <summary>Leitura com detecção de codificação e gravação atômica que preserva codificação e finais de linha.</summary>
public static class TextFileIO
{
    public static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
    private static readonly Encoding Utf8Strict = new UTF8Encoding(false, throwOnInvalidBytes: true);

    static TextFileIO()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static Encoding Legacy => Encoding.GetEncoding(1252);

    public static TextFileContent Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var stamp = FileStamp.Of(path) ?? new FileStamp(DateTime.MinValue, bytes.Length);
        var (encoding, preamble) = Detect(bytes);
        var text = encoding.GetString(bytes, preamble, bytes.Length - preamble);
        return new TextFileContent(text, encoding, stamp);
    }

    public static (Encoding Encoding, int PreambleLength) Detect(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
            return (new UTF8Encoding(true), 3);
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
            return (new UnicodeEncoding(false, true), 2);
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
            return (new UnicodeEncoding(true, true), 2);

        try
        {
            Utf8Strict.GetCharCount(bytes);
            return (Utf8NoBom, 0);
        }
        catch (DecoderFallbackException)
        {
            // Arquivo antigo em ANSI (Windows-1252), comum em editores mais velhos.
            return (Legacy, 0);
        }
    }

    /// <summary>
    /// Grava num arquivo temporário na mesma pasta e só então substitui o original.
    /// Se algo falhar, o arquivo original continua intacto.
    /// </summary>
    public static FileStamp Save(string path, string text, Encoding encoding)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full)!;
        var preamble = encoding.GetPreamble();
        var body = Encode(text, encoding);

        if (File.Exists(full) && File.GetAttributes(full).HasFlag(FileAttributes.ReadOnly))
            throw new UnauthorizedAccessException($"O arquivo \"{Path.GetFileName(full)}\" é somente leitura.");

        var temp = Path.Combine(dir, TempName(Path.GetFileName(full)));
        try
        {
            using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                fs.Write(preamble);
                fs.Write(body);
                fs.Flush(true);
            }

            DiskRetry.Run(() =>
            {
                if (File.Exists(full))
                    File.Replace(temp, full, null, ignoreMetadataErrors: true);
                else
                    File.Move(temp, full);
            });
        }
        catch
        {
            TryDelete(temp);
            throw;
        }

        return FileStamp.Of(full) ?? new FileStamp(DateTime.UtcNow, preamble.Length + body.Length);
    }

    /// <summary>
    /// Converte sem substituições silenciosas: se a codificação do arquivo (ex.: Windows-1252)
    /// não tiver algum caractere do texto, lança <see cref="UnrepresentableTextException"/>.
    /// </summary>
    public static byte[] Encode(string text, Encoding encoding)
    {
        var strict = (Encoding)encoding.Clone();
        strict.EncoderFallback = EncoderFallback.ExceptionFallback;
        try
        {
            return strict.GetBytes(text);
        }
        catch (EncoderFallbackException ex)
        {
            var sample = ex.CharUnknownHigh != '\0'
                ? new string([ex.CharUnknownHigh, ex.CharUnknownLow])
                : ex.CharUnknown.ToString();
            throw new UnrepresentableTextException(DescribeEncoding(encoding), sample);
        }
    }

    public static string TempName(string fileName) => $".{fileName}.{Guid.NewGuid().ToString("N")[..8]}.tmp";

    public static bool IsTempName(string fileName) =>
        fileName.StartsWith('.') && fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) && fileName.Length > 14;

    public static string DescribeEncoding(Encoding encoding) => encoding switch
    {
        UTF8Encoding u => u.GetPreamble().Length > 0 ? "UTF-8 com BOM" : "UTF-8",
        UnicodeEncoding u => u.GetPreamble().Length > 0 && u.GetPreamble()[0] == 0xFE ? "UTF-16 BE" : "UTF-16 LE",
        _ when encoding.CodePage == 1252 => "Windows-1252",
        _ => encoding.WebName.ToUpperInvariant(),
    };

    public static string DetectLineEnding(string text)
    {
        int crlf = 0, lf = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n') continue;
            if (i > 0 && text[i - 1] == '\r') crlf++;
            else lf++;
            if (crlf + lf > 200) break;
        }
        return lf > crlf ? "LF" : "CRLF";
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}

/// <summary>O texto tem caracteres que a codificação atual do arquivo não consegue gravar.</summary>
public sealed class UnrepresentableTextException(string encodingName, string sample)
    : IOException($"A codificação {encodingName} não consegue gravar \"{sample}\".")
{
    public string EncodingName { get; } = encodingName;
    public string Sample { get; } = sample;
}
