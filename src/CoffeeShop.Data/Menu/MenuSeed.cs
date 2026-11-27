namespace CoffeeShop.Data.Menu;

/// <summary>
/// Начальные данные меню. Их записывает в базу MigrationService при первом запуске.
/// </summary>
public static class MenuSeed
{
    /// <summary>Каждый вызов создаёт новые объекты: EF Core "присваивает" себе сущности, которые отслеживает.</summary>
    public static List<MenuItem> CreateItems() =>
    [
        new() { Name = "Эспрессо", Description = "Классический двойной шот, плотный и ароматный.", Price = 150m, Category = "Кофе", Emoji = "☕" },
        new() { Name = "Американо", Description = "Эспрессо, разбавленный горячей водой.", Price = 170m, Category = "Кофе", Emoji = "☕" },
        new() { Name = "Капучино", Description = "Эспрессо, молоко и густая молочная пена.", Price = 220m, Category = "Кофе", Emoji = "🥛" },
        new() { Name = "Латте", Description = "Много молока, немного пены и мягкий вкус.", Price = 240m, Category = "Кофе", Emoji = "🥛" },
        new() { Name = "Флэт уайт", Description = "Двойной эспрессо и бархатистое молоко.", Price = 250m, Category = "Кофе", Emoji = "🤍" },
        new() { Name = "Раф", Description = "Эспрессо, сливки и ванильный сахар, взбитые вместе.", Price = 280m, Category = "Кофе", Emoji = "🍦" },
        new() { Name = "Мокко", Description = "Эспрессо, шоколад и молоко.", Price = 270m, Category = "Кофе", Emoji = "🍫" },
        new() { Name = "Матча латте", Description = "Японский зелёный чай матча на молоке.", Price = 290m, Category = "Не кофе", Emoji = "🍵" },
        new() { Name = "Какао", Description = "Горячий шоколадный напиток с маршмеллоу.", Price = 200m, Category = "Не кофе", Emoji = "🍫" },
        new() { Name = "Круассан", Description = "Хрустящий, сливочный, только из печи.", Price = 160m, Category = "Выпечка", Emoji = "🥐" },
        new() { Name = "Чизкейк", Description = "Нью-йоркский чизкейк на песочной основе.", Price = 260m, Category = "Выпечка", Emoji = "🍰" },
    ];
}
