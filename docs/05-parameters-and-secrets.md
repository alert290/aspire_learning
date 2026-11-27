# Урок 5. Параметры и секреты

**Где в коде:** [AppHost.cs](../src/CoffeeShop.AppHost/AppHost.cs) (раздел «Параметры»),
[AppHost/appsettings.json](../src/CoffeeShop.AppHost/appsettings.json),
[BaristaWorker/Program.cs](../src/CoffeeShop.BaristaWorker/Program.cs)

## Зачем нужны параметры

Пароли, ключи API, настройки окружения не должны быть зашиты в `AppHost.cs`. **Параметр** — это
ресурс-«заглушка» для внешнего значения. При локальном запуске значение берётся из конфигурации
AppHost, а при публикации превращается в переменную для заполнения (например, в `.env` для Docker Compose).

## Обычный параметр

```csharp
var secondsPerDrink = builder.AddParameter("barista-seconds-per-drink");

builder.AddDotnetProject("barista", ...)
    .WithEnvironment("Barista__SecondsPerDrink", secondsPerDrink);
```

Значение лежит в секции `Parameters` конфигурации AppHost:

```json
// src/CoffeeShop.AppHost/appsettings.json
"Parameters": {
  "barista-seconds-per-drink": "3"
}
```

Дальше обычный .NET Options pattern. В BaristaWorker:

```csharp
builder.Services.Configure<BaristaOptions>(builder.Configuration.GetSection("Barista"));
// Barista__SecondsPerDrink (env) → Barista:SecondsPerDrink (config) → BaristaOptions.SecondsPerDrink
```

Цепочка целиком: **appsettings.json AppHost → параметр → переменная окружения сервиса → IOptions**.

Как переопределить значение, не трогая файл:

```powershell
${env:Parameters__barista-seconds-per-drink} = "1"   # переменная окружения AppHost (фигурные скобки — из-за дефисов в имени)
aspire run
```

…или через хранилище секретов: `aspire secret set Parameters:barista-seconds-per-drink 1`.

> Если значения параметра нигде нет, дашборд при запуске **спросит** его у тебя (появится
> уведомление с формой ввода). Попробуй удалить строку из appsettings.json.

## Секретный параметр с автогенерацией

```csharp
var postgresPassword = builder.AddParameter(
    "postgres-password",
    new GenerateParameterDefault { MinLength = 20, Special = false },
    secret: true,
    persist: true);

var postgres = builder.AddPostgres("postgres", password: postgresPassword);
```

Разберём аргументы:
- `GenerateParameterDefault`: если значения нет в конфигурации, сгенерировать случайное (20+ символов, без спецсимволов);
- `secret: true`: значение скрывается в дашборде и логах, при публикации считается секретом;
- `persist: true`: сгенерированное значение **сохраняется**, и при следующем запуске будет тем же.

Зачем сохранять? Контейнер PostgreSQL у нас «постоянный» с томом данных. Пароль задаётся при **первой**
инициализации тома. Если при следующем запуске сгенерировать новый пароль, сервисы получат
«authentication failed».

### Где хранятся секреты

В Aspire 17 у AppHost своё хранилище секретов:

```
~/.aspire/secrets/<UserSecretsId из CoffeeShop.AppHost.csproj>/Development.json
```

Работать с ним удобно через CLI:

```powershell
aspire secret list                                   # все сохранённые значения
aspire secret get Parameters:postgres-password       # одно значение
aspire secret set Parameters:postgres-password "..." # задать своё
aspire secret path                                   # где лежит файл
```

Там же ты найдёшь `Parameters:messaging-password` и `Parameters:cache-password`. Их сгенерировали
сами интеграции RabbitMQ и Redis: мы не указали пароль явно, и они создали параметры по тому же принципу.
А ещё там лежат зафиксированные порты persistent-контейнеров (`Resources:postgres:tcp:port`), чтобы
pgAdmin и строки подключения не менялись между запусками.

## Параметры при публикации

При `aspire publish` значения параметров в артефакты **не попадают**. Вместо них создаётся шаблон:

```dotenv
# .env (сгенерирован aspire publish)
# Parameter postgres-password
POSTGRES_PASSWORD=
# Parameter barista-seconds-per-drink
BARISTA_SECONDS_PER_DRINK=
```

а в `docker-compose.yaml` стоит подстановка `"${POSTGRES_PASSWORD}"`. Заполняет их тот, кто разворачивает
систему, или `aspire deploy`, который сохраняет значения в своё «состояние развёртывания» (урок 8).

## Строки подключения к внешним ресурсам

Если база уже существует (например, общая тестовая), её не обязательно поднимать в контейнере:

```csharp
var legacyDb = builder.AddConnectionString("legacydb");   // значение из ConnectionStrings:legacydb конфигурации AppHost
api.WithReference(legacyDb);
```

Для сервиса ничего не меняется: он так же читает `ConnectionStrings:legacydb`.

## Попробуй сам

- [ ] Выполни `aspire secret list` и найди все три сгенерированных пароля.
- [ ] Поставь `barista-seconds-per-drink` = `10`, перезапусти и оформи заказ. Посмотри метрику `coffeeshop.barista.brew_time`.
- [ ] Удали параметр из `appsettings.json` и запусти `aspire run`: что предложит дашборд?
- [ ] Добавь параметр `menu-cache-seconds` и передай его в MenuApi вместо захардкоженных 60 секунд.

➡️ Дальше: [Урок 6. Контейнеры, тома и время жизни](06-containers-and-volumes.md)
