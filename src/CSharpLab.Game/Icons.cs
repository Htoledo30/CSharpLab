using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace CSharpLab.GameEngine;

/// <summary>
/// Icon = ícone. Os desenhos prontos da peça Ícone (e do ícone do Botão). Vêm do Windows, então funcionam
/// em qualquer jogo sem precisar de arquivo de imagem. Ficam da cor que você escolher.
/// Exemplo: <c>game.Find("EnemyIcon").Icon = Icon.Goblin;</c>
/// </summary>
public enum Icon
{
    // ---- Luta
    /// <summary>Espada.</summary>
    Sword,
    /// <summary>Duas espadas cruzadas (luta, atacar).</summary>
    Swords,
    /// <summary>Escudo (defender).</summary>
    Shield,
    /// <summary>Arco e flecha.</summary>
    Bow,
    /// <summary>Machado.</summary>
    Axe,
    /// <summary>Martelo.</summary>
    Hammer,
    /// <summary>Picareta.</summary>
    Pick,
    /// <summary>Tridente (lança de três pontas).</summary>
    Spear,
    /// <summary>Faca.</summary>
    Knife,
    /// <summary>Bomba.</summary>
    Bomb,
    /// <summary>Alvo (mirar, acertar).</summary>
    Target,
    /// <summary>Soco.</summary>
    Fist,
    /// <summary>Explosão (golpe, crítico).</summary>
    Boom,
    /// <summary>Caveira (morte, perigo).</summary>
    Skull,
    /// <summary>Caveira com ossos cruzados (veneno, pirata).</summary>
    Crossbones,
    /// <summary>Correntes (preso).</summary>
    Chains,
    /// <summary>Braço forte (força).</summary>
    Muscle,
    /// <summary>Esgrimista.</summary>
    Fencer,

    // ---- Criaturas
    /// <summary>Goblin.</summary>
    Goblin,
    /// <summary>Ogro (demônio).</summary>
    Ogre,
    /// <summary>Fantasma.</summary>
    Ghost,
    /// <summary>Dragão.</summary>
    Dragon,
    /// <summary>Lobo.</summary>
    Wolf,
    /// <summary>Aranha.</summary>
    Spider,
    /// <summary>Cobra.</summary>
    Snake,
    /// <summary>Morcego.</summary>
    Bat,
    /// <summary>Zumbi.</summary>
    Zombie,
    /// <summary>Vampiro.</summary>
    Vampire,
    /// <summary>Mago.</summary>
    Mage,
    /// <summary>Elfo.</summary>
    Elf,
    /// <summary>Fada.</summary>
    Fairy,
    /// <summary>Gênio.</summary>
    Genie,
    /// <summary>Alienígena.</summary>
    Alien,
    /// <summary>Robô.</summary>
    Robot,
    /// <summary>Escorpião.</summary>
    Scorpion,
    /// <summary>Rato.</summary>
    Rat,
    /// <summary>Javali.</summary>
    Boar,
    /// <summary>Urso.</summary>
    Bear,
    /// <summary>Leão.</summary>
    Lion,
    /// <summary>Águia.</summary>
    Eagle,
    /// <summary>Coruja.</summary>
    Owl,
    /// <summary>Raposa.</summary>
    Fox,
    /// <summary>Cavalo.</summary>
    Horse,
    /// <summary>Unicórnio.</summary>
    Unicorn,
    /// <summary>Polvo.</summary>
    Octopus,
    /// <summary>Tubarão.</summary>
    Shark,
    /// <summary>Caranguejo.</summary>
    Crab,
    /// <summary>Dinossauro.</summary>
    Dino,
    /// <summary>Gato.</summary>
    Cat,
    /// <summary>Cachorro.</summary>
    Dog,

    // ---- Pessoas
    /// <summary>Pessoa (silhueta).</summary>
    Person,
    /// <summary>Rei (príncipe com coroa).</summary>
    King,
    /// <summary>Rainha (princesa com coroa).</summary>
    Queen,
    /// <summary>Guarda.</summary>
    Guard,
    /// <summary>Correndo (fugir).</summary>
    Running,

