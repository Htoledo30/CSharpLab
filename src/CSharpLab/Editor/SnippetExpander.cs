using CSharpLab.Core.Language;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Snippets;

namespace CSharpLab.Editor;

/// <summary>Expande snippets com campos editáveis (Tab avança, Enter/Esc conclui) numa única operação de desfazer.</summary>
public static class SnippetExpander
{
    public static Snippet Build(SnippetDefinition definition)
    {
        var snippet = new Snippet();
        var fields = new Dictionary<int, SnippetReplaceableTextElement>();
        foreach (var part in Snippets.Parse(definition.Template))
        {
            switch (part)
            {
                case Snippets.TextPart t:
                    snippet.Elements.Add(new SnippetTextElement { Text = t.Text });
                    break;
                case Snippets.FieldPart f when fields.TryGetValue(f.Index, out var target):
                    snippet.Elements.Add(new SnippetBoundElement { TargetElement = target });
                    break;
                case Snippets.FieldPart f:
                    var element = new SnippetReplaceableTextElement { Text = f.Default };
                    fields[f.Index] = element;
                    snippet.Elements.Add(element);
                    break;
                case Snippets.CaretPart:
                    snippet.Elements.Add(new SnippetCaretElement());
                    break;
            }
        }
        return snippet;
    }

    /// <summary>Substitui o atalho digitado (wordStart..wordStart+wordLength) pela estrutura.</summary>
    public static void Expand(TextArea textArea, SnippetDefinition definition, int wordStart, int wordLength)
    {
        var document = textArea.Document;
        var snippet = Build(definition);
        using (document.RunUpdate())
        {
            if (wordLength > 0) document.Remove(wordStart, wordLength);
            textArea.ClearSelection();
            textArea.Caret.Offset = wordStart;
            snippet.Insert(textArea);
        }
    }
}
