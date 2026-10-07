using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using CSharpLab.ViewModels;

namespace CSharpLab.Views;

public partial class ExplorerView : UserControl
{
    public ExplorerView() => InitializeComponent();

    private MainViewModel? Vm => DataContext as MainViewModel;

    /// <summary>A lista de exemplos, como em Arquivo → Exemplos para estudar.</summary>
    private void OnExamplesClick(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        var menu = new ContextMenu { PlacementTarget = ExamplesButton, Placement = PlacementMode.Bottom };
        foreach (var example in vm.ExampleProjects)
        {
            var header = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
            header.Children.Add(new TextBlock { Text = example.Title });
            var description = new TextBlock { Text = example.Description, FontSize = 11, MaxWidth = 340, TextWrapping = TextWrapping.Wrap };
            description.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
            header.Children.Add(description);
            var item = new MenuItem { Header = header };
            var chosen = example;
            item.Click += (_, _) => vm.OpenExampleCommand.Execute(chosen);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (Vm != null) Vm.Explorer.Selected = e.NewValue as ExplorerNode;
    }

    private static T? FindParent<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null && d is not T)
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as T;
    }

    private void OnTreeMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && FindParent<ToggleButton>(d) != null) return;
        if (e.OriginalSource is DependencyObject src && FindParent<TextBox>(src) != null) return;
        var item = e.OriginalSource is DependencyObject o ? FindParent<TreeViewItem>(o) : null;
        if (item?.DataContext is ExplorerNode node && Vm != null)
        {
            Vm.Explorer.Open(node);
        }
    }

    private void OnTreeRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = e.OriginalSource is DependencyObject o ? FindParent<TreeViewItem>(o) : null;
        if (item != null)
        {
            item.IsSelected = true;
            item.Focus();
        }
        else if (Vm?.Explorer.Selected is { } selected)
        {
            // Clique na área vazia: ações valem para a pasta raiz.
            selected.IsSelected = false;
            Vm.Explorer.Selected = null;
        }
    }

    private void OnTreeKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm == null || e.OriginalSource is TextBox) return;
        var node = Vm.Explorer.Selected;
        switch (e.Key)
        {
            case Key.F2:
                Vm.Explorer.RenameCommand.Execute(node);
                e.Handled = true;
                break;
            case Key.Delete:
                Vm.Explorer.DeleteCommand.Execute(node);
                e.Handled = true;
                break;
            case Key.Enter when node != null:
                Vm.Explorer.Open(node);
                e.Handled = true;
                break;
        }
    }

    private void OnEditBoxLoaded(object sender, RoutedEventArgs e) => FocusEditBox((TextBox)sender);

    private void OnEditBoxVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => FocusEditBox((TextBox)sender);

    private static void FocusEditBox(TextBox box)
    {
        if (!box.IsVisible) return;
        box.Dispatcher.BeginInvoke(() =>
        {
            box.Focus();
            Keyboard.Focus(box);
            // Seleciona só o nome, sem a extensão.
            var text = box.Text;
            var dot = text.LastIndexOf('.');
            if (dot > 0) box.Select(0, dot);
            else if (dot == 0) box.CaretIndex = 0;
            else box.SelectAll();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void OnEditBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { Tag: ExplorerNode node } || Vm == null) return;
        if (e.Key == Key.Enter)
        {
            // Arquivo novo abre no editor e recebe o foco; nos demais casos o foco volta à árvore.
            bool newFile = node.IsNew && !node.IsDirectory;
            if (Vm.Explorer.CommitEdit(node) && !newFile) Tree.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Vm.Explorer.CancelEdit(node);
            Tree.Focus();
            e.Handled = true;
        }
    }

    private void OnEditBoxLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox { Tag: ExplorerNode node } || Vm == null || !node.IsEditing) return;
        if (!Vm.Explorer.CommitEdit(node)) Vm.Explorer.CancelEdit(node);
    }
}
