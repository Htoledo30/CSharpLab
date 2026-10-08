# Guia deste jogo (para IAs e para quem programa)

> Este guia é atualizado pelo CSharp Lab junto com o motor do jogo. Anotações suas vão em outro arquivo.

Este é um **jogo com botões** feito no CSharp Lab: um jogo de texto em C#, numa janela com textos, barras e botões. Quem cria é iniciante em programação e está aprendendo C#. Escreva código que **ele consiga ler e mudar**, e deixe a lógica no código (não esconda o C# dele).

## Como funciona

O jogo tem duas partes:

- **A tela** de cada cena: o arquivo `Screens/<Cena>.json`, que o CSharp Lab abre na aba **Tela** para arrastar as peças. Diz onde fica cada coisa, tamanho, cor e texto inicial.
- **O código**, em `Program.cs` (top-level statements): o que muda na tela e o que cada botão faz. O motor é `lib/CSharpLab.Game.dll`, e o `using CSharpLab.GameEngine;` já vem pelo `.csproj`.

Mais:

- O jogo é um conjunto de **cenas**. `game.Scene("Fight", ...)` liga o código à tela `Screens/Fight.json`.
- **Depois de cada clique, o código da cena atual roda de novo sozinho.** Por isso basta mudar as variáveis no botão: a cena põe os valores novos na tela.
- O estado do jogo (vida, ouro, nome, inventário…) fica em **variáveis declaradas no topo**, antes das cenas.
- Ao **entrar** numa cena, as peças começam como no arquivo. Depois guardam o que o código mudou, até o jogador sair da cena.
- `game.Write` vai para a peça `Messages` da tela (sem ela, aparece como aviso por cima da tela) e some no clique seguinte.
- Rodar: F5 no CSharp Lab (ou `dotnet run`).

**Padrão para cena nova:** crie a tela `Screens/<Cena>.json` e a `game.Scene` com o mesmo nome, que pega as peças com `game.Find`.

## O código

```csharp
var game = new Game("A Torre");

string playerName = "";
int health = 100;
int potions = 1;
int enemyHealth = 30;

game.Scene("Name", () =>
{
    game.Find("NameField").OnAnswer(answer =>
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

    game.Find("Story").Text = $"{playerName} encontra um goblin.";
    game.Find("PlayerHealth").Value = health;
    game.Find("EnemyHealth").Value = enemyHealth;
    game.Find("Potion").Visible = potions > 0;

    game.Find("Attack").OnClick(() =>
    {
        int damage = Random.Shared.Next(5, 11);
        enemyHealth -= damage;
        game.Write($"Você causou {damage} de dano!");
    });

    game.Find("Potion").OnClick(() =>
    {
        potions--;
        health = Math.Min(100, health + 25);
        game.Write("Você se sente melhor.", Color.Green);
    });
});

// (cenas Victory e GameOver, cada uma com a sua tela)

game.Start("Name");
```

| Código | O que faz |
|---|---|
| `var game = new Game("Título");` | Cria o jogo. |
| `game.Scene("Name", () => { ... });` | O código de uma cena. Nomes em inglês: `"Start"`, `"Forest"`, `"Fight"` (iguais ao arquivo da tela). |
| `game.Find("Nome")` | Pega uma peça da tela da cena pelo nome. |
| `game.Write("texto");` / `game.Write("texto", Color.Red);` | Mensagem para o jogador, com cor opcional. |
| `game.GoTo("Name");` | Troca de cena (em botões e dentro de cenas). |
| `game.CurrentScene` | Nome da cena atual. |
| `game.Start("Start");` | Abre a janela na primeira cena. **Sempre a última linha.** |

| Propriedade (`game.Find("X").`) | Peças que têm |
|---|---|
| `Text` | Text, Button, Bar (rótulo), Input (pergunta) |
| `Value`, `Max` | Bar |
| `Color` | Text, Button, Bar, Box |
| `Image` | Image |
| `Visible`, `X`, `Y`, `Width`, `Height` | todas |
| `OnClick(() => { })` | Button, Image |
| `OnAnswer(answer => { })` | Input (o texto já vem sem espaços nas pontas e nunca vazio) |

Cores (`Color`): `White`, `Gray`, `Red`, `Green`, `Blue`, `Gold`, `Purple`, `Orange`.

## A tela: `Screens/<Cena>.json`

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
| `visible` | `false` para começar escondida (o código mostra com `.Visible = true`) |
| `background` (no topo) | Imagem de fundo da pasta `Assets/` |

Dicas para telas bonitas: deixe 40 de margem nas bordas, alinhe as peças pelas mesmas linhas (mesmo `x` ou `y`), use tamanhos parecidos para botões da mesma linha (altura 52) e uma `Box` atrás de grupos de barras.

## Regras

1. **Não use `Console`** (`ReadLine`/`WriteLine`): é um jogo de janela. Use uma peça `Input` e `game.Write`.
2. Todo `game.Find("Nome")` precisa de uma peça com esse nome na tela da cena (o editor avisa se não tiver). Ao criar uma peça no código, crie também no `.json`.
3. Ligue os cliques (`OnClick`, `OnAnswer`) **dentro da cena** da tela. `Scene` nunca fica dentro de outra cena.
4. Não faça loops de jogo (`while (true)`) nem `Thread.Sleep`. O jogo anda pelos cliques.
5. Para condições que mudam de tela, use `GoTo` dentro da cena: `if (health <= 0) game.GoTo("GameOver");`.
6. Números aleatórios: `Random.Shared.Next(min, max + 1)`.
7. **Nomes de código em inglês** (`health`, `gold`, `enemyHealth`). **Textos do jogo e comentários em português.**
8. Prefira código simples de iniciante: variáveis, `if`, `switch`, listas, métodos pequenos. Classes só quando ajudarem de verdade (ex.: `class Enemy`). Evite LINQ avançado, genéricos, eventos e async.
9. Comente o que não for óbvio, em português e com frases curtas.
10. Imagens vão na pasta `Assets/` (png ou jpg), já copiada para o jogo pelo `.csproj`.

## Cenas só com código

Uma cena **sem** arquivo em `Screens/` monta a própria tela pelo código. Serve para treinar C# ou para telas bem simples. Numa cena **com** tela desenhada, estes comandos dão erro (as peças vêm da tela).

| Código | O que faz |
|---|---|
| `game.Title("texto");` | Título grande da cena. |
| `game.Write("texto");` | Parágrafo de texto (dentro de um botão, vira aviso destacado depois do clique). |
| `game.Button("Texto", () => { ... });` | Botão. O código roda no clique. As teclas 1 a 9 apertam os botões. |
| `game.Bar("Vida", value, max, Color.Green);` | Barra (vida, mana…), mostrada à direita. |
| `game.Ask("Pergunta?", answer => { ... });` | Campo para o jogador escrever. |
| `game.Image("arquivo.png");` | Imagem da pasta `Assets/`. |

```csharp
game.Scene("Village", () =>
{
    game.Title("Vila");
    game.Write($"Ouro: {gold}", Color.Gold);
    game.Button("Procurar moedas", () => gold += 5);
    game.Button("Ir para a floresta", () => game.GoTo("Forest"));
});
```

## Erros

Os erros do motor (`GameException`) já explicam em português o que fazer, por exemplo uma cena com nome errado ou uma peça que não existe na tela. O CSharp Lab mostra a linha do erro no painel Problemas.
