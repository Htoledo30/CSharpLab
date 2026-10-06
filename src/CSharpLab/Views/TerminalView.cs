using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using CSharpLab.Core.Terminal;
using CSharpLab.ViewModels;
using Microsoft.Terminal.Wpf;

namespace CSharpLab.Views;

/// <summary>
/// Ponte entre a sessão ConPTY e o controle de terminal do Windows Terminal.
/// A saída é agrupada em lotes (~60 por segundo) para não sobrecarregar a interface.
/// </summary>
internal sealed class SessionConnection : ITerminalConnection
{
    private readonly object _gate = new();
    private readonly StringBuilder _pending = new();
    private readonly Timer _flushTimer;
    private PseudoConsoleSession? _session;
    private uint _rows = 30, _columns = 120;

    public SessionConnection() => _flushTimer = new Timer(_ => Flush(), null, 16, 16);

    public event EventHandler<TerminalOutputEventArgs>? TerminalOutput;

    public (short Columns, short Rows) Size => ((short)Math.Clamp(_columns, 20, 500), (short)Math.Clamp(_rows, 5, 300));

    public void Bind(PseudoConsoleSession? session)
    {
        if (_session != null) _session.Output -= Enqueue;
        Flush();
        _session = session;
        if (session != null)
        {
            session.Output += Enqueue;
            session.Resize(Size.Columns, Size.Rows);
        }
    }

    public void Emit(string text)
    {
        Flush();
        TerminalOutput?.Invoke(this, new TerminalOutputEventArgs(text));
    }

    private void Enqueue(string text)
    {
        lock (_gate) _pending.Append(text);
    }

    private void Flush()
    {
        string text;
        lock (_gate)
        {
            if (_pending.Length == 0) return;
            text = _pending.ToString();
            _pending.Clear();
        }
        try
        {
            TerminalOutput?.Invoke(this, new TerminalOutputEventArgs(text));
        }
        catch
        {
        }
    }

    public void Start() { }

    public void WriteInput(string data) => _session?.Write(data);

    public void Resize(uint rows, uint columns)
    {
        _rows = rows;
        _columns = columns;
        _session?.Resize(Size.Columns, Size.Rows);
    }

    public void Close() { }

    public void Dispose() => _flushTimer.Dispose();
}

public sealed class TerminalView : UserControl, ITerminalHost
{
    private static readonly FieldInfo? ContainerField =
        typeof(TerminalControl).GetField("termContainer", BindingFlags.Instance | BindingFlags.NonPublic);

    private readonly TerminalControl _control;
    private readonly SessionConnection _connection = new();
    private MainViewModel? _vm;
    private HwndHost? _container;
    private bool _connected;
    private bool _hasRun;

