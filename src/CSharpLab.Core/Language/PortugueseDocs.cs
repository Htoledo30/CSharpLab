using Microsoft.CodeAnalysis;

namespace CSharpLab.Core.Language;

/// <summary>Explicação curta em português e um exemplo (o código continua em inglês).</summary>
public sealed record PortugueseDoc(string Text, string? Example = null);

/// <summary>
/// Explicações em português para as palavras-chave do C# e os métodos mais usados por quem está
/// começando. A documentação oficial desses métodos vem em inglês; aqui fica o essencial.
/// </summary>
public static partial class PortugueseDocs
{
    public static PortugueseDoc? ForKeyword(string keyword) => Keywords.GetValueOrDefault(keyword);

    public static PortugueseDoc? ForSymbol(ISymbol? symbol)
    {
        if (symbol == null) return null;
        if (symbol is IMethodSymbol { ReducedFrom: { } reduced }) symbol = reduced;
        symbol = symbol.OriginalDefinition;
        return KeyOf(symbol) is { } key ? Members.GetValueOrDefault(key) : null;
    }

    /// <summary>
    /// Explicação para a assinatura que o autocomplete mostra ("void Game.Say(string text, ...)",
    /// "int int.Parse(string s)", "class System.Console"), que traz o nome sem o namespace.
    /// </summary>
    public static PortugueseDoc? ForSignature(string signature)
    {
        var head = signature;
        int cut = head.IndexOfAny(['(', '{', '[']);
        if (cut >= 0) head = head[..cut];
        head = Generics().Replace(head, "").Trim();
        var name = head.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        if (name == null) return null;
        return Members.GetValueOrDefault(name) ?? ShortMembers.GetValueOrDefault(name);
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"<[^<>]*>")]
    private static partial System.Text.RegularExpressions.Regex Generics();

    /// <summary>"Console.WriteLine", "int.Parse", "List.Add": tipo e membro, como aparecem nas assinaturas.</summary>
    // Calculado no primeiro uso: depende de Members, que é inicializado mais abaixo no arquivo.
    private static Dictionary<string, PortugueseDoc>? _shortMembers;
    private static Dictionary<string, PortugueseDoc> ShortMembers => _shortMembers ??= BuildShort();

    private static Dictionary<string, PortugueseDoc> BuildShort()
    {
        var aliases = new Dictionary<string, string>
        {
            ["Int32"] = "int", ["Int64"] = "long", ["Double"] = "double", ["Single"] = "float", ["Decimal"] = "decimal",
            ["Object"] = "object", ["String"] = "string", ["Boolean"] = "bool", ["Char"] = "char",
        };
        var result = new Dictionary<string, PortugueseDoc>();
        foreach (var (key, doc) in Members)
        {
            var parts = key.Split('.');
            if (parts.Length < 2) continue;
            var type = aliases.GetValueOrDefault(parts[^2], parts[^2]);
            result.TryAdd(type + "." + parts[^1], doc);
        }
        return result;
    }

    /// <summary>"System.Console.ReadLine", "System.Collections.Generic.List.Add", "System.Random"…</summary>
    internal static string? KeyOf(ISymbol symbol) => symbol switch
    {
        INamedTypeSymbol type => TypeName(type),
        IMethodSymbol or IPropertySymbol or IFieldSymbol when symbol.ContainingType is { } owner => TypeName(owner) + "." + symbol.Name,
        _ => null,
    };

    private static string TypeName(INamedTypeSymbol type) =>
        type.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() + "." + type.Name : type.Name;

    private static PortugueseDoc D(string text, string? example = null) => new(text, example);

    private static readonly Dictionary<string, PortugueseDoc> Keywords = new()
    {
        // Tipos
        ["int"] = D("Número inteiro (sem casas decimais), de -2.147.483.648 até 2.147.483.647.", "int health = 100;"),
        ["long"] = D("Número inteiro para valores muito grandes.", "long population = 8_000_000_000;"),
        ["double"] = D("Número com casas decimais. É o tipo dos números com ponto, como 1.5.", "double height = 1.75;"),
        ["float"] = D("Número com casas decimais e menos precisão que double. Precisa do sufixo F.", "float speed = 2.5F;"),
        ["decimal"] = D("Número com casas decimais exato, bom para dinheiro. Precisa do sufixo M.", "decimal price = 19.90M;"),
        ["bool"] = D("Verdadeiro ou falso (true ou false).", "bool alive = health > 0;"),
        ["char"] = D("Um único caractere, entre aspas simples.", "char letter = 'A';"),
        ["string"] = D("Texto, entre aspas duplas.", "string name = \"Ana\";"),
        ["object"] = D("O tipo mais geral: qualquer valor cabe num object."),
        ["byte"] = D("Número inteiro pequeno, de 0 até 255."),
        ["var"] = D("Deixa o compilador descobrir o tipo pelo valor à direita. O tipo continua fixo depois.", "var name = \"Ana\"; // string"),
        ["void"] = D("O método não devolve nenhum valor: ele só faz alguma coisa.", "void Attack() { health--; }"),
        ["null"] = D("Ausência de valor: a variável não aponta para nenhum objeto.", "string? nickname = null;"),
        ["true"] = D("Verdadeiro (valor bool)."),
        ["false"] = D("Falso (valor bool)."),

        // Decisões e repetições
        ["if"] = D("Executa o bloco só quando a condição for verdadeira.", "if (health <= 0)\n{\n    Console.WriteLine(\"Fim de jogo\");\n}"),
        ["else"] = D("O caminho do if quando a condição é falsa.", "if (age >= 18) { ... }\nelse { ... }"),
        ["switch"] = D("Escolhe um caminho conforme o valor. Cada case termina com break.", "switch (option)\n{\n    case 1: Play(); break;\n    default: Quit(); break;\n}"),
        ["case"] = D("Um dos valores de um switch."),
        ["default"] = D("No switch, o caminho quando nenhum case combina. Fora dele, o valor padrão do tipo (0, false, null)."),
        ["for"] = D("Repete contando: início; condição para continuar; o que muda a cada volta.", "for (int i = 0; i < 10; i++)\n{\n    Console.WriteLine(i);\n}"),
        ["foreach"] = D("Passa por cada item de uma lista ou array, um de cada vez.", "foreach (var item in inventory)\n{\n    Console.WriteLine(item);\n}"),
        ["in"] = D("No foreach, separa a variável de cada item da coleção percorrida."),
        ["while"] = D("Repete enquanto a condição for verdadeira. Confira se algo muda lá dentro, senão o loop nunca acaba.", "while (health > 0)\n{\n    PlayTurn();\n}"),
        ["do"] = D("Como o while, mas executa o bloco pelo menos uma vez antes de testar a condição.", "do\n{\n    option = ReadOption();\n} while (option != 0);"),
        ["break"] = D("Sai na hora do loop (for, while, foreach) ou termina um case do switch."),
        ["continue"] = D("Pula o resto desta volta do loop e vai para a próxima."),
        ["return"] = D("Sai do método. Se o método devolve um valor, ele vem depois do return.", "return health > 0;"),

        // Classes e objetos
        ["class"] = D("Define um tipo seu: um molde com dados (campos) e ações (métodos) para criar objetos.", "class Hero\n{\n    public int Health = 100;\n    public void Heal() { Health += 10; }\n}"),
        ["struct"] = D("Como uma class, mas copiada por valor (bom para dados pequenos, como um ponto x/y)."),
        ["record"] = D("Classe feita para guardar dados, com comparação por valor e ToString pronto.", "record Item(string Nome, int Preco);"),
        ["enum"] = D("Lista de opções com nome.", "enum Direction { Up, Down, Left, Right }"),
        ["interface"] = D("Contrato: lista os métodos que uma classe promete ter, sem dizer como."),
        ["new"] = D("Cria um objeto novo (ou um array) na memória.", "var hero = new Hero();"),
        ["this"] = D("O próprio objeto em que o código está rodando.", "this.health = health;"),
        ["base"] = D("A classe da qual esta herda: acessa a versão original de um método."),
        ["static"] = D("Pertence à classe, não a um objeto: usa-se direto pelo nome da classe, sem new.", "Math.Max(1, 2) // Max é static"),
        ["public"] = D("Pode ser usado de qualquer lugar do programa."),
        ["private"] = D("Só pode ser usado dentro da própria classe. É o padrão quando não se escreve nada."),
        ["protected"] = D("Pode ser usado na própria classe e nas que herdam dela."),
        ["internal"] = D("Pode ser usado em qualquer lugar deste projeto."),
        ["const"] = D("Valor fixo, que nunca muda e é conhecido ao compilar.", "const int MaxHealth = 100;"),
        ["readonly"] = D("Só pode receber valor na declaração ou no construtor; depois não muda mais."),
        ["override"] = D("Substitui um método virtual ou abstract da classe base."),
        ["virtual"] = D("Permite que classes filhas substituam este método com override."),
        ["abstract"] = D("Incompleto de propósito: a classe não pode ser criada com new, e o método precisa ser escrito nas classes filhas."),
        ["namespace"] = D("Agrupa classes com um nome, como uma pasta.", "namespace MyGame;"),
        ["using"] = D("No topo do arquivo: permite usar as classes de um namespace sem escrever o nome inteiro.", "using System.Text;"),

        // Parâmetros, erros e outros
        ["out"] = D("Parâmetro que o método preenche e devolve para você.", "if (int.TryParse(text, out int number)) { ... }"),
        ["ref"] = D("Passa a própria variável (não uma cópia): o método pode mudá-la."),
        ["params"] = D("Aceita qualquer quantidade de argumentos, recebidos como array."),
        ["try"] = D("Tenta executar o bloco; se der erro, o catch trata.", "try\n{\n    int n = int.Parse(text);\n}\ncatch (FormatException)\n{\n    Console.WriteLine(\"Não é número\");\n}"),
        ["catch"] = D("Trata o erro que aconteceu dentro do try."),
        ["finally"] = D("Executa sempre no fim do try, dando erro ou não."),
        ["throw"] = D("Gera um erro de propósito, que para o programa se ninguém tratar.", "throw new ArgumentException(\"Valor inválido\");"),
        ["is"] = D("Testa se um valor é de um tipo (ou combina com um padrão).", "if (enemy is Dragon dragon) { ... }"),
        ["as"] = D("Converte para um tipo; se não der, o resultado é null em vez de erro."),
        ["async"] = D("Marca um método que usa await (espera algo terminar sem travar o programa)."),
        ["await"] = D("Espera uma tarefa terminar antes de continuar.", "await Task.Delay(1000);"),
        ["nameof"] = D("O nome de uma variável ou método, como texto.", "nameof(health) // \"health\""),
    };

    private static readonly Dictionary<string, PortugueseDoc> Members = Build();

    private static Dictionary<string, PortugueseDoc> Build()
    {
        var m = new Dictionary<string, PortugueseDoc>
        {
            // Console
            ["System.Console"] = D("O terminal: escrever texto, ler o que a pessoa digita, cores e posição do cursor."),
            ["System.Console.WriteLine"] = D("Escreve no terminal e pula para a linha de baixo.", "Console.WriteLine($\"Vida: {health}\");"),
            ["System.Console.Write"] = D("Escreve no terminal e continua na mesma linha.", "Console.Write(\"Seu nome: \");"),
            ["System.Console.ReadLine"] = D("Espera a pessoa digitar e apertar Enter; devolve o texto digitado (string). Pode devolver null se a entrada acabar.", "string name = Console.ReadLine() ?? \"\";"),
            ["System.Console.ReadKey"] = D("Espera uma tecla (sem precisar de Enter). Use true para não mostrar a tecla.", "var key = Console.ReadKey(true).Key;\nif (key == ConsoleKey.UpArrow) { ... }"),
            ["System.Console.Clear"] = D("Limpa o terminal."),
            ["System.Console.SetCursorPosition"] = D("Move o cursor para a coluna e linha indicadas (começando em 0). Ótimo para desenhar jogos.", "Console.SetCursorPosition(x, y);\nConsole.Write(\"@\");"),
            ["System.Console.ForegroundColor"] = D("Cor do texto escrito daqui para frente.", "Console.ForegroundColor = ConsoleColor.Green;"),
            ["System.Console.BackgroundColor"] = D("Cor do fundo do texto escrito daqui para frente."),
            ["System.Console.ResetColor"] = D("Volta as cores do terminal ao normal."),
            ["System.Console.KeyAvailable"] = D("True se alguma tecla foi apertada e ainda não foi lida. Permite jogos que não ficam parados esperando.", "if (Console.KeyAvailable)\n{\n    var key = Console.ReadKey(true).Key;\n}"),
            ["System.Console.CursorVisible"] = D("Mostra ou esconde o cursor piscando (esconder deixa jogos mais bonitos)."),
            ["System.Console.Title"] = D("Título da janela do terminal."),
            ["System.ConsoleColor"] = D("As cores disponíveis no terminal (Red, Green, Yellow…)."),
            ["System.ConsoleKey"] = D("As teclas do teclado (Enter, Escape, UpArrow, A…), usadas com Console.ReadKey()."),

            // Texto
            ["System.String"] = D("Texto. Depois de criado não muda: métodos como ToUpper devolvem um texto novo."),
            ["System.String.Length"] = D("Quantos caracteres o texto tem.", "if (password.Length < 6) { ... }"),
            ["System.String.ToUpper"] = D("Devolve o texto em MAIÚSCULAS."),
            ["System.String.ToLower"] = D("Devolve o texto em minúsculas. Útil para comparar sem ligar para maiúsculas.", "if (answer.ToLower() == \"sim\") { ... }"),
            ["System.String.Trim"] = D("Devolve o texto sem os espaços do começo e do fim."),
            ["System.String.Split"] = D("Divide o texto em partes, num array.", "string[] parts = \"a,b,c\".Split(',');"),
            ["System.String.Contains"] = D("True se o texto contém o trecho procurado."),
            ["System.String.Replace"] = D("Devolve o texto com um trecho trocado por outro."),
            ["System.String.Substring"] = D("Pega um pedaço do texto: a partir de uma posição (começa em 0), com um tamanho.", "\"Aventura\".Substring(0, 3) // \"Ave\""),
            ["System.String.IndexOf"] = D("Posição onde o trecho aparece (começando em 0), ou -1 se não aparece."),
            ["System.String.StartsWith"] = D("True se o texto começa com o trecho."),
            ["System.String.EndsWith"] = D("True se o texto termina com o trecho."),
            ["System.String.IsNullOrEmpty"] = D("True se o texto é null ou vazio (\"\")."),
            ["System.String.IsNullOrWhiteSpace"] = D("True se o texto é null, vazio ou só tem espaços. Bom para validar o que foi digitado."),
            ["System.String.Join"] = D("Junta os itens de uma lista num texto só, com um separador.", "string.Join(\", \", inventory)"),
            ["System.String.Equals"] = D("Compara dois textos. Com StringComparison.OrdinalIgnoreCase, ignora maiúsculas."),
            ["System.Text.StringBuilder"] = D("Monta textos grandes pedaço por pedaço, sem criar um texto novo a cada passo."),
            ["System.Text.StringBuilder.Append"] = D("Acrescenta um pedaço ao fim do texto."),
            ["System.Text.StringBuilder.AppendLine"] = D("Acrescenta um pedaço e uma quebra de linha."),

            // Matemática e sorteio
            ["System.Math"] = D("Funções de matemática: Max, Min, Abs, Round, Pow, Sqrt…"),
            ["System.Math.Max"] = D("O maior dos dois valores.", "int damage = Math.Max(0, attack - defense);"),
            ["System.Math.Min"] = D("O menor dos dois valores.", "health = Math.Min(health + heal, maxHealth);"),
            ["System.Math.Abs"] = D("O valor sem sinal (distância até o zero): Math.Abs(-5) é 5."),
            ["System.Math.Round"] = D("Arredonda. O segundo argumento diz quantas casas decimais manter.", "Math.Round(3.14159, 2) // 3.14"),
            ["System.Math.Floor"] = D("Arredonda para baixo: 2.9 vira 2."),
            ["System.Math.Ceiling"] = D("Arredonda para cima: 2.1 vira 3."),
            ["System.Math.Pow"] = D("Potência: Math.Pow(2, 3) é 2³ = 8."),
            ["System.Math.Sqrt"] = D("Raiz quadrada."),
            ["System.Math.Clamp"] = D("Prende o valor entre um mínimo e um máximo.", "health = Math.Clamp(health, 0, 100);"),
            ["System.Math.PI"] = D("O número π (3,14159…)."),
            ["System.Random"] = D("Sorteia números.", "var random = new Random();\nint dice = random.Next(1, 7); // 1 a 6"),
            ["System.Random.Next"] = D("Número inteiro sorteado. Next(min, max) inclui o min e NÃO inclui o max.", "int dice = random.Next(1, 7); // 1 a 6"),
            ["System.Random.NextDouble"] = D("Número sorteado entre 0.0 e 1.0 (sem incluir o 1).", "bool critical = random.NextDouble() < 0.2; // 20%"),
            ["System.Random.Shared"] = D("Um Random pronto para usar, sem precisar de new.", "int n = Random.Shared.Next(1, 11);"),

            // Coleções
            ["System.Collections.Generic.List"] = D("Lista que cresce e diminui. As posições começam em 0.", "var inventory = new List<string>();\ninventory.Add(\"Espada\");"),
            ["System.Collections.Generic.List.Add"] = D("Coloca um item no fim da lista."),
            ["System.Collections.Generic.List.Remove"] = D("Tira o primeiro item igual ao informado. Devolve false se não achou."),
            ["System.Collections.Generic.List.RemoveAt"] = D("Tira o item da posição informada (começando em 0)."),
            ["System.Collections.Generic.List.Insert"] = D("Coloca um item numa posição, empurrando os outros."),
            ["System.Collections.Generic.List.Count"] = D("Quantos itens a lista tem.", "for (int i = 0; i < list.Count; i++) { ... }"),
            ["System.Collections.Generic.List.Contains"] = D("True se a lista tem o item."),
            ["System.Collections.Generic.List.IndexOf"] = D("Posição do item na lista, ou -1 se não estiver."),
            ["System.Collections.Generic.List.Clear"] = D("Tira todos os itens."),
            ["System.Collections.Generic.List.Sort"] = D("Ordena a própria lista (do menor para o maior)."),
            ["System.Collections.Generic.Dictionary"] = D("Guarda pares chave → valor, para achar um valor pela chave.", "var prices = new Dictionary<string, int>();\nprices[\"Espada\"] = 50;"),
            ["System.Collections.Generic.Dictionary.Add"] = D("Guarda um par chave → valor. Dá erro se a chave já existir."),
            ["System.Collections.Generic.Dictionary.ContainsKey"] = D("True se a chave existe."),
            ["System.Collections.Generic.Dictionary.TryGetValue"] = D("Procura a chave sem dar erro: devolve true e o valor, ou false se não existir.", "if (prices.TryGetValue(\"Espada\", out int price)) { ... }"),
            ["System.Collections.Generic.Dictionary.Remove"] = D("Tira a chave e o valor dela."),
            ["System.Collections.Generic.Dictionary.Count"] = D("Quantos pares existem."),
            ["System.Collections.Generic.Dictionary.Keys"] = D("Todas as chaves."),
            ["System.Collections.Generic.Dictionary.Values"] = D("Todos os valores."),
            ["System.Array.Length"] = D("Quantos itens o array tem. As posições vão de 0 até Length - 1."),

            // LINQ
            ["System.Linq.Enumerable.Where"] = D("Filtra: só os itens que passam na condição.", "var alive = enemies.Where(e => e.Health > 0);"),
            ["System.Linq.Enumerable.Select"] = D("Transforma cada item em outra coisa.", "var names = enemies.Select(e => e.Name);"),
            ["System.Linq.Enumerable.OrderBy"] = D("Ordena do menor para o maior pelo valor escolhido.", "var ranking = players.OrderBy(p => p.Time);"),
            ["System.Linq.Enumerable.OrderByDescending"] = D("Ordena do maior para o menor pelo valor escolhido."),
            ["System.Linq.Enumerable.First"] = D("O primeiro item (que passa na condição). Dá erro se não houver nenhum."),
            ["System.Linq.Enumerable.FirstOrDefault"] = D("O primeiro item, ou o valor padrão (null, 0) se não houver nenhum."),
            ["System.Linq.Enumerable.Any"] = D("True se existe pelo menos um item (que passa na condição)."),
            ["System.Linq.Enumerable.All"] = D("True se todos os itens passam na condição."),
            ["System.Linq.Enumerable.Count"] = D("Quantos itens (que passam na condição) existem."),
            ["System.Linq.Enumerable.Sum"] = D("A soma dos valores."),
            ["System.Linq.Enumerable.Max"] = D("O maior valor. Dá erro se a lista estiver vazia."),
            ["System.Linq.Enumerable.Min"] = D("O menor valor. Dá erro se a lista estiver vazia."),
            ["System.Linq.Enumerable.Average"] = D("A média dos valores. Dá erro se a lista estiver vazia."),
            ["System.Linq.Enumerable.ToList"] = D("Copia os itens para uma List nova."),
            ["System.Linq.Enumerable.ToArray"] = D("Copia os itens para um array novo."),

            // Conversões, tempo, arquivos
            ["System.Convert.ToInt32"] = D("Converte para int. Com texto, dá erro se não for um número.", "int n = Convert.ToInt32(\"42\");"),
            ["System.Convert.ToDouble"] = D("Converte para double."),
            ["System.Threading.Thread.Sleep"] = D("Pausa o programa pelos milissegundos indicados (1000 = 1 segundo). Controla a velocidade de um jogo.", "Thread.Sleep(100);"),
            ["System.Threading.Tasks.Task.Delay"] = D("Espera um tempo sem travar (use com await).", "await Task.Delay(500);"),
            ["System.DateTime.Now"] = D("A data e a hora de agora."),
            ["System.Environment.Exit"] = D("Encerra o programa na hora, com o código de saída informado (0 = tudo certo)."),
            ["System.IO.File.ReadAllText"] = D("Lê o arquivo inteiro como um texto. Caminhos sem C:\\ começam na pasta do projeto.", "string save = File.ReadAllText(\"save.txt\");"),
            ["System.IO.File.ReadAllLines"] = D("Lê o arquivo como um array de linhas."),
            ["System.IO.File.WriteAllText"] = D("Grava o texto no arquivo, substituindo o que havia.", "File.WriteAllText(\"save.txt\", $\"{health};{gold}\");"),
            ["System.IO.File.AppendAllText"] = D("Acrescenta o texto ao fim do arquivo (cria se não existir)."),
            ["System.IO.File.Exists"] = D("True se o arquivo existe."),
        };

        // Conversões de texto para número: o mesmo para int, double, decimal…
        foreach (var (type, name, example) in new[]
                 {
                     ("System.Int32", "int", "int"), ("System.Int64", "long", "long"), ("System.Double", "double", "double"),
                     ("System.Single", "float", "float"), ("System.Decimal", "decimal", "decimal"),
                 })
        {
            m[$"{type}.Parse"] = D($"Converte texto em {name}. Dá erro se o texto não for um número; para conferir antes, use {name}.TryParse.",
                $"{example} n = {name}.Parse(\"42\");");
            m[$"{type}.TryParse"] = D($"Tenta converter texto em {name} sem dar erro: devolve true se deu certo e o número vem no out.",
                $"if ({name}.TryParse(text, out {example} n))\n{{\n    // n tem o número\n}}\nelse\n{{\n    Console.WriteLine(\"Digite um número.\");\n}}");
            m[$"{type}.MaxValue"] = D($"O maior valor que um {name} consegue guardar.");
            m[$"{type}.MinValue"] = D($"O menor valor que um {name} consegue guardar.");
        }
        m["System.Object.ToString"] = D("Transforma o valor em texto.");
        AddGameDocs(m);
        return m;
    }

    /// <summary>O motor dos jogos com botões: cada nome em inglês simples, com a tradução na frente.</summary>
    private static void AddGameDocs(Dictionary<string, PortugueseDoc> m)
    {
        const string game = "CSharpLab.GameEngine.Game";
        m[game] = D("Game = jogo. A janela do jogo, com cenas, textos, barras e botões.", "var game = new Game(\"A Torre\");");
        m[game + ".Scene"] = D("Scene = cena. Cria uma tela do jogo. Ela é desenhada de novo depois de cada clique, sempre com os valores atuais das variáveis.",
            "game.Scene(\"Forest\", () =>\n{\n    game.Say(\"Árvores por todo lado.\");\n    game.Button(\"Voltar\", () => game.GoTo(\"Start\"));\n});");
        m[game + ".GoTo"] = D("GoTo = ir para. Troca para outra cena.", "game.GoTo(\"Forest\");");
        m[game + ".Title"] = D("Title = título. Texto grande no alto da cena.", "game.Title(\"Capítulo 1\");");
        m[game + ".Say"] = D("Say = dizer. Mostra um texto na tela. Dentro de um botão, aparece destacado depois do clique.",
            "game.Say($\"Ouro: {gold}\", GameColor.Gold);");
        m[game + ".Button"] = D("Button = botão. O código entre as chaves roda quando o jogador clica. As teclas 1 a 9 também apertam os botões.",
            "game.Button(\"Atacar\", () =>\n{\n    enemyHealth -= 10;\n    game.Say(\"Você acertou!\");\n});");
        m[game + ".Bar"] = D("Bar = barra. Mostra uma barra de vida, mana ou energia, à direita da tela.", "game.Bar(\"Vida\", health, 100, GameColor.Green);");
        m[game + ".Ask"] = D("Ask = perguntar. Mostra uma pergunta com um campo para o jogador escrever; a resposta chega entre as chaves.",
            "game.Ask(\"Qual é o seu nome?\", answer =>\n{\n    playerName = answer;\n    game.GoTo(\"Start\");\n});");
        m[game + ".Image"] = D("Image = imagem. Mostra uma imagem (png ou jpg) da pasta Assets do projeto.", "game.Image(\"goblin.png\");");
        m[game + ".Run"] = D("Run = começar. Abre a janela do jogo na primeira cena. Fica sempre na última linha.", "game.Run(\"Start\");");
        m[game + ".CurrentScene"] = D("CurrentScene = cena atual. O nome da cena que está na tela.");
        m["CSharpLab.GameEngine.GameColor"] = D("GameColor = cor do jogo: White, Gray, Red, Green, Blue, Gold, Purple, Orange.", "game.Say(\"Cuidado!\", GameColor.Red);");
        m["CSharpLab.GameEngine.GameException"] = D("Erro do motor do jogo. A mensagem explica em português o que fazer.");
    }
}
