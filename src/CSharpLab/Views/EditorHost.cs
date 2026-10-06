using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CSharpLab.Editor;
using CSharpLab.ViewModels;

namespace CSharpLab.Views;

/// <summary>
/// Mantém um editor por documento aberto (preserva cursor, rolagem e desfazer ao trocar de aba)
/// e mostra apenas o da aba ativa. Os editores são criados na primeira vez que a aba é mostrada.
/// </summary>
public sealed class EditorHost : Grid
{
    private readonly Dictionary<DocumentViewModel, CodeEditor> _editors = [];
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

    public CodeEditor? ActiveEditor => _vm?.ActiveDocument is { } d && _editors.TryGetValue(d, out var e) ? e : null;

    public void Bind(MainViewModel vm)
    {
        _vm = vm;
        vm.Documents.CollectionChanged += OnDocumentsChanged;
        vm.PropertyChanged += OnVmPropertyChanged;
        vm.GoToRequested += (doc, line, column, offset) =>
        {
            ShowActive();
            if (_editors.TryGetValue(doc, out var editor))
            {
                // Depois do layout, para que a rolagem funcione na aba recém-mostrada.
                Dispatcher.BeginInvoke(() => editor.GoTo(line, column, offset), DispatcherPriority.Loaded);
            }
        };
        vm.FocusEditorRequested += FocusActive;
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
            var editor = _editors[doc];
            editor.Detach();
            _editorLayer.Children.Remove(editor);
            _editors.Remove(doc);
        }
    }

    private void ShowActive()
    {
        var active = _vm?.ActiveDocument;
        if (active != null && !_editors.ContainsKey(active))
        {
            var editor = new CodeEditor(active, _vm!);
            _editors[active] = editor;
            _editorLayer.Children.Add(editor);
        }
        foreach (var (doc, editor) in _editors)
        {
            bool visible = doc == active;
            if (!visible) editor.ClosePopups();
            editor.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
        ActiveEditor?.ReportCaret();
        if (FindBar.IsOpen) FindBar.Attach(ActiveEditor);
    }

    public void FocusActive()
    {
        var editor = ActiveEditor;
        if (editor == null) return;
        Dispatcher.BeginInvoke(() =>
        {
            editor.Focus();
            editor.TextArea.Focus();
        }, DispatcherPriority.Input);
    }
}
