using System.Text.Json;
using CSharpLab.Core.Settings;

namespace CSharpLab.Tests;

public sealed class SessionLogTests
{
    /// <summary>Eventos com esse nome. Outros testes gravam ao mesmo tempo: uma linha pela metade é ignorada.</summary>
    private static List<JsonElement> Events(string eventName)
    {
        var list = new List<JsonElement>();
        foreach (var line in ReadLines())
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

    /// <summary>Lê sem impedir que outros testes continuem gravando.</summary>
    internal static List<string> ReadLines()
    {
        using var stream = new FileStream(SessionLog.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line) lines.Add(line);
        return lines;
    }

    [Fact]
    public void Grava_uma_linha_json_por_evento_com_os_campos_e_acentos_legiveis()
    {
        var name = "teste-" + Guid.NewGuid().ToString("N")[..8];
        SessionLog.Write(name, new { message = "Faltou ; no fim da linha", line = 12, file = (string?)null });

        var e = Assert.Single(Events(name));
        Assert.True(DateTime.TryParse(e.GetProperty("time").GetString(), out _));
        Assert.Equal("Faltou ; no fim da linha", e.GetProperty("message").GetString());
        Assert.Equal(12, e.GetProperty("line").GetInt32());
        Assert.Equal(JsonValueKind.Null, e.GetProperty("file").ValueKind);

        SessionLog.Write(name, new { message = "não salvou" });
        Assert.Contains(ReadLines(), l => l.Contains("\"não salvou\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Evento_sem_dados_tem_so_hora_e_nome()
    {
        var name = "teste-" + Guid.NewGuid().ToString("N")[..8];
        SessionLog.Write(name);
        Assert.Equal(["time", "event"], Assert.Single(Events(name)).EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public void Erro_registrado_no_erros_log_tambem_aparece_no_registro_da_sessao()
    {
        var context = "Teste " + Guid.NewGuid().ToString("N")[..8];
        try { throw new InvalidOperationException("quebrou de propósito"); }
        catch (InvalidOperationException ex) { AppPaths.Log(ex, context); }

        var e = Assert.Single(Events("app-error"), x => x.GetProperty("context").GetString() == context);
        Assert.Equal("System.InvalidOperationException", e.GetProperty("type").GetString());
        Assert.Equal("quebrou de propósito", e.GetProperty("message").GetString());
    }
}
