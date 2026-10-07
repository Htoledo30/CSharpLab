using CSharpLab.Core.Language;
using CSharpLab.Core.Projects;
using Microsoft.CodeAnalysis.Text;

namespace CSharpLab.Tests;

public sealed class CodeAssistTests : IDisposable
{
    private readonly string _dir;
    private readonly LanguageService _ls = new();

    public CodeAssistTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "csharplab-assist", Guid.NewGuid().ToString("N")[..8], "Jogo");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "Jogo.csproj"), ProjectCreator.CsprojContent("Jogo"));
        File.WriteAllText(Path.Combine(_dir, "Heroi.cs"), "class Heroi\n{\n    public int Vida = 10;\n    public void Atacar() { Vida--; }\n}\n");
    }

    public void Dispose()
    {
        _ls.Dispose();
        try { Directory.Delete(Path.GetDirectoryName(_dir)!, true); } catch { }
    }

    private string Open(string text, string file = "Program.cs")
    {
        var path = Path.Combine(_dir, file);
        File.WriteAllText(path, text);
        _ls.LoadProject(ProjectEvaluator.Estimate(Path.Combine(_dir, "Jogo.csproj")));
        var key = LanguageService.KeyFor(path);
        _ls.OpenDocument(key, path, SourceText.From(text), 1);
        return key;
    }

    [Theory]
    [InlineData("string nome = Console.ReadLine|", CallShape.NoParameters)]
    [InlineData("Console.WriteLine|", CallShape.HasParameters)]
    [InlineData("var h = new Heroi();\nh.Atacar|", CallShape.NoParameters)]
    [InlineData("var r = new Random();\nint n = r.Next|", CallShape.HasParameters)]
    [InlineData("var h = new Heroi();\nAction a = h.Atacar|;", CallShape.None)]
    [InlineData("var h = new Heroi();\nAction? a = null;\na += h.Atacar|;", CallShape.None)]
    [InlineData("var nome = nameof(Console.ReadLine|);", CallShape.None)]
    [InlineData("var h = new Heroi();\nint v = h.Vida|;", CallShape.None)]
    [InlineData("new List<int>().ForEach(Console.WriteLine|);", CallShape.None)]
    public async Task Metodo_completado_ganha_parenteses_so_quando_e_chamada(string code, CallShape expected)
    {
        int position = code.IndexOf('|');
        var key = Open(code.Remove(position, 1));
        Assert.Equal(expected, await _ls.GetCallShapeAsync(key, position, CancellationToken.None));
    }

    [Theory]
    [InlineData("string nome = Console.ReadLine() ?? \"\";", "ReadLine", "Espera a pessoa digitar")]
    [InlineData("int n = int.Parse(\"4\");", "Parse", "int.TryParse")]
    [InlineData("var r = new Random();\nint d = r.Next(1, 7);", "Next", "NÃO inclui o max")]
    [InlineData("var l = new List<string>();\nl.Add(\"Espada\");", "Add", "fim da lista")]
    [InlineData("var l = new List<int>();\nvar p = l.Where(x => x > 1);", "Where", "Filtra")]
    [InlineData("foreach (var c in \"abc\") { }", "foreach", "cada item")]
    [InlineData("static void Atacar() { }", "static", "Pertence à classe")]
    [InlineData("int vida = 3;", "int", "Número inteiro")]
    public async Task Dica_explica_em_portugues(string text, string word, string expected)
    {
        var key = Open(text);
        var info = await _ls.GetQuickInfoAsync(key, text.IndexOf(word, StringComparison.Ordinal) + 1, CancellationToken.None);
        Assert.NotNull(info?.Doc);
        Assert.Contains(expected, info!.Doc!.Text);
    }

    [Fact]
    public async Task Dica_do_var_mostra_o_tipo_e_a_explicacao()
    {
        var text = "var nome = \"Ana\";";
        var key = Open(text);
        var info = await _ls.GetQuickInfoAsync(key, 1, CancellationToken.None);
        Assert.Contains("string", info!.Signature, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("descobrir o tipo", info.Doc!.Text);
    }

    [Fact]
    public async Task Dica_mostra_tipo_da_variavel()
    {
        var text = "int vida = 3;\nConsole.WriteLine(vida);";
        var key = Open(text);
        var info = await _ls.GetQuickInfoAsync(key, text.LastIndexOf("vida") + 1, CancellationToken.None);
        Assert.NotNull(info);
        Assert.Contains("int vida", info!.Signature);
    }

    [Fact]
    public async Task Ir_para_definicao_em_outro_arquivo_e_no_dotnet()
    {
        var text = "var h = new Heroi();\nh.Atacar();\nConsole.WriteLine(h.Vida);";
        var key = Open(text);

        var def = await _ls.FindDefinitionAsync(key, text.IndexOf("Atacar") + 2, CancellationToken.None);
        Assert.NotNull(def);
        Assert.EndsWith("Heroi.cs", def!.DocumentKey);
        Assert.Equal(4, def.Line);

        var external = await _ls.FindDefinitionAsync(key, text.IndexOf("WriteLine") + 2, CancellationToken.None);
        Assert.NotNull(external);
        Assert.Null(external!.DocumentKey);
        Assert.Contains("Console.WriteLine", external.ExternalName);
    }

    [Fact]
    public async Task Renomear_altera_todos_os_arquivos()
    {
        var text = "var h = new Heroi();\nh.Vida = 5;\nConsole.WriteLine(h.Vida);";
        var key = Open(text);
        var position = text.IndexOf("Vida") + 1;

        var target = await _ls.GetRenameTargetAsync(key, position, CancellationToken.None);
        Assert.Equal("Vida", target?.Name);

        var (edits, occurrences, error) = await _ls.RenameAsync(key, position, "Pontos", CancellationToken.None);
        Assert.Null(error);
        Assert.Equal(2, edits.Count);
        Assert.Equal(4, occurrences); // declaração, Atacar() e os dois usos em Program.cs
        var program = Assert.Single(edits, e => e.DocumentKey == key);
        Assert.Equal("var h = new Heroi();\nh.Pontos = 5;\nConsole.WriteLine(h.Pontos);",
            SourceText.From(text).WithChanges(program.Changes).ToString());

        var (_, _, invalid) = await _ls.RenameAsync(key, position, "class", CancellationToken.None);
        Assert.NotNull(invalid);
    }

    [Fact]
    public async Task Sugere_using_para_tipo_nao_encontrado()
    {
        var text = "var sb = new StringBuilder();\nsb.Append(1);";
        var key = Open(text);
        var candidates = await _ls.FindUsingCandidatesAsync(key, "StringBuilder", CancellationToken.None);
        Assert.Contains("System.Text", candidates);

        // System.Collections.Generic já vem dos usings implícitos: não é sugerido de novo.
        var list = await _ls.FindUsingCandidatesAsync(key, "List", CancellationToken.None);
        Assert.DoesNotContain("System.Collections.Generic", list);

        var change = await _ls.AddUsingAsync(key, "System.Text", CancellationToken.None);
        Assert.NotNull(change);
        var updated = SourceText.From(text).WithChanges(change!.Value).ToString();
        Assert.StartsWith("using System.Text;", updated);
        Assert.Contains("var sb = new StringBuilder();", updated);
    }
}
