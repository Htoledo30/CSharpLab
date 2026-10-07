// COBRINHA
// Jogo em tempo real: use as setas para guiar a cobra até a comida (*). Esc sai.
// Aprenda aqui: o "loop do jogo", Console.KeyAvailable (ler tecla sem parar o jogo),
// Console.SetCursorPosition (desenhar em qualquer lugar), List e Thread.Sleep.
// Dica: aperte Ctrl+Shift+J para o terminal ocupar quase a tela toda.
// Experimente: mudar a velocidade, o tamanho do campo ou os símbolos.

const int Width = 30;
const int Height = 15;

var snake = new List<(int X, int Y)> { (10, 7), (9, 7), (8, 7) };  // a cabeça é o primeiro item
(int X, int Y) direction = (1, 0);                                // começando para a direita
var random = new Random();
var food = (X: random.Next(1, Width - 1), Y: random.Next(1, Height - 1));
int score = 0;

Console.CursorVisible = false;
Console.Clear();
DrawBorder();

while (true)
{
    // 1) Ler o teclado, se alguma tecla foi apertada (sem esperar).
    if (Console.KeyAvailable)
    {
        var key = Console.ReadKey(true).Key;
        if (key == ConsoleKey.Escape) break;
        // A cobra não pode dar meia-volta de uma vez.
        if (key == ConsoleKey.UpArrow && direction.Y == 0) direction = (0, -1);
        if (key == ConsoleKey.DownArrow && direction.Y == 0) direction = (0, 1);
        if (key == ConsoleKey.LeftArrow && direction.X == 0) direction = (-1, 0);
        if (key == ConsoleKey.RightArrow && direction.X == 0) direction = (1, 0);
    }

    // 2) Mover: a nova cabeça é a cabeça atual + a direção.
    var head = (X: snake[0].X + direction.X, Y: snake[0].Y + direction.Y);

    // 3) Bateu na parede ou nela mesma? Fim de jogo.
    if (head.X <= 0 || head.X >= Width - 1 || head.Y <= 0 || head.Y >= Height - 1 || snake.Contains(head))
        break;

    snake.Insert(0, head);
    if (head == food)
    {
        score++;
        food = (random.Next(1, Width - 1), random.Next(1, Height - 1));   // não remove o rabo: a cobra cresce
    }
    else
    {
        var tail = snake[^1];               // ^1 = o último item
        snake.RemoveAt(snake.Count - 1);
        Draw(tail.X, tail.Y, ' ');          // apaga o rabo da tela
    }

    // 4) Desenhar só o que mudou.
    Draw(food.X, food.Y, '*', ConsoleColor.Red);
    Draw(head.X, head.Y, 'O', ConsoleColor.Green);
    Console.SetCursorPosition(0, Height);
    Console.Write($"Pontos: {score}");

    // 5) Esperar um pouco: é isso que controla a velocidade do jogo.
    Thread.Sleep(120);
}

Console.SetCursorPosition(0, Height + 1);
Console.ResetColor();
Console.CursorVisible = true;
Console.WriteLine($"Fim de jogo! Você fez {score} pontos.");

static void Draw(int x, int y, char symbol, ConsoleColor color = ConsoleColor.Gray)
{
    Console.SetCursorPosition(x, y);
    Console.ForegroundColor = color;
    Console.Write(symbol);
    Console.ResetColor();
}

static void DrawBorder()
{
    for (int x = 0; x < Width; x++)
    {
        Draw(x, 0, '#', ConsoleColor.DarkGray);
        Draw(x, Height - 1, '#', ConsoleColor.DarkGray);
    }
    for (int y = 0; y < Height; y++)
    {
        Draw(0, y, '#', ConsoleColor.DarkGray);
        Draw(Width - 1, y, '#', ConsoleColor.DarkGray);
    }
}
