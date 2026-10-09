// A Coroa Perdida: um RPG completo de escolhas, com telas desenhadas (pasta Screens, botão Estúdio lá em cima).
// Mostra quase tudo que o motor faz: nome digitado, classes, loja com Lista, níveis, eventos com escolhas,
// um enigma para responder, lutas com pausa (game.Wait), tremidas (Shake) e um chefe final.
//
// Como ler este arquivo:
//   1. Os dados do jogo: classes de herói, coisas da loja e inimigos (cada um é um "record", lá no fim).
//   2. As variáveis do jogo (o "estado").
//   3. Os métodos que várias cenas usam (mostrar status, lutar, subir de nível).
//   4. As cenas, uma por tela, na ordem em que o jogador passa por elas.
// Cada cena roda de novo depois de cada clique, então o código dela tem três lugares:
//   game.OnEnter  → prepara a visita, uma vez por chegada (ex.: a luta começa com o inimigo de vida cheia);
//   o resto       → só põe os valores atuais na tela;
//   OnClick       → a escolha do jogador.

var game = new Game("A Coroa Perdida");

// ---------------------------------------------------------------- dados do jogo

// As três classes. A tela "ChooseClass" mostra uma Lista: um cartão para cada classe desta lista.
var classes = new List<HeroClass>
{
    new("Guerreiro", "⚔", Color.Red, "Forte e resistente. Aguenta pancada e bate pesado.", 130, 20, 7, 12, "Golpe brutal", 6),
    new("Maga", "🧙", Color.Blue, "Frágil, mas a magia dela derruba qualquer inimigo.", 85, 50, 4, 8, "Bola de fogo", 9),
    new("Ladino", "🥷", Color.Green, "Rápido: golpes críticos e ataques que atordoam.", 100, 30, 6, 10, "Ataque furtivo", 7),
};

// O que a loja vende. Para vender mais uma coisa, é só pôr mais uma linha: a Lista da loja cria o cartão sozinha.
var products = new List<Product>
{
    new("Poção de vida", "🧪", Color.Green, "Cura 35 de vida. Beba durante a luta.", 8, ProductKind.Potion, 35),
    new("Amuleto de mana", "📿", Color.Blue, "+15 de mana máxima, para sempre.", 25, ProductKind.Amulet, 15),
    new("Adaga", "🔪", Color.Gray, "+2 de dano em todo ataque.", 12, ProductKind.Weapon, 2),
    new("Espada de aço", "🗡", Color.Red, "+4 de dano em todo ataque.", 30, ProductKind.Weapon, 4),
    new("Martelo de guerra", "🔨", Color.Orange, "+7 de dano em todo ataque.", 55, ProductKind.Weapon, 7),
};

// Os inimigos: nome, desenho, cor, vida, dano mínimo e máximo, ouro e experiência que deixam.
var wolf = new Enemy("Lobo cinzento", "🐺", Color.Gray, 24, 3, 6, 4, 12);
var bandit = new Enemy("Bandido", "🥷", Color.Orange, 34, 4, 8, 12, 18);
var spider = new Enemy("Aranha gigante", "🕷", Color.Purple, 18, 2, 5, 3, 9);
var goblinKing = new Enemy("Rei Goblin", "👺", Color.Red, 110, 7, 13, 0, 0, IsBoss: true);

// ---------------------------------------------------------------- estado do jogo

string playerName = "";
HeroClass heroClass = classes[0];   // trocada na escolha de classe
int level = 1;
int xp = 0;
int xpToNext = 30;            // quanto falta para o próximo nível
int maxHealth = 100;
int health = 100;
int maxMana = 30;
int mana = 30;
int levelDamage = 0;          // cada nível dá +2 de dano
int gold = 15;
int potions = 1;
Product? weapon = null;       // a arma comprada (null: lutando com a arma da classe)
bool hasAmulet = false;
bool hasKey = false;
int kindness = 0;             // as escolhas boas e más mudam o final

// A luta atual (as lutas usam todas a mesma cena "Fight").
Enemy enemy = wolf;
int enemyHealth = 0;
bool enemyStunned = false;    // o ataque furtivo do Ladino faz o inimigo perder a vez
bool fightOver = false;
string afterFight = "Forest"; // para onde o "Continuar" leva depois de vencer

