using System.Text.RegularExpressions;

namespace CSharpLab.Core.Language;

public sealed record RuntimeFrame(string File, int Line, int Column, string Method);

/// <summary>Erro que parou o programa do usuário, como o gancho de execução registrou.</summary>
public sealed record RuntimeCrash(string Type, string Message, IReadOnlyList<RuntimeFrame> Frames)
{
    public string ShortType => Type[(Type.LastIndexOf('.') + 1)..];
}

/// <summary>O que aconteceu, em português, e o que fazer a respeito.</summary>
public sealed record RuntimeExplanation(string Message, string? Tip);

/// <summary>
/// Explica em português os erros de execução mais comuns de quem está aprendendo
/// (texto que não é número, null, posição fora da lista…). O código continua em inglês;
/// só a explicação muda de língua.
/// </summary>
public static partial class RuntimeErrors
{
    /// <summary>Id dos diagnósticos de execução na lista de Problemas.</summary>
    public const string DiagnosticId = "EXEC";

    /// <summary>Variável que diz ao gancho onde gravar o relatório (igual à de StartupHook).</summary>
    public const string ReportVariable = "CSHARPLAB_CRASH_REPORT";

    public const string HookFileName = "CSharpLab.RuntimeHook.dll";

    /// <summary>O gancho é compilado para .NET 8: programas mais antigos rodam sem ele.</summary>
    public const int MinimumMajor = 8;

