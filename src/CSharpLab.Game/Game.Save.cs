using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CSharpLab.GameEngine;

// Salvar e carregar: o jogo guarda valores com um nome (ouro, nível, o herói inteiro) num arquivo do jogo,
// que continua lá quando o jogo fecha e abre de novo.
public sealed partial class Game
{
    private static readonly JsonSerializerOptions SaveOptions = new() { WriteIndented = true, IncludeFields = true };

    /// <summary>Onde fica o arquivo salvo (os testes trocam a pasta). Null: a pasta do jogo, em Documentos\CSharp Lab\Saves.</summary>
    internal string? SaveFolder { get; set; }

    /// <summary>O arquivo do jogo salvo: Documentos\CSharp Lab\Saves\&lt;nome do jogo&gt;.json.</summary>
    internal string SavePath
    {
        get
        {
            var folder = SaveFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CSharp Lab", "Saves");
            var name = new StringBuilder();
            foreach (var c in WindowTitle) name.Append(Path.GetInvalidFileNameChars().Contains(c) ? '_' : c);
            return Path.Combine(folder, name.ToString().Trim() + ".json");
        }
    }

    /// <summary>
    /// Save = salvar. Guarda um valor com um nome: um número, um texto, um true/false, uma lista ou até um
    /// objeto inteiro (o herói, com vida e ouro). Fica salvo mesmo depois de fechar o jogo.
    /// </summary>
    /// <param name="name">O nome do que vai salvar. Exemplo: "gold".</param>
    /// <param name="value">O valor. Exemplo: gold.</param>
    /// <example><code>game.Save("gold", gold);
    /// game.Save("hero", hero);</code></example>
    public void Save<T>(string name, T value)
    {
        CheckSaveName(name, nameof(Save));
        var data = ReadSave();
        try
        {
            data[name] = JsonSerializer.SerializeToNode(value, SaveOptions);
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException)
        {
            throw new GameException($"O game.Save(\"{name}\", …) não sabe guardar esse valor ({typeof(T).Name}). Guarde números, textos, true/false, listas ou classes com propriedades públicas.");
        }
        try
        {
            var path = SavePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, data.ToJsonString(SaveOptions), new UTF8Encoding(false));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new GameException($"Não deu para salvar o jogo em {SavePath}: {ex.Message}");
        }
    }

    /// <summary>
    /// Load = carregar. Pega um valor salvo com game.Save. Se ainda não tem nada salvo com esse nome, volta o
    /// valor que você der (o do jogo novo).
    /// </summary>
    /// <param name="name">O nome usado no game.Save. Exemplo: "gold".</param>
    /// <param name="start">O valor quando não tem nada salvo. Exemplo: 50.</param>
    /// <example><code>int gold = game.Load("gold", 50);
    /// Hero hero = game.Load("hero", new Hero());</code></example>
    public T Load<T>(string name, T start)
    {
        CheckSaveName(name, nameof(Load));
        var data = ReadSave();
        if (data[name] is not { } node) return start;
        try
        {
            return node.Deserialize<T>(SaveOptions) ?? start;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or FormatException)
        {
            throw new GameException($"O \"{name}\" salvo não é um {typeof(T).Name} (foi salvo com outro tipo). Use o mesmo tipo do game.Save, ou apague o jogo salvo com game.DeleteSave().");
        }
    }

    /// <summary>HasSave = tem jogo salvo. true se o game.Save já guardou alguma coisa (bom para mostrar o botão "Continuar").</summary>
    /// <example><code>game.Find("Continue").Visible = game.HasSave;</code></example>
    public bool HasSave => ReadSave().Count > 0;

    /// <summary>DeleteSave = apagar o jogo salvo. Começa tudo do zero (bom para o botão "Novo jogo").</summary>
    /// <example><code>game.DeleteSave();</code></example>
    public void DeleteSave()
    {
        try
        {
            if (File.Exists(SavePath)) File.Delete(SavePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new GameException($"Não deu para apagar o jogo salvo em {SavePath}: {ex.Message}");
        }
    }

    private JsonObject ReadSave()
    {
        try
        {
            if (!File.Exists(SavePath)) return [];
            return JsonNode.Parse(File.ReadAllText(SavePath)) as JsonObject ?? [];
        }
        catch (JsonException)
        {
            return [];   // arquivo estragado: o jogo começa de novo em vez de travar
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new GameException($"Não deu para ler o jogo salvo em {SavePath}: {ex.Message}");
        }
    }

    private static void CheckSaveName(string name, string method)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new GameException($"O game.{method} precisa de um nome entre aspas, por exemplo game.{method}(\"gold\", …).");
    }
}
