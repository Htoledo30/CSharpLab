using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using CSharpLab.Core.Language;
using ICSharpCode.AvalonEdit.Document;
using Microsoft.CodeAnalysis.CSharp;

namespace CSharpLab.Editor;

/// <summary>
/// Ajudas do editor: dica ao passar o mouse, ir para a definição, renomear, adicionar using
/// e edição por linhas (comentar, duplicar, mover, apagar).
/// </summary>
public sealed partial class CodeEditor
{
    private CancellationTokenSource? _hoverCts;
    private readonly ContextMenu _menu = new();
    private MenuItem? _goToItem, _renameItem, _fixItem;

    private static readonly HashSet<string> MissingNameErrors = ["CS0246", "CS0103"];

    private void InitializeAssist()
    {
        TextArea.PreviewKeyDown += OnAssistKeyDown;
        TextArea.PreviewMouseLeftButtonDown += OnAssistMouseDown;
        TextArea.PreviewMouseRightButtonDown += OnRightButtonDown;
        BuildContextMenu();
    }

    // ================================================================ teclado

    private void OnAssistKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        switch (key)
        {
            case Key.F12 when mods == ModifierKeys.None && Doc.IsCSharp:
                _ = GoToDefinitionAsync();
                e.Handled = true;
                break;
            case Key.F2 when mods == ModifierKeys.None && Doc.IsCSharp:
                _ = RenameSymbolAsync();
                e.Handled = true;
                break;
            case Key.OemPeriod when mods == ModifierKeys.Control && Doc.IsCSharp:
                _ = ShowQuickFixesAsync();
                e.Handled = true;
                break;
            // Ctrl+/ (teclado americano) e Ctrl+; ou a tecla "/" do ABNT2.
            case Key.OemQuestion or Key.AbntC1 or Key.Divide or Key.OemSemicolon when mods == ModifierKeys.Control:
                ToggleComment();
                e.Handled = true;
                break;
            case Key.D when mods == ModifierKeys.Control:
                DuplicateLines();
                e.Handled = true;
                break;
            case Key.K when mods == (ModifierKeys.Control | ModifierKeys.Shift):
                DeleteLines();
                e.Handled = true;
                break;
            case Key.Up when mods == ModifierKeys.Alt:
                MoveLines(-1);
                e.Handled = true;
                break;
            case Key.Down when mods == ModifierKeys.Alt:
                MoveLines(1);
                e.Handled = true;
                break;
        }
    }

    private void OnAssistMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Ctrl+clique: ir para a definição.
        if (Keyboard.Modifiers != ModifierKeys.Control || !Doc.IsCSharp) return;
        var offset = OffsetAt(e);
        if (offset == null) return;
        CaretOffset = offset.Value;
        TextArea.ClearSelection();
        _ = GoToDefinitionAsync();
        e.Handled = true;
    }

    private void OnRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Clique direito fora da seleção move o cursor para lá (as ações valem para o ponto clicado).
        var offset = OffsetAt(e);
        if (offset == null) return;
        var selection = TextArea.Selection;
        bool insideSelection = !selection.IsEmpty && selection.Segments.Any(s => offset >= s.StartOffset && offset <= s.EndOffset);
        if (!insideSelection)
        {
            TextArea.ClearSelection();
            CaretOffset = offset.Value;
        }
    }

    private int? OffsetAt(MouseEventArgs e)
    {
        var view = TextArea.TextView;
        var pos = view.GetPositionFloor(e.GetPosition(view) + view.ScrollOffset);
        return pos == null ? null : Document.GetOffset(pos.Value.Location);
    }

    // ================================================================ menu do botão direito

    private void BuildContextMenu()
    {
        MenuItem Item(string header, string gesture, Action action)
        {
            var item = new MenuItem { Header = header, InputGestureText = gesture };
            item.Click += (_, _) => action();
            _menu.Items.Add(item);
            return item;
        }

        _fixItem = Item("Correções rápidas…", "Ctrl+.", () => _ = ShowQuickFixesAsync());
        _goToItem = Item("Ir para definição", "F12", () => _ = GoToDefinitionAsync());
        _renameItem = Item("Renomear…", "F2", () => _ = RenameSymbolAsync());
        _menu.Items.Add(new Separator());
        Item("Recortar", "Ctrl+X", () => ApplicationCommands.Cut.Execute(null, TextArea));
        Item("Copiar", "Ctrl+C", () => ApplicationCommands.Copy.Execute(null, TextArea));
        Item("Colar", "Ctrl+V", () => ApplicationCommands.Paste.Execute(null, TextArea));
        _menu.Items.Add(new Separator());
        Item("Comentar / descomentar", "Ctrl+/", ToggleComment);
        Item("Duplicar linha", "Ctrl+D", DuplicateLines);
        Item("Formatar documento", "Ctrl+Shift+F", () => _ = FormatAsync());

        _menu.Opened += (_, _) =>
        {
            bool csharp = Doc.IsCSharp && LanguageServices != null;
            _goToItem.Visibility = csharp ? Visibility.Visible : Visibility.Collapsed;
            _renameItem.Visibility = _goToItem.Visibility;
            _fixItem.Visibility = csharp && MissingNameAtCaret() != null ? Visibility.Visible : Visibility.Collapsed;
        };
        TextArea.ContextMenu = _menu;
    }

    // ================================================================ ir para definição

    public async Task GoToDefinitionAsync()
    {
        var ls = LanguageServices;
        if (ls == null) return;
        var caret = CaretOffset;
        DefinitionResult? result;
        try
        {
            result = await Task.Run(() => ls.FindDefinitionAsync(Doc.LanguageKey, caret, CancellationToken.None));
        }
        catch (Exception ex)
        {
            Core.Settings.AppPaths.Log(ex, "Ir para definição");
            return;
        }
        if (IsDetached) return;
        if (result == null)
        {
            _vm.NotifyInfo("Coloque o cursor sobre um nome (variável, método, classe…) para ir até a definição.");
            return;
        }
        if (result.DocumentKey == null)
        {
            _vm.NotifyInfo($"\"{result.ExternalName}\" faz parte do .NET, não do seu código.");
            return;
        }
        var target = _vm.FindOrOpen(result.DocumentKey, result.FilePath);
        if (target != null) _vm.NavigateTo(target, result.Line, result.Column, result.Offset);
    }

    // ================================================================ renomear

    public async Task RenameSymbolAsync()
    {
        var ls = LanguageServices;
        if (ls == null) return;
        var caret = CaretOffset;
        RenameTarget? target;
        try
        {
            target = await Task.Run(() => ls.GetRenameTargetAsync(Doc.LanguageKey, caret, CancellationToken.None));
        }
        catch (Exception ex)
        {
            Core.Settings.AppPaths.Log(ex, "Renomear");
            return;
        }
        if (IsDetached) return;
        if (target == null)
        {
            _vm.NotifyInfo("Coloque o cursor sobre um nome do seu código (variável, método, classe…) para renomear.");
            return;
        }

        var newName = _vm.Dialogs.AskText("Renomear", $"Novo nome para \"{target.Name}\" (muda em todos os arquivos):", target.Name,
            name => SyntaxFacts.IsValidIdentifier(name) && SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None
                ? null
                : $"\"{name}\" não é um nome válido em C#. Use letras, números e _, sem começar com número.");
        if (newName == null || newName == target.Name) return;

        var versions = _vm.DocumentVersions();
        var (edits, occurrences, error) = await Task.Run(() => ls.RenameAsync(Doc.LanguageKey, caret, newName, CancellationToken.None));
        if (error != null)
        {
            _vm.NotifyError(error);
            return;
        }
        if (!_vm.ApplyRename(edits, versions))
        {
            _vm.NotifyError("O texto mudou enquanto o renomear era calculado. Tente de novo.");
            return;
        }
        var files = edits.Count == 1 ? "1 arquivo" : $"{edits.Count} arquivos";
        _vm.NotifyInfo($"\"{target.Name}\" renomeado para \"{newName}\" ({occurrences} ocorrência{(occurrences == 1 ? "" : "s")} em {files}). Salve para gravar.");
    }

    // ================================================================ correções rápidas (using)

    /// <summary>Nome não encontrado (CS0246/CS0103) sob o cursor, se houver.</summary>
    private string? MissingNameAtCaret()
    {
        foreach (var marker in _diagnostics.At(CaretOffset))
        {
            if (!MissingNameErrors.Contains(marker.Diagnostic.Id)) continue;
            var text = Document.GetText(marker.StartOffset, marker.Length);
            var name = new string(text.TakeWhile(CompletionController.IsIdentifierChar).ToArray());
            if (name.Length > 0) return name;
        }
        return null;
    }

    public async Task ShowQuickFixesAsync()
    {
        var ls = LanguageServices;
        var name = MissingNameAtCaret();
        if (ls == null || name == null)
        {
            _vm.NotifyInfo("Nenhuma correção rápida aqui.");
            return;
        }
        IReadOnlyList<string> namespaces;
        try
        {
            namespaces = await Task.Run(() => ls.FindUsingCandidatesAsync(Doc.LanguageKey, name, CancellationToken.None));
        }
        catch (Exception ex)
        {
            Core.Settings.AppPaths.Log(ex, "Correções rápidas");
            return;
        }
        if (IsDetached) return;
        if (namespaces.Count == 0)
        {
            _vm.NotifyInfo($"Nenhum using conhecido tem \"{name}\". Confira se o nome está escrito certo.");
            return;
        }

        var menu = new ContextMenu { PlacementTarget = TextArea.TextView, Placement = PlacementMode.Relative };
        foreach (var ns in namespaces)
        {
            var item = new MenuItem { Header = $"Adicionar using {ns};" };
            item.Click += async (_, _) => await AddUsingAsync(ns);
            menu.Items.Add(item);
        }
        var caretPos = TextArea.Caret.CalculateCaretRectangle();
        menu.HorizontalOffset = caretPos.Left - TextArea.TextView.ScrollOffset.X;
        menu.VerticalOffset = caretPos.Bottom - TextArea.TextView.ScrollOffset.Y + 2;
        menu.IsOpen = true;
    }

    private async Task AddUsingAsync(string ns)
    {
        var ls = LanguageServices;
        if (ls == null) return;
        var version = Doc.Version;
        var change = await Task.Run(() => ls.AddUsingAsync(Doc.LanguageKey, ns, CancellationToken.None));
        if (change is not { } c || version != Doc.Version || IsDetached) return;
        Document.Insert(c.Span.Start, c.NewText ?? "");
        TextArea.Focus();
    }

    // ================================================================ dica ao passar o mouse

    private async void OnMouseHover(object? sender, MouseEventArgs e)
    {
        var offset = OffsetAt(e);
        if (offset == null) return;
        var markers = _diagnostics.At(offset.Value).Take(3).ToList();

        // O que é o nome sob o mouse (só sobre palavras, não sobre espaço ou pontuação).
        string? info = null;
        var ls = LanguageServices;
        if (Doc.IsCSharp && ls != null && offset.Value < Document.TextLength &&
            CompletionController.IsIdentifierChar(Document.GetCharAt(offset.Value)))
        {
            _hoverCts?.Cancel();
            var cts = _hoverCts = new CancellationTokenSource();
            var version = Doc.Version;
            try
            {
                info = await Task.Run(() => ls.GetQuickInfoAsync(Doc.LanguageKey, offset.Value, cts.Token), cts.Token);
            }
            catch
            {
                info = null;
            }
            if (cts.IsCancellationRequested || version != Doc.Version || IsDetached || !IsMouseOver) return;
        }
        if (markers.Count == 0 && info == null) return;

        var panel = new StackPanel { MaxWidth = 560 };
        foreach (var m in markers)
        {
            var d = m.Diagnostic;
            panel.Children.Add(new TextBlock
            {
                Text = $"Linha {d.Line} — {d.Message}",
                Foreground = (Brush)Application.Current.FindResource("TextPrimary"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, panel.Children.Count > 0 ? 6 : 0, 0, 0),
            });
            panel.Children.Add(new TextBlock
            {
                Text = $"{d.Id} · {d.OriginalMessage}",
                Foreground = (Brush)Application.Current.FindResource("TextMuted"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0),
            });
            if (MissingNameErrors.Contains(d.Id))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "Ctrl+. mostra correções (ex.: adicionar o using que falta).",
                    Foreground = (Brush)Application.Current.FindResource("Accent"),
                    FontSize = 11,
                    Margin = new Thickness(0, 3, 0, 0),
                });
            }
        }
        if (info != null)
        {
            panel.Children.Add(new TextBlock
            {
                Text = info,
                FontFamily = (FontFamily)Application.Current.FindResource("CodeFont"),
                FontSize = 12.5,
                Foreground = (Brush)Application.Current.FindResource("TextPrimary"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, markers.Count > 0 ? 8 : 0, 0, 0),
            });
        }
        CloseHover();
        _hoverTip = new ToolTip { Content = panel, PlacementTarget = this, Placement = PlacementMode.Mouse, IsOpen = true };
    }

    // ================================================================ edição por linhas

    private (DocumentLine First, DocumentLine Last) SelectedLines()
    {
        var selection = TextArea.Selection;
        if (selection.IsEmpty)
        {
            var line = Document.GetLineByOffset(CaretOffset);
            return (line, line);
        }
        var start = Document.GetLineByOffset(selection.SurroundingSegment.Offset);
        var endOffset = selection.SurroundingSegment.EndOffset;
        var end = Document.GetLineByOffset(endOffset);
        // Seleção que termina no começo de uma linha não inclui essa linha.
        if (end.Offset == endOffset && end != start) end = end.PreviousLine;
        return (start, end);
    }

    /// <summary>Comenta as linhas com "// " ou, se todas já estão comentadas, descomenta.</summary>
    public void ToggleComment()
    {
        var (first, last) = SelectedLines();
        var lines = new List<DocumentLine>();
        for (var l = first; l != null; l = l.NextLine)
        {
            lines.Add(l);
            if (l == last) break;
        }
        var content = lines.Where(l => Document.GetText(l).Trim().Length > 0).ToList();
        if (content.Count == 0) return;
        bool allCommented = content.All(l => Document.GetText(l).TrimStart().StartsWith("//", StringComparison.Ordinal));
        int indent = content.Min(l => Document.GetText(l).TakeWhile(c => c is ' ' or '\t').Count());

        using (Document.RunUpdate())
        {
            foreach (var line in content.AsEnumerable().Reverse())
            {
                var text = Document.GetText(line);
                if (allCommented)
                {
                    int at = text.IndexOf("//", StringComparison.Ordinal);
                    int length = at + 2 < text.Length && text[at + 2] == ' ' ? 3 : 2;
                    Document.Remove(line.Offset + at, length);
                }
                else
                {
                    Document.Insert(line.Offset + Math.Min(indent, line.Length), "// ");
                }
            }
        }
    }

    /// <summary>Duplica a seleção, ou a linha atual abaixo dela.</summary>
    public void DuplicateLines()
    {
        var selection = TextArea.Selection;
        if (!selection.IsEmpty && !selection.IsMultiline)
        {
            var segment = selection.SurroundingSegment;
            var text = Document.GetText(segment);
            Document.Insert(segment.EndOffset, text);
            return;
        }
        var (first, last) = SelectedLines();
        var newline = TextUtilities.GetNewLineFromDocument(Document, first.LineNumber);
        var block = Document.GetText(first.Offset, last.EndOffset - first.Offset);
        int column = CaretOffset - Document.GetLineByOffset(CaretOffset).Offset;
        Document.Insert(last.EndOffset, newline + block);
        // O cursor vai para a cópia, na mesma coluna.
        var target = Document.GetLineByNumber(Math.Min(Document.LineCount, Document.GetLineByOffset(CaretOffset).LineNumber + (last.LineNumber - first.LineNumber + 1)));
        TextArea.ClearSelection();
        CaretOffset = target.Offset + Math.Min(column, target.Length);
    }

    public void DeleteLines()
    {
        var (first, last) = SelectedLines();
        int start = first.Offset;
        int end = last.NextLine?.Offset ?? last.EndOffset;
        if (last.NextLine == null && first.PreviousLine != null) start = first.PreviousLine.EndOffset;
        TextArea.ClearSelection();
        Document.Remove(start, end - start);
    }

    /// <summary>Move as linhas selecionadas para cima (-1) ou para baixo (+1).</summary>
    public void MoveLines(int direction)
    {
        var (first, last) = SelectedLines();
        if (direction < 0 && first.PreviousLine == null || direction > 0 && last.NextLine == null) return;
        var newline = TextUtilities.GetNewLineFromDocument(Document, first.LineNumber);
        int caretLine = Document.GetLineByOffset(CaretOffset).LineNumber - first.LineNumber;
        int caretColumn = CaretOffset - Document.GetLineByOffset(CaretOffset).Offset;
        var block = Document.GetText(first.Offset, last.EndOffset - first.Offset);
        bool hadSelection = !TextArea.Selection.IsEmpty;
        // As linhas são recriadas pela edição: guarda os números antes.
        int firstNumber = first.LineNumber, lineCount = last.LineNumber - first.LineNumber;

        int newFirstLine;
        using (Document.RunUpdate())
        {
            if (direction < 0)
            {
                var above = first.PreviousLine!;
                var aboveText = Document.GetText(above);
                Document.Replace(above.Offset, last.EndOffset - above.Offset, block + newline + aboveText);
                newFirstLine = above.LineNumber;
            }
            else
            {
                var below = last.NextLine!;
                var belowText = Document.GetText(below);
                Document.Replace(first.Offset, below.EndOffset - first.Offset, belowText + newline + block);
                newFirstLine = firstNumber + 1;
            }
        }
        var newFirst = Document.GetLineByNumber(newFirstLine);
        var newLast = Document.GetLineByNumber(Math.Min(Document.LineCount, newFirstLine + lineCount));
        var caretTarget = Document.GetLineByNumber(Math.Min(Document.LineCount, newFirstLine + caretLine));
        CaretOffset = caretTarget.Offset + Math.Min(caretColumn, caretTarget.Length);
        if (hadSelection) Select(newFirst.Offset, newLast.EndOffset - newFirst.Offset);
    }
}
