# CSharp Lab — especificação completa da primeira versão

> Documento para Claudy implementar um aplicativo desktop local de C# para Henrique. Nome provisório: CSharp Lab. Este documento define o produto inicial inteiro; não é uma lista de possibilidades nem um roadmap.

## 1. Quem vai usar e o que ele quer

Henrique é novato em C#, mas já pratica variáveis, condições, loops, funções, arrays e pequenos jogos de combate no terminal. Ele quer programar de verdade e ter acesso aos próprios arquivos e pastas. O aplicativo deve reduzir o trabalho para escrever código, organizar arquivos, executar e entender erros.

Pense como um iniciante usando a ferramenta: ele precisa descobrir facilmente onde escrever, como salvar, como executar, onde digitar uma resposta do programa e onde corrigir um erro. Ele não precisa receber uma aula a cada clique. Trate-o como alguém capaz que ainda está aprendendo a sintaxe.

**Objetivo:** abrir o aplicativo, encontrar um editor pronto, escrever C#, executar e continuar trabalhando sem configurar uma IDE complexa.

**Tom da interface:** português brasileiro, curto, claro e respeitoso. Nomes e sintaxe de C# permanecem originais. Não infantilizar, não inserir tutoriais permanentes, não usar mensagens longas quando uma frase resolve.

Princípio de seleção de recursos: um recurso entra se economiza tempo, reduz confusão ou ajuda a escrever C# e cabe numa interface simples. Tudo especificado aqui deve estar funcional na entrega.

## 2. Escopo fechado

A primeira versão inclui:

- Aplicativo desktop local para Windows 10/11, com prioridade para Windows 11 x64.
- Editor de C# com abas, destaque de sintaxe, números de linha e edição confiável.
- Criar, abrir, salvar e salvar como arquivos; criar e abrir projetos console; abrir pastas.
- Explorador com criação de arquivos e pastas, renomeação, exclusão e atualização.
- Autocomplete contextual real, snippets essenciais e formatação automática sob comando.
- Análise de erros enquanto digita, executada em segundo plano.
- Erros objetivos em português e navegação até a localização indicada pelo compilador.
- Compilar e executar C# real com o SDK .NET.
- Terminal integrado interativo para programas e jogos de texto.
- Interromper a execução de um programa.
- Projetos/pastas recentes, recuperação de rascunhos e persistência mínima de preferências.
- Pacote Windows executável e instruções curtas de instalação e uso.

**Limite do trabalho:** implemente somente o produto descrito aqui. Não acrescente funcionalidades, telas ou menus reservados para depois. Não inclua uma seção de evoluções futuras na entrega. Não substitua recursos exigidos por mockups, botões sem ação ou resultados simulados.

## 3. Decisão técnica

Use **C# + .NET 10 + WPF**, com Roslyn para os serviços de linguagem e o SDK .NET para compilar/executar. Para o editor, use **AvalonEdit**, validando a compatibilidade da versão escolhida. A integração de autocomplete, diagnósticos e formatação deve ser implementada: adicionar AvalonEdit sozinho não fornece uma IDE C# completa.

- Aplicativo: `net10.0-windows`, WPF habilitado.
- Novos projetos do usuário: `net10.0`, console, com a versão de C# correspondente ao framework — C# 14.
- Use releases estáveis. Não use versões preview/RC nem `LangVersion=preview` ou `latest`.
- Antes de implementar, confirme nas fontes oficiais as versões estáveis dos pacotes e a compatibilidade com C# 14. Fixe as versões efetivamente utilizadas.
- Não mude a base do documento para outro framework sem uma razão concreta de incompatibilidade. Registre decisões técnicas relevantes no README.
- Não implemente o aplicativo em Python, C++ ou Electron. Interop com APIs do Windows pode ser feito em C# quando necessário.
- Use componentes de terminal maduros e compatíveis com WPF/ConPTY, com licença adequada, em vez de improvisar um emulador incompleto. Avalie essa dependência antes de construir a interface inteira.
- Não use uma API de IA para autocomplete ou explicação de erros. As funcionalidades devem ser locais, previsíveis e sem cobrança por uso.

### SDK e primeira abertura

