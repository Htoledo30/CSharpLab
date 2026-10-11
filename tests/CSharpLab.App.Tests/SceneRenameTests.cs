using CSharpLab.Core.Files;
using CSharpLab.Core.Projects;
using CSharpLab.Core.Settings;
using CSharpLab.Editor;
using CSharpLab.GameEngine;
using CSharpLab.Screens;
using CSharpLab.ViewModels;
using CSharpLab.Views;

namespace CSharpLab.App.Tests;

public sealed class SceneRenameTests
{
    private const string Code = "var game = new Game(\"T\");\n" +
        "game.Scene(\"Fight\", () => { game.Find(\"Continue\").OnClick(() => game.GoTo(\"Shop\")); game.Write(\"Fight\"); });\n" +
        "game.Scene(\"Shop\", () => game.GoTo(\"Fight\"));\n" +
        "game.Start(\"Fight\");\n";

    private static async Task<(MainViewModel Vm, string Dir, DocumentViewModel Code, DocumentViewModel Screen)> Create(string? code = null)
    {
        var vm = new MainViewModel(new AppSettings()) { Dialogs = new FakeDialogs(), Terminal = new FakeTerminal() };
        await vm.InitializeAsync();
        var project = ProjectCreator.CreateGameProject(Ui.NewFolder("rename-scene"), "Torre", code ?? Code);
        Directory.CreateDirectory(Path.Combine(project.Directory, "Screens"));
        File.WriteAllText(GameScreens.PathOf(project.Directory, "Fight"), ScreenFile.Serialize(ScreenLayout.CreateDefault("Fight")));
        await vm.OpenFolderAsync(project.Directory, project.ProjectPath, promptForUnsaved: false);
        return (vm, project.Directory, vm.OpenFile(project.ProgramPath)!, vm.OpenFile(GameScreens.PathOf(project.Directory, "Fight"))!);
    }

    [Fact]
    public void Cena_tela_destinos_e_inicio_voltam_juntos_de_qualquer_arquivo() => Ui.Run(async () =>
    {
        var (vm, dir, code, screen) = await Create();
        using (vm)
        {
            var otherPath = Path.Combine(dir, "More.cs");
            File.WriteAllText(otherPath, "using CSharpLab.GameEngine; class More { public void Play(Game g) { g.GoTo(\"Fight\"); } }");
            var studio = new StudioView();
            studio.Bind(vm);
            vm.OpenStudio("Fight");
            studio.Open("Fight");
            var screenText = screen.Document.Text;
            Assert.Null(vm.RenameGameScene(dir, "Fight", "Battle"));
            var other = vm.FindDocument(otherPath)!;
            Assert.Contains("game.Scene(\"Battle\"", code.Document.Text);
            Assert.Contains("game.GoTo(\"Battle\")", code.Document.Text);
            Assert.Contains("game.Start(\"Battle\")", code.Document.Text);
            Assert.Contains("game.Write(\"Fight\")", code.Document.Text);
            Assert.Contains("g.GoTo(\"Battle\")", other.Document.Text);
            Assert.Equal(screenText, screen.Document.Text);
            Assert.Equal("Battle", studio.CurrentScene);
            Assert.Same(screen, studio.Screen!.Model.Doc);
            Assert.False(File.Exists(GameScreens.PathOf(dir, "Fight")));
            Assert.True(File.Exists(GameScreens.PathOf(dir, "Battle")));
            Assert.False(vm.HasScreen(dir, "Fight"));
            Assert.True(vm.HasScreen(dir, "Battle"));
            Assert.Equal(Code, File.ReadAllText(code.FilePath!));
            Assert.True(code.IsDirty);

            other.Undo();
            Assert.Equal(Code, code.Document.Text);
            Assert.Contains("g.GoTo(\"Fight\")", other.Document.Text);
            Assert.Equal("Fight", studio.CurrentScene);
            Assert.True(File.Exists(GameScreens.PathOf(dir, "Fight")));
            Assert.False(code.IsDirty);
            Assert.False(screen.IsDirty);
            screen.Redo();
            Assert.Contains("game.Start(\"Battle\")", code.Document.Text);
            Assert.Equal("Battle", studio.CurrentScene);
            Assert.Null(vm.RenameGameScene(dir, "Battle", "battle"));
            code.Undo();
            Assert.Equal("Battle.json", screen.Title);
            screen.Undo();
            Assert.Equal(Code, code.Document.Text);
            Assert.Equal("Fight.json", screen.Title);
            vm.CloseStudio();
        }
    });

