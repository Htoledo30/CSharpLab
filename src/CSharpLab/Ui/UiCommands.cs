using System.Windows;
using CommunityToolkit.Mvvm.Input;

namespace CSharpLab.Ui;

public static class UiCommands
{
    public static readonly RelayCommand<string?> CopyText = new(text =>
    {
        if (string.IsNullOrEmpty(text)) return;
        try { Clipboard.SetText(text); } catch { }
    });
}
