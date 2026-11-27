// =====================================================================================
//  APPHOST — сердце Aspire-приложения.
//
//  Это обычное консольное .NET-приложение, но его код ОПИСЫВАЕТ, а не реализует систему:
//  какие ресурсы есть (контейнеры, сервисы, параметры), как они связаны и в каком
//  порядке стартуют. Такое описание называется "модель приложения" (application model).
//
//  Когда ты запускаешь `aspire run`, AppHost:
//    1. строит модель из этого файла;
//    2. просит оркестратор (DCP) запустить контейнеры (Docker) и процессы (.NET-проекты);
//    3. передаёт каждому сервису строки подключения и адреса соседей через переменные окружения;
//    4. поднимает дашборд и собирает туда логи, трейсы и метрики со всех сервисов.
//
//  Когда ты запускаешь `aspire publish`, тот же код превращается в артефакты развёртывания
//  (у нас — docker-compose.yaml). Один источник правды и для разработки, и для деплоя.
//
//  Порядок в файле: параметры → инфраструктура → сервисы. Читай сверху вниз.
// =====================================================================================

var builder = DistributedApplication.CreateBuilder(args);

// -------------------------------------------------------------------------------------
//  0. ЦЕЛЬ ПУБЛИКАЦИИ
// -------------------------------------------------------------------------------------
// Говорим Aspire: "при `aspire publish` генерируй Docker Compose". В режиме `aspire run`
// эта строка ни на что не влияет. Подробно — docs/08-deployment.md.
builder.AddDockerComposeEnvironment("compose");

// -------------------------------------------------------------------------------------
//  1. ПАРАМЕТРЫ — внешние значения (настройки и секреты), которые не хардкодятся в коде.
// -------------------------------------------------------------------------------------

// Секретный параметр с автогенерацией: если значения нет в конфигурации, Aspire сгенерирует
// случайный пароль и (persist: true) сохранит его в хранилище секретов AppHost
// (~/.aspire/secrets/<UserSecretsId>/Development.json). Это важно, т.к. контейнер PostgreSQL
// у нас "постоянный" и данные лежат в томе: при следующем запуске пароль должен совпасть.
// Посмотреть/изменить: `aspire secret list`, `aspire secret get Parameters:postgres-password`.
var postgresPassword = builder.AddParameter(
    "postgres-password",
    new GenerateParameterDefault { MinLength = 20, Special = false },
    secret: true,
    persist: true);

// Обычный (не секретный) параметр. Значение берётся из конфигурации AppHost:
// appsettings.json → "Parameters": { "barista-seconds-per-drink": "3" }.
// Можно переопределить, например, переменной окружения Parameters__barista-seconds-per-drink.
var secondsPerDrink = builder.AddParameter("barista-seconds-per-drink");

// -------------------------------------------------------------------------------------
//  2. ИНФРАСТРУКТУРА — контейнеры. Хостинговые интеграции (пакеты Aspire.Hosting.*)
//     знают, какой образ взять, какие порты открыть, как проверить здоровье и какую
//     строку подключения выдать потребителям.
// -------------------------------------------------------------------------------------

var postgres = builder.AddPostgres("postgres", password: postgresPassword)
    // Именованный Docker-том: данные переживают перезапуск AppHost.
    .WithDataVolume()
    // Persistent: контейнер НЕ удаляется при остановке AppHost и переиспользуется при
    // следующем запуске — старт быстрее, данные на месте. (По умолчанию — Session.)
    // В Aspire 17 появился и более общий (пока экспериментальный) WithPersistentLifetime().
    .WithLifetime(ContainerLifetime.Persistent)
    // pgAdmin — веб-админка; ссылка на неё появится в дашборде. Только для `aspire run`.
    .WithPgAdmin(pgAdmin => pgAdmin.WithLifetime(ContainerLifetime.Persistent));

// Две логические базы в одном сервере. Aspire сам выполнит CREATE DATABASE при старте.
var menuDb = postgres.AddDatabase("menudb");
var ordersDb = postgres.AddDatabase("ordersdb");

// Redis для кэша. Здесь контейнер "сессионный" и без тома: кэш не жалко потерять.
var cache = builder.AddRedis("cache")
    .WithRedisInsight();   // веб-UI для просмотра ключей

// RabbitMQ — брокер сообщений. Пароль не задаём явно: интеграция сама создаст
// параметр "messaging-password" со сгенерированным значением.
var messaging = builder.AddRabbitMQ("messaging")
    .WithManagementPlugin()   // образ с веб-консолью RabbitMQ Management
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

// -------------------------------------------------------------------------------------
//  3. СЕРВИСЫ — наши .NET-проекты. AddDotnetProject(имя, путь-к-csproj).
//     Имя ресурса = имя для service discovery (https+http://<имя>).
// -------------------------------------------------------------------------------------

