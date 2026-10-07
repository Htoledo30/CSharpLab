// Jogo com botões: cada cena é uma tela do jogo. Aperte F5 para jogar.
// Dica: pare o mouse em cima de Write, Button, Bar... para ver a explicação.

var game = new Game("Meu jogo");

// As variáveis do jogo ficam aqui em cima, fora das cenas.
int gold = 0;

game.Scene("Start", () =>
{
    game.Title("Bem-vindo!");
    game.Write("Você está numa vila tranquila.");
    game.Write($"Ouro: {gold}", Color.Gold);

    game.Button("Procurar moedas", () =>
    {
        gold += 5;
        game.Write("Você achou 5 moedas!");
    });

    game.Button("Ir para a floresta", () => game.GoTo("Forest"));
});

game.Scene("Forest", () =>
{
    game.Title("Floresta");
    game.Write("Árvores altas e um silêncio estranho...");
    game.Button("Voltar para a vila", () => game.GoTo("Start"));
});

// Abre a janela na primeira cena. Fica sempre no fim do arquivo.
game.Start("Start");
