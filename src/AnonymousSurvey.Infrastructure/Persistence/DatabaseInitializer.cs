using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace AnonymousSurvey.Infrastructure.Persistence;

/// <summary>
/// Tworzy bazę (jeśli brak) i wykonuje idempotentne skrypty T-SQL z zasobów (schemat, TVF, SP, seed) przez EF Core.
/// </summary>
public sealed class DatabaseInitializer(SurveyDbContext dbContext, ILogger<DatabaseInitializer> logger)
{
    private const string ScriptPrefix = "AnonymousSurvey.Infrastructure.Sql.";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var creator = dbContext.GetService<IRelationalDatabaseCreator>();
        if (!await creator.ExistsAsync(cancellationToken))
        {
            logger.LogInformation("Tworzenie bazy danych...");
            await creator.CreateAsync(cancellationToken);
        }

        var assembly = typeof(DatabaseInitializer).Assembly;
        var scripts = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ScriptPrefix, StringComparison.Ordinal) && n.EndsWith(".sql", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal);

        foreach (var name in scripts)
        {
            logger.LogInformation("Wykonywanie skryptu {Script}", name[ScriptPrefix.Length..]);
            var sql = await ReadScriptAsync(assembly, name, cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        }
    }

    private static async Task<string> ReadScriptAsync(Assembly assembly, string resourceName, CancellationToken cancellationToken)
    {
        await using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Brak zasobu {resourceName}.");
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken);
    }
}
