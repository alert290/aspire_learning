using System.Text.Json.Serialization;

namespace CoffeeShop.Contracts;

/// <summary>
/// Жизненный цикл заказа: Pending (принят) → Brewing (готовится) → Ready (готов).
/// </summary>
/// <remarks>
/// Атрибут JsonStringEnumConverter заставляет System.Text.Json писать "Brewing" вместо 1 —
/// так JSON читабельнее в логах и в трейсах дашборда.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<OrderStatus>))]
public enum OrderStatus
{
    Pending,
    Brewing,
    Ready
}

/// <summary>Запрос на оформление заказа (Web → OrdersApi).</summary>
public sealed record PlaceOrderRequest(string CustomerName, IReadOnlyList<OrderLineRequest> Items);

/// <summary>Одна строка заказа: какой напиток и сколько.</summary>
public sealed record OrderLineRequest(int MenuItemId, int Quantity);

/// <summary>Заказ, как его возвращает OrdersApi.</summary>
public sealed record OrderDto(
    Guid Id,
    string CustomerName,
    OrderStatus Status,
    decimal Total,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadyAt,
    string? PreparedBy,
    IReadOnlyList<OrderLineDto> Lines);

public sealed record OrderLineDto(string ItemName, int Quantity, decimal UnitPrice);
