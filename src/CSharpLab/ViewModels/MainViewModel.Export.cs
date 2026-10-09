using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CSharpLab.Core.Files;
using CSharpLab.Core.Projects;
using CSharpLab.Core.Settings;

namespace CSharpLab.ViewModels;

// Exportar o jogo: um .exe que roda em qualquer Windows, mesmo sem o .NET instalado, numa pasta e num .zip
// prontos para mandar para os amigos.
public sealed partial class MainViewModel
{
    /// <summary>Exportando agora (o botão fica esperando).</summary>
    [ObservableProperty]
    public partial bool IsExporting { get; set; }

    /// <summary>Onde os jogos exportados ficam: Documentos\CSharp Lab\Jogos prontos.</summary>
    public static string ExportFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CSharp Lab", "Jogos prontos");

    [RelayCommand]
    private async Task ExportGame()
    {
        if (IsExporting) return;
        if (GameProject is not { } project)
        {
            Dialogs.ShowError("Exportar o jogo", "Abra a pasta de um jogo com botões para exportar.");
            return;
        }
        if (IsBusy)
        {
            NotifyInfo("O jogo está rodando. Feche a janela dele (ou aperte Parar) e exporte de novo.");
            return;
        }
        if (!SaveProjectFiles(project)) return;

        var output = Path.Combine(ExportFolder, project.Name);
        var zip = output + ".zip";
        IsExporting = true;
        StatusText = "Exportando o jogo… (a primeira vez demora uns minutos: o .NET vai junto, para rodar em qualquer PC)";
        try
        {
            if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
            var result = await Dotnet.RunAsync(
            [
                "publish", project.Path, "-c", "Release", "-r", "win-x64", "--self-contained", "true",
                "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true", "-p:EnableCompressionInSingleFile=true",
                "-p:DebugType=None", "-p:GenerateDocumentationFile=false", "-o", output, "-nologo", "-v:q",
            ], project.Directory);
            if (result.ExitCode != 0)
            {
                StatusText = "";
                Dialogs.ShowError("Não deu para exportar o jogo",
                    "O jogo precisa compilar sem erros (aperte Executar para ver). Se for a primeira exportação, confira a internet: o .NET para o jogo é baixado uma vez.",
                    LastLines(result.Combined, 30));
                return;
            }
            // A explicação do motor (para o autocomplete) não serve para quem só joga.
            var docs = Path.Combine(output, "CSharpLab.Game.xml");
            if (File.Exists(docs)) File.Delete(docs);
            if (File.Exists(zip)) File.Delete(zip);
            ZipFile.CreateFromDirectory(output, zip, CompressionLevel.Optimal, includeBaseDirectory: true);
            StatusText = "";
            NotifyInfo($"Pronto! O jogo está em {output} ({project.Name}.exe). Para mandar para os amigos, use o {project.Name}.zip.");
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{zip}\"") { UseShellExecute = true }); }
            catch (Exception ex) { AppPaths.Log(ex, "Abrindo a pasta do jogo exportado"); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusText = "";
            Dialogs.ShowError("Não deu para exportar o jogo", FileErrors.Describe(ex, output));
        }
        finally
        {
            IsExporting = false;
        }
    }

    private static string LastLines(string text, int count)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join("\n", lines.Skip(Math.Max(0, lines.Length - count)));
    }
}
