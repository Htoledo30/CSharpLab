# Acompanhamento ao vivo (registro da sessão)

O CSharp Lab grava o que acontece na sessão num arquivo, uma linha JSON por acontecimento:

```
%LocalAppData%\CSharpLab\sessao.jsonl
```

Serve para quem acompanha de fora: por exemplo, um assistente de IA (Claude) que lê o arquivo ao
vivo enquanto a pessoa programa e avisa quando aparece um erro, quando o programa quebra ou quando
ela pede revisão. O próprio app não lê este arquivo. Tudo fica no PC; nada é enviado para a internet.

Acima de ~2 MB o arquivo vira `sessao.anterior.jsonl` e começa um novo. Gravar nunca atrapalha o
editor: se falhar, o evento é perdido em silêncio (`SessionLog.Write`).

## Formato

Toda linha tem `time` (hora local, `yyyy-MM-ddTHH:mm:ss`) e `event`. O resto depende do evento.
Um "erro" (`errors[]`) tem `file` (só o nome), `line`, `id` (CS1002, DICA03…), `message` (em
português, como no painel Problemas) e `original` (a mensagem do compilador, quando é diferente).
Um "projeto" (`project`) tem `name`, `kind` (`console` ou `game`) e `folder`.

| `event` | Quando | Campos |
|---|---|---|
| `app-start` | O CSharp Lab abriu | `version` |
| `app-exit` | O CSharp Lab fechou | — |
| `folder-opened` | Abriu uma pasta/projeto | `folder` |
| `saved` | Salvou um arquivo | `file` (caminho completo), `errorCount`, `warningCount`, `errors[]` (os que o editor mostra nele agora) |
| `build` | Compilou (F5 com algo mudado) | `project`, `outcome` (`Succeeded`, `Failed`, `RestoreFailed`), `seconds`, `errorCount`, `warningCount`, `errors[]` |
| `run-start` | O programa/jogo começou a rodar | `project`, `reusedBuild`, `startScene` (Ctrl+F5) |
| `crash` | O programa parou com uma exceção | `project`, `type`, `message`, `explanation` e `tip` (em português), `file`/`line` (a linha do código da pessoa), `inEngine`, `methods[]`, `frames[]` |
| `run-end` | O programa/jogo terminou | `project`, `exitCode`, `stoppedByUser`, `crashed` |
| `app-error` | Erro interno do CSharp Lab (o mesmo do `erros.log`) | `context`, `type`, `message`, `at` |
| `review-request` | **Projeto → Terminei: pedir revisão** (Ctrl+Shift+R) | `folder`, `project`, `activeFile`, `unsavedOutside`, `errorCount`, `warningCount`, `errors[]` |

Detalhes:

- `crash.inEngine` = o erro nasceu dentro do motor dos jogos (`CSharpLab.GameEngine`). Com
  `type` = `CSharpLab.GameEngine.GameException` é o motor avisando de um uso errado (erro de quem
  programa, já explicado em português). Com outro tipo, pode ser um problema **no próprio motor**.
- `app-error` é sempre problema do CSharp Lab, nunca de quem programa.
- "Terminei: pedir revisão" salva antes os arquivos alterados da pasta aberta, então os arquivos em
  disco são a versão que a pessoa quer revisada. `unsavedOutside` conta abas com alterações que
  ficaram de fora (arquivos fora da pasta).

## Exemplo

```json
{"time":"2026-10-12T14:03:10","event":"build","project":{"name":"Aprendizado","kind":"game","folder":"C:\\Users\\...\\Aprendizado"},"outcome":"Failed","seconds":2.4,"errorCount":1,"warningCount":0,"errors":[{"file":"Program.cs","line":12,"id":"CS1002","message":"Faltou ; no fim da linha","original":"; expected"}]}
```

## Acompanhando

Qualquer programa pode seguir o arquivo, por exemplo no Git Bash:

```bash
tail -n 0 -F "$(cygpath "$LOCALAPPDATA")/CSharpLab/sessao.jsonl"
```

Código: `src/CSharpLab.Core/Settings/SessionLog.cs` (gravação) e
`src/CSharpLab/ViewModels/MainViewModel.Session.cs` (os eventos). Os testes ficam em
`SessionLogTests` (nos dois projetos de teste) e em `RuntimeErrorTests`.
