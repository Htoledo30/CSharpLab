using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;

namespace CSharpLab.Editor;

/// <summary>
/// Lista de sugestões junto ao cursor. Filtra e ordena pelo texto digitado (igual &gt; começa com &gt;
/// iniciais em CamelCase &gt; contém), navega com as setas e aceita só com Tab ou Enter.
/// </summary>
public sealed class CompletionPopup : TextAreaStackedInputHandler
{
    private const int MaxVisibleRows = 10;
    private const double RowHeight = 24;

    private readonly CodeEditor _editor;
    private readonly Popup _popup;
    private readonly ListBox _list;
    private readonly TextBlock _detail;
    private readonly Border _detailBorder;
    private List<CompletionEntry> _all = [];
    private int _start;
    private bool _attached;

    public CompletionPopup(CodeEditor editor) : base(editor.TextArea)
    {
        _editor = editor;
        _list = new ListBox
        {
            Focusable = false,
            MaxHeight = MaxVisibleRows * RowHeight + 4,
            Width = 380,
            ItemTemplate = (DataTemplate)Application.Current.FindResource("CompletionItemTemplate"),
            ItemContainerStyle = (Style)Application.Current.FindResource("CompletionItemStyle"),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        VirtualizingPanel.SetIsVirtualizing(_list, true);
        VirtualizingPanel.SetVirtualizationMode(_list, VirtualizationMode.Recycling);
        _list.SelectionChanged += (_, _) => UpdateDetail();
        _list.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (e.ClickCount >= 1 && _list.SelectedItem is CompletionEntry entry && ItemUnderMouse(e) == entry)
            {
                Commit(entry);
                e.Handled = true;
            }
        };

        _detail = new TextBlock
        {
            FontFamily = (FontFamily)Application.Current.FindResource("CodeFont"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.FindResource("TextSecondary"),
        };
        _detailBorder = new Border
        {
            BorderBrush = (Brush)Application.Current.FindResource("BorderStrong"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(8, 6, 8, 5),
            Child = _detail,
            Visibility = Visibility.Collapsed,
            MaxWidth = 380,
        };
        var stack = new StackPanel();
        stack.Children.Add(_list);
        stack.Children.Add(_detailBorder);

        var border = new Border
        {
            Background = (Brush)Application.Current.FindResource("BgElevated"),
            BorderBrush = (Brush)Application.Current.FindResource("BorderStrong"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(3),
            Margin = new Thickness(0, 0, 8, 8),
            Child = stack,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.45, Color = Colors.Black },
        };
        _popup = new Popup
        {
            AllowsTransparency = true,
            Placement = PlacementMode.Relative,
            PlacementTarget = editor.TextArea.TextView,
            StaysOpen = true,
            Focusable = false,
            Child = border,
        };
    }

    public bool IsOpen => _popup.IsOpen;

    public int StartOffset => _start;

    private static CompletionEntry? ItemUnderMouse(MouseEventArgs e)
    {
        var d = e.OriginalSource as DependencyObject;
        while (d != null && d is not ListBoxItem) d = VisualTreeHelper.GetParent(d);
        return (d as ListBoxItem)?.DataContext as CompletionEntry;
    }

    public void Show(List<CompletionEntry> entries, int startOffset)
    {
        _all = entries;
        _start = startOffset;
        if (!Refilter()) return;
        Place();
        _popup.IsOpen = true;
        if (!_attached)
        {
            TextArea.PushStackedInputHandler(this);
            _attached = true;
        }
    }

    public void Close()
    {
        _popup.IsOpen = false;
        _list.ItemsSource = null;
        _all = [];
        if (_attached)
        {
            TextArea.PopStackedInputHandler(this);
            _attached = false;
        }
    }

    /// <summary>Chamado quando o texto ou o cursor mudam com a lista aberta.</summary>
    public void Update()
    {
        if (!IsOpen) return;
        if (!Refilter()) Close();
        else Place();
    }

    private string? CurrentQuery()
    {
        var caret = _editor.CaretOffset;
        var doc = _editor.Document;
        if (caret < _start || _start > doc.TextLength) return null;
        var query = doc.GetText(_start, caret - _start);
        foreach (var c in query)
        {
            if (!CompletionController.IsIdentifierChar(c) && c != '@') return null;
        }
        return query;
    }

    private bool Refilter()
    {
        var query = CurrentQuery();
        if (query == null) return false;
        var ranked = Rank(_all, query);
        if (ranked.Count == 0) return false;
        _list.ItemsSource = ranked;
        _list.SelectedIndex = 0;
        _list.ScrollIntoView(ranked[0]);
        return true;
    }

    /// <summary>Ordena: igual &gt; começa com &gt; iniciais &gt; contém; depois prioridade e ordem do Roslyn.</summary>
    public static List<CompletionEntry> Rank(IReadOnlyList<CompletionEntry> items, string query)
    {
        if (query.Length == 0) return items.ToList();
        var scored = new List<(CompletionEntry Item, int Tier, int Index)>(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            int tier = Score(items[i].Text, query);
            if (tier > 0) scored.Add((items[i], tier, i));
        }
        return scored
            .OrderByDescending(s => s.Tier)
            .ThenByDescending(s => s.Item.Priority)
            .ThenBy(s => s.Index)
            .Select(s => s.Item)
            .ToList();
    }

    private static int Score(string text, string query)
    {
        if (text == query) return 7;
        if (text.Equals(query, StringComparison.OrdinalIgnoreCase)) return 6;
        if (text.StartsWith(query, StringComparison.Ordinal)) return 5;
        if (text.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 4;
        if (CamelCaseMatch(text, query)) return 3;
        if (query.Length >= 3 && text.Contains(query, StringComparison.OrdinalIgnoreCase)) return 1;
        return 0;
    }

    /// <summary>"wl" encontra "WriteLine"; "rl" encontra "ReadLine".</summary>
    private static bool CamelCaseMatch(string text, string query)
    {
        if (query.Length < 2) return false;
        int q = 0;
        for (int i = 0; i < text.Length && q < query.Length; i++)
        {
            bool wordStart = i == 0 || char.IsUpper(text[i]) && !char.IsUpper(text[i - 1]) || text[i - 1] == '_';
            if (wordStart && char.ToLowerInvariant(text[i]) == char.ToLowerInvariant(query[q])) q++;
        }
        return q == query.Length;
    }

    private void UpdateDetail()
    {
        if (_list.SelectedItem is not CompletionEntry entry)
        {
            _detailBorder.Visibility = Visibility.Collapsed;
            return;
        }
        _detail.Text = entry.Detail ?? "…";
        _detailBorder.Visibility = Visibility.Visible;
        entry.DetailLoaded = e =>
        {
            if (_list.SelectedItem == e) _detail.Text = e.Detail ?? "";
        };
    }

    private void Place()
    {
        var textView = TextArea.TextView;
        var location = _editor.Document.GetLocation(Math.Min(_start, _editor.Document.TextLength));
        var pos = new ICSharpCode.AvalonEdit.TextViewPosition(location);
        var top = textView.GetVisualPosition(pos, VisualYPosition.LineTop) - textView.ScrollOffset;
        var bottom = textView.GetVisualPosition(pos, VisualYPosition.LineBottom) - textView.ScrollOffset;
        _popup.Child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double height = _popup.Child.DesiredSize.Height;
        double x = Math.Max(0, top.X - 30);
        // Abaixo da linha; se não couber, acima.
        double y = bottom.Y + 2 + height > textView.ActualHeight && top.Y - height - 2 > 0 ? top.Y - height - 2 : bottom.Y + 2;
        _popup.HorizontalOffset = x;
        _popup.VerticalOffset = y;
    }

    public override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!IsOpen) return;
        int count = _list.Items.Count;
        switch (e.Key)
        {
            case Key.Down:
                Move(1);
                e.Handled = true;
                break;
            case Key.Up:
                Move(-1);
                e.Handled = true;
                break;
            case Key.PageDown:
                Move(MaxVisibleRows - 1);
                e.Handled = true;
                break;
            case Key.PageUp:
                Move(-(MaxVisibleRows - 1));
                e.Handled = true;
                break;
            case Key.Tab when Keyboard.Modifiers == ModifierKeys.None:
            case Key.Enter when Keyboard.Modifiers == ModifierKeys.None:
                if (_list.SelectedItem is CompletionEntry entry)
                {
                    Commit(entry);
                    e.Handled = true;
                }
                break;
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
        }

        void Move(int delta)
        {
            if (count == 0) return;
            int index = Math.Clamp(_list.SelectedIndex + delta, 0, count - 1);
            _list.SelectedIndex = index;
            _list.ScrollIntoView(_list.SelectedItem);
        }
    }

    private void Commit(CompletionEntry entry)
    {
        int start = _start;
        int end = _editor.CaretOffset;
        Close();
        _editor.CompletionController.Commit(entry, start, end - start);
    }
}
