// A Coroa Perdida: um RPG completo de escolhas, feito só com as peças do Estúdio (pasta Screens, botão Estúdio
// lá em cima). Cada tela foi montada arrastando peças; o código só diz o que muda e o que cada botão faz.
//
// O mapa do jogo (23 cenas):
//   Title → ChooseClass → Village (a vila)
//   Na vila:  Tavern (descansar e jogar dados) → Fortune (a roleta da cigana)
//             Shop (loja) · Blacksmith (ferreiro) · Temple (templo, salvar o jogo) · Quests (quadro de missões)
//             Bag (mochila) · Hero (ficha do herói) · Map (sair da vila)
//   No mapa:  Forest (floresta) → Event (encontros com escolhas)
//             Swamp (pântano: caça ao tesouro na Grade) · Camp (acampamento) · Bridge (a ponte do troll)
//             Cave (caverna do enigma, depois da ponte) · Tower → TowerHall (armadilhas) → luta com o chefe
//   Fight (a luta, igual para todos os inimigos) → Victory ou GameOver
//
// Que peça aparece onde (para estudar):
//   Ícone: em quase todas as telas (retratos, cartões, mapa)     Dado: Fight, Tavern, Camp, TowerHall
//   Lista: ChooseClass, Shop, Quests, Bag                         Escolha: Blacksmith, Bridge
//   Contador: Tavern (aposta), Temple (doação)                    Roleta: Fortune
//   Grade: Swamp                                                  Campo de escrita: Title, Cave
//   Mensagens: quase todas (na luta, uma caixa para você e outra para o inimigo)
//
// Como ler este arquivo:
//   1. Os dados do jogo (classes, loja, inimigos, missões). Os tipos deles estão no GameTypes.cs.
//   2. As variáveis do jogo (o "estado").
//   3. Os métodos que várias cenas usam (status, luta, salvar).
//   4. As cenas, uma por tela, na ordem do mapa lá de cima.
// Cada cena roda de novo depois de cada clique, então o código dela tem três lugares:
//   game.OnEnter  → prepara a visita, uma vez por chegada (ex.: a luta começa com o inimigo de vida cheia);
//   o resto       → só põe os valores atuais na tela;
//   OnClick       → a escolha do jogador.

var game = new Game("A Coroa Perdida");

// ================================================================ 1. dados do jogo

// As três classes. A tela "ChooseClass" mostra uma Lista: um cartão para cada classe desta lista.
var classes = new List<HeroClass>
{
    new("Guerreiro", Icon.Swords, Color.Red, "Forte e resistente. Aguenta pancada e bate pesado.", 130, 20, 7, 12, "Golpe brutal", 6),
    new("Maga", Icon.Mage, Color.Blue, "Frágil, mas a magia dela derruba qualquer inimigo.", 85, 50, 4, 8, "Bola de fogo", 9),
    new("Ladino", Icon.Knife, Color.Green, "Rápido: mais golpes críticos e ataques que atordoam.", 100, 30, 6, 10, "Ataque furtivo", 7),
};

// O que a loja vende. Para vender mais uma coisa, é só pôr mais uma linha: a Lista da loja cria o cartão sozinha.
var products = new List<Product>
{
    new("Poção de vida", Icon.Potion, Color.Green, "Cura 35 de vida. Beba durante a luta.", 8, ProductKind.Potion, 35),
    new("Bomba", Icon.Bomb, Color.Orange, "25 de dano na luta, sem errar.", 15, ProductKind.Bomb, 25),
    new("Amuleto de mana", Icon.Gem, Color.Blue, "+15 de mana máxima, para sempre.", 25, ProductKind.Amulet, 15),
    new("Adaga", Icon.Knife, Color.Gray, "+2 de dano em todo ataque.", 12, ProductKind.Weapon, 2),
    new("Espada de aço", Icon.Sword, Color.Red, "+4 de dano em todo ataque.", 30, ProductKind.Weapon, 4),
    new("Martelo de guerra", Icon.Hammer, Color.Orange, "+7 de dano em todo ataque.", 55, ProductKind.Weapon, 7),
};

// Os inimigos: nome, desenho, cor, vida, dano mínimo e máximo, ouro e experiência que deixam.
var wolf = new Enemy("Lobo cinzento", Icon.Wolf, Color.Gray, 24, 3, 6, 4, 12);
var bandit = new Enemy("Bandido", Icon.Person, Color.Orange, 34, 4, 8, 12, 18);
var spider = new Enemy("Aranha gigante", Icon.Spider, Color.Purple, 18, 2, 5, 3, 9);
var snake = new Enemy("Cobra do pântano", Icon.Snake, Color.Green, 22, 3, 7, 6, 10);
var troll = new Enemy("Troll da ponte", Icon.Ogre, Color.Brown, 60, 5, 10, 20, 30);
var goblinKing = new Enemy("Rei Goblin", Icon.Goblin, Color.Red, 110, 7, 13, 0, 0, IsBoss: true);

// As missões do quadro da vila. O progresso de cada uma é calculado no método QuestProgress, lá embaixo.
var quests = new List<Quest>
{
    new("Wolves", "Caçador de lobos", Icon.Wolf, Color.Gray, "Derrote 3 lobos na floresta.", 3, 20, 20),
    new("Swamp", "Tesouro do pântano", Icon.Gem, Color.Green, "Ache o baú escondido no pântano.", 1, 15, 25),
    new("Bridge", "A ponte do troll", Icon.Ogre, Color.Brown, "Atravesse a ponte da montanha.", 1, 10, 30),
    new("Dice", "Sortudo da taverna", Icon.Dice, Color.Gold, "Ganhe 2 rodadas de dados na taverna.", 2, 25, 10),
};

// As três perguntas do troll da ponte. A tela "Bridge" mostra as opções de cada uma numa Escolha.
var riddles = new List<Riddle>
{
    new("Quanto é 7 + 5?", ["10", "12", "13"], "12"),
    new("O que tem dentes, mas não morde?", ["Pente", "Lobo", "Bota"], "Pente"),
    new("Quantas patas tem uma aranha?", ["6", "8", "10"], "8"),
};

