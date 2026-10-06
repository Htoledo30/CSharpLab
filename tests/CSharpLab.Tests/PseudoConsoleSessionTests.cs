using System.Diagnostics;
using System.Text;
using CSharpLab.Core.Terminal;

namespace CSharpLab.Tests;

[Collection("SampleApp")]
public class PseudoConsoleSessionTests(SampleConsoleApp app)
{
    private sealed class Capture
    {
        private readonly StringBuilder _sb = new();
        public void Add(string s) { lock (_sb) _sb.Append(s); }
        public string Text { get { lock (_sb) return _sb.ToString(); } }

        public async Task WaitFor(string text, int timeoutMs = 10000)
        {
            var sw = Stopwatch.StartNew();
            while (!Text.Contains(text))
            {
                if (sw.ElapsedMilliseconds > timeoutMs)
                    throw new TimeoutException($"Esperava \"{text}\". Saída: {Text}");
                await Task.Delay(20);
            }
        }
    }

    private PseudoConsoleSession Start(Capture cap, params string[] args)
    {
        var s = PseudoConsoleSession.Start(new ProcessLaunch(app.ExePath, args, app.Directory), 100, 30);
        s.Output += cap.Add;
        return s;
    }

    [Fact]
    public async Task ReadLine_mostra_prompt_sem_quebra_e_le_acentos()
    {
        var cap = new Capture();
        using var s = Start(cap, "readline");
        await cap.WaitFor("Nome:");
        s.Write("José Henrique\r");
        await cap.WaitFor("Olá, José Henrique!");
        Assert.Equal(0, await s.Completion.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Contains("✓", cap.Text);
    }

    [Fact]
    public async Task ReadKey_recebe_tecla_de_seta()
    {
        var cap = new Capture();
        using var s = Start(cap, "readkey");
        await cap.WaitFor("Tecla:");
        s.Write("\x1b[A");
        await cap.WaitFor("[UpArrow]");
        await s.Completion.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Saida_final_sem_quebra_e_codigo_de_saida_sao_preservados()
    {
        var cap = new Capture();
        using var s = Start(cap, "exit3");
        Assert.Equal(3, await s.Completion.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Contains("fim sem quebra", cap.Text);
    }

    [Fact]
    public async Task Kill_encerra_loop_infinito_e_processos_filhos()
    {
        var cap = new Capture();
        using var s = Start(cap, "child");
        await cap.WaitFor("child=");
        await Task.Delay(300);
        var text = cap.Text;
        var idx = text.IndexOf("child=") + 6;
        var digits = new string(text[idx..].TakeWhile(char.IsDigit).ToArray());
        var childId = int.Parse(digits);

        s.Kill();
        await s.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(300);
        Assert.Throws<ArgumentException>(() => Process.GetProcessById(childId));
    }

    [Fact]
    public async Task Console_Clear_e_cores_viram_sequencias_VT()
    {
        var cap = new Capture();
        using var s = Start(cap, "clear");
        await s.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        await cap.WaitFor("depois");
        var text = cap.Text;
        Assert.True(text.Contains("[2J") || text.Contains("[H"), "Clear deveria gerar sequência VT");
        Assert.Contains("[", text[text.IndexOf("antes")..]);
        Assert.DoesNotContain("[2J", text[text.IndexOf("depois")..]);
    }

    [Fact]
    public async Task ReadKey_com_eco_mostra_a_tecla()
    {
        var cap = new Capture();
        using var s = Start(cap, "readkeyecho");
        await cap.WaitFor(">");
        s.Write("z");
        await cap.WaitFor("[z]");
        await s.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        // A tecla aparece duas vezes: o eco do console e o texto impresso pelo programa.
        Assert.True(cap.Text.Count(c => c == 'z') >= 2, cap.Text);
    }

    [Fact]
    public async Task CtrlC_interrompe_o_programa()
    {
        var cap = new Capture();
        using var s = Start(cap, "loop");
        await cap.WaitFor("loop");
        s.Write("");
        var code = await s.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotEqual(0, code);
    }

    [Fact]
    public async Task Excecao_preserva_stack_trace_e_codigo()
    {
        var cap = new Capture();
        using var s = Start(cap, "throw");
        var code = await s.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotEqual(0, code);
        Assert.Contains("InvalidOperationException", cap.Text);
        Assert.Contains("falhou", cap.Text);
    }
}
