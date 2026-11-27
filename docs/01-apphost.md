# Урок 1. AppHost и модель приложения

**Где в коде:** [`src/CoffeeShop.AppHost/AppHost.cs`](../src/CoffeeShop.AppHost/AppHost.cs),
[`aspire.config.json`](../aspire.config.json)

## AppHost — обычная консольная программа

```csharp
var builder = DistributedApplication.CreateBuilder(args);
// ... описываем ресурсы ...
builder.Build().Run();
```

Проект AppHost — это обычный `Microsoft.NET.Sdk` с пакетом `Aspire.Hosting.AppHost`. Он ничего не
делает с заказами или кофе. Его задача — **описать** систему. Посмотри его `.csproj`: там только
пакеты хостинговых интеграций (`Aspire.Hosting.PostgreSQL`, `...Redis`, `...RabbitMQ`, `...Dotnet`,
`...Docker`) и **нет** ссылок на проекты сервисов. Сервисы подключаются по пути к `.csproj`.

## Три вида ресурсов в CoffeeShop

### 1. Контейнеры (инфраструктура)

```csharp
var postgres = builder.AddPostgres("postgres", password: postgresPassword);
var menuDb   = postgres.AddDatabase("menudb");
var cache    = builder.AddRedis("cache");
var messaging = builder.AddRabbitMQ("messaging");
```

Хостинговая интеграция знает, какой Docker-образ взять, какие порты открыть, как проверить
здоровье и как сформировать строку подключения. `AddDatabase` — «дочерний» ресурс: Aspire сам
выполнит `CREATE DATABASE menudb`, когда сервер поднимется.

### 2. .NET-проекты (наши сервисы)

```csharp
var menuApi = builder.AddDotnetProject("menuapi", "../CoffeeShop.MenuApi/CoffeeShop.MenuApi.csproj");
```

Первый аргумент — **имя ресурса**. Оно используется везде: в дашборде, в service discovery
(`https+http://menuapi`), в переменных окружения. Порты Aspire берёт из
`Properties/launchSettings.json` проекта.

> ℹ️ В Aspire 8–9 писали `builder.AddProject<Projects.CoffeeShop_MenuApi>("menuapi")` — с
> автогенерируемым классом `Projects.*`. В Aspire 17 это устаревший API: `AddDotnetProject` работает
> с путём и умеет запускать даже file-based C# приложения (`app.cs`).

### 3. Параметры

```csharp
var secondsPerDrink = builder.AddParameter("barista-seconds-per-drink");
```

Подробно — в [уроке 5](05-parameters-and-secrets.md).

## Связи: `WithReference`

`WithReference` говорит: «ресурсу A нужен ресурс B». Aspire превращает это в **переменные окружения**
процесса A. Вот что реально получил `ordersapi` (вывод `aspire describe ordersapi --format Json`, пароли скрыты):

```text
ConnectionStrings__ordersdb   = Host=localhost;Port=21555;Username=postgres;Password=***;Database=ordersdb
ConnectionStrings__messaging  = amqp://guest:***@localhost:21557
ORDERSDB_HOST=localhost  ORDERSDB_PORT=21555  ORDERSDB_DATABASENAME=ordersdb  ORDERSDB_URI=postgresql://...
MESSAGING_HOST=localhost MESSAGING_PORT=21557 MESSAGING_USERNAME=guest  MESSAGING_URI=amqp://...
services__menuapi__https__0   = https://localhost:7218       ← для service discovery
services__menuapi__http__0    = http://localhost:5054
MENUAPI_HTTPS=https://localhost:7218   MENUAPI_HTTP=http://localhost:5054
OTEL_SERVICE_NAME=ordersapi
OTEL_EXPORTER_OTLP_ENDPOINT=...       ← куда слать телеметрию (дашборд)
```

Как это читает сервис:

| Переменная | Кто читает |
|---|---|
| `ConnectionStrings__ordersdb` | `builder.AddNpgsqlDbContext<OrdersDbContext>("ordersdb")` → `Configuration.GetConnectionString("ordersdb")` |
| `services__menuapi__https__0` | Service Discovery, когда HttpClient идёт на `https+http://menuapi` |
| `ORDERSDB_HOST`, `MESSAGING_URI` … | Удобный формат для не-.NET сервисов (Node.js, Python, Go) |
| `OTEL_*` | OpenTelemetry в ServiceDefaults |

