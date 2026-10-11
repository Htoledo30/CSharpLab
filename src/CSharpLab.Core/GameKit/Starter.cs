// Jogo com botões. Aperte F5 para jogar (Ctrl+F5 joga só a cena aberta).
// As telas ficam na pasta Screens, uma por cena: abra Screens/Start.json (ou use o botão Estúdio,
// lá em cima) para arrastar os botões e mudar textos e cores.
// Aqui no código fica o que muda na tela e o que cada botão faz.

var game = new Game("Meu jogo");

// As variáveis do jogo ficam aqui em cima, fora das cenas.
int gold = 0;
int forestVisits = 0;

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

// A floresta (tela: Screens/Forest.json). Toda cena tem três momentos:
game.Scene("Forest", () =>
{
    // 1. Ao chegar: o game.OnEnter roda uma vez a cada visita (não nos cliques).
    game.OnEnter(() =>
    {
        forestVisits++;
    });

    // 2. Mostrar: põe o valor das variáveis nas peças. Roda de novo depois de cada clique.
    game.Find("Story").Text = $"Árvores altas e um silêncio estranho... (visita {forestVisits})";

    // 3. Ao clicar: o que cada botão faz.
    game.Find("Back").OnClick(() =>
    {
        game.GoTo("Start");
    });
});

// Para testar uma cena sem jogar tudo até ela: abra a cena e aperte Ctrl+F5.
// O game.Test escolhe os valores desse teste (no F5 de sempre ele não roda).
game.Test("Forest", () =>
{
    forestVisits = 10;
});

// Abre a janela na primeira cena. Fica sempre no fim do arquivo.
game.Start("Start");
