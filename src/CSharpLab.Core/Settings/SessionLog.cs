using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CSharpLab.Core.Settings;

/// <summary>
/// Registro da sessão: uma linha JSON por acontecimento (abriu pasta, salvou, compilou, executou,
/// o programa parou com erro, erro interno do app, pediu revisão) em %LocalAppData%\CSharpLab\sessao.jsonl.
/// Serve para quem acompanha de fora, como um assistente de IA lendo o arquivo ao vivo; o próprio
/// app não lê este arquivo. Nunca atrapalha o editor: qualquer falha ao gravar é ignorada.
/// O formato está descrito em docs/ACOMPANHAMENTO.md.
/// </summary>
public static class SessionLog
{
    /// <summary>Acima deste tamanho o arquivo vira sessao.anterior.jsonl e começa um novo.</summary>
    private const long MaxBytes = 2_000_000;

    private static readonly object Gate = new();

    private static readonly JsonSerializerOptions Json = new()
    {
        // Acentos legíveis no arquivo ("não", e não "não").
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string FilePath => Path.Combine(AppPaths.Root, "sessao.jsonl");
    public static string PreviousFilePath => Path.Combine(AppPaths.Root, "sessao.anterior.jsonl");

    /// <summary>
    /// Grava um acontecimento. <paramref name="data"/> é um objeto qualquer (normalmente anônimo)
    /// cujas propriedades entram na mesma linha, depois de "time" e "event".
    /// </summary>
    public static void Write(string eventName, object? data = null)
    {
        try
        {
            var line = new JsonObject
            {
                ["time"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                ["event"] = eventName,
            };
            if (data != null && JsonSerializer.SerializeToNode(data, data.GetType(), Json) is JsonObject fields)
            {
                foreach (var (key, value) in fields.ToList())
                {
                    fields.Remove(key);
                    line[key] = value;
                }
            }
            var text = line.ToJsonString(Json) + "\n";
            lock (Gate)
            {
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxBytes)
                    File.Move(FilePath, PreviousFilePath, overwrite: true);
                // Quem acompanha pode estar lendo o arquivo nesse instante: tenta de novo por ~0,1 s.
                Files.DiskRetry.Run(() => File.AppendAllText(FilePath, text, new UTF8Encoding(false)), attempts: 3);
            }
        }
        catch
        {
            // O registro é só para acompanhar; nunca pode atrapalhar quem está programando.
        }
    }
}
