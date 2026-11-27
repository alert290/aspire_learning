# Урок 4. Дашборд и телеметрия

Дашборд Aspire — главный инструмент разработчика распределённой системы. Он показывает **состояние**
ресурсов и собирает **телеметрию** (логи, трейсы, метрики) со всех сервисов по протоколу OpenTelemetry (OTLP).

## Вкладки дашборда

| Вкладка | Что показывает | Что посмотреть в CoffeeShop |
|---|---|---|
| **Overview / Resources** | Ресурсы, их состояние и health, ссылки, переменные окружения, граф зависимостей | `migrations` → Finished; реплики `menuapi`; ссылки «Manage» |
| **Console logs** | Сырой stdout/stderr процесса или контейнера | Логи старта PostgreSQL или RabbitMQ |
| **Structured logs** | Логи `ILogger` с полями, фильтрами и привязкой к трейсу | Фильтр по `OrderId` |
| **Traces** | Распределённые трейсы: дерево спанов через все сервисы | Путь заказа от клика до «Готов» |
| **Metrics** | Графики метрик по ресурсам | `CoffeeShop.Orders`, `CoffeeShop.Barista` |

> 💡 Почти всё доступно и из терминала: `aspire describe`, `aspire logs <ресурс> -f`,
> `aspire otel logs|traces|spans`. Это удобно для скриптов и для AI-ассистентов.

## Трейсы: путь заказа

Оформи заказ в приложении и найди в **Traces** трейс, который начинается со спана
`Event onclick -> CoffeeShop.Web.Components.Pages.CartPage.PlaceOrderAsync`. Это реальная структура
такого трейса (19 спанов):

```
web        Event onclick -> CartPage.PlaceOrderAsync        ← клик в Blazor (.NET 10 трассирует компоненты)
web        └─ POST                                          ← HttpClient → ordersapi
ordersapi     └─ POST /orders/
ordersapi        ├─ GET  → menuapi /menu/                   ← service discovery + resilience
menuapi          │    └─ HMGET (Redis)                      ← кэш попал
ordersapi        ├─ postgresql INSERT INTO "Orders" ...     ← EF Core
ordersapi        └─ publish orders.placed                   ← RabbitMQ (producer)
barista-xxxx           └─ deliver orders.placed             ← RabbitMQ (consumer) — другой процесс!
barista-xxxx              └─ Brew order                     ← НАШ спан (ActivitySource "CoffeeShop.Barista")
barista-xxxx                 ├─ publish orders.status (Brewing)
ordersapi                    │    └─ deliver orders.status → UPDATE "Orders" ...
barista-xxxx                 └─ publish orders.status (Ready)
ordersapi                         └─ deliver orders.status → UPDATE "Orders" ...
```

Обрати внимание на три вещи:
1. Трейс **пересекает границы процессов**: HTTP (заголовок `traceparent`) и RabbitMQ (заголовки сообщения).
2. SQL-запросы, команды Redis и операции RabbitMQ попали в трейс **без нашего кода**: это сделали клиентские интеграции.
3. У спана `Brew order` есть атрибуты `coffeeshop.order.id`, `coffeeshop.barista` и события «Латте × 2 готово».

### Свой спан: `ActivitySource`

[Barista.cs](../src/CoffeeShop.BaristaWorker/Barista.cs):

```csharp
private static readonly ActivitySource ActivitySource = new("CoffeeShop.Barista");

using var activity = ActivitySource.StartActivity("Brew order");
activity?.SetTag("coffeeshop.order.id", order.OrderId);
activity?.AddEvent(new ActivityEvent($"{line.ItemName} × {line.Quantity} готово"));
```

`ActivitySource` — стандартный .NET API (`System.Diagnostics`), а не Aspire-специфичный. Чтобы спаны
попали в дашборд, источник должен быть «подписан» в OpenTelemetry. ServiceDefaults подписывается на
все `CoffeeShop.*`. Ещё один пример — `CoffeeShop.Migrations` в
[DatabaseInitializer.cs](../src/CoffeeShop.MigrationService/DatabaseInitializer.cs).

## Структурированные логи

```csharp
logger.LogInformation("Заказ {OrderId} от {Customer} на сумму {Total} ₽ принят", order.Id, ...);
```

Плейсхолдеры `{OrderId}` становятся **полями** записи. В **Structured logs** можно отфильтровать
`OrderId = <guid>` и увидеть записи **из разных сервисов** про один заказ. А у каждой записи есть
ссылка на трейс, в рамках которого она написана.

`BaristaWorker` дополнительно использует **scope**:

```csharp
using var _ = logger.BeginScope(new Dictionary<string, object> { ["OrderId"] = order.OrderId });
```

Теперь все логи внутри обработки заказа получают поле `OrderId`, даже если в тексте его нет.

Из терминала: `aspire otel logs barista --search "Латте"`.

