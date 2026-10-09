using CommunityToolkit.Mvvm.ComponentModel;

namespace CSharpLab.ViewModels;

// Estúdio do jogo: um lugar só para o jogo, com todas as cenas e, lado a lado, a tela e o código da cena.
public sealed partial class MainViewModel
{
    /// <summary>O Estúdio está aberto (ele ocupa o lugar das abas e do explorador).</summary>
    [ObservableProperty]
    public partial bool IsStudioOpen { get; set; }

    /// <summary>A cena aberta no Estúdio (fica guardada ao fechar e abrir de novo).</summary>
    [ObservableProperty]
    public partial string? StudioScene { get; set; }

    /// <summary>A pasta do jogo aberto, ou null se a pasta não tem jogo com botões.</summary>
    public string? GameDirectory => GameProject?.Directory;

    /// <summary>O arquivo é do jogo aberto: um .cs dele ou uma tela da pasta Screens.</summary>
    public bool BelongsToGame(DocumentViewModel? doc) =>
        doc?.FilePath is { } path && GameDirectory is { } dir &&
        string.Equals(GameDirectoryOf(path), dir, StringComparison.OrdinalIgnoreCase) &&
        (doc.IsScreen || doc.IsCSharp);

    /// <summary>Abre o Estúdio numa cena (null: a última aberta, ou a primeira do jogo).</summary>
    public void OpenStudio(string? scene = null)
    {
        if (!HasGame) return;
        if (scene != null) StudioScene = scene;
        IsStudioOpen = true;
    }

    public void CloseStudio() => IsStudioOpen = false;

    /// <summary>A cena do "Jogar daqui" na execução atual (null: o jogo começa no game.Start de sempre).</summary>
    private string? _playFrom;

    /// <summary>
    /// Jogar daqui: salva, compila e abre o jogo já nesta cena. As variáveis começam com o valor do começo do
    /// código (o que as outras cenas fariam antes não aconteceu).
    /// </summary>
    public void PlayFromScene(string scene)
    {
        if (IsBusy)
        {
            NotifyInfo("O jogo já está rodando. Feche a janela dele (ou aperte Parar) e tente de novo.");
            return;
        }
        _playFrom = scene;
        _ = RunAsync();
    }
}
