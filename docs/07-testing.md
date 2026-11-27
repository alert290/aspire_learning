# Урок 7. Тестирование с Aspire.Hosting.Testing

**Где в коде:** [tests/CoffeeShop.Tests](../tests/CoffeeShop.Tests)

```powershell
dotnet test      # нужен запущенный Docker
```

Пакет `Aspire.Hosting.Testing` умеет запустить **твой AppHost** внутри теста: настоящие контейнеры,
настоящие процессы сервисов, настоящие сетевые вызовы. Тест получает доступ к модели приложения и
HTTP-клиенты к любому ресурсу.

В проекте два вида тестов.

## 1. Тесты модели (без запуска, без Docker)

[AppModelTests.cs](../tests/CoffeeShop.Tests/AppModelTests.cs)

```csharp
var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.CoffeeShop_AppHost>(args, ct);
await using var app = await appHost.BuildAsync(ct);          // построили, но НЕ запустили

var model = app.Services.GetRequiredService<DistributedApplicationModel>();
var waits = model.Resources.Single(r => r.Name == "menuapi").Annotations.OfType<WaitAnnotation>();
Assert.Contains(waits, w => w.Resource.Name == "migrations" && w.WaitType == WaitType.WaitForCompletion);
```

`Projects.CoffeeShop_AppHost` — класс, сгенерированный по `ProjectReference` на AppHost в `.csproj` тестов.

Всё, что ты пишешь в AppHost (`WithReference`, `WaitFor`, `WithReplicas`, `WithExternalHttpEndpoints`…),
превращается в **аннотации** на ресурсах. Тесты модели проверяют архитектурные правила за миллисекунды:

| Тест | Правило |
|---|---|
| `Model_ContainsAllExpectedResources` | все ресурсы на месте |
| `ApisWaitForMigrationsToComplete` | API не стартуют до завершения миграций |
| `ScaledServicesHaveReplicas` | у `menuapi` и `barista` по 2 реплики |
| `OnlyWebIsExposedExternally` | наружу торчит только `web` |

## 2. End-to-end тесты (с Docker)

[CoffeeShopAppFixture.cs](../tests/CoffeeShop.Tests/CoffeeShopAppFixture.cs),
[EndToEndTests.cs](../tests/CoffeeShop.Tests/EndToEndTests.cs)

Фикстура запускает всё приложение **один раз** на класс тестов (`IClassFixture`):

```csharp
string[] args =
[
    "Parameters:postgres-password=Test_Passw0rd_For_Integration",   // фиксированный пароль
    "Parameters:barista-seconds-per-drink=0.2"                      // бариста варит быстро
];
var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.CoffeeShop_AppHost>(args, ct);
// ... правим модель (см. ниже) ...
App = await appHost.BuildAsync(ct);
await App.StartAsync(ct);
await App.ResourceNotifications.WaitForResourceHealthyAsync("web", ct);
```

Обрати внимание на приёмы:
- **Параметры переопределяются аргументами**: они попадают в конфигурацию AppHost так же, как `appsettings.json`.
- **Ожидание готовности**: `WaitForResourceHealthyAsync("web")`. Так как `web` через `WaitFor` зависит
  от всего остального, его готовность означает готовность всей системы.
- **HTTP-клиент к ресурсу**: `App.CreateHttpClient("menuapi")`. Порты в тестах случайные, адрес знает только AppHost.

Сами тесты — обычный xUnit:

```csharp
[Fact]
public async Task PlacedOrder_GoesThroughRabbitMqAndBecomesReady()
{
    using var ordersHttp = fixture.App.CreateHttpClient("ordersapi");
    var response = await ordersHttp.PostAsJsonAsync("/orders", new PlaceOrderRequest(...));
    var order = await response.Content.ReadFromJsonAsync<OrderDto>();

    // OrdersApi → RabbitMQ → BaristaWorker → RabbitMQ → OrdersApi
    var ready = await WaitForStatusAsync(ordersHttp, order.Id, OrderStatus.Ready, ct);
    Assert.StartsWith("barista", ready.PreparedBy);
}
```

Этот тест проверяет то, что юнит-тестом не проверить: что сообщения действительно ходят через
брокер, бариста их обрабатывает, а статусы возвращаются в базу.

## Изоляция тестов от разработки (важно!)

Модель приложения можно **менять** перед запуском. Фикстура снимает с контейнеров persistent-режим и тома:

```csharp
foreach (var container in appHost.Resources.OfType<ContainerResource>())
{
    foreach (var a in container.Annotations
                 .Where(a => a is PersistenceAnnotation or ContainerLifetimeAnnotation or ContainerMountAnnotation)
                 .ToList())
    {
        container.Annotations.Remove(a);
    }
}
```

Зачем? Имя persistent-контейнера зависит от пути к AppHost и **одинаково** у тестов и у `aspire run`.
При проверке проекта первая версия фикстуры снимала только `ContainerLifetimeAnnotation`, и тест
«захватил» контейнер разработки `postgres-xxxx`: пересоздал его со своим тестовым паролем. Оказалось, что в
Aspire 17 `WithLifetime(Persistent)` ставит ещё и `PersistenceAnnotation` (новый, экспериментальный тип,
поэтому в коде стоит `#pragma warning disable ASPIREPERSISTENCE001`).

Как это нашли: вывели в тесте список аннотаций ресурса `postgres` и сравнили метку
`com.microsoft.developer.usvc-dev.creatorProcessStartTime` контейнера до и после `dotnet test`.
Сейчас тесты можно запускать прямо во время `aspire run`, и контейнеры разработки не пострадают.

## Советы

- Таймауты: первый запуск в CI может быть долгим (скачивание образов). В фикстуре стоит 5 минут на старт.
- Логи ресурсов в тестах: `appHost.Services.AddLogging(...)`. Чтобы видеть их в выводе xUnit, подключи
  пакет-адаптер логов для `ITestOutputHelper`.
- Тестов модели может быть много, они дешёвые. End-to-end тестов — несколько ключевых сценариев.

## Попробуй сам

- [ ] Удали `.WaitForCompletion(migrations)` у `ordersapi` и запусти `dotnet test --filter AppModelTests`. Какой тест упал?
- [ ] Напиши end-to-end тест: «после команды очистки кэша следующий запрос меню — MISS». Подсказка: эндпоинт `POST /admin/cache/clear` у `menuapi`.
- [ ] Напиши тест модели: «у `barista` есть переменная окружения `Barista__SecondsPerDrink`» (ищи `EnvironmentCallbackAnnotation` или используй `GetEnvironmentVariableValuesAsync`).

➡️ Дальше: [Урок 8. Публикация и деплой](08-deployment.md)
