namespace CSharpLab.Core.Files;

/// <summary>Busca de texto simples usada por Buscar/Substituir.</summary>
public static class TextOccurrences
{
    /// <summary>Todas as ocorrências (sem sobreposição) até <paramref name="limit"/>.</summary>
    public static List<(int Start, int Length)> FindAll(string text, string query, bool matchCase, int limit = int.MaxValue)
    {
        var result = new List<(int, int)>();
        if (query.Length == 0) return result;
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        int index = 0;
        while (result.Count < limit && (index = text.IndexOf(query, index, comparison)) >= 0)
        {
            result.Add((index, query.Length));
            index += query.Length;
        }
        return result;
    }
}