    public static RuntimeCrash? Read(string reportPath)
    {
        try
        {
            if (!File.Exists(reportPath)) return null;
            string? type = null, message = null;
            var frames = new List<RuntimeFrame>();
            foreach (var line in File.ReadAllLines(reportPath))
            {
                if (line.StartsWith("type=", StringComparison.Ordinal)) type = line[5..];
                else if (line.StartsWith("message=", StringComparison.Ordinal)) message = line[8..];
                else if (line.StartsWith("frame=", StringComparison.Ordinal))
                {
                    var parts = line[6..].Split('|');
                    if (parts.Length == 4 && int.TryParse(parts[1], out var l) && int.TryParse(parts[2], out var c))
                        frames.Add(new RuntimeFrame(parts[0], l, c, parts[3]));
                }
            }
            return type == null ? null : new RuntimeCrash(type, message ?? "", frames);
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>A linha mais recente do código do próprio usuário (não da biblioteca do .NET).</summary>
    public static RuntimeFrame? UserFrame(RuntimeCrash crash, string projectDirectory) =>
        crash.Frames.FirstOrDefault(f => f.Line > 0 && Files.FileOperations.IsSameOrInside(f.File, projectDirectory));

    /// <param name="sourceLine">Texto da linha onde parou, para reconhecer a chamada (ex.: int.Parse).</param>
    public static RuntimeExplanation Explain(RuntimeCrash crash, string? sourceLine)
    {
        var code = sourceLine ?? "";
        var message = crash.Message;
        var parse = ParseCall().Match(code);
        bool parses = parse.Success;
        string call = parses ? parse.Value.TrimEnd('(') : "a conversão";

        switch (crash.ShortType)
        {
            case "FormatException":
                var input = Quoted().Match(message) is { Success: true } q ? q.Groups[1].Value : null;
                return input != null && parses
                    ? new($"\"{input}\" não é um número válido, então {call} não conseguiu converter.",
                        "Use int.TryParse para conferir antes: if (int.TryParse(text, out int number)) { ... }")
                    : new("Um texto não estava no formato esperado (por exemplo, letras onde devia haver um número).",
                        parses ? "Use TryParse para conferir o texto antes de converter." : null);

            case "NullReferenceException":
                return new("Você usou algo que está vazio (null): uma variável que ainda não recebeu um objeto com new, ou um valor que não existe.",
                    "Confira se a variável recebeu um valor antes desta linha. Para textos do ReadLine, use: Console.ReadLine() ?? \"\"");

            case "IndexOutOfRangeException":
                return new("Você pediu uma posição que não existe no array.",
                    "As posições começam em 0 e vão até Length - 1: um array com 3 itens tem as posições 0, 1 e 2.");

            case "ArgumentOutOfRangeException" when message.Contains("Index was out of range", StringComparison.OrdinalIgnoreCase):
                return new("Você pediu uma posição que não existe na lista.",
                    "As posições começam em 0 e vão até Count - 1. Confira se a lista tem itens antes de acessar.");

            case "ArgumentOutOfRangeException":
                return new($"Um valor passado para um método está fora do permitido{ParamName(message)}.",
                    code.Contains(".Next(", StringComparison.Ordinal) ? "Em random.Next(min, max), o min não pode ser maior que o max." : null);

            case "DivideByZeroException":
                return new("Divisão por zero entre números inteiros.",
                    "Confira se o divisor é 0 antes de dividir (com double, o resultado seria infinito em vez de erro).");

            case "OverflowException":
                return new(parses ? "O número é grande (ou pequeno) demais para caber nesse tipo." : "Uma conta passou do maior (ou menor) valor que o tipo aguenta.",
                    "Um int vai de -2.147.483.648 até 2.147.483.647. Para números maiores, use long ou double.");

            case "InvalidCastException":
                return new("Não dá para converter um valor desse tipo para o tipo pedido.",
                    "Para transformar texto em número use int.Parse/int.TryParse, não (int).");

            case "KeyNotFoundException":
                var key = Quoted().Match(message) is { Success: true } k ? $" \"{k.Groups[1].Value}\"" : "";
                return new($"A chave{key} não existe no dicionário.",
                    "Use dictionary.TryGetValue(key, out var value) ou ContainsKey(key) antes de ler.");

            case "InvalidOperationException" when message.Contains("Collection was modified", StringComparison.OrdinalIgnoreCase):
                return new("A lista foi alterada (Add/Remove) dentro de um foreach que está percorrendo ela.",
                    "Percorra uma cópia (foreach (var item in list.ToList())) ou use um for de trás para frente.");

            case "InvalidOperationException" when message.Contains("Sequence contains no", StringComparison.OrdinalIgnoreCase):
                return new("Você pediu um item (First, Single, Max, Average…) de uma lista vazia.",
                    "Confira se list.Count > 0 antes, ou use FirstOrDefault.");

            case "InvalidOperationException" when message.Contains("Nullable object must have a value", StringComparison.OrdinalIgnoreCase):
                return new("Você usou .Value de um valor que está null.", "Confira com HasValue antes, ou use ?? para dar um valor padrão.");

            case "ArgumentNullException" when parses:
                return new("Não chegou nenhum texto para converter (o ReadLine devolveu null).",
                    "Isso acontece quando a entrada termina. Use: Console.ReadLine() ?? \"\"");

            case "ArgumentNullException":
                return new($"Um método recebeu null onde precisava de um valor{ParamName(message)}.", "Confira se a variável recebeu um valor antes desta linha.");

            case "FileNotFoundException":
            case "DirectoryNotFoundException":
                return new($"Arquivo ou pasta não encontrado{(Quoted().Match(message) is { Success: true } f ? $": {f.Groups[1].Value}" : "")}.",
                    "Caminhos sem C:\\ começam na pasta do projeto. Confira o nome e se o arquivo existe (File.Exists).");

            case "UnauthorizedAccessException":
                return new("O Windows não deixou acessar esse arquivo ou pasta.", "Use uma pasta sua, como Documentos, ou a pasta do projeto.");

            case "IOException":
                return new("Erro ao ler ou gravar um arquivo (ele pode estar aberto em outro programa).", null);

            case "OutOfMemoryException":
                return new("A memória acabou.", "Talvez uma lista esteja crescendo sem parar dentro de um loop.");

            case "NotImplementedException":
                return new("Este método ainda não foi escrito: ele só tem o throw new NotImplementedException().",
                    "Troque essa linha pelo código do método.");

            case "GameException":
                // Erro do motor dos jogos: a mensagem já é em português e diz o que fazer.
                return new(message, null);

            default:
                return new($"O programa parou com um erro ({crash.ShortType}): {message}", null);
        }
    }

    /// <summary>Códigos de saída que o .NET não consegue explicar porque o processo morre na hora.</summary>
    public static string? ExplainExitCode(int exitCode) => unchecked((uint)exitCode) switch
    {
        0xC00000FD => "Recursão infinita: um método chama a si mesmo sem parar até a memória da pilha acabar (stack overflow). " +
                      "Confira se o método tem uma condição para parar.",
        _ => null,
    };

    /// <summary>Página da Microsoft, em português, sobre esse tipo de erro.</summary>
    public static string HelpUrl(string exceptionType) =>
        "https://learn.microsoft.com/pt-br/dotnet/api/" + exceptionType.ToLowerInvariant();

    private static string ParamName(string message) =>
        ParamPattern().Match(message) is { Success: true } p ? $" (parâmetro \"{p.Groups[1].Value}\")" : "";

    [GeneratedRegex(@"\b(?:int|long|short|byte|double|float|decimal|bool|DateTime|Int32|Int64|Double|Decimal)\.Parse\(|\bConvert\.To\w+\(")]
    private static partial Regex ParseCall();

    [GeneratedRegex(@"'([^']*)'")]
    private static partial Regex Quoted();

    [GeneratedRegex(@"\(Parameter '([^']+)'\)")]
    private static partial Regex ParamPattern();
}