O aplicativo distribuído deve poder abrir mesmo sem o SDK: publique o aplicativo de forma self-contained para Windows x64. O **SDK .NET 10 continua necessário para compilar os programas do usuário**; o runtime que acompanha o editor não substitui o SDK.

Detecte os SDKs instalados, inclusive a influência de `global.json` do projeto. Se faltar um SDK compatível, mantenha a edição disponível e mostre uma mensagem curta: “Instale o SDK .NET 10 para executar C#”, com botão para a página oficial e “Verificar novamente”. Não baixar nem instalar automaticamente.

Em projetos existentes, respeite o framework e as configurações declaradas. Não atualizar o `.csproj` silenciosamente. Se forem incompatíveis com os SDKs disponíveis, explique o impedimento de forma objetiva. O suporte garantido desta versão é para projetos console C# comuns de um único projeto; não tente executar automaticamente qualquer tipo de aplicação encontrado numa pasta.

## 4. Interface e hierarquia visual

O editor é a área principal. Não colocar dashboard, cards, mascote, banners, tutorial ou painel de explicação fixo competindo com o código.

### Organização da janela

| Área | Conteúdo | Comportamento |
| --- | --- | --- |
| Barra superior | Arquivo, Editar, Projeto; Executar/Parar | Poucos comandos visíveis; ações secundárias nos menus |
| Lateral esquerda | Explorador de arquivos e pastas | Recolhível e redimensionável |
| Centro | Abas e editor | Ocupa o restante do espaço |
| Área inferior | Terminal e Problemas | Um painel com duas abas; recolhível e redimensionável |
| Barra de status discreta | Linha/coluna, estado do arquivo e projeto ativo | Sem excesso de indicadores |

Valores iniciais sugeridos: explorador com cerca de 230 px; painel inferior fechado até necessário e, quando aberto, com cerca de 25% da altura. São padrões ajustáveis por arraste, não proporções rígidas. Persistir os tamanhos e a visibilidade escolhidos.

Tema escuro sóbrio, bom contraste, cor de destaque discreta, fonte monoespaçada de aproximadamente 15 px e fonte de interface legível. Não usar várias cores chamativas fora do código. Ícones precisam de tooltip e nome acessível. Não depender exclusivamente de cor para comunicar erro ou estado.

O painel de erros não abre nem rouba foco a cada pausa na digitação. Após uma tentativa de execução com falha de compilação, abre Problemas. Após compilação bem-sucedida, abre Terminal e permite digitar nele. As abas Terminal/Problemas devem permanecer acessíveis mesmo quando o painel estiver recolhido.

O aplicativo deve continuar utilizável numa janela de aproximadamente 1024 × 768, em escalas de tela de 100%, 125% e 150%, sem botões cortados.

### Primeira abertura e abertura seguinte

Na primeira abertura, abrir um rascunho `Sem título.cs`, editável e focado, com apenas:

```csharp
Console.WriteLine("Olá, mundo!");
```

Henrique pode apagar tudo e começar. Oferecer Novo projeto e Abrir pasta nos comandos normais, sem uma tela obrigatória antes do editor. Se executar um rascunho, solicitar uma única vez o nome/local para criar um projeto console e transferir o conteúdo do rascunho para `Program.cs`. Se cancelar, manter o código intacto.

Ao reabrir, restaurar o último contexto válido e os rascunhos recuperados. Se a pasta não existir, voltar a um rascunho e informar isso sem bloquear o app. Não executar código automaticamente ao abrir ou restaurar uma sessão.

## 5. Arquivos, pastas e projetos

Todos os arquivos são normais do Windows. Código em `.cs`, projeto em `.csproj`. Nada de formato proprietário para guardar o código.

### Novo projeto

Pedir somente nome e pasta de destino, usando seletor nativo de pasta. O tipo é Console. Não perguntar namespace, arquitetura ou configurações avançadas.

Criar uma subpasta com o nome escolhido e um projeto equivalente a `dotnet new console --framework net10.0`. Não sobrescrever uma pasta com arquivos. Validar nome, permissões e colisões antes de criar. Se falhar no meio, preservar qualquer arquivo que já existia e explicar a falha.

Habilitar implicit usings e nullable conforme o template padrão. Abrir `Program.cs` e focar o editor. Não esconder os arquivos do projeto do usuário, mas recolher/omitir no explorador as pastas geradas `bin` e `obj` por padrão.

