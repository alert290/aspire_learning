using CoffeeShop.Contracts;

namespace CoffeeShop.Web.Services;

/// <summary>
/// Корзина покупателя. Scoped-сервис: в Blazor Server один scope = одна "цепь" (вкладка).
/// </summary>
/// <remarks>
/// Это чистый Blazor, Aspire тут ни при чём. Событие <see cref="Changed"/> нужно,
/// чтобы счётчик в меню навигации обновлялся, когда страница меняет корзину.
/// </remarks>
public sealed class CartState
{
    private readonly Dictionary<int, CartLine> _lines = [];

    public event Action? Changed;

    public IReadOnlyCollection<CartLine> Lines => _lines.Values;

    public int Count => _lines.Values.Sum(l => l.Quantity);

    public decimal Total => _lines.Values.Sum(l => l.Item.Price * l.Quantity);

    /// <summary>Имя покупателя запоминаем, чтобы не вводить его заново для следующего заказа.</summary>
    public string CustomerName { get; set; } = "";

    public void Add(MenuItemDto item)
    {
        if (_lines.TryGetValue(item.Id, out var line))
        {
            line.Quantity++;
        }
        else
        {
            _lines[item.Id] = new CartLine(item) { Quantity = 1 };
        }

        Changed?.Invoke();
    }

    public void Decrease(int menuItemId)
    {
        if (_lines.TryGetValue(menuItemId, out var line) && --line.Quantity <= 0)
        {
            _lines.Remove(menuItemId);
        }

        Changed?.Invoke();
    }

    public void Remove(int menuItemId)
    {
        _lines.Remove(menuItemId);
        Changed?.Invoke();
    }

    public void Clear()
    {
        _lines.Clear();
        Changed?.Invoke();
    }

    public PlaceOrderRequest ToRequest() =>
        new(CustomerName, _lines.Values.Select(l => new OrderLineRequest(l.Item.Id, l.Quantity)).ToList());
}

public sealed class CartLine(MenuItemDto item)
{
    public MenuItemDto Item { get; } = item;
    public int Quantity { get; set; }
}
