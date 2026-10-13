using System.Globalization;
using System.Text;

namespace CSharpLab.Core.Language;

/// <summary>
/// Sugere um nome em inglês para uma peça da tela a partir do texto dela ("Ataque Pesado" → HeavyAttack,
/// "Suas ações" → YourActions, "Poção de vida" → HealthPotion). Usa uma lista de palavras comuns de jogo; se
/// alguma palavra não está na lista, não sugere nada (melhor nenhuma sugestão do que uma errada).
/// </summary>
public static class PieceNames
{
    /// <summary>Até quantas palavras de verdade o nome junta (mais que isso fica comprido demais).</summary>
    private const int MaxWords = 3;

    private static readonly HashSet<string> Skip = new(StringComparer.Ordinal)
    {
        "o", "a", "os", "as", "um", "uma", "uns", "umas", "e", "em", "no", "na", "nos", "nas", "para", "pra", "com",
        "por", "pelo", "pela", "ao", "aos", "que", "se", "ou",
    };

    /// <summary>"X de Y" vira "Y X" em inglês (Poção de vida → HealthPotion).</summary>
    private static readonly HashSet<string> Of = new(StringComparer.Ordinal) { "de", "do", "da", "dos", "das" };

    /// <summary>Palavras que, em inglês, vão antes da coisa (Ataque pesado → HeavyAttack).</summary>
    private static readonly HashSet<string> Adjectives = new(StringComparer.Ordinal)
    {
        "Simple", "Heavy", "Strong", "Weak", "Quick", "Magic", "Special", "Big", "Small", "Max", "Current", "New",
        "Next", "Previous", "Light", "Dark", "Old", "Great", "Final", "Secret", "Golden", "Red", "Blue", "Green",
    };

    /// <summary>Palavras que ficam na frente de tudo (Suas ações → YourActions).</summary>
    private static readonly HashSet<string> Owners = new(StringComparer.Ordinal) { "Your", "My" };