    // ---- Itens
    /// <summary>Poção.</summary>
    Potion,
    /// <summary>Coração (vida).</summary>
    Heart,
    /// <summary>Joia, diamante.</summary>
    Gem,
    /// <summary>Moeda.</summary>
    Coin,
    /// <summary>Saco de ouro.</summary>
    Gold,
    /// <summary>Chave.</summary>
    Key,
    /// <summary>Pergaminho.</summary>
    Scroll,
    /// <summary>Livro.</summary>
    Book,
    /// <summary>Mapa.</summary>
    Map,
    /// <summary>Anel.</summary>
    Ring,
    /// <summary>Coroa.</summary>
    Crown,
    /// <summary>Maçã.</summary>
    Apple,
    /// <summary>Carne.</summary>
    Meat,
    /// <summary>Pão.</summary>
    Bread,
    /// <summary>Queijo.</summary>
    Cheese,
    /// <summary>Cogumelo.</summary>
    Mushroom,
    /// <summary>Erva.</summary>
    Herb,
    /// <summary>Remédio.</summary>
    Pill,
    /// <summary>Vela.</summary>
    Candle,
    /// <summary>Lanterna.</summary>
    Lantern,
    /// <summary>Ampulheta (tempo).</summary>
    Hourglass,
    /// <summary>Sino.</summary>
    Bell,
    /// <summary>Cadeado fechado.</summary>
    Lock,
    /// <summary>Cadeado aberto.</summary>
    Unlock,
    /// <summary>Presente, baú de recompensa.</summary>
    Gift,
    /// <summary>Mochila (inventário).</summary>
    Backpack,
    /// <summary>Bota.</summary>
    Boot,
    /// <summary>Chapéu.</summary>
    Hat,
    /// <summary>Roupa (armadura leve).</summary>
    Shirt,
    /// <summary>Taça de vinho.</summary>
    Wine,
    /// <summary>Caneca (taverna).</summary>
    Beer,
    /// <summary>Caixa de ferramentas.</summary>
    Toolbox,
    /// <summary>Osso.</summary>
    Bone,
    /// <summary>Pena.</summary>
    Feather,

    // ---- Magia e natureza
    /// <summary>Fogo.</summary>
    Fire,
    /// <summary>Gelo, floco de neve.</summary>
    Ice,
    /// <summary>Raio.</summary>
    Lightning,
    /// <summary>Gota de água.</summary>
    Water,
    /// <summary>Estrela.</summary>
    Star,
    /// <summary>Brilhos (magia).</summary>
    Sparkles,
    /// <summary>Lua.</summary>
    Moon,
    /// <summary>Sol.</summary>
    Sun,
    /// <summary>Nuvem.</summary>
    Cloud,
    /// <summary>Chuva.</summary>
    Rain,
    /// <summary>Tornado.</summary>
    Tornado,
    /// <summary>Vento.</summary>
    Wind,
    /// <summary>Bola de cristal.</summary>
    CrystalBall,
    /// <summary>Varinha mágica.</summary>
    Wand,
    /// <summary>Olho.</summary>
    Eye,
    /// <summary>Veneno (risco biológico).</summary>
    Poison,
    /// <summary>Tontura (atordoado).</summary>
    Dizzy,
    /// <summary>Teia de aranha.</summary>
    Web,
    /// <summary>Rosa.</summary>
    Rose,
    /// <summary>Folha.</summary>
    Leaf,
    /// <summary>Flor.</summary>
    Flower,
    /// <summary>Árvore.</summary>
    Tree,
    /// <summary>Cacto.</summary>
    Cactus,
    /// <summary>Pegadas de bicho.</summary>
    Paw,

    // ---- Lugares
    /// <summary>Castelo.</summary>
    Castle,
    /// <summary>Casa.</summary>
    House,
    /// <summary>Barraca (acampamento).</summary>
    Tent,
    /// <summary>Montanha.</summary>
    Mountain,
    /// <summary>Vulcão.</summary>
    Volcano,
    /// <summary>Igreja, templo.</summary>
    Church,
    /// <summary>Porta.</summary>
    Door,
    /// <summary>Barco.</summary>
    Ship,
    /// <summary>Âncora.</summary>
    Anchor,
    /// <summary>Bússola.</summary>
    Compass,