// O evento da floresta que está acontecendo (cena "Event").
string currentEvent = "";

// ---------------------------------------------------------------- métodos

// O painel de status (nome, vida, mana, experiência, ouro e poções) aparece em várias telas.
// As peças têm os mesmos nomes em todas (copiadas de uma tela para outra), então um método só preenche qualquer uma.
void ShowStatus()
{
    game.Find("PlayerInfo").Text = $"{playerName} · {heroClass.Name} · Nível {level}";

    var healthBar = game.Find("Health");
    healthBar.Value = health;
    healthBar.Max = maxHealth;
    // Vida baixa fica vermelha, para o jogador perceber o perigo.
    healthBar.Color = health * 100 / maxHealth <= 30 ? Color.Red : Color.Green;

    game.Find("Mana").Value = mana;
    game.Find("Mana").Max = maxMana;
    game.Find("Xp").Value = xp;
    game.Find("Xp").Max = xpToNext;
    ShowWallet();
}

// Só o ouro e as poções (a loja mostra a carteira, sem as barras).
void ShowWallet()
{
    game.Find("Gold").Text = $"💰 {gold}";
    game.Find("Potions").Text = $"🧪 {potions}";
}

void Heal(int amount)
{
    health = Math.Min(maxHealth, health + amount);
}

void GainXp(int amount)
{
    xp += amount;
    game.Write($"+{amount} de experiência.", Color.Purple);
    while (xp >= xpToNext)
    {
        xp -= xpToNext;
        level++;
        xpToNext += 20;
        maxHealth += 15;
        maxMana += 8;
        levelDamage += 2;
        health = maxHealth;
        mana = maxMana;
        game.Write($"Você subiu para o nível {level}! Vida e mana cheias.", Color.Gold);
    }
}

// Escolhe o inimigo e vai para a luta. Quem prepara a luta (vida cheia, mensagem) é o OnEnter da cena "Fight".
void StartFight(Enemy foe, string returnTo)
{
    enemy = foe;
    afterFight = returnTo;
    game.GoTo("Fight");
}

// Dano normal do herói: o da classe, mais o dos níveis e o da arma. O Ladino às vezes acerta um crítico.
int HeroDamage()
{
    int damage = Random.Shared.Next(heroClass.MinDamage, heroClass.MaxDamage + 1) + levelDamage;
    if (weapon != null) damage += weapon.Power;
    if (heroClass.Name == "Ladino" && Random.Shared.Next(100) < 25)
    {
        damage *= 2;
        game.Write("Golpe crítico!", Color.Orange);
    }
    return damage;
}

void HitEnemy(int damage)
{
    enemyHealth = Math.Max(0, enemyHealth - damage);
    game.Write($"Você causou {damage} de dano.", Color.Green);
    // O inimigo treme e a barra dele pisca: dá para "sentir" o golpe.
    game.Find("EnemyIcon").Shake();
    game.Find("EnemyHealth").Flash();
    if (enemyHealth == 0) WinFight();
    else EnemyTurn();
}

void EnemyTurn()
{
    // Uma pausa curta: a tela mostra o seu golpe e o inimigo responde logo depois.
    game.Wait(0.6);
    if (enemyStunned)
    {
        enemyStunned = false;
        game.Write($"{enemy.Name} ficou atordoado e perdeu a vez.", Color.Gray);
        return;
    }
    int damage = Random.Shared.Next(enemy.MinDamage, enemy.MaxDamage + 1);
    health = Math.Max(0, health - damage);
    game.Write($"{enemy.Name} ataca: -{damage} de vida.", Color.Red);
    game.Find("HeroIcon").Shake();
    game.Find("Health").Flash();
    if (health == 0) game.GoTo("GameOver");
}

void WinFight()
{
    fightOver = true;
    game.Write($"{enemy.Name} foi derrotado!", Color.Gold);
    if (enemy.Gold > 0)
    {
        gold += enemy.Gold;
        game.Write($"+{enemy.Gold} de ouro.", Color.Gold);
    }
    if (enemy.Xp > 0) GainXp(enemy.Xp);
}

// ---------------------------------------------------------------- cenas

