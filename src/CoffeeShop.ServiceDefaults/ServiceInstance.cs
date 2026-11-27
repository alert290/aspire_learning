namespace Microsoft.Extensions.Hosting;

/// <summary>
/// CoffeeShop: имя текущего экземпляра (реплики) сервиса — такое же, как в дашборде.
/// </summary>
/// <remarks>
/// <para>
/// AppHost передаёт каждому процессу переменные окружения OpenTelemetry:
/// <c>OTEL_SERVICE_NAME=menuapi</c> и <c>OTEL_RESOURCE_ATTRIBUTES=service.instance.id=rsqretjk</c>.
/// Склеив их, получаем <c>menuapi-rsqretjk</c> — именно так реплика подписана в дашборде.
/// Мы используем это имя, чтобы показать в интерфейсе, какая реплика обработала запрос.
/// </para>
/// <para>Если сервис запущен без Aspire — используем имя машины и PID процесса.</para>
/// </remarks>
public static class ServiceInstance
{
    public static string Name { get; } = Resolve();

    private static string Resolve()
    {
        var serviceName = Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME");
        var instanceId = FindInstanceId(Environment.GetEnvironmentVariable("OTEL_RESOURCE_ATTRIBUTES"));

        return (serviceName, instanceId) switch
        {
            ({ Length: > 0 }, { Length: > 0 }) when !instanceId.StartsWith(serviceName) => $"{serviceName}-{instanceId}",
            (_, { Length: > 0 }) => instanceId,
            _ => $"{Environment.MachineName}-{Environment.ProcessId}"
        };
    }

    private static string? FindInstanceId(string? attributes)
    {
        foreach (var pair in (attributes ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts is ["service.instance.id", { Length: > 0 } value])
            {
                return value;
            }
        }

        return null;
    }
}
