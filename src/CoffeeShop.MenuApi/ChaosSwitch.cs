using Microsoft.Extensions.Caching.Distributed;

namespace CoffeeShop.MenuApi;

/// <summary>
/// "Рубильник хаоса" для демонстрации resilience.
/// </summary>
/// <remarks>
/// <para>
/// Когда включён, половина запросов к /menu получает 503. Вызывающая сторона (Web, OrdersApi)
/// использует HttpClient со стандартным resilience-пайплайном из ServiceDefaults, поэтому
/// пользователь почти не заметит сбоев — а в дашборде, во вкладке Traces, будут видны
/// повторные попытки (несколько дочерних HTTP-спанов внутри одного запроса).
/// </para>
/// <para>
/// Почему флаг хранится в Redis, а не в поле класса? У menuapi две реплики, а команда дашборда
/// отправляет HTTP-запрос на ОБЩИЙ адрес ресурса — прокси Aspire может передать его любой
/// реплике. Будь флаг в памяти процесса, "сбои" включились бы на случайной реплике.
/// Общее состояние в Redis видят все реплики сразу. (Тот же принцип, что и с кэшем меню.)
/// </para>
/// </remarks>
public sealed class ChaosSwitch(IDistributedCache cache)
{
    public const string CacheKey = "menu:chaos";
    public const double FailureRate = 0.5;

    public async Task<bool> IsEnabledAsync(CancellationToken ct) =>
        await cache.GetStringAsync(CacheKey, ct) is not null;

    /// <summary>Переключает режим и возвращает новое состояние.</summary>
    public async Task<bool> ToggleAsync(CancellationToken ct)
    {
        if (await IsEnabledAsync(ct))
        {
            await cache.RemoveAsync(CacheKey, ct);
            return false;
        }

        await cache.SetStringAsync(CacheKey, "on", ct);
        return true;
    }

    public async Task<bool> ShouldFailAsync(CancellationToken ct) =>
        await IsEnabledAsync(ct) && Random.Shared.NextDouble() < FailureRate;
}