// 1. Abertura: o jogador escreve o nome.
game.Scene("Title", () =>
{
    game.Find("NameField").OnAnswer(answer =>
    {
        // Nomes muito compridos não cabem no painel.
        playerName = answer.Length > 14 ? answer.Substring(0, 14) : answer;
        game.GoTo("ChooseClass");
    });
});

// 2. Três classes, três jeitos de jogar: um cartão da Lista para cada classe.
game.Scene("ChooseClass", () =>
{
    game.Find("Subtitle").Text = $"{playerName}, como você quer enfrentar o Rei Goblin?";

    game.Find("Classes").Show(classes, (card, choice) =>
    {
        card.Find("CardBack").Color = choice.Color;
        card.Find("Icon").Text = choice.Icon;
        card.Find("Icon").Color = choice.Color;
        card.Find("Name").Text = choice.Name;
        card.Find("Name").Color = choice.Color;
        card.Find("About").Text = choice.About;
        card.Find("Stats").Text = $"♥ {choice.Health}    ✦ {choice.Mana}\nDano {choice.MinDamage} a {choice.MaxDamage}\nEspecial: {choice.Skill}";
        card.Find("Pick").Text = $"Ser {choice.Name.ToLower()}";
        card.Find("Pick").Color = choice.Color;
        card.Find("Pick").OnClick(() => BeginAdventure(choice));
    });
});

void BeginAdventure(HeroClass choice)
{
    heroClass = choice;
    maxHealth = choice.Health;
    maxMana = choice.Mana;
    health = maxHealth;
    mana = maxMana;
    game.Write($"Bem-vindo a Pedravale, {choice.Name.ToLower()} {playerName}.", Color.Gold);
    game.GoTo("Village");
}

// 3. A vila: o centro do jogo. Daqui se vai para todo lugar.
game.Scene("Village", () =>
{
    ShowStatus();
    game.Find("Story").Text = hasKey
        ? "A chave da torre pesa no seu bolso. O povo da vila olha para você com esperança."
        : "O Rei Goblin roubou a coroa da vila. Dizem que a chave da torre dele está escondida na caverna.";

    game.Find("Tavern").OnClick(() =>
    {
        if (gold < 5)
        {
            game.Write("O taverneiro balança a cabeça: o quarto custa 5 de ouro.", Color.Gray);
            return;
        }
        gold -= 5;
        health = maxHealth;
        mana = maxMana;
        game.Write("Você dormiu como uma pedra. Vida e mana cheias.", Color.Green);
    });
    game.Find("Shop").OnClick(() => game.GoTo("Shop"));
    game.Find("Forest").OnClick(() => game.GoTo("Forest"));
    game.Find("Cave").OnClick(() => game.GoTo("Cave"));

    // A torre só abre com a chave: sem ela, o botão fica apagado (Enabled = false) e o cartão cinza.
    var tower = game.Find("Tower");
    tower.Enabled = hasKey;
    tower.Text = hasKey ? "Entrar" : "🔒 Trancada";
    var towerColor = hasKey ? Color.Red : Color.Gray;
    tower.Color = towerColor;
    game.Find("TowerCard").Color = towerColor;
    game.Find("TowerIcon").Color = towerColor;
    game.Find("TowerText").Text = hasKey ? "A chave abre o portão." : "Precisa da chave da caverna.";
    tower.OnClick(() => game.GoTo("Tower"));
});

// 4. A loja: a Lista "Goods" mostra um cartão para cada coisa de "products".
game.Scene("Shop", () =>
{
    ShowWallet();
    game.Find("Goods").Show(products, (card, product) =>
    {
        card.Find("CardBack").Color = product.Color;
        card.Find("Icon").Text = product.Icon;
        card.Find("Icon").Color = product.Color;
        card.Find("Name").Text = product.Name;
        card.Find("About").Text = product.About;
        card.Find("Price").Text = $"💰 {product.Price}";

        // Sem poder comprar, o botão diz o porquê e fica apagado.
        var buy = card.Find("Buy");
        buy.Color = product.Color;
        string? reason = WhyCantBuy(product);
        buy.Text = reason ?? "Comprar";
        buy.Enabled = reason == null;
        buy.OnClick(() => Buy(product));
    });
    game.Find("Back").OnClick(() => game.GoTo("Village"));
});

