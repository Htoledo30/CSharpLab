# Roteiro de aceite — resultados

Ambiente: Windows 11 Pro (build 26300), AMD Ryzen 7 5700X, SSD, SDK .NET 10.0.301, escala 100%.
Validação automatizada da revisão e atualização automática em 06/10/2026: **97 de 97 aprovados**
(`dotnet test CSharpLab.slnx -c Release`: 70 do Core e 27 de ViewModels/editor).
Os testes do instalador (`tests/Installer.Tests.ps1`) também passaram:
falha de preparação, rollback após falha na troca e instalação bem-sucedida, numa pasta temporária.

A conferência posterior das alterações encontrou e corrigiu regressões no encerramento (callbacks que
reiniciavam timers e resultados de descoberta aplicados depois de Dispose), no cancelamento de MSBuild
durante a preparação e no rename de arquivos para pastas ignoradas. Também corrigiu o recuo com
`indent_size` diferente de `tab_width` e a prioridade do aviso de falha de atualização. Os novos testes
cobrem esses cenários, incluindo Parar/fechar enquanto o processo real de MSBuild está bloqueado numa
leitura e quatro combinações de recuo com tabs. O encerramento cancela a preparação, descarta resultados
atrasados e desliga os handlers e timers; a classificação do editor descarta resultados de contexto antigo.

Revisão "melhorar tudo" (06/10/2026): **199 de 199 aprovados** (160 do Core e 39 de ViewModels/editor),
cobrindo as ajudas do editor (dica do mouse, F12, F2, Ctrl+., comandos de linha), 31 erros comuns traduzidos,
dicas DICA01/DICA02, reaproveitamento da compilação e salvar sem recarregar o projeto. Conferido também na
tela: dica do mouse em português, "Adicionar using System.Text;", F12 entre arquivos, renomear, menu do
botão direito e um jogo em tempo real (cobrinha) no terminal expandido.

Os resultados de interface abaixo pertencem à validação anterior, conduzida por um script
(teclado/mouse simulados + capturas de tela) sobre o executável real. A revisão atual validou controles
WPF por testes automatizados; não repetiu o roteiro visual completo nem instalou o aplicativo no perfil do usuário.

Atualizações: feed local, SHA-256, cancelamento, escolha da versão preparada mais nova, detecção de arquivos
alterados, espera do processo antigo, trava de instância e rollback verificados. O auxiliar PowerShell foi
executado em pastas temporárias com arquivos de teste; preservou desinstalador e arquivos pessoais.
O fluxo WPF verificou busca ao abrir, aviso, preferência persistida e cancelamento de reinício com arquivo sujo.
Uma publicação temporária real foi gerada e conferida: versão, metadado do repositório, arquivos do instalador
e checksum. A consulta e o download de uma Release real não foram validados: o projeto ainda não informa
qual repositório deve fornecer as atualizações. O reinício do aplicativo real e o roteiro visual completo
continuam sem validação nesta revisão.

Legenda: ✅ verificado · ⚠️ implementado, mas não verificado neste ambiente (motivo indicado).

