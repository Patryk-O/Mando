using System.Text.RegularExpressions;
using AnonymousSurvey.Application;
using AnonymousSurvey.Infrastructure;
using AnonymousSurvey.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AnonymousSurvey.IntegrationTests;

/// <summary>
/// Tymczasowa baza LocalDB <c>AnonymousSurvey_Test_&lt;guid&gt;</c> z pełnym zestawem usług aplikacji.
/// Fabryka <see cref="CreateAsync"/> gwarantuje sprzątanie: nieudana inicjalizacja usuwa bazę, a
/// <see cref="DisposeAsync"/> zawsze próbuje ją usunąć. Przy pierwszym użyciu w procesie usuwane są też
/// osierocone bazy testowe starsze niż godzina (pozostałości po przerwanych przebiegach).
/// </summary>
public sealed class TempDatabase : IAsyncDisposable
{
    public const string NamePrefix = "AnonymousSurvey_Test_";

    public static readonly TimeSpan OrphanMinAge = TimeSpan.FromHours(1);

    private const string Server = "(localdb)\\mssqllocaldb";

    private static readonly Regex SafeName = new("^[A-Za-z0-9_]+$", RegexOptions.Compiled);

    private static readonly Lazy<Task> OrphanCleanup = new(CleanOrphansBestEffortAsync);

    private bool _disposed;

    private TempDatabase(string name)
    {
        Name = name;
        ConnectionString = BuildConnectionString(name);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:SurveyDb"] = ConnectionString })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(configuration);
        Services = services.BuildServiceProvider();
    }

    public string Name { get; }

    public string ConnectionString { get; }

    public ServiceProvider Services { get; }

    /// <summary>
    /// Tworzy bazę. <paramref name="initialize"/> = true uruchamia <see cref="DatabaseInitializer"/> (schemat, TVF, SP, seed);
    /// false zostawia pustą bazę (np. do odtworzenia schematu historycznego i testu upgrade'u, potem <see cref="ApplySchemaAsync"/>).
    /// <paramref name="afterCreate"/> wywoływane po utworzeniu; wyjątek z niego (lub z inicjalizacji) usuwa bazę.
    /// </summary>
    public static async Task<TempDatabase> CreateAsync(bool initialize = true, Func<TempDatabase, Task>? afterCreate = null)
    {
        await OrphanCleanup.Value;

        var database = new TempDatabase($"{NamePrefix}{Guid.NewGuid():N}");
        try
        {
            await ExecuteOnMasterAsync($"CREATE DATABASE {Quote(database.Name)};");
            if (initialize)
            {
                await database.ApplySchemaAsync();
            }

            if (afterCreate is not null)
            {
                await afterCreate(database);
            }

            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    public async Task ApplySchemaAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
    }

    public AsyncServiceScope NewScope() => Services.CreateAsyncScope();

    public Task<bool> ExistsAsync() => ExistsAsync(Name);

    public static async Task<bool> ExistsAsync(string databaseName)
    {
        await using var connection = new SqlConnection(BuildConnectionString("master"));
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT COUNT(*) FROM sys.databases WHERE name = @name", connection);
        command.Parameters.AddWithValue("@name", databaseName);
        return (int)(await command.ExecuteScalarAsync())! > 0;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            await Services.DisposeAsync();
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await DropAsync(Name);
        }
    }

    /// <summary>
    /// Usuwa bazy o nazwie zaczynającej się od <paramref name="namePrefix"/> (musi zaczynać się od <see cref="NamePrefix"/>),
    /// utworzone wcześniej niż <paramref name="minAge"/> temu. Baza zajęta przez inny proces jest pomijana.
    /// </summary>
    public static async Task<IReadOnlyList<string>> DropOrphanedAsync(TimeSpan minAge, string namePrefix = NamePrefix)
    {
        if (!namePrefix.StartsWith(NamePrefix, StringComparison.Ordinal) || !SafeName.IsMatch(namePrefix))
        {
            throw new ArgumentException($"Prefiks musi zaczynać się od {NamePrefix} i zawierać tylko litery, cyfry i _.", nameof(namePrefix));
        }

        var candidates = new List<string>();
        await using (var connection = new SqlConnection(BuildConnectionString("master")))
        {
            await connection.OpenAsync();
            await using var command = new SqlCommand(
                @"SELECT name FROM sys.databases
                  WHERE name LIKE @pattern ESCAPE '\' AND create_date < DATEADD(SECOND, -@ageSeconds, GETDATE())",
                connection);
            command.Parameters.AddWithValue("@pattern", namePrefix.Replace("_", "\\_") + "%");
            command.Parameters.AddWithValue("@ageSeconds", (int)Math.Min(minAge.TotalSeconds, int.MaxValue));
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                candidates.Add(reader.GetString(0));
            }
        }

        var dropped = new List<string>();
        foreach (var name in candidates)
        {
            try
            {
                await DropAsync(name);
                dropped.Add(name);
            }
            catch (SqlException)
            {
                // baza używana przez inny, aktywny proces — zostawiamy
            }
        }

        return dropped;
    }

    private static async Task CleanOrphansBestEffortAsync()
    {
        try
        {
            await DropOrphanedAsync(OrphanMinAge);
        }
        catch (SqlException)
        {
            // sprzątanie jest pomocnicze; brak dostępu do master nie powinien przerywać testów
        }
    }

    private static async Task DropAsync(string databaseName)
    {
        var quoted = Quote(databaseName);
        await ExecuteOnMasterAsync(
            $"IF DB_ID(N'{databaseName}') IS NOT NULL BEGIN ALTER DATABASE {quoted} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {quoted}; END");
    }

    private static async Task ExecuteOnMasterAsync(string sql)
    {
        await using var connection = new SqlConnection(BuildConnectionString("master"));
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync();
    }

    private static string Quote(string databaseName)
    {
        if (!SafeName.IsMatch(databaseName))
        {
            throw new ArgumentException($"Niedozwolona nazwa bazy: {databaseName}", nameof(databaseName));
        }

        return $"[{databaseName}]";
    }

    private static string BuildConnectionString(string database) =>
        $"Server={Server};Database={database};Trusted_Connection=True;TrustServerCertificate=True";
}
