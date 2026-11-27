using System.Diagnostics;
using CoffeeShop.Data.Menu;
using CoffeeShop.Data.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

namespace CoffeeShop.MigrationService;

/// <summary>
/// Применяет миграции и сидирует данные, после чего останавливает приложение.
/// </summary>
public sealed class DatabaseInitializer(
    IServiceProvider serviceProvider,
    IHostApplicationLifetime lifetime,
    ILogger<DatabaseInitializer> logger) : BackgroundService
{
    // Собственный источник трейсов. Имя начинается с "CoffeeShop." — ServiceDefaults
    // подписан на "CoffeeShop.*", поэтому эти спаны появятся в дашборде (вкладка Traces).
    private static readonly ActivitySource ActivitySource = new("CoffeeShop.Migrations");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var activity = ActivitySource.StartActivity("Migrate databases");

        try
        {
            // DbContext — scoped-сервис, а BackgroundService — singleton, поэтому создаём scope.
            using var scope = serviceProvider.CreateScope();

            var menuDb = scope.ServiceProvider.GetRequiredService<MenuDbContext>();
            await MigrateAsync(menuDb, stoppingToken);
            await SeedMenuAsync(menuDb, stoppingToken);

            var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            await MigrateAsync(ordersDb, stoppingToken);

            logger.LogInformation("Базы данных готовы к работе");
        }
        catch (Exception ex)
        {
            activity?.AddException(ex);
            logger.LogCritical(ex, "Не удалось подготовить базы данных");

            // Ненулевой код выхода → ресурс в дашборде станет "Finished" с ошибкой,
            // а сервисы с WaitForCompletion(migrations) так и не запустятся. Это правильно:
            // лучше не стартовать, чем работать с неготовой базой.
            Environment.ExitCode = 1;
        }
        finally
        {
            // Работа сделана — завершаем процесс.
            lifetime.StopApplication();
        }
    }

    private async Task MigrateAsync(DbContext db, CancellationToken ct)
    {
        var name = db.GetType().Name;
        logger.LogInformation("Применяю миграции для {DbContext}...", name);

        // Интеграция Aspire включает EnableRetryOnFailure. При включённых ретраях EF Core
        // требует выполнять "атомарные" операции через execution strategy — тогда при
        // временном сбое (база ещё стартует) вся операция повторится целиком.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            // 1. Создаём саму базу, если её нет.
            //    При `aspire run` базу уже создал AppHost (postgres.AddDatabase("menudb")).
            //    Но в Docker Compose (`aspire deploy`) AppHost-а нет — там есть только контейнер
            //    PostgreSQL с пустым сервером. Поэтому сервис миграций обязан уметь создать базу сам.
            var databaseCreator = db.GetService<IRelationalDatabaseCreator>();
            if (!await databaseCreator.ExistsAsync(ct))
            {
                await databaseCreator.CreateAsync(ct);
            }

            // 2. На СВЕЖЕЙ базе MigrateAsync сначала читает таблицу __EFMigrationsHistory, получает
            //    ошибку PostgreSQL 42P01 ("таблицы нет"), пишет её в лог как Error и лишь потом
            //    создаёт таблицу сам. Ошибка безвредная, но дашборд подсветил бы ресурс красным.
            //    Создаём таблицу истории заранее — и логи первого запуска остаются чистыми.
            await db.GetService<IHistoryRepository>().CreateIfNotExistsAsync(ct);

            // 3. Применяем все миграции, которых ещё нет в __EFMigrationsHistory.
            await db.Database.MigrateAsync(ct);
        });

        logger.LogInformation("Миграции {DbContext} применены", name);
    }

    private async Task SeedMenuAsync(MenuDbContext db, CancellationToken ct)
    {
        if (await db.MenuItems.AnyAsync(ct))
        {
            logger.LogInformation("Меню уже заполнено — пропускаю сидирование");
            return;
        }

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            db.MenuItems.AddRange(MenuSeed.CreateItems());
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        });

        logger.LogInformation("Меню заполнено начальными данными");
    }
}
