using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace CoffeeShop.Data.Menu;

/// <summary>Позиция меню — строка таблицы "MenuItems" в базе <c>menudb</c>.</summary>
public class MenuItem
{
    public int Id { get; set; }

    [MaxLength(100)]
    public required string Name { get; set; }

    [MaxLength(500)]
    public required string Description { get; set; }

    // Деньги никогда не храним во float/double — только decimal (numeric в PostgreSQL).
    [Precision(10, 2)]
    public decimal Price { get; set; }

    [MaxLength(50)]
    public required string Category { get; set; }

    [MaxLength(16)]
    public required string Emoji { get; set; }

    public bool IsAvailable { get; set; } = true;
}
