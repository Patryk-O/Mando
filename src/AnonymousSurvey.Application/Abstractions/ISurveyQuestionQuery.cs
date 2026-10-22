using AnonymousSurvey.Application.Dtos;

namespace AnonymousSurvey.Application.Abstractions;

/// <summary>
/// Odczyt pytań ankiety (implementacja w Infrastructure przez TVF + EF Core).
/// </summary>
public interface ISurveyQuestionQuery
{
    Task<IReadOnlyList<SurveyQuestionDto>> GetQuestionsAsync(
        int surveyId,
        CancellationToken cancellationToken = default);

    Task<SurveyQuestionDto?> GetQuestionAsync(
        int surveyId,
        int questionId,
        CancellationToken cancellationToken = default);
}
