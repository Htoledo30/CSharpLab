using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace CSharpLab.Ui;

/// <summary>Recuo de cada nível da árvore, mantendo a seleção em largura total.</summary>
public sealed class TreeIndentConverter : IValueConverter
{
    public double Indent { get; set; } = 12;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        int level = 0;
        if (value is DependencyObject item)
        {
            var parent = VisualTreeHelper.GetParent(item);
            while (parent != null && parent is not TreeView)
            {
                if (parent is TreeViewItem) level++;
                parent = VisualTreeHelper.GetParent(parent);
            }
        }
        return new Thickness(4 + level * Indent, 0, 4, 0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value == null || value is string { Length: 0 } ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
