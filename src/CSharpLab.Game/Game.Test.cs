namespace CSharpLab.GameEngine;

// Testar uma cena com valores escolhidos: "Jogar esta cena" (Ctrl+F5) começa o jogo nela, e o game.Test
// dela roda antes, para a luta começar com pouca vida, a loja com muito ouro...
public sealed partial class Game
{
    private readonly Dictionary<string, Action> _tests = new(StringComparer.OrdinalIgnoreCase);
    private Action? _pendingTest;   // o game.Test da cena do "Jogar esta cena", até ele rodar

    /// <summary>A cena do "Jogar esta cena" (vem do CSharp Lab; os testes escolhem direto). Null: o jogo começa no game.Start.</summary>
    internal string? PlayFrom { get; set; }

    /// <summary>
    /// Test = testar. Valores para testar uma cena sem jogar tudo até ela. Quando você usa
    /// "Jogar esta cena" (Ctrl+F5) nessa cena, o jogo já começa nela e este código roda antes
    /// (depois do game.OnEnter dela, se tiver). No F5 de sempre e no jogo exportado, ele não roda.
    /// </summary>
    /// <param name="scene">A cena que você quer testar. Exemplo: "Fight".</param>
    /// <param name="setValues">Os valores do teste: () => { health = 3; }</param>
    /// <example>
    /// <code>
    /// // Ctrl+F5 com a cena Fight aberta: a luta começa com o herói quase morto.
    /// game.Test("Fight", () =>
    /// {
    ///     health = 3;
    ///     potions = 0;
    /// });
    /// </code>
    /// </example>
    public void Test(string scene, Action setValues)
    {
        const string example = "game.Test(\"Fight\", () => { health = 3; });";
        if (_started || _building != null)
            throw new GameException("game.Test fica fora das cenas, antes do game.Start: " + example);
        if (string.IsNullOrWhiteSpace(scene))
            throw new GameException("O game.Test precisa do nome da cena que você quer testar. Exemplo: " + example);
        if (setValues == null)
            throw new GameException("O game.Test precisa dizer os valores do teste. Exemplo: " + example);
        if (!_tests.TryAdd(scene.Trim(), setValues))
            throw new GameException($"Já existe um game.Test para a cena \"{scene.Trim()}\". Junte os valores num só.");
    }

    /// <summary>Confere os nomes dos game.Test e, no "Jogar esta cena", começa nela com o teste pronto para rodar.</summary>
    private void PrepareTest()
    {
        foreach (var name in _tests.Keys)
        {
            try
            {
                FindScene(name);
            }
            catch (GameException ex)
            {
                throw new GameException($"No game.Test(\"{name}\", ...): {ex.Message}");
            }
        }
        if (PlayFrom is not { } from || !_scenes.ContainsKey(from.Trim())) return;
        _current = FindScene(from);
        _pendingTest = _tests.GetValueOrDefault(_current);
    }

    /// <summary>Roda o game.Test que falta (uma vez só).</summary>
    private void RunPendingTest()
    {
        if (_pendingTest is not { } test) return;
        _pendingTest = null;
        test();
    }
}
