using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace CSharpLab.GameEngine;

internal sealed partial class GameWindow
{
    private sealed record InputDraft(string Text, int Start, int Length);

    private readonly Dictionary<object, TextBox> _inputControls = [];
    private readonly Dictionary<object, InputDraft> _inputDrafts = [];
    private object? _inputVisit;
    private object? _preferredInput;
    private int _inputRedraw;

    private void BeginInputRedraw(Screen screen)
    {
        var visit = screen.Visit ?? (object?)screen.Designed ?? screen.SceneName;
        _inputDrafts.Clear();
        if (Equals(visit, _inputVisit))
        {
            foreach (var (key, box) in _inputControls)
                _inputDrafts[key] = new InputDraft(box.Text, box.SelectionStart, box.SelectionLength);
        }
        else
        {
            _preferredInput = null;
        }
        _inputVisit = visit;
        _inputControls.Clear();
        _inputRedraw++;
    }

    private void RegisterInput(object key, TextBox box)
    {
        if (_inputDrafts.TryGetValue(key, out var draft))
        {
            box.Text = draft.Text;
            box.Select(draft.Start, draft.Length);
        }
        _inputControls[key] = box;
        if (box.IsEnabled) _answerBox ??= box;
        box.GotKeyboardFocus += (_, _) => _preferredInput = key;
    }

    private void RestoreInputFocus()
    {
        var target = _preferredInput != null && _inputControls.TryGetValue(_preferredInput, out var preferred) && preferred.IsEnabled
            ? preferred : _answerBox;
        int redraw = _inputRedraw;
        Dispatcher.BeginInvoke(() =>
        {
            // Um desenho mais novo ou uma pergunta por cima da cena já escolheu outro foco.
            if (redraw != _inputRedraw || HasPrompt) return;
            if (target != null) target.Focus();
            else Keyboard.Focus(this);
        }, DispatcherPriority.Input);
    }

    private void AnswerInput(Piece piece, string answer)
    {
        if (!piece.Enabled || !_game.CanAcceptInput) return;
        if (_inputControls.TryGetValue(piece.RuntimeId, out var box)) box.Clear();
        _game.AnswerPiece(piece, answer);
    }
}
