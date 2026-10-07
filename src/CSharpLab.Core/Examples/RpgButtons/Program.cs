// RPG com botões: um jogo de janela com cenas, botões e barras de vida.
// Cada game.Scene é uma tela. Depois de cada clique, a tela é desenhada de novo
// com os valores atuais das variáveis: por isso as barras se atualizam sozinhas.

var game = new Game("A Torre do Goblin");

// O estado do jogo fica em variáveis aqui em cima, fora das cenas.
string playerName = "";
int health = 100;
int maxHealth = 100;
int gold = 10;
int potions = 1;
int enemyHealth = 30;

// Primeira tela: pergunta o nome do jogador.
game.Scene("Name", () =>
{
    game.Title("A Torre do Goblin");
    game.Write("Um goblin roubou o tesouro da vila e se escondeu na torre.");
    game.Ask("Qual é o seu nome, aventureiro?", answer =>
    {
        playerName = answer;
        game.GoTo("Village");
    });
});

// A vila: dá para comprar poções antes da luta.
game.Scene("Village", () =>
{
    game.Title("Vila");
    game.Write($"Bem-vindo, {playerName}! A torre fica logo ali.");
    game.Write($"Ouro: {gold}   Poções: {potions}", Color.Gold);
    game.Bar("Vida", health, maxHealth, Color.Green);

    game.Button("Comprar poção (5 de ouro)", () =>
    {
        if (gold >= 5)
        {
            gold -= 5;
            potions++;
            game.Write("Você comprou uma poção.", Color.Green);
        }
        else
        {
            game.Write("Ouro insuficiente!", Color.Red);
        }
    });

    game.Button("Entrar na torre", () => game.GoTo("Fight"));
});

// A luta: o goblin revida a cada ataque.
game.Scene("Fight", () =>
{
    // Quando alguém chega a 0, a cena manda para outra tela.
    if (enemyHealth <= 0) game.GoTo("Victory");
    if (health <= 0) game.GoTo("GameOver");

    game.Title("Um goblin aparece!");
    game.Write("Ele segura uma faca enferrujada e ri de você.");
    game.Bar(playerName, health, maxHealth, Color.Green);
    game.Bar("Goblin", enemyHealth, 30, Color.Red);

    game.Button("Atacar", () =>
    {
        int damage = Random.Shared.Next(5, 11); // de 5 a 10
        enemyHealth -= damage;
        game.Write($"Você causou {damage} de dano!");
        GoblinAttacks();
    });

    // O botão só aparece se ainda houver poção.
    if (potions > 0)
    {
        game.Button($"Beber poção ({potions})", () =>
        {
            potions--;
            health = Math.Min(maxHealth, health + 30);
            game.Write("Você se sente bem melhor.", Color.Green);
            GoblinAttacks();
        });
    }

    game.Button("Fugir", () => game.GoTo("Village"));
});

game.Scene("Victory", () =>
{
    game.Title("Vitória!");
    game.Write($"{playerName} derrotou o goblin e recuperou o tesouro.", Color.Gold);
    game.Write("A vila inteira comemora o seu nome.");
});

game.Scene("GameOver", () =>
{
    game.Title("Fim de jogo");
    game.Write("Você caiu... mas a vila ainda precisa de você.", Color.Red);
    game.Button("Tentar de novo", () =>
    {
        health = maxHealth;
        enemyHealth = 30;
        game.GoTo("Village");
    });
});

game.Start("Name");

// Um método: o mesmo código usado por dois botões (Atacar e Beber poção).
void GoblinAttacks()
{
    if (enemyHealth <= 0) return;
    int hit = Random.Shared.Next(4, 10);
    health -= hit;
    game.Write($"O goblin revida: -{hit} de vida.", Color.Red);
}
