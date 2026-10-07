// BATALHA RPG
// Uma luta por turnos entre o herói e um monstro.
// Aprenda aqui: classes e objetos (veja o arquivo Character.cs), new, métodos de um objeto
// e propriedades. Cada personagem é um objeto criado a partir da mesma classe.
// Experimente: criar mais monstros numa List<Character>, ou uma poção que cura.

var hero = new Character("Herói", health: 30, attack: 6);
var monster = new Character("Goblin", health: 22, attack: 4);
var random = new Random();

Console.WriteLine($"Um {monster.Name} apareceu!");

while (hero.IsAlive && monster.IsAlive)
{
    Console.WriteLine();
    Console.WriteLine($"{hero.Name}: {hero.Health} de vida   |   {monster.Name}: {monster.Health} de vida");
    Console.Write("1) Atacar   2) Defender: ");
    string choice = Console.ReadLine() ?? "";

    bool defending = choice == "2";
    if (!defending)
    {
        int damage = hero.Attack(monster, random);
        Console.WriteLine($"Você causou {damage} de dano.");
    }
    else
    {
        Console.WriteLine("Você se defende!");
    }

    if (monster.IsAlive)
    {
        int damage = monster.Attack(hero, random, halved: defending);
        Console.WriteLine($"{monster.Name} causou {damage} de dano.");
    }
}

Console.WriteLine();
Console.WriteLine(hero.IsAlive ? "Vitória!" : "Você foi derrotado...");
