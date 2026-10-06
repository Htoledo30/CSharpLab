using System.Windows.Media;
using CSharpLab.Core.Language;

namespace CSharpLab.Editor;

/// <summary>Cores do código (tema escuro sóbrio).</summary>
public static class SyntaxTheme
{
    public static readonly Brush Background = Freeze("#1E1F22");
    public static readonly Brush Foreground = Freeze("#D7D7DC");
    public static readonly Brush LineNumbers = Freeze("#55565E");
    public static readonly Brush LineNumbersActive = Freeze("#A0A0A8");
    public static readonly Brush CurrentLine = Freeze("#24252A");
    public static readonly Brush Selection = Freeze("#33405F");
    public static readonly Brush BracketMatch = Freeze("#3A3D4A");
    public static readonly Pen BracketPen = FreezePen("#6B6F85", 1);
    public static readonly Brush SearchMatch = Freeze("#4A4426");
    public static readonly Brush SearchCurrent = Freeze("#6A5A1F");
    public static readonly Color ErrorColor = (Color)ColorConverter.ConvertFromString("#F0727C");
    public static readonly Color WarningColor = (Color)ColorConverter.ConvertFromString("#D8B05E");
    public static readonly Brush SnippetField = Freeze("#2C3157");

    private static readonly Brush[] ByKind = new Brush[Enum.GetValues<TokenKind>().Length];

    static SyntaxTheme()
    {
        Set(TokenKind.Plain, "#D7D7DC");
        Set(TokenKind.Keyword, "#7FA7F5");
        Set(TokenKind.ControlKeyword, "#C792EA");
        Set(TokenKind.String, "#E2A46F");
        Set(TokenKind.StringEscape, "#E9C46A");
        Set(TokenKind.Number, "#B5CEA8");
        Set(TokenKind.Comment, "#6E9661");
        Set(TokenKind.XmlDoc, "#6E9661");
        Set(TokenKind.Preprocessor, "#9B9BA3");
        Set(TokenKind.Disabled, "#5C5D64");
        Set(TokenKind.Type, "#4FC1B0");
        Set(TokenKind.Struct, "#7DD3B8");
        Set(TokenKind.Interface, "#A9D8A0");
        Set(TokenKind.Enum, "#7DD3B8");
        Set(TokenKind.TypeParameter, "#7DD3B8");
        Set(TokenKind.Method, "#E6D58C");
        Set(TokenKind.Local, "#A9CFF2");
        Set(TokenKind.Parameter, "#A9CFF2");
        Set(TokenKind.Field, "#9CC8EE");
        Set(TokenKind.Property, "#9CC8EE");
        Set(TokenKind.Constant, "#82C8E8");
        Set(TokenKind.EnumMember, "#82C8E8");
        Set(TokenKind.Namespace, "#D7D7DC");
        Set(TokenKind.Label, "#D7D7DC");
    }

    private static void Set(TokenKind kind, string color) => ByKind[(int)kind] = Freeze(color);

    public static Brush For(TokenKind kind) => ByKind[(int)kind];

    public static Brush Freeze(string color)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        b.Freeze();
        return b;
    }

    private static Pen FreezePen(string color, double thickness)
    {
        var p = new Pen(Freeze(color), thickness);
        p.Freeze();
        return p;
    }
}
