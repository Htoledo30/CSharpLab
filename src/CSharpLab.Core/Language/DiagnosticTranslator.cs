using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace CSharpLab.Core.Language;

/// <summary>
/// Mensagens curtas em português para os diagnósticos mais comuns. Os argumentos vêm da
/// informação estruturada do compilador; quando ela não existe (compilação pelo SDK),
/// são extraídos da mensagem em inglês, que o editor sempre pede ao SDK.
/// Sem tradução própria: mensagem oficial em pt-BR do Roslyn; sem ela, a original.
/// </summary>
public static partial class DiagnosticTranslator
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private static readonly PropertyInfo? ArgumentsProperty =
        typeof(Diagnostic).GetProperty("Arguments", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly HashSet<string> NumericTypes = new(StringComparer.Ordinal)
    {
        "int", "long", "short", "byte", "sbyte", "uint", "ulong", "ushort", "double", "float", "decimal",
        "nint", "nuint", "int?", "long?", "double?", "float?", "decimal?",
    };

    /// <summary>Mensagem em português para um diagnóstico do Roslyn.</summary>
    public static string Translate(Diagnostic diagnostic)
    {
        var args = GetArguments(diagnostic);
        var own = args != null ? TranslateKnown(diagnostic.Id, args) : null;
        if (own != null) return own;

        // Tenta pela mensagem em inglês (cobre casos em que os argumentos não puderam ser lidos).
        own = TranslateFromEnglish(diagnostic.Id, diagnostic.GetMessage(English));
        if (own != null) return own;

        var localized = diagnostic.GetMessage(PtBr);
        return string.IsNullOrWhiteSpace(localized) ? diagnostic.GetMessage(English) : localized;
    }

    /// <summary>Mensagem em português para um diagnóstico vindo da compilação do SDK (texto em inglês).</summary>
    public static string TranslateBuildMessage(string id, string englishMessage) =>
        TranslateFromEnglish(id, englishMessage) ?? englishMessage;

    internal static string[]? GetArguments(Diagnostic diagnostic)
    {
        try
        {
            if (ArgumentsProperty?.GetValue(diagnostic) is not IReadOnlyList<object?> raw)
                return null;
            return raw.Select(FormatArgument).ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static readonly Dictionary<Type, MethodInfo?> PublicSymbolGetters = [];

    /// <summary>Formata um argumento como o compilador faz na mensagem (símbolos no formato curto de erro).</summary>
    private static string FormatArgument(object? arg)
    {
        if (arg == null) return "";
        var symbol = arg as ISymbol;
        if (symbol == null)
        {
            MethodInfo? getter;
            lock (PublicSymbolGetters)
            {
                var type = arg.GetType();
                if (!PublicSymbolGetters.TryGetValue(type, out getter))
                {
                    getter = type.GetInterfaces().FirstOrDefault(i => i.Name == "ISymbolInternal")?.GetMethod("GetISymbol");
                    PublicSymbolGetters[type] = getter;
                }
            }
            symbol = getter?.Invoke(arg, null) as ISymbol;
        }
        if (symbol != null)
        {
            return symbol.Kind is SymbolKind.Assembly or SymbolKind.Namespace
                ? symbol.ToString() ?? ""
                : symbol.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
        }
        return arg is IFormattable f ? f.ToString(null, English) : arg.ToString() ?? "";
    }

    internal static string? TranslateKnown(string id, IReadOnlyList<string> a)
    {
        string Arg(int i) => i < a.Count ? a[i] : "";
        bool Has(int count) => a.Count >= count && a.Take(count).All(s => s.Length > 0);

        return id switch
        {
            "CS1002" => "Faltou \";\".",
            "CS1026" => "Faltou \")\".",
            "CS1513" => "Faltou \"}\" para fechar um bloco.",
            "CS1514" => "Faltou \"{\".",
            "CS1003" when Has(1) => $"Era esperado \"{Arg(0)}\".",
            "CS1010" => "Uma string foi aberta e não foi fechada nesta linha.",
            "CS1012" => "Há caracteres demais entre aspas simples. Para texto, use aspas duplas.",
            "CS0103" when Has(1) => $"\"{Arg(0)}\" não foi encontrado neste trecho.",
            "CS0117" when Has(2) => $"\"{Arg(0)}\" não possui um membro chamado \"{Arg(1)}\".",
            "CS1061" when Has(2) => $"\"{Arg(0)}\" não possui \"{Arg(1)}\" acessível aqui. Verifique o nome e as referências necessárias.",
            "CS0029" when Has(2) && Arg(0) == "string" && NumericTypes.Contains(Arg(1)) =>
                $"Não é possível colocar texto em uma variável {Arg(1)}.",
            "CS0029" when Has(2) && NumericTypes.Contains(Arg(0)) && Arg(1) == "string" =>
                "Não é possível colocar um número em uma variável string.",
            "CS0029" when Has(2) => $"Não é possível atribuir um valor de tipo \"{Arg(0)}\" a \"{Arg(1)}\".",
            "CS0266" when Has(2) => $"A conversão de \"{Arg(0)}\" para \"{Arg(1)}\" precisa ser explícita.",
            "CS0165" when Has(1) => $"\"{Arg(0)}\" foi usada antes de receber um valor.",
            "CS0128" when Has(1) => $"\"{Arg(0)}\" já foi declarado neste bloco.",
            "CS0136" when Has(1) => $"\"{Arg(0)}\" entra em conflito com uma declaração de um bloco relacionado.",
            "CS1501" when Has(1) => $"Nenhuma versão de \"{MemberName(Arg(0))}\" aceita essa quantidade de argumentos.",
            "CS1503" when Has(3) => $"O argumento {Arg(0)} precisa ser \"{Arg(2)}\", mas recebeu \"{Arg(1)}\".",
            "CS0161" when Has(1) => $"O método \"{MemberName(Arg(0))}\" não retorna um valor em todos os caminhos.",
            "CS0019" when Has(3) => $"O operador \"{Arg(0)}\" não pode ser usado entre \"{Arg(1)}\" e \"{Arg(2)}\".",
            "CS8802" => "Mais de um arquivo contém instruções fora de uma classe. Mantenha o código inicial em um só arquivo.",
            "CS0246" when Has(1) => $"O tipo \"{Arg(0)}\" não foi encontrado. Verifique o nome e os usings.",
            "CS0168" when Has(1) => $"A variável \"{Arg(0)}\" foi declarada, mas nunca usada.",
            "CS0219" when Has(1) => $"A variável \"{Arg(0)}\" recebe um valor, mas esse valor nunca é usado.",
            "CS0162" => "Este código nunca será executado.",
            "CS8600" => "Um valor que pode ser nulo está sendo guardado num tipo que não aceita nulo.",
            "CS8602" => "Este valor pode ser nulo aqui.",
            "CS8604" when Has(1) => $"O argumento \"{Arg(0)}\" pode ser nulo aqui.",
            "CS8618" when Has(2) => $"\"{Arg(1)}\" precisa receber um valor no construtor, pois não aceita nulo.",
            _ => null,
        };
    }

    /// <summary>"Program.Calcular(int)" → "Calcular".</summary>
    private static string MemberName(string display)
    {
        var paren = display.IndexOf('(');
        var head = paren >= 0 ? display[..paren] : display;
        var lt = head.IndexOf('<');
        var beforeGeneric = lt >= 0 ? head[..lt] : head;
        var dot = beforeGeneric.LastIndexOf('.');
        return dot >= 0 ? head[(dot + 1)..] : head;
    }

    private static readonly (string Id, Regex Pattern)[] EnglishPatterns =
    [
        ("CS1003", Rx(@"^Syntax error, '(.+)' expected")),
        ("CS0103", Rx(@"^The name '(.+)' does not exist in the current context")),
        ("CS0117", Rx(@"^'(.+)' does not contain a definition for '(.+?)'")),
        ("CS1061", Rx(@"^'(.+)' does not contain a definition for '(.+?)' and no accessible")),
        ("CS0029", Rx(@"^Cannot implicitly convert type '(.+)' to '(.+?)'")),
        ("CS0266", Rx(@"^Cannot implicitly convert type '(.+)' to '(.+?)'\. An explicit")),
        ("CS0165", Rx(@"^Use of unassigned local variable '(.+)'")),
        ("CS0128", Rx(@"^A local variable or function named '(.+?)' is already defined")),
        ("CS0136", Rx(@"^A local or parameter named '(.+?)' cannot be declared")),
        ("CS1501", Rx(@"^No overload for method '(.+?)' takes (\d+) arguments")),
        ("CS1503", Rx(@"^Argument (\d+): cannot convert from '(.+)' to '(.+)'")),
        ("CS0161", Rx(@"^'(.+)': not all code paths return a value")),
        ("CS0019", Rx(@"^Operator '(.+?)' cannot be applied to operands of type '(.+)' and '(.+)'")),
        ("CS0246", Rx(@"^The type or namespace name '(.+?)' could not be found")),
        ("CS0168", Rx(@"^The variable '(.+)' is declared but never used")),
        ("CS0219", Rx(@"^The variable '(.+)' is assigned but its value is never used")),
        ("CS8604", Rx(@"^Possible null reference argument for parameter '(.+?)'")),
    ];

    private static Regex Rx(string pattern) => new(pattern, RegexOptions.CultureInvariant);

    internal static string? TranslateFromEnglish(string id, string message)
    {
        foreach (var (pid, pattern) in EnglishPatterns)
        {
            if (pid != id) continue;
            var m = pattern.Match(message);
            if (!m.Success) continue;
            var args = m.Groups.Values.Skip(1).Select(g => g.Value).ToArray();
            if (id == "CS1501") args = [args[0], args[1]];
            return TranslateKnown(id, args);
        }
        // Códigos sem argumentos.
        return TranslateKnown(id, []) is { } fixedMessage && !fixedMessage.Contains("\"\"") ? fixedMessage : null;
    }
}
