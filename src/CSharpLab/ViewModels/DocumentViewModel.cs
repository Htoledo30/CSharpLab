using System.ComponentModel;
using System.Security.Cryptography;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CSharpLab.Core.Files;
using CSharpLab.Core.Language;
using ICSharpCode.AvalonEdit.Document;
using Microsoft.CodeAnalysis.Text;

namespace CSharpLab.ViewModels;

/// <summary>Um arquivo aberto numa aba: texto, caminho, estado de alteração e diagnósticos.</summary>
public sealed partial class DocumentViewModel : ObservableObject
{
    private bool _forceDirty;
    private SourceText _sourceText;
    private int _changesSinceBaseline;

    public DocumentViewModel(string text, string? filePath, Encoding encoding, FileStamp? stamp, int untitledNumber = 0, string? recoveryId = null)
    {
        Id = recoveryId ?? Guid.NewGuid().ToString("N");
        Document = new TextDocument(text);
        Document.UndoStack.MarkAsOriginalFile();
        _sourceText = SourceText.From(text);
        FilePath = filePath;
        Encoding = encoding;
        DiskStamp = stamp;
        UntitledNumber = untitledNumber;
        Title = ComputeTitle();
        if (filePath != null && stamp != null) _diskHash = Hash(text);
        Document.Changed += OnDocumentChanged;
        Document.UndoStack.PropertyChanged += OnUndoStackChanged;
    }

    public string Id { get; }
    public TextDocument Document { get; }
    private Encoding _encoding = TextFileIO.Utf8NoBom;

    public Encoding Encoding
    {
        get => _encoding;
        set
        {
            _encoding = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EncodingName));
        }
    }
    public FileStamp? DiskStamp { get; set; }
    public int UntitledNumber { get; }
    public int Version { get; private set; } = 1;

    /// <summary>Disparado a cada alteração de texto (já com a nova versão).</summary>
    public event Action<DocumentViewModel>? TextChanged;

    [ObservableProperty]
    public partial string? FilePath { get; set; }

    [ObservableProperty]
    public partial string Title { get; set; }

    /// <summary>Pasta mostrada ao lado do nome quando há abas com o mesmo nome.</summary>
    [ObservableProperty]
    public partial string? Hint { get; set; }

    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    [ObservableProperty]
    public partial bool IsActive { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<CodeDiagnostic> Diagnostics { get; set; } = [];

    /// <summary>Recuperação local associada (id no RecoveryStore).</summary>
    public string RecoveryId => Id;

    public bool IsUntitled => FilePath == null;

    public bool IsCSharp => FilePath == null || FilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);

    /// <summary>Tela de um jogo (Screens/*.json): abre no editor visual (aba Tela).</summary>
    public bool IsScreen => FilePath != null && FilePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(Path.GetFileName(Path.GetDirectoryName(FilePath)), "Screens", StringComparison.OrdinalIgnoreCase);

    public string LanguageKey => FilePath != null ? LanguageService.KeyFor(FilePath) : LanguageService.UntitledKey(Id);

    public string ToolTip => FilePath ?? "Rascunho ainda não salvo";

    public string FileName => FilePath != null ? Path.GetFileName(FilePath) : Title;

    public string EncodingName => TextFileIO.DescribeEncoding(Encoding);

    public string LineEnding => TextFileIO.DetectLineEnding(Document.Text);

    /// <summary>Texto atual para o Roslyn, mantido de forma incremental.</summary>
    public SourceText SourceText
    {
        get
        {
            if (_changesSinceBaseline > 400)
            {
                _sourceText = SourceText.From(Document.Text);
                _changesSinceBaseline = 0;
            }
            return _sourceText;
        }
    }

    private string ComputeTitle() => FilePath != null
        ? Path.GetFileName(FilePath)
        : UntitledNumber <= 1 ? "Sem título.cs" : $"Sem título {UntitledNumber}.cs";

    partial void OnFilePathChanged(string? value)
    {
        Title = ComputeTitle();
        OnPropertyChanged(nameof(ToolTip));
        OnPropertyChanged(nameof(IsUntitled));
        OnPropertyChanged(nameof(IsCSharp));
        OnPropertyChanged(nameof(FileName));
    }

    private void OnDocumentChanged(object? sender, DocumentChangeEventArgs e)
    {
        try
        {
            _sourceText = _sourceText.WithChanges(new TextChange(new TextSpan(e.Offset, e.RemovalLength), e.InsertedText.Text));
            _changesSinceBaseline++;
        }
        catch (ArgumentException)
        {
            _sourceText = SourceText.From(Document.Text);
            _changesSinceBaseline = 0;
        }
        Version++;
        UpdateDirty();
        TextChanged?.Invoke(this);
    }

    private void OnUndoStackChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UndoStack.IsOriginalFile))
            UpdateDirty();
    }

    private byte[]? _diskHash;

    private static byte[] Hash(string text) => SHA256.HashData(Encoding.UTF8.GetBytes(text));

    /// <summary>Verdadeiro se o texto é igual ao que o editor leu ou gravou por último no disco.</summary>
    public bool MatchesDiskVersion(string text) => _diskHash != null && Hash(text).AsSpan().SequenceEqual(_diskHash);

    public void SetDiskBaseline(string text, FileStamp stamp)
    {
        DiskStamp = stamp;
        _diskHash = Hash(text);
    }

    private void UpdateDirty() => IsDirty = _forceDirty || !Document.UndoStack.IsOriginalFile;

    /// <summary>Marca como alterado mesmo sem histórico de edição (texto recuperado, arquivo excluído).</summary>
    public void ForceDirty()
    {
        _forceDirty = true;
        UpdateDirty();
    }

    public void MarkSaved(FileStamp stamp)
    {
        DiskStamp = stamp;
        _diskHash = Hash(Document.Text);
        _forceDirty = false;
        Document.UndoStack.MarkAsOriginalFile();
        UpdateDirty();
    }

    /// <summary>Recarrega do disco mantendo o desfazer possível e o cursor próximo.</summary>
    public void ReloadFrom(TextFileContent content)
    {
        Encoding = content.Encoding;
        Document.Replace(0, Document.TextLength, content.Text);
        MarkSaved(content.Stamp);
    }
}
