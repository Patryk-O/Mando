using AnonymousSurvey.Application.Abstractions;
using AnonymousSurvey.Infrastructure.Persistence;
using AnonymousSurvey.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AnonymousSurvey.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "SurveyDb";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Brak connection stringa „{ConnectionStringName}”. Ustaw go w appsettings.Development.json lub User Secrets.");
        }

        services.AddDbContext<SurveyDbContext>(options => options.UseSqlServer(connectionString));

        services.AddScoped<DatabaseInitializer>();
        services.AddScoped<ISurveyQuestionQuery, SurveyQuestionQuery>();
        services.AddScoped<ISurveyResponseSaver, SurveyResponseSaver>();
        services.AddScoped<IActiveSurveyQuery, ActiveSurveyQuery>();
        services.AddScoped<ISurveyResultsQuery, SurveyResultsQuery>();
        services.AddScoped<ISurveyConfigService, SurveyConfigService>();

        return services;
    }
}