    // ---- Sinais
    /// <summary>Certo (feito).</summary>
    Check,
    /// <summary>Errado (x).</summary>
    Cross,
    /// <summary>Atenção.</summary>
    Warning,
    /// <summary>Pergunta.</summary>
    Question,
    /// <summary>Exclamação.</summary>
    Exclamation,
    /// <summary>Troféu.</summary>
    Trophy,
    /// <summary>Medalha.</summary>
    Medal,
    /// <summary>Bandeira.</summary>
    Flag,
    /// <summary>Dado.</summary>
    Dice,
    /// <summary>Relógio.</summary>
    Clock,
    /// <summary>Engrenagem (opções).</summary>
    Gear,
    /// <summary>Música.</summary>
    Music,
    /// <summary>Balão de fala.</summary>
    Speech,
    /// <summary>Sono (zzz).</summary>
    Sleep,
    /// <summary>Pegadas.</summary>
    Footprints,
    /// <summary>Cérebro (inteligência).</summary>
    Brain,
    /// <summary>Mão aberta (parar).</summary>
    Hand,
    /// <summary>Joinha.</summary>
    ThumbsUp,
    /// <summary>Peça de quebra-cabeça.</summary>
    Puzzle,
    /// <summary>Carta coringa.</summary>
    Joker,
    /// <summary>Foguete.</summary>
    Rocket,
}

/// <summary>IconFrame = moldura do ícone. Exemplo: <c>game.Find("EnemyIcon").Frame = IconFrame.Circle;</c></summary>
public enum IconFrame
{
    /// <summary>Sem moldura: só o desenho.</summary>
    None,
    /// <summary>Num círculo (bom para retratos).</summary>
    Circle,
    /// <summary>Num quadrado de cantos redondos (bom para itens e habilidades).</summary>
    Square,
}

/// <summary>
/// Os desenhos de cada ícone: um símbolo das fontes do Windows (Segoe UI Emoji), desenhado como contorno numa cor só.
/// Os símbolos mais novos têm um substituto, para os Windows que ainda não têm o desenho deles.
/// </summary>
internal static class IconSet
{
    internal sealed record Entry(Icon Icon, string Glyph, string Portuguese, string Group, string? Fallback = null);

    /// <summary>Os grupos, na ordem em que aparecem no Estúdio.</summary>
    public static IReadOnlyList<string> Groups { get; } = ["Luta", "Criaturas", "Pessoas", "Itens", "Magia e natureza", "Lugares", "Sinais"];

