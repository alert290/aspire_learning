using System.Diagnostics.Metrics;
using CoffeeShop.Data.Orders;

namespace CoffeeShop.OrdersApi;

/// <summary>
/// Собственные (бизнес-) метрики сервиса заказов.
/// </summary>
/// <remarks>
/// <para>
/// Метрики создаются через стандартный .NET API System.Diagnostics.Metrics.
/// ServiceDefaults подписан на все Meter с префиксом "CoffeeShop." и отправляет их
/// по OTLP в дашборд. Открой в дашборде: Metrics → ordersapi → CoffeeShop.Orders.
/// </para>
/// <para>
/// IMeterFactory (из DI) — рекомендуемый способ создавать Meter в ASP.NET Core:
/// так метрики корректно изолируются, например, в тестах.
/// </para>
/// </remarks>
public sealed class OrdersMetrics
{
    public const string MeterName = "CoffeeShop.Orders";

    private readonly Counter<long> _ordersPlaced;
    private readonly Counter<long> _ordersReady;
    private readonly Counter<long> _drinksOrdered;
    private readonly Histogram<double> _orderValue;
    private readonly Histogram<double> _timeToReady;

    public OrdersMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);

        _ordersPlaced = meter.CreateCounter<long>(
            "coffeeshop.orders.placed", unit: "{order}", description: "Сколько заказов оформлено");

        _ordersReady = meter.CreateCounter<long>(
            "coffeeshop.orders.ready", unit: "{order}", description: "Сколько заказов приготовлено");

        _drinksOrdered = meter.CreateCounter<long>(
            "coffeeshop.items.ordered", unit: "{item}", description: "Сколько позиций заказано (тег item — название)");

        _orderValue = meter.CreateHistogram<double>(
            "coffeeshop.orders.value", unit: "RUB", description: "Сумма заказа");

        _timeToReady = meter.CreateHistogram<double>(
            "coffeeshop.orders.time_to_ready", unit: "s", description: "Время от заказа до готовности");
    }

    public void OrderPlaced(Order order)
    {
        _ordersPlaced.Add(1);
        _orderValue.Record((double)order.Total);

        foreach (var line in order.Lines)
        {
            // Теги (dimensions) позволяют фильтровать и группировать метрику в дашборде.
            _drinksOrdered.Add(line.Quantity, new KeyValuePair<string, object?>("item", line.ItemName));
        }
    }

    public void OrderReady(Order order)
    {
        _ordersReady.Add(1);
        if (order.ReadyAt is { } readyAt)
        {
            _timeToReady.Record((readyAt - order.CreatedAt).TotalSeconds);
        }
    }
}