// null quando dá para comprar; senão, o motivo (aparece no botão).
string? WhyCantBuy(Product product)
{
    if (product.Kind == ProductKind.Amulet && hasAmulet) return "✓ Já é seu";
    if (product.Kind == ProductKind.Weapon && weapon == product) return "✓ Na sua mão";
    if (product.Kind == ProductKind.Weapon && weapon != null && weapon.Power > product.Power) return "Você tem melhor";
    if (gold < product.Price) return "Falta ouro";
    return null;
}

void Buy(Product product)
{
    gold -= product.Price;
    switch (product.Kind)
    {
        case ProductKind.Potion:
            potions++;
            break;
        case ProductKind.Amulet:
            hasAmulet = true;
            maxMana += product.Power;
            mana += product.Power;
            break;
        case ProductKind.Weapon:
            weapon = product;
            break;
    }
    game.Write($"Você comprou: {product.Name}.", Color.Green);
}

// 5. A floresta: cada passo pode dar ouro, uma luta ou um encontro com escolhas.
game.Scene("Forest", () =>
{
    ShowStatus();

    game.Find("Explore").OnClick(() =>
    {
        int roll = Random.Shared.Next(100);   // de 0 a 99
        if (roll < 30) StartFight(wolf, "Forest");
        else if (roll < 45) StartFight(bandit, "Forest");
        else if (roll < 65)
        {
            int found = Random.Shared.Next(5, 16);
            gold += found;
            game.Write($"Entre as raízes, um saquinho com {found} de ouro!", Color.Gold);
        }
        else if (roll < 80)
        {
            currentEvent = "Traveler";
            game.GoTo("Event");
        }
        else if (roll < 92)
        {
            currentEvent = "Chest";
            game.GoTo("Event");
        }
        else game.Write("Só o vento nas folhas. Nada por aqui.", Color.Gray);
    });

    game.Find("Herbs").OnClick(() =>
    {
        if (Random.Shared.Next(100) < 25)
        {
            game.Write("Enquanto você procurava ervas, algo saltou do mato!", Color.Red);
            StartFight(spider, "Forest");
            return;
        }
        Heal(10);
        game.Write("Você achou ervas e recuperou 10 de vida.", Color.Green);
    });

    game.Find("Back").OnClick(() => game.GoTo("Village"));
});

// 6. Encontros com escolhas. Uma cena só para todos: o texto e os botões mudam conforme o evento.
game.Scene("Event", () =>
{
    ShowStatus();
    var title = game.Find("Title");
    var story = game.Find("Story");
    var choiceA = game.Find("ChoiceA");
    var choiceB = game.Find("ChoiceB");
    var choiceC = game.Find("ChoiceC");
    var leave = game.Find("Leave");

    switch (currentEvent)
    {
        case "Traveler":
            title.Text = "Um viajante ferido";
            ShowPortrait("🧓", Color.Orange);
            story.Text = "Encostado numa árvore, um velho segura a perna machucada. Uma bolsa pesada está ao lado dele.";
            choiceA.Text = "Dar uma poção a ele";
            choiceA.Enabled = potions > 0;   // sem poção, a escolha boa fica apagada
            choiceB.Text = "Seguir caminho";
            choiceC.Text = "Pegar a bolsa e correr";
            choiceA.OnClick(() =>
            {
                potions--;
                gold += 25;
                kindness++;
                game.Write("Ele agradece e te dá 25 de ouro: \"Na caverna, pense no que cresce quando se tira...\"", Color.Green);
                currentEvent = "Done";
            });
            choiceB.OnClick(() => game.GoTo("Forest"));
            choiceC.OnClick(() =>
            {
                gold += 15;
                kindness--;
                game.Write("Você correu com 15 de ouro. Os gritos dele ficam na sua cabeça.", Color.Red);
                currentEvent = "Done";
            });
            break;

        case "Chest":
            title.Text = "Um baú esquecido";
            ShowPortrait("🧰", Color.Gold);
            story.Text = "Meio enterrado na lama, um baú velho com uma fechadura enferrujada.";
            choiceA.Text = "Forçar a fechadura";
            choiceB.Text = "Deixar para lá";
            choiceC.Visible = false;
            choiceA.OnClick(() =>
            {
                if (Random.Shared.Next(100) < 60)
                {
                    gold += 20;
                    game.Write("Dentro, 20 moedas de ouro!", Color.Gold);
                }
                else
                {
                    health = Math.Max(1, health - 10);
                    game.Write("Uma agulha envenenada! -10 de vida.", Color.Red);
                }
                currentEvent = "Done";
            });
            choiceB.OnClick(() => game.GoTo("Forest"));
            break;
    }

    // Depois de escolher, as opções somem e só fica o caminho de volta.
    bool done = currentEvent == "Done";
    choiceA.Visible = !done;
    choiceB.Visible = !done;
    if (done) choiceC.Visible = false;
    leave.Visible = done;
    leave.OnClick(() => game.GoTo("Forest"));
});

