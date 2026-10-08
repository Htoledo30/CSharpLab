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
    private readonly DesignedScene? _owner;
    private List<Card> _cards = [];

    internal Item(Piece piece, string scene, DesignedScene? owner = null)
    {
        Piece = piece;
        _scene = scene;
        _owner = owner;
    }

    internal Piece Piece { get; }
    internal Action? Click { get; set; }
    internal Action<string>? Answer { get; set; }

    /// <summary>Peça de um cartão: o nome da Lista (para as mensagens de erro).</summary>
    internal string? CardOf { get; init; }

    /// <summary>Os cartões que o Show criou (só na Lista).</summary>
    internal IReadOnlyList<Card> Cards => _cards;

    /// <summary>Name = nome. O nome da peça na aba Tela.</summary>
    public string Name => Piece.Name;

    /// <summary>
    /// Text = texto. O texto de um Texto ou de um Botão, o rótulo de uma Barra, a pergunta de um Campo de escrita
    /// ou, na Lista, o que aparece quando ela está vazia.
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

    /// <summary>
    /// Enabled = ativo. false deixa o botão (ou o campo de escrita, ou a imagem) apagado e sem clique; true liga de novo.
    /// </summary>
    /// <example><code>game.Find("Tower").Enabled = hasKey;</code></example>
    public bool Enabled
    {
        get => Check(nameof(Enabled)).Enabled;
        set => Check(nameof(Enabled)).Enabled = value;
    }

    /// <summary>Color = cor. A cor do texto, do botão, da barra ou da caixa.</summary>
    /// <example><code>game.Find("Story").Color = Color.Red;</code></example>
    public Color Color
    {
        get => Check(nameof(Color)).Color ?? (Piece.Type == PieceType.Bar ? Color.Green : Color.White);
        set => Check(nameof(Color)).Color = value;
    }

    /// <summary>Shade = tom. A cor mais escura (Shade.Dark) ou mais clara (Shade.Light).</summary>
    /// <example><code>game.Find("BossFrame").Shade = Shade.Dark;</code></example>
    public Shade Shade
    {
        get => Check(nameof(Shade)).Shade ?? Shade.Normal;
        set => Check(nameof(Shade)).Shade = value == Shade.Normal ? null : value;
    }

    /// <summary>Opacity = preenchimento da Caixa, de 0 (invisível) a 100 (cheia).</summary>
    /// <example><code>game.Find("Panel").Opacity = 60;</code></example>
    public int Opacity
    {
        get => Check(nameof(Opacity)).BoxOpacity;
        set
        {
            var piece = Check(nameof(Opacity));
            if (value is < 0 or > 100)
                throw new GameException($"O Opacity da caixa \"{Name}\" vai de 0 (invisível) a 100 (cheia). Veio {value}.");
            piece.Opacity = value;
        }
    }

    /// <summary>Border = borda. true mostra a borda da Caixa; false tira.</summary>
    /// <example><code>game.Find("Panel").Border = true;</code></example>
    public bool Border
    {
        get => Check(nameof(Border)).HasBorder;
        set => Check(nameof(Border)).Border = value;
    }

    /// <summary>Corner = canto. Cantos da Caixa: Corner.Round (redondos), Corner.Square (retos) ou Corner.Circle (círculo).</summary>
    /// <example><code>game.Find("Portrait").Corner = Corner.Circle;</code></example>
    public Corner Corner
    {
        get => Check(nameof(Corner)).Corner ?? Corner.Round;
        set => Check(nameof(Corner)).Corner = value == Corner.Round ? null : value;
    }

    /// <summary>Font = fonte, o desenho das letras: Font.Normal, Font.Fantasy, Font.Book ou Font.Hand.</summary>
    /// <example><code>game.Find("Title").Font = Font.Fantasy;</code></example>
    public Font Font
    {
        get => Check(nameof(Font)).Font ?? Font.Normal;
        set => Check(nameof(Font)).Font = value == Font.Normal ? null : value;
    }

    /// <summary>Bold = negrito. true deixa as letras do Texto mais grossas.</summary>
    public bool Bold
    {
        get => Check(nameof(Bold)).Bold == true;
        set => Check(nameof(Bold)).Bold = value ? true : null;
    }

    /// <summary>Italic = itálico. true deixa as letras do Texto inclinadas.</summary>
    /// <example><code>game.Find("Letter").Italic = true;</code></example>
    public bool Italic
    {
        get => Check(nameof(Italic)).Italic == true;
        set => Check(nameof(Italic)).Italic = value ? true : null;
    }

    /// <summary>Shadow = sombra. true põe uma sombra atrás das letras, para ler bem em cima de qualquer fundo.</summary>
    /// <example><code>game.Find("Title").Shadow = true;</code></example>
    public bool Shadow
    {
        get => Check(nameof(Shadow)).Shadow == true;
        set => Check(nameof(Shadow)).Shadow = value ? true : null;
    }

    /// <summary>Style = estilo do botão: ButtonStyle.Filled (cheio), ButtonStyle.Outline (contorno) ou ButtonStyle.Text (só texto).</summary>
    /// <example><code>game.Find("Back").Style = ButtonStyle.Text;</code></example>
    public ButtonStyle Style
    {
        get => Check(nameof(Style)).Style ?? ButtonStyle.Filled;
        set => Check(nameof(Style)).Style = value == ButtonStyle.Filled ? null : value;
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

    /// <summary>
    /// Show = mostrar. Enche a Lista: um cartão para cada item. O código entre as chaves recebe o cartão
    /// e o item, e diz o que vai em cada peça do cartão. Com 3 itens aparecem 3 cartões; com 7, aparecem 7
    /// (com rolagem, se não couber).
    /// </summary>
    /// <param name="items">A lista de coisas. Exemplo: weapons (uma List&lt;Weapon&gt;).</param>
    /// <param name="fill">O que vai em cada cartão: (card, weapon) => { card.Find("Name").Text = weapon.Name; }</param>
    /// <example>
    /// <code>
    /// game.Find("Weapons").Show(weapons, (card, weapon) =>
    /// {
    ///     card.Find("Name").Text = weapon.Name;
    ///     card.Find("Price").Text = $"💰 {weapon.Price}";
    ///     card.Find("Buy").OnClick(() => Buy(weapon));
    /// });
    /// </code>
    /// </example>
    public void Show<T>(IEnumerable<T> items, Action<Card, T> fill)
    {
        if (Piece.Type != PieceType.List)
            throw Wrong("Show", "só a Lista tem Show");
        if (items == null)
            throw new GameException($"O Show da lista \"{Name}\" recebeu uma lista vazia (null). Crie a lista antes: var weapons = new List<Weapon>();");
        if (fill == null)
            throw new GameException($"O Show da lista \"{Name}\" precisa dizer o que vai em cada cartão: game.Find(\"{Name}\").Show(items, (card, item) => {{ ... }});");
        var owner = _owner ?? throw new GameException($"A lista \"{Name}\" não está numa cena desenhada.");
        if (owner.Layout.MembersOf(Name).Count == 0)
            throw new GameException($"A lista \"{Name}\" ainda não tem cartão modelo. Na aba Tela, ponha as peças (nome, preço, botão…) dentro do primeiro cartão dela.");

        var cards = new List<Card>();
        foreach (var value in items)
        {
            if (cards.Count >= MaxCards)
                throw new GameException($"A lista \"{Name}\" recebeu mais de {MaxCards} itens. Mostre menos de uma vez (por exemplo, só os da página atual).");
            var card = owner.NewCard(this, cards.Count);
            fill(card, value);
            cards.Add(card);
        }
        owner.ReplaceCards(_cards, cards);
        _cards = cards;
    }

    private const int MaxCards = 300;

    /// <summary>Shake = tremer. A peça treme um pouquinho (bom para quem levou um golpe).</summary>
    /// <example><code>game.Find("EnemyIcon").Shake();</code></example>
    public void Shake() => AddEffect(Effect.Shake);

    /// <summary>Flash = piscar. A peça pisca rapidinho (bom para dano, cura ou algo que mudou).</summary>
    /// <example><code>game.Find("PlayerHealth").Flash();</code></example>
    public void Flash() => AddEffect(Effect.Flash);

    private void AddEffect(Effect effect)
    {
        if (_owner == null) throw new GameException($"\"{Name}\" não está numa cena desenhada.");
        _owner.AddEffect(Piece, effect);
    }

    /// <summary>Mostra "Attack (Botão)".</summary>
    public override string ToString() => $"{Name} ({Piece.Describe(Piece.Type)})";

    private Piece Check(string property)
    {
        if (Piece.Supports(Piece.Type, property)) return Piece;
        throw Wrong(property, property switch
        {
            nameof(Text) => "só Texto, Botão, Barra, Campo de escrita e Lista têm Text",
            nameof(Value) or nameof(Max) => $"só barras têm {property}",
            nameof(Image) => "só imagens têm Image",
            nameof(Color) or nameof(Shade) => $"Imagem, Campo de escrita, Mensagens e Lista não têm {property}",
            nameof(Opacity) or nameof(Border) or nameof(Corner) => $"só a Caixa tem {property}",
            nameof(Bold) or nameof(Italic) or nameof(Shadow) => $"só o Texto tem {property}",
            nameof(Font) => "só Texto, Botão, Campo de escrita e Mensagens têm Font",
            nameof(Style) => "só botões têm Style",
            nameof(Enabled) => "só botões, campos de escrita e imagens têm Enabled",
            _ => $"esta peça não tem {property}",
        });
    }

    private GameException Wrong(string property, string rule) => CardOf != null
        ? new($"\"{Name}\" (no cartão da lista \"{CardOf}\") é do tipo {Piece.Describe(Piece.Type)}: {rule}.")
        : new($"\"{Name}\" (na tela \"{_scene}\") é do tipo {Piece.Describe(Piece.Type)}: {rule}.");
}
