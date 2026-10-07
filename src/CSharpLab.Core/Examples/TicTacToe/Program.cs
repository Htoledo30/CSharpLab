// JOGO DA VELHA
// Dois jogadores (X e O) no mesmo teclado.
// Aprenda aqui: array de duas dimensões (char[,]), for dentro de for, métodos que devolvem bool.
// Experimente: fazer o computador jogar como O, escolhendo uma casa livre com Random.

char[,] board = new char[3, 3];     // 3 linhas x 3 colunas
for (int row = 0; row < 3; row++)
    for (int col = 0; col < 3; col++)
        board[row, col] = ' ';

char player = 'X';

for (int turn = 1; turn <= 9; turn++)
{
    Draw(board);
    Console.Write($"Vez do {player}. Digite a casa (1 a 9): ");

    // As casas vão de 1 a 9; a conta abaixo transforma em linha e coluna (que começam em 0).
    if (!int.TryParse(Console.ReadLine(), out int cell) || cell < 1 || cell > 9)
    {
        Console.WriteLine("Casa inválida.");
        turn--;     // esta jogada não conta
        continue;
    }
    int r = (cell - 1) / 3;
    int c = (cell - 1) % 3;     // % é o resto da divisão
    if (board[r, c] != ' ')
    {
        Console.WriteLine("Essa casa já está ocupada.");
        turn--;
        continue;
    }

    board[r, c] = player;
    if (HasWon(board, player))
    {
        Draw(board);
        Console.WriteLine($"{player} venceu!");
        return;     // termina o programa
    }
    player = player == 'X' ? 'O' : 'X';     // troca de jogador
}

Draw(board);
Console.WriteLine("Deu velha (empate)!");

static void Draw(char[,] board)
{
    Console.Clear();
    for (int row = 0; row < 3; row++)
    {
        Console.WriteLine($" {Show(board, row, 0)} | {Show(board, row, 1)} | {Show(board, row, 2)}");
        if (row < 2) Console.WriteLine("---+---+---");
    }
    Console.WriteLine();
}

// Casa vazia mostra o número dela, para ajudar a escolher.
static char Show(char[,] board, int row, int col) =>
    board[row, col] == ' ' ? (char)('1' + row * 3 + col) : board[row, col];

static bool HasWon(char[,] b, char p)
{
    for (int i = 0; i < 3; i++)
    {
        if (b[i, 0] == p && b[i, 1] == p && b[i, 2] == p) return true;   // linha
        if (b[0, i] == p && b[1, i] == p && b[2, i] == p) return true;   // coluna
    }
    return (b[0, 0] == p && b[1, 1] == p && b[2, 2] == p) ||              // diagonais
           (b[0, 2] == p && b[1, 1] == p && b[2, 0] == p);
}
