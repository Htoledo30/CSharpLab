using CSharpLab.GameEngine;

namespace CSharpLab.Tests;

/// <summary>game.Save / game.Load: valores e objetos inteiros continuam salvos depois de fechar o jogo.</summary>
public sealed class SaveTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "csharplab-saves", Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    public sealed class Hero
    {
        public string Name { get; set; } = "Herói";
        public int Health { get; set; } = 100;
        public List<string> Bag { get; set; } = [];
    }

    public sealed record Weapon(string Name, int Damage);

    private Game NewGame(string title = "Torre: a volta!") => new(title) { SaveFolder = _dir };

    [Fact]
    public void Salva_e_carrega_valores_e_objetos_em_outro_jogo_aberto_depois()
    {
        var game = NewGame();
        Assert.False(game.HasSave);
        Assert.Equal(50, game.Load("gold", 50));   // nada salvo: o valor do jogo novo

        game.Save("gold", 120);
        game.Save("name", "Henrique");
        game.Save("hasKey", true);
        game.Save("hero", new Hero { Name = "Aria", Health = 72, Bag = ["poção", "chave"] });
        game.Save("weapon", new Weapon("Espada", 7));
        Assert.True(game.HasSave);

        // Como se o jogo fechasse e abrisse de novo.
        var again = NewGame();
        Assert.Equal(120, again.Load("gold", 50));
        Assert.Equal("Henrique", again.Load("name", ""));
        Assert.True(again.Load("hasKey", false));
        var hero = again.Load("hero", new Hero());
        Assert.Equal(("Aria", 72), (hero.Name, hero.Health));
        Assert.Equal(["poção", "chave"], hero.Bag);
        Assert.Equal(new Weapon("Espada", 7), again.Load("weapon", new Weapon("Graveto", 1)));

        // O nome do jogo vira o nome do arquivo (sem os caracteres que o Windows não aceita).
        Assert.Equal("Torre_ a volta!.json", Path.GetFileName(again.SavePath));

        again.DeleteSave();
        Assert.False(NewGame().HasSave);
        Assert.Equal(50, NewGame().Load("gold", 50));
    }

    [Fact]
    public void Tipo_errado_e_nome_vazio_explicam_o_que_fazer()
    {
        var game = NewGame();
        game.Save("gold", "muito");
        var error = Assert.Throws<GameException>(() => game.Load("gold", 0));
        Assert.Contains("não é um Int32", error.Message);
        Assert.Contains("DeleteSave", error.Message);
        Assert.Contains("precisa de um nome", Assert.Throws<GameException>(() => game.Save("", 1)).Message);

        // Arquivo estragado: começa do zero em vez de travar o jogo.
        File.WriteAllText(game.SavePath, "{ isso não é json");
        Assert.Equal(5, game.Load("gold", 5));
    }
}
