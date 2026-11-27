using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

// Намеренно используем namespace Microsoft.Extensions.Hosting: тогда методы AddServiceDefaults()
// и MapDefaultEndpoints() видны в Program.cs любого сервиса без дополнительного using.
namespace Microsoft.Extensions.Hosting;

// =====================================================================================
//  SERVICE DEFAULTS — "стандартная обвязка" каждого сервиса в Aspire-решении.
//
//  Это обычная библиотека (не магия!), которую подключает каждый сервис:
//      builder.AddServiceDefaults();   // в начале Program.cs
//      app.MapDefaultEndpoints();      // после builder.Build() (только для веб-сервисов)
//
//  Что она включает:
//    1. OpenTelemetry — логи, трейсы и метрики уходят в дашборд Aspire (по OTLP).
//    2. Health checks — эндпоинты /health и /alive, их опрашивает AppHost.
//    3. Service Discovery — HttpClient понимает адреса вида "https+http://menuapi".
//    4. Resilience — каждый HttpClient получает ретраи, таймауты и circuit breaker (Polly).
//
//  Файл сгенерирован шаблоном `aspire-servicedefaults` и его МОЖНО и НУЖНО менять
//  под свой проект — что мы и сделали (см. пометки "CoffeeShop:").
// =====================================================================================
public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    /// <summary>
    /// CoffeeShop: префикс наших собственных ActivitySource и Meter.
    /// Всё, что начинается с "CoffeeShop.", будет экспортироваться в дашборд.
    /// Примеры: "CoffeeShop.Barista" (трейсы бариста), "CoffeeShop.Orders" (метрики заказов).
    /// </summary>
    public const string TelemetryPrefix = "CoffeeShop.";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        // Service Discovery: регистрирует резолвер, который превращает логические имена
        // ресурсов ("menuapi") в реальные адреса. Адреса AppHost передаёт через переменные
        // окружения вида services__menuapi__https__0=https://localhost:7xxx
        builder.Services.AddServiceDiscovery();

        // Настройки по умолчанию для ВСЕХ HttpClient, созданных через IHttpClientFactory.
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Стандартный пайплайн устойчивости (Microsoft.Extensions.Http.Resilience):
            // rate limiter → общий таймаут → ретраи (3 попытки, экспоненциальная задержка)
            // → circuit breaker → таймаут попытки. Попробуй включить "сбои" в MenuApi через
            // команду в дашборде и посмотри в трейсах, как HttpClient повторяет запросы.
            http.AddStandardResilienceHandler();

            // Разрешать "https+http://имя-сервиса" через Service Discovery.
            http.AddServiceDiscovery();
        });

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        // Логи: ILogger → OpenTelemetry. В дашборде это вкладка "Structured logs".
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            // Метрики: вкладка "Metrics" в дашборде.
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()   // http.server.request.duration и т.п.
                    .AddHttpClientInstrumentation()      // исходящие HTTP-запросы
                    .AddRuntimeInstrumentation()         // GC, потоки, память
                    .AddMeter(TelemetryPrefix + "*");    // CoffeeShop: наши метрики (Meter "CoffeeShop.*")
            })
            // Трейсы (распределённая трассировка): вкладка "Traces" в дашборде.
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
                    .AddSource(TelemetryPrefix + "*")    // CoffeeShop: наши ActivitySource
                    .AddAspNetCoreInstrumentation(tracing =>
                        // Не засоряем трейсы опросами health check-ов.
                        tracing.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                            && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath)
                    )
                    .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        // Куда отправлять телеметрию? AppHost сам передаёт сервису переменную
        // OTEL_EXPORTER_OTLP_ENDPOINT с адресом дашборда. Если сервис запущен без Aspire —
        // переменной нет, и экспорт просто не включается.
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        // В облаке можно добавить, например, Azure Monitor (пакет Azure.Monitor.OpenTelemetry.AspNetCore):
        //if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        //{
        //    builder.Services.AddOpenTelemetry().UseAzureMonitor();
        //}

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            // "self" — проверка живости: процесс отвечает, значит жив. Тег "live" → эндпоинт /alive.
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        // Клиентские интеграции Aspire (AddNpgsqlDbContext, AddRedisDistributedCache,
        // AddRabbitMQClient) САМИ добавляют сюда свои проверки: "доступна ли база",
        // "доступен ли Redis" и т.д. Поэтому /health у MenuApi станет Unhealthy, если упадёт Redis.

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Публиковать health-эндпоинты наружу в production небезопасно (раскрывают детали),
        // поэтому шаблон включает их только в Development. Подробнее: https://aka.ms/aspire/healthchecks
        if (app.Environment.IsDevelopment())
        {
            // /health — "готов ли сервис принимать трафик" (все проверки, включая БД/Redis/RabbitMQ).
            // Именно его опрашивает AppHost (.WithHttpHealthCheck("/health")) и от него зависит WaitFor().
            app.MapHealthChecks(HealthEndpointPath);

            // /alive — "жив ли процесс" (только проверки с тегом "live").
            app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("live")
            });
        }

        return app;
    }
}
