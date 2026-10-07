using CSharpLab.Core.Language;

namespace CSharpLab.Tests;

/// <summary>Erros que param o programa, explicados em português.</summary>
public sealed class RuntimeErrorsTests
{
    private static RuntimeExplanation Explain(string type, string message, string line = "") =>
        RuntimeErrors.Explain(new RuntimeCrash(type, message, []), line);

    [Fact]
    public void Texto_que_nao_e_numero()
    {
        var e = Explain("System.FormatException", "The input string 'abc' was not in a correct format.", "int idade = int.Parse(texto);");
        Assert.Equal("\"abc\" não é um número válido, então int.Parse não conseguiu converter.", e.Message);
        Assert.Contains("int.TryParse", e.Tip);
    }

    [Theory]
    [InlineData("System.NullReferenceException", "Object reference not set to an instance of an object.", "", "null")]
    [InlineData("System.IndexOutOfRangeException", "Index was outside the bounds of the array.", "", "array")]
    [InlineData("System.ArgumentOutOfRangeException", "Index was out of range. Must be non-negative and less than the size of the collection. (Parameter 'index')", "", "lista")]
    [InlineData("System.DivideByZeroException", "Attempted to divide by zero.", "", "zero")]
    [InlineData("System.InvalidOperationException", "Collection was modified; enumeration operation may not execute.", "", "foreach")]
    [InlineData("System.InvalidOperationException", "Sequence contains no elements", "", "vazia")]
    [InlineData("System.Collections.Generic.KeyNotFoundException", "The given key 'espada' was not present in the dictionary.", "", "\"espada\"")]
    [InlineData("System.ArgumentNullException", "Value cannot be null. (Parameter 's')", "int n = int.Parse(Console.ReadLine());", "ReadLine")]
    [InlineData("System.OverflowException", "Value was either too large or too small for an Int32.", "int n = int.Parse(t);", "grande")]
    public void Erros_comuns_em_portugues(string type, string message, string line, string expected) =>
        Assert.Contains(expected, Explain(type, message, line).Message);

    [Fact]
    public void Erro_desconhecido_mostra_o_tipo_e_a_mensagem_original()
    {
        var e = Explain("System.Security.SecurityException", "Something odd.");
        Assert.Equal("O programa parou com um erro (SecurityException): Something odd.", e.Message);
        Assert.Null(e.Tip);
    }

    [Fact]
    public void Recursao_infinita_pelo_codigo_de_saida()
    {
        Assert.Contains("Recursão infinita", RuntimeErrors.ExplainExitCode(unchecked((int)0xC00000FD)));
        Assert.Null(RuntimeErrors.ExplainExitCode(1));
    }

    [Fact]
    public void Le_o_relatorio_do_gancho_e_acha_a_linha_do_usuario()
    {
        var dir = Path.Combine(Path.GetTempPath(), "csharplab-crash-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var report = Path.Combine(dir, "r.txt");
            File.WriteAllText(report,
                "type=System.FormatException\nmessage=The input string 'x' was not in a correct format.\n" +
                @"frame=C:\outro\Lib.cs|10|5|Parse" + "\n" + $"frame={dir}\\Program.cs|3|9|<Main>$\n");
            var crash = RuntimeErrors.Read(report)!;
            Assert.Equal("FormatException", crash.ShortType);
            Assert.Equal(2, crash.Frames.Count);
            var frame = RuntimeErrors.UserFrame(crash, dir)!;
            Assert.Equal(3, frame.Line);
            Assert.Null(RuntimeErrors.Read(Path.Combine(dir, "nao-existe.txt")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData("https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/cs0103?f1url=%3FappId",
        "https://learn.microsoft.com/pt-br/dotnet/csharp/language-reference/compiler-messages/cs0103")]
    [InlineData("https://learn.microsoft.com/dotnet/csharp/misc/cs1002", "https://learn.microsoft.com/pt-br/dotnet/csharp/misc/cs1002")]
    public void Ajuda_sempre_em_portugues(string url, string expected) => Assert.Equal(expected, HelpLinks.ToPortuguese(url));

    [Theory]
    [InlineData("CS0103", true)]
    [InlineData("EXEC", true)]
    [InlineData("DICA01", false)]
    [InlineData("NU1101", false)]
    public void Saiba_mais_so_onde_existe_pagina(string id, bool expected) => Assert.Equal(expected, HelpLinks.HasHelp(id));
}
