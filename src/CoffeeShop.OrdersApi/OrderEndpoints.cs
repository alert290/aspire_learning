using CoffeeShop.Contracts;
using CoffeeShop.Data.Orders;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace CoffeeShop.OrdersApi;

public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/orders").WithTags("Orders");

        orders.MapPost("/", PlaceOrderAsync).WithName("PlaceOrder");
        orders.MapGet("/{id:guid}", GetOrderAsync).WithName("GetOrder");
        orders.MapGet("/", GetRecentOrdersAsync).WithName("GetRecentOrders");

        // Служебный эндпоинт для команды дашборда "Час пик" (см. AppHost.cs → WithHttpCommand).
        app.MapPost("/admin/orders/simulate", SimulateRushHourAsync).ExcludeFromDescription();

        return app;
    }

    private static async Task<Results<Created<OrderDto>, ValidationProblem>> PlaceOrderAsync(
        PlaceOrderRequest request, OrderService service, CancellationToken ct)
    {
        var (order, errors) = await service.PlaceOrderAsync(request, ct);
        return errors is not null
            ? TypedResults.ValidationProblem(errors)
            : TypedResults.Created($"/orders/{order!.Id}", order);
    }

    private static async Task<Results<Ok<OrderDto>, NotFound>> GetOrderAsync(
        Guid id, OrdersDbContext db, CancellationToken ct)
    {
        var order = await db.Orders
            .AsNoTracking()
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == id, ct);

        return order is null ? TypedResults.NotFound() : TypedResults.Ok(order.ToDto());
    }

    private static async Task<Ok<List<OrderDto>>> GetRecentOrdersAsync(
        OrdersDbContext db, CancellationToken ct, int limit = 20)
    {
        limit = Math.Clamp(limit, 1, 100);

        var orders = await db.Orders
            .AsNoTracking()
            .Include(o => o.Lines)
            .OrderByDescending(o => o.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

        return TypedResults.Ok(orders.Select(o => o.ToDto()).ToList());
    }

    /// <summary>Создаёт несколько случайных заказов — удобно, чтобы "оживить" метрики и трейсы.</summary>
    private static async Task<Ok<string>> SimulateRushHourAsync(
        OrderService service, MenuApiClient menuApi, CancellationToken ct, int count = 5)
    {
        var menu = (await menuApi.GetMenuAsync(ct)).Values.ToList();
        string[] guests = ["Аня", "Борис", "Вика", "Гоша", "Даша", "Егор", "Жанна", "Захар"];

        for (var i = 0; i < count; i++)
        {
            var items = Enumerable.Range(0, Random.Shared.Next(1, 4))
                .Select(_ => new OrderLineRequest(menu[Random.Shared.Next(menu.Count)].Id, Random.Shared.Next(1, 3)))
                .ToList();

            await service.PlaceOrderAsync(
                new PlaceOrderRequest($"{guests[Random.Shared.Next(guests.Length)]} (симуляция)", items), ct);
        }

        return TypedResults.Ok($"Создано заказов: {count}. Загляни во вкладки Traces и Metrics!");
    }
}
