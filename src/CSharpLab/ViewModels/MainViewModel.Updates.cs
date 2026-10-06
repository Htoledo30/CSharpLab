using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CSharpLab.Core.Settings;
using CSharpLab.Core.Updates;

namespace CSharpLab.ViewModels;

// Atualização automática: procura, baixa em segundo plano e aplica ao fechar ou reiniciar.
public sealed partial class MainViewModel
{
    private (Version Version, string AppDir)? _stagedUpdate;
    private bool _relaunchAfterUpdate;
    private readonly CancellationTokenSource _updatesCts = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckUpdatesNowCommand))]
    public partial bool IsCheckingForUpdates { get; set; }

    /// <summary>A janela deve fechar (sem perguntar de novo) para aplicar a atualização.</summary>
    public event Action? ShutdownForUpdateRequested;

    [ObservableProperty]
    public partial string? UpdateMessage { get; set; }

    [ObservableProperty]
    public partial bool UpdateReady { get; set; }

    public bool CheckForUpdatesOnStart
    {
        get => Settings.CheckForUpdates;
        set
        {
            Settings.CheckForUpdates = value;
            OnPropertyChanged();
            ScheduleSettingsSave();
        }
    }

    public string VersionText => "Versão " + UpdateService.CurrentVersion.ToString(3);

    private void AnnounceVersionChange()
    {
        var current = UpdateService.CurrentVersion;
        if (UpdateService.TakeApplyFailure() is { } failure)
            NotifyError("A atualização não foi aplicada: " + failure);
        else if (UpdateService.TryParseVersion(Settings.LastRunVersion ?? "", out var last) && last < current)
            NotifyInfo($"CSharp Lab atualizado para a versão {current.ToString(3)}.");
        if (Settings.LastRunVersion != current.ToString(3))
        {
            Settings.LastRunVersion = current.ToString(3);
            ScheduleSettingsSave();
        }
    }

    private bool CanCheckUpdates() => !IsCheckingForUpdates && !_disposed;

    [RelayCommand(CanExecute = nameof(CanCheckUpdates))]
    private Task CheckUpdatesNow() => CheckForUpdatesAsync(manual: true);

    /// <summary>
    /// Procura uma versão nova e, se houver, já baixa e confere em segundo plano. O aviso só
    /// aparece quando ela está pronta; aplicar é decisão do usuário (agora ou ao fechar).
    /// </summary>
    public async Task CheckForUpdatesAsync(bool manual)
    {
        if (IsCheckingForUpdates || _disposed) return;
        if (!UpdateService.IsConfigured)
        {
            if (manual) NotifyInfo("Esta cópia ainda não tem um repositório de atualizações configurado.");
            return;
        }
        if (_stagedUpdate != null)
        {
            ShowUpdateReady();
            return;
        }

        IsCheckingForUpdates = true;
        var current = UpdateService.CurrentVersion;
        var ct = _updatesCts.Token;
        try
        {
            if (!manual)
            {
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
                if (!Settings.CheckForUpdates) return;
            }
            if (manual) UpdateMessage = "Procurando atualizações…";
            var staged = await Task.Run(() => { UpdateService.CleanUp(current); return UpdateService.FindStaged(current); }, ct);
            ct.ThrowIfCancellationRequested();
            _stagedUpdate = staged;
            if (_stagedUpdate == null)
            {
                var info = await UpdateService.CheckAsync(current, ct);
                if (info == null)
                {
                    if (manual) NotifyInfo($"Você já está na versão mais recente ({current.ToString(3)}).");
                    UpdateMessage = null;
                    return;
                }
                UpdateMessage = $"Baixando a versão {info.Version.ToString(3)}…";
                var appDir = await Task.Run(() => UpdateService.DownloadAndStageAsync(info, null, ct), ct);
                ct.ThrowIfCancellationRequested();
                _stagedUpdate = (info.Version, appDir);
            }
            ShowUpdateReady();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            AppPaths.Log(ex, "Atualização");
            UpdateMessage = null;
            UpdateReady = false;
            // Sem internet ou GitHub fora do ar: só incomoda se o usuário pediu.
            if (manual) NotifyError("Não foi possível procurar atualizações: " + ex.Message);
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    private void ShowUpdateReady()
    {
        if (_stagedUpdate is not { } staged) return;
        UpdateReady = true;
        UpdateMessage = $"A versão {staged.Version.ToString(3)} do CSharp Lab está pronta.";
    }

    /// <summary>Reinicia agora para atualizar (pergunta antes sobre arquivos não salvos).</summary>
    [RelayCommand]
    private void RestartToUpdate()
    {
        if (_stagedUpdate == null || IsBusy && !Dialogs.Confirm("Atualizar agora",
                "Um programa está em execução e será encerrado.", "Encerrar e atualizar")) return;
        if (!ConfirmCloseAll(Documents)) return;
        _relaunchAfterUpdate = true;
        ShutdownForUpdateRequested?.Invoke();
    }

    /// <summary>Esconde o aviso; a atualização é aplicada quando o editor for fechado.</summary>
    [RelayCommand]
    private void UpdateLater()
    {
        UpdateMessage = null;
        UpdateReady = false;
    }
}
