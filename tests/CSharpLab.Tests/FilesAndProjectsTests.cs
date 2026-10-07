using System.Text;
using CSharpLab.Core.Build;
using CSharpLab.Core.Files;
using CSharpLab.Core.Language;
using CSharpLab.Core.Projects;

namespace CSharpLab.Tests;

public sealed class FilesAndProjectsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "csharplab-fp", Guid.NewGuid().ToString("N")[..8], "Pasta com espaço e ação");

    public FilesAndProjectsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(Path.GetDirectoryName(_root)!, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(Path.GetDirectoryName(_root)!, true);
        }
        catch { }
    }

    [Theory]
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF }, "UTF-8 com BOM")]
    [InlineData(new byte[0], "UTF-8")]
    public void Salvar_preserva_codificacao_e_finais_de_linha(byte[] preamble, string expected)
    {
        var path = Path.Combine(_root, "a.cs");
        var body = Encoding.UTF8.GetBytes("// ação\nint x = 1;\r\n");
        File.WriteAllBytes(path, [.. preamble, .. body]);

        var content = TextFileIO.Read(path);
        Assert.Equal(expected, TextFileIO.DescribeEncoding(content.Encoding));
        TextFileIO.Save(path, content.Text + "// fim\n", content.Encoding);

        var bytes = File.ReadAllBytes(path);
        Assert.Equal(preamble, bytes.Take(preamble.Length).ToArray());
        Assert.Equal("// ação\nint x = 1;\r\n// fim\n", Encoding.UTF8.GetString(bytes, preamble.Length, bytes.Length - preamble.Length));
    }

    [Fact]
    public void Arquivo_ansi_antigo_e_lido_e_salvo_em_1252()
    {
        var path = Path.Combine(_root, "velho.cs");
        File.WriteAllBytes(path, TextFileIO.Legacy.GetBytes("// ação"));
        var content = TextFileIO.Read(path);
        Assert.Equal("// ação", content.Text);
        TextFileIO.Save(path, content.Text, content.Encoding);
        Assert.Equal(TextFileIO.Legacy.GetBytes("// ação"), File.ReadAllBytes(path));
    }

    [Fact]
    public void Falha_ao_salvar_nao_trunca_o_original()
    {
        var path = Path.Combine(_root, "protegido.cs");
        File.WriteAllText(path, "original");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        Assert.ThrowsAny<UnauthorizedAccessException>(() => TextFileIO.Save(path, "novo", TextFileIO.Utf8NoBom));
        Assert.Equal("original", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(_root));
    }

    [Fact]
    public void Nomes_e_extensoes()
    {
        Assert.Equal("Heroi.cs", FileOperations.WithDefaultExtension("Heroi"));
        Assert.Equal("Heroi.cs", FileOperations.WithDefaultExtension("Heroi.cs"));
        Assert.Equal("Heroi.cs", FileOperations.WithDefaultExtension("Heroi.cs.cs"));
        Assert.NotNull(FileOperations.ValidateName("a:b"));
        Assert.NotNull(FileOperations.ValidateName("CON"));
        Assert.Null(FileOperations.ValidateName("Inimigo Ágil.cs"));
    }

    [Fact]
    public void Novo_projeto_em_caminho_com_espacos_e_acentos_e_sem_sobrescrever()
    {
        var created = ProjectCreator.CreateConsoleProject(_root, "A_Torre");
        Assert.True(File.Exists(created.ProjectPath));
        Assert.Contains("net10.0", File.ReadAllText(created.ProjectPath));
        Assert.Contains("Olá, mundo!", File.ReadAllText(created.ProgramPath));

        // A pasta já existe e tem arquivos: recusa e não mexe em nada.
        var before = File.ReadAllText(created.ProgramPath);
        Assert.Throws<UserFacingException>(() => ProjectCreator.CreateConsoleProject(_root, "A_Torre", "outro"));
        Assert.Equal(before, File.ReadAllText(created.ProgramPath));
    }

    [Fact]
    public void Namespace_valido_para_nomes_com_espaco()
    {
        Assert.Equal("A_Torre", ProjectCreator.ToIdentifier("A Torre"));
        Assert.Equal("_1Jogo", ProjectCreator.ToIdentifier("1Jogo"));
        Assert.Contains("<RootNamespace>Jogo_da_Velha</RootNamespace>", ProjectCreator.CsprojContent("Jogo da Velha"));
    }

    [Fact]
    public void Localiza_projetos_console_e_ignora_bin_obj()
    {
        var a = ProjectCreator.CreateConsoleProject(_root, "Um");
        ProjectCreator.CreateConsoleProject(_root, "Dois");
        Directory.CreateDirectory(Path.Combine(a.Directory, "bin"));
        File.WriteAllText(Path.Combine(a.Directory, "bin", "Falso.csproj"), "<Project/>");
        var found = ProjectLocator.RunnableProjects(ProjectLocator.FindProjects(_root));
        Assert.Equal(2, found.Count);
    }

    [Fact]
    public void Pasta_com_dois_arquivos_de_nivel_superior_nao_vira_projeto()
    {
        File.WriteAllText(Path.Combine(_root, "a.cs"), "Console.WriteLine(1);");
        File.WriteAllText(Path.Combine(_root, "b.cs"), "Console.WriteLine(2);");
        var problem = ProjectCreator.CheckFolderForNewProject(_root, out _);
        Assert.NotNull(problem);
        Assert.Contains("Mais de um arquivo", problem);
    }

    [Fact]
    public void Mensagens_de_build_em_ingles_sao_traduzidas()
    {
        Assert.Equal("\"vida\" não foi encontrado neste trecho.",
            DiagnosticTranslator.TranslateBuildMessage("CS0103", "The name 'vida' does not exist in the current context"));
        Assert.Equal("Faltou \";\".", DiagnosticTranslator.TranslateBuildMessage("CS1002", "; expected"));
        Assert.Equal("O argumento 1 precisa ser \"int\", mas recebeu \"string\".",
            DiagnosticTranslator.TranslateBuildMessage("CS1503", "Argument 1: cannot convert from 'string' to 'int'"));
        Assert.Equal("Something new", DiagnosticTranslator.TranslateBuildMessage("CS9999", "Something new"));
    }

    [Fact]
    public void Disco_ocupado_por_um_instante_e_repetido_erro_de_verdade_nao()
    {
        // "Não foi possível remover o arquivo a ser substituído" (antivírus segurando o arquivo): tenta de novo.
        int calls = 0;
        DiskRetry.Run(() =>
        {
            if (++calls < 3) throw new IOException("ocupado", unchecked((int)0x80070497));
        });
        Assert.Equal(3, calls);

        // Arquivo que não existe: não adianta esperar.
        calls = 0;
        Assert.Throws<FileNotFoundException>(() => DiskRetry.Run(() =>
        {
            calls++;
            throw new FileNotFoundException("sumiu");
        }));
        Assert.Equal(1, calls);

        // Ocupado para sempre: desiste depois de algumas tentativas e mostra o erro.
        calls = 0;
        Assert.Throws<IOException>(() => DiskRetry.Run(() =>
        {
            calls++;
            throw new IOException("em uso", unchecked((int)0x80070020));
        }, attempts: 3));
        Assert.Equal(3, calls);
    }

    [Fact]
    public void Ponto_e_virgula_esquecido_no_fim_da_linha_nao_vira_pedido_de_virgula()
    {
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(
            "string name = Console.ReadLine()\nint age = 3;\nMath.Max(1 2);\n");
        var messages = tree.GetDiagnostics().Where(d => d.Id == "CS1003").Select(DiagnosticTranslator.Translate).ToList();
        Assert.Contains("Faltou \";\" no fim da linha.", messages);
        Assert.Contains("Era esperado \",\".", messages);       // Math.Max(1 2): aí falta mesmo a vírgula
    }

    [Fact]
    public void Texto_do_msbuild_com_caminho_complexo()
    {
        var output = @"C:\Pasta (1)\Meu Jogo\Program.cs(12,5): error CS1002: ; expected [C:\Pasta (1)\Meu Jogo\Jogo.csproj]
C:\Program Files\dotnet\sdk\10.0.301\Microsoft.Common.targets(10,5): warning MSB3270: algo [C:\x.csproj]
C:\x\x.csproj : error NU1101: Unable to find package Foo.";
        var diags = BuildService.ParseText(output, @"C:\x");
        Assert.Contains(diags, d => d.Id == "CS1002" && d.FilePath == @"C:\Pasta (1)\Meu Jogo\Program.cs" && d.Line == 12 && d.Column == 5);
        Assert.Contains(diags, d => d.Id == "NU1101" && d.Line == 0);
    }

    [Fact]
    public void Argumentos_de_execucao_sao_divididos_corretamente()
    {
        Assert.Equal(["exec", @"C:\a b\app.dll"], BuildService.SplitArguments(@"exec ""C:\a b\app.dll"""));
        Assert.Empty(BuildService.SplitArguments(""));
    }
}

