using Aspire.Hosting.Dotnet;

namespace CoffeeShop.Tests;

/// <summary>
/// Тесты МОДЕЛИ приложения: строим AppHost, но НЕ запускаем его.
/// </summary>
/// <remarks>
/// Такие тесты работают за доли секунды и без Docker. Они проверяют "архитектурные
/// договорённости": кто от кого зависит, сколько реплик, что торчит наружу. Если кто-то
/// случайно удалит WaitForCompletion(migrations) — тест это поймает.
/// </remarks>
public sealed class AppModelTests
{
    [Fact]
    public async Task Model_ContainsAllExpectedResources()
    {
        await using var app = await BuildAppAsync();
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var names = model.Resources.Select(r => r.Name).ToHashSet();

        string[] expected =
        [
            "postgres", "menudb", "ordersdb", "cache", "messaging",
            "migrations", "menuapi", "ordersapi", "barista", "web"
        ];

        Assert.All(expected, name => Assert.Contains(name, names));
    }

    [Fact]
    public async Task ApisWaitForMigrationsToComplete()
    {
        await using var app = await BuildAppAsync();

        foreach (var name in new[] { "menuapi", "ordersapi" })
        {
            var waits = GetResource(app, name).Annotations.OfType<WaitAnnotation>();

            Assert.Contains(waits, w => w.Resource.Name == "migrations" && w.WaitType == WaitType.WaitForCompletion);
        }
    }

    [Theory]
    [InlineData("menuapi", 2)]
    [InlineData("barista", 2)]
    public async Task ScaledServicesHaveReplicas(string name, int replicas)
    {
        await using var app = await BuildAppAsync();

        var annotation = GetResource(app, name).Annotations.OfType<ReplicaAnnotation>().Single();

        Assert.Equal(replicas, annotation.Replicas);
    }

    [Fact]
    public async Task OnlyWebIsExposedExternally()
    {
        await using var app = await BuildAppAsync();
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();

        var external = model.Resources
            .OfType<DotnetProjectResource>()
            .Where(r => r.Annotations.OfType<EndpointAnnotation>().Any(e => e.IsExternal))
            .Select(r => r.Name);

        Assert.Equal(["web"], external);
    }

    private static async Task<DistributedApplication> BuildAppAsync()
    {
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.CoffeeShop_AppHost>(
            ["Parameters:postgres-password=not-used"], TestContext.Current.CancellationToken);
        return await appHost.BuildAsync(TestContext.Current.CancellationToken);
    }

    private static IResource GetResource(DistributedApplication app, string name) =>
        app.Services.GetRequiredService<DistributedApplicationModel>().Resources.Single(r => r.Name == name);
}
