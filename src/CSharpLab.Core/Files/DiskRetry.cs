namespace CSharpLab.Core.Files;

/// <summary>
/// Repete por um instante as operações de disco que falham só porque outro programa está olhando o
/// arquivo naquele momento (antivírus, indexador do Windows, OneDrive). Erros de verdade (arquivo que
/// não existe, disco cheio) não são repetidos.
/// </summary>
public static class DiskRetry
{
    public static void Run(Action action, int attempts = 6)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex) when (attempt < attempts && IsTransient(ex))
            {
                Thread.Sleep(40 * attempt);   // 40, 80, 120… até ~0,6 s no total
            }
        }
    }

    /// <summary>Acesso negado ou arquivo em uso: costuma passar em milissegundos.</summary>
    public static bool IsTransient(Exception ex) => ex switch
    {
        UnauthorizedAccessException => true,
        FileNotFoundException or DirectoryNotFoundException or PathTooLongException => false,
        // Acesso negado, em uso, trecho bloqueado; File.Replace: "não foi possível remover o arquivo a ser
        // substituído" / "mover o substituto" (o arquivo antigo estava aberto por outro programa).
        IOException io => (io.HResult & 0xFFFF) is 5 or 32 or 33 or 1175 or 1176,
        _ => false,
    };
}
