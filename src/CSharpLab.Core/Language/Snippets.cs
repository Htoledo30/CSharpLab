namespace CSharpLab.Core.Language;

/// <summary>
/// Snippet essencial. O modelo usa "${n:texto}" para campos editáveis (o mesmo n repete o campo),
/// "$0" para a posição final do cursor e "\n" + 4 espaços por nível de indentação.
/// </summary>
/// <param name="GameOnly">Só aparece em jogos com botões (usa o motor CSharpLab.Game).</param>
public sealed record SnippetDefinition(string Shortcut, string Description, string Template, CodeContext Contexts, bool GameOnly = false);

public static class Snippets
{
    private const CodeContext Stmt = CodeContext.Statement;
    private const CodeContext StmtOrMember = CodeContext.Statement | CodeContext.Member;

    public static readonly IReadOnlyList<SnippetDefinition> All =
    [
        new("cw", "Console.WriteLine();", "Console.WriteLine($0);", Stmt),
        new("read", "Console.ReadLine()", "Console.ReadLine()$0", Stmt | CodeContext.Expression),
        new("if", "if (condition) { }", "if (${1:condition})\n{\n    $0\n}", Stmt),
        new("ifelse", "if (condition) { } else { }", "if (${1:condition})\n{\n    $0\n}\nelse\n{\n    \n}", Stmt),
        new("for", "for (int i = 0; i < 10; i++) { }", "for (int ${1:i} = 0; ${1:i} < ${2:10}; ${1:i}++)\n{\n    $0\n}", Stmt),
        new("foreach", "foreach (var item in items) { }", "foreach (var ${1:item} in ${2:items})\n{\n    $0\n}", Stmt),
        new("while", "while (condition) { }", "while (${1:condition})\n{\n    $0\n}", Stmt),
        new("switch", "switch (value) { case … default … }",
            "switch (${1:value})\n{\n    case ${2:1}:\n        $0\n        break;\n    default:\n        break;\n}", Stmt),
        new("method", "static void MyMethod() { }", "static void ${1:MyMethod}()\n{\n    $0\n}", StmtOrMember),
        new("methodr", "static int MyMethod() { return 0; }", "static ${1:int} ${2:MyMethod}()\n{\n    return ${3:0};$0\n}", StmtOrMember),
        new("class", "class MyClass { }", "class ${1:MyClass}\n{\n    $0\n}", CodeContext.Member | CodeContext.Type),
        new("array", "int[] values = { 1, 2, 3 };", "int[] ${1:values} = { ${2:1, 2, 3} };$0", StmtOrMember),
        new("list", "List<int> values = new();", "List<${1:int}> ${2:values} = new();$0", StmtOrMember),
        new("scene", "game.Scene(\"Name\", () => { });", "game.Scene(\"${1:Name}\", () =>\n{\n    $0\n});", Stmt, GameOnly: true),
        new("button", "game.Button(\"Texto\", () => { });", "game.Button(\"${1:Texto}\", () =>\n{\n    $0\n});", Stmt, GameOnly: true),
    ];

    public static SnippetDefinition? Find(string shortcut) =>
        All.FirstOrDefault(s => s.Shortcut == shortcut);

    public static bool Fits(SnippetDefinition snippet, CodeContext context, bool gameProject = false) =>
        (snippet.Contexts & context) != 0 && (!snippet.GameOnly || gameProject);

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
