namespace CoffeeShop.Contracts;

// ---------------------------------------------------------------------------------
// Контракты (DTO) — "общий язык" между сервисами.
//
// Этот проект не знает ничего про Aspire. Он нужен, чтобы MenuApi, OrdersApi,
// BaristaWorker и Blazor-фронтенд одинаково понимали JSON, который гоняют друг другу.
// В реальной микросервисной системе контракты часто публикуют как NuGet-пакет
// или описывают через OpenAPI / protobuf.
// ---------------------------------------------------------------------------------

/// <summary>Позиция меню, как её видит клиент (Web, OrdersApi).</summary>
public sealed record MenuItemDto(
    int Id,
    string Name,
    string Description,
    decimal Price,
    string Category,
    string Emoji);
