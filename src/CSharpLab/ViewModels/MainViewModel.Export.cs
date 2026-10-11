using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CSharpLab.Core.Build;
using CSharpLab.Core.Files;
using CSharpLab.Core.Projects;
using CSharpLab.Core.Settings;

namespace CSharpLab.ViewModels;

public sealed partial class MainViewModel
{
    private CancellationTokenSource? _exportCts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy), nameof(RunButtonText), nameof(RunButtonToolTip), nameof(RunTargetName),
        nameof(ExportButtonText), nameof(CanUseExportButton))]
    public partial bool IsExporting { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RunButtonText), nameof(RunButtonToolTip), nameof(ExportButtonText), nameof(CanUseExportButton))]
    public partial bool IsExportFinalizing { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RunButtonText), nameof(ExportButtonText), nameof(CanUseExportButton))]
    public partial bool IsExportCancelling { get; set; }

    public string ExportButtonText => IsExportFinalizing ? "Finalizando…" : IsExportCancelling ? "Cancelando…" : IsExporting ? "Cancelar exportação" : "Exportar";
    public bool CanUseExportButton => RunState == RunState.Idle && !IsExportFinalizing && !IsExportCancelling;

    public static string ExportFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CSharp Lab", "Jogos prontos");

    internal IGameExporter Exporter { get; set; } = new GameExportService();
    internal string ExportDestination { get; set; } = ExportFolder;
    internal Action<string> RevealExport { get; set; } = zip =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{zip}\"") { UseShellExecute = true });

    // O botão do Estúdio continua clicável durante a preparação para permitir cancelar.
    [RelayCommand]
    private void ExportOrCancel()
    {
        if (IsExporting) CancelExport();
        else ExportGameCommand.Execute(null);
    }

    [RelayCommand]
    private void CancelExport()
    {
        if (!IsExporting || IsExportFinalizing || _exportCts == null || _exportCts.IsCancellationRequested) return;
        _exportCts.Cancel();
        IsExportCancelling = true;
        StatusText = "Cancelando exportação…";
    }

    [RelayCommand]
    private async Task ExportGame()
    {
        if (_disposed || IsExporting) return;
        if (GameProject is not { } project)
        {
            Dialogs.ShowError("Exportar o jogo", "Abra a pasta de um jogo com botões para exportar.");
            return;
        }
        if (IsBusy)
        {
            NotifyInfo("Feche o jogo (ou aperte Parar) antes de exportar.");
            return;
        }

        using var exportCts = _exportCts = new CancellationTokenSource();
        var ct = exportCts.Token;
        IsExporting = true;
        StatusText = "Preparando exportação…";
        try
        {
            if (!await EnsureSdkAsync(project, ct))
            {
                if (!_disposed) StatusText = "Exportação não iniciada. Confira o aviso sobre o SDK do .NET.";
                return;
            }
            ct.ThrowIfCancellationRequested();
            if (!SaveProjectFiles(project))
            {
                StatusText = "Exportação cancelada: um arquivo não foi salvo.";
                return;
            }
            await Task.Run(() => GameKit.RefreshProject(project.Directory, project.Path), ct);
            ct.ThrowIfCancellationRequested();
            var progress = new Progress<GameExportStage>(stage =>
            {
                if (_disposed || _exportCts != exportCts || ct.IsCancellationRequested) return;
                IsExportFinalizing = stage == GameExportStage.Installing;
                StatusText = stage switch
                {
                    GameExportStage.Preparing => "Preparando exportação…",
                    GameExportStage.Compiling => "Compilando o jogo… Na primeira exportação, o .NET também é baixado e isso pode levar alguns minutos.",
                    GameExportStage.CreatingZip => "Criando o ZIP do jogo…",
                    GameExportStage.CheckingZip => "Conferindo os arquivos do ZIP…",
                    _ => "Guardando a versão anterior e finalizando…",
                };
            });
            var result = await Exporter.ExportAsync(project, ExportDestination, progress, ct);
            if (_disposed) return;
            StatusText = "Exportação concluída.";
            var backup = result.PreviousVersion == null ? "" : " A versão anterior foi guardada na pasta \"Versões anteriores\" de Jogos prontos.";
            NotifyInfo($"Pronto! Para jogar, abra {Path.GetFileName(result.Executable)} em {result.Folder}. Para compartilhar, use {Path.GetFileName(result.Zip)}.{backup}");
            try { RevealExport(result.Zip); }
            catch (Exception ex) { AppPaths.Log(ex, "Abrindo a pasta do jogo exportado"); }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (!_disposed) StatusText = "Exportação cancelada. A versão anterior foi preservada.";
        }
        catch (Exception ex)
        {
            AppPaths.Log(ex, "Exportando o jogo");
            if (!_disposed)
            {
                StatusText = "Não foi possível concluir a exportação.";
                if (ex is GameExportException exportError)
                    Dialogs.ShowError("Não deu para exportar o jogo", exportError.Message, exportError.Details);
                else
                    Dialogs.ShowError("Não deu para exportar o jogo", FileErrors.Describe(ex, ExportDestination));
            }
        }
        finally
        {
            _exportCts = null;
            IsExportFinalizing = false;
            IsExportCancelling = false;
            IsExporting = false;
        }
    }
}