    [Fact]
    public void Edicao_posterior_nao_e_apagada_pelo_desfazer_de_outra_aba() => Ui.Run(async () =>
    {
        var (vm, dir, code, screen) = await Create();
        using (vm)
        {
            Assert.Null(vm.RenameGameScene(dir, "Fight", "Battle"));
            code.Document.Insert(0, "// minha edição\n");
            var after = code.Document.Text;
            screen.Undo();
            Assert.Equal(after, code.Document.Text);
            Assert.Equal("Battle.json", screen.Title);
            Assert.True(screen.Document.UndoStack.CanUndo);
            Assert.Contains("alterações posteriores", vm.Notice!);
            code.Undo();
            screen.Undo();
            Assert.Equal(Code, code.Document.Text);
            Assert.Equal("Fight.json", screen.Title);
        }
    });

    [Fact]
    public void Colisao_bloqueia_desfazer_sem_consumir_o_historico() => Ui.Run(async () =>
    {
        var (vm, dir, code, screen) = await Create();
        using (vm)
        {
            Assert.Null(vm.RenameGameScene(dir, "Fight", "Battle"));
            var oldPath = GameScreens.PathOf(dir, "Fight");
            File.WriteAllText(oldPath, "meu outro arquivo");
            screen.Undo();
            Assert.Equal("Battle.json", screen.Title);
            Assert.Contains("game.Start(\"Battle\")", code.Document.Text);
            Assert.Equal("meu outro arquivo", File.ReadAllText(oldPath));
            Assert.True(screen.Document.UndoStack.CanUndo);
            File.Delete(oldPath);
            screen.Undo();
            Assert.Equal(Code, code.Document.Text);
        }
    });

    [Fact]
    public void Arquivo_travado_impede_renomeacao_inteira() => Ui.Run(async () =>
    {
        var (vm, dir, code, screen) = await Create();
        using (vm)
        {
            using (var locked = new FileStream(screen.FilePath!, FileMode.Open, FileAccess.Read, FileShare.Read))
                Assert.NotNull(vm.RenameGameScene(dir, "Fight", "Battle"));
            Assert.Equal(Code, code.Document.Text);
            Assert.Equal("Fight.json", screen.Title);
            Assert.False(code.IsDirty);
            Assert.False(File.Exists(GameScreens.PathOf(dir, "Battle")));
            Assert.Null(vm.RenameGameScene(dir, "Fight", "Battle"));
            using (var locked = new FileStream(screen.FilePath!, FileMode.Open, FileAccess.Read, FileShare.Read))
                screen.Undo();
            Assert.Equal("Battle.json", screen.Title);
            screen.Undo();
            Assert.Equal(Code, code.Document.Text);
        }
    });

    [Fact]
    public void Nomes_invalidos_cena_existente_e_destinos_indiretos_nao_mudam_nada() => Ui.Run(async () =>
    {
        var (vm, dir, code, screen) = await Create(Code + "var next = \"Fight\"; game.GoTo(next);\n");
        using (vm)
        {
            var before = code.Document.Text;
            Assert.NotNull(vm.RenameGameScene(dir, "Fight", "../Escape"));
            Assert.NotNull(vm.RenameGameScene(dir, "Fight", "shop"));
            Assert.Contains("variáveis", vm.RenameGameScene(dir, "Fight", "Battle"));
            Assert.Equal(before, code.Document.Text);
            Assert.Equal("Fight.json", screen.Title);
            Assert.False(code.IsDirty);
        }
    });

