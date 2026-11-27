using CoffeeShop.Contracts;

namespace CoffeeShop.Web.Services;

/// <summary>Меню + служебная информация о том, кто и откуда его отдал.</summary>
/// <param name="ServedBy">Имя реплики MenuApi (заголовок X-Instance).</param>
/// <param name="FromCache">true, если данные пришли из Redis (заголовок X-Cache: HIT).</param>
public sealed record MenuSnapshot(IReadOnlyList<MenuItemDto> Items, string? ServedBy, bool FromCache);

/// <summary>Типизированный HTTP-клиент к MenuApi.</summary>
public sealed class MenuApiClient(HttpClient httpClient)
{
    public async Task<MenuSnapshot> GetMenuAsync(CancellationToken ct = default)
    {
        using var response = await httpClient.GetAsync("/menu", ct);
        response.EnsureSuccessStatusCode();

        var items = await response.Content.ReadFromJsonAsync<List<MenuItemDto>>(ct) ?? [];
        var servedBy = response.Headers.TryGetValues("X-Instance", out var instance) ? instance.FirstOrDefault() : null;
        var fromCache = response.Headers.TryGetValues("X-Cache", out var cache) && cache.FirstOrDefault() == "HIT";

        return new MenuSnapshot(items, servedBy, fromCache);
    }
}
