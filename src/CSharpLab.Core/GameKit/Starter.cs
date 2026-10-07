// Jogo com botões: cada cena é uma tela do jogo. Aperte F5 para jogar.
// Dica: pare o mouse em cima de Say, Button, Bar... para ver a explicação.

var game = new Game("Meu jogo");

// As variáveis do jogo ficam aqui em cima, fora das cenas.
int gold = 0;

game.Scene("Start", () =>
{
    game.Title("Bem-vindo!");
    game.Say("Você está numa vila tranquila.");
    game.Say($"Ouro: {gold}", GameColor.Gold);

    game.Button("Procurar moedas", () =>
    {
        gold += 5;
        game.Say("Você achou 5 moedas!");
    });

    game.Button("Ir para a floresta", () => game.GoTo("Forest"));
});

game.Scene("Forest", () =>
{
    game.Title("Floresta");
    game.Say("Árvores altas e um silêncio estranho...");
    game.Button("Voltar para a vila", () => game.GoTo("Start"));
});

// Abre a janela na primeira cena. Fica sempre no fim do arquivo.
game.Run("Start");