### Abrir

- Abrir projeto aceita `.csproj` de console compatível.
- Abrir pasta mostra os arquivos sem criar projeto nem alterar a pasta silenciosamente.
- Se houver um projeto console único, identificá-lo como alvo de execução.
- Se houver vários candidatos, pedir uma seleção simples e guardar o escolhido para essa pasta. Nunca escolher aleatoriamente.
- Se a pasta não tiver projeto, permitir edição. Ao executar, oferecer criar um projeto ali, verificando colisões e a existência de código com múltiplos pontos de entrada antes de confirmar a operação.
- Abrir arquivo aceita `.cs`; permitir ver/editar `.csproj` como texto para não exigir outra ferramenta, sem transformá-lo em um editor de configurações complexo.
- Um `.cs` aberto sem projeto pode ser editado e salvo. Para executá-lo, usar o mesmo fluxo explícito de criação de projeto; esta versão adota projetos console como unidade de execução.

### Explorador e abas

Pastas primeiro, ordem alfabética, árvore expansível e menu de contexto. Pequenas ações no cabeçalho e no menu: Novo arquivo, Nova pasta, Renomear, Excluir, Atualizar e Mostrar no Explorador do Windows.

- Novo arquivo sugere `.cs`; evitar duplicar a extensão.
- Novo arquivo dentro de projeto existente começa vazio. Não gerar um segundo programa com top-level statements automaticamente.
- Ações usam a pasta selecionada; se estiver selecionado um arquivo, usam sua pasta pai. Mostrar o destino quando houver ambiguidade.
- Renomear no explorador altera nome/caminho do arquivo, não símbolos do C#.
- Abas exibem nome e marca de alteração não salva. Tooltip mostra o caminho completo; arquivos de mesmo nome devem ser distinguíveis.
- Abrir o mesmo caminho novamente foca a aba existente.
- Ao renomear ou excluir, atualizar as abas e o modelo do projeto.
- Exclusão usa Lixeira e confirmação curta. Não implementar exclusão permanente como padrão.
- Nomes inválidos, caminhos sem permissão, arquivo em uso ou nome já existente produzem mensagens claras, sem travar a janela.

### Salvamento e proteção do trabalho

`Ctrl+S` salva o arquivo ativo; Salvar tudo salva todos os documentos alterados; Salvar como abre diálogo nativo. UTF-8 por padrão para novos arquivos. Preservar codificação e finais de linha de arquivos existentes quando possível, sem conversões silenciosas destrutivas.

Ao fechar aba, trocar de pasta/projeto ou fechar o aplicativo com alterações: Salvar, Descartar ou Cancelar. Cancelar mantém o contexto inteiro. Salvar falhou? Não fechar o documento como se tivesse sido salvo.

Salvar deve evitar truncamento/perda em caso de falha, usando arquivo temporário e substituição adequada. Detectar mudanças externas: recarregar automaticamente somente se não houver mudanças locais; caso contrário oferecer manter a versão do editor ou recarregar, sem sobrescrever silenciosamente.

Manter recuperação local de rascunhos e alterações pendentes em `%LocalAppData%`, com debounce e gravação segura. É recuperação do editor, não salvamento automático sobre os arquivos originais. Limpar a recuperação ao salvar/descartar explicitamente. Recuperar após encerramento inesperado sem perder a associação ao caminho original.

Projetos/pastas recentes: até 10 itens, nome e caminho, sem duplicação. Guardar preferências locais num JSON simples, sem dados de código em serviços externos.

## 6. Editor: recursos obrigatórios

- Destaque de sintaxe C#, incluindo comentários, strings, interpolação e keywords modernas suportadas.
- Números de linha, destaque discreto da linha atual e dos pares de delimitadores.
- Undo/redo confiável, seleção, copiar/colar, recortar, busca no arquivo e substituição simples.
- Indentação ao pressionar Enter; Tab/Shift+Tab indentam e desindentam seleção.
- Fechamento automático de delimitadores/aspas com cuidado para não duplicar um fechamento já existente nem atrapalhar comentários e strings.
- Rolagem suave e preservação de cursor/rolagem ao trocar de aba.
- Sublinhado de erros com tooltip curto; mensagens não empurram nem deslocam as linhas do código.
- Ajuste do tamanho da fonte com Ctrl + roda do mouse, persistido.

