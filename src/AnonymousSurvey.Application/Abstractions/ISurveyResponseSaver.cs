using AnonymousSurvey.Application.Dtos;

namespace AnonymousSurvey.Application.Abstractions;

/// <summary>
/// Zapis odpowiedzi (implementacja w Infrastructure przez procedurę składowaną + EF Core).
/// </summary>
public interface ISurveyResponseSaver
{
    Task<SubmitSurveyResponseResultDto> SaveAsync(
        SubmitSurveyResponseDto submission,
        CancellationToken cancellationToken = default);
}
