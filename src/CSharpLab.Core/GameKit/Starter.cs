// Jogo com botões. Aperte F5 para jogar.
// As telas ficam na pasta Screens, uma por cena: abra Screens/Start.json (ou use o botão Estúdio,
// lá em cima) para arrastar os botões e mudar textos e cores.
// Aqui no código fica o que muda na tela e o que cada botão faz.

var game = new Game("Meu jogo");

// As variáveis do jogo ficam aqui em cima, fora das cenas.
int gold = 0;

// A vila (tela: Screens/Start.json).
game.Scene("Start", () =>
{
    // game.Find("Nome") pega a peça da tela pelo nome. Aqui, o texto do ouro mostra o valor atual.
    game.Find("Gold").Text = $"Ouro: {gold}";

    game.Find("Search").OnClick(() =>
    {
        gold += 5;
        game.Write("Você achou 5 moedas!");
    });

    game.Find("Forest").OnClick(() =>
    {
        game.GoTo("Forest");
    });
});

// A floresta (tela: Screens/Forest.json).
game.Scene("Forest", () =>
{
    game.Find("Back").OnClick(() =>
    {
        game.GoTo("Start");
    });
});

// Abre a janela na primeira cena. Fica sempre no fim do arquivo.
game.Start("Start");
