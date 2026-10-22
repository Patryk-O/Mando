using AnonymousSurvey.Application.Abstractions;
using AnonymousSurvey.Application.Dtos;
using AnonymousSurvey.Domain.Enums;
using AnonymousSurvey.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AnonymousSurvey.Infrastructure.Persistence.Repositories;

public sealed class SurveyResultsQuery(SurveyDbContext dbContext) : ISurveyResultsQuery
{
    public async Task<SurveyResultsDto?> GetByResultsTokenAsync(
        string resultsAccessToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(resultsAccessToken))
        {
            return null;
        }

        var survey = await SurveyWriteSupport.FindByResultsTokenAsync(
            dbContext,
            resultsAccessToken,
            cancellationToken);

        if (survey is null)
        {
            return null;
        }

        var sessions = await dbContext.ResponseSessions
            .AsNoTracking()
            .Where(s => s.SurveyId == survey.Id)
            .OrderByDescending(s => s.SubmittedAtUtc)
            .Select(s => new { s.Id, s.SubmittedAtUtc })
            .ToListAsync(cancellationToken);

        var answers = await dbContext.Answers
            .AsNoTracking()
            .Where(a => a.Session.SurveyId == survey.Id)
            .Select(a => new { a.SessionId, a.QuestionId, a.ScaleValue, a.BoolValue, a.TextValue })
            .ToListAsync(cancellationToken);

        var assigned = await dbContext.SurveyQuestions
            .AsNoTracking()
            .Where(sq => sq.SurveyId == survey.Id)
            .Select(sq => new { sq.QuestionId, sq.SortOrder, sq.AddedAtUtc })
            .ToListAsync(cancellationToken);

        var questionIds = assigned.Select(a => a.QuestionId)
            .Union(answers.Select(a => a.QuestionId))
            .ToHashSet();

        var assignedIds = assigned.Select(a => a.QuestionId).ToHashSet();
        var sortOrders = assigned.ToDictionary(a => a.QuestionId, a => a.SortOrder);
        var addedAt = assigned.ToDictionary(a => a.QuestionId, a => a.AddedAtUtc);

        var questions = (await dbContext.Questions
                .AsNoTracking()
                .Where(q => questionIds.Contains(q.Id))
                .ToListAsync(cancellationToken))
            .OrderBy(q => sortOrders.GetValueOrDefault(q.Id, int.MaxValue))
            .ThenBy(q => q.Id)
            .ToList();

        var scale = new List<ScaleQuestionAggregateDto>();
        var yesNo = new List<YesNoQuestionAggregateDto>();
        var open = new List<OpenQuestionAggregateDto>();

        foreach (var question in questions)
        {
            var questionAnswers = answers.Where(a => a.QuestionId == question.Id).ToList();
            var answeredSessions = questionAnswers
                .Where(answer => IsAnswered(question.Type, answer.ScaleValue, answer.BoolValue, answer.TextValue))
                .Select(answer => answer.SessionId)
                .ToHashSet();
            var isAssigned = assignedIds.Contains(question.Id);
            var eligible = isAssigned
                ? sessions.Count(session => session.SubmittedAtUtc >= addedAt[question.Id] || answeredSessions.Contains(session.Id))
                : answeredSessions.Count;

            switch (question.Type)
            {
                case QuestionType.Scale:
                    var values = questionAnswers.Where(a => a.ScaleValue.HasValue).Select(a => a.ScaleValue!.Value).ToList();
                    var distribution = Enumerable.Range(1, 5)
                        .ToDictionary(v => v, v => values.Count(x => x == v));
                    scale.Add(new ScaleQuestionAggregateDto(
                        question.Id,
                        question.Text,
                        values.Count == 0 ? 0 : Math.Round(values.Average(), 2),
                        distribution,
                        values.Count,
                        eligible,
                        isAssigned));
                    break;

                case QuestionType.YesNo:
                    yesNo.Add(new YesNoQuestionAggregateDto(
                        question.Id,
                        question.Text,
                        questionAnswers.Count(a => a.BoolValue == true),
                        questionAnswers.Count(a => a.BoolValue == false),
                        questionAnswers.Count(a => a.BoolValue.HasValue),
                        eligible,
                        isAssigned));
                    break;

                case QuestionType.Open:
                    var openAnswered = questionAnswers.Count(a => !string.IsNullOrWhiteSpace(a.TextValue));
                    open.Add(new OpenQuestionAggregateDto(
                        question.Id,
                        question.Text,
                        openAnswered,
                        openAnswered,
                        eligible,
                        isAssigned));
                    break;
            }
        }

        var rows = sessions
            .Select(s => new SurveyResponseRowDto(
                s.Id,
                s.SubmittedAtUtc,
                answers
                    .Where(a => a.SessionId == s.Id)
                    .Select(a => new AnswerInputDto(a.QuestionId, a.ScaleValue, a.BoolValue, a.TextValue))
                    .ToList()))
            .ToList();

        return new SurveyResultsDto(survey.Id, survey.Title, sessions.Count, scale, yesNo, open, rows);
    }

    private static bool IsAnswered(QuestionType type, int? scaleValue, bool? boolValue, string? textValue) => type switch
    {
        QuestionType.Scale => scaleValue.HasValue,
        QuestionType.YesNo => boolValue.HasValue,
        QuestionType.Open => !string.IsNullOrWhiteSpace(textValue),
        _ => scaleValue.HasValue || boolValue.HasValue || !string.IsNullOrWhiteSpace(textValue)
    };
}