// As três armadilhas do corredor da torre (cena "TowerHall"): o nome e o desenho de cada uma.
string[] trapNames = ["Lâminas giratórias", "Chão falso", "Flechas na parede"];
Icon[] trapIcons = [Icon.Gear, Icon.Warning, Icon.Bow];

// ================================================================ 2. estado do jogo

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

// A mochila: quantas de cada coisa.
int potions = 1;
int bombs = 0;
int herbs = 0;
int meat = 0;

// Equipamento.
Product? weapon = null;       // a arma comprada (null: lutando com a arma da classe)
bool hasAmulet = false;
int sharpness = 0;            // o ferreiro afia a arma: +2 de dano por nível, até 3
bool hasArmor = false;        // armadura de couro: +20 de vida máxima
bool hasShield = false;       // escudo de ferro: os inimigos tiram 2 a menos

bool hasKey = false;          // a chave da torre (o prêmio do enigma da caverna)
int kindness = 0;             // as escolhas boas e más mudam o final

// O que as missões contam.
int wolvesKilled = 0;
bool swampTreasure = false;
bool bridgeCrossed = false;
int diceWins = 0;
var questsDone = new List<string>();   // as missões cujo prêmio já foi recebido (pelo Id)

// A luta atual (as lutas usam todas a mesma cena "Fight").
Enemy enemy = wolf;
int enemyHealth = 0;
bool enemyStunned = false;    // o ataque furtivo do Ladino faz o inimigo perder a vez
bool fightOver = false;
string afterFight = "Map";    // para onde o "Continuar" leva depois de vencer

// Valores de algumas cenas.
string currentEvent = "";     // o encontro da floresta que está acontecendo (cena "Event")
string bagReturnsTo = "Village";   // para onde o "Voltar" da mochila e da ficha leva
int bet = 5;                  // a aposta da taverna (o Contador)
string chosenUpgrade = "Afiar a arma";   // a melhoria escolhida no ferreiro (a Escolha)
int donation = 5;             // a doação do templo (o Contador)
int riddleIndex = 0;          // qual pergunta do troll está valendo
int trapsPassed = 0;          // quantas armadilhas da torre já passaram
// A caça ao tesouro do pântano: onde está o baú, quantas tentativas sobram e se já achou nesta visita.
int treasureX = 0;
int treasureY = 0;
int digsLeft = 0;
bool foundThisVisit = false;

// ================================================================ 3. métodos que várias cenas usam

