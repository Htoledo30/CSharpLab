using CSharpLab.Core.Language;
using CSharpLab.Core.Projects;
using CSharpLab.GameEngine;
using Microsoft.CodeAnalysis.CSharp;

namespace CSharpLab.Tests;

/// <summary>O editor de código conhece as telas: sugere e confere os nomes do game.Find.</summary>
public sealed class GameAssistTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "csharplab-assist-telas", Guid.NewGuid().ToString("N")[..8]);

    public GameAssistTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "Screens"));
        File.WriteAllText(Path.Combine(_dir, "Screens", "Fight.json"),
            """{ "pieces": [ { "type": "Button", "name": "Attack" }, { "type": "Bar", "name": "PlayerHealth" } ] }""");
        File.WriteAllText(Path.Combine(_dir, "Screens", "Village.json"),
            """{ "pieces": [ { "type": "Button", "name": "Shop" }, { "type": "Button", "name": "Attack" } ] }""");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private const string Code = """
        var game = new Game("T");
        game.Scene("Fight", () =>
        {
            game.Find("Atk").OnClick(() => { });
            game.Find("PlayerHealth").Value = 3;
        });
        game.Scene("Village", () =>
        {
            game.Find("Attack").OnClick(() => { });
        });
        game.Scene("Plain", () =>
        {
            game.Find("X").Visible = false;
        });
        game.Start("Fight");
        """;

    [Fact]
    public void Cursor_dentro_do_Find_sabe_a_cena_e_o_que_foi_digitado()
    {
        var root = CSharpSyntaxTree.ParseText(Code).GetRoot();
        int caret = Code.IndexOf("\"Atk\"", StringComparison.Ordinal) + 3; // depois de "At
        var context = GameAssist.FindNameAt(root, caret)!;
        Assert.Equal("Fight", context.Scene);
        Assert.Equal("At", context.Typed);
        Assert.Equal(Code.IndexOf("\"Atk\"", StringComparison.Ordinal) + 1, context.Start);

        Assert.Null(GameAssist.FindNameAt(root, Code.IndexOf("\"T\"", StringComparison.Ordinal) + 1)); // new Game("T")
        Assert.Null(GameAssist.FindNameAt(root, Code.IndexOf("OnClick", StringComparison.Ordinal)));
    }

    [Fact]
    public void Aspas_recem_abertas_ja_mostram_as_sugestoes()
    {
        var code = "game.Scene(\"Fight\", () =>\n{\n    game.Find(\"\n});";
        var root = CSharpSyntaxTree.ParseText(code).GetRoot();
        var context = GameAssist.FindNameAt(root, code.IndexOf("Find(\"", StringComparison.Ordinal) + 6);
        Assert.NotNull(context);
        Assert.Equal("", context!.Typed);
        Assert.Equal("Fight", context.Scene);
    }

    [Fact]
    public void Nome_errado_e_cena_sem_tela_viram_dica()
    {
        var root = CSharpSyntaxTree.ParseText(Code).GetRoot();
        var hints = GameAssist.CheckFindNames(root, _dir).Select(h => h.Message).ToList();
        Assert.Equal(2, hints.Count);
        Assert.Contains(hints, m => m.Contains("\"Atk\" não existe na tela \"Fight\"") && m.Contains("Peças: Attack, PlayerHealth"));
        Assert.Contains(hints, m => m.Contains("A cena \"Plain\" não tem tela desenhada"));
    }

    [Fact]
    public void Tela_aberta_sem_salvar_vale_pelo_texto_da_aba()
    {
        var path = GameScreens.PathOf(_dir, "Fight");
        GameScreens.OpenTexts[path] = """{ "pieces": [ { "type": "Button", "name": "Atk" } ] }""";
        try
        {
            Assert.Equal(["Atk"], GameScreens.Pieces(_dir, "Fight")!.Select(p => p.Name));
            var root = CSharpSyntaxTree.ParseText(Code).GetRoot();
            Assert.DoesNotContain(GameAssist.CheckFindNames(root, _dir), h => h.Message.Contains("\"Atk\""));
        }
        finally
        {
            GameScreens.OpenTexts.TryRemove(path, out _);
        }
    }

    [Fact]
    public void Renomear_so_troca_a_peca_da_mesma_cena()
    {
        var root = CSharpSyntaxTree.ParseText(Code).GetRoot();
        var spans = GameAssist.FindPieceReferences(root, "Village", "attack");
        var span = Assert.Single(spans);
        Assert.Equal("Attack", Code.Substring(span.Start, span.Length));
        Assert.True(span.Start > Code.IndexOf("\"Village\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Exemplo_com_tela_desenhada_usa_so_nomes_que_existem()
    {
        var example = Examples.All.Single(e => e.Id == "RpgScreens");
        var files = Examples.FilesOf(example);
        var screens = files.Where(f => f.Key.StartsWith("Screens/", StringComparison.Ordinal)).ToList();
        Assert.Equal(5, screens.Count);
        foreach (var (name, text) in screens)
        {
            var parsed = ScreenFile.Parse(text);
            Assert.True(parsed.Success, $"{name}: {parsed.Error}");
            Assert.Empty(parsed.Warnings);
        }

        var created = Examples.Create(example, _dir);
        Assert.True(File.Exists(Path.Combine(created.Directory, "Screens", "Fight.json")));
        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(created.ProgramPath)).GetRoot();
        Assert.Empty(GameAssist.CheckFindNames(root, created.Directory).Select(h => h.Message));
        // Toda cena do código tem a sua tela.
        foreach (var scene in new[] { "Name", "Village", "Fight", "Victory", "GameOver" })
            Assert.NotNull(GameScreens.Pieces(created.Directory, scene));
    }

    private const string HandlerCode = """
        var game = new Game("T");
        game.Scene("Fight", () =>
        {
            game.Find("Attack").OnClick(() => { });
            game.Find("Potion").Visible = false;
        });
        game.Scene("Empty", () =>
        {
        });
        game.Scene("Inline", () => { game.Write("x"); });
        game.Scene("Expr", () => game.Write("x"));
        game.Start("Fight");
        """;

    [Fact]
    public void Comandos_de_montar_tela_numa_cena_desenhada_ganham_dica()
    {
        var code = """
            var game = new Game("T");
            game.Scene("Fight", () =>
            {
                game.Title("Luta");
                game.Write("Isto pode: vai para as mensagens.");
                game.Find("Attack").OnClick(() => game.GoTo("Plain"));
            });
            game.Scene("Plain", () =>
            {
                game.Title("Sem tela: aqui pode.");
                game.Button("Voltar", () => game.GoTo("Fight"));
            });
            game.Start("Fight");
            """;
        var root = CSharpSyntaxTree.ParseText(code).GetRoot();
        var hint = Assert.Single(GameAssist.CheckBuildCallsInDrawnScenes(root, _dir));
        Assert.Equal(GameAssist.BuildInDrawnSceneId, hint.Id);
        Assert.Contains("game.Title não funciona", hint.Message);
        Assert.Contains("um Texto na aba Tela", hint.Message);
        Assert.Equal(code.IndexOf("Title(\"Luta\")", StringComparison.Ordinal), hint.Location.SourceSpan.Start);
    }

    [Fact]
    public void Sabe_se_o_botao_ja_faz_algo_no_codigo()
    {
        var root = CSharpSyntaxTree.ParseText(HandlerCode).GetRoot();
        var attack = Assert.Single(GameAssist.FindPieceUses(root, "Fight", "Attack", "OnClick"));
        Assert.True(attack.IsHandler);
        Assert.Equal(HandlerCode.IndexOf("game.Find(\"Attack\")", StringComparison.Ordinal), attack.Start);
        Assert.False(Assert.Single(GameAssist.FindPieceUses(root, "Fight", "Potion", "OnClick")).IsHandler);
        Assert.Empty(GameAssist.FindPieceUses(root, "Empty", "Attack", "OnClick"));   // outra cena
    }

    [Theory]
    [InlineData("Fight", "    game.Find(\"Potion\").Visible = false;\n\n    game.Find(\"Run\").OnClick(() =>\n    {\n        \n    });\n});")]
    [InlineData("Empty", "game.Scene(\"Empty\", () =>\n{\n    game.Find(\"Run\").OnClick(() =>\n    {\n        \n    });\n});")]
    [InlineData("Inline", "game.Scene(\"Inline\", () => { game.Write(\"x\");\n\n    game.Find(\"Run\").OnClick(() =>\n    {\n        \n    });\n});")]
    public void Escreve_o_OnClick_no_fim_da_cena_com_o_recuo_certo(string scene, string expected)
    {
        var code = HandlerCode.Replace("\r\n", "\n");
        var root = CSharpSyntaxTree.ParseText(code).GetRoot();
        var insertion = GameAssist.HandlerInsertion(root, scene, "Run", "OnClick", "()", "\n")!;
        var result = code.Remove(insertion.Offset, insertion.Length).Insert(insertion.Offset, insertion.Text);
        Assert.Contains(expected, result);
        Assert.Empty(CSharpSyntaxTree.ParseText(result).GetDiagnostics());
        // O cursor fica na linha vazia entre as chaves do OnClick.
        int caret = insertion.Offset + insertion.Caret;
        Assert.Equal("        ", result[result.LastIndexOf('\n', caret - 1)..caret].TrimStart('\n'));
        Assert.Equal('\n', result[caret]);
    }

    [Fact]
    public void Cena_numa_linha_so_sem_chaves_nao_recebe_OnClick()
    {
        var root = CSharpSyntaxTree.ParseText(HandlerCode).GetRoot();
        Assert.Null(GameAssist.HandlerInsertion(root, "Expr", "Run", "OnClick", "()", "\n"));
        Assert.Null(GameAssist.HandlerInsertion(root, "Nowhere", "Run", "OnClick", "()", "\n"));
    }

    [Fact]
    public void Acha_as_cenas_do_codigo_e_o_nome_de_cena_sob_o_cursor()
    {
        var root = CSharpSyntaxTree.ParseText(Code).GetRoot();
        var scenes = GameAssist.FindScenes(root);
        Assert.Equal(["Fight", "Village", "Plain"], scenes.Select(s => s.Name));
        Assert.Equal(Code.IndexOf("game.Scene(\"Fight\"", StringComparison.Ordinal), scenes[0].Span.Start);
        Assert.Equal("\"Fight\"", Code.Substring(scenes[0].NameSpan.Start, scenes[0].NameSpan.Length));

        // O cursor dentro do código de uma cena.
        Assert.Equal("Village", GameAssist.SceneAt(root, Code.IndexOf("\"Attack\"", StringComparison.Ordinal))?.Name);
        Assert.Null(GameAssist.SceneAt(root, Code.IndexOf("game.Start", StringComparison.Ordinal)));

        // Nome entre aspas: no game.Scene é a declaração; no game.Start/GoTo é uma ida até a cena.
        var declaration = GameAssist.SceneReferenceAt(root, Code.IndexOf("\"Village\"", StringComparison.Ordinal) + 2)!;
        Assert.Equal(("Village", true), (declaration.Name, declaration.IsDeclaration));
        var start = GameAssist.SceneReferenceAt(root, Code.LastIndexOf("\"Fight\"", StringComparison.Ordinal) + 1)!;
        Assert.Equal(("Fight", false), (start.Name, start.IsDeclaration));
        Assert.Null(GameAssist.SceneReferenceAt(root, Code.IndexOf("\"Atk\"", StringComparison.Ordinal) + 1));   // game.Find
        Assert.Null(GameAssist.SceneReferenceAt(root, Code.IndexOf("\"T\"", StringComparison.Ordinal) + 1));     // new Game("T")
        Assert.Null(GameAssist.SceneReferenceAt(root, Code.IndexOf("game.Scene", StringComparison.Ordinal) + 2));  // fora das aspas
    }
}
