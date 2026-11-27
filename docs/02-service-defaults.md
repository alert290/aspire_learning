# Урок 2. ServiceDefaults: телеметрия, health checks, service discovery, resilience

**Где в коде:** [`src/CoffeeShop.ServiceDefaults/Extensions.cs`](../src/CoffeeShop.ServiceDefaults/Extensions.cs),
`Program.cs` каждого сервиса.

## Что это за проект

`CoffeeShop.ServiceDefaults` — обычная библиотека классов. Её создал шаблон `aspire-servicedefaults`,
и это **твой код**: его можно и нужно менять. Каждый сервис подключает её двумя строками:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();     // ← в начале
// ...
var app = builder.Build();
app.MapDefaultEndpoints();        // ← только у веб-сервисов
```

Что делает `AddServiceDefaults()` — четыре вещи.

## 1. OpenTelemetry

```csharp
builder.Logging.AddOpenTelemetry(...);          // логи
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation()
                       .AddRuntimeInstrumentation().AddMeter("CoffeeShop.*"))
    .WithTracing(t => t.AddSource("CoffeeShop.*")
                       .AddAspNetCoreInstrumentation().AddHttpClientInstrumentation());
```

- **Логи**: всё, что пишется через `ILogger`, уходит в дашборд как структурированные записи.
- **Метрики**: длительность HTTP-запросов, GC, потоки, плюс наши метрики из `Meter("CoffeeShop.*")`.
- **Трейсы**: входящие и исходящие HTTP-запросы, плюс наши спаны из `ActivitySource("CoffeeShop.*")`.

Экспорт включается, только если задана переменная `OTEL_EXPORTER_OTLP_ENDPOINT`. Её передаёт AppHost,
и в ней лежит адрес дашборда. Если запустить сервис без Aspire, телеметрия просто никуда не отправится.

> 💡 Клиентские интеграции (Npgsql, Redis, RabbitMQ) **сами** регистрируют свои источники трейсов.
> Поэтому в трейсе видны SQL-запросы и команды Redis, хотя в ServiceDefaults мы их не перечисляли.

**CoffeeShop-изменение:** мы добавили `AddMeter("CoffeeShop.*")` и `AddSource("CoffeeShop.*")`.
Без этого наши метрики `coffeeshop.orders.placed` и спаны `Brew order` в дашборд бы не попали.

## 2. Health checks

```csharp
builder.Services.AddHealthChecks().AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);
// ...
app.MapHealthChecks("/health");   // все проверки
app.MapHealthChecks("/alive", new() { Predicate = r => r.Tags.Contains("live") });
```

- `/alive` — «процесс жив» (только `self`);
- `/health` — «готов работать». Сюда клиентские интеграции **автоматически** добавляют свои проверки:
  `AddNpgsqlDbContext` проверяет базу, `AddRedisDistributedCache` проверяет Redis, `AddRabbitMQClient` проверяет брокер.

AppHost опрашивает `/health` (`.WithHttpHealthCheck("/health")`), а от результата зависят статус
в дашборде и `WaitFor`.

Проверь сам: открой `https://localhost:7218/health` (MenuApi). Потом останови `cache` в дашборде
и открой снова: получишь `Unhealthy`.

> ⚠️ Шаблон включает эти эндпоинты только в Development: в продакшене health-эндпоинты могут
> раскрывать лишнее и требуют отдельной настройки.

## 3. Service Discovery

```csharp
builder.Services.AddServiceDiscovery();
builder.Services.ConfigureHttpClientDefaults(http => http.AddServiceDiscovery());
```

После этого любой HttpClient понимает «логические» адреса:

```csharp
builder.Services.AddHttpClient<MenuApiClient>(c => c.BaseAddress = new("https+http://menuapi"));
```

Расшифровка `https+http://menuapi`:
- `menuapi` — имя ресурса из AppHost;
- `https+http` — «возьми HTTPS-эндпоинт, а если его нет — HTTP».

Резолвер ищет в конфигурации `services:menuapi:https:0`, а эту переменную передал AppHost
(см. урок 1). В Docker Compose туда попадёт `http://menuapi:8080`, и **код сервиса не изменится**.

Где используется:
- `Web` → `menuapi`, `ordersapi` ([Web/Program.cs](../src/CoffeeShop.Web/Program.cs));
- `OrdersApi` → `menuapi`, за ценами ([OrdersApi/Program.cs](../src/CoffeeShop.OrdersApi/Program.cs)).

## 4. Resilience (устойчивость)

```csharp
builder.Services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());
```

Каждый HttpClient получает стандартный пайплайн на основе Polly:

```
общий таймаут (30 с) → ретраи (до 3, экспоненциальная задержка) → circuit breaker → таймаут попытки (10 с)
```

### Демонстрация: включаем «сбои»

В MenuApi есть «рубильник хаоса» ([ChaosSwitch.cs](../src/CoffeeShop.MenuApi/ChaosSwitch.cs)):
когда он включён, 50% запросов к `/menu` отвечают `503`.

1. В дашборде у любой реплики `menuapi` открой меню «…» и выполни команду **«Вкл/выкл сбои»**.
   Из терминала: `aspire describe` покажет имена реплик, дальше `aspire resource menuapi-<суффикс> toggle-chaos`.
2. Несколько раз нажми «Обновить меню» в приложении. Меню почти всегда загружается, хотя иногда медленнее.
3. Открой **Traces** и найди трейс со спаном `GET` от `web` к `menuapi`. Внутри будет **несколько**
   попыток: первая с ошибкой 503 (красная), следующая успешная.
4. Выключи сбои той же командой.

При проверке проекта на 12 обновлений страницы пришлось 10 ответов 503. В 5 случаях HttpClient сам
повторил запрос, меню показалось 11 раз из 12. Один раз не помогли и 3 повтора (при 50% сбоев так бывает
примерно в 6% случаев), и страница показала аккуратное сообщение об ошибке. Ретраи **снижают**
вероятность отказа, но не отменяют её.

> 💡 Флаг «сбоев» хранится в **Redis**, а не в памяти процесса. Почему: HTTP-команда дашборда идёт на
> общий адрес ресурса, и прокси может доставить её **любой** реплике, даже если ты нажал кнопку у
> конкретной. Первая версия проекта хранила флаг в поле класса, и сбои включались не там, где ожидалось.
> Общее состояние для нескольких реплик должно жить во внешнем хранилище.

## Почему это вынесено в отдельный проект

Все сервисы получают одинаковую телеметрию, health checks и политику ретраев. Нужно поменять
таймаут или добавить экспорт в Azure Monitor? Правишь одно место.

## Попробуй сам

- [ ] Открой `/health` и `/alive` у `ordersapi`. Останови `messaging` в дашборде: какой из эндпоинтов изменился?
- [ ] Закомментируй `http.AddStandardResilienceHandler()` в ServiceDefaults, перезапусти, включи «сбои» и пообновляй меню. Почувствуй разницу. Верни строку обратно.
- [ ] Добавь в ServiceDefaults фильтр трейсов, исключающий запросы к `/openapi`.

➡️ Дальше: [Урок 3. Интеграции](03-integrations.md)
