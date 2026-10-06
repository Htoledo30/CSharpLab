using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using CSharpLab.Core.Settings;
using CSharpLab.ViewModels;
using CSharpLab.Views;

namespace CSharpLab;

/// <summary>Janela principal: só layout, atalhos e encaminhamento para o editor ativo.</summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private bool _terminalScrolledAway;

    public MainWindow(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = vm;
        vm.Dialogs = new DialogService();
        vm.Terminal = Terminal;
        Terminal.Bind(vm);
        Editors.Bind(vm);
        vm.ShutdownForUpdateRequested += () =>
        {
            _closingForUpdate = true;
            Close();
        };
        Terminal.ScrolledAwayChanged += away =>
        {
            _terminalScrolledAway = away;
            UpdateScrollEndButton();
        };

        RestorePlacement(vm.Settings.Window);
        ApplyExplorerLayout();
        ApplyPanelLayout();
        MainArea.SizeChanged += (_, _) =>
        {
            if (_vm.IsPanelMaximized) ApplyPanelLayout();
        };
        vm.PropertyChanged += OnVmPropertyChanged;

        SourceInitialized += (_, _) => ApplyWindowFrame();
        StateChanged += (_, _) => UpdateMaximizedLayout();
        Activated += (_, _) =>
        {
            // Com um diálogo aberto, ele continua sendo a janela ativa.
            if (OwnedWindows.OfType<Window>().FirstOrDefault(w => w.IsVisible) is { } dialog)
            {
                dialog.Activate();
                return;
            }
            vm.CheckAllExternalChanges();
            Dispatcher.BeginInvoke(RestoreWorkFocus, System.Windows.Threading.DispatcherPriority.Input);
        };
        Editors.IsKeyboardFocusWithinChanged += (_, e) =>
        {
            if (e.NewValue is true) Terminal.WasLastUsed = false;
        };
        PreviewKeyDown += OnWindowPreviewKeyDown;
        Loaded += async (_, _) =>
        {
            UpdateMaximizedLayout();
            await vm.InitializeAsync();
            Editors.FocusActive();
        };
    }

    // ================================================================ layout

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.IsExplorerVisible):
                ApplyExplorerLayout();
                break;
            case nameof(MainViewModel.IsPanelOpen):
            case nameof(MainViewModel.PanelTab):
            case nameof(MainViewModel.IsPanelMaximized):
                ApplyPanelLayout();
                break;
        }
    }

    private void ApplyExplorerLayout()
    {
        bool visible = _vm.IsExplorerVisible;
        ExplorerColumn.Width = visible ? new GridLength(_vm.ExplorerWidth) : new GridLength(0);
        ExplorerSplitterColumn.Width = visible ? new GridLength(4) : new GridLength(0);
        ExplorerPane.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        ExplorerSplitter.Visibility = ExplorerPane.Visibility;
    }

    private void OnExplorerResized(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        var width = ExplorerColumn.ActualWidth;
        if (width < 120)
        {
            _vm.IsExplorerVisible = false;
            return;
        }
        _vm.ExplorerWidth = width;
    }

    private void ApplyPanelLayout()
    {
        bool open = _vm.IsPanelOpen;
        if (open)
        {
            var height = _vm.PanelHeight;
            if (_vm.IsPanelMaximized)
            {
                // Deixa só algumas linhas do editor à vista.
                var available = MainArea.ActualHeight > 0 ? MainArea.ActualHeight : ActualHeight - 100;
                height = Math.Max(160, available - 130);
            }
            else if (height < 120)
            {
                // Primeira abertura: cerca de 25% da altura disponível.
                var available = MainArea.ActualHeight > 0 ? MainArea.ActualHeight : ActualHeight - 100;
                height = Math.Max(160, available * 0.25 + 31);
                _vm.PanelHeight = height;
            }
            PanelRow.Height = new GridLength(height);
            PanelSplitterRow.Height = new GridLength(4);
            PanelSplitter.Visibility = Visibility.Visible;
            PanelBody.Visibility = Visibility.Visible;
        }
        else
        {
            PanelRow.Height = new GridLength(31);
            PanelSplitterRow.Height = new GridLength(0);
            PanelSplitter.Visibility = Visibility.Collapsed;
            PanelBody.Visibility = Visibility.Collapsed;
        }

        bool terminal = _vm.PanelTab == "terminal";
        Terminal.Visibility = open && terminal ? Visibility.Visible : Visibility.Collapsed;
        ProblemsPane.Visibility = open && !terminal ? Visibility.Visible : Visibility.Collapsed;
        TerminalTab.IsChecked = open && terminal;
        ProblemsTab.IsChecked = open && !terminal;
        UpdateScrollEndButton();
    }

    private void OnPanelResized(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        var height = PanelRow.ActualHeight;
        _vm.IsPanelMaximized = false;
        if (height < 80)
        {
            _vm.IsPanelOpen = false;
            return;
        }
        _vm.PanelHeight = height;
    }

    private void UpdateScrollEndButton() =>
        ScrollEndButton.Visibility = _terminalScrolledAway && _vm.IsPanelOpen && _vm.PanelTab == "terminal"
            ? Visibility.Visible
            : Visibility.Collapsed;

    private void OnScrollTerminalToEnd(object sender, RoutedEventArgs e) => Terminal.ScrollToEnd();

    /// <summary>Ao voltar para a janela, o foco volta para onde o usuário estava trabalhando.</summary>
    private void RestoreWorkFocus()
    {
        if (!IsActive) return;
        var focused = Keyboard.FocusedElement as DependencyObject;
        if (focused is TextBox || focused != null && Editors.IsAncestorOf(focused)) return;
        if (Terminal.WasLastUsed && Terminal.IsVisible) Terminal.FocusTerminal();
        else if (focused is null or Button or Window or ListBoxItem) Editors.FocusActive();
    }

    // ================================================================ abas

    private void OnTabMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && sender is ListBoxItem { DataContext: DocumentViewModel doc })
        {
            _vm.CloseTabCommand.Execute(doc);
            e.Handled = true;
        }
        else if (e.ChangedButton == MouseButton.Left)
        {
            Editors.FocusActive();
        }
    }

    private void OnTabsWheel(object sender, MouseWheelEventArgs e)
    {
        if (FindChild<ScrollViewer>(Tabs) is { } sv)
        {
            sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta / 2.0);
            e.Handled = true;
        }
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            if (FindChild<T>(child) is { } found) return found;
        }
        return null;
    }

    // ================================================================ atalhos do editor

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        if (key == Key.F && mods == (ModifierKeys.Control | ModifierKeys.Shift) ||
            key == Key.F && mods == (ModifierKeys.Alt | ModifierKeys.Shift))
        {
            OnFormat(this, e);
            e.Handled = true;
        }
        else if (key == Key.F && mods == ModifierKeys.Control)
        {
            OpenFind(replace: false);
            e.Handled = true;
        }
        else if (key == Key.H && mods == ModifierKeys.Control)
        {
            OpenFind(replace: true);
            e.Handled = true;
        }
    }

    private void OpenFind(bool replace)
    {
        if (Editors.ActiveEditor is { } editor) Editors.FindBar.Open(editor, replace);
    }

    private void OnFind(object sender, RoutedEventArgs e) => OpenFind(replace: false);
    private void OnReplace(object sender, RoutedEventArgs e) => OpenFind(replace: true);

    private async void OnFormat(object sender, RoutedEventArgs e)
    {
        if (Editors.ActiveEditor is { } editor)
        {
            if (_vm.Language == null)
            {
                _vm.NotifyInfo("Os serviços de C# ainda estão carregando.");
                return;
            }
            await editor.FormatAsync();
        }
    }

    /// <summary>Executa uma ação no editor ativo e devolve o foco a ele.</summary>
    private void WithEditor(Action<Editor.CodeEditor> action)
    {
        if (Editors.ActiveEditor is not { } editor) return;
        editor.TextArea.Focus();
        action(editor);
    }

    private void OnQuickFix(object sender, RoutedEventArgs e) => WithEditor(ed => _ = ed.ShowQuickFixesAsync());
    private void OnGoToDefinition(object sender, RoutedEventArgs e) => WithEditor(ed => _ = ed.GoToDefinitionAsync());
    private void OnRenameSymbol(object sender, RoutedEventArgs e) => WithEditor(ed => _ = ed.RenameSymbolAsync());
    private void OnToggleComment(object sender, RoutedEventArgs e) => WithEditor(ed => ed.ToggleComment());
    private void OnDuplicateLine(object sender, RoutedEventArgs e) => WithEditor(ed => ed.DuplicateLines());
    private void OnMoveLineUp(object sender, RoutedEventArgs e) => WithEditor(ed => ed.MoveLines(-1));
    private void OnMoveLineDown(object sender, RoutedEventArgs e) => WithEditor(ed => ed.MoveLines(1));
    private void OnDeleteLine(object sender, RoutedEventArgs e) => WithEditor(ed => ed.DeleteLines());

    private void OnShowCompletion(object sender, RoutedEventArgs e)
    {
        if (Editors.ActiveEditor is { } editor)
        {
            editor.TextArea.Focus();
            editor.ShowCompletion();
        }
    }

    private void Exec(RoutedUICommand command)
    {
        if (Editors.ActiveEditor is { } editor && command.CanExecute(null, editor.TextArea))
        {
            command.Execute(null, editor.TextArea);
            editor.TextArea.Focus();
        }
    }

    private void OnUndo(object sender, RoutedEventArgs e) => Exec(ApplicationCommands.Undo);
    private void OnRedo(object sender, RoutedEventArgs e) => Exec(ApplicationCommands.Redo);
    private void OnCut(object sender, RoutedEventArgs e) => Exec(ApplicationCommands.Cut);
    private void OnCopy(object sender, RoutedEventArgs e) => Exec(ApplicationCommands.Copy);
    private void OnPaste(object sender, RoutedEventArgs e) => Exec(ApplicationCommands.Paste);

    // ================================================================ janela

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnExit(object sender, RoutedEventArgs e) => Close();

    private bool _closingForUpdate;

    protected override void OnClosing(CancelEventArgs e)
    {
        // Para atualizar, as alterações já foram confirmadas pelo comando "Reiniciar agora".
        if (!_closingForUpdate && !_vm.ConfirmExit())
        {
            e.Cancel = true;
            return;
        }
        SavePlacement();
        Terminal.Shutdown();
        _vm.Shutdown();
        base.OnClosing(e);
    }

    private void RestorePlacement(WindowPlacement? p)
    {
        if (p == null || p.Width < 400 || p.Height < 300) return;
        var area = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var rect = new Rect(p.Left, p.Top, p.Width, p.Height);
        if (!area.IntersectsWith(rect) || rect.Left + 100 > area.Right || rect.Top + 40 > area.Bottom) return;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = p.Left;
        Top = p.Top;
        Width = p.Width;
        Height = p.Height;
        if (p.Maximized) WindowState = WindowState.Maximized;
    }

    private void SavePlacement()
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        _vm.Settings.Window = new WindowPlacement
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Width = bounds.Width,
            Height = bounds.Height,
            Maximized = WindowState == WindowState.Maximized,
        };
    }

    private void UpdateMaximizedLayout()
    {
        bool max = WindowState == WindowState.Maximized;
        MaxButton.Content = max ? "" : "";
        MaxButton.ToolTip = max ? "Restaurar" : "Maximizar";
        AutomationPropertiesHelper.SetName(MaxButton, max ? "Restaurar" : "Maximizar");
        RootBorder.Padding = max ? MaximizedPadding() : new Thickness(0);
    }

    /// <summary>Janela maximizada sem moldura avança além da tela pela espessura da borda; compensa.</summary>
    private Thickness MaximizedPadding()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            uint dpi = GetDpiForWindow(hwnd);
            int frame = GetSystemMetricsForDpi(SM_CXFRAME, dpi) + GetSystemMetricsForDpi(SM_CXPADDEDBORDER, dpi);
            double scale = dpi / 96.0;
            double t = frame / scale;
            return new Thickness(t, t, t, t);
        }
        catch
        {
            return new Thickness(7);
        }
    }

    private void ApplyWindowFrame()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int dark = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            int round = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
            int border = 0x00302B2A; // COLORREF de #2A2B30
            DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));
        }
        catch
        {
            // Windows 10 não tem todos os atributos; a janela continua normal.
        }
    }

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWCP_ROUND = 2;
    private const int SM_CXFRAME = 32;
    private const int SM_CXPADDEDBORDER = 92;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);
}

internal static class AutomationPropertiesHelper
{
    public static void SetName(DependencyObject element, string name) =>
        System.Windows.Automation.AutomationProperties.SetName(element, name);
}
