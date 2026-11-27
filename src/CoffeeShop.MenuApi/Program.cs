using CoffeeShop.Data.Menu;
using CoffeeShop.MenuApi;

// =====================================================================================
//  MenuApi — сервис меню.
//
//  Данные: PostgreSQL (база "menudb"), кэш: Redis (ресурс "cache").
//  В AppHost запущен в ДВУХ экземплярах (.WithReplicas(2)) — Web обращается к нему по
//  имени "menuapi", а Aspire балансирует запросы между репликами.
// =====================================================================================

var builder = WebApplication.CreateBuilder(args);

// 1) Стандартная обвязка Aspire: OpenTelemetry, health checks, service discovery, resilience.
builder.AddServiceDefaults();

// 2) Клиентские интеграции Aspire. Имя ("menudb", "cache") должно совпадать с именем
//    ресурса в AppHost — по нему ищется строка подключения ConnectionStrings:<имя>.
builder.AddNpgsqlDbContext<MenuDbContext>("menudb");   // EF Core + ретраи + health check + трейсы SQL
builder.AddRedisDistributedCache("cache");             // IDistributedCache поверх Redis

// 3) Обычные сервисы ASP.NET Core.
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddSingleton<ChaosSwitch>();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    // Документ OpenAPI: /openapi/v1.json (ссылка на него есть в дашборде у ресурса menuapi).
    app.MapOpenApi();
}

// Каждый ответ помечаем именем экземпляра — так в UI видно, какая реплика ответила.
app.Use((context, next) =>
{
    context.Response.Headers["X-Instance"] = ServiceInstance.Name;
    return next(context);
});

app.MapGet("/", () => $"CoffeeShop.MenuApi ({ServiceInstance.Name}) работает. Меню: GET /menu")
   .ExcludeFromDescription();

app.MapMenuEndpoints();
app.MapAdminEndpoints();

// /health и /alive (см. ServiceDefaults). AppHost опрашивает /health.
app.MapDefaultEndpoints();

app.Run();
