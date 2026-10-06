using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CSharpLab.Core.Files;
using CSharpLab.Editor;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace CSharpLab.Views;

/// <summary>Busca e substituição simples no arquivo ativo.</summary>
public partial class FindReplaceBar : UserControl
{
    private const int MaxMatches = 10_000;
    private CodeEditor? _editor;
    private readonly DispatcherTimer _refresh;

    public FindReplaceBar()
    {
        InitializeComponent();
        Visibility = Visibility.Collapsed;
        _refresh = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(120) };
        _refresh.Tick += (_, _) =>
        {
            _refresh.Stop();
            Recompute(keepPosition: true);
        };
        ReplaceBox.TextChanged += (_, _) => ReplaceHint.Visibility = ReplaceBox.IsVisible && ReplaceBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public bool IsOpen => Visibility == Visibility.Visible;

    public void Open(CodeEditor editor, bool replace)
    {
        Attach(editor);
        var selection = editor.TextArea.Selection;
        if (!selection.IsEmpty && !selection.IsMultiline)
            FindBox.Text = selection.GetText();
        ReplaceToggle.IsChecked = replace || ReplaceToggle.IsChecked == true;
        Visibility = Visibility.Visible;
        Recompute(keepPosition: false);
        FindBox.Focus();
        FindBox.SelectAll();
    }

    /// <summary>Troca de aba com a barra aberta: passa a buscar no novo editor.</summary>
    public void Attach(CodeEditor? editor)
    {
        if (_editor == editor) return;
        if (_editor != null)
        {
            _editor.Document.Changed -= OnDocumentChanged;
            _editor.Search.Matches.Clear();
            _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
        }
        _editor = editor;
        if (_editor != null)
        {
            _editor.Document.Changed += OnDocumentChanged;
            if (IsOpen) Recompute(keepPosition: true);
        }
    }

    public void Close()
    {
        Visibility = Visibility.Collapsed;
        if (_editor != null)
        {
            _editor.Search.Matches.Clear();
            _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
            _editor.TextArea.Focus();
        }
    }

    private void OnDocumentChanged(object? sender, DocumentChangeEventArgs e)
    {
        if (!IsOpen) return;
        _refresh.Stop();
        _refresh.Start();
    }

    private void Recompute(bool keepPosition)
    {
        if (_editor == null) return;
        var search = _editor.Search;
        search.Matches.Clear();
        search.Current = -1;
        var query = FindBox.Text;
        if (query.Length > 0)
        {
            // O destaque mostra até MaxMatches; "Substituir tudo" busca de novo sem limite.
            search.Matches.AddRange(TextOccurrences.FindAll(_editor.Document.Text, query, MatchCase.IsChecked == true, MaxMatches));
            int caret = keepPosition && !_editor.TextArea.Selection.IsEmpty ? _editor.SelectionStart : _editor.CaretOffset;
            search.Current = search.Matches.FindIndex(m => m.Start >= caret);
            if (search.Current < 0 && search.Matches.Count > 0) search.Current = 0;
        }
        UpdateCount();
        _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
    }

    private void UpdateCount()
    {
        var search = _editor?.Search;
        if (search == null || FindBox.Text.Length == 0) CountText.Text = "";
        else if (search.Matches.Count == 0) CountText.Text = "Nenhum";
        else CountText.Text = $"{search.Current + 1} de {search.Matches.Count}{(search.Matches.Count >= MaxMatches ? "+" : "")}";
    }

    private void Move(int direction)
    {
        if (_editor == null) return;
        var search = _editor.Search;
        if (search.Matches.Count == 0) return;

        // Se a ocorrência atual já está selecionada, avança; senão, seleciona a atual.
        var current = search.Current >= 0 ? search.Matches[search.Current] : (-1, 0);
        bool selected = _editor.SelectionStart == current.Item1 && _editor.SelectionLength == current.Item2;
        if (selected || search.Current < 0)
            search.Current = (search.Current + direction + search.Matches.Count) % search.Matches.Count;
        Select(search.Matches[search.Current]);
        UpdateCount();
    }

    private void Select((int Start, int Length) match)
    {
        if (_editor == null) return;
        _editor.Select(match.Start, match.Length);
        var loc = _editor.Document.GetLocation(match.Start);
        _editor.ScrollTo(loc.Line, loc.Column);
        _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
    }

    private void OnFindTextChanged(object sender, TextChangedEventArgs e)
    {
        Recompute(keepPosition: false);
        if (_editor?.Search is { Current: >= 0 } s) Select(s.Matches[s.Current]);
    }

    private void OnOptionChanged(object sender, RoutedEventArgs e) => Recompute(keepPosition: true);

    private void OnReplaceToggled(object sender, RoutedEventArgs e)
    {
        bool on = ReplaceToggle.IsChecked == true;
        ReplaceBox.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        ReplaceButtons.Visibility = ReplaceBox.Visibility;
        ReplaceHint.Visibility = on && ReplaceBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ReplaceToggle.Content = FindResource(on ? "IconChevronDown" : "IconChevronRight");
    }

    private void OnFindKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Move(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void OnReplaceKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ReplaceCurrent();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void OnNext(object sender, RoutedEventArgs e) => Move(1);
    private void OnPrevious(object sender, RoutedEventArgs e) => Move(-1);
    private void OnClose(object sender, RoutedEventArgs e) => Close();
    private void OnReplace(object sender, RoutedEventArgs e) => ReplaceCurrent();

    private void ReplaceCurrent()
    {
        if (_editor == null) return;
        var search = _editor.Search;
        if (search.Matches.Count == 0) return;
        var (start, length) = search.Matches[Math.Max(0, search.Current)];
        if (_editor.SelectionStart != start || _editor.SelectionLength != length)
        {
            Select((start, length));
            return;
        }
        _editor.Document.Replace(start, length, ReplaceBox.Text);
        _editor.CaretOffset = start + ReplaceBox.Text.Length;
        Recompute(keepPosition: false);
        if (search.Current >= 0) Select(search.Matches[search.Current]);
        UpdateCount();
    }

    private void OnReplaceAll(object sender, RoutedEventArgs e)
    {
        if (_editor == null) return;
        var matches = TextOccurrences.FindAll(_editor.Document.Text, FindBox.Text, MatchCase.IsChecked == true);
        if (matches.Count == 0) return;
        var replacement = ReplaceBox.Text;
        using (_editor.Document.RunUpdate())
        {
            for (int i = matches.Count - 1; i >= 0; i--)
                _editor.Document.Replace(matches[i].Start, matches[i].Length, replacement);
        }
        Recompute(keepPosition: false);
        CountText.Text = $"{matches.Count} substituída{(matches.Count == 1 ? "" : "s")}";
    }
}
