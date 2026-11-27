using System.Diagnostics;
using System.Text.Json;
using CoffeeShop.Contracts;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace CoffeeShop.BaristaWorker;

/// <summary>
/// "Бариста": забирает заказы из очереди и готовит их по одному.
/// </summary>
public sealed class Barista(
    IConnection connection,
    IOptionsMonitor<BaristaOptions> options,
    BaristaMetrics metrics,
    ILogger<Barista> logger) : BackgroundService
{
    // Собственный ActivitySource → спаны "Brew order" в трейсе заказа.
    private static readonly ActivitySource ActivitySource = new("CoffeeShop.Barista");

    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await _channel.QueueDeclareAsync(Queues.OrdersPlaced, durable: true, exclusive: false,
            autoDelete: false, cancellationToken: stoppingToken);
        await _channel.QueueDeclareAsync(Queues.OrderStatusChanged, durable: true, exclusive: false,
            autoDelete: false, cancellationToken: stoppingToken);

        // prefetchCount: 1 — "один бариста готовит один заказ". Пока заказ не подтверждён (ack),
        // RabbitMQ не даст этому потребителю следующий, а отдаст его другой реплике.
        await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += (_, delivery) => HandleAsync(_channel, delivery, stoppingToken);

        await _channel.BasicConsumeAsync(Queues.OrdersPlaced, autoAck: false, consumer, stoppingToken);

        logger.LogInformation("Бариста {Barista} на смене и ждёт заказы ({Seconds} с на позицию)",
            ServiceInstance.Name, options.CurrentValue.SecondsPerDrink);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Смена закончилась.
        }
    }

    private async Task HandleAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken ct)
    {
        OrderPlacedMessage? order = null;
        var brewing = false;
        try
        {
            order = JsonSerializer.Deserialize<OrderPlacedMessage>(delivery.Body.Span)
                    ?? throw new InvalidOperationException("Пустое сообщение");

            // Спан будет вложен в трейс, начатый ещё в Web при нажатии "Оформить заказ".
            using var activity = ActivitySource.StartActivity("Brew order");
            activity?.SetTag("coffeeshop.order.id", order.OrderId);
            activity?.SetTag("coffeeshop.order.items", order.Lines.Sum(l => l.Quantity));
            activity?.SetTag("coffeeshop.barista", ServiceInstance.Name);

            // Scope логов: все записи ниже получат свойство OrderId (видно в Structured logs).
            using var _ = logger.BeginScope(new Dictionary<string, object> { ["OrderId"] = order.OrderId });

            await PublishStatusAsync(channel, order.OrderId, OrderStatus.Brewing, ct);
            metrics.BrewStarted();
            brewing = true;

            var started = Stopwatch.GetTimestamp();
            foreach (var line in order.Lines)
            {
                logger.LogInformation("Готовлю {Item} × {Quantity} для {Customer}",
                    line.ItemName, line.Quantity, order.CustomerName);

                var seconds = options.CurrentValue.SecondsPerDrink * line.Quantity;
                await Task.Delay(TimeSpan.FromSeconds(seconds), ct);
                activity?.AddEvent(new ActivityEvent($"{line.ItemName} × {line.Quantity} готово"));
            }

            var elapsed = Stopwatch.GetElapsedTime(started);
            metrics.BrewFinished(elapsed);
            brewing = false;

            await PublishStatusAsync(channel, order.OrderId, OrderStatus.Ready, ct);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, ct);

            logger.LogInformation("Заказ для {Customer} готов за {Elapsed:N1} с ☕", order.CustomerName, elapsed.TotalSeconds);
        }
        catch (OperationCanceledException)
        {
            // Остановка во время приготовления — вернём заказ в очередь, его доделает другой бариста.
            if (brewing)
            {
                metrics.BrewFinished(TimeSpan.Zero);
            }
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, CancellationToken.None);
        }
        catch (Exception ex)
        {
            if (brewing)
            {
                metrics.BrewFinished(TimeSpan.Zero);
            }
            logger.LogError(ex, "Не удалось приготовить заказ {OrderId}", order?.OrderId);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, CancellationToken.None);
        }
    }

    private static async Task PublishStatusAsync(IChannel channel, Guid orderId, OrderStatus status, CancellationToken ct)
    {
        var message = new OrderStatusChangedMessage(orderId, status, ServiceInstance.Name, DateTimeOffset.UtcNow);

        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: Queues.OrderStatusChanged,
            mandatory: false,
            basicProperties: new BasicProperties
            {
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent,
                Type = nameof(OrderStatusChangedMessage)
            },
            body: JsonSerializer.SerializeToUtf8Bytes(message),
            cancellationToken: ct);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (_channel is not null)
        {
            await _channel.CloseAsync(cancellationToken);
            await _channel.DisposeAsync();
        }
    }
}
