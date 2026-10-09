using System.Windows.Input;

namespace CSharpLab.GameEngine;

// Comandos que esperam o jogador no meio do clique (como o Console.ReadLine do terminal),
// teclas da cena e o fim do jogo.
public sealed partial class Game
{
    private Action<ConsoleKey>? _onKey;

    /// <summary>
    /// Pause = pausar. Dentro de um clique, mostra a tela como está e espera o jogador apertar
    /// Continuar (ou Enter). Depois, o código continua na linha de baixo: é o
    /// "aperte Enter para continuar" do terminal. (game.Wait espera um tempo; game.Pause espera o jogador.)
    /// </summary>
    /// <param name="button">O texto do botão. Exemplo: "Abrir o baú".</param>
    /// <example>
    /// <code>
    /// game.Find("Search").OnClick(() =>
    /// {
    ///     game.Write("Você encontrou uma chave!");
    ///     game.Pause();
    ///     game.Clear();
    ///     game.GoTo("Tower");
    /// });
    /// </code>
    /// </example>
    public void Pause(string button = "Continuar")
    {
        ShowBeforeWaiting(nameof(Pause), "game.Pause();");
        _view?.WaitForPlayer(string.IsNullOrWhiteSpace(button) ? "Continuar" : button.Trim());
        if (_closed) throw new GameClosedException();
    }

    /// <summary>
    /// Read = ler. Dentro de um clique, mostra uma pergunta com um campo para escrever e espera a resposta.
    /// A resposta volta como texto, e o código continua na linha de baixo: é o Console.ReadLine() do terminal.
    /// </summary>
    /// <param name="question">A pergunta. Exemplo: "Quantas moedas você aposta?".</param>
    /// <returns>O que o jogador escreveu (sem espaços nas pontas e nunca vazio).</returns>
    /// <example>
    /// <code>
    /// game.Find("Bet").OnClick(() =>
    /// {
    ///     string answer = game.Read("Quantas moedas você aposta?");
    ///     if (int.TryParse(answer, out int bet) &amp;&amp; bet &lt;= gold)
    ///         game.Write($"Você apostou {bet}.");
    ///     else
    ///         game.Write("Aposta inválida.", Color.Red);
    /// });
    /// </code>
    /// </example>
    public string Read(string question)
    {
        ShowBeforeWaiting(nameof(Read), "string answer = game.Read(\"Seu nome?\");");
        var answer = _view?.ReadAnswer(question ?? "") ?? "";
        if (_closed) throw new GameClosedException();
        return answer.Trim();
    }

    /// <summary>
    /// Choose = escolher. Dentro de um clique, mostra uma pergunta com um botão para cada opção e espera o
    /// jogador escolher. Volta o texto da opção escolhida (bom para usar num switch). As teclas 1 a 9 também escolhem.
    /// </summary>
    /// <param name="question">A pergunta. Exemplo: "Abrir o baú?".</param>
    /// <param name="options">As opções, de 2 a 6. Exemplo: "Sim", "Não".</param>
    /// <returns>O texto da opção escolhida, igual ao que você escreveu.</returns>
    /// <example>
    /// <code>
    /// string choice = game.Choose("Um baú velho. O que fazer?", "Abrir", "Chutar", "Deixar");
    /// switch (choice)
    /// {
    ///     case "Abrir":
    ///         gold += 10;
    ///         break;
    ///     case "Chutar":
    ///         health -= 5;
    ///         break;
    /// }
    /// </code>
    /// </example>
    public string Choose(string question, params string[] options)
    {
        if (options == null || options.Length < 2 || options.Length > 6 || options.Any(string.IsNullOrWhiteSpace))
            throw new GameException(
                "game.Choose precisa de 2 a 6 opções com texto, depois da pergunta: game.Choose(\"Abrir o baú?\", \"Sim\", \"Não\");");
        ShowBeforeWaiting(nameof(Choose), "string choice = game.Choose(\"Abrir o baú?\", \"Sim\", \"Não\");");
        int index = _view?.ChooseOption(question ?? "", options) ?? 0;
        if (_closed) throw new GameClosedException();
        return options[Math.Clamp(index, 0, options.Length - 1)];
    }

