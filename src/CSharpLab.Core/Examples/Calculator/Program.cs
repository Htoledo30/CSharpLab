// CALCULADORA
// Um menu que repete até a pessoa escolher sair.
// Aprenda aqui: métodos (funções), switch, double.TryParse e return.
// Experimente: adicionar potência (Math.Pow) ou raiz quadrada (Math.Sqrt) ao menu.

while (true)
{
    Console.WriteLine();
    Console.WriteLine("1) Somar   2) Subtrair   3) Multiplicar   4) Dividir   0) Sair");
    Console.Write("Escolha: ");
    string option = Console.ReadLine() ?? "";

    if (option == "0")
    {
        Console.WriteLine("Até mais!");
        break;
    }

    double a = ReadNumber("Primeiro número: ");
    double b = ReadNumber("Segundo número: ");

    // O switch escolhe o caminho conforme o texto digitado.
    switch (option)
    {
        case "1":
            Console.WriteLine($"Resultado: {a + b}");
            break;
        case "2":
            Console.WriteLine($"Resultado: {a - b}");
            break;
        case "3":
            Console.WriteLine($"Resultado: {a * b}");
            break;
        case "4":
            if (b == 0)
                Console.WriteLine("Não dá para dividir por zero.");
            else
                Console.WriteLine($"Resultado: {a / b}");
            break;
        default:
            Console.WriteLine("Opção inválida.");
            break;
    }
}

// Um método: um pedaço de código com nome, que pode ser usado várias vezes.
// Este pergunta até a pessoa digitar um número válido e devolve (return) esse número.
static double ReadNumber(string message)
{
    while (true)
    {
        Console.Write(message);
        if (double.TryParse(Console.ReadLine(), out double number))
            return number;
        Console.WriteLine("Isso não é um número. Use vírgula ou ponto conforme o seu Windows (ex.: 2,5).");
    }
}