## Метрики

Свои метрики создаются через `System.Diagnostics.Metrics` и `IMeterFactory`
([OrdersMetrics.cs](../src/CoffeeShop.OrdersApi/OrdersMetrics.cs), [BaristaMetrics.cs](../src/CoffeeShop.BaristaWorker/BaristaMetrics.cs)):

| Метрика | Тип | Ресурс |
|---|---|---|
| `coffeeshop.orders.placed` | Counter | ordersapi |
| `coffeeshop.orders.ready` | Counter | ordersapi |
| `coffeeshop.items.ordered` (тег `item`) | Counter | ordersapi |
| `coffeeshop.orders.value` (₽) | Histogram | ordersapi |
| `coffeeshop.orders.time_to_ready` (с) | Histogram | ordersapi |
| `coffeeshop.barista.brew_time` (с) | Histogram | barista |
| `coffeeshop.barista.orders_in_progress` | UpDownCounter | barista |

Нажми команду «Час пик» несколько раз и открой **Metrics → ordersapi → CoffeeShop.Orders**. У
`coffeeshop.items.ordered` переключи фильтр по тегу `item`: увидишь самые популярные напитки.

Встроенные метрики тоже стоит посмотреть: `http.server.request.duration` (ASP.NET Core),
`http.client.request.duration` (HttpClient), `dotnet.gc.*` (runtime).

## Команды ресурсов

AppHost может добавить ресурсу **свои кнопки**. Самый простой вариант — HTTP-команда:

```csharp
menuApi.WithHttpCommand(
    path: "/admin/cache/clear",          // куда отправить POST
    displayName: "Сбросить кэш меню",    // подпись в дашборде
    commandName: "clear-menu-cache",     // имя для CLI
    commandOptions: new HttpCommandOptions
    {
        IconName = "Broom",              // иконка из Fluent UI
        ConfirmationMessage = "Очистить кэш меню в Redis?",
        IsHighlighted = false            // true — кнопка видна прямо в строке ресурса
    });
```

В дашборде команды находятся в меню **«…»** у ресурса (в списке ресурсов слева), а команда с
`IsHighlighted = true` видна прямо в строке ресурса.

| Ресурс | Команда | CLI |
|---|---|---|
| `menuapi` | Сбросить кэш меню | `aspire resource menuapi-<суффикс> clear-menu-cache` |
| `menuapi` | Вкл/выкл сбои | `aspire resource menuapi-<суффикс> toggle-chaos` |
| `ordersapi` | Час пик: 5 заказов | `aspire resource ordersapi rush-hour` |

У ресурса с репликами в CLI нужно указать имя конкретной реплики, например `menuapi-kcjqvkgg`
(его показывает `aspire describe`). Но HTTP-команда всё равно идёт через общий прокси, поэтому
выполнить её может любая реплика. Состояние, которое меняют наши команды (кэш, флаг сбоев), лежит в
Redis и общее для всех реплик, так что это не важно.

Встроенные команды тоже есть: Stop, Restart, а для .NET-проектов ещё **Rebuild** (пересобрать
и перезапустить один сервис, не останавливая остальные). В том же меню **View JSON** показывает
полное описание ресурса, а **Export .env** выгружает его переменные окружения.

> Для произвольной логики (не HTTP) есть `WithCommand(name, displayName, executeCommand: async ctx => ...)`:
> колбэк выполняется прямо в процессе AppHost. Попробуй сделать такую команду в упражнениях.

## Ссылки в дашборде

```csharp
web.WithUrlForEndpoint("https", url => url.DisplayText = "☕ Открыть CoffeeShop");
web.WithUrlForEndpoint("http", url => url.DisplayLocation = UrlDisplayLocation.DetailsOnly);
menuApi.WithUrlForEndpoint("https", _ => new() { Url = "/openapi/v1.json", DisplayText = "OpenAPI" });
```

- Первая строка меняет подпись ссылки.
- Вторая прячет HTTP-ссылку в детали ресурса, чтобы не загромождать список.
- Третья **добавляет** ссылку: относительный путь склеивается с адресом эндпоинта.

## Попробуй сам

- [ ] Найди в **Traces** спан `GET /menu/` у `menuapi`, в котором был **промах** кэша. Какие дочерние спаны у него есть?
- [ ] Включи «сбои» и найди трейс с ошибкой. Как дашборд выделяет неудачные спаны? Найди его в терминале: `aspire otel traces --has-error true`.
- [ ] Добавь метрику `coffeeshop.menu.cache_hits` (Counter с тегом `result` = `hit`/`miss`) в MenuApi и найди её в дашборде.
- [ ] Добавь в `OrderService` свой спан `Validate order` с тегом количества позиций.

➡️ Дальше: [Урок 5. Параметры и секреты](05-parameters-and-secrets.md)
