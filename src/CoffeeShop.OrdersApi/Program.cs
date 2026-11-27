using CoffeeShop.Data.Orders;
using CoffeeShop.OrdersApi;
using CoffeeShop.OrdersApi.Messaging;

// =====================================================================================
//  OrdersApi — сервис заказов.
//
//  • Хранит заказы в PostgreSQL (база "ordersdb").
//  • Цены берёт у MenuApi по HTTP (адрес "https+http://menuapi" — service discovery).
//  • Публикует событие OrderPlaced в RabbitMQ ("messaging") → его обработает BaristaWorker.
//  • Слушает очередь статусов от бариста и обновляет заказ в базе.
//  • Пишет собственные метрики (Meter "CoffeeShop.Orders") — смотри вкладку Metrics.
// =====================================================================================

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Клиентские интеграции Aspire: имена совпадают с именами ресурсов в AppHost.
builder.AddNpgsqlDbContext<OrdersDbContext>("ordersdb");
builder.AddRabbitMQClient("messaging");   // регистрирует RabbitMQ.Client.IConnection (singleton)

// Типизированный HttpClient к MenuApi. Обрати внимание на адрес: не "localhost:7123",
// а логическое имя ресурса. "https+http" = "предпочитай HTTPS, если его нет — HTTP".
// Resilience (ретраи, таймауты) этот клиент получает автоматически из ServiceDefaults.
builder.Services.AddHttpClient<MenuApiClient>(client =>
    client.BaseAddress = new Uri("https+http://menuapi"));

builder.Services.AddSingleton<OrdersMetrics>();
builder.Services.AddSingleton<RabbitMqPublisher>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddHostedService<OrderStatusConsumer>();

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/", () => "CoffeeShop.OrdersApi работает. Заказы: GET/POST /orders")
   .ExcludeFromDescription();

app.MapOrderEndpoints();
app.MapDefaultEndpoints();

app.Run();
