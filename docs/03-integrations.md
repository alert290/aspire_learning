# Урок 3. Интеграции: PostgreSQL + EF Core, Redis, RabbitMQ

## Две половинки каждой интеграции

У каждой технологии в Aspire обычно **два** пакета:

| | Хостинговая интеграция | Клиентская интеграция |
|---|---|---|
| Где подключается | в **AppHost** | в **сервисе** |
| Имя пакета | `Aspire.Hosting.<Технология>` | `Aspire.<Клиентская библиотека>` |
| Что делает | поднимает контейнер, выдаёт строку подключения, проверяет здоровье | регистрирует клиент в DI, читает строку подключения, добавляет ретраи, health check, трейсы и метрики |
| Пример | `builder.AddPostgres("postgres").AddDatabase("menudb")` | `builder.AddNpgsqlDbContext<MenuDbContext>("menudb")` |

Связывает их **имя ресурса** (`"menudb"`): хостинг кладёт `ConnectionStrings__menudb`, клиент читает
`ConnectionStrings:menudb`.

> 💡 Клиентская интеграция **не обязательна**. Можно взять обычный `AddDbContext(... UseNpgsql(...))`
> и прочитать строку подключения сам. Но тогда health check, ретраи и трейсинг придётся настраивать
> вручную. Интеграция делает это одной строкой.

В CoffeeShop используются:

| Технология | AppHost | Сервис |
|---|---|---|
| PostgreSQL + EF Core | `Aspire.Hosting.PostgreSQL` | `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL` (MenuApi, OrdersApi, MigrationService) |
| Redis (кэш) | `Aspire.Hosting.Redis` | `Aspire.StackExchange.Redis.DistributedCaching` (MenuApi) |
| RabbitMQ | `Aspire.Hosting.RabbitMQ` | `Aspire.RabbitMQ.Client` (OrdersApi, BaristaWorker) |

Добавлять интеграции удобно через CLI: `aspire add redis` сам найдёт и поставит пакет в AppHost.

---

## PostgreSQL + EF Core

**Где в коде:** [AppHost.cs](../src/CoffeeShop.AppHost/AppHost.cs), [CoffeeShop.Data](../src/CoffeeShop.Data),
[MigrationService](../src/CoffeeShop.MigrationService), [MenuApi/Program.cs](../src/CoffeeShop.MenuApi/Program.cs).

```csharp
// AppHost
var postgres = builder.AddPostgres("postgres", password: postgresPassword).WithDataVolume();
var menuDb   = postgres.AddDatabase("menudb");
var ordersDb = postgres.AddDatabase("ordersdb");

// MenuApi/Program.cs
builder.AddNpgsqlDbContext<MenuDbContext>("menudb");
```

Один контейнер, две логические базы: у каждого сервиса свои данные. `AddNpgsqlDbContext` включает:
- пул `DbContext`;
- ретраи при временных ошибках (`EnableRetryOnFailure`);
- health check «база отвечает»;
- трейсинг. Каждый SQL-запрос виден в дашборде как спан с текстом запроса.

### Миграции: паттерн «сервис миграций»

`DbContext`-ы и миграции лежат в библиотеке `CoffeeShop.Data`. Применяет их **отдельный сервис**
[`MigrationService`](../src/CoffeeShop.MigrationService/DatabaseInitializer.cs), который:
1. создаёт базу, если её нет;
2. применяет миграции (`MigrateAsync`) внутри execution strategy, чтобы при временном сбое операция повторилась;
3. наполняет меню начальными данными;
4. **завершает процесс**. В AppHost API ждут его через `.WaitForCompletion(migrations)`.

Почему не вызвать `Migrate()` прямо в API? У `menuapi` две реплики, и они бы одновременно пытались
мигрировать одну базу. Отдельный одноразовый сервис решает это просто и надёжно.

> 🔎 **Шаг 1 (создание базы) — тонкий момент.** При `aspire run` базу создаёт AppHost
> (`AddDatabase`). В Docker Compose AppHost'а нет, есть только «голый» сервер PostgreSQL.
> Первая версия этого проекта забыла об этом, и `aspire deploy` упал с ошибкой
> `3D000: database "menudb" does not exist`. Мораль: всё, что AppHost делает «за кадром» при
> локальном запуске, в продакшене должен делать кто-то другой.

### Как добавить новую миграцию

```powershell
dotnet tool restore     # ставит локальный dotnet-ef из dotnet-tools.json
dotnet ef migrations add AddMenuItemCalories --project src/CoffeeShop.Data --context MenuDbContext --output-dir Menu/Migrations
```

