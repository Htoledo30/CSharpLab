using System.Windows;

namespace CSharpLab.GameEngine;

internal sealed partial class GameWindow
{
    private bool _fullScreen;
    private WindowState _previousWindowState;
    private WindowStyle _previousWindowStyle;
    private ResizeMode _previousResizeMode;
    private Rect _previousBounds;

    private void ToggleFullScreen()
    {
        if (!_fullScreen)
        {
            _previousWindowState = WindowState;
            _previousWindowStyle = WindowStyle;
            _previousResizeMode = ResizeMode;
            _previousBounds = RestoreBounds;
            _fullScreen = true;
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
        }
        else
        {
            WindowState = WindowState.Normal;
            WindowStyle = _previousWindowStyle;
            ResizeMode = _previousResizeMode;
            if (!_previousBounds.IsEmpty)
            {
                Left = _previousBounds.Left;
                Top = _previousBounds.Top;
                Width = _previousBounds.Width;
                Height = _previousBounds.Height;
            }
            WindowState = _previousWindowState;
            _fullScreen = false;
        }
    }
}
