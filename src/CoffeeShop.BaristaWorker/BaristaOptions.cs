namespace CoffeeShop.BaristaWorker;

public sealed class BaristaOptions
{
    public const string SectionName = "Barista";

    /// <summary>Сколько секунд бариста тратит на одну позицию заказа.</summary>
    public double SecondsPerDrink { get; set; } = 3;
}
