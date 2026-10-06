using System.Text.RegularExpressions;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Indentation;

namespace CSharpLab.Editor;

/// <summary>
/// Indentação ao pressionar Enter: mantém o recuo da linha anterior e acrescenta um nível
/// depois de "{" ou de um cabeçalho de controle sem chaves (if, for, while…).
/// "{" digitado no início de uma linha volta ao recuo do cabeçalho (chaves em nova linha).
/// </summary>
public sealed partial class SmartIndentation(int indentSize) : IIndentationStrategy
{
    [GeneratedRegex(@"^\s*(if|else if|for|foreach|while|using|lock|fixed)\b.*\)\s*$|^\s*(else|do)\s*$")]
    private static partial Regex ControlHeader();

    public void IndentLine(TextDocument document, DocumentLine line)
    {
        var previous = PreviousNonBlank(document, line);
        if (previous == null) return;
        var prevText = document.GetText(previous);
        var baseIndent = LeadingWhitespace(prevText);
        var trimmed = prevText.TrimEnd();

        string indent = baseIndent;
        if (trimmed.EndsWith('{') || trimmed.EndsWith('(') || trimmed.EndsWith('[') || ControlHeader().IsMatch(trimmed))
            indent = baseIndent + new string(' ', indentSize);

        var current = document.GetText(line);
        var existing = LeadingWhitespace(current);
        if (current.TrimStart().StartsWith('}') && indent.Length >= indentSize)
            indent = indent[..^indentSize];
        if (existing != indent)
            document.Replace(line.Offset, existing.Length, indent);
    }

    public void IndentLines(TextDocument document, int beginLine, int endLine)
    {
    }

    /// <summary>"{" digitado como primeiro caractere: alinha com a linha do cabeçalho.</summary>
    public void AlignOpeningBrace(TextDocument document, int braceOffset)
    {
        var line = document.GetLineByOffset(braceOffset);
        var before = document.GetText(line.Offset, braceOffset - line.Offset);
        if (before.Trim().Length != 0) return;
        var previous = PreviousNonBlank(document, line);
        if (previous == null) return;
        var prevText = document.GetText(previous);
        var indent = LeadingWhitespace(prevText);
        if (prevText.TrimEnd().EndsWith('{')) indent += new string(' ', indentSize);
        if (before != indent)
            document.Replace(line.Offset, before.Length, indent);
    }

    private static DocumentLine? PreviousNonBlank(TextDocument document, DocumentLine line)
    {
        for (var l = line.PreviousLine; l != null; l = l.PreviousLine)
        {
            if (document.GetText(l).Trim().Length > 0) return l;
        }
        return null;
    }

    private static string LeadingWhitespace(string text)
    {
        int i = 0;
        while (i < text.Length && text[i] is ' ' or '\t') i++;
        return text[..i];
    }
}
