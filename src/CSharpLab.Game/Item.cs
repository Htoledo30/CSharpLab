namespace CSharpLab.GameEngine;

/// <summary>
/// Item = peça. Uma peça desenhada no Estúdio (texto, botão, barra, imagem…), pega pelo nome com game.Find.
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
    internal Action<string>? Choose { get; set; }
    internal Action<int>? Change { get; set; }
    internal Action<int, int>? CellClick { get; set; }

    /// <summary>Peça de um cartão: o nome da Lista (para as mensagens de erro).</summary>
    internal string? CardOf { get; init; }

    /// <summary>Os cartões que o Show criou (só na Lista).</summary>
    internal IReadOnlyList<Card> Cards => _cards;

    /// <summary>Name = nome. O nome da peça no Estúdio.</summary>
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

    /// <summary>
    /// Value = valor. Quanto a Barra tem agora (vida, mana…), ou o número do Contador (ele fica sempre entre o Min
    /// e o Max).
    /// </summary>
    /// <example><code>game.Find("PlayerHealth").Value = health;</code></example>
    public int Value
    {
        get
        {
            var piece = Check(nameof(Value));
            return piece.Type == PieceType.Counter ? piece.CounterValue : piece.BarValue;
        }
        set
        {
            var piece = Check(nameof(Value));
            piece.Value = piece.Type == PieceType.Counter ? Math.Clamp(value, piece.CounterMin, piece.CounterMax) : value;
        }
    }

    /// <summary>Max = máximo. O valor da Barra cheia, ou o maior número do Contador.</summary>
    /// <example><code>game.Find("PlayerHealth").Max = maxHealth;</code></example>
    public int Max
    {
        get
        {
            var piece = Check(nameof(Max));
            return piece.Type == PieceType.Counter ? piece.CounterMax : piece.BarMax;
        }
        set
        {
            var piece = Check(nameof(Max));
            if (piece.Type == PieceType.Counter)
            {
                if (value < piece.CounterMin)
                    throw new GameException($"O Max do contador \"{Name}\" ({value}) precisa ser maior ou igual ao Min ({piece.CounterMin}).");
                piece.Max = value;
                piece.Value = piece.CounterValue;
                return;
            }
            if (value <= 0) throw new GameException($"O Max da barra \"{Name}\" precisa ser maior que 0 (veio {value}).");
            piece.Max = value;
        }
    }

    /// <summary>Min = mínimo. O menor número do Contador (o − para nele).</summary>
    /// <example><code>game.Find("Amount").Min = 1;</code></example>
    public int Min
    {
        get => Check(nameof(Min)).CounterMin;
        set
        {
            var piece = Check(nameof(Min));
            if (value > piece.CounterMax)
                throw new GameException($"O Min do contador \"{Name}\" ({value}) precisa ser menor ou igual ao Max ({piece.CounterMax}).");
            piece.Min = value;
            piece.Value = piece.CounterValue;
        }
    }

    /// <summary>Step = passo. De quanto em quanto o Contador anda a cada clique no − ou no + (o normal é 1).</summary>
    /// <example><code>game.Find("Bet").Step = 10;</code></example>
    public int Step
    {
        get => Check(nameof(Step)).CounterStep;
        set
        {
            var piece = Check(nameof(Step));
            if (value <= 0) throw new GameException($"O Step do contador \"{Name}\" precisa ser maior que 0 (veio {value}).");
            piece.Step = value;
        }
    }

    /// <summary>
    /// Options = opções. As opções da Escolha (um botão para cada uma) ou as fatias da Roleta. Para uma fatia sair
    /// mais vezes na Roleta, repita ela.
    /// </summary>
    /// <example><code>game.Find("Class").Options = ["Guerreiro", "Mago", "Arqueiro"];</code></example>
    public string[] Options
    {
        get => [.. Check(nameof(Options)).OptionList];
        set
        {
            var piece = Check(nameof(Options));
            var options = (value ?? []).Select(o => o?.Trim() ?? "").ToList();
            if (options.Any(o => o.Length == 0))
                throw new GameException($"As opções de \"{Name}\" não podem ter texto vazio.");
            if (options.Count > ScreenFile.MaxOptions)
                throw new GameException($"\"{Name}\" pode ter até {ScreenFile.MaxOptions} opções (vieram {options.Count}).");
            piece.Options = options;
            if (piece.SelectedOption == null) piece.Selected = null;
        }
    }

    /// <summary>
    /// Selected = escolhida. Na Escolha, a opção que o jogador escolheu ("" se nenhuma); na Roleta, onde a seta parou.
    /// Mude pelo código para já começar com uma escolhida.
    /// </summary>
    /// <example>
    /// <code>
    /// if (game.Find("Difficulty").Selected == "Difícil") enemyHealth *= 2;
    /// </code>
    /// </example>
    public string Selected
    {
        get => Check(nameof(Selected)).SelectedOption ?? "";
        set
        {
            var piece = Check(nameof(Selected));
            if (string.IsNullOrEmpty(value))
            {
                piece.Selected = null;
                return;
            }
            piece.Selected = piece.OptionList.FirstOrDefault(o => string.Equals(o, value.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new GameException($"\"{value}\" não é uma opção de \"{Name}\". Opções: {string.Join(", ", piece.OptionList.Select(o => $"\"{o}\""))}.");
        }
    }

    /// <summary>Columns = colunas. Na Grade, quantas casas por linha (1 a 20); na Escolha, quantas opções por linha.</summary>
    /// <example><code>game.Find("Board").Columns = 4;</code></example>
    public int Columns
    {
        get
        {
            var piece = Check(nameof(Columns));
            return piece.Type == PieceType.Grid ? piece.GridColumns : piece.ChoiceColumns;
        }
        set => Check(nameof(Columns)).Columns = InGridRange(value, nameof(Columns));
    }

    /// <summary>Rows = linhas. Quantas linhas de casas a Grade tem (1 a 20).</summary>
    /// <example><code>game.Find("Board").Rows = 4;</code></example>
    public int Rows
    {
        get => Check(nameof(Rows)).GridRows;
        set => Check(nameof(Rows)).Rows = InGridRange(value, nameof(Rows));
    }

    private int InGridRange(int value, string property) => value is >= 1 and <= Piece.MaxCells
        ? value
        : throw new GameException($"O {property} de \"{Name}\" vai de 1 a {Piece.MaxCells} (veio {value}).");

    /// <summary>
    /// Cell = casa. Pega a casa (x, y) da Grade: x é a coluna (0 é a da esquerda) e y é a linha (0 é a de cima).
    /// Depois, mude o Text e a Color dela.
    /// </summary>
    /// <example>
    /// <code>
    /// game.Find("Board").Cell(0, 0).Text = "X";
    /// if (game.Find("Board").Cell(1, 1).IsEmpty) { ... }
    /// </code>
    /// </example>
    public Cell Cell(int x, int y)
    {
        if (Piece.Type != PieceType.Grid) throw Wrong("Cell", "só a Grade tem Cell");
        int columns = Piece.GridColumns, rows = Piece.GridRows;
        if (x < 0 || x >= columns || y < 0 || y >= rows)
            throw new GameException($"A grade \"{Name}\" tem {columns} colunas (x de 0 a {columns - 1}) e {rows} linhas (y de 0 a {rows - 1}). " +
                                    $"Veio Cell({x}, {y}).");
        return new Cell(Piece, x, y);
    }

    /// <summary>
    /// Animate = animar. true: quando o Value muda, a Barra desliza até o valor novo e, se perdeu, deixa um rastro
    /// claro do pedaço perdido (como nos jogos de luta).
    /// </summary>
    /// <example><code>game.Find("EnemyHealth").Animate = true;</code></example>
    public bool Animate
    {
        get => Check(nameof(Animate)).Animate == true;
        set => Check(nameof(Animate)).Animate = value ? true : null;
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

    /// <summary>BorderColor = cor da borda da Caixa. Escolher uma cor já liga a borda.</summary>
    /// <example><code>game.Find("Panel").BorderColor = Color.Gold;</code></example>
    public Color BorderColor
    {
        get => Check(nameof(BorderColor)).BorderColor ?? Color;
        set
        {
            var piece = Check(nameof(BorderColor));
            piece.BorderColor = value;
            piece.Border ??= true;
        }
    }

    /// <summary>BorderWidth = grossura da borda da Caixa, de 1 (fininha) a 20.</summary>
    /// <example><code>game.Find("Panel").BorderWidth = 3;</code></example>
    public int BorderWidth
    {
        get => Check(nameof(BorderWidth)).BorderThickness;
        set
        {
            var piece = Check(nameof(BorderWidth));
            if (value is < 1 or > 20)
                throw new GameException($"O BorderWidth da caixa \"{Name}\" vai de 1 (fininha) a 20. Veio {value}.");
            piece.BorderWidth = value;
        }
    }

    /// <summary>
    /// Gradient = degradê. A segunda cor da Caixa: ela vai da cor dela (Color) até esta. null volta a ter uma cor só.
    /// </summary>
    /// <example><code>game.Find("Sky").Gradient = Color.Hex("#1A4F9C");</code></example>
    public Color? Gradient
    {
        get => Check(nameof(Gradient)).Gradient;
        set => Check(nameof(Gradient)).Gradient = value;
    }

    /// <summary>
    /// GradientDirection = direção do degradê: GradientDirection.Down (de cima para baixo), Right (da esquerda para
    /// a direita) ou Diagonal.
    /// </summary>
    /// <example><code>game.Find("Sky").GradientDirection = GradientDirection.Right;</code></example>
    public GradientDirection GradientDirection
    {
        get => Check(nameof(GradientDirection)).GradientDirection ?? GradientDirection.Down;
        set => Check(nameof(GradientDirection)).GradientDirection = value == GradientDirection.Down ? null : value;
    }

    /// <summary>
    /// Corner = canto. Cantos da Caixa, do Botão e da Barra: Corner.Round (redondos), Corner.Square (retos) ou
    /// Corner.Circle (na Caixa, um círculo; no Botão e na Barra, pontas todas redondas, como uma pílula).
    /// </summary>
    /// <example><code>game.Find("Portrait").Corner = Corner.Circle;</code></example>
    public Corner Corner
    {
        get => Check(nameof(Corner)).Corner ?? Corner.Round;
        set => Check(nameof(Corner)).Corner = value == Corner.Round ? null : value;
    }

    /// <summary>
    /// Font = fonte, o desenho das letras: Font.Normal, Fantasy, Book, Hand, Strong, Classic, Elegant, Fun, Retro ou Tech.
    /// </summary>
    /// <example><code>game.Find("Title").Font = Font.Fantasy;</code></example>
    public Font Font
    {
        get => Check(nameof(Font)).Font ?? Font.Normal;
        set => Check(nameof(Font)).Font = value == Font.Normal ? null : value;
    }

    /// <summary>Bold = negrito. true deixa as letras mais grossas (em qualquer peça com letras; o nome da Barra já começa em negrito).</summary>
    public bool Bold
    {
        get => Check(nameof(Bold)).IsBold;
        set => Check(nameof(Bold)).Bold = value == Piece.DefaultBold(Piece.Type) ? null : value;
    }

    /// <summary>Italic = itálico. true deixa as letras inclinadas (em qualquer peça com letras).</summary>
    /// <example><code>game.Find("Letter").Italic = true;</code></example>
    public bool Italic
    {
        get => Check(nameof(Italic)).Italic == true;
        set => Check(nameof(Italic)).Italic = value ? true : null;
    }

    /// <summary>
    /// Scroll = rolagem. true dá uma barra de rolagem ao Texto, para textos compridos (uma carta, a história)
    /// não serem cortados: o jogador rola com a roda do mouse.
    /// </summary>
    /// <example><code>game.Find("Letter").Scroll = true;</code></example>
    public bool Scroll
    {
        get => Check(nameof(Scroll)).Scroll == true;
        set => Check(nameof(Scroll)).Scroll = value ? true : null;
    }

    /// <summary>
    /// Shadow = sombra. No Texto, na Barra, no Campo de escrita, nas Mensagens e na Lista, uma sombra atrás das letras (para ler bem em cima de qualquer fundo);
    /// no Botão e na Caixa, uma sombra embaixo da peça, que parece saltar da tela.
    /// </summary>
    /// <example><code>game.Find("Title").Shadow = true;</code></example>
    public bool Shadow
    {
        get => Check(nameof(Shadow)).Shadow == true;
        set => Check(nameof(Shadow)).Shadow = value ? true : null;
    }

    /// <summary>
    /// Style = estilo do botão: ButtonStyle.Filled (cheio), Gradient (degradê), Soft (suave), Outline (contorno)
    /// ou Text (só texto).
    /// </summary>
    /// <example><code>game.Find("Back").Style = ButtonStyle.Text;</code></example>
    public ButtonStyle Style
    {
        get => Check(nameof(Style)).Style ?? ButtonStyle.Filled;
        set => Check(nameof(Style)).Style = value == ButtonStyle.Filled ? null : value;
    }

    /// <summary>
    /// TextShadow = sombra nas letras do Botão (o Shadow do botão é a sombra embaixo dele inteiro).
    /// </summary>
    /// <example><code>game.Find("Attack").TextShadow = true;</code></example>
    public bool TextShadow
    {
        get => Check(nameof(TextShadow)).TextShadow == true;
        set => Check(nameof(TextShadow)).TextShadow = value ? true : null;
    }

    /// <summary>
    /// TextColor = cor da letra do Botão, da Barra (o nome dela), do Campo de escrita (a pergunta), das Mensagens
    /// (as que não têm cor) e da Lista vazia. Sem escolher, ela é automática. No Texto, a cor da letra é o Color.
    /// </summary>
    /// <example><code>game.Find("Buy").TextColor = Color.Gold;</code></example>
    public Color TextColor
    {
        get => Check(nameof(TextColor)).TextColor ?? Color.White;
        set => Check(nameof(TextColor)).TextColor = value;
    }

    /// <summary>BarStyle = estilo da barra: BarStyle.Smooth (lisa), BarStyle.Shine (brilhante) ou BarStyle.Blocks (em blocos).</summary>
    /// <example><code>game.Find("Mana").BarStyle = BarStyle.Shine;</code></example>
    public BarStyle BarStyle
    {
        get => Check(nameof(BarStyle)).BarStyle ?? BarStyle.Smooth;
        set => Check(nameof(BarStyle)).BarStyle = value == BarStyle.Smooth ? null : value;
    }

    /// <summary>BarText = onde ficam o nome e o número da barra: BarText.Above (em cima), BarText.Inside (dentro) ou BarText.None (sem texto).</summary>
    /// <example><code>game.Find("Health").BarText = BarText.Inside;</code></example>
    public BarText BarText
    {
        get => Check(nameof(BarText)).BarText ?? BarText.Above;
        set => Check(nameof(BarText)).BarText = value == BarText.Above ? null : value;
    }

    /// <summary>
    /// Shortcut = atalho. A tecla que executa o mesmo OnClick do botão: "Space" (Espaço), "Enter", "Escape",
    /// "Up", "Down", "Left", "Right", "A" a "Z" ou "D0" a "D9". Vazio tira o atalho.
    /// Só funciona na cena atual, com o botão visível e ativo, fora de campos de escrita.
    /// </summary>
    /// <example><code>game.Find("StartGame").Shortcut = "Space";</code></example>
    public string Shortcut
    {
        get => Check(nameof(Shortcut)).Shortcut ?? "";
        set
        {
            var piece = Check(nameof(Shortcut));
            if (!ButtonShortcuts.TryNormalize(value, out var shortcut))
                throw new GameException($"O Shortcut do botão \"{Name}\" não reconhece a tecla \"{value}\". {ButtonShortcuts.Help}");
            piece.Shortcut = shortcut;
        }
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
    /// Write = escrever. Escreve uma mensagem só nesta caixa de Mensagens (o game.Write escreve em todas as que
    /// recebem o game.Write). Bom para separar: uma caixa com o que você faz e outra com o que o inimigo faz.
    /// </summary>
    /// <param name="text">O texto. Exemplo: $"O goblin causou {damage} de dano."</param>
    /// <param name="color">Cor opcional. Exemplo: Color.Red.</param>
    /// <example>
    /// <code>
    /// game.Find("PlayerLog").Write($"Você causou {damage} de dano.", Color.Green);
    /// game.Find("EnemyLog").Write($"O goblin causou {hit} de dano.", Color.Red);
    /// </code>
    /// </example>
    public void Write(string text, Color? color = null)
    {
        if (Piece.Type != PieceType.Messages)
            throw Wrong("Write", Piece.Type == PieceType.Text
                ? "só a peça Mensagens tem Write; para mudar um Texto, use .Text = \"...\""
                : "só a peça Mensagens tem Write");
        var game = _owner?.Game ?? throw new GameException($"\"{Name}\" não está numa cena desenhada.");
        game.WriteTo(Piece.Name, text ?? "", color);
    }

    /// <summary>
    /// Clear = limpar. Na Grade, apaga todas as casas (texto e cor). Nas Mensagens, apaga só as mensagens escritas
    /// nesta caixa (o game.Clear apaga todas).
    /// </summary>
    /// <example><code>game.Find("Board").Clear();</code></example>
    public void Clear()
    {
        if (Piece.Type == PieceType.Grid)
        {
            Piece.Cells = null;
            return;
        }
        if (Piece.Type != PieceType.Messages) throw Wrong("Clear", "só a Grade e a peça Mensagens têm Clear");
        var game = _owner?.Game ?? throw new GameException($"\"{Name}\" não está numa cena desenhada.");
        game.ClearTarget(Piece.Name);
    }

    /// <summary>
    /// GameWrite = recebe o game.Write. false: esta caixa de Mensagens mostra só o que for escrito nela com
    /// game.Find("Nome").Write (a caixa do inimigo, por exemplo).
    /// </summary>
    /// <example><code>game.Find("EnemyLog").GameWrite = false;</code></example>
    public bool GameWrite
    {
        get => Check(nameof(GameWrite)).GameWrite;
        set => Check(nameof(GameWrite)).GameWrite = value;
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
    /// OnChoose = ao escolher. O código entre as chaves roda quando o jogador escolhe uma opção da Escolha;
    /// choice é o texto da opção.
    /// </summary>
    /// <example>
    /// <code>
    /// game.Find("Class").OnChoose(choice =>
    /// {
    ///     heroClass = choice;
    ///     game.Write($"Você escolheu {choice}.");
    /// });
    /// </code>
    /// </example>
    public void OnChoose(Action<string> action)
    {
        if (Piece.Type != PieceType.Choice) throw Wrong("OnChoose", "só a Escolha tem OnChoose");
        Choose = action ?? throw new GameException($"O OnChoose de \"{Name}\" precisa dizer o que fazer: game.Find(\"{Name}\").OnChoose(choice => {{ ... }});");
    }

    /// <summary>
    /// OnChange = ao mudar. O código entre as chaves roda quando o jogador aperta o − ou o + do Contador;
    /// value é o número novo.
    /// </summary>
    /// <example>
    /// <code>
    /// game.Find("Amount").OnChange(value =>
    /// {
    ///     game.Find("Total").Text = $"Total: {value * price} moedas";
    /// });
    /// </code>
    /// </example>
    public void OnChange(Action<int> action)
    {
        if (Piece.Type != PieceType.Counter) throw Wrong("OnChange", "só o Contador tem OnChange");
        Change = action ?? throw new GameException($"O OnChange de \"{Name}\" precisa dizer o que fazer: game.Find(\"{Name}\").OnChange(value => {{ ... }});");
    }

    /// <summary>
    /// OnCellClick = ao clicar numa casa. O código entre as chaves roda quando o jogador clica numa casa da Grade;
    /// x é a coluna e y é a linha da casa (a de cima à esquerda é 0, 0).
    /// </summary>
    /// <example>
    /// <code>
    /// game.Find("Board").OnCellClick((x, y) =>
    /// {
    ///     var cell = game.Find("Board").Cell(x, y);
    ///     if (cell.IsEmpty) cell.Text = "X";
    /// });
    /// </code>
    /// </example>
    public void OnCellClick(Action<int, int> action)
    {
        if (Piece.Type != PieceType.Grid) throw Wrong("OnCellClick", "só a Grade tem OnCellClick");
        CellClick = action ?? throw new GameException($"O OnCellClick de \"{Name}\" precisa dizer o que fazer: game.Find(\"{Name}\").OnCellClick((x, y) => {{ ... }});");
    }

    /// <summary>
    /// Spin = girar. Gira a Roleta, espera ela parar e devolve a opção sorteada. Funciona dentro de um clique
    /// (o OnClick de um botão "Girar"), como o game.Wait: o jogador vê a roleta girando e o código continua depois.
    /// </summary>
    /// <returns>O texto da fatia onde a seta parou.</returns>
    /// <example>
    /// <code>
    /// game.Find("SpinButton").OnClick(() =>
    /// {
    ///     string prize = game.Find("Wheel").Spin();
    ///     game.Write($"Você ganhou: {prize}!");
    /// });
    /// </code>
    /// </example>
    public string Spin()
    {
        if (Piece.Type != PieceType.Wheel) throw Wrong("Spin", "só a Roleta tem Spin");
        var game = _owner?.Game ?? throw new GameException($"\"{Name}\" não está numa cena desenhada.");
        return game.SpinWheel(this);
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
            throw new GameException($"A lista \"{Name}\" ainda não tem cartão modelo. No Estúdio, ponha as peças (nome, preço, botão…) dentro do primeiro cartão dela.");

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

    /// <summary>
    /// Pop = saltar. Um texto aparece em cima da peça, sobe e some: o dano que o inimigo levou, a cura, o ouro
    /// ganho. Funciona em qualquer peça (na barra de vida, no retrato do inimigo…).
    /// </summary>
    /// <param name="text">O que aparece. Exemplo: $"-{damage}".</param>
    /// <param name="color">Cor opcional. Exemplo: Color.Red (dano), Color.Green (cura), Color.Gold (ouro).</param>
    /// <example>
    /// <code>
    /// game.Find("EnemyHealth").Pop($"-{damage}", Color.Red);
    /// game.Find("Gold").Pop("+10", Color.Gold);
    /// </code>
    /// </example>
    public void Pop(string text, Color? color = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new GameException($"O Pop de \"{Name}\" precisa de um texto para mostrar, por exemplo: game.Find(\"{Name}\").Pop($\"-{{damage}}\", Color.Red);");
        AddEffect(Effect.Pop, text.Trim(), color);
    }

    private void AddEffect(Effect effect, string? text = null, Color? color = null)
    {
        if (_owner == null) throw new GameException($"\"{Name}\" não está numa cena desenhada.");
        _owner.AddEffect(new PieceEffect(Piece, effect, text, color));
    }

    /// <summary>Mostra "Attack (Botão)".</summary>
    public override string ToString() => $"{Name} ({Piece.Describe(Piece.Type)})";

    private Piece Check(string property)
    {
        if (Piece.Supports(Piece.Type, property)) return Piece;
        throw Wrong(property, property switch
        {
            nameof(Text) => "só Texto, Botão, Barra, Campo de escrita e Lista têm Text",
            nameof(Value) or nameof(Max) => $"só a Barra e o Contador têm {property}",
            nameof(Image) => "só imagens têm Image",
            nameof(Color) or nameof(Shade) => $"Imagem, Campo de escrita, Mensagens e Lista não têm {property}",
            nameof(Opacity) or nameof(Border) or nameof(BorderColor) or nameof(BorderWidth) or nameof(Gradient) or nameof(GradientDirection) =>
                $"só a Caixa tem {property}",
            nameof(Min) or nameof(Step) => $"só o Contador tem {property}",
            nameof(Options) or nameof(Selected) => $"só a Escolha e a Roleta têm {property}",
            nameof(Columns) => "só a Grade e a Escolha têm Columns",
            nameof(Rows) => "só a Grade tem Rows",
            nameof(Animate) => "só barras têm Animate",
            nameof(Corner) => "só Caixa, Botão, Barra, Escolha, Contador e Grade têm Corner",
            nameof(Italic) or nameof(Bold) or nameof(Font) =>
                $"só as peças com letras (Texto, Botão, Barra, Campo de escrita, Mensagens e Lista) têm {property}",
            nameof(Shadow) => "Imagem não tem Shadow",
            nameof(TextShadow) => "só o Botão tem TextShadow (nas outras peças, a sombra das letras é o Shadow)",
            nameof(Style) => "só botões têm Style",
            nameof(GameWrite) => "só a peça Mensagens tem GameWrite",
            nameof(TextColor) => Piece.Type == PieceType.Text
                ? "no Texto, a cor da letra é o Color"
                : "só as peças com letras têm TextColor",
            nameof(BarStyle) or nameof(BarText) => $"só barras têm {property}",
            nameof(Shortcut) => "só botões têm Shortcut (atalho de teclado)",
            nameof(Enabled) => "só botões, campos de escrita, imagens, Escolha, Contador e Grade têm Enabled",
            _ => $"esta peça não tem {property}",
        });
    }

    private GameException Wrong(string property, string rule) => CardOf != null
        ? new($"\"{Name}\" (no cartão da lista \"{CardOf}\") é do tipo {Piece.Describe(Piece.Type)}: {rule}.")
        : new($"\"{Name}\" (na tela \"{_scene}\") é do tipo {Piece.Describe(Piece.Type)}: {rule}.");
}
