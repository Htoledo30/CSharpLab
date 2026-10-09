using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.Input;
using CSharpLab.Core.Files;
using CSharpLab.Core.Projects;
using CSharpLab.Core.Settings;

namespace CSharpLab.ViewModels;

// Abas: abrir, fechar, salvar, alterações feitas fora do editor e recuperação de texto não salvo.
public sealed partial class MainViewModel
{
    private const int MaxFileBytes = 8 * 1024 * 1024;

    private readonly RecoveryStore _recovery = new();
    private readonly Dictionary<string, int> _recoveryVersions = [];
    private bool _askingExternalChange;
    private int _untitledCounter;

    public DocumentViewModel? FindDocument(string path)
    {
        var full = Path.GetFullPath(path);
        return Documents.FirstOrDefault(d => d.FilePath != null && string.Equals(d.FilePath, full, StringComparison.OrdinalIgnoreCase));
    }

    private DocumentViewModel? FindByKey(string key) =>
        Documents.FirstOrDefault(d => string.Equals(d.LanguageKey, key, StringComparison.OrdinalIgnoreCase));

    private string DisplayNameFor(string? key)
    {
        if (key == null) return "Projeto";
        var doc = FindByKey(key);
        if (doc != null) return doc.Hint != null ? $"{doc.Title} ({doc.Hint})" : doc.Title;
        return ProblemsViewModel.FileNameOf(key);
    }

    public DocumentViewModel NewDraft(string text = "")
    {
        var doc = new DocumentViewModel(text, null, TextFileIO.Utf8NoBom, null, ++_untitledCounter);
        AddDocument(doc, activate: true);
        return doc;
    }

    public void FocusEditor() => FocusEditorRequested?.Invoke();

    [RelayCommand]
    private void NewFile()
    {
        NewDraft();
        FocusEditorRequested?.Invoke();
    }

    public DocumentViewModel? OpenFile(string path, bool activate = true)
    {
        path = Path.GetFullPath(path);
        var existing = FindDocument(path);
        if (existing != null)
        {
            if (activate) ActiveDocument = existing;
            return existing;
        }

        try
        {
            var info = new FileInfo(path);
            if (info.Length > MaxFileBytes)
            {
                NotifyError($"\"{info.Name}\" é grande demais para o editor.");
                return null;
            }
            var content = TextFileIO.Read(path);
            if (content.Text.AsSpan(0, Math.Min(8000, content.Text.Length)).Contains('\0'))
            {
                NotifyError($"\"{info.Name}\" não parece ser um arquivo de texto.");
                return null;
            }
            var doc = new DocumentViewModel(content.Text, path, content.Encoding, content.Stamp);
            AddDocument(doc, activate);
            return doc;
        }
        catch (Exception ex)
        {
            NotifyError(FileErrors.Describe(ex, path));
            return null;
        }
    }

    private void AddDocument(DocumentViewModel doc, bool activate)
    {
        // Um rascunho inicial intocado é substituído pelo primeiro arquivo aberto.
        var pristine = Documents.Count == 1 && Documents[0] is { IsUntitled: true, IsDirty: false } only &&
                       only.Document.Text == ProjectCreator.DefaultProgram && !doc.IsUntitled
            ? Documents[0]
            : null;

        doc.LivesInStudio = doc.IsScreen && BelongsToGame(doc);
        Documents.Add(doc);
        doc.TextChanged += OnDocumentTextChanged;
        doc.PropertyChanged += OnDocumentPropertyChanged;
        AttachLanguage(doc);
        SyncScreenText(doc);
        UpdateHints();
        if (activate) ActiveDocument = doc;
        if (pristine != null) RemoveDocument(pristine);
        ScheduleSettingsSave();
    }

    private void AttachLanguage(DocumentViewModel doc)
    {
        if (_ls != null && doc.IsCSharp)
            _ls.OpenDocument(doc.LanguageKey, doc.FilePath, doc.SourceText, doc.Version);
    }

