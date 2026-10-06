using System.Security;

namespace CSharpLab.Core.Files;

/// <summary>Converte exceções de E/S em frases curtas para o usuário.</summary>
public static class FileErrors
{
    private const int ErrorSharingViolation = unchecked((int)0x80070020);
    private const int ErrorLockViolation = unchecked((int)0x80070021);
    private const int ErrorDiskFull = unchecked((int)0x80070070);
    private const int ErrorHandleDiskFull = unchecked((int)0x80070027);

    public static string Describe(Exception ex, string path)
    {
        var name = Path.GetFileName(path.TrimEnd('\\', '/'));
        if (string.IsNullOrEmpty(name)) name = path;
        return ex switch
        {
            UnauthorizedAccessException u when u.Message.Contains("somente leitura") => u.Message,
            UnauthorizedAccessException or SecurityException => $"Sem permissão para acessar \"{name}\".",
            PathTooLongException => $"O caminho de \"{name}\" é longo demais.",
            DirectoryNotFoundException => $"A pasta de \"{name}\" não existe mais.",
            FileNotFoundException => $"\"{name}\" não foi encontrado.",
            IOException io when io.HResult is ErrorSharingViolation or ErrorLockViolation =>
                $"\"{name}\" está em uso por outro programa.",
            IOException io when io.HResult is ErrorDiskFull or ErrorHandleDiskFull => "Não há espaço suficiente no disco.",
            UserFacingException u => u.Message,
            IOException io => $"Não foi possível acessar \"{name}\": {io.Message}",
            _ => $"Não foi possível concluir a operação com \"{name}\": {ex.Message}",
        };
    }
}

/// <summary>Erro cuja mensagem já está pronta para o usuário.</summary>
public sealed class UserFacingException(string message) : IOException(message);