| # | Fluxo | Resultado |
| --- | --- | --- |
| 1 | Primeira abertura | ✅ Editor pronto com `Sem título.cs` e o `Console.WriteLine("Olá, mundo!");`, focado; desfazer funciona. ⚠️ Abertura **sem SDK** não testada (a máquina tem o SDK); a verificação e o aviso "Instale o SDK .NET 10 para executar C#" com link e "Verificar novamente" estão implementados, e o app é self-contained. |
| 2 | Projeto `A_Torre` em caminho com espaços e acentos | ✅ Criado (teste automatizado e pela interface em `Projetos Ação\A_Torre`), aberto, salvo, sessão restaurada ao reabrir, executado. |
| 3 | Arquivos | ✅ Novo `.cs` pelo explorador (abre e recebe o foco), duas abas, aviso ao fechar com texto não salvo (Cancelar mantém tudo), renomear (testes, inclusive só maiúsculas), envio à Lixeira (verificado com arquivo temporário); excluir arquivo ou pasta com abas alteradas avisa que as alterações serão perdidas (teste). Abas com mesmo nome mostram a pasta ao lado (implementado). |
| 4 | Salvar | ✅ Falha por arquivo somente leitura não altera o original (teste); gravação atômica; mudança externa com edição local abre "Manter a minha / Recarregar do disco"; sem edição local recarrega sozinho. Salvar confere o disco antes de gravar, mesmo que o monitor ainda não tenha avisado (teste). Arquivo Windows-1252 com caractere fora da codificação pede para salvar em UTF-8 em vez de trocar o caractere (teste). |
| 5 | Recuperação | ✅ Processo encerrado à força com arquivo alterado: texto recuperado na reabertura, ligado ao caminho original, marcado como não salvo. Gravações e exclusões da recuperação são feitas em ordem (teste): uma gravação atrasada não recria rascunho descartado. |
| 6 | Completion | ✅ `Console.Wri` → `Write`, `WriteLine`; variável local e membros de classe de outro arquivo; texto não salvo de outro arquivo reflete nas sugestões (teste). |
| 7 | Snippets | ✅ `for` + Tab cria a estrutura; Tab percorre `i` (espelhado nas três posições) e `10`; Enter conclui; `cw` + Tab dentro do bloco. Tab sem atalho válido indenta normalmente. |
| 8 | Formatar | ✅ Bloco mal indentado organizado; strings e comentários intactos; um `Ctrl+Z` restaura tudo. |
| 9 | Erros | ✅ CS1002 "Faltou ";"", CS0103, CS0117, CS0029 "Não é possível colocar texto em uma variável int.", erro em arquivo irmão com arquivo/linha corretos; clique em Problemas leva à posição; dica ao passar o mouse com código e mensagem original. |
| 10 | Contexto | ✅ Projeto com usings implícitos e dois arquivos sem falsos erros (teste); a compilação real concorda. |
| 11 | ReadLine | ✅ `Console.Write("Nome: ")` aparece sem quebra de linha; "José Henrique" lido e respondido na hora, com acentos e ⚔. |
| 12 | ReadKey | ✅ `ReadKey(true)` com seta para cima → `UpArrow`; `ReadKey(false)` ecoa a tecla (teste); cores e `Console.Clear` geram sequências VT, não texto (teste + tela). |
| 13 | Parar | ✅ Loop infinito encerrado por Shift+F5 e pelo botão (o botão continua habilitado quando a execução começou por ele — teste); processos filhos encerrados (teste com filho); nenhum processo órfão; Ctrl+C sem seleção interrompe o programa (teste); executar de novo funciona. |
| 14 | Falha de build | ✅ Erro introduzido → arquivos salvos, Problemas aberto, "A compilação falhou", binário antigo não executado. |
| 15 | Falha de execução | ✅ Exceção preserva stack trace e código de saída (teste); o editor mostra "O programa encerrou com erro (código N)" separado da saída. |
| 16 | Volume | ✅ 100.000 linhas: compilação + execução em ~5 s, janela respondendo em todas as amostras (0 de 10 travadas), memória estável (~260 → ~305 MB), histórico limitado. |
| 17 | Offline | ⚠️ Não testado sem rede. Projetos já restaurados compilam com `--no-restore` e a análise usa o cache local; falha de restore mostra "Não foi possível restaurar as dependências" com detalhes. |
| 18 | Portabilidade | ✅ Projetos criados compilam com `dotnet build` puro (testes automatizados); arquivos são `.cs`/`.csproj` comuns. |
| 19 | Distribuição | ✅ Pacote self-contained gerado (`artifacts/CSharpLab-win-x64.zip`, 94 MB) e executado a partir da pasta publicada. ⚠️ Não testado num Windows sem nenhum runtime .NET instalado (o pacote não usa o runtime da máquina). O SDK continua necessário para executar programas. |

Outros pontos verificados: janela 1024×768 sem botões cortados; ícones com nome acessível e dica;
erros e avisos distinguidos por ícone e rótulo (não só cor). ⚠️ Escalas de 125% e 150% não foram testadas
(manifesto Per-Monitor V2, layout em unidades independentes de DPI).

## Medições (Release, ReadyToRun, projeto console pequeno)

| Medida | Resultado |
| --- | --- |
| Janela visível com a última pasta aberta (3 aberturas) | 799 ms (primeira), 678 ms, 679 ms |
| Carga dos serviços do Roslyn (MEF), em segundo plano | ~0,7 s |
| Avaliação real do projeto pelo SDK (com cache depois) | ~0,7 s |
| Primeiro completion (frio, sem aquecimento) | ~1,3 s — o editor faz um aquecimento em segundo plano ao abrir |
| Completion aquecido | mediana 14 ms, máximo 25 ms |
| Diagnósticos após uma edição | 164 ms na primeira, mediana 3 ms depois (+ ~420 ms de espera após a digitação) |
| Formatação do documento | ~275 ms (primeira vez) |
| `dotnet build` incremental | ~0,75–0,8 s com o servidor de compilação aquecido |
| F5 de novo sem mudar nada | ~30 ms (reaproveita a última compilação; antes ~1,3 s) |
| Editar, salvar e F5 | ~0,8 s |
| Primeiro F5 após abrir | ~1,06 s |
| Memória da janela após abrir | ~260 MB |

Primeira execução de um projeto novo inclui o restore (alguns segundos); reexecuções compilam em menos de 1 s.