    // Sem acento e em minúsculas → inglês. Plurais em -s, -es e -ões são achados pelo singular.
    private static readonly Dictionary<string, string> Words = new(StringComparer.Ordinal)
    {
        // de quem
        ["seu"] = "Your", ["sua"] = "Your", ["seus"] = "Your", ["suas"] = "Your", ["meu"] = "My", ["minha"] = "My", ["meus"] = "My", ["minhas"] = "My",
        // personagens
        ["heroi"] = "Hero", ["jogador"] = "Player", ["personagem"] = "Character", ["inimigo"] = "Enemy", ["monstro"] = "Monster",
        ["chefe"] = "Boss", ["goblin"] = "Goblin", ["orc"] = "Orc", ["dragao"] = "Dragon", ["lobo"] = "Wolf", ["esqueleto"] = "Skeleton",
        ["slime"] = "Slime", ["zumbi"] = "Zombie", ["rei"] = "King", ["rainha"] = "Queen", ["princesa"] = "Princess", ["principe"] = "Prince",
        ["mago"] = "Mage", ["guerreiro"] = "Warrior", ["arqueiro"] = "Archer", ["ladrao"] = "Thief", ["mercador"] = "Merchant",
        ["vendedor"] = "Seller", ["ferreiro"] = "Blacksmith", ["guarda"] = "Guard", ["aldeao"] = "Villager", ["bruxa"] = "Witch",
        // ações
        ["atacar"] = "Attack", ["ataque"] = "Attack", ["defender"] = "Defend", ["defesa"] = "Defense", ["fugir"] = "Flee", ["fuga"] = "Escape",
        ["correr"] = "Run", ["curar"] = "Heal", ["cura"] = "Heal", ["usar"] = "Use", ["comprar"] = "Buy", ["compra"] = "Buy",
        ["vender"] = "Sell", ["venda"] = "Sell", ["voltar"] = "Back", ["sair"] = "Exit", ["jogar"] = "Play", ["comecar"] = "Start",
        ["iniciar"] = "Start", ["inicio"] = "Start", ["continuar"] = "Continue", ["explorar"] = "Explore", ["buscar"] = "Search",
        ["procurar"] = "Search", ["descansar"] = "Rest", ["dormir"] = "Sleep", ["abrir"] = "Open", ["fechar"] = "Close",
        ["falar"] = "Talk", ["conversar"] = "Talk", ["entrar"] = "Enter", ["passar"] = "Pass", ["salvar"] = "Save",
        ["carregar"] = "Load", ["pegar"] = "Take", ["beber"] = "Drink", ["comer"] = "Eat", ["equipar"] = "Equip", ["aceitar"] = "Accept",
        ["recusar"] = "Refuse", ["sim"] = "Yes", ["nao"] = "No", ["deixar"] = "Leave", ["ignorar"] = "Ignore", ["lutar"] = "Fight",
        ["luta"] = "Fight", ["batalha"] = "Battle", ["combate"] = "Combat", ["escolher"] = "Choose", ["escolha"] = "Choice",
        ["tentar"] = "Try", ["seguir"] = "Follow", ["esperar"] = "Wait", ["confirmar"] = "Confirm", ["cancelar"] = "Cancel",
        ["recomecar"] = "Restart", ["reiniciar"] = "Restart", ["avancar"] = "Next", ["proximo"] = "Next", ["anterior"] = "Previous",
        // coisas
        ["ouro"] = "Gold", ["moeda"] = "Coin", ["dinheiro"] = "Money", ["pocao"] = "Potion", ["espada"] = "Sword", ["escudo"] = "Shield",
        ["arco"] = "Bow", ["flecha"] = "Arrow", ["machado"] = "Axe", ["adaga"] = "Dagger", ["cajado"] = "Staff", ["armadura"] = "Armor",
        ["arma"] = "Weapon", ["item"] = "Item", ["inventario"] = "Inventory", ["mochila"] = "Bag", ["chave"] = "Key", ["bau"] = "Chest",
        ["mapa"] = "Map", ["tesouro"] = "Treasure", ["anel"] = "Ring", ["livro"] = "Book", ["carta"] = "Letter", ["comida"] = "Food",
        ["pao"] = "Bread", ["joia"] = "Gem", ["cristal"] = "Crystal", ["coroa"] = "Crown",
        // lugares
        ["loja"] = "Shop", ["vila"] = "Village", ["aldeia"] = "Village", ["cidade"] = "City", ["floresta"] = "Forest", ["caverna"] = "Cave",
        ["castelo"] = "Castle", ["torre"] = "Tower", ["masmorra"] = "Dungeon", ["taverna"] = "Tavern", ["casa"] = "House",
        ["porta"] = "Door", ["ponte"] = "Bridge", ["rio"] = "River", ["montanha"] = "Mountain", ["praia"] = "Beach", ["mar"] = "Sea",
        ["estrada"] = "Road", ["templo"] = "Temple", ["mercado"] = "Market", ["arena"] = "Arena",
        // números do jogo
        ["vida"] = "Health", ["hp"] = "Hp", ["mp"] = "Mp", ["xp"] = "Xp", ["ap"] = "Ap", ["mana"] = "Mana", ["energia"] = "Energy",
        ["forca"] = "Strength", ["nivel"] = "Level", ["experiencia"] = "Experience", ["dano"] = "Damage", ["ponto"] = "Point",
        ["acao"] = "Action", ["status"] = "Status", ["atributo"] = "Stat", ["velocidade"] = "Speed", ["sorte"] = "Luck",
        ["fome"] = "Hunger", ["sede"] = "Thirst", ["tempo"] = "Time", ["dia"] = "Day", ["noite"] = "Night", ["turno"] = "Turn",
        ["rodada"] = "Round", ["vitoria"] = "Victory", ["derrota"] = "Defeat", ["fim"] = "End", ["recompensa"] = "Reward",
        ["preco"] = "Price", ["quantidade"] = "Amount", ["total"] = "Total", ["placar"] = "Score", ["pontuacao"] = "Score",
        ["missao"] = "Quest", ["objetivo"] = "Goal", ["jogo"] = "Game", ["fase"] = "Stage",
        // partes da tela
        ["titulo"] = "Title", ["historia"] = "Story", ["texto"] = "Text", ["mensagem"] = "Message", ["registro"] = "Log", ["log"] = "Log",
        ["painel"] = "Panel", ["fundo"] = "Background", ["quadro"] = "Frame", ["moldura"] = "Frame", ["retrato"] = "Portrait",
        ["imagem"] = "Image", ["icone"] = "Icon", ["nome"] = "Name", ["pergunta"] = "Question", ["resposta"] = "Answer",
        ["menu"] = "Menu", ["opcao"] = "Option", ["botao"] = "Button", ["barra"] = "Bar", ["lista"] = "List", ["caixa"] = "Box",
        ["tela"] = "Screen", ["cena"] = "Scene", ["aviso"] = "Warning", ["dica"] = "Tip", ["ajuda"] = "Help", ["configuracao"] = "Settings",
        // jeitos
        ["simples"] = "Simple", ["pesado"] = "Heavy", ["forte"] = "Strong", ["fraco"] = "Weak", ["rapido"] = "Quick",
        ["magico"] = "Magic", ["magia"] = "Magic", ["especial"] = "Special", ["grande"] = "Big", ["pequeno"] = "Small",
        ["maximo"] = "Max", ["atual"] = "Current", ["novo"] = "New", ["nova"] = "New", ["leve"] = "Light", ["escuro"] = "Dark",
        ["velho"] = "Old", ["antigo"] = "Old", ["final"] = "Final", ["secreto"] = "Secret", ["dourado"] = "Golden",
        ["pesada"] = "Heavy", ["fraca"] = "Weak", ["rapida"] = "Quick", ["magica"] = "Magic", ["pequena"] = "Small", ["maxima"] = "Max",
        ["escura"] = "Dark", ["velha"] = "Old", ["antiga"] = "Old", ["secreta"] = "Secret", ["dourada"] = "Golden",
    };