    public static IReadOnlyList<Entry> All { get; } =
    [
        new(Icon.Sword, "🗡", "Espada", "Luta"),
        new(Icon.Swords, "⚔", "Espadas cruzadas", "Luta"),
        new(Icon.Shield, "🛡", "Escudo", "Luta"),
        new(Icon.Bow, "🏹", "Arco", "Luta"),
        new(Icon.Axe, "🪓", "Machado", "Luta", "⛏"),
        new(Icon.Hammer, "🔨", "Martelo", "Luta"),
        new(Icon.Pick, "⛏", "Picareta", "Luta"),
        new(Icon.Spear, "🔱", "Tridente", "Luta"),
        new(Icon.Knife, "🔪", "Faca", "Luta"),
        new(Icon.Bomb, "💣", "Bomba", "Luta"),
        new(Icon.Target, "🎯", "Alvo", "Luta"),
        new(Icon.Fist, "👊", "Soco", "Luta"),
        new(Icon.Boom, "💥", "Explosão", "Luta"),
        new(Icon.Skull, "💀", "Caveira", "Luta"),
        new(Icon.Crossbones, "☠", "Caveira e ossos", "Luta"),
        new(Icon.Chains, "⛓", "Correntes", "Luta"),
        new(Icon.Muscle, "💪", "Força", "Luta"),
        new(Icon.Fencer, "🤺", "Esgrimista", "Luta"),

        new(Icon.Goblin, "👺", "Goblin", "Criaturas"),
        new(Icon.Ogre, "👹", "Ogro", "Criaturas"),
        new(Icon.Ghost, "👻", "Fantasma", "Criaturas"),
        new(Icon.Dragon, "🐉", "Dragão", "Criaturas"),
        new(Icon.Wolf, "🐺", "Lobo", "Criaturas"),
        new(Icon.Spider, "🕷", "Aranha", "Criaturas"),
        new(Icon.Snake, "🐍", "Cobra", "Criaturas"),
        new(Icon.Bat, "🦇", "Morcego", "Criaturas"),
        new(Icon.Zombie, "🧟", "Zumbi", "Criaturas"),
        new(Icon.Vampire, "🧛", "Vampiro", "Criaturas"),
        new(Icon.Mage, "🧙", "Mago", "Criaturas"),
        new(Icon.Elf, "🧝", "Elfo", "Criaturas"),
        new(Icon.Fairy, "🧚", "Fada", "Criaturas"),
        new(Icon.Genie, "🧞", "Gênio", "Criaturas"),
        new(Icon.Alien, "👽", "Alienígena", "Criaturas"),
        new(Icon.Robot, "🤖", "Robô", "Criaturas"),
        new(Icon.Scorpion, "🦂", "Escorpião", "Criaturas"),
        new(Icon.Rat, "🐀", "Rato", "Criaturas"),
        new(Icon.Boar, "🐗", "Javali", "Criaturas"),
        new(Icon.Bear, "🐻", "Urso", "Criaturas"),
        new(Icon.Lion, "🦁", "Leão", "Criaturas"),
        new(Icon.Eagle, "🦅", "Águia", "Criaturas"),
        new(Icon.Owl, "🦉", "Coruja", "Criaturas"),
        new(Icon.Fox, "🦊", "Raposa", "Criaturas"),
        new(Icon.Horse, "🐎", "Cavalo", "Criaturas"),
        new(Icon.Unicorn, "🦄", "Unicórnio", "Criaturas"),
        new(Icon.Octopus, "🐙", "Polvo", "Criaturas"),
        new(Icon.Shark, "🦈", "Tubarão", "Criaturas"),
        new(Icon.Crab, "🦀", "Caranguejo", "Criaturas"),
        new(Icon.Dino, "🦖", "Dinossauro", "Criaturas"),
        new(Icon.Cat, "🐈", "Gato", "Criaturas"),
        new(Icon.Dog, "🐕", "Cachorro", "Criaturas"),

        new(Icon.Person, "👤", "Pessoa", "Pessoas"),
        new(Icon.King, "🤴", "Rei", "Pessoas"),
        new(Icon.Queen, "👸", "Rainha", "Pessoas"),
        new(Icon.Guard, "💂", "Guarda", "Pessoas"),
        new(Icon.Running, "🏃", "Correndo", "Pessoas"),

        new(Icon.Potion, "🧪", "Poção", "Itens"),
        new(Icon.Heart, "❤", "Coração", "Itens"),
        new(Icon.Gem, "💎", "Joia", "Itens"),
        new(Icon.Coin, "🪙", "Moeda", "Itens", "💰"),
        new(Icon.Gold, "💰", "Saco de ouro", "Itens"),
        new(Icon.Key, "🗝", "Chave", "Itens"),
        new(Icon.Scroll, "📜", "Pergaminho", "Itens"),
        new(Icon.Book, "📖", "Livro", "Itens"),
        new(Icon.Map, "🗺", "Mapa", "Itens"),
        new(Icon.Ring, "💍", "Anel", "Itens"),
        new(Icon.Crown, "👑", "Coroa", "Itens"),
        new(Icon.Apple, "🍎", "Maçã", "Itens"),
        new(Icon.Meat, "🍖", "Carne", "Itens"),
        new(Icon.Bread, "🍞", "Pão", "Itens"),
        new(Icon.Cheese, "🧀", "Queijo", "Itens"),
        new(Icon.Mushroom, "🍄", "Cogumelo", "Itens"),
        new(Icon.Herb, "🌿", "Erva", "Itens"),
        new(Icon.Pill, "💊", "Remédio", "Itens"),
        new(Icon.Candle, "🕯", "Vela", "Itens"),
        new(Icon.Lantern, "🏮", "Lanterna", "Itens"),
        new(Icon.Hourglass, "⏳", "Ampulheta", "Itens"),
        new(Icon.Bell, "🔔", "Sino", "Itens"),
        new(Icon.Lock, "🔒", "Cadeado", "Itens"),
        new(Icon.Unlock, "🔓", "Cadeado aberto", "Itens"),
        new(Icon.Gift, "🎁", "Presente", "Itens"),
        new(Icon.Backpack, "🎒", "Mochila", "Itens"),
        new(Icon.Boot, "👢", "Bota", "Itens"),
        new(Icon.Hat, "🎩", "Chapéu", "Itens"),
        new(Icon.Shirt, "👕", "Roupa", "Itens"),
        new(Icon.Wine, "🍷", "Taça", "Itens"),
        new(Icon.Beer, "🍺", "Caneca", "Itens"),
        new(Icon.Toolbox, "🧰", "Ferramentas", "Itens"),
        new(Icon.Bone, "🦴", "Osso", "Itens"),
        new(Icon.Feather, "🪶", "Pena", "Itens", "🍃"),

        new(Icon.Fire, "🔥", "Fogo", "Magia e natureza"),
        new(Icon.Ice, "❄", "Gelo", "Magia e natureza"),
        new(Icon.Lightning, "⚡", "Raio", "Magia e natureza"),
        new(Icon.Water, "💧", "Água", "Magia e natureza"),
        new(Icon.Star, "⭐", "Estrela", "Magia e natureza"),
        new(Icon.Sparkles, "✨", "Brilhos", "Magia e natureza"),
        new(Icon.Moon, "🌙", "Lua", "Magia e natureza"),
        new(Icon.Sun, "☀", "Sol", "Magia e natureza"),
        new(Icon.Cloud, "☁", "Nuvem", "Magia e natureza"),
        new(Icon.Rain, "🌧", "Chuva", "Magia e natureza"),
        new(Icon.Tornado, "🌪", "Tornado", "Magia e natureza"),
        new(Icon.Wind, "💨", "Vento", "Magia e natureza"),
        new(Icon.CrystalBall, "🔮", "Bola de cristal", "Magia e natureza"),
        new(Icon.Wand, "🪄", "Varinha", "Magia e natureza", "✨"),
        new(Icon.Eye, "👁", "Olho", "Magia e natureza"),
        new(Icon.Poison, "☣", "Veneno", "Magia e natureza"),
        new(Icon.Dizzy, "💫", "Tontura", "Magia e natureza"),
        new(Icon.Web, "🕸", "Teia", "Magia e natureza"),
        new(Icon.Rose, "🌹", "Rosa", "Magia e natureza"),
        new(Icon.Leaf, "🍁", "Folha", "Magia e natureza"),
        new(Icon.Flower, "🌸", "Flor", "Magia e natureza"),
        new(Icon.Tree, "🌲", "Árvore", "Magia e natureza"),
        new(Icon.Cactus, "🌵", "Cacto", "Magia e natureza"),
        new(Icon.Paw, "🐾", "Pegadas de bicho", "Magia e natureza"),

        new(Icon.Castle, "🏰", "Castelo", "Lugares"),
        new(Icon.House, "🏠", "Casa", "Lugares"),
        new(Icon.Tent, "⛺", "Barraca", "Lugares"),
        new(Icon.Mountain, "⛰", "Montanha", "Lugares"),
        new(Icon.Volcano, "🌋", "Vulcão", "Lugares"),
        new(Icon.Church, "⛪", "Templo", "Lugares"),
        new(Icon.Door, "🚪", "Porta", "Lugares"),
        new(Icon.Ship, "⛵", "Barco", "Lugares"),
        new(Icon.Anchor, "⚓", "Âncora", "Lugares"),
        new(Icon.Compass, "🧭", "Bússola", "Lugares"),

        new(Icon.Check, "✔", "Certo", "Sinais"),
        new(Icon.Cross, "✖", "Errado", "Sinais"),
        new(Icon.Warning, "⚠", "Atenção", "Sinais"),
        new(Icon.Question, "❓", "Pergunta", "Sinais"),
        new(Icon.Exclamation, "❗", "Exclamação", "Sinais"),
        new(Icon.Trophy, "🏆", "Troféu", "Sinais"),
        new(Icon.Medal, "🏅", "Medalha", "Sinais"),
        new(Icon.Flag, "🚩", "Bandeira", "Sinais"),
        new(Icon.Dice, "🎲", "Dado", "Sinais"),
        new(Icon.Clock, "⏰", "Relógio", "Sinais"),
        new(Icon.Gear, "⚙", "Engrenagem", "Sinais"),
        new(Icon.Music, "🎵", "Música", "Sinais"),
        new(Icon.Speech, "💬", "Fala", "Sinais"),
        new(Icon.Sleep, "💤", "Sono", "Sinais"),
        new(Icon.Footprints, "👣", "Pegadas", "Sinais"),
        new(Icon.Brain, "🧠", "Cérebro", "Sinais"),
        new(Icon.Hand, "✋", "Mão", "Sinais"),
        new(Icon.ThumbsUp, "👍", "Joinha", "Sinais"),
        new(Icon.Puzzle, "🧩", "Quebra-cabeça", "Sinais"),
        new(Icon.Joker, "🃏", "Coringa", "Sinais"),
        new(Icon.Rocket, "🚀", "Foguete", "Sinais"),
    ];

