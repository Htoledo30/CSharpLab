namespace CSharpLab.GameEngine;

/// <summary>
/// Card = cartão. Um cartão da Lista: uma cópia do cartão modelo desenhado na aba Tela, uma para cada item.
/// Ele chega pronto dentro do Show; mude as peças dele com card.Find.
/// </summary>
/// <example>
/// <code>
/// game.Find("Weapons").Show(weapons, (card, weapon) =>
/// {
///     card.Find("Name").Text = weapon.Name;
///     card.Find("Buy").OnClick(() => Buy(weapon));
/// });
/// </code>
/// </example>
public sealed class Card
{
    private readonly string _list;

    internal Card(string list, int index, IReadOnlyList<Item> items)
    {
        _list = list;
        Index = index;
        Items = items;
    }

    internal IReadOnlyList<Item> Items { get; }

    /// <summary>Index = posição. O primeiro cartão é 0, o segundo é 1…</summary>
    public int Index { get; }

    /// <summary>Find = encontrar. Pega uma peça deste cartão pelo nome (o mesmo nome da peça no cartão modelo).</summary>
    /// <param name="name">Exemplo: "Price".</param>
    /// <example><code>card.Find("Price").Text = $"💰 {weapon.Price}";</code></example>
    public Item Find(string name)
    {
        name = name?.Trim() ?? "";
        var item = Items.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));
        if (item != null) return item;
        var names = Items.Select(i => i.Name).ToList();
        var guess = Game.Guess(names, name);
        var hint = guess != null ? $" Você quis dizer \"{guess}\"?" : "";
        var list = names.Count > 0
            ? $" Peças do cartão: {string.Join(", ", names.Select(n => $"\"{n}\""))}."
            : " O cartão ainda não tem peças: desenhe-as dentro do primeiro cartão da Lista, na aba Tela.";
        throw new GameException(name.Length == 0
            ? $"Faltou o nome da peça no card.Find.{list}"
            : $"O cartão da lista \"{_list}\" não tem a peça \"{name}\".{hint}{list}");
    }

    /// <summary>Mostra "Weapons, cartão 2".</summary>
    public override string ToString() => $"{_list}, cartão {Index + 1}";
}
