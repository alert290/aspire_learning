using System.Text.Json;
using RabbitMQ.Client;

namespace CoffeeShop.OrdersApi.Messaging;

/// <summary>
/// Публикует JSON-сообщения в очередь RabbitMQ.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IConnection"/> зарегистрировала клиентская интеграция Aspire
/// (<c>builder.AddRabbitMQClient("messaging")</c>): она прочитала строку подключения
/// <c>ConnectionStrings:messaging</c> (amqp://guest:пароль@localhost:порт), включила
/// ретраи подключения, health check и трейсинг.
/// </para>
/// <para>
/// Трейсинг: RabbitMQ.Client 7 сам кладёт контекст трейса (traceparent) в заголовки
/// сообщения, а потребитель его достаёт. Поэтому в дашборде цепочка
/// Web → OrdersApi → RabbitMQ → BaristaWorker видна как ОДИН распределённый трейс.
/// </para>
/// </remarks>
public sealed class RabbitMqPublisher(IConnection connection)
{
    public async Task PublishAsync<T>(string queue, T message, CancellationToken ct)
    {
        // Учебное упрощение: новый канал на каждую публикацию. Это просто и безопасно
        // для многопоточности; в нагруженной системе каналы переиспользуют (пул).
        await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);

        // Объявление очереди идемпотентно: если она уже есть с теми же параметрами — ничего не произойдёт.
        await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);

        var properties = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,   // сообщение переживёт рестарт брокера
            MessageId = Guid.NewGuid().ToString(),
            Type = typeof(T).Name
        };

        var body = JsonSerializer.SerializeToUtf8Bytes(message);

        // exchange "" (default exchange) + routingKey = имя очереди → сообщение попадёт прямо в очередь.
        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: queue,
            mandatory: false,
            basicProperties: properties,
            body: body,
            cancellationToken: ct);
    }
}
