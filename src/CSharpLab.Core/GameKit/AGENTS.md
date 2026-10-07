# Guia deste jogo (para IAs e para quem programa)

> Este guia é atualizado pelo CSharp Lab junto com o motor do jogo. Anotações suas vão em outro arquivo.

Este é um **jogo com botões** feito no CSharp Lab: um jogo de texto em C#, numa janela com títulos, textos, barras e botões. Quem cria é iniciante em programação. Escreva código que **ele consiga ler e mudar**.

## Como funciona

- Tudo fica em `Program.cs`, com top-level statements. O motor é a biblioteca `lib/CSharpLab.Game.dll`, e o `using CSharpLab.GameEngine;` já vem pelo `.csproj`.
- O jogo é um conjunto de **cenas**. Cada cena é uma função que **desenha a tela inteira** com os valores atuais das variáveis.
- **Depois de cada clique, a cena atual é desenhada de novo sozinha.** Por isso basta mudar as variáveis no botão: as barras e os textos se atualizam.
- O estado do jogo (vida, ouro, nome, inventário…) fica em **variáveis declaradas no topo**, antes das cenas.
- `game.Write` dentro de um botão vira um **aviso destacado** que aparece depois do clique ("Você causou 7 de dano!"). Some no clique seguinte.
- As teclas 1 a 9 apertam os botões.
- Rodar: F5 no CSharp Lab (ou `dotnet run`).

## API completa

| Código | O que faz |
|---|---|
| `var game = new Game("Título");` | Cria o jogo. |
| `game.Scene("Name", () => { ... });` | Cria uma cena (tela). Nomes em inglês: `"Start"`, `"Forest"`, `"Fight"`. |
| `game.Title("texto");` | Título grande da cena. |
| `game.Write("texto");` / `game.Write("texto", Color.Red);` | Parágrafo de texto, com cor opcional. |
| `game.Button("Texto", () => { ... });` | Botão. O código roda no clique. |
| `game.Bar("Vida", value, max, Color.Green);` | Barra (vida, mana…), mostrada à direita. |
| `game.Ask("Pergunta?", answer => { ... });` | Campo para o jogador escrever; recebe o texto (já sem espaços nas pontas e nunca vazio). |
| `game.Image("arquivo.png");` | Imagem da pasta `Assets/`. |
| `game.GoTo("Name");` | Troca de cena (pode ser usado em botões e dentro de cenas). |
| `game.CurrentScene` | Nome da cena atual. |
| `game.Start("Start");` | Abre a janela na primeira cena. **Sempre a última linha.** |

Cores (`Color`): `White`, `Gray`, `Red`, `Green`, `Blue`, `Gold`, `Purple`, `Orange`.

## Regras

1. **Não use `Console`** (`ReadLine`/`WriteLine`): é um jogo de janela. Use `game.Ask` e `game.Write`.
2. `Title`, `Button`, `Bar`, `Ask` e `Image` só funcionam **dentro de uma cena**. `Scene` nunca fica dentro de outra cena.
3. Não faça loops de jogo (`while (true)`) nem `Thread.Sleep`. O jogo anda pelos cliques.
4. Para condições que mudam de tela, use `GoTo` dentro da cena: `if (health <= 0) game.GoTo("GameOver");`.
5. Números aleatórios: `Random.Shared.Next(min, max + 1)`.
6. **Nomes de código em inglês** (`health`, `gold`, `enemyHealth`). **Textos do jogo e comentários em português.**
7. Prefira código simples de iniciante: variáveis, `if`, `switch`, listas, métodos pequenos. Classes só quando ajudarem de verdade (ex.: `class Enemy`). Evite LINQ avançado, genéricos, eventos e async.
8. Comente o que não for óbvio, em português e com frases curtas.
9. Imagens vão na pasta `Assets/` (png ou jpg), já copiada para o jogo pelo `.csproj`.

## Telas desenhadas (aba Tela)

Uma cena pode ter a tela **desenhada**: o arquivo `Screens/<Cena>.json`, com o mesmo nome da cena. O CSharp Lab abre esse arquivo na aba **Tela**, onde as peças são arrastadas, e a pessoa vê o resultado na hora. **Você pode criar e mudar telas escrevendo esse arquivo.**

- Quando `Screens/Fight.json` existe, `game.Scene("Fight", ...)` usa essa tela. Sem o arquivo, a cena monta a tela sozinha (`Write`, `Button`…).
- Numa cena desenhada, o código **não cria peças**: ele pega as peças pelo nome com `game.Find("Nome")` e muda as propriedades com `=`. `Title`, `Button`, `Bar`, `Ask` e `Image` dão erro ali.
- Ao **entrar** na cena, as peças começam como no arquivo. Depois guardam o que o código mudou, até o jogador sair da cena. A cena roda de novo depois de cada clique, então ligue os cliques (`OnClick`) dentro dela.
- `game.Write` numa cena desenhada vai para a peça `Messages`. Sem ela, aparece como aviso embaixo da tela.

