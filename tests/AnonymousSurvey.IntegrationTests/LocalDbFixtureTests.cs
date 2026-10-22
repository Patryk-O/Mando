using System.Collections.Concurrent;
using AnonymousSurvey.Domain.Enums;
using AnonymousSurvey.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AnonymousSurvey.IntegrationTests;

/// <summary>Testy fabryki <see cref="TempDatabase"/> i sprzątania (bez fixture — same tworzą i usuwają bazy).</summary>
public class TempDatabaseTests
{
    [Fact]
    public async Task Create_returns_distinct_databases_and_dispose_drops_them()
    {
        var first = await TempDatabase.CreateAsync();
        var second = await TempDatabase.CreateAsync();
        var names = new[] { first.Name, second.Name };
        try
        {
            Assert.NotEqual(first.Name, second.Name);
            Assert.All(names, n => Assert.StartsWith(TempDatabase.NamePrefix, n));
            Assert.True(await first.ExistsAsync());
            Assert.True(await second.ExistsAsync());
        }
        finally
        {
            await first.DisposeAsync();
            await second.DisposeAsync();
        }

        foreach (var name in names)
        {
            Assert.False(await TempDatabase.ExistsAsync(name), $"Baza {name} powinna zostać usunięta.");
        }
    }

    [Fact]
    public async Task Dispose_is_idempotent()
    {
        var database = await TempDatabase.CreateAsync(initialize: false);
        await database.DisposeAsync();
        await database.DisposeAsync();

        Assert.False(await TempDatabase.ExistsAsync(database.Name));
    }

    [Fact]
    public async Task Failure_after_create_drops_the_database()
    {
        string? name = null;

        await Assert.ThrowsAsync<InvalidOperationException>(() => TempDatabase.CreateAsync(afterCreate: db =>
        {
            name = db.Name;
            throw new InvalidOperationException("błąd inicjalizacji");
        }));

        Assert.NotNull(name);
        Assert.False(await TempDatabase.ExistsAsync(name));
    }

    [Fact]
    public async Task Initialize_false_gives_empty_database_and_ApplySchema_creates_the_schema()
    {
        await using var database = await TempDatabase.CreateAsync(initialize: false);

        Assert.True(await database.ExistsAsync());
        Assert.Equal(0, await CountTablesAsync(database));

        await database.ApplySchemaAsync();

        Assert.True(await CountTablesAsync(database) > 0);
    }

    [Fact]
    public async Task DropOrphaned_drops_only_old_databases_with_matching_prefix()
    {
        var token = Guid.NewGuid().ToString("N");
        var targetPrefix = $"{TempDatabase.NamePrefix}Probe{token}_";
        var target = targetPrefix + "target";
        var bystander = $"{TempDatabase.NamePrefix}Bystander{token}";

        await ExecuteOnMasterAsync($"CREATE DATABASE [{target}]; CREATE DATABASE [{bystander}];");
        try
        {
            Assert.Empty(await TempDatabase.DropOrphanedAsync(TempDatabase.OrphanMinAge, targetPrefix));
            Assert.True(await TempDatabase.ExistsAsync(target));

            await Task.Delay(50);
            var dropped = await TempDatabase.DropOrphanedAsync(TimeSpan.Zero, targetPrefix);

            Assert.Equal([target], dropped);
            Assert.False(await TempDatabase.ExistsAsync(target));
            Assert.True(await TempDatabase.ExistsAsync(bystander));
        }
        finally
        {
            await TempDatabase.DropOrphanedAsync(TimeSpan.Zero, $"{TempDatabase.NamePrefix}Probe{token}_");
            await TempDatabase.DropOrphanedAsync(TimeSpan.Zero, $"{TempDatabase.NamePrefix}Bystander{token}");
        }
    }

    [Theory]
    [InlineData("AnonymousSurvey")]
    [InlineData("AnonymousSurvey_E2E_")]
    [InlineData("")]
    [InlineData("AnonymousSurvey_Test_'; DROP DATABASE master; --")]
    public async Task DropOrphaned_rejects_prefix_outside_test_databases(string prefix)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => TempDatabase.DropOrphanedAsync(TimeSpan.Zero, prefix));
    }

    private static async Task<int> CountTablesAsync(TempDatabase database)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT COUNT(*) FROM sys.tables", connection);
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private static async Task ExecuteOnMasterAsync(string sql)
    {
        await using var connection = new SqlConnection(
            "Server=(localdb)\\mssqllocaldb;Database=master;Trusted_Connection=True;TrustServerCertificate=True");
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}