    private static readonly Dictionary<Icon, Entry> ByIcon = All.ToDictionary(e => e.Icon);

    public static Entry Of(Icon icon) => ByIcon.TryGetValue(icon, out var entry) ? entry : ByIcon[Icon.Star];

    public static readonly FontFamily Family = new("Segoe UI Emoji, Segoe UI Symbol");

    private static readonly Typeface Face = new(Family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Icon, Geometry> Shapes = new();

    /// <summary>O símbolo que este Windows sabe desenhar (o substituto, se o principal for novo demais para a fonte).</summary>
    public static string GlyphOf(Icon icon)
    {
        var entry = Of(icon);
        if (entry.Fallback == null) return entry.Glyph;
        return Has(entry.Glyph) ? entry.Glyph : entry.Fallback;
    }

    private static bool Has(string text)
    {
        int code = char.ConvertToUtf32(text, 0);
        foreach (var name in new[] { "Segoe UI Emoji", "Segoe UI Symbol" })
        {
            if (new Typeface(name).TryGetGlyphTypeface(out var glyphs) && glyphs.CharacterToGlyphMap.ContainsKey(code)) return true;
        }
        return false;
    }

    /// <summary>O contorno do ícone, justo no desenho (sem a sobra de cima e de baixo das letras), para encher a peça certinho.</summary>
    public static Geometry Shape(Icon icon) => Shapes.GetOrAdd(icon, i =>
    {
        var text = new FormattedText(GlyphOf(i), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 100, Brushes.Black, 1);
        var geometry = text.BuildGeometry(new Point(0, 0));
        if (geometry.CanFreeze) geometry.Freeze();
        return geometry;
    });

    /// <summary>O nome do ícone em português, com o nome do código: "Goblin (Icon.Goblin)".</summary>
    public static string Describe(Icon icon) => $"{Of(icon).Portuguese} (Icon.{icon})";

    /// <summary>Acha o ícone pelo nome em inglês ou em português (para a busca do Estúdio).</summary>
    public static IEnumerable<Entry> Search(string text)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) return All;
        return All.Where(e => Fold(e.Icon.ToString()).Contains(Fold(text)) || Fold(e.Portuguese).Contains(Fold(text)));
    }

    /// <summary>Sem acentos e em minúsculas: "Dragão" acha "dragao".</summary>
    private static string Fold(string text)
    {
        var normalized = text.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        return new string(normalized.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
    }
}
