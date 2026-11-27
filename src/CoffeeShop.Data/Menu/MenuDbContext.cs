using Microsoft.EntityFrameworkCore;

namespace CoffeeShop.Data.Menu;

/// <summary>
/// EF Core контекст базы <c>menudb</c>.
/// </summary>
/// <remarks>
/// <para>
/// Обрати внимание: здесь НЕТ строки подключения. Её в рантайме подставляет Aspire:
/// </para>
/// <list type="number">
///   <item>AppHost объявляет базу: <c>postgres.AddDatabase("menudb")</c>.</item>
///   <item>AppHost передаёт её сервису: <c>menuApi.WithReference(menuDb)</c> — сервис получает
///         переменную окружения <c>ConnectionStrings__menudb=Host=...;Port=...;Password=...</c>.</item>
///   <item>Сервис регистрирует контекст: <c>builder.AddNpgsqlDbContext&lt;MenuDbContext&gt;("menudb")</c> —
///         клиентская интеграция Aspire читает эту строку, включает ретраи, health check и трейсинг.</item>
/// </list>
/// </remarks>
public class MenuDbContext(DbContextOptions<MenuDbContext> options) : DbContext(options)
{
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
}