// O painel de status (retrato, nome, vida, mana, experiência, ouro e poções) aparece em várias telas.
// As peças têm os mesmos nomes em todas (copiadas de uma tela para outra), então um método só preenche qualquer uma.
void ShowStatus()
{
    game.Find("HeroBadge").Icon = heroClass.Icon;
    game.Find("HeroBadge").Color = heroClass.Color;
    game.Find("PlayerInfo").Text = $"{playerName}\n{heroClass.Name} · Nível {level}";

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
    game.Find("Gold").Text = $"{gold}";
    game.Find("Potions").Text = $"{potions}";
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

// Abre a mochila (ou a ficha) e lembra de onde veio, para o "Voltar" levar de volta.
void OpenFrom(string scene, string from)
{
    bagReturnsTo = from;
    game.GoTo(scene);
}

// Escolhe o inimigo e vai para a luta. Quem prepara a luta (vida cheia, mensagem) é o OnEnter da cena "Fight".
void StartFight(Enemy foe, string returnTo)
{
    enemy = foe;
    afterFight = returnTo;
    game.GoTo("Fight");
}

// Dano normal do herói: o da classe, mais o dos níveis, o da arma e o da afiação do ferreiro.
int HeroDamage()
{
    int damage = Random.Shared.Next(heroClass.MinDamage, heroClass.MaxDamage + 1) + levelDamage + sharpness * 2;
    if (weapon != null) damage += weapon.Power;
    return damage;
}

void HitEnemy(int damage)
{
    enemyHealth = Math.Max(0, enemyHealth - damage);
    game.Write($"Você causou {damage} de dano.", Color.Green);
    // O inimigo treme, a barra dele pisca e o número do dano salta em cima dele: dá para "sentir" o golpe.
    game.Find("EnemyIcon").Shake();
    game.Find("EnemyIcon").Pop($"-{damage}", Color.Red);
    game.Find("EnemyHealth").Flash();
    if (enemyHealth == 0) WinFight();
    else EnemyTurn();
}

void EnemyTurn()
{
    // Uma pausa curta: a tela mostra o seu golpe e o inimigo responde logo depois.
    game.Wait(0.6);
    // O que o inimigo faz vai para a caixa de mensagens dele (EnemyLog), do lado direito.
    var enemyLog = game.Find("EnemyLog");
    if (enemyStunned)
    {
        enemyStunned = false;
        enemyLog.Write($"{enemy.Name} ficou atordoado e perdeu a vez.", Color.Gray);
        return;
    }
    int damage = Random.Shared.Next(enemy.MinDamage, enemy.MaxDamage + 1);
    if (hasShield) damage = Math.Max(1, damage - 2);
    health = Math.Max(0, health - damage);
    enemyLog.Write($"{enemy.Name} ataca: -{damage} de vida.", Color.Red);
    game.Find("HeroIcon").Shake();
    game.Find("HeroIcon").Pop($"-{damage}", Color.Red);
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
    if (enemy == wolf)
    {
        wolvesKilled++;
        meat++;
        game.Write("Você guardou um pedaço de carne na mochila.", Color.Orange);
    }
    if (enemy == troll)
    {
        bridgeCrossed = true;
        game.Write("O caminho da ponte está livre!", Color.Gold);
    }
    if (enemy.Xp > 0) GainXp(enemy.Xp);
}

// Quanto de cada missão já foi feito (de 0 até o Goal dela).
int QuestProgress(Quest quest)
{
    switch (quest.Id)
    {
        case "Wolves":
            return Math.Min(wolvesKilled, quest.Goal);
        case "Swamp":
            return swampTreasure ? 1 : 0;
        case "Bridge":
            return bridgeCrossed ? 1 : 0;
        case "Dice":
            return Math.Min(diceWins, quest.Goal);
        default:
            return 0;
    }
}

// Salvar e carregar: cada variável é guardada com um nome. As classes e as armas são guardadas pelo nome delas.
void SaveGame()
{
    game.Save("playerName", playerName);
    game.Save("heroClass", heroClass.Name);
    game.Save("level", level);
    game.Save("xp", xp);
    game.Save("xpToNext", xpToNext);
    game.Save("maxHealth", maxHealth);
    game.Save("health", health);
    game.Save("maxMana", maxMana);
    game.Save("mana", mana);
    game.Save("levelDamage", levelDamage);
    game.Save("gold", gold);
    game.Save("potions", potions);
    game.Save("bombs", bombs);
    game.Save("herbs", herbs);
    game.Save("meat", meat);
    game.Save("weapon", weapon == null ? "" : weapon.Name);
    game.Save("hasAmulet", hasAmulet);
    game.Save("sharpness", sharpness);
    game.Save("hasArmor", hasArmor);
    game.Save("hasShield", hasShield);
    game.Save("hasKey", hasKey);
    game.Save("kindness", kindness);
    game.Save("wolvesKilled", wolvesKilled);
    game.Save("swampTreasure", swampTreasure);
    game.Save("bridgeCrossed", bridgeCrossed);
    game.Save("diceWins", diceWins);
    game.Save("questsDone", questsDone);
}

void LoadGame()
{
    playerName = game.Load("playerName", "Herói");
    string className = game.Load("heroClass", "Guerreiro");
    foreach (var each in classes)
    {
        if (each.Name == className) heroClass = each;
    }
    level = game.Load("level", 1);
    xp = game.Load("xp", 0);
    xpToNext = game.Load("xpToNext", 30);
    maxHealth = game.Load("maxHealth", heroClass.Health);
    health = game.Load("health", maxHealth);
    maxMana = game.Load("maxMana", heroClass.Mana);
    mana = game.Load("mana", maxMana);
    levelDamage = game.Load("levelDamage", 0);
    gold = game.Load("gold", 15);
    potions = game.Load("potions", 1);
    bombs = game.Load("bombs", 0);
    herbs = game.Load("herbs", 0);
    meat = game.Load("meat", 0);
    string weaponName = game.Load("weapon", "");
    weapon = null;
    foreach (var product in products)
    {
        if (product.Name == weaponName) weapon = product;
    }
    hasAmulet = game.Load("hasAmulet", false);
    sharpness = game.Load("sharpness", 0);
    hasArmor = game.Load("hasArmor", false);
    hasShield = game.Load("hasShield", false);
    hasKey = game.Load("hasKey", false);
    kindness = game.Load("kindness", 0);
    wolvesKilled = game.Load("wolvesKilled", 0);
    swampTreasure = game.Load("swampTreasure", false);
    bridgeCrossed = game.Load("bridgeCrossed", false);
    diceWins = game.Load("diceWins", 0);
    questsDone = game.Load("questsDone", new List<string>());
}

// ================================================================ 4. cenas

// ---------------------------------------------------------------- 1. Title: o nome (e "Continuar", se tiver jogo salvo)
game.Scene("Title", () =>
{
    // HasSave: já existe um jogo salvo (no templo). Sem save, o botão nem aparece.
    game.Find("Continue").Visible = game.HasSave;
    game.Find("Continue").OnClick(() =>
    {
        LoadGame();
        game.Write($"Bem-vindo de volta, {playerName}.", Color.Gold);
        game.GoTo("Village");
    });

    game.Find("NameField").OnAnswer(answer =>
    {
        // Nomes muito compridos não cabem no painel.
        playerName = answer.Length > 14 ? answer.Substring(0, 14) : answer;
        game.GoTo("ChooseClass");
    });
});

// ---------------------------------------------------------------- 2. ChooseClass: um cartão da Lista para cada classe
game.Scene("ChooseClass", () =>
{
    game.Find("Subtitle").Text = $"{playerName}, como você quer enfrentar o Rei Goblin?";

    game.Find("Classes").Show(classes, (card, choice) =>
    {
        card.Find("CardBack").Color = choice.Color;
        card.Find("Icon").Icon = choice.Icon;
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

// ---------------------------------------------------------------- 3. Village: a vila, com um cartão para cada lugar
game.Scene("Village", () =>
{
    ShowStatus();
    game.Find("Story").Text = hasKey
        ? "A chave da torre pesa no seu bolso. O povo da vila olha para você com esperança."
        : "O Rei Goblin roubou a coroa da vila. Dizem que a chave da torre dele está numa caverna, depois da ponte do troll.";

    // Missões prontas para receber: o cartão do quadro avisa.
    int ready = 0;
    foreach (var quest in quests)
    {
        if (QuestProgress(quest) >= quest.Goal && !questsDone.Contains(quest.Id)) ready++;
    }
    game.Find("QuestsText").Text = ready > 0 ? $"{ready} prêmio(s) esperando por você!" : "Trabalhos que pagam bem.";
    game.Find("QuestsText").Color = ready > 0 ? Color.Gold : Color.Gray;

    game.Find("Tavern").OnClick(() => game.GoTo("Tavern"));
    game.Find("Shop").OnClick(() => game.GoTo("Shop"));
    game.Find("Smith").OnClick(() => game.GoTo("Blacksmith"));
    game.Find("Temple").OnClick(() => game.GoTo("Temple"));
    game.Find("Quests").OnClick(() => game.GoTo("Quests"));
    game.Find("Gate").OnClick(() => game.GoTo("Map"));
    game.Find("Bag").OnClick(() => OpenFrom("Bag", "Village"));
    game.Find("Sheet").OnClick(() => OpenFrom("Hero", "Village"));
});

// ---------------------------------------------------------------- 4. Tavern: descansar e o jogo de dados do Bartô
game.Scene("Tavern", () =>
{
    ShowStatus();

    // O Contador da aposta: vai de 1 até 20 (ou até o seu ouro, se você tiver menos).
    var betCounter = game.Find("Bet");
    betCounter.Max = Math.Max(1, Math.Min(20, gold));
    bet = Math.Min(bet, betCounter.Max);
    betCounter.Value = bet;
    betCounter.OnChange(value => bet = value);
    game.Find("Play").Enabled = gold >= 1;

    game.Find("Sleep").OnClick(() =>
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
        game.Find("Health").Flash();
    });

    // Cada um rola um dado de 6 lados: quem tirar mais leva a aposta. Roll() mostra o dado rolando e devolve o número.
    game.Find("Play").OnClick(() =>
    {
        int mine = game.Find("MyDie").Roll();
        int his = game.Find("HisDie").Roll();
        if (mine > his)
        {
            gold += bet;
            diceWins++;
            game.Write($"Você tirou {mine} e o Bartô tirou {his}. Você ganhou {bet} de ouro!", Color.Gold);
            game.Find("Gold").Pop($"+{bet}", Color.Gold);
        }
        else if (mine < his)
        {
            gold -= bet;
            game.Write($"Você tirou {mine} e o Bartô tirou {his}. Ele ri e guarda as suas {bet} moedas.", Color.Red);
            game.Find("Gold").Pop($"-{bet}", Color.Red);
        }
        else
        {
            game.Write($"Os dois tiraram {mine}. Empate: ninguém perde nada.", Color.Gray);
        }
    });

    game.Find("Fortune").OnClick(() => game.GoTo("Fortune"));
    game.Find("Back").OnClick(() => game.GoTo("Village"));
});

// ---------------------------------------------------------------- 5. Fortune: a roleta da cigana Zara
game.Scene("Fortune", () =>
{
    ShowStatus();
    game.Find("Spin").Enabled = gold >= 10;

    game.Find("Spin").OnClick(() =>
    {
        gold -= 10;
        // Spin() gira a roleta, espera ela parar e devolve o texto da fatia. As fatias estão escritas na tela (Estúdio).
        string prize = game.Find("Wheel").Spin();
        switch (prize)
        {
            case "30 de ouro":
                gold += 30;
                game.Write("A roleta para no ouro: +30!", Color.Gold);
                break;
            case "Poção":
                potions++;
                game.Write("Zara te dá uma poção de vida.", Color.Green);
                break;
            case "Bomba":
                bombs++;
                game.Write("Uma bomba! \"Cuidado com isso\", ela sussurra.", Color.Orange);
                break;
            case "Vida +10":
                maxHealth += 10;
                health += 10;
                game.Write("Você se sente mais forte: +10 de vida máxima.", Color.Green);
                break;
            case "Mana +10":
                maxMana += 10;
                mana += 10;
                game.Write("Uma luz azul te envolve: +10 de mana máxima.", Color.Blue);
                break;
            case "Maldição":
                health = Math.Max(1, health - 10);
                game.Write("Maldição! Um arrepio frio: -10 de vida.", Color.Purple);
                game.Find("Health").Flash();
                break;
            default:
                game.Write("Nada desta vez. \"O destino é caprichoso\", diz Zara.", Color.Gray);
                break;
        }
    });

    game.Find("Back").OnClick(() => game.GoTo("Tavern"));
});

// ---------------------------------------------------------------- 6. Shop: a Lista "Goods" mostra um cartão para cada coisa de "products"
game.Scene("Shop", () =>
{
    ShowWallet();
    game.Find("Goods").Show(products, (card, product) =>
    {
        card.Find("CardBack").Color = product.Color;
        card.Find("Icon").Icon = product.Icon;
        card.Find("Icon").Color = product.Color;
        card.Find("Name").Text = product.Name;
        card.Find("About").Text = product.About;
        card.Find("Price").Text = $"{product.Price}";

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
        case ProductKind.Bomb:
            bombs++;
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

// ---------------------------------------------------------------- 7. Blacksmith: escolher uma melhoria (Escolha) e forjar
game.Scene("Blacksmith", () =>
{
    ShowStatus();

    // A Escolha guarda a opção na variável chosenUpgrade; a cena mostra o preço e o que ela faz.
    var upgrade = game.Find("Upgrade");
    upgrade.Selected = chosenUpgrade;
    upgrade.OnChoose(choice => chosenUpgrade = choice);

    int price = 0;
    string about = "";
    bool done = false;
    switch (chosenUpgrade)
    {
        case "Afiar a arma":
            price = 20 + sharpness * 10;
            about = $"+2 de dano em todo ataque. Afiação agora: {sharpness} de 3.";
            done = sharpness >= 3;
            break;
        case "Armadura de couro":
            price = 25;
            about = "+20 de vida máxima, para sempre.";
            done = hasArmor;
            break;
        case "Escudo de ferro":
            price = 30;
            about = "Todo golpe dos inimigos tira 2 a menos.";
            done = hasShield;
            break;
    }
    game.Find("Details").Text = about;
    game.Find("Price").Text = done ? "Já está pronto" : $"{price} de ouro";
    var forge = game.Find("Forge");
    forge.Enabled = !done && gold >= price;
    forge.Text = done ? "✓ Feito" : gold >= price ? "Forjar" : "Falta ouro";

    // A barra em blocos mostra a afiação (0 a 3) e os ícones acendem o que você já tem.
    game.Find("Sharpness").Value = sharpness;
    game.Find("ArmorIcon").Color = hasArmor ? Color.Brown : Color.Gray;
    game.Find("ShieldIcon").Color = hasShield ? Color.Blue : Color.Gray;

    forge.OnClick(() =>
    {
        gold -= price;
        switch (chosenUpgrade)
        {
            case "Afiar a arma":
                sharpness++;
                break;
            case "Armadura de couro":
                hasArmor = true;
                maxHealth += 20;
                health += 20;
                break;
            case "Escudo de ferro":
                hasShield = true;
                break;
        }
        game.Write($"Gunnar bate o martelo: \"{chosenUpgrade}, pronto!\"", Color.Orange);
        game.Find("Anvil").Shake();
    });

    game.Find("Back").OnClick(() => game.GoTo("Village"));
});

// ---------------------------------------------------------------- 8. Temple: doar (Contador) e salvar o jogo
game.Scene("Temple", () =>
{
    ShowStatus();

    var gift = game.Find("Donation");
    gift.Max = Math.Max(5, Math.Min(50, gold - gold % 5));
    donation = Math.Min(donation, gift.Max);
    gift.Value = donation;
    gift.OnChange(value => donation = value);
    game.Find("Donate").Enabled = gold >= donation;

    // Doar cura o dobro do que você doou. Doações grandes contam como uma boa ação.
    game.Find("Donate").OnClick(() =>
    {
        gold -= donation;
        Heal(donation * 2);
        game.Write($"A sacerdotisa agradece. A luz do templo te cura: +{donation * 2} de vida.", Color.Gold);
        if (donation >= 20)
        {
            kindness++;
            game.Write("Uma doação generosa. A vila vai lembrar disso.", Color.Green);
        }
        game.Find("Health").Flash();
    });

    // game.Pause espera o jogador apertar o botão antes de continuar (como um Console.ReadKey).
    game.Find("Pray").OnClick(() =>
    {
        game.Write("Você fecha os olhos e escuta o silêncio do templo...", Color.Gray);
        game.Pause("Abrir os olhos");
        mana = maxMana;
        game.Write("Você se sente em paz. Mana cheia.", Color.Blue);
    });

    // Salvar: guarda tudo. No começo do jogo, o botão "Continuar" carrega daqui.
    game.Find("Save").OnClick(() =>
    {
        SaveGame();
        game.Write("Você acende uma vela. Seu progresso foi guardado.", Color.Gold);
        game.Find("Candle").Flash();
    });

    game.Find("Back").OnClick(() => game.GoTo("Village"));
});

// ---------------------------------------------------------------- 9. Quests: o quadro de missões (Lista com barra de progresso)
game.Scene("Quests", () =>
{
    ShowStatus();
    game.Find("Board").Show(quests, (card, quest) =>
    {
        int progress = QuestProgress(quest);
        bool claimed = questsDone.Contains(quest.Id);

        card.Find("Icon").Icon = quest.Icon;
        card.Find("Icon").Color = quest.Color;
        card.Find("Name").Text = quest.Title;
        card.Find("About").Text = quest.About;
        card.Find("Reward").Text = $"Prêmio: {quest.Gold} de ouro e {quest.Xp} de XP";
        var bar = card.Find("Progress");
        bar.Max = quest.Goal;
        bar.Value = progress;

        var claim = card.Find("Claim");
        claim.Enabled = progress >= quest.Goal && !claimed;
        claim.Text = claimed ? "✓ Recebido" : progress >= quest.Goal ? "Receber prêmio" : "Em andamento";
        claim.OnClick(() =>
        {
            questsDone.Add(quest.Id);
            gold += quest.Gold;
            game.Write($"Missão cumprida: {quest.Title}! +{quest.Gold} de ouro.", Color.Gold);
            GainXp(quest.Xp);
        });
    });
    game.Find("Back").OnClick(() => game.GoTo("Village"));
});

// ---------------------------------------------------------------- 10. Bag: a mochila (Lista montada com o que você tem)
game.Scene("Bag", () =>
{
    ShowStatus();

    // Só entram na lista as coisas que você tem (Count maior que 0).
    var lines = new List<BagLine>();
    if (potions > 0) lines.Add(new("Poção de vida", Icon.Potion, Color.Green, potions, "Cura 35 de vida.", "Beber", true));
    if (herbs > 0) lines.Add(new("Erva", Icon.Herb, Color.Green, herbs, "Cura 15 de vida.", "Mastigar", true));
    if (meat > 0) lines.Add(new("Carne", Icon.Meat, Color.Orange, meat, "Crua cura 10. Assada na fogueira, 30.", "Comer crua", true));
    if (bombs > 0) lines.Add(new("Bomba", Icon.Bomb, Color.Orange, bombs, "25 de dano. Só na luta.", "Só na luta", false));
    if (hasKey) lines.Add(new("Chave da torre", Icon.Key, Color.Gold, 1, "Abre o portão do Rei Goblin.", "Item de missão", false));

    game.Find("Items").Show(lines, (card, line) =>
    {
        card.Find("Icon").Icon = line.Icon;
        card.Find("Icon").Color = line.Color;
        card.Find("Name").Text = line.Name;
        card.Find("Count").Text = $"x{line.Count}";
        card.Find("About").Text = line.About;
        var use = card.Find("Use");
        use.Text = line.Action;
        use.Enabled = line.CanUse;
        use.OnClick(() => UseItem(line.Name));
    });

    // O equipamento fica num painel separado (não se usa: está sempre valendo).
    game.Find("WeaponText").Text = weapon == null ? "Arma da classe" : $"{weapon.Name} (+{weapon.Power})";
    game.Find("SharpText").Text = $"Afiação {sharpness} de 3";
    game.Find("ArmorText").Text = hasArmor ? "Armadura de couro" : "Sem armadura";
    game.Find("ShieldText").Text = hasShield ? "Escudo de ferro" : "Sem escudo";
    game.Find("AmuletText").Text = hasAmulet ? "Amuleto de mana" : "Sem amuleto";

    game.Find("Back").OnClick(() => game.GoTo(bagReturnsTo));
});

void UseItem(string name)
{
    switch (name)
    {
        case "Poção de vida":
            potions--;
            Heal(35);
            game.Write("Você bebeu uma poção: +35 de vida.", Color.Green);
            break;
        case "Erva":
            herbs--;
            Heal(15);
            game.Write("Amarga, mas funciona: +15 de vida.", Color.Green);
            break;
        case "Carne":
            meat--;
            Heal(10);
            game.Write("Carne crua... +10 de vida. Na fogueira seria melhor.", Color.Orange);
            break;
    }
    game.Find("Health").Flash();
}

// ---------------------------------------------------------------- 11. Hero: a ficha do herói (só mostra, não muda nada)
game.Scene("Hero", () =>
{
    ShowStatus();
    game.Find("Portrait").Icon = heroClass.Icon;
    game.Find("Portrait").Color = heroClass.Color;
    game.Find("Name").Text = playerName;
    game.Find("Class").Text = $"{heroClass.Name} · Nível {level}";
    game.Find("Class").Color = heroClass.Color;

    int bonus = levelDamage + sharpness * 2 + (weapon == null ? 0 : weapon.Power);
    game.Find("Damage").Text = $"Dano: {heroClass.MinDamage + bonus} a {heroClass.MaxDamage + bonus}";
    game.Find("Skill").Text = $"Especial: {heroClass.Skill} ({heroClass.SkillCost} de mana)";
    game.Find("Defense").Text = hasShield ? "Escudo: os golpes tiram 2 a menos" : "Sem escudo";
    game.Find("Crit").Text = heroClass.Name == "Ladino" ? "Crítico: tirar 18, 19 ou 20 no d20" : "Crítico: tirar 20 no d20";

    // A barra da bondade vai de 0 a 10; o meio (5) é neutro.
    game.Find("Kindness").Value = Math.Clamp(kindness + 5, 0, 10);
    game.Find("QuestCount").Text = $"Missões cumpridas: {questsDone.Count} de {quests.Count}";
    game.Find("KeyIcon").Color = hasKey ? Color.Gold : Color.Gray;
    game.Find("KeyText").Text = hasKey ? "Você tem a chave da torre" : "A chave está na caverna";

    game.Find("Back").OnClick(() => game.GoTo(bagReturnsTo));
});

// ---------------------------------------------------------------- 12. Map: o mapa do reino (fora da vila)
game.Scene("Map", () =>
{
    ShowStatus();

    // A caverna fica depois da ponte; a torre só abre com a chave. Sem isso, o botão fica apagado.
    var cave = game.Find("Cave");
    cave.Enabled = bridgeCrossed;
    cave.Text = bridgeCrossed ? "Caverna" : "🔒 Caverna";
    game.Find("CaveIcon").Color = bridgeCrossed ? Color.Purple : Color.Gray;

    var tower = game.Find("Tower");
    tower.Enabled = hasKey;
    tower.Text = hasKey ? "Torre" : "🔒 Torre";
    game.Find("TowerIcon").Color = hasKey ? Color.Red : Color.Gray;

    game.Find("Village").OnClick(() => game.GoTo("Village"));
    game.Find("Forest").OnClick(() => game.GoTo("Forest"));
    game.Find("Swamp").OnClick(() => game.GoTo("Swamp"));
    game.Find("Camp").OnClick(() => game.GoTo("Camp"));
    game.Find("Bridge").OnClick(() => game.GoTo("Bridge"));
    cave.OnClick(() => game.GoTo("Cave"));
    tower.OnClick(() => game.GoTo("Tower"));
    game.Find("Bag").OnClick(() => OpenFrom("Bag", "Map"));
    game.Find("Sheet").OnClick(() => OpenFrom("Hero", "Map"));
});

// ---------------------------------------------------------------- 13. Forest: cada passo pode dar ouro, uma luta ou um encontro
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
        herbs++;
        game.Write("Você achou uma erva que cura e guardou na mochila.", Color.Green);
    });

    game.Find("Back").OnClick(() => game.GoTo("Map"));
});

// ---------------------------------------------------------------- 14. Event: encontros com escolhas (uma cena para todos)
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
            ShowPortrait(Icon.Person, Color.Orange);
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
            ShowPortrait(Icon.Gift, Color.Gold);
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

// O retrato do encontro: o desenho (Ícone) e a cor dele.
void ShowPortrait(Icon icon, Color color)
{
    game.Find("Portrait").Icon = icon;
    game.Find("Portrait").Color = color;
}

// ---------------------------------------------------------------- 15. Swamp: caça ao tesouro na Grade
game.Scene("Swamp", () =>
{
    // Na chegada: o baú vai para uma casa sorteada, a grade fica limpa e você tem 6 tentativas.
    game.OnEnter(() =>
    {
        treasureX = Random.Shared.Next(6);   // coluna de 0 a 5
        treasureY = Random.Shared.Next(4);   // linha de 0 a 3
        digsLeft = 6;
        foundThisVisit = false;
        game.Find("Mud").Clear();
    });

    ShowStatus();
    game.Find("Digs").Text = foundThisVisit ? "Você achou o baú!" : $"Tentativas: {digsLeft}";
    game.Find("Shovels").Value = digsLeft;

    // x é a coluna e y é a linha da casa clicada (a de cima à esquerda é 0, 0).
    game.Find("Mud").OnCellClick((x, y) =>
    {
        var cell = game.Find("Mud").Cell(x, y);
        if (digsLeft == 0 || foundThisVisit || !cell.IsEmpty) return;   // sem tentativas, ou casa já cavada
        digsLeft--;

        // A distância até o baú: quantas casas para o lado mais quantas para cima ou para baixo.
        int distance = Math.Abs(x - treasureX) + Math.Abs(y - treasureY);
        if (distance == 0)
        {
            cell.Text = "💰";
            cell.Color = Color.Gold;
            foundThisVisit = true;
            int prize = swampTreasure ? 10 : 30;   // a primeira vez vale mais (e conta para a missão)
            gold += prize;
            swampTreasure = true;
            game.Write($"Você achou o baú! +{prize} de ouro.", Color.Gold);
        }
        else if (distance <= 1)
        {
            cell.Text = "🔥";
            cell.Color = Color.Orange;
            game.Write("Quente! O baú está do lado.", Color.Orange);
        }
        else if (distance <= 3)
        {
            cell.Text = "~";
            cell.Color = Color.Gold;
            game.Write("Morno... está perto.", Color.Gold);
        }
        else
        {
            cell.Text = "❄";
            cell.Color = Color.Blue;
            game.Write("Frio. O baú está longe daqui.", Color.Blue);
        }

        // Acabaram as tentativas sem achar: uma cobra sai da lama.
        if (digsLeft == 0 && !foundThisVisit)
        {
            game.Write("A lama se mexe... uma cobra!", Color.Red);
            game.Wait(0.6);
            StartFight(snake, "Map");
        }
    });

    game.Find("Back").OnClick(() => game.GoTo("Map"));
});

// ---------------------------------------------------------------- 16. Camp: a fogueira (carne, dormir com dado, barulhos)
game.Scene("Camp", () =>
{
    ShowStatus();
    game.Find("Cook").Enabled = meat > 0;
    game.Find("Cook").Text = $"Assar carne ({meat})";

    game.Find("Cook").OnClick(() =>
    {
        meat--;
        Heal(30);
        game.Write("O cheiro de carne assada... +30 de vida.", Color.Orange);
        game.Find("Fire").Flash();
    });

    // Dormir ao relento: um d20 decide se a noite é tranquila (8 ou mais) ou se algo ataca.
    game.Find("Sleep").OnClick(() =>
    {
        int roll = game.Find("NightDie").Roll();
        if (roll >= 8)
        {
            Heal(maxHealth / 2);
            mana = Math.Min(maxMana, mana + maxMana / 2);
            game.Write($"Tirou {roll}: uma noite tranquila. Metade da vida e da mana de volta.", Color.Green);
        }
        else
        {
            game.Write($"Tirou {roll}: olhos brilham no escuro...", Color.Red);
            game.Wait(0.6);
            StartFight(wolf, "Camp");
        }
    });

    // game.Choose mostra uma pergunta com botões e espera a resposta (o texto do botão escolhido).
    game.Find("Listen").OnClick(() =>
    {
        string choice = game.Choose("Um galho estala no escuro. O que fazer?", "Ir ver o que é", "Ficar quieto");
        if (choice == "Ficar quieto")
        {
            game.Write("Você prende a respiração. O barulho vai embora.", Color.Gray);
        }
        else if (Random.Shared.Next(100) < 50)
        {
            gold += 10;
            game.Write("Era só um esquilo... perto de umas moedas perdidas! +10 de ouro.", Color.Gold);
        }
        else
        {
            StartFight(spider, "Camp");
        }
    });

    game.Find("Back").OnClick(() => game.GoTo("Map"));
});

// ---------------------------------------------------------------- 17. Bridge: as perguntas do troll (Escolha)
game.Scene("Bridge", () =>
{
    ShowStatus();
    var answers = game.Find("Answers");
    var reply = game.Find("Reply");

    if (bridgeCrossed)
    {
        // Já passou: o troll deixa o caminho livre.
        game.Find("Question").Text = "\"Pode passar, esperto.\" O troll nem levanta os olhos.";
        game.Find("Counter").Text = "Ponte livre";
        answers.Visible = false;
        reply.Visible = false;
    }
    else
    {
        var riddle = riddles[riddleIndex];
        game.Find("Question").Text = riddle.Question;
        game.Find("Counter").Text = $"Pergunta {riddleIndex + 1} de {riddles.Count}";
        answers.Options = riddle.Options;

        reply.OnClick(() =>
        {
            // Selected: a opção que o jogador escolheu ("" se ainda não escolheu nenhuma).
            string picked = answers.Selected;
            if (picked == "")
            {
                game.Write("Escolha uma resposta primeiro.", Color.Gray);
                return;
            }
            if (picked == riddle.Answer)
            {
                riddleIndex++;
                answers.Selected = "";
                game.Write("\"Hmpf. Certo.\"", Color.Green);
                if (riddleIndex == riddles.Count)
                {
                    bridgeCrossed = true;
                    game.Write("O troll sai do caminho. A ponte está livre!", Color.Gold);
                    GainXp(10);
                }
            }
            else
            {
                riddleIndex = 0;
                game.Write("\"ERRADO!\" O troll pega o porrete.", Color.Red);
                game.Find("TrollIcon").Shake();
                game.Wait(0.6);
                StartFight(troll, "Bridge");
            }
        });
    }

    game.Find("Back").OnClick(() => game.GoTo("Map"));
});

// ---------------------------------------------------------------- 18. Cave: um enigma para responder escrevendo
game.Scene("Cave", () =>
{
    ShowStatus();
    game.Find("Riddle").Visible = !hasKey;
    game.Find("Echo").Visible = !hasKey;
    game.Find("KeyIcon").Visible = hasKey;
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
    game.Find("Back").OnClick(() => game.GoTo("Map"));
});

// ---------------------------------------------------------------- 19. Tower: a porta da torre
game.Scene("Tower", () =>
{
    ShowStatus();
    game.Find("Enter").OnClick(() => game.GoTo("TowerHall"));
    game.Find("Back").OnClick(() => game.GoTo("Map"));
});

// ---------------------------------------------------------------- 20. TowerHall: três armadilhas (d20) antes do chefe
game.Scene("TowerHall", () =>
{
    game.OnEnter(() => trapsPassed = 0);

    ShowStatus();
    bool cleared = trapsPassed == trapNames.Length;
    game.Find("Jump").Visible = !cleared;
    game.Find("Throne").Visible = cleared;
    game.Find("Steps").Value = trapsPassed;
    if (cleared)
    {
        game.Find("TrapName").Text = "O salão do trono";
        game.Find("TrapIcon").Icon = Icon.Crown;
        game.Find("TrapIcon").Color = Color.Gold;
        game.Find("TrapText").Text = "Atrás da última porta, o Rei Goblin espera sentado no trono, com a coroa na cabeça.";
    }
    else
    {
        game.Find("TrapName").Text = trapNames[trapsPassed];
        game.Find("TrapIcon").Icon = trapIcons[trapsPassed];
        game.Find("TrapIcon").Color = Color.Red;
        game.Find("TrapText").Text = "Role o d20: 10 ou mais e você passa. Menos que isso, a armadilha te pega (-12 de vida).";
    }

    game.Find("Jump").OnClick(() =>
    {
        int roll = game.Find("TrapDie").Roll();
        if (roll >= 10)
        {
            game.Write($"Tirou {roll}: você passou pela armadilha!", Color.Green);
        }
        else
        {
            health = Math.Max(1, health - 12);
            game.Write($"Tirou {roll}: a armadilha te pegou. -12 de vida.", Color.Red);
            game.Find("Health").Flash();
            game.Find("TrapIcon").Shake();
        }
        trapsPassed++;
    });

    game.Find("Throne").OnClick(() =>
    {
        game.Write("A porta do salão range devagar...", Color.Gray);
        game.Pause("Entrar no salão");
        StartFight(goblinKing, "Victory");
    });
});

// ---------------------------------------------------------------- 21. Fight: a luta (a mesma cena para todos os inimigos)
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
    // Os retratos: o desenho da classe do herói e o do inimigo, cada um num Ícone com moldura redonda.
    game.Find("HeroIcon").Icon = heroClass.Icon;
    game.Find("HeroIcon").Color = heroClass.Color;
    var enemyIcon = game.Find("EnemyIcon");
    enemyIcon.Icon = enemy.Icon;
    enemyIcon.Color = enemy.Color;
    game.Find("EnemyName").Text = enemy.Name;
    var enemyBar = game.Find("EnemyHealth");
    enemyBar.Value = enemyHealth;
    enemyBar.Max = enemy.Health;
    game.Find("Story").Text = fightOver
        ? "A luta acabou."
        : enemy.IsBoss ? "O Rei Goblin ergue o machado. É agora ou nunca." : $"{enemy.Name} bloqueia o caminho.";

    // Durante a luta, os botões de ação; depois, só o "Continuar".
    var attack = game.Find("Attack");
    var skill = game.Find("Skill");
    var potion = game.Find("Potion");
    var bomb = game.Find("Bomb");
    var flee = game.Find("Flee");
    attack.Visible = !fightOver;
    skill.Visible = !fightOver;
    potion.Visible = !fightOver;
    bomb.Visible = !fightOver;
    flee.Visible = !fightOver && !enemy.IsBoss;
    game.Find("Continue").Visible = fightOver;

    // A habilidade especial vem da classe; sem mana (ou sem poção, ou sem bomba), o botão fica apagado.
    skill.Text = $"{heroClass.Skill} ({heroClass.SkillCost})";
    skill.Enabled = mana >= heroClass.SkillCost;
    potion.Text = $"Poção ({potions})";
    potion.Enabled = potions > 0;
    bomb.Text = $"Bomba ({bombs})";
    bomb.Enabled = bombs > 0;

    // Atacar: o d20 decide. 1 = errou; 20 (ou 18+ no Ladino) = crítico, dano em dobro.
    attack.OnClick(() =>
    {
        int roll = game.Find("D20").Roll();
        int critFrom = heroClass.Name == "Ladino" ? 18 : 20;
        if (roll == 1)
        {
            game.Write("Tirou 1 no dado: você errou o golpe!", Color.Gray);
            EnemyTurn();
            return;
        }
        int damage = HeroDamage();
        if (roll >= critFrom)
        {
            damage *= 2;
            game.Write($"Tirou {roll}: GOLPE CRÍTICO!", Color.Orange);
        }
        HitEnemy(damage);
    });

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
        game.Find("HeroIcon").Pop("+35", Color.Green);
        game.Find("Health").Flash();
        EnemyTurn();
    });

    bomb.OnClick(() =>
    {
        bombs--;
        game.Write("Você joga a bomba. BUM!", Color.Orange);
        HitEnemy(25);
    });

    flee.OnClick(() =>
    {
        if (Random.Shared.Next(100) < 60)
        {
            game.Write("Você conseguiu fugir.", Color.Gray);
            game.GoTo(afterFight == "Victory" ? "Map" : afterFight);
        }
        else
        {
            game.Write("Não deu para fugir!", Color.Orange);
            EnemyTurn();
        }
    });

    game.Find("Continue").OnClick(() => game.GoTo(afterFight));
});

