using System.Windows;
using System.Windows.Threading;
using CSharpLab.Core.Language;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using Microsoft.CodeAnalysis.Text;

namespace CSharpLab.Editor;

/// <summary>
/// Formatação automática, como no Visual Studio: ao digitar ";" (a instrução) ou "}" (o bloco) e ao colar
/// várias linhas (o trecho colado ganha o recuo de onde entrou). Cada formatação é um passo de desfazer
/// próprio: Ctrl+Z logo depois devolve o texto como foi digitado ou colado.
/// </summary>
public sealed partial class CodeEditor
{
    private SpanFormatter? _formatter;
    private (int Start, int End)? _pasted;

    // Chaves faltando bagunçam a estrutura toda; aí o formatador erraria o recuo em vez de ajudar.
    private static readonly HashSet<string> BrokenStructure = ["CS1513", "CS1514", "CS1022"];

    /// <summary>A formatação automática está pronta (o Roslyn carregou as opções deste arquivo).</summary>
    internal bool CanFormat => _formatter != null;

    private void InitializeFormat()
    {
        DataObject.AddPastingHandler(TextArea, OnPasting);
    }

    private async void LoadFormatter(LanguageService ls, string key, CancellationToken ct)
    {
        _formatter = null;
        try
        {
            var formatter = await Task.Run(() => ls.GetSpanFormatterAsync(key, ct), ct);
            if (!ct.IsCancellationRequested && !IsDetached && key == Doc.LanguageKey) _formatter = formatter;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Core.Settings.AppPaths.Log(ex, "Preparando a formatação automática"); }
    }

    /// <summary>";" ou "}" digitado em <paramref name="offset"/>: formata a instrução ou o bloco. False se não formatou.</summary>
    private bool FormatAfterTyping(int offset)
    {
        if (_formatter == null || !Doc.IsCSharp) return false;
        TextSpan? span;
        try
        {
            span = SpanFormatter.SpanToFormatAfter(Syntax.Root, offset);
        }
        catch
        {
            return false;
        }
        if (span is not { } s) return false;
        // Desde o começo da linha: o recuo dela também entra na formatação.
        var line = Document.GetLineByOffset(s.Start);
        int start = Document.GetText(line.Offset, s.Start - line.Offset).Trim().Length == 0 ? line.Offset : s.Start;
        return ApplyFormat(TextSpan.FromBounds(start, s.End));
    }

    private bool ApplyFormat(TextSpan span)
    {
        if (_formatter == null) return false;
        CloseEditGroup();
        IReadOnlyList<Microsoft.CodeAnalysis.Text.TextChange> changes;
        try
        {
            var root = Syntax.Root;
            if (root.GetDiagnostics().Any(d => BrokenStructure.Contains(d.Id))) return false;
            changes = _formatter.Format(root, span, TextUtilities.GetNewLineFromDocument(Document, 1));
        }
        catch (Exception ex)
        {
            Core.Settings.AppPaths.Log(ex, "Formatação automática");
            return false;
        }
        if (changes.Count == 0) return true;
        // De trás para frente, para as posições continuarem valendo; tudo num passo só de desfazer.
        using (Document.RunUpdate())
        {
            foreach (var change in changes.OrderByDescending(c => c.Span.Start))
                Document.Replace(change.Span.Start, change.Span.Length, change.NewText ?? "");
        }
        return true;
    }

    // ================================================================ colar

    /// <summary>O AvalonEdit avisa antes de colar: anota onde o texto entra para formatar logo depois.</summary>
    private void OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (_formatter == null || !Doc.IsCSharp || e.DataObject.GetDataPresent(RectangleSelection.RectangularSelectionDataType)) return;
        _pasted = null;
        Document.Changed += OnPasteChanged;
        Dispatcher.BeginInvoke(FinishPaste, DispatcherPriority.Normal);
    }

    private void OnPasteChanged(object? sender, DocumentChangeEventArgs e)
    {
        if (e.InsertionLength == 0) return;
        int start = e.Offset, end = e.Offset + e.InsertionLength;
        _pasted = _pasted is { } p ? (Math.Min(p.Start, start), Math.Max(p.End, end)) : (start, end);
    }

    /// <summary>Colou várias linhas: formata as linhas coladas inteiras (o recuo passa a ser o de onde entraram).</summary>
    private void FinishPaste()
    {
        Document.Changed -= OnPasteChanged;
        if (_pasted is not { } pasted || IsDetached) return;
        _pasted = null;
        int end = Math.Min(pasted.End, Document.TextLength);
        if (pasted.Start >= end || Document.GetText(pasted.Start, end - pasted.Start).IndexOf('\n') < 0) return;
        var first = Document.GetLineByOffset(pasted.Start);
        var last = Document.GetLineByOffset(end);
        ApplyFormat(TextSpan.FromBounds(first.Offset, last.EndOffset));
    }
}