// O retrato do encontro: um símbolo grande e a moldura redonda na mesma cor.
void ShowPortrait(string icon, Color color)
{
    game.Find("Portrait").Text = icon;
    game.Find("Portrait").Color = color;
    game.Find("PortraitBox").Color = color;
}

// 7. A caverna: um enigma para responder escrevendo.
game.Scene("Cave", () =>
{
    ShowStatus();
    game.Find("Riddle").Visible = !hasKey;
    game.Find("Echo").Visible = !hasKey;
    game.Find("Story").Text = hasKey
        ? "A caverna está em silêncio. A pedra que guardava a chave agora está vazia."
        : "Uma voz ecoa nas pedras: \"Responda e a chave é sua. Erre e sinta a minha mordida.\"";

    game.Find("Riddle").OnAnswer(answer =>
    {
        // ToLower: "Buraco" e "buraco" valem igual.
        string guess = answer.ToLower();
        if (guess == "buraco" || guess == "um buraco")
        {
            hasKey = true;
            GainXp(15);
            game.Write("A pedra se abre. Você pegou a chave da torre!", Color.Gold);
        }
        else
        {
            health = Math.Max(1, health - 8);
            game.Write($"\"{answer}\"? Errado! Algo te morde no escuro: -8 de vida.", Color.Red);
            game.Find("Health").Flash();
        }
    });
    game.Find("Back").OnClick(() => game.GoTo("Village"));
});

// 8. A torre: o chefe final.
game.Scene("Tower", () =>
{
    ShowStatus();
    game.Find("Challenge").OnClick(() => StartFight(goblinKing, "Victory"));
    game.Find("Back").OnClick(() => game.GoTo("Village"));
});

// 9. A luta: a mesma cena para todos os inimigos.
game.Scene("Fight", () =>
{
    // Uma vez por luta, na chegada: o inimigo começa com a vida cheia.
    // (Se isto ficasse solto na cena, a vida dele voltaria a encher a cada clique!)
    game.OnEnter(() =>
    {
        enemyHealth = enemy.Health;
        enemyStunned = false;
        fightOver = false;
        game.Write($"{enemy.Name} aparece!", Color.Orange);
    });

    ShowStatus();
    // Os "retratos": o símbolo da classe do herói e o do inimigo, cada um numa moldura redonda.
    game.Find("HeroIcon").Text = heroClass.Icon;
    game.Find("HeroIcon").Color = heroClass.Color;
    game.Find("HeroRing").Color = heroClass.Color;
    game.Find("EnemyIcon").Text = enemy.Icon;
    game.Find("EnemyIcon").Color = enemy.Color;
    var ring = game.Find("EnemyRing");
    ring.Color = enemy.Color;
    // O chefe ganha uma moldura cheia, vermelho escuro.
    ring.Shade = enemy.IsBoss ? Shade.Dark : Shade.Normal;
    ring.Opacity = enemy.IsBoss ? 100 : 30;
    game.Find("EnemyName").Text = enemy.Name;
    var enemyBar = game.Find("EnemyHealth");
    enemyBar.Value = enemyHealth;
    enemyBar.Max = enemy.Health;
    game.Find("Story").Text = fightOver
        ? "A luta acabou."
        : enemy.IsBoss ? "O Rei Goblin ergue o machado. É agora ou nunca." : $"{enemy.Name} bloqueia o caminho.";

    // Durante a luta, os botões de ação; depois, só o "Continuar".
    var skill = game.Find("Skill");
    var potion = game.Find("Potion");
    var flee = game.Find("Flee");
    game.Find("Attack").Visible = !fightOver;
    skill.Visible = !fightOver;
    potion.Visible = !fightOver;
    potion.Enabled = potions > 0;
    flee.Visible = !fightOver && !enemy.IsBoss;
    game.Find("Continue").Visible = fightOver;

    // A habilidade especial vem da classe; sem mana, o botão fica apagado.
    skill.Text = $"✦ {heroClass.Skill} ({heroClass.SkillCost} de mana)";
    skill.Enabled = mana >= heroClass.SkillCost;
    potion.Text = $"🧪 Beber poção ({potions})";

    game.Find("Attack").OnClick(() => HitEnemy(HeroDamage()));

    skill.OnClick(() =>
    {
        mana -= heroClass.SkillCost;
        game.Write($"{heroClass.Skill}!", Color.Blue);
        switch (heroClass.Name)
        {
            case "Guerreiro":
                HitEnemy(Random.Shared.Next(16, 24) + (weapon?.Power ?? 0));
                break;
            case "Maga":
                HitEnemy(Random.Shared.Next(22, 31));
                break;
            default:
                enemyStunned = true;
                HitEnemy(Random.Shared.Next(12, 19));
                break;
        }
    });

    potion.OnClick(() =>
    {
        potions--;
        Heal(35);
        game.Write("Você bebeu uma poção: +35 de vida.", Color.Green);
        game.Find("Health").Flash();
        EnemyTurn();
    });

    flee.OnClick(() =>
    {
        if (Random.Shared.Next(100) < 60)
        {
            game.Write("Você conseguiu fugir.", Color.Gray);
            game.GoTo(afterFight);
        }
        else
        {
            game.Write("Não deu para fugir!", Color.Orange);
            EnemyTurn();
        }
    });

    game.Find("Continue").OnClick(() => game.GoTo(afterFight));
});

