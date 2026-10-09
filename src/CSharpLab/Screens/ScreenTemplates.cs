using CSharpLab.GameEngine;

namespace CSharpLab.Screens;

/// <summary>
/// Um modelo de tela: as peças já arrumadas e bonitas, e o começo do código da cena (os botões ligados, com
/// comentários do que falta). O que o jogo faz continua sendo escrito pela pessoa.
/// </summary>
/// <param name="Id">Nome curto, em inglês (para os testes e para lembrar a escolha).</param>
/// <param name="Code">As linhas de dentro do game.Scene (sem o recuo).</param>
internal sealed record ScreenTemplate(string Id, string Title, string Description, Func<string, ScreenLayout> Layout, Func<string, IReadOnlyList<string>> Code);

/// <summary>Os modelos que aparecem ao criar uma cena: Em branco, Abertura, História, Escolhas, Combate e Loja.</summary>
internal static class ScreenTemplates
{
    public static IReadOnlyList<ScreenTemplate> All { get; } =
    [
        new("Blank", "Em branco", "Só um título e um botão Continuar. Você monta o resto.", ScreenLayout.CreateDefault, Blank),
        new("Opening", "Abertura", "O nome do jogo bem grande e o botão Começar (o Enter também começa).", Opening, OpeningCode),
        new("Story", "História", "Um personagem falando, com um texto comprido que rola, e Continuar.", Story, StoryCode),
        new("Choices", "Escolhas", "Uma situação e três caminhos para o jogador escolher, com as mensagens ao lado.", Choices, ChoicesCode),
        new("Fight", "Combate", "Inimigo, barras de vida e mana, Atacar, Magia, Poção e Fugir.", Fight, FightCode),
        new("Shop", "Loja", "Um cartão para cada item à venda, o ouro do jogador e Voltar.", Shop, ShopCode),
    ];

