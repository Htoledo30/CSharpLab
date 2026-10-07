// Uma classe é um molde. Cada "new Character(...)" cria um personagem diferente,
// com a própria vida, mas todos com os mesmos métodos.
class Character
{
    // Propriedades: dados do objeto. "private set" = só a própria classe pode mudar.
    public string Name { get; }
    public int Health { get; private set; }
    public int AttackPower { get; }

    // => cria uma propriedade calculada: IsAlive é true enquanto a vida for maior que zero.
    public bool IsAlive => Health > 0;

    // O construtor roda no momento do new: guarda os valores iniciais.
    public Character(string name, int health, int attack)
    {
        Name = name;
        Health = health;
        AttackPower = attack;
    }

    // Ataca outro personagem e devolve quanto dano causou.
    public int Attack(Character target, Random random, bool halved = false)
    {
        int damage = random.Next(AttackPower / 2, AttackPower + 1);
        if (halved) damage /= 2;
        target.TakeDamage(damage);
        return damage;
    }

    private void TakeDamage(int amount)
    {
        Health = Math.Max(0, Health - amount);    // a vida nunca fica negativa
    }
}