```csharp
game.Scene("Fight", () =>
{
    game.Find("Story").Text = $"{playerName} encontra um goblin.";
    game.Find("PlayerHealth").Value = health;
    game.Find("Potion").Visible = potions > 0;

    game.Find("Attack").OnClick(() =>
    {
        enemyHealth -= Random.Shared.Next(5, 11);
        game.Write("Você acertou!");
        if (enemyHealth <= 0) game.GoTo("Victory");
    });
});
```

| Propriedade (`game.Find("X").`) | Peças que têm |
|---|---|
| `Text` | Text, Button, Bar (rótulo), Input (pergunta) |
| `Value`, `Max` | Bar |
| `Color` | Text, Button, Bar, Box |
| `Image` | Image |
| `Visible`, `X`, `Y`, `Width`, `Height` | todas |
| `OnClick(() => { })` | Button, Image |
| `OnAnswer(answer => { })` | Input |

### O arquivo `Screens/<Cena>.json`

Palco de **960 × 540**: `x` vai da esquerda para a direita, `y` de cima para baixo. As peças são desenhadas na ordem da lista (a última fica por cima). Uma peça por linha.

```json
{
  "format": 1,
  "background": "forest.png",
  "pieces": [
    { "type": "Box", "name": "Panel", "x": 620, "y": 24, "width": 316, "height": 130 },
    { "type": "Text", "name": "Title", "x": 40, "y": 30, "width": 560, "height": 50, "text": "A Torre do Goblin", "size": 32, "bold": true },
    { "type": "Text", "name": "Story", "x": 40, "y": 90, "width": 560, "height": 70, "text": "Um goblin aparece.", "size": 19 },
    { "type": "Bar", "name": "PlayerHealth", "x": 640, "y": 40, "width": 276, "height": 44, "text": "Vida", "value": 100, "max": 100, "color": "Green" },
    { "type": "Messages", "name": "Log", "x": 40, "y": 180, "width": 560, "height": 150 },
    { "type": "Button", "name": "Attack", "x": 40, "y": 460, "width": 170, "height": 52, "text": "Atacar" },
    { "type": "Button", "name": "Potion", "x": 224, "y": 460, "width": 200, "height": 52, "text": "Beber poção", "color": "Green" }
  ]
}
```

| Chave | Valor |
|---|---|
| `type` | `Text`, `Button`, `Bar`, `Image`, `Box`, `Input`, `Messages` |
| `name` | Nome único, em inglês, só letras sem acento, números e `_` (é o nome do `game.Find`) |
| `x`, `y`, `width`, `height` | Posição e tamanho (números) |
| `text` | Texto da peça, em português |
| `size` | Tamanho da letra (Text: 20; Button: 17) |
| `bold` / `align` | Só Text: `true` / `Left`, `Center`, `Right` |
| `color` | Uma das cores acima |
| `value`, `max` | Só Bar |
| `image` | Só Image: arquivo da pasta `Assets/` |
| `visible` | `false` para começar escondida |
| `background` (no topo) | Imagem de fundo da pasta `Assets/` |

Dicas para telas bonitas: deixe 40 de margem nas bordas, alinhe as peças pelas mesmas linhas (mesmo `x` ou `y`), use tamanhos parecidos para botões da mesma linha (altura 52) e uma `Box` atrás de grupos de barras.

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
    game.Write($"{playerName}, o goblin rosna para você.");
    game.Bar(playerName, health, 100, Color.Green);
    game.Bar("Goblin", enemyHealth, 30, Color.Red);

    game.Button("Atacar", () =>
    {
        int damage = Random.Shared.Next(5, 11);
        enemyHealth -= damage;
        game.Write($"Você causou {damage} de dano!");

        if (enemyHealth > 0)
        {
            int hit = Random.Shared.Next(3, 9);
            health -= hit;
            game.Write($"O goblin revida: -{hit} de vida.", Color.Red);
        }
    });

    if (potions > 0)
    {
        game.Button($"Beber poção ({potions})", () =>
        {
            potions--;
            health = Math.Min(100, health + 25);
            game.Write("Você se sente melhor.", Color.Green);
        });
    }
});

game.Scene("Victory", () =>
{
    game.Title("Vitória!");
    game.Write($"{playerName} venceu o goblin.", Color.Gold);
});

game.Scene("GameOver", () =>
{
    game.Title("Fim de jogo");
    game.Write("Você caiu...", Color.Red);
    game.Button("Tentar de novo", () =>
    {
        health = 100;
        potions = 2;
        enemyHealth = 30;
        game.GoTo("Fight");
    });
});

game.Start("Name");
```

## Erros

Os erros do motor (`GameException`) já explicam em português o que fazer, por exemplo uma cena com nome errado ou um botão fora de uma cena. O CSharp Lab mostra a linha do erro no painel Problemas.
