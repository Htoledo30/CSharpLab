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
    /// <summary>Forte: letra alta e grossa, de cartaz. Boa para "VITÓRIA!", "GAME OVER" e números de dano.</summary>
    Strong,
    /// <summary>Clássica: letra de jornal antigo, séria e bonita. Boa para histórias e diálogos.</summary>
    Classic,
    /// <summary>Elegante: letra cursiva, de convite. Boa para nomes de reinos e cartas reais.</summary>
    Elegant,
    /// <summary>Divertida: letra de quadrinho. Boa para jogos engraçados e falas de personagens.</summary>
    Fun,
    /// <summary>Retrô: letra de computador antigo, cada letra do mesmo tamanho. Boa para terminais e jogos de nave.</summary>
    Retro,
    /// <summary>Técnica: letra reta e moderna, de painel. Boa para placares, menus e ficção científica.</summary>
    Tech,
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
    /// <summary>Degradê: claro em cima e escuro embaixo, com borda. Parece um botão de videogame.</summary>
    Gradient,
    /// <summary>Suave: fundo clarinho da cor, sem borda. Bom para opções e abas.</summary>
    Soft,
}

/// <summary>BarStyle = estilo da barra. Exemplo: <c>game.Find("Mana").BarStyle = BarStyle.Shine;</c></summary>
public enum BarStyle
{
    /// <summary>Lisa: uma cor só (o normal).</summary>
    Smooth,
    /// <summary>Brilhante: degradê com brilho e fundo da mesma cor. Boa para mana e magia.</summary>
    Shine,
    /// <summary>Em blocos: a barra dividida em pedaços (um por ponto, até 20). Boa para corações, energia e munição.</summary>
    Blocks,
}

/// <summary>BarText = onde ficam o nome e o número da barra. Exemplo: <c>game.Find("Health").BarText = BarText.Inside;</c></summary>
public enum BarText
{
    /// <summary>Em cima da barra (o normal): "Vida" à esquerda e "30 / 50" à direita.</summary>
    Above,
    /// <summary>Dentro da barra, como nos jogos de luta. A barra fica grossa, da altura da peça.</summary>
    Inside,
    /// <summary>Sem texto: só a barra.</summary>
    None,
}
