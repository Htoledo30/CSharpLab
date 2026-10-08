using System.Reflection;

namespace CSharpLab.Core.Projects;

/// <summary>Projeto de exemplo para estudar: código em inglês, comentários em português.</summary>
/// <param name="IsGame">Jogo com botões (janela) em vez de console.</param>
public sealed record ExampleProject(string Id, string Title, string Description, bool IsGame = false);

/// <summary>
/// Exemplos prontos (adivinhe o número, calculadora, jogo da velha, cobrinha, batalha RPG e os dois RPGs de janela),
/// guardados dentro do programa e copiados para uma pasta do usuário ao abrir.
/// </summary>
public static class Examples
{
    public static IReadOnlyList<ExampleProject> All { get; } =
    [
        new("GuessTheNumber", "Adivinhe o número", "Random, while, if/else e int.TryParse"),
        new("Calculator", "Calculadora", "Métodos, switch e um menu que repete"),
        new("TicTacToe", "Jogo da velha", "Array de duas dimensões e for dentro de for"),
        new("RpgBattle", "Batalha RPG", "Classes e objetos, em dois arquivos"),
        new("Snake", "Cobrinha", "Jogo em tempo real: teclas, cores e posição na tela"),
        new("RpgScreens", "RPG com botões", "Telas desenhadas na aba Tela; o código diz o que cada botão faz", IsGame: true),
        new("RpgButtons", "RPG só com código", "As mesmas ideias sem desenhar: game.Title, game.Button, game.Bar", IsGame: true),
    ];

    /// <summary>Pasta onde os exemplos são criados (Documentos\CSharp Lab\Exemplos).</summary>
    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CSharp Lab", "Exemplos");

    /// <summary>
    /// Arquivos do exemplo (caminho relativo → conteúdo), lidos dos recursos do programa: os .cs e as
    /// telas desenhadas ("Screens/Fight.json").
    /// </summary>
    public static IReadOnlyDictionary<string, string> FilesOf(ExampleProject example)
    {
        var assembly = typeof(Examples).Assembly;
        var prefix = $"CSharpLab.Core.Examples.{example.Id}.";
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.StartsWith(prefix, StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            var name = resource[prefix.Length..];
            // Recursos guardam pastas com ponto: "Screens.Fight.json" volta a ser "Screens/Fight.json".
            if (name.StartsWith(GameScreens.Folder + ".", StringComparison.Ordinal))
                name = GameScreens.Folder + "/" + name[(GameScreens.Folder.Length + 1)..];
            files[name] = reader.ReadToEnd().Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
        }
        return files;
    }

    /// <summary>
    /// Cria o exemplo em <paramref name="parentFolder"/>. Se ele já existe (de uma vez anterior),
    /// abre o que está lá: as mudanças que o usuário fez nunca são apagadas.
    /// </summary>
    public static CreatedProject Create(ExampleProject example, string parentFolder)
    {
        var dir = Path.Combine(parentFolder, example.Id);
        var csproj = Path.Combine(dir, example.Id + ".csproj");
        var program = Path.Combine(dir, "Program.cs");
        if (File.Exists(csproj)) return new CreatedProject(csproj, program);

        Directory.CreateDirectory(parentFolder);
        var files = FilesOf(example);
        var created = example.IsGame
            ? ProjectCreator.CreateGameProject(parentFolder, example.Id, files["Program.cs"])
            : ProjectCreator.CreateConsoleProject(parentFolder, example.Id, files["Program.cs"]);
        foreach (var (name, text) in files)
        {
            if (name.Equals("Program.cs", StringComparison.OrdinalIgnoreCase)) continue;
            var path = Path.Combine(created.Directory, name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text, Files.TextFileIO.Utf8NoBom);
        }
        return created;
    }
}
