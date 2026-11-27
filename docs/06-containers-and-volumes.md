# Урок 6. Контейнеры, тома и время жизни

**Где в коде:** [AppHost.cs](../src/CoffeeShop.AppHost/AppHost.cs), раздел «Инфраструктура».

## Откуда берутся контейнеры

`AddPostgres`, `AddRedis`, `AddRabbitMQ` — это обёртки над `AddContainer`. Интеграция знает образ и
тег (`postgres:18.x`, `redis:8.x`, `rabbitmq:4.x-management`), порты, переменные окружения и проверку
здоровья. Запускает контейнеры Aspire через Docker (или Podman). Посмотри `docker ps` при работающем
`aspire run`.

Любой другой образ можно добавить напрямую:

```csharp
builder.AddContainer("mailpit", "axllent/mailpit")
       .WithHttpEndpoint(targetPort: 8025, name: "ui")
       .WithEndpoint(targetPort: 1025, name: "smtp");
```

## Время жизни: Session и Persistent

```csharp
var postgres = builder.AddPostgres("postgres", ...)
    .WithLifetime(ContainerLifetime.Persistent);
```

| | `Session` (по умолчанию) | `Persistent` |
|---|---|---|
| При остановке AppHost | контейнер удаляется | контейнер **продолжает работать** |
| При следующем запуске | создаётся заново (долго) | переиспользуется (мгновенно) |
| Порт | случайный каждый раз | фиксируется и сохраняется |
| В CoffeeShop | `cache` (Redis) | `postgres`, `pgadmin`, `messaging` |

После `Ctrl+C` проверь сам: `docker ps` покажет, что `postgres-xxxx` и `messaging-xxxx` живы, а Redis нет.

> ⚠️ Persistent-контейнер пересоздаётся, если поменялась его конфигурация (образ, переменные,
> аргументы). Aspire хранит «отпечаток» конфигурации и сравнивает его при запуске.

> ⚠️ Имя persistent-контейнера (`postgres-<хэш>`) вычисляется из пути к AppHost. Его разделяют **все**,
> кто запускает этот AppHost на машине, включая интеграционные тесты. При проверке проекта тест
> поначалу «захватывал» контейнер разработки и пересоздавал его со своим паролем. Как это исправлено,
> рассказано в [уроке 7](07-testing.md).

В Aspire 17 появился и более общий API `WithPersistentLifetime()` / `WithSessionLifetime()`: он
работает не только для контейнеров, но и для проектов. Пока он помечен как экспериментальный
(предупреждение `ASPIREPERSISTENCE001`), поэтому в проекте используется стабильный `WithLifetime(...)`.

## Тома: данные переживают перезапуск

```csharp
postgres.WithDataVolume();     // именованный Docker-том для /var/lib/postgresql
messaging.WithDataVolume();    // для /var/lib/rabbitmq
```

Тома называются по имени AppHost и ресурса: `coffeeshop.apphost-<хэш>-postgres-data`. Посмотреть:

```powershell
docker volume ls --filter name=coffeeshop
```

Альтернатива — `WithDataBindMount("./data/pg")`: данные в папке на диске. Удобно заглянуть в файлы,
но на Windows bind-mount'ы медленнее.

Без тома данные живут, пока жив контейнер. У Redis-кэша мы том не делали: кэш не жалко потерять.

## Админки «из коробки»

```csharp
postgres.WithPgAdmin();          // pgAdmin — уже подключён к серверу
cache.WithRedisInsight();        // Redis Insight — уже подключён к Redis
messaging.WithManagementPlugin(); // образ RabbitMQ с веб-консолью
```

Ссылки появляются у ресурса в дашборде («Manage»). Важная деталь: эти инструменты существуют
**только в режиме разработки**. В `docker-compose.yaml` после `aspire publish` их нет (урок 8).

## Как всё сбросить

Иногда нужно начать с нуля: поменял пароль, сломал данные, хочешь проверить «первый запуск».

```powershell
aspire stop                                                            # если запущено в фоне (или Ctrl+C)
docker rm -f $(docker ps -aq --filter "label=com.microsoft.developer.usvc-dev.persistent=true")
docker volume ls -q --filter name=coffeeshop.apphost | ForEach-Object { docker volume rm $_ }
```

> Первая команда удаляет **все** persistent-контейнеры Aspire на машине (их легко узнать по метке).
> Если у тебя есть другие Aspire-проекты, удаляй контейнеры по именам: `docker rm -f postgres-xxxx messaging-xxxx pgadmin-xxxx`.

Если поменялся пароль, а том старый, PostgreSQL будет отвечать «password authentication failed».
Лечится сбросом тома (выше) или возвратом старого пароля через `aspire secret set`.

## Попробуй сам

- [ ] Запусти приложение, оформи заказ, останови (`Ctrl+C`) и запусти снова. Заказ на месте? Почему?
- [ ] Убери `.WithLifetime(ContainerLifetime.Persistent)` у `postgres` и сравни время старта.
- [ ] Добавь контейнер Mailpit (пример выше) и найди его в дашборде.
- [ ] Сбрось все данные по инструкции выше и посмотри логи `migrations` при «первом» запуске.

➡️ Дальше: [Урок 7. Тестирование](07-testing.md)
