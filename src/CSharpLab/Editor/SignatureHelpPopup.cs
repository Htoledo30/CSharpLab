using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using CSharpLab.Core.Language;
using ICSharpCode.AvalonEdit.Rendering;

namespace CSharpLab.Editor;

/// <summary>Assinatura compacta da chamada atual, com o parâmetro em uso destacado.</summary>
public sealed class SignatureHelpPopup
{
    private readonly CodeEditor _editor;
    private readonly Popup _popup;
    private readonly TextBlock _text;
    private readonly TextBlock _counter;
    private SignatureHelpResult? _result;
    private int _index;
    private CancellationTokenSource? _cts;

    public SignatureHelpPopup(CodeEditor editor)
    {
        _editor = editor;
        _text = new TextBlock
        {
            FontFamily = (FontFamily)Application.Current.FindResource("CodeFont"),
            FontSize = 13,
            Foreground = SyntaxTheme.Foreground,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 720,
        };
        _counter = new TextBlock
        {
            FontSize = 11,
            Foreground = (Brush)Application.Current.FindResource("TextMuted"),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var panel = new DockPanel();
        DockPanel.SetDock(_counter, Dock.Left);
        panel.Children.Add(_counter);
        panel.Children.Add(_text);
        _popup = new Popup
        {
            AllowsTransparency = true,
            Placement = PlacementMode.Relative,
            PlacementTarget = editor.TextArea.TextView,
            StaysOpen = true,
            Focusable = false,
            Child = new Border
            {
                Background = (Brush)Application.Current.FindResource("BgElevated"),
                BorderBrush = (Brush)Application.Current.FindResource("BorderStrong"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 6, 10, 6),
                Child = panel,
            },
        };
    }

    public bool IsOpen => _popup.IsOpen;

    public bool HasOverloads => _result is { Signatures.Count: > 1 };

    public void Close()
    {
        _cts?.Cancel();
        _popup.IsOpen = false;
        _result = null;
    }

    public void Cycle(int direction)
    {
        if (_result == null || _result.Signatures.Count < 2) return;
        _index = (_index + direction + _result.Signatures.Count) % _result.Signatures.Count;
        Render();
    }

    public async void Update()
    {
        var ls = _editor.LanguageServices;
        if (ls == null || !_editor.Doc.IsCSharp)
        {
            Close();
            return;
        }
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        int caret = _editor.CaretOffset;
        var key = _editor.Doc.LanguageKey;
        SignatureHelpResult? result;
        try
        {
            result = await Task.Run(() => ls.GetSignatureHelpAsync(key, caret, cts.Token), cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch
        {
            result = null;
        }
        if (cts.IsCancellationRequested) return;
        if (result == null || caret != _editor.CaretOffset)
        {
            if (result == null) Close();
            return;
        }

        bool sameCall = _result != null && _result.ArgumentListStart == result.ArgumentListStart &&
                        _result.Signatures.Count == result.Signatures.Count;
        _index = sameCall ? _index : result.ActiveSignature;
        _result = result;
        Render();
        Place(result.ArgumentListStart);
        _popup.IsOpen = true;
    }

    private void Render()
    {
        if (_result == null) return;
        var sig = _result.Signatures[Math.Clamp(_index, 0, _result.Signatures.Count - 1)];
        _counter.Text = _result.Signatures.Count > 1 ? $"{_index + 1}/{_result.Signatures.Count}  ▴▾" : "";
        _counter.Visibility = _result.Signatures.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        _text.Inlines.Clear();

        int active = _result.ActiveParameter;
        if (sig.HasParamsArray && active >= sig.Parameters.Count) active = sig.Parameters.Count - 1;
        int pos = 0;
        for (int i = 0; i < sig.Parameters.Count; i++)
        {
            var (start, length) = sig.Parameters[i];
            if (start > pos) _text.Inlines.Add(new Run(sig.Text[pos..start]));
            var run = new Run(sig.Text.Substring(start, length));
            if (i == active)
            {
                run.FontWeight = FontWeights.SemiBold;
                run.Foreground = (Brush)Application.Current.FindResource("Accent");
            }
            _text.Inlines.Add(run);
            pos = start + length;
        }
        if (pos < sig.Text.Length) _text.Inlines.Add(new Run(sig.Text[pos..]));
    }

    private void Place(int offset)
    {
        var textView = _editor.TextArea.TextView;
        var location = _editor.Document.GetLocation(Math.Min(offset, _editor.Document.TextLength));
        var top = textView.GetVisualPosition(new ICSharpCode.AvalonEdit.TextViewPosition(location), VisualYPosition.LineTop) - textView.ScrollOffset;
        _popup.Child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var height = _popup.Child.DesiredSize.Height;
        _popup.HorizontalOffset = Math.Max(0, top.X - 12);
        _popup.VerticalOffset = top.Y - height - 4 < 0 ? top.Y + textView.DefaultLineHeight + 4 : top.Y - height - 4;
    }
}
