using CoffeeShop.Web.Components;
using CoffeeShop.Web.Services;

// =====================================================================================
//  CoffeeShop.Web — Blazor Web App (Interactive Server).
//
//  Фронтенд ничего не знает про базы, Redis и RabbitMQ — он общается только с двумя API
//  по их ЛОГИЧЕСКИМ именам: "menuapi" и "ordersapi". Реальные адреса (порты) подставляет
//  Aspire через Service Discovery. Если завтра MenuApi переедет на другой порт или
//  в Kubernetes — код фронтенда не изменится.
// =====================================================================================

var builder = WebApplication.CreateBuilder(args);

// Телеметрия, health checks, service discovery, resilience для HttpClient.
builder.AddServiceDefaults();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Типизированные HttpClient-ы. "https+http://menuapi" расшифровывается так:
//   https+http — "сначала попробуй HTTPS-эндпоинт ресурса, если его нет — HTTP";
//   menuapi    — имя ресурса из AppHost.cs (builder.AddDotnetProject("menuapi", ...)).
// AppHost передал нам адреса через переменные окружения services__menuapi__https__0 и т.п.
builder.Services.AddHttpClient<MenuApiClient>(client =>
    client.BaseAddress = new Uri("https+http://menuapi"));

builder.Services.AddHttpClient<OrdersApiClient>(client =>
    client.BaseAddress = new Uri("https+http://ordersapi"));

// Корзина живёт в рамках "цепи" (circuit) Blazor Server — т.е. одной вкладки браузера.
builder.Services.AddScoped<CartState>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

// Русская локаль для чисел и дат: "1 180 ₽" вместо "1,180 ₽".
// Blazor Server берёт культуру из запроса, с которого началась "цепь" (circuit).
app.UseRequestLocalization("ru-RU");

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// /health и /alive из ServiceDefaults.
app.MapDefaultEndpoints();

app.Run();