    /// <summary>O nome sugerido para o texto, ou null (texto vazio, comprido ou com palavra que a lista não conhece).</summary>
    public static string? Suggest(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = new List<(string Word, bool AfterOf)>();
        bool of = false;
        foreach (var raw in Split(text))
        {
            var word = Plain(raw);
            if (word.Length == 0 || word.All(char.IsAsciiDigit)) continue;   // números mudam no jogo ("Ouro: 15")
            if (Of.Contains(word))
            {
                of = parts.Count > 0;
                continue;
            }
            if (Skip.Contains(word)) continue;
            if (Translate(word) is not { } english) return null;
            parts.Add((english, of));
            of = false;
            if (parts.Count > MaxWords) return null;
        }
        if (parts.Count == 0) return null;

        // "Poção de vida": o que vem depois do "de" vai para a frente (HealthPotion).
        int split = parts.FindIndex(p => p.AfterOf);
        var words = split > 0
            ? parts.Skip(split).Concat(parts.Take(split)).Select(p => p.Word).ToList()
            : parts.Select(p => p.Word).ToList();

        // "Ataque pesado": o jeito vai antes da coisa (HeavyAttack). "Suas ações": o dono vai na frente.
        for (int i = 1; i < words.Count; i++)
        {
            if (Adjectives.Contains(words[i]) && !Adjectives.Contains(words[i - 1]) && !Owners.Contains(words[i - 1]))
                (words[i - 1], words[i]) = (words[i], words[i - 1]);
        }
        var owners = words.Where(Owners.Contains).Take(1).ToList();
        words = owners.Concat(words.Where(w => !Owners.Contains(w))).ToList();
        if (words.Count == 1 && owners.Count == 1) return null;   // só "Seu" não diz nada

        var name = string.Concat(words.Distinct());
        return name.Length is > 0 and <= 40 ? name : null;
    }

    /// <summary>A palavra em inglês (com plural, se a palavra estava no plural), ou null se não conhece.</summary>
    private static string? Translate(string word)
    {
        if (Words.TryGetValue(word, out var english)) return english;
        // Plurais: ações → ação, poções → poção, moedas → moeda, pontos → ponto, heróis → herói, inimigos → inimigo.
        foreach (var (end, singular) in new[] { ("oes", "ao"), ("aes", "ao"), ("ois", "oi"), ("es", ""), ("s", "") })
        {
            if (word.Length > end.Length + 1 && word.EndsWith(end, StringComparison.Ordinal) &&
                Words.TryGetValue(word[..^end.Length] + singular, out var one))
                return Owners.Contains(one) || Adjectives.Contains(one) ? one : Plural(one);
        }
        return null;
    }

    private static string Plural(string word) =>
        word.EndsWith('y') && word.Length > 1 && "aeiou".IndexOf(word[^2]) < 0 ? word[..^1] + "ies"
        : word.EndsWith('s') || word.EndsWith('x') || word.EndsWith("ch", StringComparison.Ordinal) ? word + "es"
        : word + "s";

    private static IEnumerable<string> Split(string text)
    {
        var word = new StringBuilder();
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                word.Append(c);
                continue;
            }
            if (word.Length > 0) yield return word.ToString();
            word.Clear();
        }
        if (word.Length > 0) yield return word.ToString();
    }

    /// <summary>Sem acento e em minúsculas ("Ações" → "acoes").</summary>
    private static string Plain(string word)
    {
        var sb = new StringBuilder(word.Length);
        foreach (var c in word.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
