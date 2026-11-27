using CoffeeShop.Data.Menu;
using CoffeeShop.Data.Orders;
using CoffeeShop.MigrationService;

// =====================================================================================
//  MigrationService — "одноразовый" сервис.
//
//  Задача: применить EF Core миграции к menudb и ordersdb, заполнить меню начальными
//  данными и ЗАВЕРШИТЬСЯ. В AppHost остальные сервисы объявлены с
//  .WaitForCompletion(migrations) — они стартуют только после того, как этот процесс
//  завершится с кодом 0. Так мы гарантируем, что API никогда не увидит "пустую" базу.
//
//  Это официальный паттерн Aspire. Альтернатива — вызывать Migrate() в каждом API при
//  старте, но тогда при нескольких репликах они будут конкурировать за миграции.
// =====================================================================================

var builder = Host.CreateApplicationBuilder(args);

// Телеметрия, health checks, service discovery — как у всех сервисов.
builder.AddServiceDefaults();

// Клиентские интеграции Aspire: регистрируют DbContext, берут строку подключения из
// ConnectionStrings:menudb / ConnectionStrings:ordersdb (их передал AppHost),
// добавляют ретраи, health checks и трейсинг SQL-запросов.
builder.AddNpgsqlDbContext<MenuDbContext>("menudb");
builder.AddNpgsqlDbContext<OrdersDbContext>("ordersdb");

builder.Services.AddHostedService<DatabaseInitializer>();

var host = builder.Build();
host.Run();