    [Fact]
    public void Peca_tela_e_codigo_desfazem_juntos_preservando_edicoes_anteriores() => Ui.Run(async () =>
    {
        var (vm, _, code, screen) = await Create();
        using (vm)
        {
            var view = new ScreenEditorView(screen, vm);
            try
            {
                code.Document.Insert(0, "// antes\n");
                view.Model.Select("Continue");
                Assert.Null(view.Model.Rename("Attack"));
                Assert.Contains("game.Find(\"Attack\")", code.Document.Text);
                // Também funciona pelo histórico nativo quando os participantes estão na mesma posição.
                screen.Document.UndoStack.Undo();
                Assert.NotNull(view.Model.Layout!.Find("Continue"));
                Assert.Contains("game.Find(\"Continue\")", code.Document.Text);
                Assert.StartsWith("// antes", code.Document.Text);
                code.Redo();
                Assert.NotNull(view.Model.Layout!.Find("Attack"));
                view.Model.Undo();
                code.Undo();
                Assert.Equal(Code, code.Document.Text);
                Assert.False(code.IsDirty);
            }
            finally { view.Detach(); }
        }
    });

    [Fact]
    public void Salvar_tudo_e_desfazer_mantem_estado_de_cada_arquivo() => Ui.Run(async () =>
    {
        var (vm, dir, code, screen) = await Create();
        using (vm)
        {
            Assert.Null(vm.RenameGameScene(dir, "Fight", "Battle"));
            Assert.True(vm.Save(code));
            Assert.True(vm.Save(screen));
            Assert.False(code.IsDirty);
            Assert.False(screen.IsDirty);
            screen.Undo();
            Assert.True(code.IsDirty);
            Assert.True(screen.IsDirty);
            code.Redo();
            Assert.False(code.IsDirty);
            Assert.False(screen.IsDirty);
        }
    });

    [Fact]
    public void Descartar_ao_trocar_de_pasta_devolve_os_nomes_ao_codigo_salvo() => Ui.Run(async () =>
    {
        var (vm, dir, code, screen) = await Create();
        using (vm)
        {
            Assert.Null(vm.RenameGameScene(dir, "Fight", "Battle"));
            Assert.Null(vm.RenameGameScene(dir, "Battle", "Arena"));
            Assert.True(await vm.OpenFolderAsync(Ui.NewFolder("outro"), null, promptForUnsaved: true));
            Assert.True(File.Exists(GameScreens.PathOf(dir, "Fight")));
            Assert.False(File.Exists(GameScreens.PathOf(dir, "Battle")));
            Assert.False(File.Exists(GameScreens.PathOf(dir, "Arena")));
            Assert.Equal(Code, File.ReadAllText(code.FilePath!));
        }
    });

    [Fact]
    public void Codigo_salvo_define_o_nome_ao_descartar_e_desfazer_salvo_tambem_pode_ser_descartado() => Ui.Run(async () =>
    {
        var (vm, dir, code, screen) = await Create();
        using (vm)
        {
            Assert.Null(vm.RenameGameScene(dir, "Fight", "Battle"));
            Assert.True(vm.Save(code)); // a tela não mudou de conteúdo: só de caminho
            Assert.True(vm.ConfirmCloseAll(vm.Documents));
            Assert.True(File.Exists(GameScreens.PathOf(dir, "Battle")));
            Assert.True(vm.Save(screen));
            code.Undo();
            Assert.True(vm.ConfirmCloseAll(vm.Documents));
            Assert.True(File.Exists(GameScreens.PathOf(dir, "Battle")));
            Assert.False(File.Exists(GameScreens.PathOf(dir, "Fight")));
        }
    });

    [Fact]
    public void Falha_ao_descartar_duas_renomeacoes_devolve_a_primeira_ao_estado_aberto() => Ui.Run(async () =>
    {
        var (vm, dir, code, screen) = await Create();
        using (vm)
        {
            Assert.Null(vm.RenameGameScene(dir, "Fight", "Battle"));
            Assert.Null(vm.RenameGameScene(dir, "Battle", "Arena"));
            File.WriteAllText(GameScreens.PathOf(dir, "Fight"), "outro arquivo");
            Assert.False(vm.ConfirmCloseAll(vm.Documents));
            Assert.Equal("Arena.json", screen.Title);
            Assert.True(File.Exists(GameScreens.PathOf(dir, "Arena")));
            Assert.Contains("game.Start(\"Arena\")", code.Document.Text);
            Assert.Equal("outro arquivo", File.ReadAllText(GameScreens.PathOf(dir, "Fight")));
        }
    });

