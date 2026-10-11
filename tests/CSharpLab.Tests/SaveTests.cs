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

    private Game NewGame(string title = "Torre: a volta!", string id = "torre-de-teste") => new(title, id) { SaveFolder = _dir };

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

        // O título pode mudar: é o identificador permanente que separa os saves.
        Assert.Equal("save.json", Path.GetFileName(again.SavePath));
        Assert.Equal("torre-de-teste", Path.GetFileName(Path.GetDirectoryName(again.SavePath)));

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

        // Arquivo estragado: explica o erro, sem fingir que não havia progresso.
        File.WriteAllText(game.SavePath, "{ isso não é json");
        Assert.Contains("preservado", Assert.Throws<GameException>(() => game.Load("gold", 5)).Message);
    }

    [Fact]
    public void Mesmo_titulo_em_jogos_diferentes_nao_mistura_saves_e_mudar_titulo_mantem_progresso()
    {
        NewGame("A Torre", "jogo-um").Save("gold", 90);
        NewGame("A Torre", "jogo-dois").Save("gold", 12);
        Assert.Equal(90, NewGame("A Torre — edição nova", "jogo-um").Load("gold", 0));
        Assert.Equal(12, NewGame("A Torre", "jogo-dois").Load("gold", 0));
    }

    [Theory]
    [InlineData("{ quebrado")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("{\"gold\": 10, \"gold\": 20}")]
    [InlineData("")]
    public void Save_invalido_bloqueia_load_has_save_e_gravacao_sem_alterar_arquivos(string invalid)
    {
        var game = NewGame();
        game.Save("gold", 10);
        game.Save("gold", 20);
        var backup = BackupPath(game);
        var backupBytes = File.ReadAllBytes(backup);
        File.WriteAllText(game.SavePath, invalid);
        var original = File.ReadAllBytes(game.SavePath);
        Assert.Throws<GameException>(() => game.Load("gold", 0));
        Assert.Throws<GameException>(() => game.HasSave);
        Assert.Throws<GameException>(() => game.Save("gold", 99));
        Assert.Equal(original, File.ReadAllBytes(game.SavePath));
        Assert.Equal(backupBytes, File.ReadAllBytes(backup));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(game.SavePath)!, "*.tmp"));
    }

    [Fact]
    public void Restauracao_e_explicita_preserva_danificado_e_nao_troca_backup_bom()
    {
        var game = NewGame();
        game.Save("gold", 10);
        game.Save("gold", 20);
        File.WriteAllText(game.SavePath, "{ danificado");
        Assert.Throws<GameException>(() => game.Load("gold", 0));
        Assert.Equal("{ danificado", File.ReadAllText(game.SavePath));

        Assert.True(game.RestoreSave());
        Assert.Equal(10, NewGame().Load("gold", 0));
        Assert.Contains(HistoryFiles(game), file => File.ReadAllText(file) == "{ danificado");
        Assert.Equal(10, System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(BackupPath(game)))!["gold"]!.GetValue<int>());
    }

    [Fact]
    public void Sem_copia_nao_restaura_e_copia_danificada_nao_substitui_o_save_atual()
    {
        var game = NewGame();
        Assert.False(game.RestoreSave());
        game.Save("gold", 35);
        Assert.False(game.RestoreSave());
        File.WriteAllText(BackupPath(game), "[copia ruim");
        var current = File.ReadAllBytes(game.SavePath);
        Assert.Throws<GameException>(() => game.RestoreSave());
        Assert.Equal(current, File.ReadAllBytes(game.SavePath));
        Assert.Equal("[copia ruim", File.ReadAllText(BackupPath(game)));
    }

    [Fact]
    public void Save_ausente_com_backup_nao_e_considerado_jogo_novo()
    {
        var game = NewGame();
        game.Save("gold", 10);
        game.Save("gold", 20);
        File.Delete(game.SavePath);
        Assert.Contains("RestoreSave", Assert.Throws<GameException>(() => game.Load("gold", 0)).Message);
        Assert.Throws<GameException>(() => game.Save("gold", 99));
        Assert.True(game.RestoreSave());
        Assert.Equal(10, game.Load("gold", 0));
    }

    [Fact]
    public void Comecar_de_novo_arquiva_progresso_e_pode_ser_desfeito_pela_copia_anterior()
    {
        var game = NewGame();
        game.Save("gold", 10);
        game.Save("gold", 20);
        game.DeleteSave();
        Assert.False(game.HasSave);
        Assert.Equal(50, NewGame().Load("gold", 50));
        Assert.Contains(HistoryFiles(game), file => File.ReadAllText(file).Contains("20"));
        Assert.Contains(HistoryFiles(game), file => File.ReadAllText(file).Contains("10"));
        game.DeleteSave(); // Repetir não pode trocar a cópia do progresso por um save vazio.
        Assert.True(game.RestoreSave());
        Assert.Equal(20, NewGame().Load("gold", 0));
    }

    [Fact]
    public void Comecar_de_novo_com_arquivos_danificados_preserva_os_dois_originais()
    {
        var game = NewGame();
        game.Save("gold", 10);
        File.WriteAllText(game.SavePath, "arquivo quebrado");
        File.WriteAllText(BackupPath(game), "copia quebrada");
        game.DeleteSave();
        Assert.False(game.HasSave);
        Assert.Contains(HistoryFiles(game), file => File.ReadAllText(file) == "arquivo quebrado");
        Assert.Contains(HistoryFiles(game), file => File.ReadAllText(file) == "copia quebrada");
    }

    [Fact]
    public void Save_antigo_precisa_de_importacao_explicita_e_o_original_nao_muda()
    {
        Directory.CreateDirectory(_dir);
        var legacy = Path.Combine(_dir, "Torre_ a volta!.json");
        File.WriteAllText(legacy, "{\"gold\":70}");
        var game = NewGame();
        Assert.Contains("ImportLegacySave", Assert.Throws<GameException>(() => game.Load("gold", 0)).Message);
        Assert.Throws<GameException>(() => game.Save("gold", 0));
        Assert.True(game.ImportLegacySave());
        Assert.Equal(70, NewGame().Load("gold", 0));
        Assert.Equal("{\"gold\":70}", File.ReadAllText(legacy));
        Assert.False(game.ImportLegacySave());
        Assert.Contains("ImportLegacySave", Assert.Throws<GameException>(() => NewGame(id: "outro-jogo").Load("gold", 0)).Message);
        game.DeleteSave();
        Assert.False(NewGame().HasSave);
        Assert.Equal(0, NewGame().Load("gold", 0));
    }

    [Fact]
    public void Titulo_antigo_pode_ser_informado_e_legado_invalido_nao_e_importado()
    {
        Directory.CreateDirectory(_dir);
        var legacy = Path.Combine(_dir, "Antigo.json");
        File.WriteAllText(legacy, "{\"gold\":80}");
        var game = NewGame("Título novo");
        Assert.True(game.ImportLegacySave("Antigo"));
        Assert.Equal(80, game.Load("gold", 0));
        File.WriteAllText(legacy, "{ arquivo quebrado");
        var other = NewGame("Outro", "outra-identidade");
        Assert.Throws<GameException>(() => other.ImportLegacySave("Antigo"));
        Assert.False(File.Exists(other.SavePath));
        Assert.Equal("{ arquivo quebrado", File.ReadAllText(legacy));
    }

    [Theory]
    [InlineData("current")]
    [InlineData("backup")]
    public void Arquivo_aberto_impede_gravacao_sem_perder_progresso(string target)
    {
        var game = NewGame();
        game.Save("gold", 10);
        game.Save("gold", 20);
        var current = File.ReadAllBytes(game.SavePath);
        var backup = File.ReadAllBytes(BackupPath(game));
        using (var locked = new FileStream(target == "current" ? game.SavePath : BackupPath(game), FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.Throws<GameException>(() => game.Save("gold", 99));
        Assert.Equal(current, File.ReadAllBytes(game.SavePath));
        Assert.Equal(backup, File.ReadAllBytes(BackupPath(game)));
        Assert.Equal(20, game.Load("gold", 0));
    }

    [Fact]
    public void Save_somente_leitura_nao_e_substituido_e_nao_rotaciona_backup()
    {
        var game = NewGame();
        game.Save("gold", 10);
        game.Save("gold", 20);
        var backup = File.ReadAllBytes(BackupPath(game));
        File.SetAttributes(game.SavePath, FileAttributes.ReadOnly);
        try
        {
            Assert.Throws<GameException>(() => game.Save("gold", 99));
            Assert.Throws<GameException>(() => game.DeleteSave());
            Assert.Equal(20, game.Load("gold", 0));
            Assert.Equal(backup, File.ReadAllBytes(BackupPath(game)));
        }
        finally { File.SetAttributes(game.SavePath, FileAttributes.Normal); }
    }

    [Fact]
    public void Trava_de_outra_instancia_nao_permite_perder_atualizacoes()
    {
        var game = NewGame();
        game.Save("gold", 10);
        using (var locked = new FileStream(Path.Combine(Path.GetDirectoryName(game.SavePath)!, "save.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Contains("Outra janela", Assert.Throws<GameException>(() => NewGame().Save("health", 100)).Message);
            Assert.Throws<GameException>(() => NewGame().DeleteSave());
        }
        NewGame().Save("health", 100);
        Assert.Equal(10, game.Load("gold", 0));
        Assert.Equal(100, game.Load("health", 0));
    }

    [Fact]
    public void Ciclo_no_objeto_explicado_e_nao_modifica_o_save()
    {
        var game = NewGame();
        game.Save("gold", 10);
        var value = new Circular();
        value.Self = value;
        Assert.Contains("referências circulares", Assert.Throws<GameException>(() => game.Save("hero", value)).Message);
        Assert.Equal(10, game.Load("gold", 0));
    }

    [Fact]
    public void Utf8_invalido_nao_e_convertido_silenciosamente_e_load_sem_save_nao_cria_pastas()
    {
        var game = NewGame();
        Assert.Equal(5, game.Load("gold", 5));
        Assert.False(Directory.Exists(_dir));
        game.Save("gold", 10);
        byte[] invalid = [0x7B, 0x22, 0x6E, 0x22, 0x3A, 0x22, 0xFF, 0x22, 0x7D];
        File.WriteAllBytes(game.SavePath, invalid);
        Assert.Throws<GameException>(() => game.Load("gold", 0));
        Assert.Throws<GameException>(() => game.Save("gold", 30));
        Assert.Equal(invalid, File.ReadAllBytes(game.SavePath));
    }

    [Fact]
    public void Salvar_o_mesmo_valor_nao_regrava_nem_descarta_a_copia_anterior()
    {
        var game = NewGame();
        game.Save("gold", 10);
        game.Save("gold", 20);
        var main = File.ReadAllBytes(game.SavePath);
        var backup = File.ReadAllBytes(BackupPath(game));
        var stamp = File.GetLastWriteTimeUtc(game.SavePath);
        using (var locked = new FileStream(BackupPath(game), FileMode.Open, FileAccess.Read, FileShare.None))
            game.Save("gold", 20);
        Assert.Equal(main, File.ReadAllBytes(game.SavePath));
        Assert.Equal(backup, File.ReadAllBytes(BackupPath(game)));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(game.SavePath));
    }

    [Fact]
    public void Backup_danificado_e_arquivado_antes_de_receber_uma_copia_valida()
    {
        var game = NewGame();
        game.Save("gold", 10);
        File.WriteAllText(BackupPath(game), "copia invalida");
        game.Save("gold", 20);
        Assert.Contains(HistoryFiles(game), file => File.ReadAllText(file) == "copia invalida");
        Assert.True(game.RestoreSave());
        Assert.Equal(10, game.Load("gold", 0));
    }

    public sealed class Circular { public Circular? Self { get; set; } }

    private static string BackupPath(Game game) => Path.Combine(Path.GetDirectoryName(game.SavePath)!, "save.anterior.json");
    private static string[] HistoryFiles(Game game) => Directory.GetFiles(Path.Combine(Path.GetDirectoryName(game.SavePath)!, "Historico"), "*.json", SearchOption.AllDirectories);
}
