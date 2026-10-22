using AnonymousSurvey.Application.Common;
using AnonymousSurvey.Application.Dtos;

namespace AnonymousSurvey.Application.Abstractions;

public interface ISurveyAuthoringService
{
    /// <summary>Wszystkie aktywne pytania (Question.IsActive), rosnąco po Id.</summary>
    Task<IReadOnlyList<QuestionPoolItemDto>> GetQuestionPoolAsync(CancellationToken cancellationToken = default);

    Task<CreateSurveyOutcome> CreateAsync(CreateSurveyDto survey, CancellationToken cancellationToken = default);
}