    private void OnDocumentTextChanged(DocumentViewModel doc)
    {
        if (_disposed) return;
        if (doc.IsCSharp) _ls?.UpdateDocument(doc.LanguageKey, doc.SourceText, doc.Version);
        SyncScreenText(doc);
        Problems.InvalidateBuildFor(doc.LanguageKey);
        ScheduleDiagnostics();
        ScheduleRecovery();
    }

    /// <summary>Telas abertas valem pelo texto da aba (mesmo sem salvar) para as sugestões e avisos do game.Find.</summary>
    private static void SyncScreenText(DocumentViewModel doc)
    {
        if (doc.IsScreen) GameScreens.OpenTexts[Path.GetFullPath(doc.FilePath!)] = doc.Document.Text;
    }

    private void OnDocumentPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentViewModel.IsDirty)) ScheduleRecovery();
    }

    /// <summary>Distingue abas com o mesmo nome mostrando a pasta.</summary>
    private void UpdateHints()
    {
        foreach (var group in Documents.GroupBy(d => d.Title, StringComparer.OrdinalIgnoreCase))
        {
            var list = group.ToList();
            foreach (var d in list)
            {
                d.Hint = list.Count > 1 && d.FilePath != null
                    ? Path.GetFileName(Path.GetDirectoryName(d.FilePath)) is { Length: > 0 } dir ? dir : null
                    : null;
            }
        }
    }

    [RelayCommand]
    private void CloseTab(DocumentViewModel? doc)
    {
        doc ??= ActiveDocument;
        if (doc == null) return;
        if (doc.IsDirty)
        {
            switch (Dialogs.AskSaveChanges([doc.Title]))
            {
                case SaveChoice.Cancel:
                    return;
                case SaveChoice.Save:
                    if (!Save(doc)) return;
                    break;
            }
        }
        RemoveDocument(doc);
    }

    private void RemoveDocument(DocumentViewModel doc)
    {
        var index = Documents.IndexOf(doc);
        if (index < 0) return;
        doc.TextChanged -= OnDocumentTextChanged;
        doc.PropertyChanged -= OnDocumentPropertyChanged;
        if (doc.IsCSharp) _ls?.CloseDocument(doc.LanguageKey);
        if (doc.IsScreen) GameScreens.OpenTexts.TryRemove(Path.GetFullPath(doc.FilePath!), out _);
        _recovery.Delete(doc.RecoveryId);
        _recoveryVersions.Remove(doc.RecoveryId);
        Documents.RemoveAt(index);
        if (ActiveDocument == doc)
            ActiveDocument = Documents.Count == 0 ? null : Documents[Math.Min(index, Documents.Count - 1)];
        UpdateHints();
        ScheduleDiagnostics();
        ScheduleSettingsSave();
    }

    /// <summary>Pergunta sobre alterações não salvas. Retorna false se o usuário cancelar ou se salvar falhar.</summary>
    public bool ConfirmCloseAll(IEnumerable<DocumentViewModel> docs)
    {
        var dirty = docs.Where(d => d.IsDirty).ToList();
        if (dirty.Count == 0) return true;
        switch (Dialogs.AskSaveChanges(dirty.Select(d => d.Title).ToList()))
        {
            case SaveChoice.Cancel:
                return false;
            case SaveChoice.Save:
                foreach (var d in dirty)
                {
                    if (!Save(d)) return false;
                }
                return true;
            default:
                foreach (var d in dirty) _recovery.Delete(d.RecoveryId);
                return true;
        }
    }

    [RelayCommand]
    private void SaveActive()
    {
        if (ActiveDocument != null) Save(ActiveDocument);
    }

    [RelayCommand]
    private void SaveActiveAs()
    {
        if (ActiveDocument != null) SaveAs(ActiveDocument);
    }

    [RelayCommand]
    private void SaveAll()
    {
        foreach (var d in Documents.Where(d => d.IsDirty).ToList())
        {
            if (!Save(d)) return;
        }
    }

    public bool Save(DocumentViewModel doc) => doc.FilePath == null ? SaveAs(doc) : SaveTo(doc, doc.FilePath);

    public bool SaveAs(DocumentViewModel doc)
    {
        var initial = doc.FilePath != null ? Path.GetDirectoryName(doc.FilePath) : CurrentFolder ?? Settings.LastProjectLocation;
        var path = Dialogs.PickSaveFile(doc.FileName, initial);
        if (path == null) return false;
        path = Path.GetFullPath(path);
        var other = FindDocument(path);
        if (other != null && other != doc)
        {
            Dialogs.ShowError("Não foi possível salvar", $"\"{Path.GetFileName(path)}\" já está aberto em outra aba.");
            return false;
        }
        var oldKey = doc.LanguageKey;
        var wasCSharp = doc.IsCSharp;
        if (!SaveTo(doc, path)) return false;
        if (doc.FilePath == null || !string.Equals(doc.FilePath, path, StringComparison.OrdinalIgnoreCase))
        {
            doc.FilePath = path;
            if (wasCSharp && doc.IsCSharp) _ls?.MoveDocument(oldKey, doc.LanguageKey, path);
            else if (wasCSharp) _ls?.CloseDocument(oldKey);
            else AttachLanguage(doc);
            UpdateHints();
            ScheduleDiagnostics();
        }
        return true;
    }

    private bool SaveTo(DocumentViewModel doc, string path)
    {
        // Salvar no próprio arquivo: confere no disco se ele mudou desde a última leitura/gravação,
        // mesmo que o monitor da pasta ainda não tenha avisado (ou o arquivo esteja fora da pasta).
        if (doc.FilePath != null && string.Equals(doc.FilePath, path, StringComparison.OrdinalIgnoreCase) &&
            ChangedOnDisk(doc, path) &&
            !Dialogs.Confirm("Arquivo alterado fora do editor",
                $"\"{doc.Title}\" foi alterado por outro programa depois que você o abriu. Salvar vai substituir essa versão pela do editor.",
                "Substituir", danger: true))
        {
            return false;
        }

        try
        {
            var stamp = TextFileIO.Save(path, doc.Document.Text, doc.Encoding);
            doc.MarkSaved(stamp);
            _recovery.Delete(doc.RecoveryId);
            _recoveryVersions.Remove(doc.RecoveryId);
            return true;
        }
        catch (UnrepresentableTextException ex) when (doc.Encoding is not (UTF8Encoding or UnicodeEncoding))
        {
            // Nada é trocado em silêncio: o usuário decide converter para UTF-8 ou cancelar.
            if (!Dialogs.Confirm("Salvar em UTF-8?",
                    $"\"{doc.Title}\" usa {ex.EncodingName}, que não tem alguns caracteres do texto (por exemplo \"{ex.Sample}\"). " +
                    "Salvar em UTF-8 mantém todos eles.",
                    "Salvar em UTF-8"))
            {
                return false;
            }
            doc.Encoding = TextFileIO.Utf8NoBom;
            return SaveTo(doc, path);
        }
        catch (Exception ex)
        {
            Dialogs.ShowError("Não foi possível salvar", FileErrors.Describe(ex, path));
            return false;
        }
    }

    /// <summary>O arquivo no disco não é mais o que o editor leu ou gravou por último.</summary>
    private static bool ChangedOnDisk(DocumentViewModel doc, string path)
    {
        var stamp = FileStamp.Of(path);
        if (stamp == null || doc.DiskStamp == null || stamp == doc.DiskStamp) return false;
        try
        {
            // Data diferente mas mesmo conteúdo (ex.: outro programa só "tocou" o arquivo) não conta.
            var current = TextFileIO.Read(path);
            return !doc.MatchesDiskVersion(current.Text);
        }
        catch
        {
            return true;
        }
    }

    /// <summary>Compara cada aba com o arquivo no disco (chamado em eventos da pasta e ao ativar a janela).</summary>
    public void CheckAllExternalChanges()
    {
        if (_askingExternalChange) return;
        foreach (var doc in Documents.ToList())
            CheckExternalChange(doc);
    }

    private void CheckExternalChange(DocumentViewModel doc)
    {
        if (doc.FilePath == null) return;
        var stamp = FileStamp.Of(doc.FilePath);
        if (stamp == null)
        {
            if (doc.DiskStamp != null)
            {
                doc.DiskStamp = null;
                doc.ForceDirty();
                NotifyInfo($"\"{doc.Title}\" foi excluído fora do editor. O texto continua aberto; salve para recriá-lo.");
            }
            return;
        }
        if (doc.DiskStamp == stamp) return;

        TextFileContent content;
        try
        {
            content = TextFileIO.Read(doc.FilePath);
        }
        catch
        {
            return; // Provavelmente ainda está sendo gravado; tenta no próximo evento.
        }
        if (content.Text == doc.Document.Text)
        {
            doc.DiskStamp = content.Stamp;
            if (doc.IsDirty) doc.MarkSaved(content.Stamp);
            return;
        }

        if (!doc.IsDirty)
        {
            doc.ReloadFrom(content);
            return;
        }

        _askingExternalChange = true;
        try
        {
            var choice = Dialogs.AskExternalChange(doc.Title);
            if (choice == ExternalChangeChoice.Reload)
            {
                doc.ReloadFrom(content);
                _recovery.Delete(doc.RecoveryId);
            }
            else
            {
                // Mantém o texto do editor; o arquivo só muda quando o usuário salvar.
                doc.DiskStamp = content.Stamp;
            }
        }
        finally
        {
            _askingExternalChange = false;
        }
    }

    // Recuperação: cópias do texto não salvo, gravadas em segundo plano.

    private void RestoreRecoveredDocuments()
    {
        foreach (var entry in _recovery.LoadAll())
        {
            try
            {
                DocumentViewModel doc;
                if (entry.OriginalPath != null)
                {
                    var existing = FindDocument(entry.OriginalPath);
                    if (existing != null)
                        doc = new DocumentViewModel(entry.Text, null, entry.GetEncoding(), null, ++_untitledCounter, entry.Id);
                    else
                    {
                        doc = new DocumentViewModel(entry.Text, entry.OriginalPath, entry.GetEncoding(), null, recoveryId: entry.Id);
                        if (File.Exists(entry.OriginalPath))
                        {
                            try
                            {
                                var disk = TextFileIO.Read(entry.OriginalPath);
                                doc.SetDiskBaseline(disk.Text, disk.Stamp);
                            }
                            catch (Exception ex) { AppPaths.Log(ex, "Lendo arquivo de uma recuperação"); }
                        }
                    }
                }
                else
                {
                    doc = new DocumentViewModel(entry.Text, null, entry.GetEncoding(), null, ++_untitledCounter, entry.Id);
                }
                doc.ForceDirty();
                AddDocument(doc, activate: true);
                // A cópia existente continua válida até salvar ou descartar explicitamente.
                _recoveryVersions[doc.RecoveryId] = doc.IsUntitled && entry.OriginalPath != null ? -1 : doc.Version;
            }
            catch (Exception ex)
            {
                AppPaths.Log(ex, "Restaurando rascunho");
            }
        }
        if (Documents.Any(d => d.IsDirty))
        {
            NotifyInfo("Alterações não salvas da última sessão foram recuperadas.");
            ScheduleRecovery();
        }
    }

    private void ScheduleRecovery()
    {
        if (_disposed) return;
        _recoveryTimer.Stop();
        _recoveryTimer.Start();
    }

    private void WriteRecovery()
    {
        for (int i = 0; i < Documents.Count; i++)
        {
            var doc = Documents[i];
            if (!doc.IsDirty)
            {
                if (_recoveryVersions.Remove(doc.RecoveryId)) _recovery.Delete(doc.RecoveryId);
                continue;
            }
            if (_recoveryVersions.TryGetValue(doc.RecoveryId, out var v) && v == doc.Version) continue;
            var entry = RecoveryStore.Create(doc.RecoveryId, doc.FilePath, doc.IsUntitled ? doc.Title : null, doc.Document.Text, doc.Encoding, i);
            _recoveryVersions[doc.RecoveryId] = doc.Version;
            _recovery.Save(entry);
        }
    }
}