### Atalhos

| Ação | Atalho |
| --- | --- |
| Novo arquivo | Ctrl+N |
| Abrir arquivo | Ctrl+O |
| Salvar | Ctrl+S |
| Salvar tudo | Ctrl+Shift+S |
| Fechar aba | Ctrl+W |
| Formatar documento | Ctrl+Shift+F e Alt+Shift+F |
| Mostrar autocomplete | Ctrl+Space |
| Executar | F5 |
| Parar execução | Shift+F5 |
| Buscar / substituir no arquivo | Ctrl+F / Ctrl+H |
| Desfazer / refazer | Ctrl+Z / Ctrl+Y |

F5 aqui significa executar, conforme indicado no menu e tooltip. Tab só aceita sugestão/expande snippet quando isso estiver claramente disponível; fora desse contexto continua sendo indentação normal.

### Autocomplete real

Usar serviços de completion do Roslyn, considerando projeto, referências, arquivos e alterações ainda não salvas. Completar variáveis, métodos, classes e membros existentes. Exemplo: `Console.Wri` oferece `Write` e `WriteLine`; um objeto criado pelo usuário oferece seus próprios membros.

Popup pequeno perto do cursor, filtrado pelo texto, com navegação por setas, Tab para aceitar e Esc para fechar. Não aceitar sugestões ao apertar espaço ou pontuação de maneira surpreendente. Não modificar código automaticamente com base em uma suposição.

Adicionar informação compacta de parâmetros quando o usuário digita uma chamada: assinatura e destaque do parâmetro atual, sem documentação extensa. Esse recurso ajuda a usar os métodos sugeridos sem abrir outra ferramenta.

### Snippets essenciais

| Gatilho | Estrutura |
| --- | --- |
| `cw` | `Console.WriteLine();` |
| `read` | `Console.ReadLine()` |
| `if` | `if (condicao) { }` |
| `ifelse` | `if (condicao) { } else { }` |
| `for` | `for (int i = 0; i < 10; i++) { }` |
| `foreach` | `foreach (var item in itens) { }` |
| `while` | `while (condicao) { }` |
| `switch` | `switch (valor)` com um `case` e `default` |
| `method` | `static void MeuMetodo() { }` |
| `methodr` | `static int MeuMetodo() { return 0; }` |
| `class` | `class MinhaClasse { }` |
| `array` | `int[] valores = { 1, 2, 3 };` |
| `list` | `List<int> valores = new();` |

Expansão gera código formatado; cursor vai ao campo útil e Tab percorre placeholders. Uma expansão é uma operação de undo. Os nomes são exemplos editáveis, não regras impostas. Não expandir dentro de comentários/strings. Oferecer apenas snippets adequados ao contexto, evitando sugerir uma declaração de método no meio de uma expressão.

Não introduzir comentários didáticos automaticamente. Condições e placeholders devem ser claros, mas não inventar variáveis silenciosamente para fazer o snippet compilar. O autocomplete deve distinguir um snippet de um símbolo real.

### Formatação

Usar formatter do Roslyn, não regex para reorganizar C#. Quatro espaços por nível e chaves em nova linha como padrão, respeitando `.editorconfig` quando houver. Formatar por botão/menu e atalhos; não reformatar o documento inteiro a cada tecla.

Não alterar lógica, nomes, valores nem organizar/remover imports como efeito colateral. Preservar o cursor aproximadamente e permitir desfazer toda a formatação em uma operação. Com código incompleto, formatar com segurança o que for possível; não “corrigir” a sintaxe inventando código.

## 7. Erros simples em português

Henrique pediu objetividade. O formato principal é:

**`Program.cs · Linha 12 — Faltou ";".`**

No tooltip de um único arquivo pode ser só “Linha 12 — Faltou \";\".”. Na lista, exibir arquivo, linha e mensagem; guardar coluna e posição para navegar. Não incluir aula, comparativo antes/depois ou solução extensa.

