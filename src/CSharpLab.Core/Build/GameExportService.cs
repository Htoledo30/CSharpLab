using CSharpLab.Core.Files;
using CSharpLab.Core.Projects;

namespace CSharpLab.Core.Build;

public enum GameExportStage { Preparing, Compiling, CreatingZip, CheckingZip, Installing }

public sealed record GameExportResult(string Folder, string Executable, string Zip, string? PreviousVersion);

public interface IGameExporter
{
    Task<GameExportResult> ExportAsync(ProjectFile project, string destination,
        IProgress<GameExportStage>? progress, CancellationToken cancellationToken);
}

public sealed class GameExportException(string message, string? details = null, Exception? inner = null)
    : Exception(message, inner)
{
    public string? Details { get; } = details;
}

/// <summary>Prepara e verifica tudo antes de substituir uma exportação existente.</summary>
public sealed class GameExportService : IGameExporter
{
    private static readonly string[] PublishProperties =
    [
        "-p:Configuration=Release", "-p:RuntimeIdentifier=win-x64", "-p:SelfContained=true",
        "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:EnableCompressionInSingleFile=true", "-p:DebugType=None", "-p:GenerateDocumentationFile=false",
    ];
    private readonly Func<IReadOnlyList<string>, string, CancellationToken, Task<ProcessResult>> _run;

    public GameExportService() : this((args, cwd, ct) => Dotnet.RunAsync(args, cwd, ct)) { }

    internal GameExportService(Func<IReadOnlyList<string>, string, CancellationToken, Task<ProcessResult>> run)
        => _run = run;

    public Task<GameExportResult> ExportAsync(ProjectFile project, string destination,
        IProgress<GameExportStage>? progress, CancellationToken cancellationToken)
        // Compactação e acesso ao disco também não podem prender a interface.
        => Task.Run(() => ExportCoreAsync(project, destination, progress, cancellationToken), cancellationToken);

    private async Task<GameExportResult> ExportCoreAsync(ProjectFile project, string destination,
        IProgress<GameExportStage>? progress, CancellationToken ct)
    {
        progress?.Report(GameExportStage.Preparing);
        ct.ThrowIfCancellationRequested();
        using var transaction = ExportTransaction.Open(project, destination);
        try
        {
            // AssemblyName pode mudar em Release; não presumimos que o exe se chama como o projeto.
            var nameResult = await _run([
                "msbuild", project.Path, "-nologo", "-v:q", "-tl:off",
                ..PublishProperties, "-getProperty:AssemblyName",
            ], project.Directory, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            var assemblyName = nameResult.StandardOutput.Trim();
            if (nameResult.ExitCode != 0 || FileOperations.ValidateName(assemblyName) != null)
                throw new GameExportException("Não foi possível identificar o executável do jogo.", LastLines(nameResult.Combined));

            progress?.Report(GameExportStage.Compiling);
            var result = await _run([
                "publish", project.Path, ..PublishProperties, "-o", transaction.PreparedFolder, "-nologo", "-v:q",
            ], project.Directory, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (result.ExitCode != 0)
                throw new GameExportException(
                    "O jogo precisa compilar sem erros. Aperte Executar para conferir. Na primeira exportação, o .NET do jogo também precisa ser baixado.",
                    LastLines(result.Combined));

            var executable = assemblyName + ".exe";
            var exePath = Path.Combine(transaction.PreparedFolder, executable);
            if (!File.Exists(exePath) || new FileInfo(exePath).Length == 0)
                throw new GameExportException($"A compilação terminou, mas não gerou o executável esperado: {executable}.");
            var docs = Path.Combine(transaction.PreparedFolder, "CSharpLab.Game.xml");
            if (File.Exists(docs)) File.Delete(docs);

            progress?.Report(GameExportStage.CreatingZip);
            var contents = ExportArchive.Create(transaction.PreparedFolder, transaction.PreparedZip, project.Name, ct);
            progress?.Report(GameExportStage.CheckingZip);
            ExportArchive.Verify(transaction.PreparedZip, contents, ct);
            ct.ThrowIfCancellationRequested();

            // Daqui em diante a troca curta termina ou é desfeita; cancelar não a deixa pela metade.
            progress?.Report(GameExportStage.Installing);
            var previous = transaction.Install();
            return new(transaction.Output, Path.Combine(transaction.Output, executable), transaction.Zip, previous);
        }
        finally
        {
            transaction.CleanIfSafe();
        }
    }

    private static string LastLines(string text)
        => string.Join("\n", text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).TakeLast(30));
}
