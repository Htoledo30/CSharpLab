// ADIVINHE O NÚMERO
// O computador sorteia um número de 1 a 100 e você tenta adivinhar.
// Aprenda aqui: Random, while, if/else, int.TryParse e variáveis contadoras.
// Experimente mudar: o limite do sorteio, o número de tentativas, as mensagens.

var random = new Random();
int secret = random.Next(1, 101);   // Next(1, 101) sorteia de 1 até 100 (o 101 não entra)
int attempts = 0;

Console.WriteLine("Pensei num número de 1 a 100. Tente adivinhar!");

while (true)
{
    Console.Write("Seu palpite: ");
    string input = Console.ReadLine() ?? "";

    // TryParse não para o programa se a pessoa digitar letras: só devolve false.
    if (!int.TryParse(input, out int guess))
    {
        Console.WriteLine("Digite um número, por favor.");
        continue;   // volta para o começo do while
    }

    attempts++;     // o mesmo que attempts = attempts + 1

    if (guess < secret)
    {
        Console.WriteLine("Mais alto!");
    }
    else if (guess > secret)
    {
        Console.WriteLine("Mais baixo!");
    }
    else
    {
        Console.WriteLine($"Acertou! O número era {secret}. Você usou {attempts} tentativas.");
        break;      // sai do while: o jogo acabou
    }
}
