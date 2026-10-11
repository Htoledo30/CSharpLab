using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace CSharpLab.GameEngine;

internal static class GameSaveIdentity
{
    internal const string MetadataName = "CSharpLab.SaveId";

    public static string Of(Assembly? assembly)
    {
        var metadata = assembly?.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(a => string.Equals(a.Key, MetadataName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (metadata is { Count: > 1 })
            throw new GameException("O projeto tem mais de um identificador de salvamento. Mantenha um único CSharpLab.SaveId.");
        if (metadata is { Count: 1 }) return Normalize(metadata[0].Value!);
        // Projetos feitos fora do editor continuam funcionando. O título da janela não identifica saves.
        var name = assembly?.GetName().Name ?? "manual-game";
        return "app-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name.ToUpperInvariant()))).ToLowerInvariant();
    }

    public static string Normalize(string id)
    {
        if (Guid.TryParse(id, out var guid)) return guid.ToString("N");
        if (string.IsNullOrWhiteSpace(id) || id.Length > 80 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new GameException("O identificador do save precisa ter de 1 a 80 letras sem acento, números, hífens ou sublinhados. Exemplo: new Game(\"A Torre\", \"a-torre\"). Use um identificador diferente para cada jogo.");
        return id.ToLowerInvariant();
    }
}
