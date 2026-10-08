# CSharp Lab

Editor de C# leve para Windows: arquivos à esquerda, código no centro, terminal e problemas embaixo.
Autocomplete de verdade (Roslyn), erros curtos em português, snippets, formatação e um terminal
interativo para programas e jogos de texto.

## O que precisa

- Windows 10 ou 11, 64 bits.
- **SDK .NET 10** para *executar* programas: <https://dotnet.microsoft.com/pt-br/download/dotnet/10.0>
  (escolha "SDK", não só o "Runtime"). O editor abre e funciona sem ele; só o botão Executar depende do SDK.

## Instalar

1. Baixe `CSharpLab-win-x64.zip` da [última versão](https://github.com/Htoledo30/CSharpLab/releases/latest) e extraia a pasta inteira.
2. Dê dois cliques em **Instalar.cmd**.
   Ele copia o programa para `%LocalAppData%\Programs\CSharpLab` e cria atalhos na
   Área de Trabalho e no Menu Iniciar. Não precisa de administrador.

**Sem instalar (pen drive):** abra `app\CSharpLab.exe` direto da pasta extraída.

**Desinstalar:** Configurações → Aplicativos instalados → CSharp Lab, ou rode `Desinstalar.cmd`.
Seus projetos nunca são apagados.

## Atualizações

Nas cópias publicadas com um repositório de atualizações configurado, o editor procura uma versão
nova ao abrir e baixa o pacote em segundo plano. Quando estiver pronto, aparece um aviso com
**Reiniciar agora** ou **Ao fechar**. Reiniciar pergunta antes sobre alterações não salvas.
Ao fechar normalmente, uma atualização já preparada é instalada automaticamente.

Em **Arquivo → Procurar atualizações ao abrir**, você pode ligar ou desligar a busca automática.
**Arquivo → Verificar atualizações** faz uma busca manual. Sem internet, o editor continua funcionando.
Uma cópia antiga que ainda não tem esse recurso precisa receber esta versão manualmente uma vez.

## Primeiros passos

| Quero… | Como |
| --- | --- |
| Escrever algo rápido | Ao abrir, já existe um rascunho `Sem título.cs`. É só digitar. |
| Criar um projeto | **Arquivo → Novo projeto…**: tipo (**Programa console** ou **Jogo com botões**), nome e pasta. Abre o `Program.cs`. |
| Abrir o que já tenho | **Arquivo → Abrir pasta…** e escolha a pasta do projeto. (Escolher um `.csproj` em **Abrir arquivo…** também abre o projeto.) |
| Salvar | `Ctrl+S` (salvar tudo: `Ctrl+Shift+S`). Abas com `●` têm alterações não salvas. |
| Executar | `F5` ou o botão **Executar** (ele mostra o nome do projeto que vai rodar). Os arquivos do projeto são salvos e compilados antes. |
| Digitar no programa | Clique no **Terminal** e digite. `ReadLine`, `ReadKey`, setas e cores funcionam. |
| Parar | `Shift+F5` ou o botão **Parar** (funciona até em loop infinito). |
| Ver erros | Aba **Problemas**: "Program.cs · Linha 12 — Faltou ";"." Clique para ir até a linha; **Saiba mais** abre a explicação da Microsoft em português. |
| Programa parou com erro | O terminal explica em português o que aconteceu e em que linha (ex.: "abc" não é um número válido), com uma dica. A linha fica sublinhada. |
| Entender o código | Pare o mouse sobre um nome: aparece o tipo e a explicação. |
| Consertar um erro | Com o cursor no sublinhado, `Ctrl+.` mostra correções (ex.: adicionar o `using` que falta). |
| Fazer jogos no console | `Ctrl+Shift+J` expande o terminal para quase a tela toda. |
| Organizar o código | `Ctrl+Shift+F` formata o arquivo (desfaz com `Ctrl+Z`). |
| Estudar com exemplos | **Arquivo → Exemplos para estudar**: adivinhe o número, calculadora, jogo da velha, batalha RPG, cobrinha e três RPGs de janela: com telas desenhadas, só com código e o completo **A Coroa Perdida** (classes, loja, níveis, escolhas, enigma e chefe final). Código em inglês, comentários em português. Ficam em `Documentos\CSharp Lab\Exemplos` e suas mudanças nunca são apagadas. |
| Fazer o primeiro jogo | **Ajuda → Seu primeiro jogo em 5 minutos**: cinco passos que ficam numa janelinha ao lado enquanto você faz (criar, jogar, mudar a tela, um botão que faz algo, uma cena nova). |

Executar um rascunho pede uma única vez o nome e a pasta de um projeto novo; o código vira o `Program.cs` dele.

## Jogos com botões

Um **jogo com botões** abre numa janela própria, com textos, barras de vida e botões, em vez do terminal.
Crie em **Arquivo → Novo projeto… → Jogo com botões**. Ele já nasce com duas cenas prontas para mexer
(ou abra o exemplo **RPG com botões**).

O jogo tem duas partes, cada uma no seu lugar:

- **A tela** (aba Tela): onde fica cada coisa, tamanho, cor e texto. Você arrasta com o mouse.
- **O código** (`Program.cs`): o que muda na tela e o que cada botão faz. Você escreve em C#.

### A tela

Cada cena tem a sua tela em `Screens/<Cena>.json`. Abra pelo botão **Cenas** (ao lado do **Executar**) ou pelo explorador.
Para uma cena nova: **Cenas → Nova tela do jogo…**.

- **Peças** à esquerda: Texto, Botão, Barra, Imagem, Caixa, Campo de escrita, Mensagens e Lista. Clique para pôr no meio ou arraste até o lugar.
- **Palco** no meio: arraste para mover, puxe os quadradinhos para mudar o tamanho (Shift mantém a proporção).
  As peças grudam nas bordas e nos centros das outras, com linhas-guia (Alt solta livre).
  Setas movem 1 (Shift: 10), `Ctrl+D` duplica, `Del` apaga, `Ctrl+Z` desfaz, Tab passa para a próxima peça, botão direito tem mais opções.
- **Várias peças**: Shift+clique ou arraste um retângulo no fundo do palco (`Ctrl+A` pega todas). Elas andam, duplicam e apagam juntas.
- **Copiar e colar entre telas**: `Ctrl+C` numa tela e `Ctrl+V` em outra cola no mesmo lugar e com os mesmos nomes
  (copie o painel de status da Vila e cole na Floresta: o mesmo código serve às duas).
- **Propriedades** à direita: nome, texto, letra, cor, imagem, posição e tamanho, e os estilos de cada peça:

| Peça | Estilos |
| --- | --- |
| Caixa | Tom da cor (normal, escuro, claro), preenchimento de 0 a 100%, borda, cantos redondos, retos ou círculo (retratos redondos). |
| Texto | 4 fontes do Windows (Normal, Fantasia, Livro, À mão), negrito, itálico, sombra nas letras e rolagem para texto comprido. |
| Botão | Cheio, só contorno ou só texto, e **Ativo no começo** (desligado: apagado e sem clique). |
| Mensagens | Guarda as mensagens da cena: as do último clique destacadas, as antigas apagadinhas. |
| Lista | Um cartão modelo que se repete para cada item (loja, inventário). Veja abaixo. |

- Mais em **Propriedades**:
  Num botão, **Ao clicar** mostra se ele já faz algo e em que linha do código (clique para ir até lá). Se ainda não faz nada,
  **Escrever o que ele faz** cria o `game.Find("Nome").OnClick(() => { });` na cena certa e deixa o cursor entre as chaves:
  o que o botão faz, você escreve. O campo de escrita tem o mesmo com **Ao responder** (`OnAnswer`).
- **Texto que não cabe**: um Texto cujo fim ficaria cortado no jogo (ou um botão cujo texto terminaria em "…") ganha um
  contorno laranja com um **!** no palco. Nas Propriedades aparece quanto falta e o conserto: **Ajustar a altura ao texto**
  (no botão, **Aumentar a largura**), ou ligar a **Rolagem** para textos compridos. O aviso some sozinho quando passa a caber,
  até enquanto você arrasta o tamanho.

### O código

Cada `game.Scene` liga o código à tela de mesmo nome. A cena pega as peças pelo nome com `game.Find` e muda
as propriedades com `=`:

```csharp
var game = new Game("A Torre");
int gold = 0;

game.Scene("Start", () =>
{
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

game.Start("Start");
```

| Comando | Em português | O que faz |
| --- | --- | --- |
| `Scene` | cena | O código de uma tela. Roda de novo depois de cada clique, com os valores atuais das variáveis. |
| `OnEnter` | ao entrar | Dentro da cena, roda **uma vez** cada vez que o jogador chega nela (não nos cliques): sortear o inimigo, a recompensa da chegada. |
| `Find("Nome")` | achar | Pega uma peça da tela. Depois: `.Text`, `.Value`, `.Max`, `.Visible`, `.Enabled`, `.Color`, `.Image` e os estilos (`.Shade`, `.Font`, `.Style`…). |
| `OnClick` | ao clicar | O código entre as chaves roda quando o jogador clica no botão. |
| `OnAnswer` | ao responder | O mesmo, para o campo de escrita: `answer` é o que o jogador escreveu. |
| `Write` | escrever | Uma mensagem para o jogador (aparece na peça Mensagens). |
| `GoTo` | ir para | Troca de cena. |
| `Enabled` | ativo | `game.Find("Tower").Enabled = hasKey;` deixa o botão apagado e sem clique enquanto for `false`. |
| `Show` | mostrar | Enche uma Lista: um cartão para cada item (veja abaixo). |
| `Wait` | esperar | Dentro de um clique, uma pausa curta: `game.Wait(0.6);` mostra o seu golpe e o inimigo responde logo depois. |
| `Shake` / `Flash` | tremer / piscar | `game.Find("EnemyIcon").Shake();` treme a peça; `.Flash();` faz piscar. Bom ao levar dano. |
| `Background` | fundo | A imagem de fundo da tela (pasta `Assets`), para a mesma tela servir a lugares diferentes: `game.Background = "tower.png";`. |
| `Start` | começar | Abre a janela na primeira cena. Sempre na última linha. |

Ao trocar de cena, a tela escurece e clareia sozinha.

**Cada coisa no seu lugar.** Como a cena roda de novo depois de cada clique, o código dela tem três lugares:

```csharp
game.Scene("Fight", () =>
{
    game.OnEnter(() =>                       // 1. ao chegar: prepara a visita (uma vez)
    {
        enemyHealth = 30;
        game.Write("Um goblin aparece!");
    });

    game.Find("EnemyHealth").Value = enemyHealth;   // 2. a cena: só mostra o que as variáveis têm

    game.Find("Attack").OnClick(() =>        // 3. o clique: a escolha do jogador
    {
        enemyHealth -= 7;
    });
});
```

Um `gold += 10` ou um sorteio com `Random` solto na cena (fora do `OnEnter` e do `OnClick`) se repetiria a cada clique;
o editor sublinha e explica. Voltar para a cena roda o `OnEnter` de novo: uma recompensa que só pode acontecer uma vez
na aventura precisa de uma variável (`if (!gotKey) { ... gotKey = true; }`).

### Lista: um cartão para cada item

Ponha uma **Lista** na tela e desenhe dentro do primeiro cartão (o tracejado) as peças de um item: ícone, nome, preço,
botão. Na aba Tela os outros cartões aparecem apagadinhos, como prévia. No código, entregue a lista e diga o que vai
em cada cartão:

```csharp
game.Find("Weapons").Show(weapons, (card, weapon) =>
{
    card.Find("Name").Text = weapon.Name;
    card.Find("Price").Text = $"💰 {weapon.Price}";
    card.Find("Buy").OnClick(() => Buy(weapon));
});
```

Com 3 armas aparecem 3 cartões; com 7, aparecem 7, com rolagem se não couber. Nas propriedades da Lista: tamanho do
cartão, espaço entre eles e o texto de quando ela está vazia. A loja e a escolha de classe de **A Coroa Perdida** usam Listas.

O autocomplete e o mouse em cima de cada comando mostram a tradução e um exemplo. Ao digitar `game.Find("`, o editor
sugere os nomes das peças daquela tela; um nome que não existe ganha uma dica antes de rodar, e renomear uma peça na Tela
também troca o nome no código.

Para ir e voltar: o botão **Cenas** troca entre a tela e o código da cena em que você está; no alto da aba Tela,
**Código da cena** leva até o `game.Scene`; `Ctrl`+clique ou `F12` no nome da cena abre a tela dela, e no nome de
um `game.GoTo("Shop")` vai até o código da cena.

### Cenas só com código

Uma cena sem tela desenhada também funciona: o próprio código monta a tela com `game.Title`, `game.Write`,
`game.Button`, `game.Bar`, `game.Ask` e `game.Image` (exemplo: **RPG só com código**; snippets `scene` e `button`).
É um bom jeito de treinar C#. Numa cena com tela desenhada esses comandos não funcionam (as peças vêm da tela),
e o editor avisa antes de rodar.

### Para IAs

A tela é um arquivo de texto (`Screens/Fight.json`, botão **Arquivo** no alto da aba), então uma IA também consegue
criar e mudar telas. Cada jogo leva um `AGENTS.md`/`CLAUDE.md` que ensina o motor para o Claude e o Codex, e uma
cópia do motor em `lib\` (funciona mesmo se a pasta mudar de lugar).

## Atalhos

| Ação | Atalho |
| --- | --- |
| Novo arquivo / Abrir arquivo | `Ctrl+N` / `Ctrl+O` |
| Salvar / Salvar tudo | `Ctrl+S` / `Ctrl+Shift+S` |
| Fechar aba | `Ctrl+W` |
| Executar / Parar | `F5` / `Shift+F5` |
| Formatar documento | `Ctrl+Shift+F` ou `Alt+Shift+F` |
| Mostrar sugestões | `Ctrl+Space` |
| Buscar / Substituir | `Ctrl+F` / `Ctrl+H` |
| Desfazer / Refazer | `Ctrl+Z` / `Ctrl+Y` |
| Mostrar explorador / painel inferior | `Ctrl+B` / `Ctrl+J` |
| Expandir o terminal | `Ctrl+Shift+J` |
| Correções rápidas | `Ctrl+.` |
| Ir para definição | `F12` ou `Ctrl` + clique |
| Renomear em todo o projeto | `F2` |
| Comentar / descomentar linhas | `Ctrl+/` |
| Duplicar / apagar linha | `Ctrl+D` / `Ctrl+Shift+K` |
| Mover linha | `Alt+↑` / `Alt+↓` |
| Tamanho da fonte | `Ctrl` + roda do mouse |

Sugestões: setas para escolher, **Tab** ou **Enter** para aceitar, **Esc** para fechar.
Espaço e pontuação nunca aceitam uma sugestão sozinhos.

Tudo isso também está no menu **Editar** e no botão direito do mouse, para não precisar decorar.

**Dicas** (sublinhado azul) apontam armadilhas que o compilador aceita calado: imprimir uma lista
direto (`Console.WriteLine(list)` mostra o nome do tipo), divisão entre inteiros que perde as casas
decimais (`double average = sum / count;`), `int.Parse(Console.ReadLine())` sem conferir, comparação
com o que foi digitado que diferencia maiúsculas e `while (true)` sem saída. Não impedem a execução.

**Mouse em cima** de uma palavra-chave (`static`, `foreach`…) ou de um método comum (`ReadLine`, `Random.Next`…)
mostra uma explicação em português com exemplo. Sugestões de método já entram com `()`.

## Snippets

Digite o atalho e aperte **Tab**. Depois, **Tab** passa para o próximo campo e **Enter** termina.

`cw` `read` `if` `ifelse` `for` `foreach` `while` `switch` `method` `methodr` `class` `array` `list`

Em jogos com botões: `scene` `button`

## Onde ficam as coisas

- Seu código: arquivos `.cs` e `.csproj` normais, nas pastas que você escolher. Abrem em qualquer outra ferramenta e compilam com `dotnet build`.
- Preferências, recentes e recuperação de rascunhos: `%LocalAppData%\CSharpLab`.
- Se o computador desligar com alterações não salvas, o texto é recuperado na próxima abertura.
- Uma instância por pasta de preferências mantém a recuperação protegida; a cópia só é apagada ao salvar ou descartar.

Detalhes técnicos e como compilar o próprio editor: [docs/DESENVOLVIMENTO.md](docs/DESENVOLVIMENTO.md).
