using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CSharpLab.Editor;
using CSharpLab.Screens;
using CSharpLab.ViewModels;

namespace CSharpLab.Views;

/// <summary>
/// Mantém um editor por documento aberto (preserva cursor, rolagem e desfazer ao trocar de aba)
/// e mostra apenas o da aba ativa. Os editores são criados na primeira vez que a aba é mostrada.
/// Telas de jogo (Screens/*.json) abrem no editor visual, com a opção de ver o texto.
/// </summary>
public sealed class EditorHost : Grid
{
    private readonly Dictionary<DocumentViewModel, FrameworkElement> _editors = [];
    private readonly Grid _editorLayer = new();
    private MainViewModel? _vm;

    public EditorHost()
    {
        Children.Add(_editorLayer);
        FindBar = new FindReplaceBar
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 22, 0),
        };
        Panel.SetZIndex(FindBar, 10);
        Children.Add(FindBar);
    }

    public FindReplaceBar FindBar { get; }

    /// <summary>
    /// Com o Estúdio aberto, as abas ficam escondidas: nada de criar editores para elas até ele fechar
    /// (o Estúdio tem os próprios editores dos arquivos do jogo).
    /// </summary>
    public bool Suspended
    {
        get => _suspended;
        set
        {
            if (_suspended == value) return;
            _suspended = value;
            if (value)
            {
                FindBar.Close();
                foreach (var view in _editors.Values)
                {
                    if (view is CodeEditor editor) editor.ClosePopups();
                    else if (view is ScreenEditorView screen) screen.ClosePopups();
                }
            }
            else
            {
                ShowActive();
            }
        }
    }

    private bool _suspended;

    private FrameworkElement? ActiveView => _vm?.ActiveDocument is { } d && _editors.TryGetValue(d, out var e) ? e : null;

    /// <summary>O editor de texto da aba ativa (null quando ela mostra o editor visual de uma tela).</summary>
    public CodeEditor? ActiveEditor => ActiveView switch
    {
        CodeEditor editor => editor,
        ScreenEditorView screen => screen.ActiveTextEditor,
        _ => null,
    };

    /// <summary>O editor visual da aba ativa, quando ela é uma tela no Estúdio.</summary>
    public ScreenEditorView? ActiveScreen => ActiveView is ScreenEditorView { IsDesignMode: true } screen ? screen : null;

    public void Bind(MainViewModel vm)
    {
        _vm = vm;
        vm.Documents.CollectionChanged += OnDocumentsChanged;
        vm.PropertyChanged += OnVmPropertyChanged;
        vm.GoToRequested += (doc, line, column, offset) =>
        {
            if (_suspended) return;
            ShowActive();
            if (!_editors.TryGetValue(doc, out var view)) return;
            if (view is ScreenEditorView screen)
            {
                screen.GoTo(line, column, offset);
            }
            else if (view is CodeEditor editor)
            {
                // Depois do layout, para que a rolagem funcione na aba recém-mostrada.
                Dispatcher.BeginInvoke(() => editor.GoTo(line, column, offset), DispatcherPriority.Loaded);
            }
        };
        vm.FocusEditorRequested += FocusActive;
        vm.ScreenDesignRequested += doc =>
        {
            if (_suspended) return;
            ShowActive();
            if (_editors.TryGetValue(doc, out var view) && view is ScreenEditorView screen) screen.ShowDesign();
        };
        ShowActive();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ActiveDocument)) ShowActive();
    }

    private void OnDocumentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_vm == null) return;
        foreach (var doc in _editors.Keys.Where(d => !_vm.Documents.Contains(d)).ToList())
        {
            var view = _editors[doc];
            if (view is CodeEditor editor) editor.Detach();
            else if (view is ScreenEditorView screen) screen.Detach();
            _editorLayer.Children.Remove(view);
            _editors.Remove(doc);
        }
    }

    private void ShowActive()
    {
        if (_suspended) return;
        var active = _vm?.ActiveDocument;
        if (active != null && !_editors.ContainsKey(active))
        {
            FrameworkElement view = active.IsScreen ? new ScreenEditorView(active, _vm!) : new CodeEditor(active, _vm!);
            _editors[active] = view;
            _editorLayer.Children.Add(view);
        }
        foreach (var (doc, view) in _editors)
        {
            bool visible = doc == active;
            if (!visible)
            {
                if (view is CodeEditor editor) editor.ClosePopups();
                else if (view is ScreenEditorView screen) screen.ClosePopups();
            }
            view.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
        ActiveEditor?.ReportCaret();
        if (FindBar.IsOpen) FindBar.Attach(ActiveEditor);
    }

    public void FocusActive()
    {
        if (_suspended) return;
        if (ActiveView is ScreenEditorView screen)
        {
            screen.FocusActive();
            return;
        }
        var editor = ActiveEditor;
        if (editor == null) return;
        Dispatcher.BeginInvoke(() =>
        {
            editor.Focus();
            editor.TextArea.Focus();
        }, DispatcherPriority.Input);
    }
}
