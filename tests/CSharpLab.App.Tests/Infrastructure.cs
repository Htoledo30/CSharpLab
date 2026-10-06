using System.Diagnostics;
using System.Text;
using System.Windows.Threading;
using CSharpLab.Core.Projects;
using CSharpLab.Core.Settings;
using CSharpLab.Core.Terminal;
using CSharpLab.ViewModels;

// Os ViewModels usam um Dispatcher por thread; os testes rodam um de cada vez.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace CSharpLab.App.Tests;

/// <summary>Roda um teste assíncrono numa thread STA com Dispatcher, como na interface real.</summary>
public static class Ui
{
    static Ui()
    {
        // Preferências e recuperação dos testes ficam numa pasta própria.
        AppPaths.Root = Path.Combine(Path.GetTempPath(), "csharplab-apptests", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(AppPaths.Root);
    }

    public static void Run(Func<Task> test, int timeoutSeconds = 120)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(TimeSpan.FromSeconds(timeoutSeconds), DispatcherPriority.Normal, (_, _) =>
            {
                failure ??= new TimeoutException("O teste não terminou a tempo.");
                frame.Continue = false;
            }, dispatcher);
            dispatcher.BeginInvoke(async () =>
            {
                try { await test(); }
                catch (Exception ex) { failure ??= ex; }
                finally { frame.Continue = false; }
            });
            Dispatcher.PushFrame(frame);
            timer.Stop();
            dispatcher.InvokeShutdown();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw new Exception(failure.Message, failure);
    }

    public static async Task WaitUntil(Func<bool> condition, int timeoutMs = 30000, string? what = null)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs)
                throw new TimeoutException("Esperando: " + (what ?? "condição"));
            await Task.Delay(25);
        }
    }

    public static string NewFolder(string name = "teste")
    {
        var dir = Path.Combine(Path.GetTempPath(), "csharplab-apptests", Guid.NewGuid().ToString("N")[..8], name);
        Directory.CreateDirectory(dir);
        return dir;
    }
}

/// <summary>Diálogos com respostas programadas e registro do que foi perguntado.</summary>
public sealed class FakeDialogs : IDialogService
{
    public List<(string Title, string Message)> Confirms { get; } = [];
    public List<string> Errors { get; } = [];
    public Func<string, string, bool> ConfirmAnswer { get; set; } = (_, _) => true;
    public SaveChoice SaveAnswer { get; set; } = SaveChoice.Discard;

    public SaveChoice AskSaveChanges(IReadOnlyList<string> fileNames) => SaveAnswer;

    public bool Confirm(string title, string message, string confirmText, bool danger = false)
    {
        Confirms.Add((title, message));
        return ConfirmAnswer(title, message);
    }

    public void ShowError(string title, string message, string? details = null) => Errors.Add(title + ": " + message + " " + details);
    public ExternalChangeChoice AskExternalChange(string fileName) => ExternalChangeChoice.KeepMine;
    public bool ShowSdkMissing(string message, string? details) => false;
    public string? PickFolder(string title, string? initialDirectory) => null;
    public string? PickFile(string title, string filter, string? initialDirectory) => null;
    public string? PickSaveFile(string suggestedName, string? initialDirectory) => null;
    public NewProjectRequest? AskNewProject(string title, string? explanation, string defaultName, string defaultLocation) => null;
    public ProjectFile? SelectProject(IReadOnlyList<ProjectFile> projects, string folder) => projects.FirstOrDefault();
}

public sealed class FakeTerminal : ITerminalHost
{
    private readonly StringBuilder _output = new();
    public PseudoConsoleSession? Session { get; private set; }
    public List<string> Notices { get; } = [];

    public string Output
    {
        get { lock (_output) return _output.ToString(); }
    }

    public (short Columns, short Rows) Size => (100, 30);

    public void Attach(PseudoConsoleSession session)
    {
        Session = session;
        session.Output += text => { lock (_output) _output.Append(text); };
    }

    public void WriteNotice(string text, bool isError = false) => Notices.Add(text);
    public void Clear() { }
    public void FocusTerminal() { }
}
