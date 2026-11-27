using System.Net.Http.Json;
using CoffeeShop.Contracts;

namespace CoffeeShop.Tests;

/// <summary>
/// End-to-end тесты: обращаемся к сервисам по HTTP так же, как это делал бы пользователь
/// или другой сервис. Требуют запущенного Docker.
/// </summary>
public sealed class EndToEndTests(CoffeeShopAppFixture fixture) : IClassFixture<CoffeeShopAppFixture>
{
    private static readonly TimeSpan OrderTimeout = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task MenuApi_ReturnsSeededMenu()
    {
        var ct = TestContext.Current.CancellationToken;

        // CreateHttpClient("menuapi") — клиент с BaseAddress, указывающим на ресурс menuapi.
        // Адрес мы не знаем заранее (порты в тестах случайные) — его знает AppHost.
        using var http = fixture.App.CreateHttpClient("menuapi");

        var menu = await http.GetFromJsonAsync<List<MenuItemDto>>("/menu", ct);

        Assert.NotNull(menu);
        Assert.True(menu.Count >= 10, $"Ожидали минимум 10 позиций меню, получили {menu.Count}");
        Assert.Contains(menu, item => item.Name == "Эспрессо");
    }

    [Fact]
    public async Task MenuApi_SecondRequest_IsServedFromRedisCache()
    {
        var ct = TestContext.Current.CancellationToken;
        using var http = fixture.App.CreateHttpClient("menuapi");

        // Первый запрос гарантированно наполняет кэш (или он уже наполнен другим тестом).
        using var first = await http.GetAsync("/menu", ct);
        first.EnsureSuccessStatusCode();

        // Второй запрос (даже если попадёт в другую реплику) читает из общего Redis.
        using var second = await http.GetAsync("/menu", ct);
        second.EnsureSuccessStatusCode();

        Assert.Equal("HIT", second.Headers.GetValues("X-Cache").Single());
    }

    [Fact]
    public async Task WebFrontend_RendersMenuFromMenuApi()
    {
        var ct = TestContext.Current.CancellationToken;
        using var http = fixture.App.CreateHttpClient("web");

        using var response = await http.GetAsync("/", ct);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(ct));

        // Главная страница пререндерится на сервере: если в HTML есть название напитка,
        // значит цепочка web → menuapi → Redis/PostgreSQL работает.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Эспрессо", html);
    }

    [Fact]
    public async Task PlacedOrder_GoesThroughRabbitMqAndBecomesReady()
    {
        var ct = TestContext.Current.CancellationToken;
        using var menuHttp = fixture.App.CreateHttpClient("menuapi");
        using var ordersHttp = fixture.App.CreateHttpClient("ordersapi");

        var menu = (await menuHttp.GetFromJsonAsync<List<MenuItemDto>>("/menu", ct))!;
        var latte = menu.First(i => i.Name == "Латте");

        // Act: оформляем заказ.
        using var response = await ordersHttp.PostAsJsonAsync("/orders",
            new PlaceOrderRequest("Интеграционный тест", [new OrderLineRequest(latte.Id, 2)]), ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = (await response.Content.ReadFromJsonAsync<OrderDto>(ct))!;
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(latte.Price * 2, order.Total);

        // Assert: заказ должен пройти OrdersApi → RabbitMQ → BaristaWorker → RabbitMQ → OrdersApi.
        var ready = await WaitForStatusAsync(ordersHttp, order.Id, OrderStatus.Ready, ct);

        Assert.NotNull(ready.ReadyAt);
        Assert.StartsWith("barista", ready.PreparedBy);
    }

    [Fact]
    public async Task PlacingEmptyOrder_ReturnsValidationProblem()
    {
        var ct = TestContext.Current.CancellationToken;
        using var http = fixture.App.CreateHttpClient("ordersapi");

        using var response = await http.PostAsJsonAsync("/orders", new PlaceOrderRequest("", []), ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<OrderDto> WaitForStatusAsync(HttpClient http, Guid orderId, OrderStatus expected, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(OrderTimeout);

        while (true)
        {
            var order = await http.GetFromJsonAsync<OrderDto>($"/orders/{orderId}", timeout.Token);
            if (order!.Status == expected)
            {
                return order;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), timeout.Token);
        }
    }
}
