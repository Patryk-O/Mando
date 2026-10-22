using AnonymousSurvey.Application.Abstractions;
using AnonymousSurvey.Application.Dtos;
using Microsoft.EntityFrameworkCore;

namespace AnonymousSurvey.Infrastructure.Persistence.Repositories;

public sealed class ActiveSurveyQuery(SurveyDbContext dbContext) : IActiveSurveyQuery
{
    public async Task<ActiveSurveyDto?> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.Surveys
            .AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.Id)
            .Select(s => new ActiveSurveyDto(s.Id, s.Title))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