[Collection("SampleApp")]
public sealed class BuildServiceTests(SampleConsoleApp app)
{
    [Fact]
    public async Task Compila_e_obtem_comando_de_execucao()
    {
        var result = await BuildService.BuildAsync(app.ProjectPath, null, CancellationToken.None);
        Assert.True(result.Success, result.Log);
        var model = await ProjectEvaluator.EvaluateAsync(app.ProjectPath, true, CancellationToken.None);
        Assert.True(model.IsEvaluated);
        Assert.Contains(model.References, r => r.EndsWith("System.Runtime.dll"));
        var launch = await BuildService.GetLaunchAsync(model, CancellationToken.None);
        Assert.Equal(app.ExePath, launch.FileName, ignoreCase: true);
        Assert.Equal(app.Directory, launch.WorkingDirectory, ignoreCase: true);
    }

    [Fact]
    public async Task Erro_de_compilacao_vem_estruturado_com_arquivo_e_linha()
    {
        var dir = Path.Combine(Path.GetTempPath(), "csharplab-build", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var created = ProjectCreator.CreateConsoleProject(dir, "Quebrado", "Console.WriteLine(1)\nint y = 2;\n");
            var result = await BuildService.BuildAsync(created.ProjectPath, null, CancellationToken.None);
            Assert.Equal(BuildOutcome.Failed, result.Outcome);
            var e = Assert.Single(result.Diagnostics, d => d.Severity == BuildSeverity.Error);
            Assert.Equal("CS1002", e.Id);
            Assert.Equal(created.ProgramPath, e.FilePath, ignoreCase: true);
            Assert.Equal(1, e.Line);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public async Task Cancelar_compilacao_retorna_cancelado()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var dir = Path.Combine(Path.GetTempPath(), "csharplab-build", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var created = ProjectCreator.CreateConsoleProject(dir, "Cancelado");
            var result = await BuildService.BuildAsync(created.ProjectPath, null, cts.Token);
            Assert.Equal(BuildOutcome.Cancelled, result.Outcome);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}

public sealed class RenameTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "csharplab-ren", Guid.NewGuid().ToString("N")[..8]);

    public RenameTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void Renomeia_arquivo_e_permite_trocar_so_maiusculas()
    {
        var path = Path.Combine(_dir, "heroi.cs");
        File.WriteAllText(path, "x");
        var renamed = FileOperations.Rename(path, "Heroi.cs");
        Assert.Equal("Heroi.cs", Path.GetFileName(Directory.GetFiles(_dir).Single()));
        Assert.Equal(Path.Combine(_dir, "Heroi.cs"), renamed);
    }

    [Fact]
    public void Nao_sobrescreve_ao_renomear()
    {
        File.WriteAllText(Path.Combine(_dir, "a.cs"), "a");
        File.WriteAllText(Path.Combine(_dir, "b.cs"), "b");
        Assert.Throws<UserFacingException>(() => FileOperations.Rename(Path.Combine(_dir, "a.cs"), "b.cs"));
        Assert.Equal("b", File.ReadAllText(Path.Combine(_dir, "b.cs")));
    }

    [Fact]
    public void Renomeia_pasta_so_com_maiusculas()
    {
        var sub = Path.Combine(_dir, "modelos");
        Directory.CreateDirectory(sub);
        FileOperations.Rename(sub, "Modelos");
        Assert.Equal("Modelos", Path.GetFileName(Directory.GetDirectories(_dir).Single()));
    }
}
