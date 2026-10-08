using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CSharpLab.Core.Files;
using CSharpLab.Core.Projects;

namespace CSharpLab.ViewModels;

public enum NodeKind
{
    Folder,
    CSharp,
    Project,
    Json,
    Text,
    Other,
}

public sealed partial class ExplorerNode : ObservableObject
{
    private static readonly ExplorerNode Placeholder = new(null, "", "", true) { IsPlaceholder = true };
    private bool _loaded;

    public ExplorerNode(ExplorerNode? parent, string fullPath, string name, bool isDirectory)
    {
        Parent = parent;
        FullPath = fullPath;
        Name = name;
        IsDirectory = isDirectory;
        if (isDirectory && !ReferenceEquals(Placeholder, null) && fullPath.Length > 0)
            Children.Add(Placeholder);
    }

    public ExplorerNode? Parent { get; }
    public string FullPath { get; private set; }
    public bool IsDirectory { get; }
    public bool IsPlaceholder { get; private init; }
    /// <summary>Nó temporário usado para digitar o nome de um arquivo ou pasta novo.</summary>
    public bool IsNew { get; init; }
    public ObservableCollection<ExplorerNode> Children { get; } = [];

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    [ObservableProperty]
    public partial string EditName { get; set; } = "";

    [ObservableProperty]
    public partial string? EditError { get; set; }

    public NodeKind Kind => IsDirectory ? NodeKind.Folder : Path.GetExtension(Name).ToLowerInvariant() switch
    {
        ".cs" => NodeKind.CSharp,
        ".csproj" or ".sln" or ".slnx" => NodeKind.Project,
        ".json" => NodeKind.Json,
        ".txt" or ".md" => NodeKind.Text,
        _ => NodeKind.Other,
    };

    /// <summary>
    /// Explicação das pastas e arquivos especiais de um jogo (o que é, se precisa mexer). Os que o
    /// CSharp Lab cuida sozinho (<see cref="IsQuiet"/>) aparecem mais apagados.
    /// </summary>
    public string? Explanation
    {
        get
        {
            if (!_explained)
            {
                _explanation = Explain();
                _explained = true;
            }
            return _explanation;
        }
    }

    private string? _explanation;
    private bool _explained;

    private string? Explain()
    {
        // Só os nomes especiais olham o disco (para saber se a pasta é de um jogo).
        var name = Name.ToLowerInvariant();
        if (FullPath.Length == 0 || Parent == null || name is not ("lib" or "screens" or "assets" or "agents.md" or "claude.md" or "gamestyle.json")) return null;
        var folder = Path.GetDirectoryName(FullPath) ?? "";
        if (!File.Exists(Path.Combine(folder, "lib", Core.Projects.GameKit.LibraryName + ".dll"))) return null;
        return (IsDirectory, name) switch
        {
            (true, "lib") => "O motor dos jogos (CSharpLab.Game). O CSharp Lab atualiza sozinho: não precisa mexer.",
            (true, "screens") => "As telas desenhadas: cada arquivo é uma cena e abre na aba Tela.",
            (true, "assets") => "As imagens do jogo (.png, .jpg). Use nas peças Imagem ou em game.Image(\"arquivo.png\").",
            (false, "agents.md") => "Guia do motor para IAs (Codex e outras). Pode ignorar.",
            (false, "gamestyle.json") => "O tema do jogo (Clássico, Fantasia, Livro ou Moderno). Escolha na aba Tela, sem nenhuma peça selecionada.",
            (false, "claude.md") => "Guia do motor para o Claude (aponta para o AGENTS.md). Pode ignorar.",
            _ => null,
        };
    }

    public bool IsQuiet => Explanation != null && Name.ToLowerInvariant() is "lib" or "agents.md" or "claude.md";

