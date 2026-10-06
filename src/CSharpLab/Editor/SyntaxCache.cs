using CSharpLab.ViewModels;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CSharpLab.Editor;

/// <summary>
/// Árvore sintática do texto atual do editor, atualizada de forma incremental e só quando
/// alguém precisa dela (colorir uma linha, decidir um fechamento automático, um snippet…).
/// </summary>
public sealed class SyntaxCache
{
    private readonly DocumentViewModel _doc;
    private SyntaxTree? _tree;
    private int _version = -1;

    public SyntaxCache(DocumentViewModel doc) => _doc = doc;

    public CSharpParseOptions Options { get; set; } = CSharpParseOptions.Default.WithDocumentationMode(DocumentationMode.Parse);

    public SyntaxNode Root
    {
        get
        {
            if (_tree == null || _version != _doc.Version)
            {
                var text = _doc.SourceText;
                _tree = _tree == null || !ReferenceEquals(_tree.Options, Options)
                    ? CSharpSyntaxTree.ParseText(text, Options)
                    : _tree.WithChangedText(text);
                _version = _doc.Version;
            }
            return _tree.GetRoot();
        }
    }

    public void SetOptions(CSharpParseOptions? options)
    {
        if (options == null || options.Equals(Options)) return;
        Options = options.WithDocumentationMode(DocumentationMode.Parse);
        _tree = null;
    }
}
