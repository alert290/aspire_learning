# Урок 0. Что такое .NET Aspire и как устроен CoffeeShop

## Проблема, которую решает Aspire

Представь, что тебе нужно локально запустить систему из нескольких сервисов: два API, фронтенд,
фоновый обработчик, плюс PostgreSQL, Redis и RabbitMQ. Без Aspire это выглядит так:

- написать `docker-compose.yml` для инфраструктуры и не забывать его запускать;
- прописать в `appsettings.Development.json` каждого сервиса строки подключения и порты соседей
  (и следить, чтобы они совпадали);
- открыть 5 терминалов и запустить сервисы в правильном порядке;
- чтобы понять, почему заказ «завис», читать логи в пяти окнах и сопоставлять их по времени.

**Aspire** — это набор инструментов, который берёт всё это на себя:

| Часть Aspire | Что делает | В CoffeeShop |
|---|---|---|
| **AppHost** | C#-код, описывающий всю систему: ресурсы, связи, порядок запуска | `src/CoffeeShop.AppHost/AppHost.cs` |
| **Интеграции** | NuGet-пакеты: «как поднять Postgres» (хостинг) и «как к нему подключиться» (клиент) | `Aspire.Hosting.PostgreSQL`, `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL` и др. |
| **ServiceDefaults** | Общая обвязка сервисов: телеметрия, health checks, service discovery, ретраи | `src/CoffeeShop.ServiceDefaults` |
| **Дашборд** | Веб-панель: состояние ресурсов, логи, трейсы, метрики | открывается при `aspire run` |
| **Aspire CLI** | `aspire run / start / describe / logs / publish / deploy …` | установлен глобально |
| **Тестирование** | Запуск всей системы из теста | `tests/CoffeeShop.Tests` |
| **Публикация** | Превращение модели в артефакты деплоя (Docker Compose, Kubernetes, Azure…) | `aspire publish` |

> 💡 Важно понимать: Aspire **не** заменяет ASP.NET Core, EF Core или RabbitMQ.Client. Сервисы — это
> обычные .NET-приложения. Aspire добавляет «клей» между ними и инструменты вокруг них.

## Ключевые понятия

- **Ресурс (resource)** — всё, что AppHost запускает или о чём знает: контейнер (`postgres`),
  .NET-проект (`menuapi`), база внутри сервера (`menudb`), параметр (`postgres-password`).
- **Модель приложения (application model)** — граф ресурсов и связей между ними, построенный
  кодом `AppHost.cs`.
- **Ссылка (reference)** — `.WithReference(x)`: «этому ресурсу нужен x». Aspire передаёт строку
  подключения или адрес через переменные окружения.
- **Ожидание (wait)** — `.WaitFor(x)`: «не запускай, пока x не станет здоровым».
- **DCP** (Developer Control Plane) — внутренний оркестратор, который реально запускает процессы
  и контейнеры. Тебе с ним работать напрямую не нужно.

## Экскурсия: запускаем и смотрим

```powershell
aspire run
```

Открой ссылку на дашборд из вывода команды. Слева — **список ресурсов** (кнопкой ☰ рядом с
«All resources» его можно скрыть или показать), в центре — вкладки **Overview, Console logs,
Structured logs, Traces, Metrics**. Что ты увидишь:

1. **Список ресурсов**. Обрати внимание:
   - `migrations` в состоянии **Finished (exit code: 0)**: он отработал и завершился, как и задумано;
   - `menuapi` и `barista` — по **две** строки: это реплики (`menuapi-abcd1234`, `menuapi-efgh5678`);
   - `postgres-password`, `barista-seconds-per-drink` — это тоже ресурсы (параметры);
   - у `postgres`, `cache`, `messaging` есть ссылки **Manage** на pgAdmin, Redis Insight и RabbitMQ Management.
2. Открой приложение (ссылка «☕ Открыть CoffeeShop» у ресурса `web`), добавь пару напитков в корзину,
   оформи заказ и смотри, как меняется статус.
3. Вернись в дашборд → **Traces**. Найди трейс, начинающийся со спана
   `Event onclick -> ...CartPage.PlaceOrderAsync`. Раскрой его — это путь твоего заказа через
   **все** сервисы: Blazor → OrdersApi → MenuApi → Redis → PostgreSQL → RabbitMQ → BaristaWorker →
   RabbitMQ → OrdersApi.

То же самое можно посмотреть из терминала:

```powershell
aspire describe            # таблица ресурсов
aspire otel traces -n 5    # последние 5 трейсов
```

## Как читать код проекта

Рекомендуемый порядок:

1. [`AppHost.cs`](../src/CoffeeShop.AppHost/AppHost.cs) — «карта» всей системы (урок 1).
2. [`Extensions.cs`](../src/CoffeeShop.ServiceDefaults/Extensions.cs) в ServiceDefaults (урок 2).
3. `Program.cs` любого сервиса: найди вызовы `AddServiceDefaults()`, `AddNpgsqlDbContext`,
   `AddRedisDistributedCache`, `AddRabbitMQClient` (урок 3).
4. Дальше по интересу: бизнес-логика в `OrderService.cs`, обмен сообщениями в `Messaging/` и `Barista.cs`.

Комментарии в коде объясняют **зачем**, а уроки — **как это связано между собой**.

## Попробуй сам

- [ ] Запусти `aspire run` и пройдись по всем вкладкам дашборда: Overview, Console logs, Structured logs, Traces, Metrics.
- [ ] Останови ресурс `cache` (кнопка ⏹ появляется при наведении на строку ресурса). Обнови меню в приложении несколько раз.
      Что стало с health-статусом `menuapi`? Запусти `cache` снова.
- [ ] Выполни `aspire describe` в соседнем терминале и сравни с дашбордом.

➡️ Дальше: [Урок 1. AppHost и модель приложения](01-apphost.md)