Máximo de uma ou duas frases. A explicação deve descrever o que o diagnóstico realmente permite afirmar, sem inventar causa. O compilador pode apontar onde percebeu o erro, e não onde começou a causa: navegar à localização real e não prometer uma precisão causal que o compilador não fornece.

Exemplos de mensagens e cobertura mínima:

| Código | Mensagem simples, adaptada aos argumentos do diagnóstico |
| --- | --- |
| CS1002 | Faltou ";". |
| CS1026 | Faltou ")". |
| CS1513 | Faltou "}" para fechar um bloco. |
| CS1003 | Era esperado "{token informado pelo compilador}". |
| CS0103 | "{nome}" não foi encontrado neste trecho. |
| CS0117 | "{tipo}" não possui um membro chamado "{membro}". |
| CS1061 | "{tipo}" não possui "{membro}" acessível aqui. Verifique o nome e as referências necessárias. |
| CS0029 | Não é possível atribuir um valor de tipo "{origem}" a "{destino}". |
| CS0266 | A conversão de "{origem}" para "{destino}" precisa ser explícita. |
| CS0165 | "{variável}" foi usada antes de receber um valor. |
| CS0128 | "{nome}" já foi declarado neste bloco. |
| CS0136 | "{nome}" entra em conflito com uma declaração de um bloco relacionado. |
| CS1501 | Nenhuma versão de "{método}" aceita essa quantidade de argumentos. |
| CS1503 | O argumento {posição} precisa ser "{destino}", mas recebeu "{origem}". |
| CS0161 | O método "{nome}" não retorna um valor em todos os caminhos. |
| CS0019 | O operador "{operador}" não pode ser usado entre "{tipo1}" e "{tipo2}". |
| CS8802 | Mais de um arquivo contém instruções fora de uma classe. Mantenha o código inicial em um só arquivo. |

Para tipos conhecidos, mensagens ainda mais claras são válidas: “Não é possível colocar texto em uma variável int.” Não dizer isso para qualquer conversão: `double` para `int` não é texto para número.

Código original e mensagem original ficam disponíveis em um detalhe recolhido, tooltip adicional ou comando “Detalhes”, nunca ocupando toda a lista. Para diagnósticos sem tradução própria, tentar mensagem localizada oficial em pt-BR e preservar a original. Se não houver tradução confiável, exibir o diagnóstico original com código e localização; não fabricar uma explicação.

Evitar extrair parâmetros apenas procurando palavras em mensagens inglesas: usar o diagnóstico e sua informação estruturada/semântica quando disponível, com fallback seguro. Não concluir que um nome está fora de escopo apenas a partir de CS0103; esse diagnóstico também pode significar erro de digitação ou nome nunca declarado.

### Análise e apresentação

Análise em segundo plano após cerca de 350–500 ms sem digitação. Cancelar trabalho obsoleto, associar resultados à versão do documento e nunca mostrar diagnósticos de uma versão anterior como se fossem atuais.

Usar o contexto real do projeto, implicit usings, nullable, referências e arquivos irmãos. Não compilar cada arquivo isoladamente quando ele faz parte de um projeto. Para rascunhos sem projeto, criar contexto console em memória compatível com os padrões adotados.

Separar erros de avisos por ícone/rótulo. Erros aparecem primeiro; avisos ficam num grupo recolhível da mesma aba. Avisos comuns podem receber frases curtas, mas não devem impedir execução quando o projeto não os trata como erro.

Remover erros resolvidos, evitar duplicações entre análise ao digitar e compilação, ordenar por arquivo/linha e permitir clicar para abrir o arquivo e posicionar o cursor. Erros do SDK/projeto sem localização de código aparecem como erro do projeto, sem inventar linha.

## 8. Compilar, executar e parar

Executar sempre usa os arquivos salvos do projeto ativo. Antes, salvar os documentos alterados desse projeto, conforme explicado pelo próprio comando. Se algum arquivo precisar de nome ou falhar ao salvar, resolver isso ou cancelar a execução; nunca executar uma versão antiga silenciosamente.

Fluxo:

1. Verificar SDK, projeto e arquivos.
2. Salvar alterações necessárias.
3. Compilar assincronamente com o SDK, respeitando o projeto.
4. Se falhar, abrir Problemas e não iniciar executável antigo.
5. Se passar, iniciar o programa compilado no terminal integrado, sem compilar novamente.
6. Exibir estado Executando e substituir Executar por Parar.
7. Ao terminar, manter a saída visível e permitir executar novamente.

