using System.Windows.Input;

namespace CSharpLab.GameEngine;

/// <summary>Teclas simples de botão, com os mesmos nomes no arquivo, no código e no editor.</summary>
internal static class ButtonShortcuts
{
    public static IReadOnlyList<(string Key, string Label)> Options { get; } =
    [
        ("Space", "Espaço"), ("Enter", "Enter"), ("Escape", "Esc"),
        ("Up", "Seta ↑"), ("Down", "Seta ↓"), ("Left", "Seta ←"), ("Right", "Seta →"),
        .. Enumerable.Range('A', 26).Select(c => (((char)c).ToString(), ((char)c).ToString())),
        .. Enumerable.Range(0, 10).Select(n => ($"D{n}", n.ToString(System.Globalization.CultureInfo.InvariantCulture))),
    ];

    public static bool TryNormalize(string? value, out string? key)
    {
        value = value?.Trim();
        key = null;
        if (string.IsNullOrEmpty(value)) return true;
        key = Options.FirstOrDefault(o => string.Equals(o.Key, value, StringComparison.OrdinalIgnoreCase)).Key;
        return key != null;
    }

    public static string? FromKey(Key key) => key switch
    {
        Key.Enter => "Enter",
        >= Key.NumPad0 and <= Key.NumPad9 => $"D{key - Key.NumPad0}",
        _ => TryNormalize(key.ToString(), out var name) ? name : null,
    };

    public const string Help = "Use Space (Espaço), Enter, Escape, Up, Down, Left, Right, uma letra de A a Z ou D0 a D9. Vazio tira o atalho.";
}
