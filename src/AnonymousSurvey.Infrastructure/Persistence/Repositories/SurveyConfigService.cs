using AnonymousSurvey.Application.Abstractions;
using AnonymousSurvey.Application.Common;
using AnonymousSurvey.Application.Dtos;
using Microsoft.EntityFrameworkCore;

namespace AnonymousSurvey.Infrastructure.Persistence.Repositories;

public sealed class SurveyConfigService(SurveyDbContext dbContext) : ISurveyConfigService
{
    public async Task<SurveyConfigDto?> GetByConfigTokenAsync(
        string configAccessToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(configAccessToken))
        {
            return null;
        }

        var survey = await dbContext.Surveys
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ConfigAccessToken == configAccessToken, cancellationToken);

        if (survey is null)
        {
            return null;
        }

        var answerCounts = await dbContext.Answers
            .AsNoTracking()
            .Where(a => a.Session.SurveyId == survey.Id)
            .GroupBy(a => a.QuestionId)
            .Select(g => new { QuestionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.QuestionId, x => x.Count, cancellationToken);

        var assigned = (await dbContext.SurveyQuestions
                .AsNoTracking()
                .Where(sq => sq.SurveyId == survey.Id)
                .OrderBy(sq => sq.SortOrder)
                .Select(sq => new { sq.QuestionId, sq.Question.Text, sq.Question.Type, sq.SortOrder, sq.IsRequired })
                .ToListAsync(cancellationToken))
            .Select(sq => new SurveyConfigQuestionDto(
                sq.QuestionId,
                sq.Text,
                sq.Type,
                sq.SortOrder,
                sq.IsRequired,
                answerCounts.GetValueOrDefault(sq.QuestionId)))
            .ToList();

        var pool = await dbContext.Questions
            .AsNoTracking()
            .Where(q => q.IsActive)
            .OrderBy(q => q.Id)
            .Select(q => new QuestionPoolItemDto(q.Id, q.Text, q.Type, q.IsActive))
            .ToListAsync(cancellationToken);

        var responseCount = await dbContext.ResponseSessions
            .AsNoTracking()
            .CountAsync(s => s.SurveyId == survey.Id, cancellationToken);

        return new SurveyConfigDto(
            survey.Id,
            survey.Slug,
            survey.Title,
            survey.Description,
            survey.Status,
            survey.IsDefault,
            responseCount,
            survey.RowVersion,
            assigned,
            pool);
    }

    public Task<UpdateSurveyConfigOutcome> UpdateAsync(
        string configAccessToken,
        UpdateSurveyConfigDto update,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("SM-C: zapis konfiguracji ankiety.");
}