Compilação e execução são processos separados do editor; o código do usuário nunca roda dentro do processo WPF. Use argumentos estruturados, caminhos absolutos e diretório de trabalho correto. Não construir comandos de shell por concatenação de nomes e caminhos. Testar espaços, acentos e caracteres especiais.

O diretório de trabalho do programa é a pasta do projeto. Assim, arquivos relativos de um jogo são previsíveis. Projetos executáveis existentes devem respeitar seu próprio target; não assumir que o nome da pasta é sempre o nome da DLL.

Um processo de execução por vez. Impedir execuções duplicadas e informar o estado sem bloquear digitação. Parar deve funcionar também durante compilação/restauração, liberar recursos e encerrar a árvore de processos criada pelo aplicativo quando necessário.

Restaurar dependências quando preciso, sem exigir que o usuário execute comandos manualmente. Edição, ajuda e execução de projetos já restaurados devem funcionar offline. Falha de rede durante restore não vira erro de C#: mostrar “Não foi possível restaurar as dependências” e manter o detalhe técnico acessível.

Erros de compilação devem vir de informação estruturada quando possível. Se analisar saída textual do SDK, usar formato/idioma controlado, suportar caminhos complexos e não depender de uma regex frágil como única fonte. O diagnóstico da compilação real é a autoridade sobre poder executar.

Exceções durante execução ficam no terminal. Pode haver uma frase curta “O programa encerrou com erro”, preservando stack trace e código de saída. Não misturar exceções de execução com erros CS de compilação nem ocultar a saída original.

## 9. Terminal integrado de verdade

Esta área não pode ser somente uma caixa de logs com uma textbox separada apresentada como terminal completo. Jogos console precisam de interação por linha e por tecla, incluindo saída sem quebra de linha.

Use ConPTY e um componente/emulador capaz de renderizar a sessão. Encaminhar entrada, saída e redimensionamento corretamente. A escolha precisa ser validada com um protótipo funcional logo no início.

Suporte exigido:

- `Console.WriteLine` e `Console.Write`, mostrando texto imediatamente, inclusive prompts sem nova linha.
- `Console.ReadLine`, com digitação, Backspace e Enter.
- `Console.ReadKey(true)` e `Console.ReadKey(false)`, com comportamento apropriado de eco.
- Acentos portugueses e caracteres Unicode comuns.
- Cores de console e sequências ANSI/VT usuais.
- `Console.Clear`, sem imprimir sequências de escape como texto.
- Navegação/setas em programas que aguardam teclas.
- Copiar texto selecionado e colar entrada; Ctrl+C copia quando houver seleção e sinaliza interrupção quando não houver.
- Rolagem, redimensionamento e histórico com limite de memória.
- Parar pelo botão mesmo quando o programa entrou num loop infinito ou não atende a Ctrl+C.

Ao abrir Terminal sem programa rodando, mostrar a saída anterior ou estado vazio discreto. Nesta versão, Terminal é o console do programa executado; não é necessário iniciar um shell geral para comandos arbitrários.

Ao executar, abrir o painel e transferir foco para a sessão somente depois de o programa iniciar. Durante compilação o editor pode continuar utilizável. Saída nova não deve forçar o usuário ao fim se ele estiver lendo o histórico acima; oferecer retornar ao fim.

Cada execução começa com sessão de tela limpa e identificação discreta do projeto. A conclusão aparece separada da saída do programa. Se ocorrer muito output, limitar buffer e atualizar a tela em lotes, sem congelar a janela.

## 10. Arquitetura e desempenho

Estruture a solução em poucas responsabilidades claras. MVVM no WPF, sem colocar lógica de arquivos, processos e Roslyn no code-behind da janela.

| Responsabilidade | O que controla |
| --- | --- |
| Documentos | Texto, caminho, dirty state, abas, salvamento e recuperação |
| Workspace/projeto | Projeto ativo, arquivos, referências e seleção do SDK |
| Linguagem | Roslyn: completion, assinatura, diagnósticos e formatter |
| Execução | Build, cancelamento, processo do usuário e estados |
| Terminal | ConPTY, renderização, teclado e ciclo de vida da sessão |
| Interface | Layout, comandos e navegação |
| Preferências | JSON local, recentes e restauração da sessão |

