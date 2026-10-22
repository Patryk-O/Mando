using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AnonymousSurvey.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Faza 3+: DbContext, repozytoria, integracja TVF/SP przez EF Core.
        _ = configuration.GetConnectionString("SurveyDb");
        return services;
    }
}
