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
    private static readonly Dispatcher TestDispatcher;
    static Ui()
    {
        // Preferências e recuperação dos testes ficam numa pasta própria.
        AppPaths.Root = Path.Combine(Path.GetTempPath(), "csharplab-apptests", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(AppPaths.Root);
        using var ready = new ManualResetEventSlim();
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = (System.Windows.Application)Activator.CreateInstance(
                    typeof(MainViewModel).Assembly.GetType("CSharpLab.App", throwOnError: true)!)!;
                app.GetType().GetMethod("InitializeComponent")!.Invoke(app, null);
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
                ready.Set();
                Dispatcher.Run();
            }
            catch (Exception ex) { failure = ex; ready.Set(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        if (failure != null) throw failure;
        TestDispatcher = System.Windows.Application.Current.Dispatcher;
    }

    public static void Run(Func<Task> test, int timeoutSeconds = 120)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TestDispatcher.BeginInvoke(async () =>
        {
            try { await test(); completion.SetResult(); }
            catch (Exception ex) { completion.SetException(ex); }
        });
        completion.Task.WaitAsync(TimeSpan.FromSeconds(timeoutSeconds)).GetAwaiter().GetResult();
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
    public string? FileAnswer { get; set; }
    public string? PickFile(string title, string filter, string? initialDirectory) => FileAnswer;
    public string? PickSaveFile(string suggestedName, string? initialDirectory) => null;
    public NewProjectRequest? AskNewProject(string title, string? explanation, string defaultName, string defaultLocation) => null;
    public ProjectFile? SelectProject(IReadOnlyList<ProjectFile> projects, string folder) => projects.FirstOrDefault();
    public string? TextAnswer { get; set; }
    public string? AskText(string title, string message, string initial, Func<string, string?>? validate = null) => TextAnswer;
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
