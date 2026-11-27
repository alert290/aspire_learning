namespace CoffeeShop.Contracts;

// ---------------------------------------------------------------------------------
// Сообщения, которые ходят через RabbitMQ.
//
//   OrdersApi ──(OrderPlacedMessage)──► очередь "orders.placed" ──► BaristaWorker
//   BaristaWorker ──(OrderStatusChangedMessage)──► очередь "orders.status" ──► OrdersApi
//
// Обрати внимание: сам RabbitMQ (контейнер) поднимает Aspire, а строку подключения
// к нему сервисы получают автоматически — см. AppHost.cs (.WithReference(messaging)).
// ---------------------------------------------------------------------------------

/// <summary>Имена очередей. Используются и издателем, и потребителем.</summary>
public static class Queues
{
    /// <summary>Новые заказы: OrdersApi → BaristaWorker.</summary>
    public const string OrdersPlaced = "orders.placed";

    /// <summary>Смена статуса заказа: BaristaWorker → OrdersApi.</summary>
    public const string OrderStatusChanged = "orders.status";
}

/// <summary>Событие "заказ оформлен". Бариста получает его и начинает готовить.</summary>
public sealed record OrderPlacedMessage(
    Guid OrderId,
    string CustomerName,
    IReadOnlyList<OrderPlacedLine> Lines,
    DateTimeOffset PlacedAt);

public sealed record OrderPlacedLine(string ItemName, int Quantity);

/// <summary>Событие "статус заказа изменился". OrdersApi сохраняет его в базу.</summary>
/// <param name="PreparedBy">Какой экземпляр бариста готовит заказ (видно, как работают реплики).</param>
public sealed record OrderStatusChangedMessage(
    Guid OrderId,
    OrderStatus Status,
    string PreparedBy,
    DateTimeOffset ChangedAt);
