using AnonymousSurvey.Application.Abstractions;
using AnonymousSurvey.Application.Common;
using AnonymousSurvey.Application.Dtos;

namespace AnonymousSurvey.Infrastructure.Persistence.Repositories;

public sealed class SurveyAuthoringService(SurveyDbContext dbContext) : ISurveyAuthoringService
{
    public Task<IReadOnlyList<QuestionPoolItemDto>> GetQuestionPoolAsync(CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("SM-C: tworzenie ankiet.");

    public Task<CreateSurveyOutcome> CreateAsync(CreateSurveyDto survey, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("SM-C: tworzenie ankiet.");
}