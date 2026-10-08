using System.IO;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.Input;
using CSharpLab.Core.Files;
using CSharpLab.Core.Projects;
using CSharpLab.GameEngine;

namespace CSharpLab.ViewModels;

// Telas desenhadas dos jogos (aba Tela): criar a tela de uma cena e ligar ela ao Program.cs.
public sealed partial class MainViewModel
{
    /// <summary>O jogo aberto (o projeto de janela que o Executar roda), ou null se a pasta não tem jogo.</summary>
    private ProjectFile? GameProject =>
        RunProject is { IsWindowApp: true } run ? run : Projects.FirstOrDefault(p => p.Error == null && p.IsWindowApp);

    [RelayCommand]
    private void NewScreen()
    {
        if (GameProject is not { } project)
        {
            Dialogs.ShowError("Nova tela",
                "Telas desenhadas são para jogos com botões. Crie um jogo em Arquivo → Novo projeto… → Jogo com botões, ou abra a pasta de um jogo.");
            return;
        }
        var screens = Path.Combine(project.Directory, ScreenLibrary.Folder);
        var program = Path.Combine(project.Directory, "Program.cs");
        var code = ReadCode(program);
        var scenes = ScenesIn(code);
        var suggestion = scenes.FirstOrDefault(s => !File.Exists(Path.Combine(screens, s + ".json"))) ?? NextFreeName(screens);

        var name = Dialogs.AskText("Nova tela",
            "Nome da cena desta tela, em inglês e sem espaços (ex.: Village, Fight, Shop). É o mesmo nome do game.Scene no código.",
            suggestion, value =>
            {
                value = value.Trim();
                if (ScreenFile.NameProblem(value) is { } problem) return char.ToUpperInvariant(problem[0]) + problem[1..];
                return File.Exists(Path.Combine(screens, value + ".json")) ? $"A cena \"{value}\" já tem uma tela." : null;
            });
        if (name == null) return;
        name = name.Trim();

        var path = Path.Combine(screens, name + ".json");
        if (!CreateScreenFile(path, name)) return;

        // Se o código ainda não tem a cena, oferece escrever o começo dela (com o botão da tela já ligado).
        if (!scenes.Contains(name, StringComparer.OrdinalIgnoreCase) && File.Exists(program) &&
            Dialogs.Confirm("Ligar a tela ao código",
                $"O Program.cs ainda não tem a cena \"{name}\". Quer que o CSharp Lab escreva o começo dela? " +
                "Ela aparece antes do game.Start, pronta para você completar.",
                "Escrever a cena"))
        {
            AddSceneToProgram(program, name);
        }
        OpenFile(path);
        FocusEditorRequested?.Invoke();
    }

    /// <summary>Cria Screens/&lt;Cena&gt;.json com a tela inicial (título e botão Continuar).</summary>
    private bool CreateScreenFile(string path, string scene)
    {
        var screens = Path.GetDirectoryName(path)!;
        try
        {
            Directory.CreateDirectory(screens);
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            stream.Write(TextFileIO.Utf8NoBom.GetBytes(ScreenFile.Serialize(ScreenLayout.CreateDefault(scene))));
            return true;
        }
        catch (Exception ex)
        {
            Dialogs.ShowError("Não foi possível criar a tela", FileErrors.Describe(ex, screens));
            return false;
        }
    }

    /// <summary>A pasta aberta tem um jogo com botões (mostra o botão Cenas ao lado do Executar).</summary>
    public bool HasGame => GameProject != null;

    /// <summary>
    /// As cenas do jogo para o botão Cenas: as do código (na ordem em que aparecem) e as telas que
    /// ainda não têm cena no código.
    /// </summary>
    public IReadOnlyList<GameSceneInfo> GameScenes()
    {
        if (GameProject is not { } project) return [];
        var dir = project.Directory;
        var scenes = new List<GameSceneInfo>();
        string? start = null;
        foreach (var file in CodeFiles(dir))
        {
            var text = FindDocument(file)?.Document.Text ?? TryRead(file);
            if (text == null || !text.Contains("Scene", StringComparison.Ordinal)) continue;
            var root = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(text).GetRoot();
            foreach (var call in Core.Language.GameAssist.FindScenes(root))
            {
                if (scenes.Any(s => string.Equals(s.Name, call.Name, StringComparison.OrdinalIgnoreCase))) continue;
                scenes.Add(new GameSceneInfo(call.Name, HasScreen(dir, call.Name), InCode: true, IsStart: false));
            }
            start ??= StartScene().Match(text) is { Success: true } m ? m.Groups[1].Value : null;
        }
        foreach (var screen in GameScreens.Scenes(dir))
        {
            if (!scenes.Any(s => string.Equals(s.Name, screen, StringComparison.OrdinalIgnoreCase)))
                scenes.Add(new GameSceneInfo(screen, HasScreen: true, InCode: false, IsStart: false));
        }
        return scenes.Select(s => s with { IsStart = string.Equals(s.Name, start, StringComparison.OrdinalIgnoreCase) }).ToList();
    }

