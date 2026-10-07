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
| Estudar com exemplos | **Arquivo → Exemplos para estudar**: adivinhe o número, calculadora, jogo da velha, batalha RPG, cobrinha e RPG com botões. Código em inglês, comentários em português. Ficam em `Documentos\CSharp Lab\Exemplos` e suas mudanças nunca são apagadas. |

Executar um rascunho pede uma única vez o nome e a pasta de um projeto novo; o código vira o `Program.cs` dele.

## Jogos com botões

Um **jogo com botões** abre numa janela própria, com título, textos, barras de vida e botões, em vez do terminal.
Crie em **Arquivo → Novo projeto… → Jogo com botões** (ou abra o exemplo **RPG com botões**). Cada coisa na tela é uma linha de C#:

```csharp
var game = new Game("A Torre");
int gold = 0;

game.Scene("Start", () =>
{
    game.Title("Vila");
    game.Say($"Ouro: {gold}", GameColor.Gold);
    game.Button("Procurar moedas", () => gold += 5);
    game.Button("Ir para a floresta", () => game.GoTo("Forest"));
});

game.Scene("Forest", () => game.Say("Árvores por todo lado."));

game.Run("Start");
```

| Comando | Em português | O que faz |
| --- | --- | --- |
| `Scene` | cena | Uma tela do jogo. É desenhada de novo depois de cada clique, com os valores atuais das variáveis. |
| `Title` | título | Texto grande no alto. |
| `Say` | dizer | Um texto. Dentro de um botão, aparece destacado depois do clique. |
| `Button` | botão | O código entre as chaves roda no clique. As teclas 1 a 9 também apertam os botões. |
| `Bar` | barra | Vida, mana, energia… |
| `Ask` | perguntar | Campo para o jogador escrever (ex.: o nome). |
| `Image` | imagem | Uma imagem da pasta `Assets` do projeto. |
| `GoTo` | ir para | Troca de cena. |
| `Run` | começar | Abre a janela. Sempre na última linha. |

O autocomplete e o mouse em cima de cada comando mostram a tradução e um exemplo. Os snippets `scene` e `button`
escrevem a estrutura com as chaves. O jogo leva uma cópia do motor em `lib\` (funciona mesmo se a pasta mudar de lugar)
e um `AGENTS.md`/`CLAUDE.md` que ensina o motor para IAs como o Claude e o Codex.

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
