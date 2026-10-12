using System.Text.Json;
using CSharpLab.Core.Settings;
using CSharpLab.ViewModels;

namespace CSharpLab.App.Tests;

public sealed class SessionLogTests
{
    /// <summary>Eventos com esse nome. Outros testes gravam ao mesmo tempo: uma linha pela metade é ignorada.</summary>
    internal static List<JsonElement> Events(string eventName)
    {
        var list = new List<JsonElement>();
        using var stream = new FileStream(SessionLog.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            try
            {
                var e = JsonDocument.Parse(line).RootElement;
                if (e.GetProperty("event").GetString() == eventName) list.Add(e);
            }
            catch (JsonException) { }
        }
        return list;
    }

    [Fact]
    public void Terminei_salva_os_arquivos_da_pasta_e_registra_o_pedido_de_revisao() => Ui.Run(async () =>
    {
        var vm = new MainViewModel(new AppSettings()) { Dialogs = new FakeDialogs(), Terminal = new FakeTerminal() };
        var dir = Ui.NewFolder("revisao");
        var path = Path.Combine(dir, "Heroi.cs");
        File.WriteAllText(path, "class Heroi { }");
        Assert.True(await vm.OpenFolderAsync(dir, null, promptForUnsaved: false));
        var doc = vm.OpenFile(path)!;
        doc.Document.Insert(0, "// mexi aqui\n");

        vm.RequestReviewCommand.Execute(null);

        Assert.False(doc.IsDirty);
        Assert.StartsWith("// mexi aqui", File.ReadAllText(path));
        Assert.Contains(Events("folder-opened"), e => e.GetProperty("folder").GetString() == dir);
        Assert.Contains(Events("saved"), e => e.GetProperty("file").GetString() == path);
        var review = Events("review-request").Last(e => e.GetProperty("folder").GetString() == dir);
        Assert.Equal(path, review.GetProperty("activeFile").GetString());
        Assert.True(review.TryGetProperty("errorCount", out _));
        vm.Dispose();
    });
}
