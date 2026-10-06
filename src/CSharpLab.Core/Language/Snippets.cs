namespace CSharpLab.Core.Language;

/// <summary>
/// Snippet essencial. O modelo usa "${n:texto}" para campos editáveis (o mesmo n repete o campo),
/// "$0" para a posição final do cursor e "\n" + 4 espaços por nível de indentação.
/// </summary>
public sealed record SnippetDefinition(string Shortcut, string Description, string Template, CodeContext Contexts);

public static class Snippets
{
    private const CodeContext Stmt = CodeContext.Statement;
    private const CodeContext StmtOrMember = CodeContext.Statement | CodeContext.Member;

    public static readonly IReadOnlyList<SnippetDefinition> All =
    [
        new("cw", "Console.WriteLine();", "Console.WriteLine($0);", Stmt),
        new("read", "Console.ReadLine()", "Console.ReadLine()$0", Stmt | CodeContext.Expression),
        new("if", "if (condicao) { }", "if (${1:condicao})\n{\n    $0\n}", Stmt),
        new("ifelse", "if (condicao) { } else { }", "if (${1:condicao})\n{\n    $0\n}\nelse\n{\n    \n}", Stmt),
        new("for", "for (int i = 0; i < 10; i++) { }", "for (int ${1:i} = 0; ${1:i} < ${2:10}; ${1:i}++)\n{\n    $0\n}", Stmt),
        new("foreach", "foreach (var item in itens) { }", "foreach (var ${1:item} in ${2:itens})\n{\n    $0\n}", Stmt),
        new("while", "while (condicao) { }", "while (${1:condicao})\n{\n    $0\n}", Stmt),
        new("switch", "switch (valor) { case … default … }",
            "switch (${1:valor})\n{\n    case ${2:1}:\n        $0\n        break;\n    default:\n        break;\n}", Stmt),
        new("method", "static void MeuMetodo() { }", "static void ${1:MeuMetodo}()\n{\n    $0\n}", StmtOrMember),
        new("methodr", "static int MeuMetodo() { return 0; }", "static ${1:int} ${2:MeuMetodo}()\n{\n    return ${3:0};$0\n}", StmtOrMember),
        new("class", "class MinhaClasse { }", "class ${1:MinhaClasse}\n{\n    $0\n}", CodeContext.Member | CodeContext.Type),
        new("array", "int[] valores = { 1, 2, 3 };", "int[] ${1:valores} = { ${2:1, 2, 3} };$0", StmtOrMember),
        new("list", "List<int> valores = new();", "List<${1:int}> ${2:valores} = new();$0", StmtOrMember),
    ];

    public static SnippetDefinition? Find(string shortcut) =>
        All.FirstOrDefault(s => s.Shortcut == shortcut);

    public static bool Fits(SnippetDefinition snippet, CodeContext context) => (snippet.Contexts & context) != 0;

    /// <summary>Parte do modelo: texto literal, campo editável ou posição final.</summary>
    public abstract record Part;
    public sealed record TextPart(string Text) : Part;
    public sealed record FieldPart(int Index, string Default) : Part;
    public sealed record CaretPart : Part;

    public static IReadOnlyList<Part> Parse(string template)
    {
        var parts = new List<Part>();
        var text = new System.Text.StringBuilder();
        for (int i = 0; i < template.Length; i++)
        {
            if (template[i] == '$' && i + 1 < template.Length)
            {
                if (template[i + 1] == '0')
                {
                    Flush();
                    parts.Add(new CaretPart());
                    i++;
                    continue;
                }
                if (template[i + 1] == '{')
                {
                    int colon = template.IndexOf(':', i);
                    int close = template.IndexOf('}', i);
                    if (colon > 0 && close > colon && int.TryParse(template[(i + 2)..colon], out var index))
                    {
                        Flush();
                        parts.Add(new FieldPart(index, template[(colon + 1)..close]));
                        i = close;
                        continue;
                    }
                }
            }
            text.Append(template[i]);
        }
        Flush();
        return parts;

        void Flush()
        {
            if (text.Length > 0)
            {
                parts.Add(new TextPart(text.ToString()));
                text.Clear();
            }
        }
    }
}
