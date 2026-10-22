using AnonymousSurvey.Application.Abstractions;
using AnonymousSurvey.Application.Dtos;
using AnonymousSurvey.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace AnonymousSurvey.Infrastructure.Persistence.Repositories;

public sealed class SurveyCatalogQuery(SurveyDbContext dbContext) : ISurveyCatalogQuery
{
    public async Task<SurveyPublicDto?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        var normalized = slug.Trim().ToLowerInvariant();
        var survey = await dbContext.Surveys
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Slug == normalized, cancellationToken);

        if (survey is null || survey.Status == SurveyStatus.Draft)
        {
            return null;
        }

        return new SurveyPublicDto(survey.Id, survey.Slug, survey.Title, survey.Description, survey.Status);
    }

    public async Task<string?> GetDefaultSlugAsync(CancellationToken cancellationToken = default)
    {
        var preferred = await dbContext.Surveys
            .AsNoTracking()
            .Where(survey => survey.IsDefault && survey.Status == SurveyStatus.Active)
            .Select(survey => survey.Slug)
            .FirstOrDefaultAsync(cancellationToken);

        if (preferred is not null)
        {
            return preferred;
        }

        return await dbContext.Surveys
            .AsNoTracking()
            .Where(survey => survey.Status == SurveyStatus.Active)
            .OrderBy(survey => survey.CreatedAtUtc)
            .ThenBy(survey => survey.Id)
            .Select(survey => survey.Slug)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
