using System.Reflection;
using System.Reflection.Emit;
using System.Xml.Linq;
using CSharpLab.Core.Projects;
using CSharpLab.Core.Files;
using CSharpLab.GameEngine;
using ProjectIdentity = CSharpLab.Core.Projects.GameSaveIdentity;
using RuntimeIdentity = CSharpLab.GameEngine.GameSaveIdentity;

namespace CSharpLab.Tests;

public sealed class GameSaveIdentityTests
{
    private readonly string _root = Path.Combine(TestData.Root, "identidades", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Projetos_com_mesmo_nome_recebem_identificadores_diferentes()
    {
        Directory.CreateDirectory(Path.Combine(_root, "um"));
        Directory.CreateDirectory(Path.Combine(_root, "dois"));
        var a = ProjectCreator.CreateGameProject(Path.Combine(_root, "um"), "Torre");
        var b = ProjectCreator.CreateGameProject(Path.Combine(_root, "dois"), "Torre");
        var idA = Id(a.ProjectPath);
        var idB = Id(b.ProjectPath);
        Assert.True(Guid.TryParse(idA, out _));
        Assert.True(Guid.TryParse(idB, out _));
        Assert.NotEqual(idA, idB);
        GameKit.RefreshProject(a.Directory, a.ProjectPath);
        Assert.Equal(idA, Id(a.ProjectPath));
    }

    [Fact]
    public void Projeto_antigo_recebe_identidade_uma_vez_preservando_codigo_comentarios_e_codificacao()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "Torre.csproj");
        var original = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<Project Sdk=\"Microsoft.NET.Sdk\">\r\n  <!-- meu comentário -->\r\n  <PropertyGroup><OutputType>WinExe</OutputType></PropertyGroup>\r\n</Project>\r\n";
        File.WriteAllText(path, original, new System.Text.UTF8Encoding(true));
        ProjectIdentity.Ensure(path);
        var id = Id(path);
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]));
        var text = File.ReadAllText(path);
        Assert.Contains("<!-- meu comentário -->", text);
        Assert.Contains("<?xml", text);
        Assert.DoesNotContain("\n", text.Replace("\r\n", ""));
        ProjectIdentity.Ensure(path);
        Assert.Equal(bytes, File.ReadAllBytes(path));
        var renamed = Path.Combine(_root, "TorreNova.csproj");
        File.Move(path, renamed);
        ProjectIdentity.Ensure(renamed);
        Assert.Equal(id, Id(renamed));
    }

    [Theory]
    [InlineData("<AssemblyMetadata Include=\"CSharpLab.SaveId\" Value=\"invalido\" />")]
    [InlineData("<AssemblyMetadata Include=\"CSharpLab.SaveId\" Value=\"00000000000000000000000000000001\" Condition=\"'$(Configuration)' == 'Debug'\" />")]
    [InlineData("<AssemblyMetadata Include=\"CSharpLab.SaveId\" Value=\"00000000000000000000000000000001\" /><AssemblyMetadata Include=\"CSharpLab.SaveId\" Value=\"00000000000000000000000000000002\" />")]
    public void Identidade_invalida_ou_ambigua_nao_e_trocada_por_outra(string metadata)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "Torre.csproj");
        var original = "<Project><ItemGroup>" + metadata + "</ItemGroup></Project>";
        File.WriteAllText(path, original);
        Assert.Throws<UserFacingException>(() => ProjectIdentity.Ensure(path));
        Assert.Equal(original, File.ReadAllText(path));
    }

    [Fact]
    public void Motor_antigo_bloqueado_nao_e_usado_silenciosamente()
    {
        Directory.CreateDirectory(_root);
        var created = ProjectCreator.CreateGameProject(_root, "Torre");
        var library = GameKit.LibraryPath(created.Directory);
        var old = File.ReadAllBytes(library).Concat(new byte[] { 0 }).ToArray();
        File.WriteAllBytes(library, old); // PE válido, mas diferente da versão que veio com o editor.
        var id = Id(created.ProjectPath);
        using (var locked = new FileStream(library, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.Throws<UserFacingException>(() => GameKit.RefreshProject(created.Directory, created.ProjectPath));
        Assert.Equal(old, File.ReadAllBytes(library));
        Assert.Equal(id, Id(created.ProjectPath));
        GameKit.RefreshProject(created.Directory, created.ProjectPath);
        Assert.Equal(File.ReadAllBytes(GameKit.LibrarySource), File.ReadAllBytes(library));
    }

    [Fact]
    public void Runtime_le_identidade_embutida_e_normaliza_guid()
    {
        var id = Guid.NewGuid();
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("JogoTeste"), AssemblyBuilderAccess.Run);
        assembly.SetCustomAttribute(new CustomAttributeBuilder(typeof(AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)])!,
            [RuntimeIdentity.MetadataName, id.ToString("D")]));
        Assert.Equal(id.ToString("N"), RuntimeIdentity.Of(assembly));
        Assert.Equal(new Game("Título", id.ToString("D")).SaveId, new Game("Título novo", id.ToString("N")).SaveId);
    }

    [Theory]
    [InlineData("../outro")]
    [InlineData("a\\b")]
    [InlineData("")]
    [InlineData("nome com espaço")]
    public void Identificador_manual_invalido_nao_pode_sair_da_pasta_de_saves(string id)
        => Assert.Throws<GameException>(() => new Game("Título", id));

    internal static string Id(string path) => (string)XDocument.Load(path).Descendants("AssemblyMetadata")
        .Single(e => (string?)e.Attribute("Include") == ProjectIdentity.MetadataName).Attribute("Value")!;
}
