using System.IO;
using System.Text;
using System.Text.Json;

namespace CSharpLab.GameEngine;

// A regra do jogo continua no C#: o motor só protege a gravação e a recuperação dos valores.
public sealed partial class Game
{
    internal static readonly JsonSerializerOptions SaveOptions = new() { WriteIndented = true, IncludeFields = true };

    /// <summary>Pasta de saves (os testes usam uma pasta própria).</summary>
    internal string? SaveFolder { get; set; }

    private string SaveRoot => SaveFolder ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CSharp Lab", "Saves");
    private GameSaveStore SaveStore => new(Path.Combine(SaveRoot, SaveId), LegacySavePath(WindowTitle));

    /// <summary>Arquivo atual, separado por identificador do jogo.</summary>
    internal string SavePath => SaveStore.Path;

    /// <summary>Save = salvar. Guarda um número, texto, bool, lista ou objeto. A gravação protege o arquivo atual e mantém uma cópia anterior.</summary>
    /// <param name="name">Nome do valor, por exemplo "gold".</param>
    /// <param name="value">Valor a guardar, por exemplo gold.</param>
    /// <example><code>game.Save("gold", gold);</code></example>
    public void Save<T>(string name, T value)
    {
        CheckSaveName(name, nameof(Save));
        System.Text.Json.Nodes.JsonNode? node;
        try { node = JsonSerializer.SerializeToNode(value, SaveOptions); }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            throw new GameException($"O game.Save(\"{name}\", …) não sabe guardar esse valor ({typeof(T).Name}). Guarde números, textos, true/false, listas ou classes com propriedades e campos públicos, sem referências circulares.");
        }
        WithSaveError("salvar", () => { SaveStore.Save(name, node); return true; });
    }

    /// <summary>Load = carregar. Pega um valor salvo. Se não houver save ou esse nome ainda não existir, devolve o valor inicial. Um arquivo danificado gera uma explicação e permanece intacto.</summary>
    /// <param name="name">Nome usado em game.Save, por exemplo "gold".</param>
    /// <param name="start">Valor inicial de um jogo novo, por exemplo 50.</param>
    /// <example><code>int gold = game.Load("gold", 50);</code></example>
    public T Load<T>(string name, T start)
    {
        CheckSaveName(name, nameof(Load));
        var data = WithSaveError("ler", () => SaveStore.Read());
        if (data[name] is not { } node) return start;
        try { return node.Deserialize<T>(SaveOptions) ?? start; }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or FormatException)
        {
            throw new GameException($"O \"{name}\" salvo não é um {typeof(T).Name} (foi salvo com outro tipo). Use o mesmo tipo de game.Save. Se quiser começar de novo, game.DeleteSave() guarda uma cópia antes de limpar o progresso.");
        }
    }

    /// <summary>HasSave = tem progresso salvo. Um save danificado gera uma explicação em vez de parecer um jogo novo.</summary>
    public bool HasSave => WithSaveError("ler", () => SaveStore.Read().Count > 0);

    /// <summary>DeleteSave = começar de novo. Arquiva os arquivos atuais em Historico e limpa o progresso de forma segura.</summary>
    /// <example><code>game.DeleteSave();</code></example>
    public void DeleteSave() => WithSaveError("começar um jogo novo", () => { SaveStore.Reset(); return true; });

    /// <summary>RestoreSave = restaurar a cópia anterior, por escolha sua. Preserva o arquivo atual no histórico. Depois use game.Load novamente para atualizar as variáveis.</summary>
    /// <returns>True se restaurou; false se ainda não existe uma cópia anterior.</returns>
    /// <example><code>if (game.RestoreSave()) gold = game.Load("gold", 50);</code></example>
    public bool RestoreSave() => WithSaveError("restaurar", () => SaveStore.Restore());

    /// <summary>Copia um save antigo, que usava o título como nome, para este jogo. Confira se o progresso pertence a este projeto. O arquivo antigo não é alterado.</summary>
    /// <param name="previousTitle">Título antigo, se ele mudou. Sem informar, usa o título atual.</param>
    /// <returns>True se importou; false se não há arquivo antigo ou se este jogo já tem save/cópia anterior.</returns>
    /// <example><code>game.ImportLegacySave(); // antes de game.Load, somente para recuperar um save antigo</code></example>
    public bool ImportLegacySave(string? previousTitle = null)
    {
        var title = previousTitle ?? WindowTitle;
        if (string.IsNullOrWhiteSpace(title)) throw new GameException("Informe o título usado quando o save antigo foi criado.");
        var store = new GameSaveStore(Path.Combine(SaveRoot, SaveId), LegacySavePath(title));
        return WithSaveError("importar o save antigo", () => store.ImportLegacy());
    }

    private string LegacySavePath(string title)
    {
        var name = new StringBuilder();
        foreach (var c in title) name.Append(Path.GetInvalidFileNameChars().Contains(c) ? '_' : c);
        return Path.Combine(SaveRoot, name.ToString().Trim() + ".json");
    }

    private T WithSaveError<T>(string operation, Func<T> action)
    {
        try { return action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new GameException($"Não deu para {operation} o jogo salvo. Nenhum save será apagado para resolver o erro. Feche outras janelas deste jogo e confira a permissão de escrita da pasta.\nArquivo: {SavePath}\nDetalhe: {ex.Message}");
        }
    }

    private static void CheckSaveName(string name, string method)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new GameException($"O game.{method} precisa de um nome entre aspas, por exemplo game.{method}(\"gold\", …).");
    }
}
