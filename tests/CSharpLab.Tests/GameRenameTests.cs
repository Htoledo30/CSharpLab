using CSharpLab.Core.Language;
using CSharpLab.GameEngine;
using Microsoft.CodeAnalysis.Text;

namespace CSharpLab.Tests;

public sealed class GameRenameTests
{
    [Fact]
    public void So_metodos_do_motor_mudam_incluindo_aspas_especiais_e_argumentos_nomeados()
    {
        var code = """"
            var game = new Game("T");
            game.Scene(name: @"Fight", build: () => game.Write("Fight"));
            game.GoTo("""Fight""");
            game.Test("Fight", () => { });
            game.Start("F\u0069ght");
            var other = new Other(); other.GoTo("Fight"); other.Start("Fight");
            // game.GoTo("Fight");
            class Other { public void GoTo(string n) {} public void Start(string n) {} }
            """";
        var plan = GameRename.Scene(new Dictionary<string, string> { ["Program.cs"] = code }, "Fight", "Battle", typeof(Game).Assembly.Location);
        var changes = plan.Changes["Program.cs"];
        Assert.Equal(4, changes.Count);
        var renamed = SourceText.From(code).WithChanges(changes).ToString();
        Assert.Contains("game.Write(\"Fight\")", renamed);
        Assert.Contains("other.GoTo(\"Fight\")", renamed);
        Assert.Contains("// game.GoTo(\"Fight\")", renamed);
        Assert.Contains("game.Start(\"Battle\")", renamed);
        Assert.Contains("game.Test(\"Battle\"", renamed);
        Assert.Empty(plan.IndirectFiles);
    }

    [Fact]
    public void Variaveis_e_constantes_que_apontam_para_a_cena_precisam_de_revisao()
    {
        var code = "var game = new Game(\"T\"); const string target = \"Fight\"; game.GoTo(target);";
        var plan = GameRename.Scene(new Dictionary<string, string> { ["Program.cs"] = code }, "Fight", "Battle", typeof(Game).Assembly.Location);
        Assert.Equal(["Program.cs"], plan.IndirectFiles);
        Assert.Empty(plan.Changes);
    }

    [Fact]
    public void Peca_so_muda_no_motor_e_na_cena_certa_sem_trocar_textos()
    {
        var code = """"
            var game = new Game("T"); var other = new Other();
            game.Scene("Fight", () => { game.Find(@"Continue"); game.Find("""Continue"""); other.Find("Continue"); game.Write("Continue"); });
            game.Scene("Shop", () => game.Find("Continue"));
            class Other { public void Find(string n) {} }
            """";
        var plan = GameRename.Piece(new Dictionary<string, string> { ["Program.cs"] = code }, "Fight", "Continue", "Attack", typeof(Game).Assembly.Location);
        Assert.Equal(2, plan.Changes["Program.cs"].Count);
        var renamed = SourceText.From(code).WithChanges(plan.Changes["Program.cs"]).ToString();
        Assert.Contains("other.Find(\"Continue\")", renamed);
        Assert.Contains("game.Write(\"Continue\")", renamed);
        Assert.Contains("game.Scene(\"Shop\", () => game.Find(\"Continue\"))", renamed);
    }

    [Fact]
    public void Chamada_de_cena_com_erro_precisa_ser_corrigida_antes_de_renomear()
    {
        var code = "var game = new Game(\"T\"); game.Scene(\"Fight\", render: () => game.Write(\"T\")); game.Start(\"Fight\");";
        var plan = GameRename.Scene(new Dictionary<string, string> { ["Program.cs"] = code }, "Fight", "Battle", typeof(Game).Assembly.Location);
        Assert.Equal(["Program.cs"], plan.UnresolvedFiles);
    }

    [Fact]
    public void Find_em_metodo_reutilizado_precisa_de_revisao_para_identificar_a_tela()
    {
        var code = "var game = new Game(\"T\"); game.Scene(\"Fight\", Build); void Build() { game.Find(\"Continue\"); }";
        var plan = GameRename.Piece(new Dictionary<string, string> { ["Program.cs"] = code }, "Fight", "Continue", "Attack", typeof(Game).Assembly.Location);
        Assert.Equal(["Program.cs"], plan.IndirectFiles);
        Assert.Empty(plan.Changes);
    }
}
