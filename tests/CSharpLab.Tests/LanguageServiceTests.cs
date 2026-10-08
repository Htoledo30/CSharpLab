using CSharpLab.Core.Language;
using CSharpLab.Core.Projects;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace CSharpLab.Tests;

public sealed class LanguageServiceTests : IDisposable
{
    private readonly string _dir;
    private readonly LanguageService _ls = new();

    public LanguageServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "csharplab-ls", Guid.NewGuid().ToString("N")[..8], "Meu Jogo");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "MeuJogo.csproj"), ProjectCreator.CsprojContent("MeuJogo"));
        File.WriteAllText(Path.Combine(_dir, "Heroi.cs"), "class Heroi\n{\n    public int Vida = 10;\n    public void Atacar() { }\n}\n");
    }

    public void Dispose()
    {
        _ls.Dispose();
        try { Directory.Delete(Path.GetDirectoryName(_dir)!, true); } catch { }
    }

    private string Open(string fileName, string text)
    {
        var path = Path.Combine(_dir, fileName);
        File.WriteAllText(path, text);
        _ls.LoadProject(ProjectEvaluator.Estimate(Path.Combine(_dir, "MeuJogo.csproj")));
        var key = LanguageService.KeyFor(path);
        _ls.OpenDocument(key, path, SourceText.From(text), 1);
        return key;
    }

    private async Task<List<string>> Complete(string key, string text, string marker)
    {
        var pos = text.IndexOf(marker) + marker.Length;
        var result = await _ls.GetCompletionsAsync(key, pos, null, CancellationToken.None);
        return result?.Items.Select(i => i.DisplayText).ToList() ?? [];
    }

    [Fact]
    public async Task Completion_sugere_membros_reais_de_Console()
    {
        var text = "Console.Wri";
        var key = Open("Program.cs", text);
        var items = await Complete(key, text, "Console.Wri");
        Assert.Contains("WriteLine", items);
        Assert.Contains("Write", items);
    }

    [Fact]
    public async Task Completion_ve_variavel_local_e_classe_de_outro_arquivo()
    {
        var text = "var heroi = new Heroi();\nint pontos = 3;\nheroi.\n";
        var key = Open("Program.cs", text);
        var members = await Complete(key, text, "heroi.");
        Assert.Contains("Vida", members);
        Assert.Contains("Atacar", members);

        var text2 = "int pontos = 3;\nvar x = po";
        _ls.UpdateDocument(key, SourceText.From(text2), 2);
        var names = await Complete(key, text2, "= po");
        Assert.Contains("pontos", names);
    }

    [Fact]
    public async Task Completion_reflete_texto_nao_salvo_de_outro_arquivo()
    {
        var key = Open("Program.cs", "var h = new Heroi();\nh.");
        var heroiPath = Path.Combine(_dir, "Heroi.cs");
        var heroiKey = LanguageService.KeyFor(heroiPath);
        _ls.OpenDocument(heroiKey, heroiPath, SourceText.From("class Heroi { public int Mana; }"), 1);
        var items = await Complete(key, "var h = new Heroi();\nh.", "h.");
        Assert.Contains("Mana", items);
        Assert.DoesNotContain("Vida", items);
    }

    private async Task<List<CodeDiagnostic>> Diagnose(string text, string file = "Program.cs")
    {
        Open(file, text);
        var snapshot = await _ls.GetDiagnosticsAsync(CancellationToken.None);
        return snapshot.Diagnostics.Where(d => d.Level == DiagnosticLevel.Error).ToList();
    }

    [Fact]
    public async Task Erros_comuns_em_portugues()
    {
        var d = await Diagnose("Console.WriteLine(1)\nint y = 2;");
        Assert.Contains(d, e => e.Id == "CS1002" && e.Message == "Faltou \";\"." && e.Line == 1);

        d = await Diagnose("Console.WriteLine(vidaa);");
        Assert.Contains(d, e => e.Id == "CS0103" && e.Message == "\"vidaa\" não foi encontrado neste trecho.");

        // Nome quase igual a um que existe: sugere o certo (o erro continua sendo para a pessoa corrigir).
        d = await Diagnose("string name = \"Ana\";\nConsole.WriteLine(nome);");
        Assert.Contains(d, e => e.Id == "CS0103" && e.Message == "\"nome\" não foi encontrado neste trecho. Você quis dizer \"name\"?");
        d = await Diagnose("int playerHealth = 10;\nplayerhealth -= 2;");
        Assert.Contains(d, e => e.Id == "CS0103" && e.Message.EndsWith("Você quis dizer \"playerHealth\"?"));

        d = await Diagnose("Console.Escrever(\"oi\");");
        Assert.Contains(d, e => e.Id == "CS0117" && e.Message == "\"Console\" não possui um membro chamado \"Escrever\".");

        d = await Diagnose("int idade = \"vinte\";");
        Assert.Contains(d, e => e.Id == "CS0029" && e.Message == "Não é possível colocar texto em uma variável int.");

        d = await Diagnose("double a = 1.5; int b = a;");
        Assert.Contains(d, e => e.Id == "CS0266" && e.Message == "A conversão de \"double\" para \"int\" precisa ser explícita.");
    }

    [Fact]
    public async Task Implicit_usings_e_arquivo_irmao_nao_geram_falso_erro()
    {
        var d = await Diagnose("List<int> valores = new();\nvar h = new Heroi();\nh.Atacar();\nConsole.WriteLine(valores.Count + h.Vida);");
        Assert.Empty(d);
    }

    [Fact]
    public async Task Erro_em_arquivo_irmao_aparece_com_o_arquivo_certo()
    {
        File.WriteAllText(Path.Combine(_dir, "Heroi.cs"), "class Heroi\n{\n    public int Vida = \"dez\";\n}\n");
        var d = await Diagnose("var h = new Heroi();");
        var e = Assert.Single(d);
        Assert.EndsWith("Heroi.cs", e.FilePath);
        Assert.Equal(3, e.Line);
    }

    [Fact]
    public async Task Formatacao_organiza_sem_mudar_strings_e_comentarios()
    {
        var text = "if(true){\nConsole.WriteLine(\"a   b\");   // comentário   assim\n      int  x=1;}\n";
        var key = Open("Program.cs", text);
        var changes = await _ls.FormatAsync(key, CancellationToken.None);
        var formatted = SourceText.From(text).WithChanges(changes).ToString();
        Assert.Contains("if (true)\n{\n    Console.WriteLine(\"a   b\");", formatted.Replace("\r\n", "\n"));
        Assert.Contains("// comentário   assim", formatted);
        Assert.Contains("    int x = 1;", formatted);
    }

    [Fact]
    public async Task Assinatura_mostra_parametro_atual()
    {
        var text = "Math.Max(1, ";
        var key = Open("Program.cs", text);
        var help = await _ls.GetSignatureHelpAsync(key, text.Length, CancellationToken.None);
        Assert.NotNull(help);
        Assert.Equal(1, help!.ActiveParameter);
        Assert.Contains(help.Signatures, s => s.Text.Contains("Max(int val1, int val2)"));
    }

    [Theory]
    [InlineData("class A { void M() { fo } }", "fo", CodeContext.Statement)]
    [InlineData("class A { fo }", "fo", CodeContext.Member)]
    [InlineData("int x = 1;\nfo", "fo", CodeContext.Statement)]
    [InlineData("var nome = re", "re", CodeContext.Expression)]
    [InlineData("// fo", "fo", CodeContext.None)]
    [InlineData("var s = \"fo\";", "fo", CodeContext.None)]
    public void Contexto_para_snippets(string code, string word, CodeContext expected)
    {
        var root = CSharpSyntaxTree.ParseText(code).GetRoot();
        var ctx = SyntaxContext.GetContext(root, code.IndexOf(word));
        if (expected == CodeContext.None) Assert.Equal(CodeContext.None, ctx);
        else Assert.True((ctx & expected) != 0, $"Contexto {ctx}, esperado {expected}");
    }

    [Fact]
    public void Class_nao_e_oferecido_dentro_de_metodo()
    {
        var code = "class A { void M() { cl } }";
        var root = CSharpSyntaxTree.ParseText(code).GetRoot();
        var ctx = SyntaxContext.GetContext(root, code.IndexOf("cl }"));
        Assert.False(Snippets.Fits(Snippets.Find("class")!, ctx));
        Assert.True(Snippets.Fits(Snippets.Find("for")!, ctx));
    }
}
