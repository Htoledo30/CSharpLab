# Guia deste jogo (para IAs e para quem programa)

Este é um **jogo com botões** feito no CSharp Lab: um jogo de texto em C#, numa janela com títulos, textos, barras e botões. Quem cria é iniciante em programação. Escreva código que **ele consiga ler e mudar**.

## Como funciona

- Tudo fica em `Program.cs`, com top-level statements. O motor é a biblioteca `lib/CSharpLab.Game.dll`, e o `using CSharpLab.GameEngine;` já vem pelo `.csproj`.
- O jogo é um conjunto de **cenas**. Cada cena é uma função que **desenha a tela inteira** com os valores atuais das variáveis.
- **Depois de cada clique, a cena atual é desenhada de novo sozinha.** Por isso basta mudar as variáveis no botão: as barras e os textos se atualizam.
- O estado do jogo (vida, ouro, nome, inventário…) fica em **variáveis declaradas no topo**, antes das cenas.
- `game.Say` dentro de um botão vira um **aviso destacado** que aparece depois do clique ("Você causou 7 de dano!"). Some no clique seguinte.
- As teclas 1 a 9 apertam os botões.
- Rodar: F5 no CSharp Lab (ou `dotnet run`).

## API completa

| Código | O que faz |
|---|---|
| `var game = new Game("Título");` | Cria o jogo. |
| `game.Scene("Name", () => { ... });` | Cria uma cena (tela). Nomes em inglês: `"Start"`, `"Forest"`, `"Fight"`. |
| `game.Title("texto");` | Título grande da cena. |
| `game.Say("texto");` / `game.Say("texto", GameColor.Red);` | Parágrafo de texto, com cor opcional. |
| `game.Button("Texto", () => { ... });` | Botão. O código roda no clique. |
| `game.Bar("Vida", value, max, GameColor.Green);` | Barra (vida, mana…), mostrada à direita. |
| `game.Ask("Pergunta?", answer => { ... });` | Campo para o jogador escrever; recebe o texto (já sem espaços nas pontas e nunca vazio). |
| `game.Image("arquivo.png");` | Imagem da pasta `Assets/`. |
| `game.GoTo("Name");` | Troca de cena (pode ser usado em botões e dentro de cenas). |
| `game.CurrentScene` | Nome da cena atual. |
| `game.Run("Start");` | Abre a janela na primeira cena. **Sempre a última linha.** |

Cores (`GameColor`): `White`, `Gray`, `Red`, `Green`, `Blue`, `Gold`, `Purple`, `Orange`.

## Regras

1. **Não use `Console`** (`ReadLine`/`WriteLine`): é um jogo de janela. Use `game.Ask` e `game.Say`.
2. `Title`, `Button`, `Bar`, `Ask` e `Image` só funcionam **dentro de uma cena**. `Scene` nunca fica dentro de outra cena.
3. Não faça loops de jogo (`while (true)`) nem `Thread.Sleep`. O jogo anda pelos cliques.
4. Para condições que mudam de tela, use `GoTo` dentro da cena: `if (health <= 0) game.GoTo("GameOver");`.
5. Números aleatórios: `Random.Shared.Next(min, max + 1)`.
6. **Nomes de código em inglês** (`health`, `gold`, `enemyHealth`). **Textos do jogo e comentários em português.**
7. Prefira código simples de iniciante: variáveis, `if`, `switch`, listas, métodos pequenos. Classes só quando ajudarem de verdade (ex.: `class Enemy`). Evite LINQ avançado, genéricos, eventos e async.
8. Comente o que não for óbvio, em português e com frases curtas.
9. Imagens vão na pasta `Assets/` (png ou jpg), já copiada para o jogo pelo `.csproj`.

## Exemplo completo

```csharp
var game = new Game("A Torre");

string playerName = "";
int health = 100;
int potions = 2;
int enemyHealth = 30;

game.Scene("Name", () =>
{
    game.Title("A Torre");
    game.Ask("Qual é o seu nome, aventureiro?", answer =>
    {
        playerName = answer;
        game.GoTo("Fight");
    });
});

game.Scene("Fight", () =>
{
    // Quando alguém chega a 0, a cena manda para outra.
    if (enemyHealth <= 0) game.GoTo("Victory");
    if (health <= 0) game.GoTo("GameOver");

    game.Title("Um goblin aparece!");
    game.Say($"{playerName}, o goblin rosna para você.");
    game.Bar(playerName, health, 100, GameColor.Green);
    game.Bar("Goblin", enemyHealth, 30, GameColor.Red);

    game.Button("Atacar", () =>
    {
        int damage = Random.Shared.Next(5, 11);
        enemyHealth -= damage;
        game.Say($"Você causou {damage} de dano!");

        if (enemyHealth > 0)
        {
            int hit = Random.Shared.Next(3, 9);
            health -= hit;
            game.Say($"O goblin revida: -{hit} de vida.", GameColor.Red);
        }
    });

    if (potions > 0)
    {
        game.Button($"Beber poção ({potions})", () =>
        {
            potions--;
            health = Math.Min(100, health + 25);
            game.Say("Você se sente melhor.", GameColor.Green);
        });
    }
});

game.Scene("Victory", () =>
{
    game.Title("Vitória!");
    game.Say($"{playerName} venceu o goblin.", GameColor.Gold);
});

game.Scene("GameOver", () =>
{
    game.Title("Fim de jogo");
    game.Say("Você caiu...", GameColor.Red);
    game.Button("Tentar de novo", () =>
    {
        health = 100;
        potions = 2;
        enemyHealth = 30;
        game.GoTo("Fight");
    });
});

game.Run("Name");
```

## Erros

Os erros do motor (`GameException`) já explicam em português o que fazer, por exemplo uma cena com nome errado ou um botão fora de uma cena. O CSharp Lab mostra a linha do erro no painel Problemas.