    public TerminalView()
    {
        _control = new TerminalControl { Focusable = true };
        System.Windows.Automation.AutomationProperties.SetName(_control, "Terminal");
        Content = _control;
        Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1B, 0x1E));
        Loaded += OnLoaded;
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) Dispatcher.BeginInvoke(ApplyTheme, System.Windows.Threading.DispatcherPriority.ContextIdle);
        };
    }

    /// <summary>Verdadeiro se o último clique ou tecla foi no terminal.</summary>
    public bool WasLastUsed { get; set; }

    /// <summary>Verdadeiro quando o usuário rolou para cima e não está vendo o fim da saída.</summary>
    public event Action<bool>? ScrolledAwayChanged;

    public void Bind(MainViewModel vm) => _vm = vm;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_connected)
        {
            _control.Connection = _connection;
            _connected = true;
        }
        if (_container == null)
        {
            // TerminalContainer é interno ao pacote; usado só para rolagem e foco.
            _container = ContainerField?.GetValue(_control) as HwndHost;
            if (_container != null)
            {
                // Permite que o WPF saiba quando o foco está no terminal (e o devolva a ele ao reativar a janela).
                _container.Focusable = true;
                _container.IsKeyboardFocusWithinChanged += (_, e) =>
                {
                    if (e.NewValue is true) WasLastUsed = true;
                };
            }
            var scrolled = _container?.GetType().GetEvent("TerminalScrolled", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (scrolled != null && scrolled.EventHandlerType == typeof(EventHandler<(int, int, int)>))
            {
                EventHandler<(int, int, int)> handler = (_, args) =>
                {
                    var (viewTop, viewHeight, bufferSize) = args;
                    bool away = viewTop + viewHeight < bufferSize - 1;
                    Dispatcher.BeginInvoke(() => ScrolledAwayChanged?.Invoke(away));
                };
                scrolled.GetAddMethod(nonPublic: true)?.Invoke(_container, [handler]);
            }
        }
        ApplyTheme();
        ComponentDispatcher.ThreadFilterMessage -= OnPreprocessMessage;
        ComponentDispatcher.ThreadFilterMessage += OnPreprocessMessage;
        if (!_hasRun)
            _connection.Emit("\x1b[90mO programa aparece aqui quando você executar (F5).\x1b[0m");
    }

    private void ApplyTheme()
    {
        if (!IsLoaded || !IsVisible || _container is not { Handle: not 0 }) return;
        try
        {
            var theme = new TerminalTheme
            {
                DefaultBackground = Rgb(0x1A1B1E),
                DefaultForeground = Rgb(0xD7D7DC),
                DefaultSelectionBackground = Rgb(0x3B4670),
                CursorStyle = CursorStyle.BlinkingBar,
                ColorTable =
                [
                    Rgb(0x2B2C31), Rgb(0xF0727C), Rgb(0x7EC699), Rgb(0xE2B66A),
                    Rgb(0x7FA7F5), Rgb(0xC792EA), Rgb(0x4FC1B0), Rgb(0xC8C8CE),
                    Rgb(0x6E6E77), Rgb(0xFF8A93), Rgb(0x98D9AE), Rgb(0xF0CE8A),
                    Rgb(0x9DBBFA), Rgb(0xD7A8F2), Rgb(0x74D4C5), Rgb(0xF2F2F5),
                ],
            };
            _control.SetTheme(theme, "Cascadia Mono", 11, Color.FromRgb(0x1A, 0x1B, 0x1E));
        }
        catch
        {
            // O controle nativo ainda não está pronto; tenta de novo quando ficar visível.
        }
    }

    /// <summary>COLORREF (0x00BBGGRR) a partir de 0xRRGGBB.</summary>
    private static uint Rgb(uint rgb) => ((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF);

    public (short Columns, short Rows) Size => _connection.Size;

    public void Attach(PseudoConsoleSession session)
    {
        _hasRun = true;
        ApplyTheme();
        WasLastUsed = true;
        // Sessão nova começa com a tela limpa (inclusive o histórico).
        _connection.Emit("\x1b[0m\x1b[2J\x1b[3J\x1b[H");
        _connection.Bind(session);
    }

    public void WriteNotice(string text, bool isError = false)
    {
        _connection.Bind(null);
        var color = isError ? "\x1b[91m" : "\x1b[90m";
        _connection.Emit($"\x1b[0m\r\n\r\n{color}── {text} ──\x1b[0m\r\n");
    }

    public void Clear() => _connection.Emit("\x1b[0m\x1b[2J\x1b[3J\x1b[H");

    public void FocusTerminal()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_container != null) Keyboard.Focus(_container);
            else _control.Focus();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    public void ScrollToEnd()
    {
        if (_container == null) return;
        // Rola bastante para baixo; o controle limita ao fim do buffer.
        var scroll = _container.GetType().GetMethod("UserScroll", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        try { scroll?.Invoke(_container, [int.MaxValue / 2]); } catch { }
        FocusTerminal();
    }

    // ------------------------------------------------------------- teclado

    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_LBUTTONDOWN = 0x0201;

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG msg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG msg);
    private const int WM_SYSKEYDOWN = 0x0104;

    [DllImport("user32.dll")]
    private static extern bool IsChild(IntPtr parent, IntPtr child);

    private bool IsTerminalWindow(IntPtr hwnd)
    {
        var host = _container?.Handle ?? IntPtr.Zero;
        return host != IntPtr.Zero && (hwnd == host || IsChild(host, hwnd));
    }

    /// <summary>
    /// O terminal é uma janela nativa: os atalhos do editor (F5, Shift+F5, Ctrl+S…) e
    /// copiar/colar são tratados aqui antes de a tecla chegar a ele.
    /// </summary>
    private void OnPreprocessMessage(ref MSG msg, ref bool handled)
    {
        if (handled || (msg.message != WM_KEYDOWN && msg.message != WM_SYSKEYDOWN && msg.message != WM_KEYUP && msg.message != WM_LBUTTONDOWN) ||
            !IsTerminalWindow(msg.hwnd)) return;
        if (msg.message == WM_KEYUP)
        {
            Forward(ref msg, ref handled);
            return;
        }
        WasLastUsed = true;
        if (msg.message == WM_LBUTTONDOWN)
        {
            // O clique dá foco à janela nativa; o WPF também precisa saber disso.
            if (_container != null && !_container.IsKeyboardFocusWithin)
                Dispatcher.BeginInvoke(() => Keyboard.Focus(_container), System.Windows.Threading.DispatcherPriority.Input);
            return;
        }
        var key = KeyInterop.KeyFromVirtualKey((int)msg.wParam);
        var mods = Keyboard.Modifiers;
        var vm = _vm;
        if (vm == null) return;

        switch (key)
        {
            case Key.F5 when mods == ModifierKeys.Shift:
                vm.StopCommand.Execute(null);
                handled = true;
                break;
            case Key.F5 when mods == ModifierKeys.None:
                vm.RunCommand.Execute(null);
                handled = true;
                break;
            case Key.C when mods == ModifierKeys.Control:
                var selected = _control.GetSelectedText();
                if (!string.IsNullOrEmpty(selected))
                {
                    try { Clipboard.SetText(selected); } catch { }
                    handled = true;
                }
                // Sem seleção, a tecla segue para o programa como interrupção (Ctrl+C).
                break;
            case Key.V when mods == ModifierKeys.Control:
            case Key.Insert when mods == ModifierKeys.Shift:
                try
                {
                    if (Clipboard.ContainsText())
                        _connection.WriteInput(Clipboard.GetText().Replace("\r\n", "\r").Replace('\n', '\r'));
                }
                catch
                {
                }
                handled = true;
                break;
            case Key.S when mods == ModifierKeys.Control:
                vm.SaveActiveCommand.Execute(null);
                handled = true;
                break;
            case Key.S when mods == (ModifierKeys.Control | ModifierKeys.Shift):
                vm.SaveAllCommand.Execute(null);
                handled = true;
                break;
            case Key.J when mods == ModifierKeys.Control:
                vm.TogglePanelCommand.Execute(null);
                handled = true;
                break;
            case Key.B when mods == ModifierKeys.Control:
                vm.ToggleExplorerCommand.Execute(null);
                handled = true;
                break;
        }

        // Demais teclas vão direto ao terminal: o WPF consumiria setas e Tab para navegar entre controles.
        if (!handled && msg.message == WM_KEYDOWN) Forward(ref msg, ref handled);
    }

    private static void Forward(ref MSG msg, ref bool handled)
    {
        TranslateMessage(ref msg);
        DispatchMessage(ref msg);
        handled = true;
    }

    public void Shutdown()
    {
        ComponentDispatcher.ThreadFilterMessage -= OnPreprocessMessage;
        _connection.Bind(null);
        _connection.Dispose();
    }
}