    /// <summary>Abre a cena: a tela, se ela tiver; senão, o código dela.</summary>
    public void OpenGameScene(string scene)
    {
        if (GameProject is not { } project) return;
        if (HasScreen(project.Directory, scene)) OpenSceneScreen(project.Directory, scene);
        else GoToSceneCode(project.Directory, scene);
    }

    [GeneratedRegex("""\.Start\(\s*"([A-Za-z_][A-Za-z0-9_]*)"\s*\)""")]
    private static partial Regex StartScene();

    /// <summary>Pede para a aba de uma tela mostrar o desenho (não o Texto).</summary>
    public event Action<DocumentViewModel>? ScreenDesignRequested;

    /// <summary>A pasta do jogo (projeto de janela) que contém o arquivo, ou null.</summary>
    public string? GameDirectoryOf(string? file)
    {
        if (file == null) return null;
        var full = Path.GetFullPath(file);
        return Projects
            .Where(p => p.Error == null && p.IsWindowApp &&
                        full.StartsWith(p.Directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => p.Directory.Length)
            .FirstOrDefault()?.Directory;
    }

    /// <summary>A cena tem tela desenhada (no disco ou numa aba ainda não salva)?</summary>
    public bool HasScreen(string projectDirectory, string scene)
    {
        var path = GameScreens.PathOf(projectDirectory, scene);
        return GameScreens.OpenTexts.ContainsKey(path) || File.Exists(path);
    }

    /// <summary>Abre a tela da cena na aba Tela. Sem tela ainda, oferece criar.</summary>
    public void OpenSceneScreen(string projectDirectory, string scene)
    {
        var path = GameScreens.PathOf(projectDirectory, scene);
        if (FindDocument(path) == null && !File.Exists(path))
        {
            if (ScreenFile.NameProblem(scene) is { } problem)
            {
                NotifyInfo($"A cena \"{scene}\" não pode ter tela desenhada: {problem}");
                return;
            }
            if (!Dialogs.Confirm("Desenhar a tela",
                    $"A cena \"{scene}\" ainda não tem tela desenhada. Quer criar agora? Ela começa com um título e um botão Continuar.\n\n" +
                    "Numa cena com tela, os textos, botões e barras ficam na tela, e o código liga eles com game.Find(\"Nome\"). " +
                    "O game.Write continua valendo dentro dos botões.",
                    "Criar a tela"))
                return;
            if (!CreateScreenFile(path, scene)) return;
        }
        var doc = OpenFile(path);
        if (doc == null) return;
        ScreenDesignRequested?.Invoke(doc);
    }

    /// <summary>Vai até o game.Scene("Nome", …) no código. Sem a cena no código, oferece escrever o começo dela.</summary>
    public void GoToSceneCode(string projectDirectory, string scene)
    {
        var found = FindSceneInCode(projectDirectory, scene);
        if (found == null)
        {
            var program = Path.Combine(projectDirectory, "Program.cs");
            if (FindDocument(program) == null && !File.Exists(program))
            {
                NotifyInfo($"O código ainda não tem a cena \"{scene}\". Escreva game.Scene(\"{scene}\", () => {{ … }}); no seu código.");
                return;
            }
            if (!Dialogs.Confirm("Código da cena",
                    $"O código ainda não tem a cena \"{scene}\". Quer que o CSharp Lab escreva o começo dela no Program.cs? " +
                    "Ela aparece antes do game.Start, pronta para você completar.",
                    "Escrever a cena"))
                return;
            AddSceneToProgram(program, scene);
            found = FindSceneInCode(projectDirectory, scene);
            if (found == null) return;
        }
        var (file, offset) = found.Value;
        var doc = OpenFile(file, activate: false);
        if (doc != null) NavigateTo(doc, 1, 1, offset);
    }

    /// <summary>
    /// Onde o código usa a peça na cena: o lugar que liga o evento dela (OnClick/OnAnswer), se houver;
    /// senão, o primeiro game.Find("Nome"). Null se o código não usa a peça.
    /// </summary>
    public PieceCode? FindPieceCode(string projectDirectory, string scene, string piece, string handler)
    {
        PieceCode? firstUse = null;
        foreach (var file in CodeFiles(projectDirectory))
        {
            var text = FindDocument(file)?.Document.Text ?? TryRead(file);
            if (text == null || !text.Contains(piece, StringComparison.OrdinalIgnoreCase)) continue;
            var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(text);
            foreach (var use in Core.Language.GameAssist.FindPieceUses(tree.GetRoot(), scene, piece, handler))
            {
                var line = tree.GetText().Lines.GetLineFromPosition(use.Start).LineNumber + 1;
                var found = new PieceCode(file, use.Start, line, use.IsHandler);
                if (use.IsHandler) return found;
                firstUse ??= found;
            }
        }
        return firstUse;
    }

    /// <summary>Onde está o game.Scene("Nome", …) da cena, ou null se o código ainda não tem a cena.</summary>
    public PieceCode? FindSceneCode(string projectDirectory, string scene)
    {
        if (FindSceneInCode(projectDirectory, scene) is not { } found) return null;
        var (file, offset) = found;
        var text = FindDocument(file)?.Document.Text ?? TryRead(file) ?? "";
        int line = 1;
        for (int i = 0; i < offset && i < text.Length; i++)
            if (text[i] == '\n') line++;
        return new PieceCode(file, offset, line, HasHandler: true);
    }

    public void GoToPieceCode(PieceCode code)
    {
        var doc = OpenFile(code.File, activate: false);
        if (doc == null) return;
        // O arquivo pode ter mudado desde que o painel olhou: confere a posição.
        int offset = code.Offset <= doc.Document.TextLength ? code.Offset : 0;
        NavigateTo(doc, code.Line, 1, offset);
    }

    /// <summary>
    /// Escreve game.Find("Nome").OnClick(() => { }); no fim do código da cena e põe o cursor entre as chaves:
    /// o que a peça faz, a pessoa escreve. Sem a cena no código, oferece escrever a cena primeiro.
    /// </summary>
    public void WritePieceHandler(string projectDirectory, string scene, string piece, string handler)
    {
        var parameter = handler == "OnAnswer" ? "answer" : "()";
        var found = FindSceneInCode(projectDirectory, scene);
        if (found == null)
        {
            GoToSceneCode(projectDirectory, scene);   // pergunta se pode escrever a cena
            found = FindSceneInCode(projectDirectory, scene);
            if (found == null) return;
        }
        var (file, sceneOffset) = found.Value;
        var doc = OpenFile(file, activate: false);
        if (doc == null) return;
        var text = doc.Document.Text;
        var root = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(text).GetRoot();
        var newLine = TextFileIO.DetectLineEnding(text) is "LF" ? "\n" : "\r\n";
        if (Core.Language.GameAssist.HandlerInsertion(root, scene, piece, handler, parameter, newLine) is not { } insertion)
        {
            NavigateTo(doc, 1, 1, sceneOffset);
            NotifyInfo($"A cena \"{scene}\" está escrita sem chaves. Coloque o código dela entre {{ }} e escreva ali: game.Find(\"{piece}\").{handler}({parameter} => {{ }});");
            return;
        }
        doc.Document.Replace(insertion.Offset, insertion.Length, insertion.Text);
        NavigateTo(doc, 1, 1, insertion.Offset + insertion.Caret);
        NotifyInfo(handler == "OnAnswer"
            ? $"Pronto: escreva entre as chaves o que acontece quando o jogador responde no \"{piece}\" (ainda não salvo)."
            : $"Pronto: escreva entre as chaves o que o botão \"{piece}\" faz (ainda não salvo).");
    }

    /// <summary>Onde está o game.Scene("Nome", …): arquivo e posição (Program.cs primeiro; abas abertas valem pelo texto da aba).</summary>
    private (string File, int Offset)? FindSceneInCode(string projectDirectory, string scene)
    {
        (string, int)? caseInsensitive = null;
        foreach (var file in CodeFiles(projectDirectory))
        {
            var text = FindDocument(file)?.Document.Text ?? TryRead(file);
            if (text == null || !text.Contains(scene, StringComparison.OrdinalIgnoreCase)) continue;
            var root = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(text).GetRoot();
            foreach (var call in Core.Language.GameAssist.FindScenes(root))
            {
                if (call.Name == scene) return (file, call.Span.Start);
                if (caseInsensitive == null && string.Equals(call.Name, scene, StringComparison.OrdinalIgnoreCase))
                    caseInsensitive = (file, call.Span.Start);
            }
        }
        return caseInsensitive;
    }

    private string ReadCode(string program)
    {
        if (FindDocument(program) is { } open) return open.Document.Text;
        try { return File.Exists(program) ? File.ReadAllText(program) : ""; }
        catch (IOException) { return ""; }
    }

    /// <summary>Nomes das cenas do código: game.Scene("Nome", ...).</summary>
    internal static List<string> ScenesIn(string code) =>
        SceneCall().Matches(code).Select(m => m.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    [GeneratedRegex("""\.Scene\(\s*"([A-Za-z_][A-Za-z0-9_]*)"\s*,""")]
    private static partial Regex SceneCall();

    private static string NextFreeName(string screens)
    {
        for (int i = 1; ; i++)
        {
            var name = i == 1 ? "Start" : "Screen" + i;
            if (!File.Exists(Path.Combine(screens, name + ".json"))) return name;
        }
    }

    /// <summary>Escreve a cena no Program.cs (aberto numa aba, para a pessoa ver e poder desfazer).</summary>
    private void AddSceneToProgram(string program, string name)
    {
        var doc = OpenFile(program, activate: false);
        if (doc == null) return;
        var text = doc.Document.Text;
        var nl = TextFileIO.DetectLineEnding(text) is "LF" ? "\n" : "\r\n";
        var snippet = string.Join(nl,
        [
            $"game.Scene(\"{name}\", () =>",
            "{",
            $"    // As peças da tela \"{name}\" (aba Tela). Mude pelo nome: game.Find(\"Nome\")",
            "    game.Find(\"Continue\").OnClick(() =>",
            "    {",
            "        game.Write(\"Você clicou em Continuar!\");",
            "    });",
            "});",
            "",
            "",
        ]);

        // Antes do game.Start (que fica no fim); sem ele, no fim do arquivo.
        var start = StartCall().Matches(text).LastOrDefault();
        int offset;
        if (start != null)
        {
            offset = start.Index;
        }
        else
        {
            offset = text.Length;
            if (text.Length > 0 && !text.EndsWith('\n')) snippet = nl + nl + snippet;
        }
        doc.Document.Insert(offset, snippet);
        NotifyInfo($"A cena \"{name}\" foi escrita no Program.cs (ainda não salvo).");
    }

    /// <summary>
    /// Uma peça mudou de nome na aba Tela: troca os game.Find("antigo") daquela cena no código.
    /// As abas alteradas ficam sem salvar (dá para desfazer). Retorna quantos lugares mudaram.
    /// </summary>
    public int RenamePieceInCode(string projectDirectory, string scene, string oldName, string newName)
    {
        int count = 0;
        List<string> files;
        try
        {
            files = Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
                .Where(f => !ProjectLocator.IsInsideSkippedDirectory(f, projectDirectory))
                .Take(300)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
        foreach (var file in files)
        {
            var text = FindDocument(file)?.Document.Text ?? TryRead(file);
            if (text == null || !text.Contains(oldName, StringComparison.OrdinalIgnoreCase)) continue;
            var root = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(text).GetRoot();
            var spans = Core.Language.GameAssist.FindPieceReferences(root, scene, oldName);
            if (spans.Count == 0) continue;
            var doc = OpenFile(file, activate: false);
            if (doc == null || doc.Document.Text != text) continue;
            doc.Document.BeginUpdate();   // um passo só no desfazer
            try
            {
                foreach (var span in spans.OrderByDescending(s => s.Start))
                    doc.Document.Replace(span.Start, span.Length, newName);
            }
            finally
            {
                doc.Document.EndUpdate();
            }
            count += spans.Count;
        }
        return count;
    }

    /// <summary>Os .cs do projeto, Program.cs primeiro (sem bin/obj).</summary>
    private static List<string> CodeFiles(string projectDirectory)
    {
        try
        {
            return Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
                .Where(f => !ProjectLocator.IsInsideSkippedDirectory(f, projectDirectory))
                .Take(300)
                .OrderBy(f => string.Equals(Path.GetFileName(f), "Program.cs", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string? TryRead(string file)
    {
        try { return File.ReadAllText(file); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    [GeneratedRegex(@"^[ \t]*\w+\.Start\(", RegexOptions.Multiline)]
    private static partial Regex StartCall();
}

/// <summary>Uma cena do jogo no botão Cenas.</summary>
/// <param name="HasScreen">Tem tela desenhada (Screens/Nome.json).</param>
/// <param name="InCode">Tem game.Scene("Nome", …) no código.</param>
/// <param name="IsStart">É a cena do game.Start (onde o jogo começa).</param>
public sealed record GameSceneInfo(string Name, bool HasScreen, bool InCode, bool IsStart);

/// <summary>Onde o código usa uma peça (para o painel mostrar "Faz algo — Program.cs, linha 34").</summary>
/// <param name="HasHandler">Ali o evento da peça é ligado (OnClick/OnAnswer).</param>
public sealed record PieceCode(string File, int Offset, int Line, bool HasHandler);
