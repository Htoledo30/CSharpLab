namespace CSharpLab.GameEngine;

/// <summary>Cores para textos e barras. Exemplo: <c>game.Write("Cuidado!", Color.Red);</c></summary>
public enum Color
{
    /// <summary>Branco (a cor normal dos textos).</summary>
    White,
    /// <summary>Cinza: bom para detalhes e dicas.</summary>
    Gray,
    /// <summary>Vermelho: perigo, dano, inimigo.</summary>
    Red,
    /// <summary>Verde: vida, cura, sucesso.</summary>
    Green,
    /// <summary>Azul: mana, água, magia.</summary>
    Blue,
    /// <summary>Dourado: ouro, prêmios, coisas raras.</summary>
    Gold,
    /// <summary>Roxo: mistério, veneno.</summary>
    Purple,
    /// <summary>Laranja: fogo, alerta.</summary>
    Orange,
}
