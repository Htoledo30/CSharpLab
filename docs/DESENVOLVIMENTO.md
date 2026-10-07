# Desenvolvimento do CSharp Lab

## Compilar, testar e publicar

```powershell
dotnet build CSharpLab.slnx                 # compila tudo
dotnet test CSharpLab.slnx                  # testes do Core e dos ViewModels (precisa do SDK .NET 10)
./tests/Installer.Tests.ps1                # testa instalação e rollback numa pasta temporária
dotnet run --project src/CSharpLab          # abre o editor em modo Debug
powershell -ExecutionPolicy Bypass -File build\publish.ps1   # gera artifacts\CSharpLab-win-x64.zip
```

`CSHARPLAB_DATA=<pasta>` faz o editor usar outra pasta de preferências/recuperação (útil para testar sem
mexer na sua sessão).

Publicar uma versão no GitHub: crie uma tag `v1.2.3` e envie (`git push --tags`). O workflow
`.github/workflows/release.yml` compila, testa, gera o zip e cria a Release com ele anexado.

## Atualização automática

O workflow grava `${{ github.repository }}` no executável como metadado `UpdateRepository` e anexa
`CSharpLab-win-x64.zip` e `CSharpLab-win-x64.zip.sha256` à Release. O editor usa a
[API de Releases do GitHub](https://docs.github.com/en/rest/releases/releases#get-the-latest-release)
para procurar versões estáveis mais novas. O repositório deve ser público para a consulta sem autenticação.

Para gerar o pacote localmente com esse recurso:

```powershell
./build/publish.ps1 -Version 1.0.1 -UpdateRepository usuario/repositorio
```

Sem `UpdateRepository`, o pacote abre e edita normalmente, mas a busca de atualizações fica sem fonte.
`CSHARPLAB_UPDATE_FEED` permite um JSON local no formato da Release para os testes; `CSHARPLAB_UPDATE_REPO`
substitui o repositório embutido. Use `CSHARPLAB_DATA` numa pasta própria em qualquer validação isolada.

O pacote só fica pronto após conferir o SHA-256. Um manifesto guarda os hashes dos arquivos extraídos;
eles são conferidos novamente antes de aplicar. O auxiliar PowerShell espera o processo antigo terminar,
adquire a trava de instância e prepara a nova pasta antes da troca. Preserva o desinstalador e arquivos
pessoais; só remove arquivos conhecidos do pacote anterior. Falhas na troca restauram a versão anterior.
Falhas ficam no log `Updates/atualizacao.log` e são avisadas na próxima abertura. Fechar o editor cancela
consultas e downloads pendentes. As cópias preparadas ficam em `Updates` até instalar ou ficarem inválidas.

As versões devem usar três números, como `1.2.3`. Não é necessário republicar versões antigas para
oferecer versões novas, mas uma cópia anterior ao recurso precisa ser instalada manualmente uma vez.

## Estrutura

| Pasta | Responsabilidade |
| --- | --- |
| `src/CSharpLab.Core/Files` | Leitura com detecção de codificação, gravação atômica, Lixeira, validação de nomes |
| `src/CSharpLab.Core/Projects` | SDK (`dotnet --list-sdks`, `global.json`), localizar/criar projetos console e jogos (`GameKit`), avaliação MSBuild, o que cada evento do disco exige (`FolderChanges`) |
| `src/CSharpLab.Core/Language` | Roslyn: workspace (`LanguageService.cs`), consultas (`LanguageService.Queries.cs`: completion, assinatura, dica, definição, renomear, formatação, cores, diagnósticos), erros traduzidos, dicas para iniciantes, snippets |
| `src/CSharpLab.Core/Build` | `dotnet restore/build` com diagnósticos SARIF, comando de execução e o que o F5 reaproveita na sessão (`RunCache`) |
| `src/CSharpLab.Core/Terminal` | Sessão ConPTY + Job Object (processo do usuário fora do editor) |
| `src/CSharpLab.Core/Settings` | Preferências JSON, recentes, recuperação de rascunhos |
| `src/CSharpLab.Game` | Motor dos jogos com botões (namespace `CSharpLab.GameEngine`, WPF). `Game` monta cada cena numa `Screen` (testável com uma `IGameView` falsa) e a `GameWindow` desenha num palco de 960×540 que escala com a janela. Copiado para `lib\` de cada jogo pelo `GameKit` (Core), que também cria o `.csproj` (WinExe), o `AGENTS.md`/`CLAUDE.md` e atualiza o motor de jogos antigos ao executar. |
| `src/CSharpLab.RuntimeHook` | Gancho (`DOTNET_STARTUP_HOOKS`, .NET 8+) carregado no programa do usuário: registra tipo, mensagem e linhas de um erro não tratado para o editor explicar em português (`RuntimeErrors`). Não altera o comportamento do programa. |
| `src/CSharpLab/ViewModels` | MVVM. `MainViewModel` é dividido por assunto: `.cs` (estado, início, painéis, avisos, encerramento), `.Documents` (abas, salvar, recuperação), `.Folders` (pasta e projetos), `.FileSystem` (mudanças no disco), `.Analysis` (erros ao vivo, navegação), `.Run` (SDK → salvar → compilar → terminal), `.Updates`. Mais explorador e problemas. |
| `src/CSharpLab/Editor` | AvalonEdit + recursos de C# (cores, sublinhados, sugestões, assinatura, snippets, indentação) |
| `src/CSharpLab/Screens` | Aba Tela (telas desenhadas dos jogos, `Screens/*.json`). `ScreenDesignerModel` edita a tela gravando o JSON no mesmo `TextDocument` da aba (desfazer, salvar, recuperação e mudança no disco de graça; digitação e setas seguidas viram um passo só). `ScreenStage` desenha com o `ScreenRenderer` do motor (igual ao jogo; os estilos do editor não entram no palco) e só grava ao soltar o mouse; `Snapper` faz o ímã com linhas-guia. `ScreenPropertiesPanel` atualiza os campos sem recriá-los (o cursor não se perde). O Core (`GameScreens`, `GameAssist`) sugere e confere os nomes do `game.Find`, usando o texto das telas abertas. Tela ↔ código: botão Cenas da barra de cima (`Views/ScenesMenu`, `MainViewModel.GameScenes`/`OpenGameScene`), "Código da cena" na aba Tela (`GoToSceneCode`) e F12/Ctrl+clique no nome da cena (`OpenSceneScreen`). |
| `src/CSharpLab/Views` | Explorador, terminal, busca, diálogos, host de editores |
| `tests/CSharpLab.Tests` | Tradução de erros, arquivos, projetos, build real, cancelamento e terminal |
| `tests/CSharpLab.App.Tests` | ViewModels e editor numa thread STA com recursos WPF reais: execução/parada, arquivos, recuperação e completion |

## Decisões técnicas

- **.NET 10 + WPF**, aplicativo `net10.0-windows` publicado *self-contained* para win-x64 com ReadyToRun
  (abre sem runtime instalado; o SDK só é exigido para compilar os programas do usuário).
- **Versões fixadas**: AvalonEdit 6.3.1.120, Roslyn (Microsoft.CodeAnalysis.*) 5.6.0,
  CI.Microsoft.Terminal.Wpf 1.25.260303002, CommunityToolkit.Mvvm 8.4.2,
  Basic.Reference.Assemblies.Net100 1.8.12. O Roslyn 5.6.0 é o mesmo compilador do SDK 10.0.301, então a
  análise ao digitar e a compilação real concordam (C# 14 por padrão em `net10.0`).
- **Workspace**: em vez do `MSBuildWorkspace` (processo de build separado e carga lenta), o editor monta um
  `AdhocWorkspace` com os dados reais do projeto obtidos do próprio SDK:
  `dotnet msbuild -t:ResolveAssemblyReferences -getProperty:… -getItem:Compile;Using;ReferencePath`.
  Isso traz arquivos, referências (inclusive de pacotes NuGet), usings implícitos, `Nullable`, `LangVersion`,
  `NoWarn`, `WarningLevel` e o comando de execução. O resultado fica em cache e é refeito quando o
  `.csproj`, o `project.assets.json`, `Directory.Build.*` ou o `global.json` mudam. Enquanto o SDK responde
  (ou se ele não existir), uma estimativa a partir do `.csproj` mantém o autocomplete funcionando.
  Textos não salvos atualizam os documentos do workspace sem gravar em disco.
- **Pastas sem projeto e rascunhos** usam um contexto console em memória com os padrões do template.
- **Diagnósticos ao digitar**: ~420 ms após a última tecla, em segundo plano, cancelando a análise anterior.
  O resultado só é aplicado se nenhum documento mudou desde o início (versão por documento).
- **Mensagens em português**: os argumentos vêm do diagnóstico do compilador (informação estruturada, lida
  por reflexão com fallback seguro). Erros da compilação real chegam por SARIF; para os códigos conhecidos
  os argumentos são extraídos da mensagem em inglês (o SDK é chamado com `DOTNET_CLI_UI_LANGUAGE=en`).
  Sem tradução própria: mensagem oficial pt-BR do Roslyn; sem ela, a original com o código.
- **Compilação real é a autoridade**: Executar salva os arquivos do projeto, roda `dotnet restore` só se
  necessário e `dotnet build --no-restore`. Se falhar, nada é executado.
- **Terminal**: `Microsoft.Terminal.Wpf` (o controle do Windows Terminal) ligado a uma conexão própria sobre
  **ConPTY**. O processo roda num **Job Object** com `KILL_ON_JOB_CLOSE`: Parar e fechar o editor encerram
  a árvore inteira. Assim que o programa inicia, o editor se anexa ao console dele por um instante e troca
  só a página de código de *saída* para UTF-8 (como um `chcp`), para emojis e símbolos não virarem "?";
  a entrada continua na página padrão, que cobre os acentos. A saída é entregue em lotes (~60/s) e o
  histórico do terminal é limitado pelo próprio controle.
- **Teclado no terminal**: o controle é uma janela nativa; as mensagens de teclado são filtradas antes do WPF
  (`ComponentDispatcher.ThreadFilterMessage`) para que setas e Tab cheguem ao programa e atalhos como F5,
  Shift+F5, Ctrl+S e Ctrl+C/Ctrl+V (copiar seleção/colar) funcionem.
- **Salvamento**: grava num temporário na mesma pasta e substitui o original (`File.Replace`); codificação
  (UTF-8 com/sem BOM, UTF-16, Windows-1252) e finais de linha são preservados.
- **Recuperação**: cópias de documentos alterados em `%LocalAppData%\CSharpLab\Recovery` (1,5 s após a
  última edição, gravação atômica), apagadas ao salvar ou descartar. Gravações e exclusões passam por uma
  fila única e ordenada, então uma gravação atrasada nunca recria um rascunho já descartado.
  Ao restaurar, a cópia mantém o mesmo identificador e continua no disco até salvar ou descartar,
  inclusive se o aplicativo encerrar inesperadamente de novo. Uma trava exclusiva impede duas
  instâncias de usarem a mesma pasta de preferências/recuperação.
- **Contexto do editor**: renomear entre `.cs` e outras extensões atualiza colorização e indentação.
  Mudanças no projeto atualizam opções de sintaxe, inclusive símbolos de pré-processador, e o
  `.editorconfig` define tabs/espaços e tamanho da indentação usada por Tab e Enter.
- **Instalação**: os arquivos são copiados para uma pasta temporária irmã antes da troca. A versão
  anterior fica disponível para rollback até a nova pasta ocupar o destino com sucesso.
- **Salvar** confere o arquivo no disco antes de gravar (data, tamanho e conteúdo) e pergunta antes de
  substituir uma versão alterada por outro programa. Codificações antigas (Windows-1252) nunca trocam
  caracteres em silêncio: se faltar algum, o editor oferece salvar em UTF-8.
- **Restore** é refeito quando o `.csproj`, `Directory.Build.*`, `Directory.Packages.props`, `NuGet.config`,
  `global.json` ou `packages.lock.json` mudam depois do `project.assets.json`. O cache da avaliação deixa de
  valer quando arquivos `.cs` são criados ou apagados, para respeitar `<Compile Remove>` e globs.
- **Sugestões**: lista própria (não a janela padrão do AvalonEdit) para ordenar por qualidade de
  correspondência: igual > começa com > iniciais CamelCase > contém. Os snippets do Roslyn são omitidos; os
  do editor aparecem identificados como "snippet".