Строку подключения при этом подставлять не нужно: для `dotnet ef` в проекте есть design-time фабрики
([DesignTimeFactories.cs](../src/CoffeeShop.Data/DesignTimeFactories.cs)). При следующем `aspire run`
MigrationService применит новую миграцию сам.

### Про версию пакета 13.6.5

Почти все пакеты Aspire в проекте версии **17.0.0**, но `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL`
версии **13.6.5**. Для 17.0 этот пакет пока выходит только как preview под EF Core 11 / .NET 11,
а мы на .NET 10 (LTS) и EF Core 10. Хостинговые и клиентские пакеты общаются **только через строку
подключения**, поэтому смешивать их версии безопасно. На практике такое встречается нередко.

---

## Redis: распределённый кэш

**Где в коде:** [MenuEndpoints.cs](../src/CoffeeShop.MenuApi/MenuEndpoints.cs)

```csharp
// AppHost
var cache = builder.AddRedis("cache").WithRedisInsight();

// MenuApi/Program.cs
builder.AddRedisDistributedCache("cache");   // → IDistributedCache
```

Паттерн **cache-aside**: сначала смотрим в Redis, при промахе идём в PostgreSQL и кладём результат в
кэш на 60 секунд. В ответе MenuApi есть заголовок `X-Cache: HIT/MISS`, и главная страница показывает,
откуда пришли данные.

Почему кэш именно в Redis, а не в памяти процесса? У `menuapi` две реплики. In-memory кэш был бы у
каждой свой, а Redis — общий: после сброса кэша **обе** реплики пойдут в базу.

Обрати внимание: Redis в Aspire 17 по умолчанию запускается **с паролем**. Интеграция сама создала
параметр `cache-password` и вписала его в строку подключения, тебе ничего делать не нужно.

---

## RabbitMQ: обмен сообщениями

**Где в коде:** [RabbitMqPublisher.cs](../src/CoffeeShop.OrdersApi/Messaging/RabbitMqPublisher.cs),
[OrderStatusConsumer.cs](../src/CoffeeShop.OrdersApi/Messaging/OrderStatusConsumer.cs),
[Barista.cs](../src/CoffeeShop.BaristaWorker/Barista.cs), [Messages.cs](../src/CoffeeShop.Contracts/Messages.cs)

```csharp
// AppHost
var messaging = builder.AddRabbitMQ("messaging").WithManagementPlugin();

// OrdersApi/Program.cs, BaristaWorker/Program.cs
builder.AddRabbitMQClient("messaging");   // → RabbitMQ.Client.IConnection (singleton)
```

Поток сообщений:

```
OrdersApi ──OrderPlacedMessage──► [orders.placed] ──► barista (2 реплики, prefetch=1)
OrdersApi ◄─OrderStatusChanged─── [orders.status] ◄── barista (Brewing, затем Ready)
```

Что даёт интеграция:
- `IConnection` с повторными попытками подключения (брокер может стартовать дольше сервиса);
- health check «брокер доступен»;
- **трейсинг**: RabbitMQ.Client 7 кладёт контекст трейса (`traceparent`) в заголовки сообщения, а
  потребитель его извлекает. Поэтому в дашборде `publish orders.placed` в OrdersApi и
  `deliver orders.placed` у бариста — части **одного** трейса. Своего кода для этого писать не пришлось.

Веб-консоль RabbitMQ (ссылка **Manage** у ресурса `messaging`) показывает очереди `orders.placed` и
`orders.status`, а также подключённых потребителей. Логин `guest`, пароль — значение сгенерированного
параметра `messaging-password`: `aspire secret get Parameters:messaging-password`.

---

## Попробуй сам

- [ ] Выполни команду **«Сбросить кэш меню»** у `menuapi` и обнови главную страницу. Где теперь взялись данные?
- [ ] Открой pgAdmin (ссылка **Manage** у `postgres`) и найди таблицу `Orders` в базе `ordersdb`.
- [ ] Останови обе реплики `barista` и оформи заказ. Где «застрял» заказ? Открой RabbitMQ Management и найди сообщение в очереди `orders.placed`. Запусти бариста снова.
- [ ] Добавь в меню поле `Calories`: свойство в `MenuItem`, миграция, DTO и вывод на странице.

➡️ Дальше: [Урок 4. Дашборд и телеметрия](04-dashboard-and-telemetry.md)
