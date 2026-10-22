using AnonymousSurvey.Application.Abstractions;
using AnonymousSurvey.Application.Common;
using AnonymousSurvey.Application.Dtos;
using AnonymousSurvey.Domain.Entities;
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

        var assigned = await dbContext.SurveyQuestions
            .AsNoTracking()
            .Where(sq => sq.SurveyId == survey.Id)
            .OrderBy(sq => sq.SortOrder)
            .Select(sq => new SurveyConfigQuestionDto(sq.QuestionId, sq.Question.Text, sq.Question.Type, sq.SortOrder))
            .ToListAsync(cancellationToken);

        var pool = await dbContext.Questions
            .AsNoTracking()
            .Where(q => q.IsActive)
            .OrderBy(q => q.Id)
            .Select(q => new QuestionPoolItemDto(q.Id, q.Text, q.Type, q.IsActive))
            .ToListAsync(cancellationToken);

        return new SurveyConfigDto(survey.Id, survey.Title, assigned, pool);
    }

    public async Task<ValidationResult> UpdateAssignmentsAsync(
        string configAccessToken,
        UpdateSurveyConfigDto update,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(configAccessToken))
        {
            return ValidationResult.Failure("Nieprawidłowy token konfiguracji.");
        }

        var survey = await dbContext.Surveys
            .FirstOrDefaultAsync(s => s.ConfigAccessToken == configAccessToken, cancellationToken);

        if (survey is null || survey.Id != update.SurveyId)
        {
            return ValidationResult.Failure("Nieprawidłowy token konfiguracji.");
        }

        var ordered = update.Assignments
            .Select((a, index) => (a.QuestionId, a.SortOrder, index))
            .OrderBy(a => a.SortOrder)
            .ThenBy(a => a.index)
            .Select(a => a.QuestionId)
            .ToList();

        if (ordered.Count == 0)
        {
            return ValidationResult.Failure("Wybierz co najmniej jedno pytanie.");
        }

        if (ordered.Distinct().Count() != ordered.Count)
        {
            return ValidationResult.Failure("Pytanie nie może być przypisane więcej niż raz.");
        }

        var validIds = await dbContext.Questions
            .Where(q => q.IsActive && ordered.Contains(q.Id))
            .Select(q => q.Id)
            .ToListAsync(cancellationToken);

        if (validIds.Count != ordered.Count)
        {
            return ValidationResult.Failure("Wybrano nieistniejące lub nieaktywne pytanie.");
        }

        var existing = await dbContext.SurveyQuestions
            .Where(sq => sq.SurveyId == survey.Id)
            .ToListAsync(cancellationToken);

        dbContext.SurveyQuestions.RemoveRange(existing.Where(e => !ordered.Contains(e.QuestionId)));

        for (var i = 0; i < ordered.Count; i++)
        {
            var questionId = ordered[i];
            var current = existing.FirstOrDefault(e => e.QuestionId == questionId);

            if (current is null)
            {
                dbContext.SurveyQuestions.Add(new SurveyQuestion
                {
                    SurveyId = survey.Id,
                    QuestionId = questionId,
                    SortOrder = i + 1
                });
            }
            else
            {
                current.SortOrder = i + 1;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return ValidationResult.Success();
    }
}
