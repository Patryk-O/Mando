using AnonymousSurvey.Application.Dtos;

namespace AnonymousSurvey.Application.Abstractions;

public interface ISurveyResultsQuery
{
    Task<SurveyResultsDto?> GetByResultsTokenAsync(
        string resultsAccessToken,
        CancellationToken cancellationToken = default);
}
