using System.Text;
using CSharpLab.Core.Files;
using CSharpLab.Core.Projects;
using CSharpLab.Core.Settings;
using CSharpLab.ViewModels;

namespace CSharpLab.App.Tests;

public sealed class MainViewModelTests
{
    private static (MainViewModel Vm, FakeDialogs Dialogs, FakeTerminal Terminal) Create(AppSettings? settings = null)
    {
        var dialogs = new FakeDialogs();
        var terminal = new FakeTerminal();
        var vm = new MainViewModel(settings ?? new AppSettings()) { Dialogs = dialogs, Terminal = terminal };
        return (vm, dialogs, terminal);
    }

    [Fact]
    public void Painel_inferior_volta_aberto_se_estava_aberto() => Ui.Run(() =>
    {
        var (vm, _, _) = Create(new AppSettings { PanelOpen = true, PanelHeight = 240 });
        Assert.True(vm.IsPanelOpen);
        Assert.Equal(240, vm.PanelHeight);
        vm.Dispose();
        return Task.CompletedTask;
    });

    [Fact]
    public void Excluir_arquivo_com_alteracoes_avisa_que_serao_perdidas() => Ui.Run(() =>
    {
        var (vm, dialogs, _) = Create();
        var dir = Ui.NewFolder();
        var path = Path.Combine(dir, "Heroi.cs");
        File.WriteAllText(path, "class Heroi { }");
        var doc = vm.OpenFile(path)!;
        doc.Document.Insert(0, "// mudança não salva\n");

        dialogs.ConfirmAnswer = (_, _) => false;
        Assert.False(vm.ConfirmDelete(path, isDirectory: false));
        var (title, message) = Assert.Single(dialogs.Confirms);
        Assert.Contains("não salvas", title + message);
        Assert.Contains("Heroi.cs", message);
        Assert.True(File.Exists(path));
        Assert.Contains(doc, vm.Documents);

        // Excluir a pasta que contém o arquivo também avisa.
        dialogs.Confirms.Clear();
        vm.ConfirmDelete(dir, isDirectory: true);
        Assert.Contains("não salvas", Assert.Single(dialogs.Confirms).Message);
        vm.Dispose();
        return Task.CompletedTask;
    });

    [Fact]
    public void Excluir_arquivo_sem_alteracoes_so_confirma() => Ui.Run(() =>
    {
        var (vm, dialogs, _) = Create();
        var path = Path.Combine(Ui.NewFolder(), "Limpo.cs");
        File.WriteAllText(path, "class Limpo { }");
        vm.OpenFile(path);
        vm.ConfirmDelete(path, isDirectory: false);
        Assert.DoesNotContain("não salvas", Assert.Single(dialogs.Confirms).Message);
        vm.Dispose();
        return Task.CompletedTask;
    });

    [Fact]
    public void Salvar_detecta_alteracao_externa_ainda_nao_percebida() => Ui.Run(() =>
    {
        var (vm, dialogs, _) = Create();
        var path = Path.Combine(Ui.NewFolder(), "Program.cs");
        File.WriteAllText(path, "Console.WriteLine(1);");
        var doc = vm.OpenFile(path)!;
        doc.Document.Insert(0, "// editor\n");

        // Outro programa grava o arquivo (fora de qualquer pasta monitorada).
        File.WriteAllText(path, "Console.WriteLine(\"externo\");");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(5));

        dialogs.ConfirmAnswer = (_, _) => false;
        Assert.False(vm.Save(doc));
        Assert.Contains("alterado", Assert.Single(dialogs.Confirms).Title);
        Assert.Equal("Console.WriteLine(\"externo\");", File.ReadAllText(path));
        Assert.True(doc.IsDirty);

        dialogs.ConfirmAnswer = (_, _) => true;
        Assert.True(vm.Save(doc));
        Assert.StartsWith("// editor", File.ReadAllText(path));

        // Depois de salvar, um novo Ctrl+S não pergunta nada.
        dialogs.Confirms.Clear();
        doc.Document.Insert(0, " ");
        Assert.True(vm.Save(doc));
        Assert.Empty(dialogs.Confirms);
        vm.Dispose();
        return Task.CompletedTask;
    });

    [Fact]
    public void Arquivo_1252_com_emoji_pede_para_salvar_em_UTF8() => Ui.Run(() =>
    {
        var (vm, dialogs, _) = Create();
        var path = Path.Combine(Ui.NewFolder(), "Velho.cs");
        File.WriteAllBytes(path, TextFileIO.Legacy.GetBytes("// ação"));
        var doc = vm.OpenFile(path)!;
        Assert.Equal(1252, doc.Encoding.CodePage);
        doc.Document.Insert(doc.Document.TextLength, " 😀");

        dialogs.ConfirmAnswer = (title, _) => !title.Contains("UTF-8");
        Assert.False(vm.Save(doc));
        Assert.Equal(TextFileIO.Legacy.GetBytes("// ação"), File.ReadAllBytes(path));

        dialogs.ConfirmAnswer = (_, _) => true;
        Assert.True(vm.Save(doc));
        Assert.Equal("// ação 😀", Encoding.UTF8.GetString(File.ReadAllBytes(path)));
        Assert.Equal("UTF-8", doc.EncodingName);
        vm.Dispose();
        return Task.CompletedTask;
    });

    [Fact]
    public void Fechar_pasta_descarta_avaliacao_que_termina_depois() => Ui.Run(async () =>
    {
        var (vm, _, _) = Create();
        await vm.InitializeAsync();
        var created = ProjectCreator.CreateConsoleProject(Ui.NewFolder("pasta"), "Lento");

        var opening = vm.OpenFolderAsync(created.Directory, created.ProjectPath, promptForUnsaved: false);
        await Ui.WaitUntil(() => vm.Language?.MainModel != null, what: "estimativa do projeto");
        await vm.CloseFolderCommand.ExecuteAsync(null);
        await opening;
        await Task.Delay(500);

        Assert.Null(vm.CurrentFolder);
        Assert.Null(vm.Language!.MainModel);
        vm.Dispose();
    });

    [Fact]
    public void Botao_Parar_continua_habilitado_durante_a_execucao() => Ui.Run(async () =>
    {
        var (vm, dialogs, terminal) = Create();
        await vm.InitializeAsync();
        var created = ProjectCreator.CreateConsoleProject(Ui.NewFolder("pasta"), "Espera",
            "Console.Write(\"Nome: \");" + Environment.NewLine + "Console.ReadLine();" + Environment.NewLine);
        await vm.OpenFolderAsync(created.Directory, created.ProjectPath, promptForUnsaved: false);

        // Começa pelo mesmo botão (Executar/Parar).
        vm.RunOrStopCommand.Execute(null);
        await Ui.WaitUntil(() => vm.RunState == RunState.Running, 120_000, "programa em execução");
        await Ui.WaitUntil(() => terminal.Output.Contains("Nome:"), 10_000, "prompt no terminal");

        Assert.Equal("Parar", vm.RunButtonText);
        Assert.True(vm.RunOrStopCommand.CanExecute(null), "O botão Parar precisa estar habilitado.");

        vm.RunOrStopCommand.Execute(null);
        await Ui.WaitUntil(() => vm.RunState == RunState.Idle, 15_000, "fim da execução");
        Assert.Contains("Execução interrompida.", terminal.Notices);
        Assert.Empty(dialogs.Errors);
        vm.Dispose();
    });
}
