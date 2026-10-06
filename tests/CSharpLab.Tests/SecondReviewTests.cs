using CSharpLab.Core.Language;
using CSharpLab.Core.Projects;
using CSharpLab.Core.Settings;
using Microsoft.CodeAnalysis.Text;

namespace CSharpLab.Tests;

public sealed class SecondReviewTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "csharplab-review", Guid.NewGuid().ToString("N"));
    public SecondReviewTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Uma_instancia_por_pasta_de_configuracoes()
    {
        using (var first = InstanceLease.TryAcquire(_dir))
        {
            Assert.NotNull(first);
            Assert.Null(InstanceLease.TryAcquire(_dir));
        }
        using var reopened = InstanceLease.TryAcquire(_dir);
        Assert.NotNull(reopened);
    }

    [Fact]
    public async Task Console_definido_em_props_importado_e_condicoes_sao_avaliados()
    {
        var project = Path.Combine(_dir, "App.csproj");
        File.WriteAllText(Path.Combine(_dir, "Directory.Build.props"),
            "<Project><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        File.WriteAllText(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup Condition="'$(Configuration)' == 'Release'">
                <OutputType>Library</OutputType><TargetFramework>net9.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        Assert.Null(ProjectFile.Read(project).OutputType);
        var evaluated = await ProjectFile.ReadEvaluatedAsync(project, CancellationToken.None);
        Assert.True(evaluated.IsConsole);
        Assert.Equal("net10.0", evaluated.TargetFramework);
    }

    [Fact]
    public async Task Varredura_de_arquivos_nao_entra_em_junctions()
    {
        var root = Path.Combine(_dir, "projeto");
        var outside = Path.Combine(_dir, "externo");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        var ownFile = Path.Combine(root, "Program.cs");
        File.WriteAllText(ownFile, "// próprio");
        File.WriteAllText(Path.Combine(outside, "Fora.cs"), "// externo");
        var link = Path.Combine(root, "link");
        var command = $"New-Item -ItemType Junction -Path '{link.Replace("'", "''")}' -Target '{outside.Replace("'", "''")}' -ErrorAction Stop | Out-Null";
        var start = new System.Diagnostics.ProcessStartInfo("powershell.exe")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(command)));
        using var process = System.Diagnostics.Process.Start(start)!;
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, error);
        try
        {
            Assert.True(File.GetAttributes(link).HasFlag(FileAttributes.ReparsePoint));
            Assert.Equal(ownFile, Assert.Single(ProjectLocator.DefaultCompileFiles(root)));
        }
        finally { Directory.Delete(link); }
    }

    [Fact]
    public async Task Assinatura_destaca_parametro_nomeado_fora_da_ordem()
    {
        using var ls = new LanguageService();
        var path = Path.Combine(_dir, "Program.cs");
        var text = "void M(int primeiro, string segundo) {}\nM(segundo: \"a\", primeiro: 1);";
        var key = LanguageService.KeyFor(path);
        ls.OpenDocument(key, path, SourceText.From(text), 1);
        var help = await ls.GetSignatureHelpAsync(key, text.IndexOf("\"a\"") + 1, CancellationToken.None);
        Assert.NotNull(help);
        Assert.Equal(1, help.ActiveParameter);
        help = await ls.GetSignatureHelpAsync(key, text.LastIndexOf('1') + 1, CancellationToken.None);
        Assert.Equal(0, help!.ActiveParameter);
    }

    [Fact]
    public async Task Parametro_nomeado_tem_indice_proprio_em_cada_sobrecarga()
    {
        using var ls = new LanguageService();
        var path = Path.Combine(_dir, "Program.cs");
        var text = "class C { void M(int primeiro, string segundo) {} void M(string segundo, int primeiro, bool terceiro) {} void T() { M(segundo: \"a\", primeiro: 1); } }";
        var key = LanguageService.KeyFor(path);
        ls.OpenDocument(key, path, SourceText.From(text), 1);
        var help = await ls.GetSignatureHelpAsync(key, text.IndexOf("\"a\"") + 1, CancellationToken.None);
        Assert.NotNull(help);
        Assert.Equal(new[] { 1, 0 }, help.ActiveParameters);
    }

    [Fact]
    public async Task Opcoes_do_editor_seguem_editorconfig_e_mudam_ao_recarregar()
    {
        var path = Path.Combine(_dir, "Program.cs");
        File.WriteAllText(path, "class C {} ");
        var config = Path.Combine(_dir, ".editorconfig");
        File.WriteAllText(config, "root = true\n[*.cs]\nindent_style = space\nindent_size = 2\ntab_width = 8\n");
        using var ls = new LanguageService();
        var model = ProjectModel.Loose("App", _dir) with { CompileFiles = [path] };
        ls.LoadProject(model);
        var key = LanguageService.KeyFor(path);
        var options = await ls.GetEditorOptionsAsync(key, CancellationToken.None);
        Assert.Equal(new EditorOptions(false, 2, 8), options);
        File.WriteAllText(config, "root = true\n[*.cs]\nindent_style = tab\nindent_size = 3\ntab_width = 3\n");
        ls.LoadProject(model);
        Assert.Equal(new EditorOptions(true, 3, 3), await ls.GetEditorOptionsAsync(key, CancellationToken.None));
    }

    [Fact]
    public async Task Abrir_arquivo_excluido_do_compile_nao_o_reintroduz_no_projeto()
    {
        var main = Path.Combine(_dir, "Program.cs");
        var excluded = Path.Combine(_dir, "Fora.cs");
        File.WriteAllText(main, "class Main {} ");
        File.WriteAllText(excluded, "class Fora {} ");
        using var ls = new LanguageService();
        ls.LoadProject(ProjectModel.Loose("App", _dir) with { CompileFiles = [main], IsEvaluated = true });
        var key = LanguageService.KeyFor(excluded);
        ls.OpenDocument(key, excluded, SourceText.From("class Fora {}"), 1);
        ls.OnFileCreatedOrChanged(excluded);
        Assert.NotEqual(ls.GetDocument(LanguageService.KeyFor(main))!.Project.Id, ls.GetDocument(key)!.Project.Id);
        var compilation = await ls.GetDocument(LanguageService.KeyFor(main))!.Project.GetCompilationAsync();
        Assert.Null(compilation!.GetTypeByMetadataName("Fora"));
    }
}
