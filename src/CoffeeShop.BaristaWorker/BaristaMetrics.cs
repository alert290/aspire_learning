using System.Diagnostics.Metrics;

namespace CoffeeShop.BaristaWorker;

/// <summary>Метрики бариста (Meter "CoffeeShop.Barista").</summary>
public sealed class BaristaMetrics
{
    private readonly Histogram<double> _brewTime;
    private readonly UpDownCounter<int> _ordersInProgress;

    public BaristaMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("CoffeeShop.Barista");

        _brewTime = meter.CreateHistogram<double>(
            "coffeeshop.barista.brew_time", unit: "s", description: "Время приготовления заказа");

        // UpDownCounter может и расти, и уменьшаться — подходит для "сколько сейчас в работе".
        _ordersInProgress = meter.CreateUpDownCounter<int>(
            "coffeeshop.barista.orders_in_progress", unit: "{order}", description: "Заказы в работе прямо сейчас");
    }

    public void BrewStarted() => _ordersInProgress.Add(1);

    public void BrewFinished(TimeSpan duration)
    {
        _ordersInProgress.Add(-1);
        _brewTime.Record(duration.TotalSeconds);
    }
}