    [Fact]
    public void Nova_edicao_apos_desfazer_invalida_refazer_em_todas_as_abas() => Ui.Run(async () =>
    {
        var (vm, dir, code, screen) = await Create();
        using (vm)
        {
            Assert.Null(vm.RenameGameScene(dir, "Fight", "Battle"));
            screen.Undo();
            code.Document.Insert(0, "// outro caminho\n");
            code.Undo();
            screen.Redo();
            Assert.Equal("Fight.json", screen.Title);
            Assert.Equal(Code, code.Document.Text);
            Assert.Contains("nova edição", vm.Notice!);
            code.Redo();
            Assert.StartsWith("// outro caminho", code.Document.Text);
        }
    });

    [Fact]
    public void Tela_alterada_fora_do_editor_nao_e_movida_sem_aviso() => Ui.Run(async () =>
    {
        var (vm, dir, code, screen) = await Create();
        using (vm)
        {
            File.WriteAllText(screen.FilePath!, screen.Document.Text + "\n\n");
            Assert.Contains("fora do editor", vm.RenameGameScene(dir, "Fight", "Battle"));
            Assert.Equal("Fight.json", screen.Title);
            Assert.Equal(Code, code.Document.Text);
        }
    });

    [Fact]
    public void Explorer_renomeia_cena_com_codigo_e_nao_aceita_trocar_extensao() => Ui.Run(async () =>
    {
        var (vm, dir, code, screen) = await Create();
        using (vm)
        {
            var folder = vm.Explorer.Root!.Children.First(n => n.Name == "Screens");
            folder.IsExpanded = true;
            var node = folder.Children.First(n => n.Name == "Fight.json");
            node.IsEditing = true;
            node.EditName = "Battle.txt";
            Assert.False(vm.Explorer.CommitEdit(node));
            Assert.Contains(".json", node.EditError!);
            node.EditName = "Battle.json";
            Assert.True(vm.Explorer.CommitEdit(node));
            Assert.Equal("Battle.json", screen.Title);
            Assert.Contains("game.Start(\"Battle\")", code.Document.Text);
            screen.Undo();
            Assert.Equal(Code, code.Document.Text);
        }
    });

    [Fact]
    public void Cenas_so_de_codigo_e_telas_sem_codigo_tambem_podem_ser_renomeadas() => Ui.Run(async () =>
    {
        var (vm, dir, code, _) = await Create();
        using (vm)
        {
            Assert.Null(vm.RenameGameScene(dir, "Shop", "Market"));
            Assert.Contains("game.Scene(\"Market\"", code.Document.Text);
            Assert.False(File.Exists(GameScreens.PathOf(dir, "Market")));
            code.Undo();
            Assert.Equal(Code, code.Document.Text);
            var cavePath = GameScreens.PathOf(dir, "Cave");
            File.WriteAllText(cavePath, "{ broken JSON"); // o nome é independente do conteúdo
            Assert.Null(vm.RenameGameScene(dir, "Cave", "Dungeon"));
            var cave = vm.FindDocument(GameScreens.PathOf(dir, "Dungeon"))!;
            cave.Undo();
            Assert.Equal("{ broken JSON", File.ReadAllText(cavePath));
        }
    });

    [Fact]
    public void F2_reconhece_a_cena_do_motor_sem_confundir_metodos_de_outras_classes() => Ui.Run(async () =>
    {
        var extra = "var other = new Other(); other.GoTo(\"Fight\"); class Other { public void GoTo(string name) {} }\n";
        var (vm, _, code, screen) = await Create(Code + extra);
        using (vm)
        {
            ((FakeDialogs)vm.Dialogs).TextAnswer = "Battle";
            await Ui.WaitUntil(() => vm.Language?.GetDocument(code.LanguageKey) != null);
            var editor = new CodeEditor(code, vm);
            try
            {
                editor.CaretOffset = code.Document.Text.IndexOf("other.GoTo(\"Fight\")", StringComparison.Ordinal) + "other.GoTo(\"".Length;
                await editor.RenameSymbolAsync();
                Assert.Equal("Fight.json", screen.Title);
                editor.CaretOffset = code.Document.Text.IndexOf("game.Start(\"Fight\")", StringComparison.Ordinal) + "game.Start(\"".Length;
                await editor.RenameSymbolAsync();
                Assert.Equal("Battle.json", screen.Title);
                Assert.Contains("other.GoTo(\"Fight\")", code.Document.Text);
                editor.Undo();
                Assert.Equal(Code + extra, code.Document.Text);
            }
            finally { editor.Detach(); }
        }
    });

