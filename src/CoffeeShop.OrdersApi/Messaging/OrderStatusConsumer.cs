using System.Text.Json;
using CoffeeShop.Contracts;
using CoffeeShop.Data.Orders;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace CoffeeShop.OrdersApi.Messaging;

/// <summary>
/// Фоновый потребитель очереди <see cref="Queues.OrderStatusChanged"/>:
/// бариста сообщает "начал готовить" / "готово", а мы обновляем заказ в базе.
/// </summary>
public sealed class OrderStatusConsumer(
    IConnection connection,
    IServiceScopeFactory scopeFactory,
    OrdersMetrics metrics,
    ILogger<OrderStatusConsumer> logger) : BackgroundService
{
    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await _channel.QueueDeclareAsync(Queues.OrderStatusChanged, durable: true, exclusive: false,
            autoDelete: false, cancellationToken: stoppingToken);

        // Не больше 10 неподтверждённых сообщений одновременно на этого потребителя.
        await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 10, global: false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += (_, delivery) => HandleAsync(_channel, delivery, stoppingToken);

        // autoAck: false — подтверждаем вручную ПОСЛЕ успешной записи в базу.
        await _channel.BasicConsumeAsync(Queues.OrderStatusChanged, autoAck: false, consumer, stoppingToken);

        logger.LogInformation("Слушаю очередь {Queue}", Queues.OrderStatusChanged);

        // Сообщения обрабатываются в колбэке; здесь просто ждём остановки приложения.
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Нормальная остановка.
        }
    }

    private async Task HandleAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken ct)
    {
        try
        {
            var message = JsonSerializer.Deserialize<OrderStatusChangedMessage>(delivery.Body.Span)
                          ?? throw new InvalidOperationException("Пустое сообщение");

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

            var order = await db.Orders.FindAsync([message.OrderId], ct);
            if (order is null)
            {
                logger.LogWarning("Заказ {OrderId} не найден — сообщение пропущено", message.OrderId);
            }
            // Статусы только "движутся вперёд": защищаемся от сообщений, пришедших не по порядку.
            else if (message.Status > order.Status)
            {
                order.Status = message.Status;
                order.PreparedBy = message.PreparedBy;
                if (message.Status == OrderStatus.Ready)
                {
                    order.ReadyAt = message.ChangedAt;
                }

                await db.SaveChangesAsync(ct);

                if (order.Status == OrderStatus.Ready)
                {
                    metrics.OrderReady(order);
                }

                logger.LogInformation("Заказ {OrderId}: статус {Status} (бариста {Barista})",
                    order.Id, order.Status, message.PreparedBy);
            }

            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Ошибка обработки сообщения о статусе заказа");

            // requeue: false — не зацикливаемся на "битом" сообщении. В продакшене такие
            // сообщения отправляют в dead-letter очередь для последующего разбора.
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, ct);
        }
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