    public static ScreenTemplate? Find(string? id) => All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));

    // ------------------------------------------------------------------ montagem

    private static Piece P(PieceType type, string name, double x, double y, double width, double height, Action<Piece>? style = null)
    {
        var piece = Piece.CreateDefault(type, name, x, y);
        (piece.Width, piece.Height) = (width, height);
        if (type == PieceType.Bar) piece.Color = null;
        style?.Invoke(piece);
        return piece;
    }

    private static ScreenLayout Layout(params Piece[] pieces)
    {
        var layout = new ScreenLayout();
        layout.Pieces.AddRange(pieces);
        return layout;
    }

    private static IReadOnlyList<string> Blank(string scene) =>
    [
        "game.Find(\"Continue\").OnClick(() =>",
        "{",
        "    game.Write(\"Você clicou em Continuar!\");",
        "});",
    ];

    // ------------------------------------------------------------------ Abertura

    private static ScreenLayout Opening(string scene) => Layout(
        P(PieceType.Box, "Backdrop", 180, 80, 600, 380, p => { p.Opacity = 70; p.Shadow = true; }),
        P(PieceType.Text, "Icon", 180, 100, 600, 74, p => { p.Text = "👑"; p.Size = 48; p.Align = TextAlign.Center; p.Color = Color.Gold; }),
        P(PieceType.Text, "Title", 200, 180, 560, 72, p => { p.Text = "Nome do Jogo"; p.Size = 48; p.Bold = true; p.Align = TextAlign.Center; p.Color = Color.Gold; p.Shadow = true; }),
        P(PieceType.Text, "Subtitle", 200, 256, 560, 34, p => { p.Text = "Uma aventura feita por você"; p.Size = 18; p.Align = TextAlign.Center; p.Color = Color.Gray; p.Italic = true; }),
        P(PieceType.Button, "Play", 350, 318, 260, 58, p => { p.Text = "▶  Começar"; p.Size = 20; p.Color = Color.Green; p.Style = ButtonStyle.Gradient; p.Corner = Corner.Circle; p.Bold = true; p.Shadow = true; p.Shortcut = "Enter"; }),
        P(PieceType.Text, "Hint", 200, 396, 560, 28, p => { p.Text = "Aperte Enter para começar"; p.Size = 14; p.Align = TextAlign.Center; p.Color = Color.Gray; }));

    private static IReadOnlyList<string> OpeningCode(string scene) =>
    [
        "game.Find(\"Play\").OnClick(() =>",
        "{",
        "    // Para onde o jogo vai? Troque pelo nome da primeira cena, por exemplo: game.GoTo(\"Village\");",
        "    game.Write(\"Começou!\");",
        "});",
    ];

    // ------------------------------------------------------------------ História

    private static ScreenLayout Story(string scene) => Layout(
        P(PieceType.Box, "Panel", 40, 36, 880, 392, p => p.Opacity = 85),
        P(PieceType.Box, "PortraitFrame", 72, 72, 160, 160, p => { p.Color = Color.Gold; p.Opacity = 25; p.Corner = Corner.Circle; }),
        P(PieceType.Text, "Portrait", 72, 102, 160, 100, p => { p.Text = "🧙"; p.Size = 64; p.Align = TextAlign.Center; p.Color = Color.Gold; }),
        P(PieceType.Text, "Speaker", 52, 244, 200, 32, p => { p.Text = "Mago Aldo"; p.Size = 18; p.Bold = true; p.Align = TextAlign.Center; p.Color = Color.Gold; }),
        P(PieceType.Text, "Title", 264, 64, 620, 52, p => { p.Text = "Capítulo 1"; p.Size = 32; p.Bold = true; }),
        P(PieceType.Text, "Story", 264, 124, 620, 280, p =>
        {
            p.Text = "Era uma vez, num reino distante, um herói que ainda não sabia que era herói. " +
                     "Escreva aqui a história da cena (ou mude pelo código, com game.Find(\"Story\").Text).";
            p.Size = 20;
            p.Scroll = true;
        }),
        P(PieceType.Button, "Continue", 680, 452, 240, 56, p => { p.Text = "Continuar  ➜"; p.Color = Color.Gold; p.Style = ButtonStyle.Gradient; p.Bold = true; p.Shortcut = "Space"; }));

    private static IReadOnlyList<string> StoryCode(string scene) =>
    [
        "game.Find(\"Story\").Text = \"Era uma vez...\";",
        "",
        "game.Find(\"Continue\").OnClick(() =>",
        "{",
        "    // Para onde a história vai? Por exemplo: game.GoTo(\"Village\");",
        "    game.Write(\"Continua...\");",
        "});",
    ];

    // ------------------------------------------------------------------ Escolhas

    private static ScreenLayout Choices(string scene) => Layout(
        P(PieceType.Text, "Title", 40, 28, 880, 56, p => { p.Text = "Uma encruzilhada"; p.Size = 34; p.Bold = true; }),
        P(PieceType.Text, "Story", 40, 92, 560, 112, p => { p.Text = "Três caminhos se abrem na sua frente. Qual você escolhe?"; p.Size = 20; }),
        P(PieceType.Button, "ChoiceA", 40, 224, 560, 60, p => { p.Text = "🌲  Entrar na floresta"; p.Size = 18; p.Color = Color.Green; p.Style = ButtonStyle.Soft; }),
        P(PieceType.Button, "ChoiceB", 40, 298, 560, 60, p => { p.Text = "⛰  Subir a montanha"; p.Size = 18; p.Color = Color.Orange; p.Style = ButtonStyle.Soft; }),
        P(PieceType.Button, "ChoiceC", 40, 372, 560, 60, p => { p.Text = "🌊  Seguir o rio"; p.Size = 18; p.Color = Color.Cyan; p.Style = ButtonStyle.Soft; }),
        P(PieceType.Messages, "Log", 624, 92, 296, 340, p => p.Size = 15));

    private static IReadOnlyList<string> ChoicesCode(string scene) =>
    [
        "game.Find(\"ChoiceA\").OnClick(() =>",
        "{",
        "    game.Write(\"Você entrou na floresta.\");",
        "    // game.GoTo(\"Forest\");",
        "});",
        "",
        "game.Find(\"ChoiceB\").OnClick(() =>",
        "{",
        "    game.Write(\"Você subiu a montanha.\");",
        "});",
        "",
        "game.Find(\"ChoiceC\").OnClick(() =>",
        "{",
        "    game.Write(\"Você seguiu o rio.\");",
        "});",
    ];

    // ------------------------------------------------------------------ Combate

    private static ScreenLayout Fight(string scene) => Layout(
        P(PieceType.Text, "Story", 24, 36, 320, 120, p => { p.Text = "Um goblin pula do mato!"; p.Size = 22; p.Bold = true; }),
        P(PieceType.Box, "EnemyFrame", 380, 28, 200, 200, p => { p.Color = Color.Red; p.Shade = Shade.Dark; p.Opacity = 100; p.Border = true; p.Corner = Corner.Circle; }),
        P(PieceType.Text, "EnemyIcon", 380, 70, 200, 116, p => { p.Text = "👹"; p.Size = 76; p.Align = TextAlign.Center; p.Color = Color.Red; p.Shade = Shade.Light; }),
        P(PieceType.Text, "EnemyName", 330, 234, 300, 40, p => { p.Text = "Goblin"; p.Size = 26; p.Bold = true; p.Align = TextAlign.Center; }),
        P(PieceType.Bar, "EnemyHealth", 300, 280, 360, 34, p => { p.Text = "Goblin"; p.Value = 30; p.Max = 30; p.Color = Color.Red; p.BarStyle = BarStyle.Shine; p.BarText = BarText.Inside; }),
        P(PieceType.Messages, "Log", 680, 28, 256, 290, p => p.Size = 14),
        P(PieceType.Box, "PlayerPanel", 24, 340, 300, 176, p => p.Opacity = 90),
        P(PieceType.Text, "PlayerName", 44, 350, 260, 30, p => { p.Text = "Herói"; p.Size = 18; p.Bold = true; }),
        P(PieceType.Bar, "Health", 44, 384, 260, 42, p => { p.Text = "❤ Vida"; p.Value = 100; p.Max = 100; p.Color = Color.Green; }),
        P(PieceType.Bar, "Mana", 44, 432, 260, 42, p => { p.Text = "✦ Mana"; p.Value = 30; p.Max = 30; p.Color = Color.Blue; p.BarStyle = BarStyle.Shine; }),
        P(PieceType.Button, "Attack", 344, 346, 280, 54, p => { p.Text = "⚔  Atacar"; p.Color = Color.Red; p.Style = ButtonStyle.Gradient; p.Bold = true; p.Shadow = true; p.Shortcut = "Space"; }),
        P(PieceType.Button, "Magic", 640, 346, 280, 54, p => { p.Text = "✦  Magia"; p.Color = Color.Purple; p.Style = ButtonStyle.Gradient; p.Bold = true; p.Shadow = true; }),
        P(PieceType.Button, "Potion", 344, 412, 280, 54, p => { p.Text = "🧪  Poção"; p.Color = Color.Green; p.Style = ButtonStyle.Soft; }),
        P(PieceType.Button, "Flee", 640, 412, 280, 54, p => { p.Text = "🏃  Fugir"; p.Style = ButtonStyle.Outline; }));

    private static IReadOnlyList<string> FightCode(string scene) =>
    [
        "game.Find(\"Attack\").OnClick(() =>",
        "{",
        "    game.Write(\"Você atacou!\", Color.Green);",
        "    game.Find(\"EnemyIcon\").Shake();",
        "    // Para a vida do inimigo cair: crie lá em cima \"int enemyHealth = 30;\",",
        "    // escreva aqui \"enemyHealth -= 10;\" e, no começo da cena, game.Find(\"EnemyHealth\").Value = enemyHealth;",
        "});",
        "",
        "game.Find(\"Magic\").OnClick(() =>",
        "{",
        "    game.Write(\"Você lançou uma magia!\", Color.Purple);",
        "});",
        "",
        "game.Find(\"Potion\").OnClick(() =>",
        "{",
        "    game.Write(\"Você bebeu uma poção.\", Color.Green);",
        "});",
        "",
        "game.Find(\"Flee\").OnClick(() =>",
        "{",
        "    game.Write(\"Você fugiu!\");",
        "    // game.GoTo(\"Village\");",
        "});",
    ];

    // ------------------------------------------------------------------ Loja

    private static ScreenLayout Shop(string scene)
    {
        Piece Card(PieceType type, string name, double x, double y, double width, double height, Action<Piece> style) =>
            P(type, name, x, y, width, height, p =>
            {
                p.List = "Items";
                style(p);
            });
        return Layout(
            P(PieceType.Text, "Title", 40, 22, 600, 56, p => { p.Text = "🏪  Loja"; p.Size = 34; p.Bold = true; }),
            P(PieceType.Text, "Gold", 640, 32, 280, 38, p => { p.Text = "💰 50 moedas"; p.Size = 20; p.Bold = true; p.Align = TextAlign.Right; p.Color = Color.Gold; }),
            P(PieceType.List, "Items", 40, 92, 880, 250, p => { p.CardWidth = 200; p.CardHeight = 240; p.Gap = 20; p.Text = "A loja está vazia."; }),
            Card(PieceType.Box, "CardBack", 0, 0, 200, 240, p => { p.Color = Color.Gold; p.Shade = Shade.Dark; p.Opacity = 30; }),
            Card(PieceType.Text, "Icon", 0, 14, 200, 70, p => { p.Text = "🗡"; p.Size = 46; p.Align = TextAlign.Center; p.Color = Color.Gold; }),
            Card(PieceType.Text, "Name", 10, 90, 180, 32, p => { p.Text = "Espada"; p.Size = 18; p.Bold = true; p.Align = TextAlign.Center; }),
            Card(PieceType.Text, "Price", 10, 124, 180, 30, p => { p.Text = "💰 30"; p.Size = 16; p.Align = TextAlign.Center; p.Color = Color.Gold; }),
            Card(PieceType.Button, "Buy", 25, 178, 150, 46, p => { p.Text = "Comprar"; p.Size = 15; p.Color = Color.Green; p.Style = ButtonStyle.Gradient; p.Bold = true; }),
            P(PieceType.Messages, "Log", 40, 360, 600, 160, p => p.Size = 15),
            P(PieceType.Button, "Back", 680, 462, 240, 54, p => { p.Text = "↩  Voltar"; p.Style = ButtonStyle.Outline; }));
    }

    private static IReadOnlyList<string> ShopCode(string scene) =>
    [
        "// A Lista mostra um cartão para cada item. Escreva os itens lá em cima do código e entregue aqui:",
        "// game.Find(\"Items\").Show(items, (card, item) =>",
        "// {",
        "//     card.Find(\"Name\").Text = item.Name;",
        "//     card.Find(\"Buy\").OnClick(() => game.Write($\"Você comprou {item.Name}!\"));",
        "// });",
        "",
        "game.Find(\"Back\").OnClick(() =>",
        "{",
        "    // game.GoTo(\"Village\");",
        "    game.Write(\"Até a próxima!\");",
        "});",
    ];
}
