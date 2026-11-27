using CoffeeShop.BaristaWorker;

// =====================================================================================
//  BaristaWorker — фоновый сервис без HTTP (Worker Service).
//
//  Слушает очередь "orders.placed", "готовит" напитки (Task.Delay) и сообщает статусы
//  в очередь "orders.status". В AppHost запущен в ДВУХ репликах — это классический
//  паттерн "конкурирующие потребители": RabbitMQ раздаёт заказы свободным бариста.
//
//  Время приготовления задаётся ПАРАМЕТРОМ AppHost "barista-seconds-per-drink"
//  и приходит сюда как переменная окружения Barista__SecondsPerDrink.
// =====================================================================================

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddRabbitMQClient("messaging");

// Options pattern: секция "Barista" конфигурации → BaristaOptions.
// Переменная окружения Barista__SecondsPerDrink попадает в Barista:SecondsPerDrink.
builder.Services.Configure<BaristaOptions>(builder.Configuration.GetSection(BaristaOptions.SectionName));

builder.Services.AddSingleton<BaristaMetrics>();
builder.Services.AddHostedService<Barista>();

var host = builder.Build();
host.Run();
