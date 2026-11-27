using System.Net;
using CoffeeShop.Contracts;
using Microsoft.AspNetCore.Http;

namespace CoffeeShop.Web.Services;

/// <summary>Типизированный HTTP-клиент к OrdersApi.</summary>
public sealed class OrdersApiClient(HttpClient httpClient)
{
    public async Task<OrderDto> PlaceOrderAsync(PlaceOrderRequest request, CancellationToken ct = default)
    {
        using var response = await httpClient.PostAsJsonAsync("/orders", request, ct);

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            // OrdersApi возвращает ValidationProblem (RFC 9457) — достаём текст ошибок.
            var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(ct);
            var messages = problem?.Errors.SelectMany(e => e.Value) ?? ["Некорректный заказ"];
            throw new OrderValidationException(string.Join(" ", messages));
        }

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrderDto>(ct))!;
    }

    public async Task<OrderDto?> GetOrderAsync(Guid id, CancellationToken ct = default)
    {
        using var response = await httpClient.GetAsync($"/orders/{id}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OrderDto>(ct);
    }

    public async Task<IReadOnlyList<OrderDto>> GetRecentOrdersAsync(int limit = 20, CancellationToken ct = default) =>
        await httpClient.GetFromJsonAsync<List<OrderDto>>($"/orders?limit={limit}", ct) ?? [];
}

public sealed class OrderValidationException(string message) : Exception(message);
