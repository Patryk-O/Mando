using AnonymousSurvey.Application.Dtos;

namespace AnonymousSurvey.Application.Abstractions;

public interface IActiveSurveyQuery
{
    Task<ActiveSurveyDto?> GetActiveAsync(CancellationToken cancellationToken = default);
}
