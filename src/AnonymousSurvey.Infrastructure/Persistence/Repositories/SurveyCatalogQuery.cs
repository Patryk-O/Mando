using AnonymousSurvey.Application.Abstractions;
using AnonymousSurvey.Application.Dtos;

namespace AnonymousSurvey.Infrastructure.Persistence.Repositories;

public sealed class SurveyCatalogQuery(SurveyDbContext dbContext) : ISurveyCatalogQuery
{
    public Task<SurveyPublicDto?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("SM-C: katalog ankiet.");

    public Task<string?> GetDefaultSlugAsync(CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("SM-C: katalog ankiet.");
}