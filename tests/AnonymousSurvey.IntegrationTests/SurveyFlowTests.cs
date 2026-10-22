using AnonymousSurvey.Application.Abstractions;
using AnonymousSurvey.Application.Dtos;
using AnonymousSurvey.Domain.Enums;
using AnonymousSurvey.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AnonymousSurvey.IntegrationTests;

public class SurveyFlowTests(LocalDbFixture fixture) : IClassFixture<LocalDbFixture>
{
    private const string Pending = "SM-A/C: schemat i serwisy w przebudowie";

    private AsyncServiceScope NewScope() => fixture.Services.CreateAsyncScope();

    private static async Task<SurveyPublicDto?> GetActiveAsync(AsyncServiceScope scope)
    {
        var catalog = scope.ServiceProvider.GetRequiredService<ISurveyCatalogQuery>();
        var slug = await catalog.GetDefaultSlugAsync();
        return slug is null ? null : await catalog.GetBySlugAsync(slug);
    }

    private static List<AnswerInputDto> ValidAnswers(IEnumerable<SurveyQuestionDto> questions) =>
        questions.Select(q => q.Type switch
        {
            QuestionType.Scale => new AnswerInputDto(q.QuestionId, 4, null, null),
            QuestionType.YesNo => new AnswerInputDto(q.QuestionId, null, true, null),
            _ => new AnswerInputDto(q.QuestionId, null, null, "Więcej opcji płatności")
        }).ToList();

    private static UpdateSurveyConfigDto ToUpdate(SurveyConfigDto current, IEnumerable<int> questionIds) =>
        new(
            current.SurveyId,
            current.Title,
            current.Description,
            current.Status,
            current.IsDefault,
            current.RowVersion,
            questionIds.Select(id => new SurveyItemInputDto(id, null, true)).ToList());

