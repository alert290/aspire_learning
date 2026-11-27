using Microsoft.Extensions.Caching.Distributed;

namespace CoffeeShop.MenuApi;

/// <summary>
/// Служебные эндпоинты. Их вызывают КОМАНДЫ ДАШБОРДА Aspire — кнопки в меню ресурса
/// (см. AppHost.cs: <c>.WithHttpCommand("/admin/cache/clear", ...)</c>).
/// </summary>
/// <remarks>
/// В реальном проекте такие эндпоинты обязательно закрывают авторизацией
/// или не публикуют вовсе за пределами Development.
/// </remarks>
public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin").WithTags("Admin").ExcludeFromDescription();

        admin.MapPost("/cache/clear", async (IDistributedCache cache, ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            await cache.RemoveAsync(MenuEndpoints.MenuCacheKey, ct);
            loggerFactory.CreateLogger("CoffeeShop.MenuApi.Admin").LogWarning("Кэш меню очищен по команде из дашборда");
            return Results.Ok("Кэш меню очищен");
        });

        admin.MapPost("/chaos/toggle", async (ChaosSwitch chaos, ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            var enabled = await chaos.ToggleAsync(ct);
            var message = enabled
                ? $"Сбои ВКЛЮЧЕНЫ (команду обработала {ServiceInstance.Name}): {ChaosSwitch.FailureRate:P0} запросов к /menu на всех репликах вернут 503"
                : $"Сбои выключены (команду обработала {ServiceInstance.Name})";
            loggerFactory.CreateLogger("CoffeeShop.MenuApi.Admin").LogWarning("{Message}", message);
            return Results.Ok(message);
        });

        return app;
    }
}
