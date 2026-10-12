using System.Text.RegularExpressions;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Indentation;

namespace CSharpLab.Editor;

/// <summary>
/// Indentação ao pressionar Enter: mantém o recuo da linha anterior e acrescenta um nível
/// depois de "{" ou de um cabeçalho de controle sem chaves (if, for, while…).
/// "{" digitado no início de uma linha volta ao recuo do cabeçalho (chaves em nova linha).
/// Enter numa linha vazia mantém o recuo dela e não deixa espaços sobrando para trás.
/// </summary>
public sealed partial class SmartIndentation(int indentSize) : IIndentationStrategy
{
    public int IndentSize { get; set; } = indentSize;
    public int TabSize { get; set; } = indentSize;
    public bool UseTabs { get; set; }

    public string IncreaseIndent(string indent) => UseTabs
        ? CreateIndent(Columns(indent) + IndentSize)
        : indent + new string(' ', IndentSize);

    private int Columns(string indent)
    {
        int column = 0;
        foreach (var c in indent)
            column = c == '\t' ? column + TabSize - column % TabSize : column + 1;
        return column;
    }

    private string CreateIndent(int columns) => UseTabs
        ? new string('\t', columns / TabSize) + new string(' ', columns % TabSize)
        : new string(' ', columns);
    [GeneratedRegex(@"^\s*(if|else if|for|foreach|while|using|lock|fixed)\b.*\)\s*$|^\s*(else|do)\s*$")]
    private static partial Regex ControlHeader();

    public void IndentLine(TextDocument document, DocumentLine line)
    {
        // Enter numa linha só com recuo: a linha nova fica no mesmo recuo (o que a pessoa escolheu,
        // mesmo que tenha voltado um nível) e a de cima não guarda espaços sobrando.
        var above = line.PreviousLine;
        if (above != null && above.Length > 0 && document.GetText(above).Trim().Length == 0)
        {
            var kept = document.GetText(above);
            document.Remove(above.Offset, above.Length);
            var currentText = document.GetText(line);
            if (currentText.Trim().Length == 0)
            {
                document.Replace(line.Offset, LeadingWhitespace(currentText).Length, kept);
                return;
            }
        }

        var previous = PreviousNonBlank(document, line);
        if (previous == null) return;
        var prevText = document.GetText(previous);
        var baseIndent = LeadingWhitespace(prevText);
        var trimmed = prevText.TrimEnd();

        string indent = baseIndent;
        if (trimmed.EndsWith('{') || trimmed.EndsWith('(') || trimmed.EndsWith('[') || ControlHeader().IsMatch(trimmed))
            indent = IncreaseIndent(baseIndent);

        var current = document.GetText(line);
        var existing = LeadingWhitespace(current);
        if (current.TrimStart().StartsWith('}') && indent.Length > 0)
            indent = CreateIndent(Math.Max(0, Columns(indent) - IndentSize));
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
        if (prevText.TrimEnd().EndsWith('{')) indent = IncreaseIndent(indent);
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