// MigrationService: применяет миграции и завершается.
var migrations = builder.AddDotnetProject("migrations", "../CoffeeShop.MigrationService/CoffeeShop.MigrationService.csproj")
    // WithReference(db) → переменная окружения ConnectionStrings__menudb=Host=...;Password=...
    .WithReference(menuDb)
    .WithReference(ordersDb)
    // WaitFor → не запускать, пока база не станет Healthy (сервер поднялся, БД создана).
    .WaitFor(menuDb)
    .WaitFor(ordersDb);

// MenuApi: меню из PostgreSQL + кэш в Redis, две реплики.
var menuApi = builder.AddDotnetProject("menuapi", "../CoffeeShop.MenuApi/CoffeeShop.MenuApi.csproj")
    .WithReference(menuDb)
    .WithReference(cache)
    .WaitFor(cache)
    // WaitForCompletion → ждать, пока migrations ЗАВЕРШИТСЯ с кодом 0.
    .WaitForCompletion(migrations)
    // Две копии процесса. Потребители по-прежнему обращаются к "menuapi" — Aspire
    // ставит перед репликами прокси и балансирует запросы.
    .WithReplicas(2)
    // Ресурс считается Healthy, когда GET /health возвращает 200. От этого зависят
    // WaitFor(menuApi) у других сервисов и статус в дашборде.
    .WithHttpHealthCheck("/health")
    // Дополнительная ссылка в дашборде (относительный путь склеивается с адресом эндпоинта).
    .WithUrlForEndpoint("https", _ => new() { Url = "/openapi/v1.json", DisplayText = "OpenAPI" })
    // Команды дашборда: кнопки в меню ресурса, отправляющие HTTP-запрос (по умолчанию POST) в сервис.
    // commandName — короткое имя для CLI: `aspire resource menuapi clear-menu-cache`.
    .WithHttpCommand(
        path: "/admin/cache/clear",
        displayName: "Сбросить кэш меню",
        commandName: "clear-menu-cache",
        commandOptions: new HttpCommandOptions
        {
            Description = "Удаляет закэшированное меню из Redis — следующий запрос пойдёт в PostgreSQL",
            IconName = "Broom",
            ConfirmationMessage = "Очистить кэш меню в Redis?"
        })
    .WithHttpCommand(
        path: "/admin/chaos/toggle",
        displayName: "Вкл/выкл сбои",
        commandName: "toggle-chaos",
        commandOptions: new HttpCommandOptions
        {
            Description = "Половина запросов к /menu начнёт отвечать 503 — посмотри, как спасают ретраи",
            IconName = "Bug"
        });

// OrdersApi: заказы в PostgreSQL, события в RabbitMQ, цены — у MenuApi.
var ordersApi = builder.AddDotnetProject("ordersapi", "../CoffeeShop.OrdersApi/CoffeeShop.OrdersApi.csproj")
    .WithReference(ordersDb)
    .WithReference(messaging)
    // Ссылка на другой сервис → переменные services__menuapi__https__0=https://localhost:xxxx
    // (их читает Service Discovery, когда OrdersApi обращается к "https+http://menuapi").
    .WithReference(menuApi)
    .WaitFor(messaging)
    .WaitFor(menuApi)
    .WaitForCompletion(migrations)
    .WithHttpHealthCheck("/health")
    .WithUrlForEndpoint("https", _ => new() { Url = "/openapi/v1.json", DisplayText = "OpenAPI" })
    .WithHttpCommand(
        path: "/admin/orders/simulate",
        displayName: "Час пик: 5 заказов",
        commandName: "rush-hour",
        commandOptions: new HttpCommandOptions
        {
            Description = "Создаёт 5 случайных заказов, чтобы оживить трейсы и метрики",
            IconName = "PeopleQueue",
            IsHighlighted = true   // кнопка видна прямо в строке ресурса, а не только в меню "…"
        });

// BaristaWorker: фоновый обработчик очереди, две конкурирующие реплики.
builder.AddDotnetProject("barista", "../CoffeeShop.BaristaWorker/CoffeeShop.BaristaWorker.csproj")
    .WithReference(messaging)
    .WaitFor(messaging)
    // Значение параметра → переменная окружения. В сервисе она станет конфигом Barista:SecondsPerDrink.
    .WithEnvironment("Barista__SecondsPerDrink", secondsPerDrink)
    .WithReplicas(2);

// Web: Blazor-фронтенд. Знает только про два API.
builder.AddDotnetProject("web", "../CoffeeShop.Web/CoffeeShop.Web.csproj")
    // Помечаем эндпоинты как внешние: при публикации они будут доступны снаружи
    // (остальные сервисы останутся внутренними).
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(menuApi)
    .WithReference(ordersApi)
    .WaitFor(menuApi)
    .WaitFor(ordersApi)
    // Красивая подпись ссылки в дашборде вместо голого https://localhost:7147.
    .WithUrlForEndpoint("https", url => url.DisplayText = "☕ Открыть CoffeeShop")
    .WithUrlForEndpoint("http", url => url.DisplayLocation = UrlDisplayLocation.DetailsOnly);

builder.Build().Run();
