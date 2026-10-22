using System.Text.Json;
using AnonymousSurvey.Application;
using AnonymousSurvey.Infrastructure;
using AnonymousSurvey.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AnonymousSurvey.IntegrationTests;

/// <summary>
/// Tymczasowa baza LocalDB dla testów SQL (strumień A): osobna od fixture'a współdzielonego,
/// z możliwością odtworzenia starego schematu (9334b2f) i uruchomienia na nim <see cref="DatabaseInitializer"/>.
/// Prefiks nazwy jest inny niż <c>AnonymousSurvey_Test_</c>, żeby sprzątanie osieroconych baz innych strumieni nie kasowało baz w trakcie testu.
/// </summary>
public sealed class TempDatabase : IAsyncDisposable
{
    public const string Prefix = "AnonymousSurvey_SqlTest_";

    private const string Server = "Server=(localdb)\\mssqllocaldb;Trusted_Connection=True;TrustServerCertificate=True";

    private TempDatabase()
    {
        Name = $"{Prefix}{Guid.NewGuid():N}";
        ConnectionString = $"{Server};Database={Name}";
    }

    public string Name { get; }

    public string ConnectionString { get; }

    public static async Task<TempDatabase> CreateEmptyAsync()
    {
        var database = new TempDatabase();
        await using var connection = new SqlConnection($"{Server};Database=master");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{database.Name}]";
        await command.ExecuteNonQueryAsync();
        return database;
    }

    public static async Task<TempDatabase> CreateInitializedAsync()
    {
        var database = await CreateEmptyAsync();
        await database.InitializeSchemaAsync();
        return database;
    }

    public static async Task<TempDatabase> CreateLegacyAsync(bool withLegacySeed = false)
    {
        var database = await CreateEmptyAsync();
        await database.RunEmbeddedScriptAsync("LegacySchema.sql");
        await database.RunEmbeddedScriptAsync("LegacyFunction.sql");
        await database.RunEmbeddedScriptAsync("LegacyProcedure.sql");
        if (withLegacySeed)
        {
            await database.RunEmbeddedScriptAsync("LegacySeed.sql");
        }

        return database;
    }

    public ServiceProvider BuildServices()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:SurveyDb"] = ConnectionString })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(configuration);
        return services.BuildServiceProvider();
    }

    public async Task InitializeSchemaAsync()
    {
        await using var provider = BuildServices();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
    }

    public async Task RunEmbeddedScriptAsync(string fileName)
    {
        var assembly = typeof(TempDatabase).Assembly;
        var resource = assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith($".Resources.{fileName}", StringComparison.Ordinal));
        await using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        await ExecuteAsync(await reader.ReadToEndAsync());
    }

    public async Task<int> ExecuteAsync(string sql, params SqlParameter[] parameters)
    {
        await using var connection = await OpenAsync();
        await using var command = Command(connection, sql, parameters);
        return await command.ExecuteNonQueryAsync();
    }

    public async Task<T> ScalarAsync<T>(string sql, params SqlParameter[] parameters)
    {
        await using var connection = await OpenAsync();
        await using var command = Command(connection, sql, parameters);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? default! : (T)Convert.ChangeType(value, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T));
    }

    public async Task<List<object?[]>> QueryAsync(string sql, params SqlParameter[] parameters)
    {
        await using var connection = await OpenAsync();
        await using var command = Command(connection, sql, parameters);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<object?[]>();
        while (await reader.ReadAsync())
        {
            var row = new object?[reader.FieldCount];
            for (var i = 0; i < row.Length; i++)
            {
                row[i] = await reader.IsDBNullAsync(i) ? null : reader.GetValue(i);
            }

            rows.Add(row);
        }

        return rows;
    }

    public async Task<int> InsertSurveyAsync(int status = 2, bool isDefault = false)
    {
        var id = Guid.NewGuid().ToString("N");
        return await ScalarAsync<int>(
            """
            INSERT INTO dbo.Survey (Slug, Title, [Status], IsDefault, ResultsAccessToken, ConfigAccessToken)
            VALUES (@slug, N'Ankieta testowa', @status, @isDefault, @results, @config);
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """,
            new SqlParameter("@slug", $"t-{id}"),
            new SqlParameter("@status", status),
            new SqlParameter("@isDefault", isDefault),
            new SqlParameter("@results", $"r-{id}"),
            new SqlParameter("@config", $"c-{id}"));
    }

    public async Task<int> InsertQuestionAsync(int type, string? text = null, bool isActive = true) =>
        await ScalarAsync<int>(
            """
            INSERT INTO dbo.Question ([Text], [Type], IsActive) VALUES (@text, @type, @active);
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """,
            new SqlParameter("@text", text ?? $"Pytanie {Guid.NewGuid():N}"),
            new SqlParameter("@type", type),
            new SqlParameter("@active", isActive));

    public Task AssignAsync(int surveyId, int questionId, int sortOrder, bool isRequired = true) =>
        ExecuteAsync(
            "INSERT INTO dbo.SurveyQuestion (SurveyId, QuestionId, SortOrder, IsRequired) VALUES (@s, @q, @o, @r)",
            new SqlParameter("@s", surveyId),
            new SqlParameter("@q", questionId),
            new SqlParameter("@o", sortOrder),
            new SqlParameter("@r", isRequired));

    public async Task<Guid> SaveResponseAsync(int surveyId, string answersJson, Guid? sessionId = null)
    {
        var session = sessionId ?? Guid.NewGuid();
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "EXEC dbo.usp_SaveSurveyResponse @SurveyId = @s, @SessionId = @sid, @AnswersJson = @json";
        command.Parameters.AddWithValue("@s", surveyId);
        command.Parameters.AddWithValue("@sid", session);
        command.Parameters.Add(new SqlParameter("@json", System.Data.SqlDbType.NVarChar, -1) { Value = (object?)answersJson ?? DBNull.Value });
        await command.ExecuteNonQueryAsync();
        return session;
    }

    public static string Json(params (int QuestionId, int? Scale, bool? Bool, string? Text)[] answers) =>
        JsonSerializer.Serialize(
            answers.Select(a => new { questionId = a.QuestionId, scaleValue = a.Scale, boolValue = a.Bool, textValue = a.Text }));

    public Task<int> CountAsync(string table, string? where = null) =>
        ScalarAsync<int>($"SELECT COUNT(*) FROM dbo.{table}" + (where is null ? string.Empty : $" WHERE {where}"));

    public async ValueTask DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        try
        {
            await using var connection = new SqlConnection($"{Server};Database=master");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"IF DB_ID(N'{Name}') IS NOT NULL BEGIN ALTER DATABASE [{Name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Name}]; END";
            await command.ExecuteNonQueryAsync();
        }
        catch (SqlException)
        {
            // Sprzątanie best-effort: nieudane usunięcie nie może maskować wyniku testu.
        }
    }

    private async Task<SqlConnection> OpenAsync()
    {
        var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static SqlCommand Command(SqlConnection connection, string sql, SqlParameter[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        return command;
    }
}
