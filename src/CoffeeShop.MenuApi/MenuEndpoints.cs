using System.Text.Json;
using CoffeeShop.Contracts;
using CoffeeShop.Data.Menu;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace CoffeeShop.MenuApi;

public static class MenuEndpoints
{
    // Ключ в Redis. Версия в ключе ("v1") — простой способ "сбросить" кэш при смене формата.
    public const string MenuCacheKey = "menu:v1:all";

    private static readonly DistributedCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60)
    };

    public static IEndpointRouteBuilder MapMenuEndpoints(this IEndpointRouteBuilder app)
    {
        var menu = app.MapGroup("/menu").WithTags("Menu");

        // "Хаос": если сбои включены командой в дашборде, часть запросов к /menu
        // завершится ошибкой 503 ДО выполнения обработчика.
        menu.AddEndpointFilter(async (context, next) =>
        {
            var chaos = context.HttpContext.RequestServices.GetRequiredService<ChaosSwitch>();
            return await chaos.ShouldFailAsync(context.HttpContext.RequestAborted)
                ? Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                                  title: "Искусственный сбой (chaos mode)")
                : await next(context);
        });

        menu.MapGet("/", GetMenuAsync).WithName("GetMenu");
        menu.MapGet("/{id:int}", GetMenuItemAsync).WithName("GetMenuItem");

        return app;
    }

    /// <summary>
    /// Паттерн cache-aside: сначала смотрим в Redis, при промахе идём в PostgreSQL и кладём
    /// результат в кэш. Заголовок X-Cache (HIT/MISS) показывает, откуда пришли данные.
    /// </summary>
    private static async Task<Ok<List<MenuItemDto>>> GetMenuAsync(
        MenuDbContext db,
        IDistributedCache cache,
        HttpContext http,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("CoffeeShop.MenuApi.Menu");

        var cached = await cache.GetStringAsync(MenuCacheKey, ct);
        if (cached is not null)
        {
            logger.LogInformation("Меню отдано из кэша Redis");
            http.Response.Headers["X-Cache"] = "HIT";
            return TypedResults.Ok(JsonSerializer.Deserialize<List<MenuItemDto>>(cached)!);
        }

        // Этот запрос будет виден в трейсе как отдельный спан с SQL-текстом —
        // спасибо интеграции Aspire.Npgsql.EntityFrameworkCore.PostgreSQL.
        var items = await db.MenuItems
            .AsNoTracking()
            .Where(i => i.IsAvailable)
            .OrderBy(i => i.Id)
            .Select(i => new MenuItemDto(i.Id, i.Name, i.Description, i.Price, i.Category, i.Emoji))
            .ToListAsync(ct);

        await cache.SetStringAsync(MenuCacheKey, JsonSerializer.Serialize(items), CacheOptions, ct);

        logger.LogInformation("Меню загружено из PostgreSQL ({Count} позиций) и положено в кэш", items.Count);
        http.Response.Headers["X-Cache"] = "MISS";
        return TypedResults.Ok(items);
    }

    private static async Task<Results<Ok<MenuItemDto>, NotFound>> GetMenuItemAsync(
        int id, MenuDbContext db, CancellationToken ct)
    {
        var item = await db.MenuItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct);
        return item is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new MenuItemDto(item.Id, item.Name, item.Description, item.Price, item.Category, item.Emoji));
    }
}
