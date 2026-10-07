namespace CSharpLab.GameEngine;

/// <summary>
/// Item = peça. Uma peça desenhada na aba Tela (texto, botão, barra, imagem…), pega pelo nome com game.Find.
/// Mude as propriedades dela com =.
/// </summary>
/// <example>
/// <code>
/// game.Find("Story").Text = "Um goblin aparece!";
/// game.Find("PlayerHealth").Value = health;
/// game.Find("Potion").Visible = potions > 0;
/// game.Find("Attack").OnClick(() => enemyHealth -= 10);
/// </code>
/// </example>
public sealed class Item
{
    private readonly string _scene;

    internal Item(Piece piece, string scene)
    {
        Piece = piece;
        _scene = scene;
    }

    internal Piece Piece { get; }
    internal Action? Click { get; set; }
    internal Action<string>? Answer { get; set; }

    /// <summary>Name = nome. O nome da peça na aba Tela.</summary>
    public string Name => Piece.Name;

    /// <summary>
    /// Text = texto. O texto de um Texto ou de um Botão, o rótulo de uma Barra ou a pergunta de um Campo de escrita.
    /// </summary>
    /// <example><code>game.Find("Story").Text = $"Ouro: {gold}";</code></example>
    public string Text
    {
        get => Check(nameof(Text)).Text ?? "";
        set => Check(nameof(Text)).Text = value ?? "";
    }

    /// <summary>Value = valor. Quanto a Barra tem agora (vida, mana…).</summary>
    /// <example><code>game.Find("PlayerHealth").Value = health;</code></example>
    public int Value
    {
        get => Check(nameof(Value)).BarValue;
        set => Check(nameof(Value)).Value = value;
    }

    /// <summary>Max = máximo. O valor da Barra cheia.</summary>
    /// <example><code>game.Find("PlayerHealth").Max = maxHealth;</code></example>
    public int Max
    {
        get => Check(nameof(Max)).BarMax;
        set
        {
            var piece = Check(nameof(Max));
            if (value <= 0) throw new GameException($"O Max da barra \"{Name}\" precisa ser maior que 0 (veio {value}).");
            piece.Max = value;
        }
    }

    /// <summary>Visible = visível. false esconde a peça; true mostra de novo.</summary>
    /// <example><code>game.Find("Potion").Visible = potions > 0;</code></example>
    public bool Visible
    {
        get => Piece.Visible;
        set => Piece.Visible = value;
    }

    /// <summary>Color = cor. A cor do texto, do botão, da barra ou da caixa.</summary>
    /// <example><code>game.Find("Story").Color = Color.Red;</code></example>
    public Color Color
    {
        get => Check(nameof(Color)).Color ?? (Piece.Type == PieceType.Bar ? Color.Green : Color.White);
        set => Check(nameof(Color)).Color = value;
    }

    /// <summary>Image = imagem. O arquivo (da pasta Assets) que a peça Imagem mostra.</summary>
    /// <example><code>game.Find("Enemy").Image = "dragon.png";</code></example>
    public string Image
    {
        get => Check(nameof(Image)).Image ?? "";
        set => Check(nameof(Image)).Image = value;
    }

    /// <summary>X = posição da esquerda para a direita (0 até 960).</summary>
    public double X { get => Piece.X; set => Piece.X = value; }

    /// <summary>Y = posição de cima para baixo (0 até 540).</summary>
    public double Y { get => Piece.Y; set => Piece.Y = value; }

    /// <summary>Width = largura.</summary>
    public double Width
    {
        get => Piece.Width;
        set => Piece.Width = value > 0 ? value : throw new GameException($"A largura de \"{Name}\" precisa ser maior que 0.");
    }

    /// <summary>Height = altura.</summary>
    public double Height
    {
        get => Piece.Height;
        set => Piece.Height = value > 0 ? value : throw new GameException($"A altura de \"{Name}\" precisa ser maior que 0.");
    }

    /// <summary>
    /// OnClick = ao clicar. O código entre as chaves roda quando o jogador clica no Botão (ou na Imagem).
    /// </summary>
    /// <example>
    /// <code>
    /// game.Find("Attack").OnClick(() =>
    /// {
    ///     enemyHealth -= 10;
    ///     game.Write("Você acertou!");
    /// });
    /// </code>
    /// </example>
    public void OnClick(Action action)
    {
        if (Piece.Type is not (PieceType.Button or PieceType.Image))
            throw Wrong("OnClick", "só botões e imagens têm OnClick");
        Click = action ?? throw new GameException($"O OnClick de \"{Name}\" precisa dizer o que fazer: game.Find(\"{Name}\").OnClick(() => {{ ... }});");
    }

    /// <summary>
    /// OnAnswer = ao responder. Recebe o que o jogador escreveu no Campo de escrita (quando ele aperta OK ou Enter).
    /// </summary>
    /// <example>
    /// <code>
    /// game.Find("NameField").OnAnswer(answer =>
    /// {
    ///     playerName = answer;
    ///     game.GoTo("Village");
    /// });
    /// </code>
    /// </example>
    public void OnAnswer(Action<string> action)
    {
        if (Piece.Type != PieceType.Input)
            throw Wrong("OnAnswer", "só o Campo de escrita tem OnAnswer");
        Answer = action ?? throw new GameException($"O OnAnswer de \"{Name}\" precisa dizer o que fazer: game.Find(\"{Name}\").OnAnswer(answer => {{ ... }});");
    }

    /// <summary>Mostra "Attack (Botão)".</summary>
    public override string ToString() => $"{Name} ({Piece.Describe(Piece.Type)})";

    private Piece Check(string property)
    {
        if (Piece.Supports(Piece.Type, property)) return Piece;
        throw Wrong(property, property switch
        {
            nameof(Text) => "só Texto, Botão, Barra e Campo de escrita têm Text",
            nameof(Value) or nameof(Max) => $"só barras têm {property}",
            nameof(Image) => "só imagens têm Image",
            nameof(Color) => "Imagem, Campo de escrita e Mensagens não têm Color",
            _ => $"esta peça não tem {property}",
        });
    }

    private GameException Wrong(string property, string rule) =>
        new($"\"{Name}\" (na tela \"{_scene}\") é do tipo {Piece.Describe(Piece.Type)}: {rule}.");
}