    [Fact]
    public void Codigo_somente_leitura_impede_mudar_o_nome_da_tela() => Ui.Run(async () =>
    {
        var (vm, dir, code, screen) = await Create();
        using (vm)
        {
            File.SetAttributes(code.FilePath!, FileAttributes.ReadOnly);
            try
            {
                Assert.Contains("somente para leitura", vm.RenameGameScene(dir, "Fight", "Battle"));
                Assert.Equal(Code, code.Document.Text);
                Assert.Equal("Fight.json", screen.Title);
            }
            finally { File.SetAttributes(code.FilePath!, FileAttributes.Normal); }
        }
    });

    [Fact]
    public void Descartar_varios_desfazeres_e_uma_nova_renomeacao_volta_ao_ultimo_nome_salvo() => Ui.Run(async () =>
    {
        var (vm, dir, code, screen) = await Create();
        using (vm)
        {
            Assert.Null(vm.RenameGameScene(dir, "Fight", "Battle"));
            Assert.Null(vm.RenameGameScene(dir, "Battle", "Arena"));
            Assert.True(vm.Save(code));
            Assert.True(vm.Save(screen));
            screen.Undo();
            screen.Undo();
            Assert.Null(vm.RenameGameScene(dir, "Fight", "Dungeon"));
            Assert.True(await vm.OpenFolderAsync(Ui.NewFolder("outro"), null, promptForUnsaved: true));
            Assert.True(File.Exists(GameScreens.PathOf(dir, "Arena")));
            Assert.False(File.Exists(GameScreens.PathOf(dir, "Fight")));
            Assert.False(File.Exists(GameScreens.PathOf(dir, "Battle")));
            Assert.False(File.Exists(GameScreens.PathOf(dir, "Dungeon")));
            Assert.Contains("game.Start(\"Arena\")", File.ReadAllText(code.FilePath!));
        }
    });

    [Fact]
    public void Salvar_so_um_codigo_da_cena_impede_descartar_os_demais() => Ui.Run(async () =>
    {
        var (vm, dir, code, screen) = await Create();
        using (vm)
        {
            var extraPath = Path.Combine(dir, "More.cs");
            File.WriteAllText(extraPath, "class More { public void Go(Game g) { g.GoTo(\"Fight\"); } }");
            Assert.Null(vm.RenameGameScene(dir, "Fight", "Battle"));
            Assert.True(vm.Save(code));
            Assert.False(vm.ConfirmCloseAll(vm.Documents));
            Assert.Contains("Parte de uma renomeação", vm.Notice!);
            Assert.Equal("Battle.json", screen.Title);
            Assert.Contains("g.GoTo(\"Battle\")", vm.FindDocument(extraPath)!.Document.Text);
            Assert.True(vm.Save(vm.FindDocument(extraPath)!));
            Assert.True(vm.ConfirmCloseAll(vm.Documents));
            Assert.True(File.Exists(GameScreens.PathOf(dir, "Battle")));
        }
    });

    [Fact]
    public void Peca_parcialmente_salva_pode_voltar_ao_nome_antigo_salvando_o_desfazer() => Ui.Run(async () =>
    {
        var (vm, _, code, screen) = await Create();
        using (vm)
        {
            var view = new ScreenEditorView(screen, vm);
            try
            {
                view.Model.Select("Continue");
                Assert.Null(view.Model.Rename("Attack"));
                Assert.True(vm.Save(screen));
                Assert.False(vm.ConfirmCloseAll(vm.Documents));
                view.Model.Undo();
                Assert.True(vm.Save(screen));
                Assert.True(vm.ConfirmCloseAll(vm.Documents));
                Assert.Equal(Code, code.Document.Text);
                Assert.NotNull(view.Model.Layout!.Find("Continue"));
            }
            finally { view.Detach(); }
        }
    });
}
