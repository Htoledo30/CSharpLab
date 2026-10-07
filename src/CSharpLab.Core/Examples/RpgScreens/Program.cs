// RPG com tela desenhada: as telas estão na pasta Screens (abra Fight.json para ver a aba Tela).
// Aqui o código não cria botões: ele pega as peças desenhadas pelo nome, com game.Find,
// e diz o que muda na tela e o que cada botão faz.

var game = new Game("A Torre do Goblin");

// O estado do jogo fica em variáveis aqui em cima, fora das cenas.
string playerName = "";
int health = 100;
int gold = 10;
int potions = 1;
int enemyHealth = 30;

// Primeira tela: o jogador escreve o nome no campo NameField.
game.Scene("Name", () =>
{
    game.Find("NameField").OnAnswer(answer =>
    {
        playerName = answer;
        game.GoTo("Village");
    });
});

// A vila: mostra vida, ouro e poções, e deixa comprar antes da luta.
game.Scene("Village", () =>
{
    game.Find("Story").Text = $"Bem-vindo, {playerName}! A torre do goblin fica logo ali.";
    game.Find("PlayerHealth").Value = health;
    game.Find("Gold").Text = $"Ouro: {gold}";
    game.Find("Potions").Text = $"Poções: {potions}";

    game.Find("Shop").OnClick(() =>
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

    game.Find("Tower").OnClick(() => game.GoTo("Fight"));
});

// A luta: as barras mostram a vida dos dois, e o goblin revida a cada ação.
game.Scene("Fight", () =>
{
    // Quando alguém chega a 0, a cena manda para outra tela.
    if (enemyHealth <= 0) game.GoTo("Victory");
    if (health <= 0) game.GoTo("GameOver");

    game.Find("PlayerHealth").Text = playerName;
    game.Find("PlayerHealth").Value = health;
    game.Find("EnemyHealth").Value = enemyHealth;

    // O botão de poção só aparece se ainda houver poção.
    game.Find("Potion").Text = $"Beber poção ({potions})";
    game.Find("Potion").Visible = potions > 0;

    game.Find("Attack").OnClick(() =>
    {
        int damage = Random.Shared.Next(5, 11); // de 5 a 10
        enemyHealth -= damage;
        game.Write($"Você causou {damage} de dano!");
        GoblinAttacks();
    });

    game.Find("Potion").OnClick(() =>
    {
        potions--;
        health = Math.Min(100, health + 30);
        game.Write("Você se sente bem melhor.", Color.Green);
        GoblinAttacks();
    });

    game.Find("Flee").OnClick(() => game.GoTo("Village"));
});

game.Scene("Victory", () =>
{
    game.Find("Story").Text = $"{playerName} derrotou o goblin e recuperou o tesouro!";
    game.Find("PlayAgain").OnClick(() =>
    {
        gold += 20;
        health = 100;
        enemyHealth = 30;
        game.GoTo("Village");
    });
});

game.Scene("GameOver", () =>
{
    game.Find("Retry").OnClick(() =>
    {
        health = 100;
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
