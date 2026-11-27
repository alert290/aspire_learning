using CoffeeShop.Contracts;

namespace CoffeeShop.OrdersApi;

/// <summary>
/// HTTP-клиент к MenuApi. Цены берём с сервера меню, а не доверяем клиенту.
/// </summary>
/// <remarks>
/// Этот вызов "сервис → сервис" хорошо виден в дашборде: в трейсе POST /orders
/// будет вложенный спан GET menuapi/menu, а внутри него — Redis или SQL.
/// </remarks>
public sealed class MenuApiClient(HttpClient httpClient)
{
    public async Task<IReadOnlyDictionary<int, MenuItemDto>> GetMenuAsync(CancellationToken ct)
    {
        var items = await httpClient.GetFromJsonAsync<List<MenuItemDto>>("/menu", ct) ?? [];
        return items.ToDictionary(i => i.Id);
    }
}
