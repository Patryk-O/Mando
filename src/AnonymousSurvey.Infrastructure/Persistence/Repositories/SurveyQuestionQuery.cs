using AnonymousSurvey.Application.Abstractions;
using AnonymousSurvey.Application.Dtos;
using Microsoft.EntityFrameworkCore;

namespace AnonymousSurvey.Infrastructure.Persistence.Repositories;

/// <summary>
/// Pytania ankiety wyłącznie z funkcji tabelarycznej dbo.fn_GetSurveyQuestions przez EF Core.
/// </summary>
public sealed class SurveyQuestionQuery(SurveyDbContext dbContext) : ISurveyQuestionQuery
{
    public async Task<IReadOnlyList<SurveyQuestionDto>> GetQuestionsAsync(
        int surveyId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.GetSurveyQuestions(surveyId)
            .OrderBy(x => x.SortOrder)
            .Select(x => new SurveyQuestionDto(x.QuestionId, x.Text, x.Type, x.SortOrder))
            .ToListAsync(cancellationToken);
    }

    public async Task<SurveyQuestionDto?> GetQuestionAsync(
        int surveyId,
        int questionId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.GetSurveyQuestions(surveyId)
            .Where(x => x.QuestionId == questionId)
            .Select(x => new SurveyQuestionDto(x.QuestionId, x.Text, x.Type, x.SortOrder))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