Use workspace do Roslyn apropriado ao projeto real, por exemplo MSBuildWorkspace com configuração correta da descoberta de MSBuild/SDK. Validar carregamento, referências e compatibilidade dos serviços de linguagem. Buffers não salvos precisam atualizar os Documents do workspace sem depender de gravar em disco a cada tecla.

Operações pesadas são assíncronas. Não usar `.Wait()`/`.Result` na thread da interface. Cancelar completion/análise ao trocar arquivo ou mudar versão; reaproveitar caches e documentos; descartar watchers, processos, workspaces e handles ao fechar.

Não indexar o disco inteiro nem varrer dependências e pastas geradas a cada tecla. Explorer precisa de atualização incremental/debounce e tolerância a arquivos externos mudando. Evitar reentrada dos watchers por causa do próprio salvamento.

Metas a medir em Release no Windows, com um projeto console pequeno, no PC de Henrique ou máquina comparável: Ryzen 7 5700X, 32 GB RAM e SSD.

- Editor disponível em até aproximadamente 3 segundos numa abertura normal; serviços podem carregar depois sem bloquear a escrita.
- Digitação sem pausas perceptíveis causadas pela análise.
- Completion com cache aquecido em cerca de 300 ms nas situações comuns.
- Diagnósticos atualizados em cerca de 1 segundo após pausa numa edição simples.
- Build, restore e programas longos não congelam a janela.

São metas de experiência, não alegações sem medição. Registre condições e resultados. Separar custo de primeira inicialização/restore do custo de reexecução. Não prometer compilação instantânea nem comparar velocidade ao Bloco de Notas sem medir.

## 11. Validação obrigatória da versão completa

Validar em Windows real: build WPF, renderização, atalhos, diálogos, escala e terminal não podem ser certificados apenas por testes em Linux.

Automatizar testes significativos de tradução de diagnósticos, preservação de arquivos, resolução de projeto e cancelamento de execução. Validar a interface e o terminal com roteiro de uso. Não criar centenas de testes que apenas repetem a implementação.

### Roteiro de aceite

1. **Primeira abertura:** sem sessão anterior, editor pronto; escrever e desfazer. Sem SDK, editor abre e executar informa a instalação necessária.
2. **Projeto:** criar `A_Torre` em caminho com espaços/acentos; abrir `Program.cs`, salvar, fechar app, reabrir e executar.
3. **Arquivos:** criar pasta e `.cs`, abrir duas abas, distinguir arquivos com mesmo nome, renomear, enviar à Lixeira; cancelar fechamento com texto não salvo.
4. **Salvar:** falha por falta de permissão não descarta o conteúdo; arquivo existente não é truncado; mudança externa com edição local não sobrescreve nada silenciosamente.
5. **Recuperação:** encerrar abruptamente com rascunho alterado e confirmar recuperação do texto ao reabrir.
6. **Completion:** `Console.Wri` sugere membros reais; variável criada no arquivo e classe criada em outro arquivo aparecem; remover/renomear símbolo atualiza sugestões.
7. **Snippets:** `for` + Tab cria a estrutura, permite editar limite/corpo e desfazer em uma operação; Tab normal funciona sem sugestão ativa.
8. **Formatar:** bloco mal indentado fica organizado sem mudança de strings/comentários/lógica; desfazer restaura o texto.
9. **Erros:** testar CS1002, CS0103, CS0117, CS0029 e erro em arquivo irmão. Mensagem curta em português e clique levando ao local real; correção remove o erro.
10. **Contexto:** projeto com implicit usings e dois arquivos não exibe falsos erros por análise isolada; build real concorda com a possibilidade de executar.
11. **ReadLine:** prompt com `Console.Write("Nome: ")`, ler “Henrique” e responder imediatamente, com acentos.
12. **ReadKey:** menu de combate por tecla única, testar eco ligado/desligado e setas; `Console.Clear` e cores funcionam.
13. **Parar:** programa com loop infinito e programa aguardando entrada encerram pelo botão; é possível executar novamente; não ficam processos filhos órfãos.
14. **Falha de build:** introduzir erro após execução bem-sucedida; não executar o binário antigo; mostrar Problemas.
15. **Falha de execução:** provocar exceção; terminal mantém stack trace/saída e editor continua funcionando.
16. **Volume:** programa com muitas linhas não congela o editor nem cresce memória indefinidamente; painel redimensionável permanece utilizável.
17. **Offline:** abrir, editar, completar, formatar e executar projeto já restaurado sem internet.
18. **Portabilidade do código:** abrir o projeto produzido em outra ferramenta ou executar `dotnet build` na pasta sem depender do CSharp Lab.
19. **Distribuição:** testar pacote publicado em Windows sem runtime do aplicativo previamente instalado; informar separadamente a exigência do SDK para os projetos.

