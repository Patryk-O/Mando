using AnonymousSurvey.Application.Abstractions;
using AnonymousSurvey.Application.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace AnonymousSurvey.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<ISurveyResponseValidator, SurveyResponseValidator>();
        return services;
    }
}
