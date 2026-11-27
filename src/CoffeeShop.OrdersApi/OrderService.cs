using CoffeeShop.Contracts;
using CoffeeShop.Data.Orders;
using CoffeeShop.OrdersApi.Messaging;

namespace CoffeeShop.OrdersApi;

/// <summary>Бизнес-логика оформления заказа.</summary>
public sealed class OrderService(
    OrdersDbContext db,
    MenuApiClient menuApi,
    RabbitMqPublisher publisher,
    OrdersMetrics metrics,
    ILogger<OrderService> logger)
{
    public const int MaxQuantityPerLine = 20;

    /// <summary>
    /// Оформить заказ: проверить → посчитать цены → сохранить → опубликовать событие.
    /// </summary>
    /// <returns>Заказ или словарь ошибок валидации.</returns>
    public async Task<(OrderDto? Order, Dictionary<string, string[]>? Errors)> PlaceOrderAsync(
        PlaceOrderRequest request, CancellationToken ct)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return (null, errors);
        }

        // 1. Цены — у MenuApi (через service discovery + resilience).
        var menu = await menuApi.GetMenuAsync(ct);

        var unknown = request.Items.Where(i => !menu.ContainsKey(i.MenuItemId)).Select(i => i.MenuItemId).ToList();
        if (unknown.Count > 0)
        {
            return (null, new() { ["items"] = [$"Неизвестные позиции меню: {string.Join(", ", unknown)}"] });
        }

        // 2. Сохраняем заказ со статусом Pending.
        var order = new Order
        {
            Id = Guid.CreateVersion7(),
            CustomerName = request.CustomerName.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
            Lines = request.Items
                .GroupBy(i => i.MenuItemId)
                .Select(g => new OrderLine
                {
                    MenuItemId = g.Key,
                    ItemName = menu[g.Key].Name,
                    UnitPrice = menu[g.Key].Price,
                    Quantity = g.Sum(i => i.Quantity)
                })
                .ToList()
        };

        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);

        // 3. Публикуем событие для бариста.
        //    ⚠ Учебное упрощение: если процесс упадёт между SaveChanges и Publish, заказ
        //    "застрянет" в Pending. В продакшене для этого применяют паттерн Transactional Outbox.
        await publisher.PublishAsync(
            Queues.OrdersPlaced,
            new OrderPlacedMessage(
                order.Id,
                order.CustomerName,
                order.Lines.Select(l => new OrderPlacedLine(l.ItemName, l.Quantity)).ToList(),
                order.CreatedAt),
            ct);

        // 4. Метрики и лог.
        metrics.OrderPlaced(order);
        logger.LogInformation("Заказ {OrderId} от {Customer} на сумму {Total} ₽ принят",
            order.Id, order.CustomerName, order.Total);

        return (order.ToDto(), null);
    }

    private static Dictionary<string, string[]> Validate(PlaceOrderRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.CustomerName))
        {
            errors["customerName"] = ["Укажите имя — бариста должен знать, кого позвать."];
        }
        else if (request.CustomerName.Trim().Length > 100)
        {
            errors["customerName"] = ["Имя не длиннее 100 символов."];
        }

        if (request.Items is not { Count: > 0 })
        {
            errors["items"] = ["Корзина пуста."];
        }
        else if (request.Items.Any(i => i.Quantity is < 1 or > MaxQuantityPerLine))
        {
            errors["items"] = [$"Количество каждой позиции — от 1 до {MaxQuantityPerLine}."];
        }

        return errors;
    }
}
