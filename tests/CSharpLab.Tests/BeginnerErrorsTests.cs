using CSharpLab.Core.Language;
using CSharpLab.Core.Projects;
using Microsoft.CodeAnalysis.Text;

namespace CSharpLab.Tests;

/// <summary>Mensagens em português para os erros mais comuns de quem está aprendendo.</summary>
public sealed class BeginnerErrorsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "csharplab-erros", Guid.NewGuid().ToString("N")[..8], "Jogo");
    private readonly LanguageService _ls = new();

    public BeginnerErrorsTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "Jogo.csproj"), ProjectCreator.CsprojContent("Jogo"));
    }

    public void Dispose()
    {
        _ls.Dispose();
        try { Directory.Delete(Path.GetDirectoryName(_dir)!, true); } catch { }
    }

    private async Task<string> MessageFor(string code, string id)
    {
        var path = Path.Combine(_dir, "Program.cs");
        File.WriteAllText(path, code);
        _ls.LoadProject(ProjectEvaluator.Estimate(Path.Combine(_dir, "Jogo.csproj")));
        _ls.OpenDocument(LanguageService.KeyFor(path), path, SourceText.From(code), 1);
        var snapshot = await _ls.GetDiagnosticsAsync(CancellationToken.None);
        var d = snapshot.Diagnostics.FirstOrDefault(x => x.Id == id);
        Assert.True(d != null, $"Esperava {id}. Veio: {string.Join(", ", snapshot.Diagnostics.Select(x => x.Id))}");
        return d!.Message;
    }

    [Fact]
    public async Task Metodo_de_instancia_chamado_sem_objeto() =>
        Assert.Equal("\"Atacar\" não é static: crie um objeto com new para usá-lo, ou marque-o como static.",
            await MessageFor("class Jogo { void Atacar() { } static void Main() { Atacar(); } }", "CS0120"));

    [Fact]
    public async Task Esqueceu_os_parenteses() =>
        Assert.Equal("\"ReadLine\" é um método: faltaram os parênteses para chamá-lo.",
            await MessageFor("string nome = Console.ReadLine;", "CS0428"));

    [Fact]
    public async Task Float_sem_sufixo() =>
        Assert.Equal("Números com vírgula são double. Para float, use o sufixo F (ex.: 1.5F).",
            await MessageFor("float dano = 1.5;", "CS0664"));

    [Fact]
    public async Task Atribuicao_no_lugar_de_comparacao() =>
        Assert.Equal("Um número não é verdadeiro/falso. Para comparar, use == (dois sinais de igual).",
            await MessageFor("int x = 1;\nif (x = 5) { }", "CS0029"));

    [Fact]
    public async Task Argumento_faltando() =>
        Assert.Equal("Falta o argumento \"dano\" na chamada de \"Atacar\".",
            await MessageFor("Atacar();\nstatic void Atacar(int dano) { }", "CS7036"));

    [Fact]
    public async Task Membro_privado() =>
        Assert.Equal("\"vida\" não é public e não pode ser usado daqui.",
            await MessageFor("var h = new Heroi();\nConsole.WriteLine(h.vida);\nclass Heroi { private int vida; }", "CS0122"));

    [Fact]
    public async Task Programa_vazio() =>
        Assert.StartsWith("O programa não tem por onde começar",
            await MessageFor("// nada aqui ainda", "CS5001"));

    [Fact]
    public async Task Return_com_valor_em_metodo_void() =>
        Assert.Equal("\"Curar\" é void: o return não pode devolver um valor.",
            await MessageFor("static void Curar() { return 5; }\nCurar();", "CS0127"));
}

public sealed class BeginnerHintsTests
{
    private static List<string> Hints(string code)
    {
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(
            "global using System; global using System.Collections.Generic; global using System.Linq;\n" + code);
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("t", [tree],
            Basic.Reference.Assemblies.Net100.References.All,
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.ConsoleApplication));
        return BeginnerHints.Analyze(compilation.GetSemanticModel(tree), CancellationToken.None).Select(h => h.Id).ToList();
    }

    [Theory]
    [InlineData("List<int> valores = new();\nConsole.WriteLine(valores);", BeginnerHints.PrintCollectionId)]
    [InlineData("int[] notas = { 1, 2 };\nConsole.Write(notas);", BeginnerHints.PrintCollectionId)]
    [InlineData("int soma = 7, qtd = 2;\ndouble media = soma / qtd;", BeginnerHints.IntegerDivisionId)]
    [InlineData("int a = 7, b = 2;\nvar r = (double)(a / b);", BeginnerHints.IntegerDivisionId)]
    [InlineData("int a = 7;\nfloat f = a / 2;", BeginnerHints.IntegerDivisionId)]
    [InlineData("int idade = int.Parse(Console.ReadLine());", BeginnerHints.ParseInputId)]
    [InlineData("double preco = double.Parse(Console.ReadLine()!);", BeginnerHints.ParseInputId)]
    [InlineData("string resposta = Console.ReadLine() ?? \"\";\nif (resposta == \"sim\") { }", BeginnerHints.CaseSensitiveInputId)]
    [InlineData("while (true)\n{\n    Console.WriteLine(\"oi\");\n}", BeginnerHints.EndlessLoopId)]
    [InlineData("while (true)\n{\n    for (int i = 0; i < 3; i++) { break; }\n}", BeginnerHints.EndlessLoopId)]
    public void Aponta_armadilhas(string code, string expected) => Assert.Contains(expected, Hints(code));

    [Theory]
    [InlineData("string nome = \"Ana\";\nConsole.WriteLine(nome);")]
    [InlineData("List<int> v = new();\nConsole.WriteLine(string.Join(\", \", v));")]
    [InlineData("char[] letras = { 'a' };\nConsole.WriteLine(letras);")]
    [InlineData("int a = 7, b = 2;\nint r = a / b;")]
    [InlineData("int a = 7, b = 2;\ndouble r = (double)a / b;")]
    [InlineData("double x = 7.0;\ndouble r = x / 2;")]
    [InlineData("List<int> v = new();\nConsole.WriteLine(v.Count);")]
    [InlineData("int.TryParse(Console.ReadLine(), out int n);")]
    [InlineData("int n = int.Parse(\"42\");")]
    [InlineData("string r = Console.ReadLine() ?? \"\";\nif (r.ToLower() == \"sim\") { }")]
    [InlineData("string r = Console.ReadLine() ?? \"\";\nif (r == \"1\") { }")]
    [InlineData("string estado = \"menu\";\nif (estado == \"menu\") { }")]
    [InlineData("while (true)\n{\n    if (Console.ReadKey().Key == ConsoleKey.Escape) break;\n}")]
    [InlineData("while (true)\n{\n    switch (1) { case 1: return; }\n}")]
    [InlineData("while (true)\n{\n    Action a = () => { return; };\n    break;\n}")]
    public void Nao_reclama_de_codigo_correto(string code) => Assert.Empty(Hints(code));
}
