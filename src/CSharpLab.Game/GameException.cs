namespace CSharpLab.GameEngine;

/// <summary>
/// Erro de uso do motor (uma cena que não existe, um botão fora de uma cena…).
/// A mensagem já é em português e diz o que fazer.
/// </summary>
public sealed class GameException : Exception
{
    /// <summary>Cria o erro com a explicação em português.</summary>
    public GameException(string message) : base(message) { }
}
