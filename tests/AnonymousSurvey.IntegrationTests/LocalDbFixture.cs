using AnonymousSurvey.Application;
using AnonymousSurvey.Infrastructure;
using AnonymousSurvey.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AnonymousSurvey.IntegrationTests;

[CollectionDefinition(Name)]
public sealed class LocalDbCollection : ICollectionFixture<LocalDbFixture>
{
    public const string Name = "LocalDb";
}

public sealed class LocalDbFixture : IAsyncLifetime
{
    public const string ResultsToken = "wyniki-4f9c2a7e1b3d48a69c5e0d7f2a8b6c13";
    public const string ConfigToken = "konfiguracja-8e1d5b3a9c7f42d6b0a4e2c9f7d1b538";

    private readonly string _databaseName = $"AnonymousSurvey_Test_{Guid.NewGuid():N}";

    public ServiceProvider Services { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:SurveyDb"] =
                    $"Server=(localdb)\\mssqllocaldb;Database={_databaseName};Trusted_Connection=True;TrustServerCertificate=True"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(configuration);
        Services = services.BuildServiceProvider();

        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await using (var scope = Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SurveyDbContext>();
            await db.Database.EnsureDeletedAsync();
        }

        await Services.DisposeAsync();
    }
}
