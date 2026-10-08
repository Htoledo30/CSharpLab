namespace CSharpLab.GameEngine;

/// <summary>Shade = tom. Deixa a cor mais escura ou mais clara. Exemplo: <c>game.Find("Frame").Shade = Shade.Dark;</c></summary>
public enum Shade
{
    /// <summary>A cor como ela é.</summary>
    Normal,
    /// <summary>Escuro: vermelho escuro, azul escuro… Bom para molduras e fundos.</summary>
    Dark,
    /// <summary>Claro: a cor mais clarinha, quase pastel.</summary>
    Light,
}

/// <summary>Corner = canto. O formato dos cantos da Caixa. Exemplo: <c>game.Find("Portrait").Corner = Corner.Circle;</c></summary>
public enum Corner
{
    /// <summary>Cantos arredondados (o normal).</summary>
    Round,
    /// <summary>Cantos retos, em ângulo.</summary>
    Square,
    /// <summary>Círculo (numa caixa quadrada) ou oval: bom para retratos redondos.</summary>
    Circle,
}

/// <summary>Font = fonte, o desenho das letras. Todas já vêm no Windows. Exemplo: <c>game.Find("Title").Font = Font.Fantasy;</c></summary>
public enum Font
{
    /// <summary>A letra normal, limpa e fácil de ler.</summary>
    Normal,
    /// <summary>Fantasia: letra caprichada, de conto de fadas. Boa para títulos.</summary>
    Fantasy,
    /// <summary>Livro: letra de livro antigo, boa para pergaminhos e histórias.</summary>
    Book,
    /// <summary>À mão: parece escrita com caneta. Boa para bilhetes e diários.</summary>
    Hand,
}

/// <summary>ButtonStyle = estilo do botão. Exemplo: <c>game.Find("Back").Style = ButtonStyle.Outline;</c></summary>
public enum ButtonStyle
{
    /// <summary>Cheio: fundo colorido. O botão mais forte, para a ação principal ("Atacar").</summary>
    Filled,
    /// <summary>Contorno: só a borda colorida. Para ações secundárias.</summary>
    Outline,
    /// <summary>Só texto: sem fundo nem borda. Para ações discretas ("Voltar").</summary>
    Text,
}