/// <summary>
/// Dowód „baza na klasę”: dwie klasy testowe (w jednej kolekcji, więc uruchamiane sekwencyjnie) dostają różne bazy;
/// dopiero druga w kolejności porównuje swoją bazę z zapisaną przez pierwszą.
/// </summary>
[Collection(FixtureProbes.Name)]
public class FixtureProbeA(LocalDbFixture fixture) : IClassFixture<LocalDbFixture>
{
    [Fact]
    public async Task Class_gets_its_own_existing_database() => await FixtureProbes.VerifyAsync(nameof(FixtureProbeA), fixture);
}

[Collection(FixtureProbes.Name)]
public class FixtureProbeB(LocalDbFixture fixture) : IClassFixture<LocalDbFixture>
{
    [Fact]
    public async Task Class_gets_its_own_existing_database() => await FixtureProbes.VerifyAsync(nameof(FixtureProbeB), fixture);
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class FixtureProbes
{
    public const string Name = "FixtureProbes";

    private static readonly ConcurrentDictionary<string, string> Databases = new();

    public static async Task VerifyAsync(string probe, LocalDbFixture fixture)
    {
        Assert.True(await TempDatabase.ExistsAsync(fixture.DatabaseName));
        Assert.StartsWith(TempDatabase.NamePrefix, fixture.DatabaseName);

        Databases[probe] = fixture.DatabaseName;
        Assert.Equal(Databases.Count, Databases.Values.Distinct().Count());
    }
}

/// <summary>Helper <see cref="LocalDbFixture.CreateSurveyAsync"/> — wymaga nowego schematu (strumień A).</summary>
public class LocalDbFixtureSurveyTests(LocalDbFixture fixture) : IClassFixture<LocalDbFixture>
{
    [NewSchemaFact]
    public async Task CreateSurvey_returns_unique_slug_tokens_and_own_questions()
    {
        var first = await fixture.CreateSurveyAsync();
        var second = await fixture.CreateSurveyAsync();

        Assert.NotEqual(first.Slug, second.Slug);
        Assert.NotEqual(first.ResultsToken, second.ResultsToken);
        Assert.NotEqual(first.ConfigToken, second.ConfigToken);
        Assert.Empty(first.QuestionIds.Intersect(second.QuestionIds));
        Assert.Equal(3, first.QuestionIds.Count);

        await using var scope = fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<SurveyDbContext>();
        var stored = await db.Surveys.Include(s => s.SurveyQuestions).SingleAsync(s => s.Id == first.Id);

        Assert.Equal(SurveyStatus.Active, stored.Status);
        Assert.False(stored.IsDefault);
        Assert.Equal(first.QuestionIds, stored.SurveyQuestions.OrderBy(q => q.SortOrder).Select(q => q.QuestionId));
        Assert.All(stored.SurveyQuestions, q => Assert.True(q.IsRequired));
    }

    [NewSchemaFact]
    public async Task CreateSurvey_honours_status_default_flag_and_optional_questions()
    {
        var seedDefaults = await CountDefaultsAsync();
        Assert.True(seedDefaults <= 1);

        await fixture.CreateSurveyAsync(isDefault: true);
        var draft = await fixture.CreateSurveyAsync(
            SurveyStatus.Draft,
            [new TestQuestion(QuestionType.Open, IsRequired: false)],
            isDefault: true);

        Assert.Equal(1, await CountDefaultsAsync());

        await using var scope = fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<SurveyDbContext>();
        var stored = await db.Surveys.Include(s => s.SurveyQuestions).SingleAsync(s => s.Id == draft.Id);
        Assert.Equal(SurveyStatus.Draft, stored.Status);
        Assert.True(stored.IsDefault);
        Assert.False(Assert.Single(stored.SurveyQuestions).IsRequired);
    }

    private async Task<int> CountDefaultsAsync()
    {
        await using var scope = fixture.NewScope();
        return await scope.ServiceProvider.GetRequiredService<SurveyDbContext>().Surveys.CountAsync(s => s.IsDefault);
    }
}