> 💡 Двойное подчёркивание `__` в имени переменной = двоеточие `:` в конфигурации .NET.
> `ConnectionStrings__ordersdb` → `ConnectionStrings:ordersdb`.

## Порядок запуска: `WaitFor` и `WaitForCompletion`

Без ожиданий Aspire запустил бы всё параллельно, и API упали бы, не найдя таблиц в базе.

```csharp
var migrations = builder.AddDotnetProject("migrations", ...)
    .WithReference(menuDb).WaitFor(menuDb);          // ждать, пока база Healthy

var menuApi = builder.AddDotnetProject("menuapi", ...)
    .WaitFor(cache)                                  // ждать, пока Redis Healthy
    .WaitForCompletion(migrations);                  // ждать, пока migrations ЗАВЕРШИТСЯ с кодом 0

builder.AddDotnetProject("web", ...)
    .WaitFor(menuApi).WaitFor(ordersApi);            // ждать, пока API Healthy (/health → 200)
```

Получается цепочка:

```
postgres ─► menudb/ordersdb ─► migrations (Finished) ─► menuapi, ordersapi ─► web
cache ─────────────────────────────────────────────────► menuapi
messaging ─────────────────────────────────────────────► ordersapi, barista
```

Что значит «Healthy» для каждого ресурса:
- для контейнеров — встроенная проверка интеграции (например, Postgres отвечает на запрос);
- для наших API — `.WithHttpHealthCheck("/health")`: Aspire опрашивает эндпоинт, который добавил
  `app.MapDefaultEndpoints()` из ServiceDefaults.

> ⚠️ `WithReference` и `WaitFor` — **разные вещи**. Ссылка без ожидания допустима (например,
> `menuapi` ссылается на `menudb`, но ждёт не её, а `migrations`, который сам ждёт базу).

## Реплики: `WithReplicas`

```csharp
menuApi.WithReplicas(2);
barista.WithReplicas(2);
```

Aspire запускает две копии процесса. Для `menuapi` он ставит перед ними прокси на «главном» порту
(7218), поэтому потребители по-прежнему ходят на `menuapi`, ничего не зная о репликах. На главной
странице приложения видно, какая реплика отдала меню.

> 🔎 **Нюанс, который мы обнаружили при проверке.** Прокси балансирует **TCP-соединения**, а не
> отдельные HTTP-запросы. HttpClient во `web` держит соединение открытым и переиспользует его,
> поэтому подряд идущие запросы обычно попадают в **одну** реплику. Убедиться в балансировке можно,
> открывая новое соединение на каждый запрос:
>
> ```powershell
> 1..8 | ForEach-Object { (Invoke-WebRequest https://localhost:7218/menu -UseBasicParsing -DisableKeepAlive).Headers['X-Instance'] } | Group-Object
> ```
>
> Результат будет примерно 4 / 4 между `menuapi-xxxx` и `menuapi-yyyy`. В продакшене (Kubernetes,
> Azure Container Apps) балансировка устроена по-своему, но идея та же: реплики прячутся за одним адресом.

Для `barista` реплики — это **конкурирующие потребители** очереди RabbitMQ: в колонке «Бариста» на
странице «Заказы» видно, как заказы распределяются.

## Внешние эндпоинты

```csharp
builder.AddDotnetProject("web", ...).WithExternalHttpEndpoints();
```

Локально это ничего не меняет. При публикации (урок 8) только `web` получит порт, проброшенный
наружу (`ports:` в docker-compose), остальные сервисы останутся во внутренней сети. Тест
`OnlyWebIsExposedExternally` в `AppModelTests.cs` следит за этим правилом.

## Попробуй сам

- [ ] Выполни `aspire describe menuapi --format Json` и найди в выводе `ConnectionStrings__menudb` и `ConnectionStrings__cache`.
- [ ] Закомментируй `.WaitForCompletion(migrations)` у `menuapi`, удали тома (урок 6) и запусти. Что увидишь в логах `menuapi` при первом запросе меню? Верни строку обратно.
- [ ] Поменяй `WithReplicas(2)` у `barista` на `3`. Нажми команду «Час пик» у `ordersapi` и посмотри колонку «Бариста».
- [ ] Переименуй ресурс `menuapi` → `menu` в AppHost. Что ещё придётся поменять, чтобы всё заработало? (Подсказка: ищи строку `"https+http://menuapi"`.)

➡️ Дальше: [Урок 2. ServiceDefaults](02-service-defaults.md)