Considere concluído somente quando esses fluxos funcionarem. Se algum ambiente impedir a validação, diga exatamente o que foi implementado e o que não foi testado; não marque como aprovado por suposição.

## 12. Como conduzir a implementação

As etapas abaixo são a ordem de construção do mesmo produto inicial, não versões opcionais:

1. Validar um pequeno protótipo WPF com editor, Roslyn compatível e terminal ConPTY interativo. Resolver dependências/licenças antes de consolidar a arquitetura.
2. Implementar documentos, arquivos/pastas, projeto console e salvamento seguro.
3. Integrar o workspace real, completion, assinatura, snippets, formatação e diagnósticos curtos.
4. Integrar build, execução, input, parada e transições entre Terminal/Problemas.
5. Finalizar recuperação, recentes, preferências e ajustes de interface.
6. Executar o roteiro de aceite, corrigir falhas, medir desempenho e publicar o pacote Windows.

Não parar depois de desenhar a interface. Não chamar um resultado de “completo” com autocomplete falso, execução simulada ou terminal que só mostra logs.

## 13. O que Claudy deve entregar

- Código-fonte completo e organizado, com versões de dependências fixadas.
- Solução/projetos que compilam e scripts/comandos claros para build e publish.
- Pacote Windows x64 self-contained, com o executável fácil de localizar e iniciar.
- README curto em português: pré-requisito do SDK, como abrir o app, criar projeto, escrever, salvar, executar, digitar no terminal e formatar.
- Instruções de desenvolvimento separadas do uso normal.
- Licenças/avisos das dependências distribuídas.
- Resultado do roteiro de aceite, medições e limitações reais de ambiente, se houver.

Sem seção de funcionalidades futuras. A entrega é este aplicativo inicial utilizável.

## 14. Referências técnicas para implementação

Usar documentação oficial e repositórios dos próprios componentes. Estas referências sustentam a seleção de ferramentas; confirme APIs e versões durante a implementação.

- [.NET 10 — downloads oficiais](https://dotnet.microsoft.com/pt-br/download/dotnet/10.0)
- [Versionamento de C# por framework](https://learn.microsoft.com/pt-br/dotnet/csharp/language-reference/language-versioning)
- [WPF](https://learn.microsoft.com/pt-br/dotnet/desktop/wpf/overview/)
- [AvalonEdit — repositório oficial](https://github.com/icsharpcode/AvalonEdit)
- [Roslyn — repositório oficial](https://github.com/dotnet/roslyn)
- [Modelo de workspace do Roslyn](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/workspaces)
- [dotnet new](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-new)
- [dotnet build](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-build)
- [dotnet run](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-run)
- [Pseudoconsoles do Windows](https://learn.microsoft.com/en-us/windows/console/pseudoconsoles)
- [Criar sessão ConPTY](https://learn.microsoft.com/en-us/windows/console/creating-a-pseudoconsole-session)
- [Publicação de aplicativos .NET](https://learn.microsoft.com/en-us/dotnet/core/deploying/)

## Orientação final para Claudy

Henrique quer facilitar a vida enquanto aprende C#. Construa uma ferramenta real, rápida e discreta: arquivos à esquerda, código no centro, terminal ou problemas embaixo. Ajuda boa é uma sugestão correta, um snippet útil, uma formatação confiável e um erro curto que leva à linha indicada. Cada detalhe técnico deste documento serve para que ele consiga abrir, escrever e usar o próprio código sem depender de outra IDE para o básico.