    [Fact(Skip = Pending)]
    public async Task Tvf_returns_seeded_questions_in_order_through_ef()
    {
        await using var scope = NewScope();
        var active = await GetActiveAsync(scope);
        Assert.NotNull(active);

        var questions = await scope.ServiceProvider.GetRequiredService<ISurveyQuestionQuery>()
            .GetQuestionsAsync(active.SurveyId);

        Assert.Equal(5, questions.Count);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, questions.Select(q => q.SortOrder));
        Assert.Contains(questions, q => q.Type == QuestionType.Scale);
        Assert.Contains(questions, q => q.Type == QuestionType.Open);
        Assert.Contains(questions, q => q.Type == QuestionType.YesNo);
    }

    [Fact(Skip = Pending)]
    public async Task Initializer_is_idempotent()
    {
        await using var scope = NewScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync();

        var db = scope.ServiceProvider.GetRequiredService<SurveyDbContext>();
        Assert.Equal(1, await db.Surveys.CountAsync());
    }

    [Fact(Skip = Pending)]
    public async Task Submission_saves_via_stored_procedure_and_appears_in_results()
    {
        await using var scope = NewScope();
        var active = (await GetActiveAsync(scope))!;
        var questions = await scope.ServiceProvider.GetRequiredService<ISurveyQuestionQuery>().GetQuestionsAsync(active.SurveyId);
        var results = scope.ServiceProvider.GetRequiredService<ISurveyResultsQuery>();

        var before = (await results.GetByResultsTokenAsync(LocalDbFixture.ResultsToken))!.TotalSessions;

        var outcome = await scope.ServiceProvider.GetRequiredService<ISurveySubmissionService>()
            .SubmitAsync(new SubmitSurveyResponseDto(active.SurveyId, ValidAnswers(questions)));

        Assert.True(outcome.Succeeded);

        var after = (await results.GetByResultsTokenAsync(LocalDbFixture.ResultsToken))!;
        Assert.Equal(before + 1, after.TotalSessions);
        Assert.Contains(after.RawResponses, r => r.SessionId == outcome.SessionId);
        Assert.NotEmpty(after.ScaleAggregates);
        Assert.NotEmpty(after.YesNoAggregates);
        Assert.NotEmpty(after.OpenAggregates);
    }

    [Fact(Skip = Pending)]
    public async Task Invalid_submission_is_rejected_and_not_stored()
    {
        await using var scope = NewScope();
        var active = (await GetActiveAsync(scope))!;
        var results = scope.ServiceProvider.GetRequiredService<ISurveyResultsQuery>();
        var before = (await results.GetByResultsTokenAsync(LocalDbFixture.ResultsToken))!.TotalSessions;

        var outcome = await scope.ServiceProvider.GetRequiredService<ISurveySubmissionService>()
            .SubmitAsync(new SubmitSurveyResponseDto(active.SurveyId, []));

        Assert.False(outcome.Succeeded);
        Assert.NotEmpty(outcome.Validation.Errors);
        Assert.Equal(before, (await results.GetByResultsTokenAsync(LocalDbFixture.ResultsToken))!.TotalSessions);
    }

    [Fact(Skip = Pending)]
    public async Task Stored_procedure_rolls_back_when_answers_do_not_match_survey()
    {
        await using var scope = NewScope();
        var active = (await GetActiveAsync(scope))!;
        var db = scope.ServiceProvider.GetRequiredService<SurveyDbContext>();
        var before = await db.ResponseSessions.CountAsync();

        var incomplete = new SubmitSurveyResponseDto(active.SurveyId, [new AnswerInputDto(1, 3, null, null)]);

        await Assert.ThrowsAsync<SqlException>(() =>
            scope.ServiceProvider.GetRequiredService<ISurveyResponseSaver>().SaveAsync(incomplete));

        Assert.Equal(before, await db.ResponseSessions.CountAsync());
    }

    [Fact(Skip = Pending)]
    public async Task Config_update_changes_question_set_returned_by_tvf()
    {
        await using var scope = NewScope();
        var config = scope.ServiceProvider.GetRequiredService<ISurveyConfigService>();
        var queries = scope.ServiceProvider.GetRequiredService<ISurveyQuestionQuery>();

        var current = (await config.GetByConfigTokenAsync(LocalDbFixture.ConfigToken))!;
        var originalIds = current.AssignedQuestions.OrderBy(q => q.SortOrder).Select(q => q.QuestionId).ToList();
        var extra = current.QuestionPool.First(p => !originalIds.Contains(p.QuestionId));

        try
        {
            var newOrder = new List<int> { extra.QuestionId, originalIds[0] };
            var result = await config.UpdateAsync(
                LocalDbFixture.ConfigToken,
                ToUpdate(current, newOrder));

            Assert.True(result.Succeeded);

            var questions = await queries.GetQuestionsAsync(current.SurveyId);
            Assert.Equal(newOrder, questions.Select(q => q.QuestionId));
        }
        finally
        {
            var latest = (await config.GetByConfigTokenAsync(LocalDbFixture.ConfigToken))!;
            await config.UpdateAsync(LocalDbFixture.ConfigToken, ToUpdate(latest, originalIds));
        }
    }

    [Fact(Skip = Pending)]
    public async Task Config_update_rejects_empty_assignment_and_wrong_token()
    {
        await using var scope = NewScope();
        var config = scope.ServiceProvider.GetRequiredService<ISurveyConfigService>();
        var current = (await config.GetByConfigTokenAsync(LocalDbFixture.ConfigToken))!;

        var empty = await config.UpdateAsync(LocalDbFixture.ConfigToken, ToUpdate(current, []));
        var wrongToken = await config.UpdateAsync("zly-token", ToUpdate(current, [1]));

        Assert.False(empty.Succeeded);
        Assert.False(wrongToken.Succeeded);
        Assert.Null(await config.GetByConfigTokenAsync("zly-token"));
    }
}
