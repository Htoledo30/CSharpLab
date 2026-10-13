using System.Windows;
using CSharpLab.GameEngine;

namespace CSharpLab.Screens;

/// <summary>O que o "▶ Ver" do painel mostra no palco: um movimento da peça ou a barra animada.</summary>
public enum PiecePreview { Shake, Flash, Pop, Bar, Spin }

// Mexer na tela sem brigar com ela: travar uma peça (o mouse não pega nem arrasta), esconder uma peça só aqui
// no Estúdio (para alcançar o que está atrás) e a Caixa que leva junto o que está em cima dela.
public sealed partial class ScreenDesignerModel
{
    /// <summary>O painel pediu para ver um movimento no palco (o nome da peça e qual movimento).</summary>
    public event Action<string, PiecePreview>? PreviewRequested;

    /// <summary>Mostra no palco, uma vez, como a peça treme, pisca, faz o Pop ou como a barra anima no jogo.</summary>
    public void Preview(string name, PiecePreview kind)
    {
        if (Layout?.Find(name) is { } piece) PreviewRequested?.Invoke(piece.Name, kind);
    }

    /// <summary>Peças escondidas só no Estúdio (o olho das Camadas). Não vai para o arquivo: o jogo mostra normalmente.</summary>
    private readonly HashSet<string> _hiddenHere = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Travada: o mouse não pega nem arrasta no palco (a peça de cartão segue a trava da Lista dela).</summary>
    internal bool IsLocked(Piece piece) => IsLocked(Layout, piece);

    private static bool IsLocked(ScreenLayout? layout, Piece piece) => piece.Locked || layout?.ListOf(piece)?.Locked == true;

    /// <summary>Escondida só aqui no Estúdio (a peça de cartão some junto com a Lista dela).</summary>
    internal bool IsHiddenHere(Piece piece) =>
        _hiddenHere.Contains(piece.Name) || piece.List != null && _hiddenHere.Contains(piece.List);

    /// <summary>A peça pode ser pega com o mouse no palco (nem travada, nem escondida aqui).</summary>
    internal bool CanGrab(Piece piece) => !IsLocked(piece) && !IsHiddenHere(piece);

    public bool IsHiddenHere(string name) => _hiddenHere.Contains(name);

    /// <summary>O olho das Camadas: esconde (ou mostra de novo) a peça só no Estúdio. Não entra no desfazer.</summary>
    public void SetHiddenHere(string name, bool hidden)
    {
        if (Layout?.Find(name) is not { } piece) return;
        if (hidden ? !_hiddenHere.Add(piece.Name) : !_hiddenHere.Remove(piece.Name)) return;
        // Escondida, sai da seleção: assim as setas e o Del não mexem numa peça que não dá para ver.
        if (hidden && _selected.Any(n => Layout.Find(n) is { } p && IsHiddenHere(p)))
        {
            _selected = _selected.Where(n => Layout.Find(n) is { } p && !IsHiddenHere(p)).ToList();
            SelectionChanged?.Invoke();
        }
        Changed?.Invoke();
    }

    /// <summary>O cadeado: trava ou destrava a peça. Fica gravado na tela ("locked": true); o jogo ignora.</summary>
    public void SetLocked(string name, bool locked)
    {
        if (Layout?.Find(name) is not { } piece || piece.Locked == locked) return;
        EndMerge();
        Edit(piece.Name, p => p.Locked = locked);
        EndMerge();
    }

    /// <summary>
    /// O que uma Caixa leva junto quando é arrastada: as peças soltas que estão inteiras dentro dela e por cima
    /// dela (mais para a frente nas Camadas). Uma caixa dentro de outra leva as dela também. Travadas e
    /// escondidas aqui ficam onde estão.
    /// </summary>
    internal IReadOnlyList<string> Carried(IEnumerable<string> names)
    {
        if (Layout == null) return [];
        var moving = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        var carried = new List<string>();
        var boxes = new Queue<Piece>(Layout.Pieces.Where(p => p.Type == PieceType.Box && p.List == null && moving.Contains(p.Name)));
        while (boxes.Count > 0)
        {
            var box = boxes.Dequeue();
            var area = PlainBounds(box);
            area.Inflate(0.5, 0.5);
            int index = Layout.Pieces.IndexOf(box);
            for (int i = index + 1; i < Layout.Pieces.Count; i++)
            {
                var piece = Layout.Pieces[i];
                if (piece.List != null || moving.Contains(piece.Name) || !CanGrab(piece) || !area.Contains(PlainBounds(piece))) continue;
                moving.Add(piece.Name);
                carried.Add(piece.Name);
                if (piece.Type == PieceType.Box) boxes.Enqueue(piece);
            }
        }
        return carried;
    }

    // ------------------------------------------------------------------ nome sugerido

    /// <summary>
    /// Um nome em inglês tirado do texto da peça ("Ataque Pesado" → HeavyAttack), só enquanto ela ainda tem o
    /// nome automático (Button3, Text1…). Se o nome já é de outra peça, tenta com o tipo no fim (HealthBar).
    /// Null quando não tem o que sugerir.
    /// </summary>
    internal string? SuggestedName(Piece piece)
    {
        if (Layout == null || !IsAutomaticName(piece)) return null;
        if (Core.Language.PieceNames.Suggest(piece.Text) is not { } word) return null;
        foreach (var name in new[] { word, word + piece.Type })
        {
            if (string.Equals(name, piece.Name, StringComparison.OrdinalIgnoreCase)) return null;
            if (ScreenFile.NameProblem(name) == null && Layout.Find(name) == null) return name;
        }
        return null;
    }

    /// <summary>O nome que o Estúdio deu sozinho: o tipo e um número (Text1, Button12).</summary>
    private static bool IsAutomaticName(Piece piece) =>
        piece.Name.StartsWith(piece.Type.ToString(), StringComparison.OrdinalIgnoreCase) &&
        piece.Name.Length > piece.Type.ToString().Length &&
        piece.Name[piece.Type.ToString().Length..].All(char.IsAsciiDigit);

    // ------------------------------------------------------------------ fundo da tela

    public void SetBackgroundColor(GameEngine.Color? color)
    {
        if (Layout == null || Layout.BackgroundColor == color) return;
        var layout = Layout.Clone();
        layout.BackgroundColor = color;
        Commit(layout);
    }

    public void SetBackgroundGradient(GameEngine.Color? color)
    {
        if (Layout == null || Layout.BackgroundGradient == color) return;
        var layout = Layout.Clone();
        layout.BackgroundGradient = color;
        Commit(layout);
    }
}
