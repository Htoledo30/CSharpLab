using System.IO;
using System.Windows;
using CSharpLab.GameEngine;
using CSharpLab.ViewModels;

namespace CSharpLab.Screens;

/// <summary>
/// O que a aba Tela edita, sem nada de interface (testável). O texto do arquivo continua sendo a fonte
/// de verdade: cada mudança grava o JSON no mesmo documento da aba, então desfazer, salvar, a recuperação
/// e o aviso de "arquivo mudou no disco" funcionam como em qualquer arquivo.
/// </summary>
public sealed class ScreenDesignerModel : IDisposable
{
    /// <summary>Quanto de uma peça precisa continuar dentro do palco ao arrastar para fora.</summary>
    public const double KeepInside = 16;
    public const double MinSize = 12;

    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".bmp"];

    private bool _committing;
    private string? _mergeKey;
    private int _mergeVersion = -1;
    private Piece? _clipboard;

    public ScreenDesignerModel(DocumentViewModel doc)
    {
        Doc = doc;
        ProjectDirectory = FindProjectDirectory(doc.FilePath);
        doc.TextChanged += OnTextChanged;
        Reparse();
    }

    public DocumentViewModel Doc { get; }

    /// <summary>Pasta do jogo (a que tem a pasta Screens). As imagens vêm de Assets, dentro dela.</summary>
    public string? ProjectDirectory { get; }

    public string SceneName => Doc.FilePath != null ? Path.GetFileNameWithoutExtension(Doc.FilePath) : "Tela";

    /// <summary>A tela atual (não altere diretamente: use os métodos deste modelo).</summary>
    internal ScreenLayout? Layout { get; private set; }

    public string? Error { get; private set; }
    public int? ErrorLine { get; private set; }
    public IReadOnlyList<string> Warnings { get; private set; } = [];

    public string? SelectedName { get; private set; }

    internal Piece? Selected => SelectedName != null ? Layout?.Find(SelectedName) : null;

    /// <summary>A tela mudou (edição, desfazer, texto mudado na aba Texto ou no disco).</summary>
    public event Action? Changed;

    public event Action? SelectionChanged;

    /// <summary>Uma peça mudou de nome (antigo, novo): o código que usa o nome antigo pode ser atualizado.</summary>
    public event Action<string, string>? Renamed;

    /// <summary>
    /// Quando o texto muda enquanto a aba Tela está escondida (aba Texto), a leitura espera ela aparecer.
    /// </summary>
    public bool IsLive { get; set; } = true;

    private bool _stale;

    private void OnTextChanged(DocumentViewModel _)
    {
        if (_committing) return;
        // Qualquer mudança de fora (digitação, desfazer, disco) encerra a junção de passos de desfazer.
        _mergeKey = null;
        if (!IsLive)
        {
            _stale = true;
            return;
        }
        Reparse();
    }

    /// <summary>Lê o texto de novo (ao voltar para a aba Tela, por exemplo).</summary>
    public void Refresh()
    {
        if (_stale || Layout == null && Error == null) Reparse();
    }

    private void Reparse()
    {
        _stale = false;
        var result = ScreenFile.Parse(Doc.Document.Text);
        Layout = result.Layout;
        Error = result.Error;
        ErrorLine = result.ErrorLine;
        Warnings = result.Warnings;
        if (SelectedName != null && Layout?.Find(SelectedName) == null)
        {
            SelectedName = null;
            SelectionChanged?.Invoke();
        }
        Changed?.Invoke();
    }

    // ------------------------------------------------------------------ seleção

    public void Select(string? name)
    {
        var piece = name != null ? Layout?.Find(name) : null;
        var newName = piece?.Name;
        if (newName == SelectedName) return;
        SelectedName = newName;
        EndMerge();
        SelectionChanged?.Invoke();
    }

    /// <summary>A peça de cima que está no ponto (as últimas da lista ficam por cima).</summary>
    internal Piece? HitTest(Point point, double tolerance = 0)
    {
        if (Layout == null) return null;
        for (int i = Layout.Pieces.Count - 1; i >= 0; i--)
        {
            var p = Layout.Pieces[i];
            if (BoundsOf(p).Inflate(tolerance).Contains(point)) return p;
        }
        return null;
    }

    internal static Rect BoundsOf(Piece p) => new(p.X, p.Y, Math.Max(1, p.Width), Math.Max(1, p.Height));

    // ------------------------------------------------------------------ edição

    /// <summary>Cria uma peça. Sem posição, ela aparece num lugar livre perto do centro.</summary>
    internal Piece? Add(PieceType type, Point? at = null)
    {
        if (Layout == null) return null;
        var layout = Layout.Clone();
        var piece = Piece.CreateDefault(type, layout.NewName(type), 0, 0);
        if (at is { } p)
        {
            // O ponto é o centro da peça (onde o mouse soltou).
            piece.X = p.X - piece.Width / 2;
            piece.Y = p.Y - piece.Height / 2;
        }
        else
        {
            (piece.X, piece.Y) = FreeSpot(layout, piece.Width, piece.Height);
        }
        piece.X = Math.Round(piece.X / 8) * 8;
        piece.Y = Math.Round(piece.Y / 8) * 8;
        ClampInside(piece);
        layout.Pieces.Add(piece);
        Commit(layout);
        Select(piece.Name);
        return Layout!.Find(piece.Name);
    }

    /// <summary>Centro do palco; se já tiver uma peça ali, um pouco para o lado e para baixo.</summary>
    private static (double X, double Y) FreeSpot(ScreenLayout layout, double width, double height)
    {
        double x = Math.Round((ScreenLayout.Width - width) / 2 / 8) * 8;
        double y = Math.Round((ScreenLayout.Height - height) / 2 / 8) * 8;
        for (int i = 0; i < 12 && layout.Pieces.Any(p => Math.Abs(p.X - x) < 4 && Math.Abs(p.Y - y) < 4); i++)
        {
            x += 24;
            y += 24;
        }
        return (x, y);
    }

    internal static void ClampInside(Piece piece)
    {
        piece.Width = Math.Max(MinSize, piece.Width);
        piece.Height = Math.Max(MinSize, piece.Height);
        piece.X = Math.Clamp(piece.X, KeepInside - piece.Width, ScreenLayout.Width - KeepInside);
        piece.Y = Math.Clamp(piece.Y, KeepInside - piece.Height, ScreenLayout.Height - KeepInside);
    }

    /// <summary>Posição e tamanho novos (depois de arrastar ou redimensionar). Valores arredondados.</summary>
    internal void SetBounds(string name, Rect bounds, string? mergeKey = null)
    {
        Edit(name, p =>
        {
            p.X = Math.Round(bounds.X);
            p.Y = Math.Round(bounds.Y);
            p.Width = Math.Round(bounds.Width);
            p.Height = Math.Round(bounds.Height);
            ClampInside(p);
        }, mergeKey);
    }

    /// <summary>Setas do teclado. Toques seguidos viram um passo só no desfazer.</summary>
    public void Nudge(double dx, double dy)
    {
        if (Selected is not { } piece) return;
        Edit(piece.Name, p =>
        {
            p.X += dx;
            p.Y += dy;
            ClampInside(p);
        }, "nudge:" + piece.Name);
    }

    /// <summary>
    /// Muda uma peça. <paramref name="mergeKey"/>: mudanças seguidas com a mesma chave (digitar um texto,
    /// girar a roda num número) viram um passo só no desfazer.
    /// </summary>
    internal void Edit(string name, Action<Piece> change, string? mergeKey = null)
    {
        if (Layout?.Find(name) == null) return;
        var layout = Layout.Clone();
        change(layout.Find(name)!);
        Commit(layout, mergeKey);
    }

    public void SetBackground(string? image)
    {
        if (Layout == null) return;
        var layout = Layout.Clone();
        layout.Background = string.IsNullOrWhiteSpace(image) ? null : image;
        Commit(layout);
    }

    /// <summary>Troca o nome da peça. Retorna o problema (em português) ou null se deu certo.</summary>
    public string? Rename(string newName)
    {
        if (Selected is not { } piece || Layout == null) return null;
        newName = newName.Trim();
        if (newName == piece.Name) return null;
        if (ScreenFile.NameProblem(newName) is { } problem) return char.ToUpperInvariant(problem[0]) + problem[1..];
        if (Layout.Pieces.Any(p => p != piece && string.Equals(p.Name, newName, StringComparison.OrdinalIgnoreCase)))
            return $"Já existe uma peça chamada \"{newName}\" nesta tela.";
        var layout = Layout.Clone();
        layout.Find(piece.Name)!.Name = newName;
        // A seleção acompanha o nome novo antes de avisar a tela, para nada piscar.
        SelectedName = newName;
        Commit(layout);
        Renamed?.Invoke(piece.Name, newName);
        return null;
    }

    public void Delete()
    {
        if (Selected is not { } piece || Layout == null) return;
        var layout = Layout.Clone();
        int index = layout.Pieces.FindIndex(p => p.Name == piece.Name);
        layout.Pieces.RemoveAt(index);
        SelectedName = null;
        Commit(layout);
        SelectionChanged?.Invoke();
    }

    /// <summary>Cópia da peça um pouco deslocada, com nome novo, já selecionada.</summary>
    internal Piece? Duplicate() => Selected is { } piece ? Paste(piece) : null;

    public void Copy() => _clipboard = Selected?.Clone();

    public bool CanPaste => _clipboard != null && Layout != null;

    internal Piece? Paste() => _clipboard != null ? Paste(_clipboard) : null;

    private Piece? Paste(Piece source)
    {
        if (Layout == null) return null;
        var layout = Layout.Clone();
        var copy = source.Clone();
        copy.Name = UniqueCopyName(layout, source.Name);
        copy.X += 16;
        copy.Y += 16;
        ClampInside(copy);
        layout.Pieces.Add(copy);
        Commit(layout);
        Select(copy.Name);
        return Layout!.Find(copy.Name);
    }

    private static string UniqueCopyName(ScreenLayout layout, string name)
    {
        // Attack -> Attack2, Attack2 -> Attack3…
        var stem = name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        if (stem.Length == 0) stem = name;
        for (int i = 2; ; i++)
        {
            var candidate = stem + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (layout.Find(candidate) == null) return candidate;
        }
    }

    public void BringToFront() => Reorder(toFront: true);

    public void SendToBack() => Reorder(toFront: false);

    private void Reorder(bool toFront)
    {
        if (Selected is not { } piece || Layout == null) return;
        var layout = Layout.Clone();
        int index = layout.Pieces.FindIndex(p => p.Name == piece.Name);
        if (toFront ? index == layout.Pieces.Count - 1 : index == 0) return;
        var moving = layout.Pieces[index];
        layout.Pieces.RemoveAt(index);
        if (toFront) layout.Pieces.Add(moving);
        else layout.Pieces.Insert(0, moving);
        Commit(layout);
    }

    /// <summary>Termina a junção de passos de desfazer (ao sair de um campo, trocar de peça…).</summary>
    public void EndMerge() => _mergeKey = null;

    public bool CanUndo => Doc.Document.UndoStack.CanUndo;
    public bool CanRedo => Doc.Document.UndoStack.CanRedo;

    public void Undo()
    {
        EndMerge();
        if (CanUndo) Doc.Document.UndoStack.Undo();
    }

    public void Redo()
    {
        EndMerge();
        if (CanRedo) Doc.Document.UndoStack.Redo();
    }

    /// <summary>
    /// Grava a tela no documento. Só a parte do texto que mudou é trocada, para o desfazer ficar leve e a
    /// aba Texto não pular de lugar.
    /// </summary>
    private void Commit(ScreenLayout layout, string? mergeKey = null)
    {
        var text = ScreenFile.Serialize(layout);
        var document = Doc.Document;
        var old = document.Text;
        if (text == old)
        {
            Layout = layout;
            return;
        }

        int prefix = 0, max = Math.Min(old.Length, text.Length);
        while (prefix < max && old[prefix] == text[prefix]) prefix++;
        int suffix = 0;
        while (suffix < max - prefix && old[old.Length - 1 - suffix] == text[text.Length - 1 - suffix]) suffix++;

        bool merge = mergeKey != null && mergeKey == _mergeKey && _mergeVersion == Doc.Version;
        _committing = true;
        try
        {
            if (merge) document.UndoStack.StartContinuedUndoGroup();
            else document.UndoStack.StartUndoGroup();
            document.Replace(prefix, old.Length - prefix - suffix, text.Substring(prefix, text.Length - prefix - suffix));
            document.UndoStack.EndUndoGroup();
        }
        finally
        {
            _committing = false;
        }
        _mergeKey = mergeKey;
        _mergeVersion = Doc.Version;
        Layout = layout;
        Error = null;
        ErrorLine = null;
        Warnings = [];
        Changed?.Invoke();
    }

    // ------------------------------------------------------------------ imagens

    public string? AssetsDirectory => ProjectDirectory != null ? Path.Combine(ProjectDirectory, "Assets") : null;

    /// <summary>As imagens da pasta Assets (o nome usado na tela, ex.: "goblin.png" ou "enemies/orc.png").</summary>
    public IReadOnlyList<string> ImageFiles()
    {
        var dir = AssetsDirectory;
        if (dir == null || !Directory.Exists(dir)) return [];
        try
        {
            return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Where(f => ImageExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .Select(f => Path.GetRelativePath(dir, f).Replace('\\', '/'))
                .Order(StringComparer.OrdinalIgnoreCase)
                .Take(500)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Copia uma imagem para a pasta Assets (sem apagar nenhuma existente) e devolve o nome para a tela.</summary>
    public string ImportImage(string sourcePath)
    {
        var dir = AssetsDirectory ?? throw new InvalidOperationException("A tela não está dentro de um projeto de jogo.");
        Directory.CreateDirectory(dir);
        var full = Path.GetFullPath(sourcePath);
        if (string.Equals(Path.GetDirectoryName(full), Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase))
            return Path.GetFileName(full);
        var name = Path.GetFileName(full);
        var target = Path.Combine(dir, name);
        for (int i = 2; File.Exists(target); i++)
        {
            name = $"{Path.GetFileNameWithoutExtension(full)}-{i}{Path.GetExtension(full)}";
            target = Path.Combine(dir, name);
        }
        File.Copy(full, target);
        return name;
    }

    // ------------------------------------------------------------------ código

    /// <summary>Exemplo de código para a peça, para a pessoa ver como ligar a tela ao programa.</summary>
    internal static string CodeExample(Piece piece)
    {
        var find = $"game.Find(\"{piece.Name}\")";
        return piece.Type switch
        {
            PieceType.Button => $"{find}.OnClick(() =>\n{{\n    \n}});",
            PieceType.Text => $"{find}.Text = \"{Escape(piece.Text ?? "")}\";",
            PieceType.Bar => $"{find}.Value = health;",
            PieceType.Image => $"{find}.Image = \"{Escape(piece.Image ?? "goblin.png")}\";",
            PieceType.Box => $"{find}.Visible = true;",
            PieceType.Input => $"{find}.OnAnswer(answer =>\n{{\n    \n}});",
            PieceType.Messages => "game.Write(\"Você causou 7 de dano!\");",
            _ => find,
        };

        static string Escape(string text) => text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");
    }

    private static string? FindProjectDirectory(string? file)
    {
        if (file == null) return null;
        var screens = Path.GetDirectoryName(Path.GetFullPath(file));
        if (screens == null) return null;
        return string.Equals(Path.GetFileName(screens), ScreenLibrary.Folder, StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(screens)
            : screens;
    }

    public void Dispose() => Doc.TextChanged -= OnTextChanged;
}

internal static class RectExtensions
{
    public static Rect Inflate(this Rect rect, double amount)
    {
        if (amount <= 0) return rect;
        rect.Inflate(amount, amount);
        return rect;
    }
}
