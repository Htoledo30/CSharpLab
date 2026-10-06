# Desenvolvimento do CSharp Lab

## Compilar, testar e publicar

```powershell
dotnet build CSharpLab.slnx                 # compila tudo
dotnet test tests/CSharpLab.Tests           # testes (precisa do SDK .NET 10)
dotnet run --project src/CSharpLab          # abre o editor em modo Debug
powershell -ExecutionPolicy Bypass -File build\publish.ps1   # gera artifacts\CSharpLab-win-x64.zip
```

`CSHARPLAB_DATA=<pasta>` faz o editor usar outra pasta de preferências/recuperação (útil para testar sem
mexer na sua sessão).

Publicar uma versão no GitHub: crie uma tag `v1.2.3` e envie (`git push --tags`). O workflow
`.github/workflows/release.yml` compila, testa, gera o zip e cria a Release com ele anexado.

## Estrutura

| Pasta | Responsabilidade |
| --- | --- |
| `src/CSharpLab.Core/Files` | Leitura com detecção de codificação, gravação atômica, Lixeira, validação de nomes |
| `src/CSharpLab.Core/Projects` | SDK (`dotnet --list-sdks`, `global.json`), localizar/criar projetos console, avaliação MSBuild |
| `src/CSharpLab.Core/Language` | Roslyn: workspace, completion, assinatura, diagnósticos traduzidos, formatação, classificação, snippets |
| `src/CSharpLab.Core/Build` | `dotnet restore/build` com diagnósticos SARIF e comando de execução |
| `src/CSharpLab.Core/Terminal` | Sessão ConPTY + Job Object (processo do usuário fora do editor) |
| `src/CSharpLab.Core/Settings` | Preferências JSON, recentes, recuperação de rascunhos |
| `src/CSharpLab/ViewModels` | MVVM: documentos, explorador, problemas, execução |
| `src/CSharpLab/Editor` | AvalonEdit + recursos de C# (cores, sublinhados, sugestões, assinatura, snippets, indentação) |
| `src/CSharpLab/Views` | Explorador, terminal, busca, diálogos, host de editores |
| `tests/CSharpLab.Tests` | Tradução de erros, arquivos, projetos, build real, cancelamento e terminal |

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
  última edição, gravação atômica), apagadas ao salvar ou descartar.
- **Sugestões**: lista própria (não a janela padrão do AvalonEdit) para ordenar por qualidade de
  correspondência: igual > começa com > iniciais CamelCase > contém. Os snippets do Roslyn são omitidos; os
  do editor aparecem identificados como "snippet".