    /// <summary>A dica do mouse: a explicação (se houver) e o caminho.</summary>
    public string ToolTipText => Explanation is { } text ? text + "\n" + FullPath : FullPath;

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && !_loaded) LoadChildren();
    }

    public void Rebase(string newPath)
    {
        var old = FullPath;
        FullPath = newPath;
        Name = Path.GetFileName(newPath);
        OnPropertyChanged(nameof(Kind));
        _explained = false;
        OnPropertyChanged(nameof(Explanation));
        OnPropertyChanged(nameof(IsQuiet));
        OnPropertyChanged(nameof(ToolTipText));
        if (_loaded)
        {
            foreach (var c in Children)
            {
                if (!c.IsPlaceholder)
                    c.Rebase(Path.Combine(newPath, Path.GetRelativePath(old, c.FullPath)));
            }
        }
    }

    public void LoadChildren()
    {
        _loaded = true;
        var entries = ReadEntries();
        Children.Clear();
        foreach (var (path, isDir) in entries)
            Children.Add(new ExplorerNode(this, path, Path.GetFileName(path), isDir));
    }

    /// <summary>Atualiza os filhos sem perder o que está expandido.</summary>
    public void Refresh()
    {
        if (!_loaded) return;
        var entries = ReadEntries();
        var wanted = entries.Select(e => e.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (int i = Children.Count - 1; i >= 0; i--)
        {
            var c = Children[i];
            if (c.IsNew) continue;
            if (c.IsPlaceholder || !wanted.Contains(c.FullPath)) Children.RemoveAt(i);
        }
        for (int i = 0; i < entries.Count; i++)
        {
            var (path, isDir) = entries[i];
            var existing = Children.FirstOrDefault(c => string.Equals(c.FullPath, path, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                existing = new ExplorerNode(this, path, Path.GetFileName(path), isDir);
                Children.Insert(Math.Min(i, Children.Count), existing);
            }
            else
            {
                if (existing.Name != Path.GetFileName(path)) existing.Name = Path.GetFileName(path);
                var idx = Children.IndexOf(existing);
                if (idx != i && i < Children.Count) Children.Move(idx, i);
                existing.Refresh();
            }
        }
    }

    private List<(string Path, bool IsDir)> ReadEntries()
    {
        var list = new List<(string, bool)>();
        try
        {
            var dirs = Directory.EnumerateDirectories(FullPath)
                .Where(d =>
                {
                    var name = Path.GetFileName(d);
                    if (ProjectLocator.IsSkippedDirectory(name)) return false;
                    try { return !File.GetAttributes(d).HasFlag(FileAttributes.Hidden); } catch { return false; }
                })
                .OrderBy(d => Path.GetFileName(d), StringComparer.CurrentCultureIgnoreCase);
            var files = Directory.EnumerateFiles(FullPath)
                .Where(f =>
                {
                    var name = Path.GetFileName(f);
                    if (TextFileIO.IsTempName(name)) return false;
                    try { return !File.GetAttributes(f).HasFlag(FileAttributes.Hidden); } catch { return false; }
                })
                .OrderBy(f => Path.GetFileName(f), StringComparer.CurrentCultureIgnoreCase);
            list.AddRange(dirs.Select(d => (d, true)));
            list.AddRange(files.Select(f => (f, false)));
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
        return list;
    }

    public ExplorerNode? Find(string path)
    {
        if (string.Equals(FullPath, path, StringComparison.OrdinalIgnoreCase)) return this;
        if (!_loaded || !IsDirectory || !FileOperations.IsSameOrInside(path, FullPath)) return null;
        foreach (var c in Children)
        {
            var found = c.Find(path);
            if (found != null) return found;
        }
        return null;
    }

    public void CollapseAll()
    {
        foreach (var c in Children)
        {
            c.CollapseAll();
            c.IsExpanded = false;
        }
    }
}

public sealed partial class ExplorerViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public ExplorerViewModel(MainViewModel main) => _main = main;

    [ObservableProperty]
    public partial ExplorerNode? Root { get; set; }

    [ObservableProperty]
    public partial ExplorerNode? Selected { get; set; }

    public string? FolderName => Root?.Name;

    public bool HasFolder => Root != null;

    public void Load(string? folder)
    {
        if (folder == null)
        {
            Root = null;
        }
        else
        {
            var root = new ExplorerNode(null, folder, Path.GetFileName(folder.TrimEnd('\\')), true);
            root.LoadChildren();
            Root = root;
        }
        OnPropertyChanged(nameof(FolderName));
        OnPropertyChanged(nameof(HasFolder));
    }

    /// <summary>Atualiza as pastas afetadas por mudanças no disco.</summary>
    public void RefreshPaths(IEnumerable<string> changedDirectories)
    {
        if (Root == null) return;
        foreach (var dir in changedDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var node = string.Equals(dir.TrimEnd('\\'), Root.FullPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ? Root : Root.Find(dir);
            node?.Refresh();
        }
    }

    /// <summary>Pasta onde novos itens são criados: a selecionada, ou a pasta do arquivo selecionado.</summary>
    public ExplorerNode? TargetFolder()
    {
        var sel = Selected;
        if (sel == null || sel.IsNew) return Root;
        return sel.IsDirectory ? sel : sel.Parent ?? Root;
    }

    [RelayCommand]
    private void Refresh()
    {
        Root?.Refresh();
    }

    [RelayCommand]
    private void CollapseAll() => Root?.CollapseAll();

    [RelayCommand]
    private void NewFile() => BeginCreate(isDirectory: false);

    [RelayCommand]
    private void NewFolder() => BeginCreate(isDirectory: true);

    private void BeginCreate(bool isDirectory)
    {
        var target = TargetFolder();
        if (target == null)
        {
            _main.NotifyInfo("Abra uma pasta para criar arquivos nela.");
            return;
        }
        CancelPendingEdits();
        if (target != Root) target.IsExpanded = true;
        var collection = target.Children;
        var node = new ExplorerNode(target, Path.Combine(target.FullPath, "_novo_"), "", isDirectory)
        {
            IsNew = true,
            IsEditing = true,
            EditName = isDirectory ? "" : ".cs",
        };
        int index = isDirectory ? 0 : collection.Count(c => c.IsDirectory);
        collection.Insert(index, node);
        node.IsSelected = true;
    }

    [RelayCommand]
    private void Rename(ExplorerNode? node)
    {
        node ??= Selected;
        if (node == null || node.IsNew || node == Root) return;
        CancelPendingEdits();
        node.EditName = node.Name;
        node.EditError = null;
        node.IsEditing = true;
    }

    /// <summary>Confirma a edição do nome (criação ou renomeação). Retorna false se o nome é inválido.</summary>
    public bool CommitEdit(ExplorerNode node)
    {
        if (!node.IsEditing) return true;
        var name = node.EditName.Trim();
        if (node.IsNew)
        {
            if (name.Length == 0 || name == ".cs")
            {
                RemoveNew(node);
                return true;
            }
            if (!node.IsDirectory) name = FileOperations.WithDefaultExtension(name);
            var parent = node.Parent ?? Root!;
            try
            {
                var created = node.IsDirectory
                    ? FileOperations.CreateFolder(parent.FullPath, name)
                    : FileOperations.CreateFile(parent.FullPath, name);
                RemoveNew(node);
                RefreshPaths([parent.FullPath]);
                var createdNode = parent.Find(created);
                if (createdNode != null) createdNode.IsSelected = true;
                if (!node.IsDirectory)
                {
                    _main.OpenFile(created);
                    _main.FocusEditor();
                }
                return true;
            }
            catch (Exception ex)
            {
                node.EditError = FileErrors.Describe(ex, name);
                return false;
            }
        }

        node.IsEditing = false;
        if (name == node.Name) return true;
        try
        {
            var oldPath = node.FullPath;
            var newPath = FileOperations.Rename(oldPath, name);
            node.Rebase(newPath);
            _main.OnPathRenamed(oldPath, newPath);
            RefreshPaths([Path.GetDirectoryName(newPath)!]);
        }
        catch (Exception ex)
        {
            _main.NotifyError(FileErrors.Describe(ex, node.Name));
        }
        return true;
    }

    public void CancelEdit(ExplorerNode node)
    {
        if (node.IsNew) RemoveNew(node);
        else node.IsEditing = false;
    }

    private void CancelPendingEdits()
    {
        if (Root != null) CancelIn(Root);

        void CancelIn(ExplorerNode n)
        {
            foreach (var c in n.Children.ToList())
            {
                if (c.IsNew) RemoveNew(c);
                else
                {
                    c.IsEditing = false;
                    if (c.IsDirectory && c.IsExpanded) CancelIn(c);
                }
            }
        }
    }

    private void RemoveNew(ExplorerNode node)
    {
        node.IsEditing = false;
        node.Parent?.Children.Remove(node);
    }

    [RelayCommand]
    private void Delete(ExplorerNode? node)
    {
        node ??= Selected;
        if (node == null || node.IsNew || node == Root) return;
        if (!_main.ConfirmDelete(node.FullPath, node.IsDirectory)) return;
        try
        {
            FileOperations.SendToRecycleBin(node.FullPath);
            _main.OnPathDeleted(node.FullPath);
            RefreshPaths([Path.GetDirectoryName(node.FullPath)!]);
        }
        catch (Exception ex)
        {
            _main.NotifyError(FileErrors.Describe(ex, node.FullPath));
        }
    }

    [RelayCommand]
    private void Reveal(ExplorerNode? node)
    {
        var path = (node ?? Selected ?? Root)?.FullPath;
        if (path == null) return;
        try
        {
            var psi = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            if (File.Exists(path)) psi.ArgumentList.Add("/select," + path);
            else psi.ArgumentList.Add(path);
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            _main.NotifyError(FileErrors.Describe(ex, path));
        }
    }

    public void Open(ExplorerNode node)
    {
        if (node.IsNew || node.IsEditing) return;
        if (node.IsDirectory) node.IsExpanded = !node.IsExpanded;
        else _main.OpenFile(node.FullPath);
    }

    /// <summary>Destaca no explorador o arquivo da aba ativa, se estiver visível.</summary>
    public void Reveal(string path)
    {
        if (Root == null || !FileOperations.IsSameOrInside(path, Root.FullPath)) return;
        var node = Root.Find(path);
        if (node != null) node.IsSelected = true;
    }
}
