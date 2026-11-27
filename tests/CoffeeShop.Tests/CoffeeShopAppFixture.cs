using Microsoft.Extensions.Logging;

namespace CoffeeShop.Tests;

// =====================================================================================
//  Фикстура: поднимает ВСЁ приложение (AppHost) один раз на класс тестов.
//
//  Aspire.Hosting.Testing запускает настоящий AppHost: реальные контейнеры PostgreSQL,
//  Redis, RabbitMQ и реальные процессы сервисов. Это интеграционные (end-to-end) тесты —
//  для них нужен запущенный Docker.
//
//  Стартовать приложение на каждый тест было бы долго, поэтому используем
//  IClassFixture: один запуск — много тестов.
// =====================================================================================
public sealed class CoffeeShopAppFixture : IAsyncLifetime
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);

    public DistributedApplication App { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        using var cts = new CancellationTokenSource(StartupTimeout);

        // Аргументы попадают в конфигурацию AppHost — так тест переопределяет параметры:
        //  • фиксированный пароль Postgres вместо сгенерированного и сохранённого в user secrets;
        //  • бариста "варит" быстро, чтобы тест не ждал по 3 секунды на напиток.
        string[] args =
        [
            "Parameters:postgres-password=Test_Passw0rd_For_Integration",
            "Parameters:barista-seconds-per-drink=0.2"
        ];

        // Projects.CoffeeShop_AppHost — класс, сгенерированный по ProjectReference на AppHost.
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.CoffeeShop_AppHost>(args, cts.Token);

        // Модель приложения можно МЕНЯТЬ перед запуском. В тестах нам не нужны постоянные
        // контейнеры и тома: каждый прогон должен начинаться с чистой базы.
        //
        // ⚠ Это не только про скорость, но и про ИЗОЛЯЦИЮ. Имя persistent-контейнера
        // (postgres-<хэш>) зависит от пути к AppHost — у тестов и у `aspire run` оно одинаковое.
        // Если оставить persistent, тест "захватит" контейнер разработки и пересоздаст его
        // со своим паролем. Поэтому снимаем аннотации времени жизни и тома:
        //   • PersistenceAnnotation и ContainerLifetimeAnnotation — их ставит WithLifetime(Persistent);
        //   • ContainerMountAnnotation — их ставит WithDataVolume().
        // Без них контейнеры становятся "сессионными": создаются с нуля и удаляются после тестов.
        //
        // PersistenceAnnotation в Aspire 17 помечена как экспериментальная (диагностика
        // ASPIREPERSISTENCE001): API может измениться. Осознанно подавляем предупреждение
        // только в этом месте — так принято поступать с experimental API.
#pragma warning disable ASPIREPERSISTENCE001
        foreach (var container in appHost.Resources.OfType<ContainerResource>())
        {
            foreach (var annotation in container.Annotations
                         .Where(a => a is PersistenceAnnotation or ContainerLifetimeAnnotation or ContainerMountAnnotation)
                         .ToList())
            {
                container.Annotations.Remove(annotation);
            }
        }
#pragma warning restore ASPIREPERSISTENCE001

        appHost.Services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Information);
            logging.AddFilter("Aspire.", LogLevel.Warning);
        });

        // HttpClient-ы, созданные через App.CreateHttpClient(...), тоже получают ретраи.
        appHost.Services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());

        App = await appHost.BuildAsync(cts.Token);
        await App.StartAsync(cts.Token);

        // web зависит (WaitFor) от menuapi и ordersapi, а они — от баз, Redis, RabbitMQ и
        // migrations. Значит, когда web Healthy — готово всё приложение.
        await App.ResourceNotifications.WaitForResourceHealthyAsync("web", cts.Token);
    }

    public async ValueTask DisposeAsync()
    {
        if (App is not null)
        {
            await App.DisposeAsync();
        }
    }
}
