using System.ComponentModel.DataAnnotations;
using CoffeeShop.Contracts;
using Microsoft.EntityFrameworkCore;

namespace CoffeeShop.Data.Orders;

/// <summary>Заказ — строка таблицы "Orders" в базе <c>ordersdb</c>.</summary>
public class Order
{
    public Guid Id { get; set; }

    [MaxLength(100)]
    public required string CustomerName { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ReadyAt { get; set; }

    /// <summary>Идентификатор экземпляра бариста, который готовил заказ.</summary>
    [MaxLength(100)]
    public string? PreparedBy { get; set; }

    public List<OrderLine> Lines { get; set; } = [];

    public decimal Total => Lines.Sum(l => l.UnitPrice * l.Quantity);

    public OrderDto ToDto() => new(
        Id,
        CustomerName,
        Status,
        Total,
        CreatedAt,
        ReadyAt,
        PreparedBy,
        Lines.Select(l => new OrderLineDto(l.ItemName, l.Quantity, l.UnitPrice)).ToList());
}

/// <summary>Строка заказа. Название и цену копируем в момент заказа — меню может поменяться.</summary>
public class OrderLine
{
    public int Id { get; set; }

    public Guid OrderId { get; set; }

    public int MenuItemId { get; set; }

    [MaxLength(100)]
    public required string ItemName { get; set; }

    public int Quantity { get; set; }

    [Precision(10, 2)]
    public decimal UnitPrice { get; set; }
}
