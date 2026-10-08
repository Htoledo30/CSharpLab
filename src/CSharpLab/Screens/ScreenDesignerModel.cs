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
    private List<string> _selected = [];

    /// <summary>
    /// Peças copiadas (Ctrl+C), com a posição na tela e a Lista de cada uma. Fica no programa todo:
    /// copiar na Vila e colar na Floresta funciona.
    /// </summary>
    private static List<Piece>? _clipboard;

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

    /// <summary>A peça selecionada, quando é uma só (com várias selecionadas, null).</summary>
    public string? SelectedName => _selected.Count == 1 ? _selected[0] : null;

    /// <summary>Todas as peças selecionadas (Shift+clique ou arrastando um retângulo no palco).</summary>
    public IReadOnlyList<string> SelectedNames => _selected;

    internal Piece? Selected => SelectedName != null ? Layout?.Find(SelectedName) : null;

    internal IReadOnlyList<Piece> SelectedPieces =>
        Layout == null ? [] : _selected.Select(n => Layout.Find(n)).OfType<Piece>().ToList();

    public bool IsSelected(string name) => _selected.Contains(name, StringComparer.OrdinalIgnoreCase);

    /// <summary>A tela mudou (edição, desfazer, texto mudado no Arquivo ou no disco).</summary>
    public event Action? Changed;

    public event Action? SelectionChanged;

    /// <summary>Uma peça mudou de nome (antigo, novo): o código que usa o nome antigo pode ser atualizado.</summary>
    public event Action<string, string>? Renamed;

    /// <summary>
    /// Quando o texto muda enquanto a aba Tela está escondida (Arquivo aberto), a leitura espera ela aparecer.
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
        if (_selected.Count > 0 && _selected.Any(n => Layout?.Find(n) == null))
        {
            _selected = _selected.Where(n => Layout?.Find(n) != null).ToList();
            SelectionChanged?.Invoke();
        }
        Changed?.Invoke();
    }

    // ------------------------------------------------------------------ seleção

    public void Select(string? name)
    {
        var piece = name != null ? Layout?.Find(name) : null;
        SelectMany(piece != null ? [piece.Name] : []);
    }

    /// <summary>Seleciona estas peças (as que não existem são ignoradas).</summary>
    public void SelectMany(IEnumerable<string> names)
    {
        var list = new List<string>();
        foreach (var name in names)
        {
            if (Layout?.Find(name) is { } piece && !list.Contains(piece.Name, StringComparer.OrdinalIgnoreCase)) list.Add(piece.Name);
        }
        if (list.SequenceEqual(_selected)) return;
        _selected = list;
        EndMerge();
        SelectionChanged?.Invoke();
    }

    /// <summary>Shift+clique: põe a peça na seleção, ou tira se já estava.</summary>
    public void ToggleSelect(string name)
    {
        if (Layout?.Find(name) is not { } piece) return;
        SelectMany(IsSelected(piece.Name)
            ? _selected.Where(n => !string.Equals(n, piece.Name, StringComparison.OrdinalIgnoreCase))
            : _selected.Append(piece.Name));
    }

    public void SelectAll() => SelectMany(Layout?.Pieces.Select(p => p.Name) ?? []);

    /// <summary>A peça de cima que está no ponto (as últimas da lista ficam por cima).</summary>
    internal Piece? HitTest(Point point, double tolerance = 0)
    {
        if (Layout == null) return null;
        var order = DrawOrder();
        for (int i = order.Count - 1; i >= 0; i--)
        {
            var p = order[i];
            if (BoundsOf(p).Inflate(tolerance).Contains(point)) return p;
        }
        return null;
    }

    /// <summary>As peças que o retângulo toca (para a seleção arrastando no palco).</summary>
    internal IReadOnlyList<Piece> PiecesIn(Rect area) =>
        Layout == null ? [] : DrawOrder().Where(p => BoundsOf(p).IntersectsWith(area)).ToList();

    /// <summary>
    /// A ordem em que as peças aparecem: a da lista, com as peças do cartão de cada Lista logo depois dela
    /// (por cima da Lista, como no jogo).
    /// </summary>
    internal IReadOnlyList<Piece> DrawOrder()
    {
        if (Layout == null) return [];
        var order = new List<Piece>();
        foreach (var piece in Layout.Pieces)
        {
            if (piece.List != null && Layout.ListOf(piece) != null) continue;
            order.Add(piece);
            if (piece.Type == PieceType.List) order.AddRange(Layout.MembersOf(piece.Name));
        }
        return order;
    }

    /// <summary>Onde a peça está no palco (as peças de cartão contam a partir do cartão modelo da Lista).</summary>
    internal Rect BoundsOf(Piece p) => Layout != null ? AbsoluteBounds(Layout, p) : PlainBounds(p);

    internal static Rect PlainBounds(Piece p) => new(p.X, p.Y, Math.Max(1, p.Width), Math.Max(1, p.Height));

    internal static Rect AbsoluteBounds(ScreenLayout layout, Piece p)
    {
        var rect = PlainBounds(p);
        if (layout.ListOf(p) is { } list) rect.Offset(list.X, list.Y);
        return rect;
    }

    /// <summary>O cartão modelo da Lista: onde ficam as peças dela, no palco.</summary>
    internal static Rect CardSlot(Piece list) => new(list.X, list.Y, list.CardW, list.CardH);

    /// <summary>A Lista cujo cartão modelo tem este retângulo inteiro dentro (null se nenhuma).</summary>
    internal Piece? ListAt(Rect bounds, Piece? except = null) => Layout != null ? ListAt(Layout, bounds, except) : null;

    private static Piece? ListAt(ScreenLayout layout, Rect bounds, Piece? except)
    {
        for (int i = layout.Pieces.Count - 1; i >= 0; i--)
        {
            var list = layout.Pieces[i];
            if (list.Type != PieceType.List || list == except || except?.Name == list.Name) continue;
            var slot = CardSlot(list);
            slot.Inflate(0.5, 0.5);
            if (slot.Contains(bounds)) return list;
        }
        return null;
    }

    /// <summary>
    /// Depois de mover: dentro do cartão modelo de uma Lista, a peça passa a fazer parte do cartão; com o
    /// meio dela fora do cartão, volta a ser uma peça solta.
    /// </summary>
    private static void Reparent(ScreenLayout layout, Piece piece)
    {
        if (!Piece.Supports(piece.Type, nameof(Piece.List)))
        {
            piece.List = null;
            return;
        }
        var abs = AbsoluteBounds(layout, piece);
        if (layout.ListOf(piece) is { } current)
        {
            var center = new Point(abs.X + abs.Width / 2, abs.Y + abs.Height / 2);
            if (CardSlot(current).Contains(center)) return;
            piece.List = null;
            (piece.X, piece.Y) = (abs.X, abs.Y);
            return;
        }
        piece.List = null;
        if (ListAt(layout, abs, piece) is { } list)
        {
            piece.List = list.Name;
            (piece.X, piece.Y) = (abs.X - list.X, abs.Y - list.Y);
        }
    }

    /// <summary>Põe a peça no lugar (posição no palco), cuidando de quem é de cartão.</summary>
    private static void Place(ScreenLayout layout, Piece piece, Rect abs)
    {
        var offset = layout.ListOf(piece) is { } list ? new Vector(list.X, list.Y) : new Vector();
        piece.X = Math.Round(abs.X - offset.X);
        piece.Y = Math.Round(abs.Y - offset.Y);
        piece.Width = Math.Round(abs.Width);
        piece.Height = Math.Round(abs.Height);
    }

    // ------------------------------------------------------------------ edição

    /// <summary>Cria uma peça. Sem posição, ela aparece num lugar livre perto do centro.</summary>
    internal Piece? Add(PieceType type, Point? at = null)
    {
        if (Layout == null) return null;
        var layout = Layout.Clone();
        var piece = Piece.CreateDefault(type, layout.NewName(type), 0, 0);
        if (type == PieceType.List) piece.Text = null;
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
        Reparent(layout, piece);   // solta dentro do cartão modelo de uma Lista: vira parte do cartão
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

    /// <summary>Um pedaço da peça sempre fica dentro do palco, para ela nunca se perder.</summary>
    internal static Rect ClampInside(Rect r)
    {
        double width = Math.Max(MinSize, r.Width), height = Math.Max(MinSize, r.Height);
        return new Rect(
            Math.Clamp(r.X, KeepInside - width, ScreenLayout.Width - KeepInside),
            Math.Clamp(r.Y, KeepInside - height, ScreenLayout.Height - KeepInside),
            width, height);
    }

    /// <summary>Posição e tamanho novos no palco (depois de arrastar ou redimensionar). Valores arredondados.</summary>
    internal void SetBounds(string name, Rect bounds, string? mergeKey = null)
    {
        if (Layout?.Find(name) == null) return;
        var layout = Layout.Clone();
        var piece = layout.Find(name)!;
        Place(layout, piece, ClampInside(bounds));
        Reparent(layout, piece);
        Commit(layout, mergeKey);
    }

    /// <summary>
    /// Move várias peças juntas (arrastando a seleção ou com as setas). As peças do cartão de uma Lista
    /// que também está sendo movida vão junto com ela.
    /// </summary>
    internal void MoveBy(IEnumerable<string> names, double dx, double dy, string? mergeKey = null)
    {
        if (Layout == null) return;
        var layout = Layout.Clone();
        var set = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        var moved = new List<Piece>();
        foreach (var piece in layout.Pieces)
        {
            if (!set.Contains(piece.Name) || piece.List != null && set.Contains(piece.List)) continue;
            var abs = AbsoluteBounds(layout, piece);
            abs.Offset(dx, dy);
            Place(layout, piece, ClampInside(abs));
            moved.Add(piece);
        }
        if (moved.Count == 0) return;
        foreach (var piece in moved) Reparent(layout, piece);
        Commit(layout, mergeKey);
    }

    /// <summary>Setas do teclado: movem todas as selecionadas. Toques seguidos viram um passo só no desfazer.</summary>
    public void Nudge(double dx, double dy)
    {
        if (_selected.Count == 0) return;
        MoveBy(_selected, dx, dy, "nudge:" + string.Join(",", _selected));
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
        // As peças do cartão continuam na Lista, agora com o nome novo.
        foreach (var member in layout.Pieces.Where(p => string.Equals(p.List, piece.Name, StringComparison.OrdinalIgnoreCase)))
            member.List = newName;
        // A seleção acompanha o nome novo antes de avisar a tela, para nada piscar.
        _selected = [newName];
        Commit(layout);
        Renamed?.Invoke(piece.Name, newName);
        return null;
    }

    /// <summary>Apaga as peças selecionadas (apagar uma Lista apaga o cartão dela junto).</summary>
    public void Delete()
    {
        if (_selected.Count == 0 || Layout == null) return;
        var layout = Layout.Clone();
        var gone = new HashSet<string>(_selected, StringComparer.OrdinalIgnoreCase);
        layout.Pieces.RemoveAll(p => gone.Contains(p.Name) || p.List != null && gone.Contains(p.List));
        _selected = [];
        Commit(layout);
        SelectionChanged?.Invoke();
    }

    /// <summary>As selecionadas (e o cartão das Listas selecionadas), com a posição no palco.</summary>
    private List<Piece> TakeSelection()
    {
        if (Layout == null || _selected.Count == 0) return [];
        var names = new HashSet<string>(_selected, StringComparer.OrdinalIgnoreCase);
        var result = new List<Piece>();
        foreach (var piece in Layout.Pieces)
        {
            if (!names.Contains(piece.Name) && !(piece.List != null && names.Contains(piece.List))) continue;
            var copy = piece.Clone();
            var abs = AbsoluteBounds(Layout, piece);
            (copy.X, copy.Y) = (abs.X, abs.Y);
            result.Add(copy);
        }
        return result;
    }

    /// <summary>Cópia das selecionadas um pouco deslocada, com nomes novos, já selecionada (não mexe no Ctrl+C).</summary>
    internal Piece? Duplicate()
    {
        var pieces = TakeSelection();
        return pieces.Count > 0 ? Paste(pieces) : null;
    }

    public void Copy()
    {
        var pieces = TakeSelection();
        if (pieces.Count > 0) _clipboard = pieces;
    }

    public bool CanPaste => _clipboard != null && Layout != null;

    /// <summary>
    /// Cola o que foi copiado, nesta tela ou em outra: no mesmo lugar e com os mesmos nomes (assim o mesmo
    /// código serve às duas telas). Se o lugar já está ocupado pela mesma peça, um pouco para o lado; se o
    /// nome já existe, um nome novo (Attack2…).
    /// </summary>
    internal Piece? Paste() => _clipboard != null ? Paste(_clipboard) : null;

    private Piece? Paste(IReadOnlyList<Piece> source)
    {
        if (Layout == null || source.Count == 0) return null;
        var layout = Layout.Clone();

        // Colar por cima da mesma peça no mesmo lugar não serve: desloca até achar lugar livre.
        double shift = 0;
        while (shift < 16 * 20 && source.Any(s => layout.Pieces.Any(p =>
                   p.Type == s.Type && AbsoluteBounds(layout, p) == new Rect(s.X + shift, s.Y + shift, Math.Max(1, s.Width), Math.Max(1, s.Height)))))
            shift += 16;

        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pasted = new List<Piece>();
        var ordered = source.OrderBy(p => p.Type == PieceType.List ? 0 : 1).ToList();   // as Listas antes do cartão delas
        foreach (var original in ordered)
        {
            var copy = original.Clone();
            copy.Name = layout.Find(original.Name) == null ? original.Name : UniqueCopyName(layout, original.Name);
            names[original.Name] = copy.Name;
            copy.List = null;
            var abs = ClampInside(new Rect(original.X + shift, original.Y + shift, copy.Width, copy.Height));
            (copy.X, copy.Y) = (abs.X, abs.Y);
            layout.Pieces.Add(copy);
            pasted.Add(copy);
        }
        // As peças de cartão voltam para a Lista colada junto (ou entram num cartão se caírem dentro de um).
        for (int i = 0; i < pasted.Count; i++)
        {
            var copy = pasted[i];
            var original = ordered[i];
            if (original.List != null && names.TryGetValue(original.List, out var listName) && layout.Find(listName) is { } list)
            {
                copy.List = list.Name;
                (copy.X, copy.Y) = (copy.X - list.X, copy.Y - list.Y);
            }
            else
            {
                Reparent(layout, copy);
            }
        }
        Commit(layout);
        SelectMany(pasted.Where(p => p.List == null || !names.ContainsValue(p.List)).Select(p => p.Name));
        return Layout!.Find(pasted[0].Name);
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

    /// <summary>Leva as selecionadas para cima (ou para baixo) de todas, sem mudar a ordem entre elas.</summary>
    private void Reorder(bool toFront)
    {
        if (_selected.Count == 0 || Layout == null) return;
        var layout = Layout.Clone();
        var names = new HashSet<string>(_selected, StringComparer.OrdinalIgnoreCase);
        var moving = layout.Pieces.Where(p => names.Contains(p.Name)).ToList();
        var rest = layout.Pieces.Where(p => !names.Contains(p.Name)).ToList();
        layout.Pieces = toFront ? [.. rest, .. moving] : [.. moving, .. rest];
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
    /// o Arquivo não pular de lugar.
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
        var find = piece.List != null ? $"card.Find(\"{piece.Name}\")" : $"game.Find(\"{piece.Name}\")";
        return piece.Type switch
        {
            PieceType.Button => $"{find}.OnClick(() =>\n{{\n    \n}});",
            PieceType.Text => $"{find}.Text = \"{Escape(piece.Text ?? "")}\";",
            PieceType.Bar => $"{find}.Value = health;",
            PieceType.Image => $"{find}.Image = \"{Escape(piece.Image ?? "goblin.png")}\";",
            PieceType.Box => $"{find}.Visible = true;",
            PieceType.Input => $"{find}.OnAnswer(answer =>\n{{\n    \n}});",
            PieceType.Messages => "game.Write(\"Você causou 7 de dano!\");",
            PieceType.List => $"{find}.Show(items, (card, item) =>\n{{\n    card.Find(\"Name\").Text = item.Name;\n}});",
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
