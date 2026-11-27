# Урок 8. Публикация и деплой: `aspire publish` и `aspire deploy`

**Где в коде:** [AppHost.cs](../src/CoffeeShop.AppHost/AppHost.cs), строка `builder.AddDockerComposeEnvironment("compose")`.

## Идея: та же модель — другой результат

`AppHost.cs` описывает систему **один раз**. Дальше её можно:
- **запустить** локально (`aspire run`): процессы и контейнеры на твоей машине;
- **опубликовать** (`aspire publish`): сгенерировать артефакты для развёртывания;
- **развернуть** (`aspire deploy`): сгенерировать, собрать образы и запустить.

Куда именно публиковать, определяет **среда развёртывания** (compute environment) в AppHost:

```csharp
builder.AddDockerComposeEnvironment("compose");   // пакет Aspire.Hosting.Docker
```

Другие варианты: `AddKubernetesEnvironment` (Helm-чарты), `AddAzureContainerAppEnvironment`
(Azure Container Apps) и др. Код сервисов при этом не меняется.

В режиме `aspire run` эта строка ни на что не влияет, ресурс `compose` даже не виден в дашборде.

## `aspire publish` → docker-compose.yaml

```powershell
aspire publish -o artifacts/compose
```

Получаются два файла: `docker-compose.yaml` и `.env`. Посмотрим, во что превратился наш AppHost
(фрагменты реального вывода):

```yaml
services:
  compose-dashboard:                       # ← дашборд Aspire едет вместе с приложением!
    image: "mcr.microsoft.com/aspire/nightly/dashboard:17.0"
    ports: ["18888"]

  postgres:
    image: "docker.io/library/postgres:18.6"
    environment:
      POSTGRES_PASSWORD: "${POSTGRES_PASSWORD}"     # ← параметр → переменная из .env
    volumes:
      - source: "coffeeshop.apphost-...-postgres-data"   # ← WithDataVolume()
        target: "/var/lib/postgresql"

  menuapi:
    image: "${MENUAPI_IMAGE}"
    environment:
      ConnectionStrings__menudb: "Host=postgres;Port=5432;Username=postgres;Password=${POSTGRES_PASSWORD};Database=menudb"
      ConnectionStrings__cache: "cache:6379,password=${CACHE_PASSWORD}"
      OTEL_EXPORTER_OTLP_ENDPOINT: "http://compose-dashboard:18889"
    depends_on:
      cache:      { condition: "service_started" }
      migrations: { condition: "service_completed_successfully" }   # ← WaitForCompletion(migrations)!

  ordersapi:
    environment:
      services__menuapi__http__0: "http://menuapi:${MENUAPI_PORT}"  # ← service discovery в Compose

  web:
    ports: ["${WEB_PORT}"]                 # ← WithExternalHttpEndpoints(): единственный порт наружу
```

Сравни с тем, что было при `aspire run`:

| В AppHost | При `aspire run` | В docker-compose.yaml |
|---|---|---|
| `WithReference(menuDb)` | `ConnectionStrings__menudb=Host=localhost;Port=21555…` | `…Host=postgres;Port=5432…` |
| `WithReference(menuApi)` | `services__menuapi__https__0=https://localhost:7218` | `services__menuapi__http__0=http://menuapi:8080` |
| `WaitForCompletion(migrations)` | Aspire ждёт выхода процесса | `depends_on: service_completed_successfully` |
| `WaitFor(cache)` | ждёт Healthy | `depends_on: service_started` |
| `AddParameter(..., secret: true)` | значение из хранилища секретов | `${POSTGRES_PASSWORD}` + пустая строка в `.env` |
| `WithPgAdmin()`, `WithRedisInsight()` | есть | **нет**: это инструменты разработки |
| `WithExternalHttpEndpoints()` | — | `ports:` только у `web` |
| `WithReplicas(2)` | 2 процесса | одна копия сервиса |

Код сервисов не изменился: тот же `https+http://menuapi` в Compose разрешится в `http://menuapi:8080`.

## `aspire deploy` → всё запущено в Docker

```powershell
aspire deploy
```

Посмотреть шаги без выполнения: `aspire deploy --list-steps`. Для Docker Compose пайплайн такой:

1. `process-parameters`: определить значения параметров (сгенерированные пароли сохраняются в
   состояние развёртывания `~/.aspire/deployments/...`);
2. `build-<сервис>`: собрать контейнерный образ каждого .NET-проекта (`web:latest`, `menuapi:latest`, …).
   Dockerfile не нужен: используется встроенная в .NET SDK сборка контейнеров;
3. `publish-compose` / `prepare-compose`: сгенерировать `docker-compose.yaml` и заполненный `.env`;
4. `docker-compose-up-compose`: выполнить `docker compose up`.

В конце CLI печатает адреса:

```
web: http://localhost:52543
compose-dashboard: http://localhost:60284
```

Удалить развёртывание: `aspire destroy --yes`. Контейнеры удаляются, а **тома остаются**. Их можно удалить
вручную через `docker volume ls` и `docker volume rm`.

### История из жизни этого проекта

Первый `aspire deploy` упал: контейнер `migrations` завершился с ошибкой
`3D000: database "menudb" does not exist`, и Compose не стал запускать API. При `aspire run` базы
создаёт AppHost (`AddDatabase`), а в Compose AppHost'а нет. Исправление в
[DatabaseInitializer.cs](../src/CoffeeShop.MigrationService/DatabaseInitializer.cs): сервис миграций
создаёт базу сам, если её нет. Отсюда правило: **локальный запуск под Aspire что-то делает за тебя,
проверяй деплой отдельно.**

Есть и другие отличия Compose-окружения, о которых стоит знать:
- сервисы работают по HTTP внутри сети Docker (сертификата разработчика там нет);
- `ASPNETCORE_ENVIRONMENT` = Production, поэтому эндпоинты `/health` и `/openapi` не публикуются
  (см. `MapDefaultEndpoints` и `if (app.Environment.IsDevelopment())` в `Program.cs`);
- `ServiceInstance.Name` показывает «id-контейнера-PID», потому что переменной `service.instance.id` в Compose нет.

## Дальше: облако

Для Azure достаточно поменять среду развёртывания:

```csharp
builder.AddAzureContainerAppEnvironment("aca");   // пакет Aspire.Hosting.Azure.AppContainers
```

После этого `aspire deploy` развернёт систему в Azure Container Apps. Понадобится подписка Azure,
`az login`, а PostgreSQL/Redis обычно заменяют на управляемые сервисы (`AddAzurePostgresFlexibleServer`,
`AddAzureManagedRedis`). Это хорошее упражнение «на потом».

## Попробуй сам

- [ ] Выполни `aspire publish -o artifacts/compose` и найди в `docker-compose.yaml` все места, где упоминается `menuapi`.
- [ ] Выполни `aspire deploy`, открой адрес `web` и оформи заказ. Открой `compose-dashboard`: телеметрия собирается и тут!
- [ ] Выполни `aspire destroy --yes` и проверь `docker ps`.
- [ ] Добавь `WithReplicas(2)` в публикацию: подсказка, `PublishAsDockerComposeService((resource, service) => { ... })` позволяет менять сгенерированный сервис.

➡️ Дальше: [Урок 9. Упражнения и что дальше](09-exercises.md)
