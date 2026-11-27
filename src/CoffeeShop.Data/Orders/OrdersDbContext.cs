using Microsoft.EntityFrameworkCore;

namespace CoffeeShop.Data.Orders;

/// <summary>
/// EF Core контекст базы <c>ordersdb</c>.
/// </summary>
/// <remarks>
/// Почему у меню и заказов разные базы? Это классический принцип микросервисов:
/// "у каждого сервиса свои данные". В AppHost это всего две строки:
/// <c>postgres.AddDatabase("menudb")</c> и <c>postgres.AddDatabase("ordersdb")</c> —
/// один контейнер PostgreSQL, две логические базы.
/// </remarks>
public class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(order =>
        {
            // Total вычисляется в C#, в базе такой колонки нет.
            order.Ignore(o => o.Total);

            // Храним статус строкой ("Brewing"), а не числом — удобно смотреть в pgAdmin.
            order.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);

            order.HasMany(o => o.Lines)
                 .WithOne()
                 .HasForeignKey(l => l.OrderId)
                 .OnDelete(DeleteBehavior.Cascade);

            order.HasIndex(o => o.CreatedAt);
        });
    }
}
