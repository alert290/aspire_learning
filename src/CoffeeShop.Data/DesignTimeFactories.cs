using CoffeeShop.Data.Menu;
using CoffeeShop.Data.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CoffeeShop.Data;

// ---------------------------------------------------------------------------------
// Фабрики "времени разработки" (design-time) — нужны ТОЛЬКО инструменту `dotnet ef`.
//
// Когда ты выполняешь `dotnet ef migrations add ...`, EF Core должен создать DbContext,
// чтобы прочитать модель. В рантайме строку подключения даёт Aspire, но `dotnet ef`
// запускается без AppHost — поэтому даём ему фиктивную строку. К базе он при
// создании миграции не подключается, ему нужен только провайдер (Npgsql).
//
// Команды (из корня репозитория):
//   dotnet ef migrations add <Имя> --project src/CoffeeShop.Data --context MenuDbContext   --output-dir Menu/Migrations
//   dotnet ef migrations add <Имя> --project src/CoffeeShop.Data --context OrdersDbContext --output-dir Orders/Migrations
//
// Применяет миграции не разработчик руками, а MigrationService при старте приложения.
// ---------------------------------------------------------------------------------

public sealed class MenuDbContextFactory : IDesignTimeDbContextFactory<MenuDbContext>
{
    public MenuDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<MenuDbContext>()
            .UseNpgsql("Host=localhost;Database=menudb")
            .Options);
}

public sealed class OrdersDbContextFactory : IDesignTimeDbContextFactory<OrdersDbContext>
{
    public OrdersDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<OrdersDbContext>()
            .UseNpgsql("Host=localhost;Database=ordersdb")
            .Options);
}