// 10. Os finais: o que você fez no caminho muda o texto.
game.Scene("Victory", () =>
{
    game.Find("Story").Text = kindness > 0
        ? $"{playerName} volta com a coroa. A vila faz uma festa, e o velho viajante está lá, sem mancar, levantando um copo para você."
        : kindness < 0
            ? $"{playerName} volta com a coroa. A vila comemora, mas alguém comenta baixinho sobre um viajante roubado na floresta..."
            : $"{playerName} volta com a coroa. A vila inteira grita o seu nome.";
    game.Find("Summary").Text = $"{heroClass.Name} · nível {level} · {gold} de ouro";
    game.Find("Again").OnClick(Restart);
});

game.Scene("GameOver", () =>
{
    game.Find("Story").Text = $"{playerName} caiu diante de {enemy.Name}. Mas lendas sempre ganham uma segunda chance.";
    game.Find("Again").OnClick(Restart);
});

void Restart()
{
    level = 1;
    xp = 0;
    xpToNext = 30;
    levelDamage = 0;
    gold = 15;
    potions = 1;
    kindness = 0;
    weapon = null;
    hasAmulet = false;
    hasKey = false;
    currentEvent = "";
    game.GoTo("ChooseClass");
}

game.Start("Title");

// ---------------------------------------------------------------- os tipos do jogo
// "record" é uma classe curtinha só com dados: o que vem entre parênteses vira propriedade
// (classes[0].Name, wolf.Health…). Um valor depois do = é o padrão, quando não é dito.

// Uma classe de herói: nome, desenho, cor, texto, vida, mana, dano mínimo e máximo, e a habilidade especial.
record HeroClass(string Name, string Icon, Color Color, string About, int Health, int Mana, int MinDamage, int MaxDamage, string Skill, int SkillCost);

// O tipo de uma coisa da loja: diz o que acontece ao comprar.
enum ProductKind { Potion, Amulet, Weapon }

// Uma coisa à venda. Power: quanto cura (poção), quanta mana dá (amuleto) ou quanto dano soma (arma).
record Product(string Name, string Icon, Color Color, string About, int Price, ProductKind Kind, int Power);

// Um inimigo: nome, símbolo e cor (o "retrato" na luta), vida, dano mínimo e máximo, e o que ele deixa ao ser derrotado.
record Enemy(string Name, string Icon, Color Color, int Health, int MinDamage, int MaxDamage, int Gold, int Xp, bool IsBoss = false);
