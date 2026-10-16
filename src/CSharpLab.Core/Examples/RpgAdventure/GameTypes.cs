// Os tipos do jogo: as "fichas" de cada coisa (classe de herói, coisa da loja, inimigo, missão, pergunta do troll).
//
// "record" é uma classe curtinha só com dados: o que vem entre parênteses vira propriedade
// (classes[0].Name, wolf.Health…). Um valor depois do = é o padrão, quando não é dito.
// Ficam num arquivo separado só para o Program.cs ficar mais curto: o C# junta os dois arquivos sozinho.

// Uma classe de herói: nome, desenho, cor, texto, vida, mana, dano mínimo e máximo, e a habilidade especial.
record HeroClass(string Name, Icon Icon, Color Color, string About, int Health, int Mana, int MinDamage, int MaxDamage, string Skill, int SkillCost);

// O tipo de uma coisa da loja: diz o que acontece ao comprar.
enum ProductKind { Potion, Bomb, Amulet, Weapon }

// Uma coisa à venda. Power: quanto cura (poção), quanto dano faz (bomba), quanta mana dá (amuleto) ou quanto dano soma (arma).
record Product(string Name, Icon Icon, Color Color, string About, int Price, ProductKind Kind, int Power);

// Um inimigo: nome, desenho e cor (o "retrato" na luta), vida, dano mínimo e máximo, e o que ele deixa ao ser derrotado.
record Enemy(string Name, Icon Icon, Color Color, int Health, int MinDamage, int MaxDamage, int Gold, int Xp, bool IsBoss = false);

// Uma missão do quadro da vila: o que fazer, quanto precisa (Goal) e o prêmio (ouro e experiência).
record Quest(string Id, string Title, Icon Icon, Color Color, string About, int Goal, int Gold, int Xp);

// Uma linha da mochila: o que é, quantos você tem e o que o botão dela diz.
record BagLine(string Name, Icon Icon, Color Color, int Count, string About, string Action, bool CanUse);

// Uma pergunta do troll da ponte: a pergunta, as opções (os botões da Escolha) e a resposta certa.
record Riddle(string Question, string[] Options, string Answer);
