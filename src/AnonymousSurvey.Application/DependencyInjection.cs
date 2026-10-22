using Microsoft.Extensions.DependencyInjection;

namespace AnonymousSurvey.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Faza 2+: use-case'y, walidacja, interfejsy serwisów.
        return services;
    }
}