    /// <summary>
    /// OnKey = ao apertar uma tecla. Fica dentro da cena: o código entre as chaves recebe cada tecla que o
    /// jogador apertar enquanto a cena está na tela (segurar a tecla repete). Compare com ConsoleKey, como no terminal.
    /// Teclas de atalho de botões continuam indo para os botões.
    /// </summary>
    /// <param name="onKey">O que fazer com a tecla: key => { if (key == ConsoleKey.D) x++; }</param>
    /// <example>
    /// <code>
    /// game.Scene("Map", () =>
    /// {
    ///     game.Find("Hero").X = x * 32;
    ///     game.OnKey(key =>
    ///     {
    ///         if (key == ConsoleKey.D || key == ConsoleKey.RightArrow) x++;
    ///         else if (key == ConsoleKey.A || key == ConsoleKey.LeftArrow) x--;
    ///     });
    /// });
    /// </code>
    /// </example>
    public void OnKey(Action<ConsoleKey> onKey)
    {
        if (_building == null || _inEnter)
            throw new GameException(
                "game.OnKey fica direto dentro da cena, fora dos botões: " +
                "game.Scene(\"Map\", () => { game.OnKey(key => { if (key == ConsoleKey.D) x++; }); });");
        _onKey = onKey ?? throw new GameException("O game.OnKey precisa dizer o que fazer com a tecla: game.OnKey(key => { ... });");
    }

    /// <summary>
    /// Close = fechar. Termina o jogo e fecha a janela (bom para um botão "Sair"). Dentro de um clique, o que
    /// vem depois do Close não roda.
    /// </summary>
    /// <example>
    /// <code>
    /// game.Find("Quit").OnClick(() =>
    /// {
    ///     game.Close();
    /// });
    /// </code>
    /// </example>
    public void Close()
    {
        if (_closed) return;
        _closed = true;
        _view?.CloseGame();
        if (_inAction) throw new GameClosedException();
    }

    // ------------------------------------------------------------------ por dentro

    /// <summary>A janela fechou (pelo X ou pelo game.Close): um Wait, Pause, Read ou Choose em andamento para.</summary>
    internal void WindowClosed() => _closed = true;

    /// <summary>Uma tecla da janela: vai para o game.OnKey da cena (se tiver). True se alguém usou a tecla.</summary>
    internal bool PressKey(Key key)
    {
        if (_onKey is not { } onKey || _inAction || _closed || ToConsoleKey(key) is not { } consoleKey) return false;
        Act(() => onKey(consoleKey));
        return true;
    }

    /// <summary>Cada desenho da cena liga o OnKey de novo (como os OnClick): o da cena anterior deixa de valer.</summary>
    private void ClearKeyHandler() => _onKey = null;

    /// <summary>Antes de esperar o jogador: confere onde foi chamado e mostra o que o clique já fez.</summary>
    private void ShowBeforeWaiting(string method, string example)
    {
        if (!_started || _building != null || !_inAction)
            throw new GameException(
                $"game.{method} funciona dentro de um clique (OnClick, OnAnswer ou game.Button), porque ele espera o jogador " +
                $"no meio do clique: game.Find(\"Open\").OnClick(() => {{ {example} }}); " +
                "Para uma pergunta fixa na tela, use a peça Campo de escrita.");
        if (_pendingScene != null)
        {
            _current = _pendingScene;
            _pendingScene = null;
            _entering = true;
        }
        Redraw();
    }

    /// <summary>A tecla da janela com o nome que o terminal usa (ConsoleKey). Null para teclas que o jogo não usa.</summary>
    internal static ConsoleKey? ToConsoleKey(Key key) => key switch
    {
        >= Key.A and <= Key.Z => ConsoleKey.A + (key - Key.A),
        >= Key.D0 and <= Key.D9 => ConsoleKey.D0 + (key - Key.D0),
        >= Key.NumPad0 and <= Key.NumPad9 => ConsoleKey.NumPad0 + (key - Key.NumPad0),
        >= Key.F1 and <= Key.F12 => ConsoleKey.F1 + (key - Key.F1),
        Key.Left => ConsoleKey.LeftArrow,
        Key.Right => ConsoleKey.RightArrow,
        Key.Up => ConsoleKey.UpArrow,
        Key.Down => ConsoleKey.DownArrow,
        Key.Enter => ConsoleKey.Enter,
        Key.Space => ConsoleKey.Spacebar,
        Key.Escape => ConsoleKey.Escape,
        Key.Tab => ConsoleKey.Tab,
        Key.Back => ConsoleKey.Backspace,
        Key.Delete => ConsoleKey.Delete,
        _ => null,
    };
}