// ---------------------------------------------------------------- 22 e 23. Os finais: o que você fez no caminho muda o texto
game.Scene("Victory", () =>
{
    game.Find("Story").Text = kindness > 0
        ? $"{playerName} volta com a coroa. A vila faz uma festa, e o velho viajante está lá, sem mancar, levantando um copo para você."
        : kindness < 0
            ? $"{playerName} volta com a coroa. A vila comemora, mas alguém comenta baixinho sobre um viajante roubado na floresta..."
            : $"{playerName} volta com a coroa. A vila inteira grita o seu nome.";
    game.Find("Summary").Text = $"{heroClass.Name} · nível {level} · {gold} de ouro · {questsDone.Count} missões";
    game.Find("Again").OnClick(Restart);
});

game.Scene("GameOver", () =>
{
    game.Find("Story").Text = $"{playerName} caiu diante de {enemy.Name}. Mas lendas sempre ganham uma segunda chance.";
    // Com jogo salvo no templo, dá para voltar de lá em vez de começar tudo de novo.
    game.Find("Load").Visible = game.HasSave;
    game.Find("Load").OnClick(() =>
    {
        LoadGame();
        game.Write("Você acorda no templo, com a vela ainda acesa.", Color.Gold);
        game.GoTo("Temple");
    });
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
    bombs = 0;
    herbs = 0;
    meat = 0;
    kindness = 0;
    weapon = null;
    hasAmulet = false;
    sharpness = 0;
    hasArmor = false;
    hasShield = false;
    hasKey = false;
    wolvesKilled = 0;
    swampTreasure = false;
    bridgeCrossed = false;
    diceWins = 0;
    questsDone = new List<string>();
    riddleIndex = 0;
    currentEvent = "";
    game.GoTo("ChooseClass");
}

game.Start("Title");
