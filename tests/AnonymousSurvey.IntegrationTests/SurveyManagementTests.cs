using AnonymousSurvey.Application.Abstractions;
using AnonymousSurvey.Application.Common;
using AnonymousSurvey.Application.Dtos;
using AnonymousSurvey.Domain.Entities;
using AnonymousSurvey.Domain.Enums;
using AnonymousSurvey.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AnonymousSurvey.IntegrationTests;

public class SurveyManagementTests(LocalDbFixture fixture) : IClassFixture<LocalDbFixture>
{
    private const string SlugTaken = "Ten adres jest już zajęty.";
    private const string ActiveNeedsQuestion = "Ankieta aktywna musi mieć co najmniej jedno pytanie.";
    private const string TooManyItems = "Ankieta może zawierać maksymalnie 30 pytań.";
    private const string InvalidToken = "Nieprawidłowy token konfiguracji.";
    private const string CannotUnsetDefault = "Aby zmienić ankietę domyślną, ustaw inną jako domyślną.";
    private const string ConcurrencyMessage = "Konfiguracja została zmieniona przez kogoś innego. Odśwież stronę.";
    private const string PoolDuplicateFragment = "już istnieje w puli";

    private AsyncServiceScope NewScope() => fixture.Services.CreateAsyncScope();

    private static string Id() => Guid.NewGuid().ToString("N")[..12];

    public static IEnumerable<object[]> StatusTransitions()
    {
        foreach (var from in Enum.GetValues<SurveyStatus>())
        {
            foreach (var to in Enum.GetValues<SurveyStatus>())
            {
                foreach (var emptyItems in new[] { false, true })
                {
                    foreach (var withResponses in new[] { false, true })
                    {
                        yield return new object[] { from, to, emptyItems, withResponses };
                    }
                }
            }
        }
    }

    [Fact]
    public async Task Create_survey_assigns_new_question_only_to_new_survey()
    {
        var before = await AssignmentMapAsync();
        var text = $"Tylko nowa {Id()}";
        var created = await CreateAsync($"Nowa {Id()}", SurveyStatus.Active, [NewItem(text, QuestionType.Open, false)]);

        Assert.True(created.Succeeded);
        Assert.Matches("^wyniki-[0-9a-f]{32}$", created.Result!.ResultsAccessToken);
        Assert.Matches("^konfiguracja-[0-9a-f]{32}$", created.Result.ConfigAccessToken);
        Assert.NotEqual(created.Result.ResultsAccessToken, created.Result.ConfigAccessToken);

        var config = await GetConfigAsync(created.Result.ConfigAccessToken);
        var questionId = config.AssignedQuestions.Single(q => q.Text == text).QuestionId;
        var links = await CountLinksAsync(questionId);
        Assert.Equal(1, links);

        var after = await AssignmentMapAsync();
        foreach (var (surveyId, questions) in before)
        {
            Assert.Equal(questions, after[surveyId]);
        }

        Assert.Equal([questionId], after[created.Result.SurveyId].Where(id => !before.Values.SelectMany(v => v).Contains(id)).ToList());
        Assert.Equal(1, await CountDefaultsAsync());
    }

    [Fact]
    public async Task Create_survey_new_question_is_in_pool_for_other_surveys_but_not_assigned()
    {
        var existing = await fixture.CreateSurveyAsync();
        var before = (await GetConfigAsync(existing.ConfigToken)).AssignedQuestions.Select(q => q.QuestionId).ToList();
        var text = $"Pula {Id()}";

        var created = await CreateAsync($"Ankieta B {Id()}", SurveyStatus.Active, [NewItem(text, QuestionType.YesNo)]);
        Assert.True(created.Succeeded);

        var configA = await GetConfigAsync(existing.ConfigToken);
        Assert.Equal(before, configA.AssignedQuestions.Select(q => q.QuestionId));
        Assert.Contains(configA.QuestionPool, q => q.Text == text && q.IsActive);
        Assert.DoesNotContain(configA.AssignedQuestions, q => q.Text == text);

        var pool = await PoolAsync();
        Assert.Contains(pool, q => q.Text == text);
        Assert.Equal(pool.Select(q => q.QuestionId), pool.OrderBy(q => q.QuestionId).Select(q => q.QuestionId));
    }

    [Fact]
    public async Task Config_add_new_question_does_not_touch_other_surveys()
    {
        var target = await fixture.CreateSurveyAsync();
        var other = await fixture.CreateSurveyAsync();
        var otherBefore = (await GetConfigAsync(other.ConfigToken)).AssignedQuestions.Select(q => q.QuestionId).ToList();
        var text = $"Dodane {Id()}";

        var updated = await UpdateAsync(
            target.ConfigToken,
            items: AssignedItems(await GetConfigAsync(target.ConfigToken))
                .Append(NewItem(text, QuestionType.Scale, false))
                .ToList());

        Assert.True(updated.Succeeded);
        var targetConfig = await GetConfigAsync(target.ConfigToken);
        var added = targetConfig.AssignedQuestions.Single(q => q.Text == text);
        Assert.False(added.IsRequired);
        Assert.Equal(1, await CountLinksAsync(added.QuestionId));

        var otherConfig = await GetConfigAsync(other.ConfigToken);
        Assert.Equal(otherBefore, otherConfig.AssignedQuestions.Select(q => q.QuestionId));
        Assert.Contains(otherConfig.QuestionPool, q => q.QuestionId == added.QuestionId);
        Assert.DoesNotContain(otherConfig.AssignedQuestions, q => q.QuestionId == added.QuestionId);
    }

    [Fact]
    public async Task Config_update_never_deletes_responses()
    {
        var survey = await fixture.CreateSurveyAsync(questions: [new TestQuestion(QuestionType.Scale), new TestQuestion(QuestionType.Open)]);
        var questions = await QuestionsAsync(survey.Id);
        var submitted = await SubmitAsync(survey.Id, questions);
        Assert.True(submitted.Succeeded);

        var sessionsBefore = await CountSessionsAsync(survey.Id);
        var answersBefore = await CountAnswersAsync(survey.Id);
        var current = await GetConfigAsync(survey.ConfigToken);
        var removed = current.AssignedQuestions.OrderBy(q => q.SortOrder).Last();
        var kept = AssignedItems(current).Where(i => i.QuestionId != removed.QuestionId).ToList();

        var updated = await UpdateAsync(survey.ConfigToken, items: kept);
        Assert.True(updated.Succeeded);
        Assert.Equal(sessionsBefore, await CountSessionsAsync(survey.Id));
        Assert.Equal(answersBefore, await CountAnswersAsync(survey.Id));

        var results = await ResultsAsync(survey.ResultsToken);
        var metrics = Metrics(results, removed.QuestionId);
        Assert.False(metrics.Assigned);
        Assert.True(metrics.Answered > 0);
        Assert.Equal(metrics.Answered, metrics.Eligible);
    }

    [Fact]
    public async Task Config_update_changes_required_flag_and_tvf_reflects_it()
    {
        var survey = await fixture.CreateSurveyAsync();
        var current = await GetConfigAsync(survey.ConfigToken);
        var items = AssignedItems(current);
        items[0] = items[0] with { IsRequired = false };

        var updated = await UpdateAsync(survey.ConfigToken, items: items);
        Assert.True(updated.Succeeded);

        var questions = await QuestionsAsync(survey.Id);
        Assert.False(questions.Single(q => q.QuestionId == items[0].QuestionId).IsRequired);
        Assert.All(questions.Where(q => q.QuestionId != items[0].QuestionId), q => Assert.True(q.IsRequired));
    }

    [Fact]
    public async Task Create_rejects_duplicate_pool_question()
    {
        var before = await CountsAsync();
        var text = "Jak oceniasz ogólne wrażenia z korzystania z naszej usługi?";
        var created = await CreateAsync($"Duplikat {Id()}", SurveyStatus.Active, [NewItem(text, QuestionType.Scale)]);

        Assert.False(created.Succeeded);
        Assert.Null(created.Result);
        Assert.Contains(PoolMessage(text), created.Validation.Errors);
        Assert.Equal(before, await CountsAsync());
    }

    [Fact]
    public async Task Create_slug_collision_adds_suffix()
    {
        var title = $"Kolizja {Id()}";
        var baseSlug = SlugGenerator.FromTitle(title);
        var first = await CreateAsync(title, SurveyStatus.Draft);
        var second = await CreateAsync(title, SurveyStatus.Draft);

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal(baseSlug, first.Result!.Slug);
        Assert.Equal(baseSlug + "-2", second.Result!.Slug);
        Assert.NotEqual(first.Result.SurveyId, second.Result.SurveyId);
    }

    [Fact]
    public async Task Create_manual_slug_collision_is_error()
    {
        var slug = $"reczny-{Id()}";
        var first = await CreateAsync($"Reczny {Id()}", SurveyStatus.Draft, slug: slug);
        Assert.True(first.Succeeded);

        var before = await CountsAsync();
        var again = await CreateAsync($"Inny {Id()}", SurveyStatus.Draft, slug: slug);
        Assert.False(again.Succeeded);
        Assert.Contains(SlugTaken, again.Validation.Errors);
        Assert.Equal(before, await CountsAsync());

        var invalid = await CreateAsync($"Zly {Id()}", SurveyStatus.Draft, slug: "AB");
        Assert.False(invalid.Succeeded);
        Assert.Equal(before, await CountsAsync());
    }

    [Fact]
    public async Task Create_active_requires_at_least_one_question()
    {
        var created = await CreateAsync($"Pusta aktywna {Id()}", SurveyStatus.Active);
        Assert.False(created.Succeeded);
        Assert.Contains(ActiveNeedsQuestion, created.Validation.Errors);
    }

    [Fact]
    public async Task Draft_is_hidden_from_catalog_closed_is_visible()
    {
        var draft = await fixture.CreateSurveyAsync(SurveyStatus.Draft);
        var closed = await fixture.CreateSurveyAsync(SurveyStatus.Closed);
        var active = await fixture.CreateSurveyAsync();

        Assert.Null(await BySlugAsync(draft.Slug));
        Assert.Null(await BySlugAsync(draft.Slug.ToUpperInvariant()));

        var closedDto = await BySlugAsync(closed.Slug.ToUpperInvariant());
        Assert.NotNull(closedDto);
        Assert.Equal(closed.Slug, closedDto!.Slug);
        Assert.Equal(SurveyStatus.Closed, closedDto.Status);
        Assert.Equal(closed.Title, closedDto.Title);

        var activeDto = await BySlugAsync(active.Slug);
        Assert.NotNull(activeDto);
        Assert.Equal(SurveyStatus.Active, activeDto!.Status);
        Assert.Equal(active.Slug, activeDto.Slug);
    }

    [Fact]
    public async Task Default_slug_prefers_default_active_then_first_active_then_null()
    {
        await ResetSeedDefaultActiveAsync();
        var snapshot = await SnapshotAsync();
        int? probeId = null;
        try
        {
            Assert.Equal("ankieta-satysfakcji", await DefaultSlugAsync());

            await UpdateAsync(LocalDbFixture.ConfigToken, status: SurveyStatus.Draft, isDefault: true);
            var probe = await fixture.CreateSurveyAsync();
            probeId = probe.Id;
            await SetCreatedAtAsync(probe.Id, new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            Assert.Equal(probe.Slug, await DefaultSlugAsync());

            await CloseAllAsync();
            Assert.Null(await DefaultSlugAsync());
        }
        finally
        {
            if (probeId is int id)
            {
                await SetCreatedAtAsync(id, DateTime.UtcNow);
            }

            await RestoreSnapshotAsync(snapshot);
            await ResetSeedDefaultActiveAsync();
        }
    }

    [Fact]
    public async Task Set_default_clears_other_defaults()
    {
        try
        {
            await ResetSeedDefaultActiveAsync();
            var survey = await fixture.CreateSurveyAsync();
            var updated = await UpdateAsync(survey.ConfigToken, isDefault: true);
            Assert.True(updated.Succeeded);

            await using var scope = NewScope();
            var db = scope.ServiceProvider.GetRequiredService<SurveyDbContext>();
            var defaults = await db.Surveys.Where(s => s.IsDefault).Select(s => s.Id).ToListAsync();
            Assert.Equal([survey.Id], defaults);
        }
        finally
        {
            await ResetSeedDefaultActiveAsync();
        }
    }

    [Fact]
    public async Task Cannot_unset_only_default()
    {
        await ResetSeedDefaultActiveAsync();
        var before = await CountsAsync();
        var current = await GetConfigAsync(LocalDbFixture.ConfigToken);
        var updated = await UpdateAsync(LocalDbFixture.ConfigToken, isDefault: false);

        Assert.False(updated.Succeeded);
        Assert.Contains(CannotUnsetDefault, updated.Validation.Errors);
        Assert.False(updated.IsConcurrencyConflict);
        var after = await GetConfigAsync(LocalDbFixture.ConfigToken);
        Assert.True(after.IsDefault);
        Assert.Equal(SurveyStatus.Active, after.Status);
        Assert.Equal(before, await CountsAsync());
        Assert.Equal(1, await CountDefaultsAsync());
    }

    [Fact]
    public async Task Config_exposes_response_count_and_answer_counts()
    {
        var survey = await fixture.CreateSurveyAsync(questions:
        [
            new TestQuestion(QuestionType.Scale),
            new TestQuestion(QuestionType.Open, false)
        ]);
        var questions = await QuestionsAsync(survey.Id);
        var open = questions.Single(q => q.Type == QuestionType.Open);
        Assert.True((await SubmitAsync(survey.Id, questions)).Succeeded);
        Assert.True((await SubmitAsync(survey.Id, questions.Where(q => q.QuestionId != open.QuestionId))).Succeeded);

        var config = await GetConfigAsync(survey.ConfigToken);
        Assert.Equal(2, config.ResponseCount);
        Assert.Equal(2, config.AssignedQuestions.Single(q => q.Type == QuestionType.Scale).AnswerCount);
        Assert.Equal(1, config.AssignedQuestions.Single(q => q.QuestionId == open.QuestionId).AnswerCount);
        Assert.NotEmpty(config.RowVersion);
        Assert.Contains(config.QuestionPool, q => q.QuestionId == open.QuestionId);
    }

    [Fact]
    public async Task Submission_to_closed_survey_is_rejected_by_sp()
    {
        var survey = await fixture.CreateSurveyAsync(questions: [new TestQuestion(QuestionType.Scale)]);
        var closed = await UpdateAsync(survey.ConfigToken, status: SurveyStatus.Closed);
        Assert.True(closed.Succeeded);

        var questions = await QuestionsAsync(survey.Id);
        var before = await CountSessionsAsync(survey.Id);
        var submitted = await SubmitAsync(survey.Id, questions);

        Assert.False(submitted.Succeeded);
        Assert.Equal(SubmissionConflict.SurveyClosed, submitted.Conflict);
        Assert.Contains("została zamknięta", submitted.Validation.Errors[0]);
        Assert.Equal(before, await CountSessionsAsync(survey.Id));

        await using var scope = NewScope();
        var error = await Assert.ThrowsAsync<SurveyChangedException>(() =>
            scope.ServiceProvider.GetRequiredService<ISurveyResponseSaver>()
                .SaveAsync(new SubmitSurveyResponseDto(survey.Id, questions.Select(ValidAnswer).ToList())));
        Assert.Equal(SubmissionConflict.SurveyClosed, error.Conflict);
        Assert.Equal(before, await CountSessionsAsync(survey.Id));
    }

    [Fact]
    public async Task Submission_skips_optional_question_end_to_end()
    {
        var survey = await fixture.CreateSurveyAsync(questions:
        [
            new TestQuestion(QuestionType.Scale),
            new TestQuestion(QuestionType.Open, false)
        ]);
        var questions = await QuestionsAsync(survey.Id);
        var optional = questions.Single(q => !q.IsRequired);
        var submitted = await SubmitAsync(survey.Id, questions.Where(q => q.IsRequired));

        Assert.True(submitted.Succeeded);
        Assert.Equal(SubmissionConflict.None, submitted.Conflict);

        var results = await ResultsAsync(survey.ResultsToken);
        Assert.Equal(1, results.TotalSessions);
        var metrics = Metrics(results, optional.QuestionId);
        Assert.Equal(0, metrics.Answered);
        Assert.True(metrics.Answered < results.TotalSessions);
        Assert.Equal(1, metrics.Eligible);
        Assert.True(metrics.Assigned);
        Assert.Equal(1, await CountAnswersAsync(survey.Id));
    }

    [Theory]
    [MemberData(nameof(StatusTransitions))]
    public async Task Status_transitions_matrix(SurveyStatus from, SurveyStatus to, bool emptyItems, bool withResponses)
    {
        var survey = await fixture.CreateSurveyAsync(questions: [new TestQuestion(QuestionType.Scale)]);
        if (withResponses)
        {
            Assert.True((await SubmitAsync(survey.Id, await QuestionsAsync(survey.Id))).Succeeded);
        }

        if (from != SurveyStatus.Active)
        {
            var moved = await UpdateAsync(survey.ConfigToken, status: from);
            Assert.True(moved.Succeeded);
        }

        var sessionsBefore = await CountSessionsAsync(survey.Id);
        var answersBefore = await CountAnswersAsync(survey.Id);
        var current = await GetConfigAsync(survey.ConfigToken);
        var items = emptyItems ? [] : AssignedItems(current);
        var updated = await UpdateAsync(survey.ConfigToken, status: to, items: items);
        var allowed = !emptyItems || to == SurveyStatus.Draft;

        if (allowed)
        {
            Assert.True(updated.Succeeded);
            var after = await GetConfigAsync(survey.ConfigToken);
            Assert.Equal(to, after.Status);
            Assert.Equal(emptyItems ? 0 : 1, after.AssignedQuestions.Count);
        }
        else
        {
            Assert.False(updated.Succeeded);
            if (to == SurveyStatus.Active)
            {
                Assert.Contains(ActiveNeedsQuestion, updated.Validation.Errors);
            }

            var after = await GetConfigAsync(survey.ConfigToken);
            Assert.Equal(from, after.Status);
            Assert.Single(after.AssignedQuestions);
        }

        Assert.Equal(sessionsBefore, await CountSessionsAsync(survey.Id));
        Assert.Equal(answersBefore, await CountAnswersAsync(survey.Id));
    }

    [Fact]
    public async Task Cannot_remove_last_question_from_active_survey()
    {
        var survey = await fixture.CreateSurveyAsync(questions: [new TestQuestion(QuestionType.YesNo)]);
        var before = await GetConfigAsync(survey.ConfigToken);
        var updated = await UpdateAsync(survey.ConfigToken, items: []);

        Assert.False(updated.Succeeded);
        Assert.Contains(ActiveNeedsQuestion, updated.Validation.Errors);
        var after = await GetConfigAsync(survey.ConfigToken);
        Assert.Equal(before.AssignedQuestions.Select(q => q.QuestionId), after.AssignedQuestions.Select(q => q.QuestionId));
    }

    [Fact]
    public async Task Default_may_be_draft_or_closed_but_catalog_skips_it()
    {
        await NormalizeAncientSurveysAsync();
        await ResetSeedDefaultActiveAsync();
        var snapshot = await SnapshotAsync();
        int? probeId = null;
        try
        {
            var probe = await fixture.CreateSurveyAsync();
            probeId = probe.Id;
            await SetCreatedAtAsync(probe.Id, new DateTime(1980, 3, 3, 0, 0, 0, DateTimeKind.Utc));

            Assert.True((await UpdateAsync(LocalDbFixture.ConfigToken, status: SurveyStatus.Draft, isDefault: true)).Succeeded);
            Assert.Null(await BySlugAsync("ankieta-satysfakcji"));
            Assert.Equal(probe.Slug, await DefaultSlugAsync());

            Assert.True((await UpdateAsync(LocalDbFixture.ConfigToken, status: SurveyStatus.Closed, isDefault: true)).Succeeded);
            var closed = await BySlugAsync("ANKIETA-SATYSFAKCJI");
            Assert.NotNull(closed);
            Assert.Equal(SurveyStatus.Closed, closed!.Status);
            Assert.Equal("ankieta-satysfakcji", closed.Slug);
            Assert.Equal(probe.Slug, await DefaultSlugAsync());
            Assert.Equal(1, await CountDefaultsAsync());
        }
        finally
        {
            if (probeId is int id)
            {
                await SetCreatedAtAsync(id, DateTime.UtcNow);
            }

            await RestoreSnapshotAsync(snapshot);
            await ResetSeedDefaultActiveAsync();
        }
    }

    [Fact]
    public async Task Default_tie_on_created_at_is_broken_by_id()
    {
        await NormalizeAncientSurveysAsync();
        await ResetSeedDefaultActiveAsync();
        var snapshot = await SnapshotAsync();
        var created = new List<int>();
        try
        {
            Assert.True((await UpdateAsync(LocalDbFixture.ConfigToken, status: SurveyStatus.Closed, isDefault: true)).Succeeded);
            var first = await fixture.CreateSurveyAsync();
            var second = await fixture.CreateSurveyAsync();
            created.Add(first.Id);
            created.Add(second.Id);
            var tie = new DateTime(1981, 4, 4, 0, 0, 0, DateTimeKind.Utc);
            await SetCreatedAtAsync(first.Id, tie);
            await SetCreatedAtAsync(second.Id, tie);

            var expected = first.Id < second.Id ? first.Slug : second.Slug;
            Assert.Equal(expected, await DefaultSlugAsync());
        }
        finally
        {
            foreach (var id in created)
            {
                await SetCreatedAtAsync(id, DateTime.UtcNow);
            }

            await RestoreSnapshotAsync(snapshot);
            await ResetSeedDefaultActiveAsync();
        }
    }

    [Fact]
    public async Task Exactly_one_default_after_every_operation()
    {
        try
        {
            await ResetSeedDefaultActiveAsync();
            Assert.Equal(1, await CountDefaultsAsync());

            var created = await CreateAsync($"Licznik {Id()}", SurveyStatus.Active, [NewItem($"P {Id()}", QuestionType.Open)]);
            Assert.True(created.Succeeded);
            Assert.False((await GetConfigAsync(created.Result!.ConfigAccessToken)).IsDefault);
            Assert.Equal(1, await CountDefaultsAsync());

            Assert.True((await UpdateAsync(created.Result.ConfigAccessToken, title: $"Zmiana {Id()}")).Succeeded);
            Assert.Equal(1, await CountDefaultsAsync());

            Assert.True((await UpdateAsync(created.Result.ConfigAccessToken, isDefault: true)).Succeeded);
            Assert.Equal(1, await CountDefaultsAsync());
            Assert.True((await GetConfigAsync(created.Result.ConfigAccessToken)).IsDefault);
            Assert.False((await GetConfigAsync(LocalDbFixture.ConfigToken)).IsDefault);
        }
        finally
        {
            await ResetSeedDefaultActiveAsync();
        }
    }

    [Fact]
    public async Task Create_rejects_31_items_accepts_30()
    {
        var marker = Id();
        var tooMany = Enumerable.Range(1, 31).Select(i => NewItem($"Limit {marker} {i}", QuestionType.Open)).ToList();
        var rejected = await CreateAsync($"Limit {marker}", SurveyStatus.Active, tooMany);
        Assert.False(rejected.Succeeded);
        Assert.Contains(TooManyItems, rejected.Validation.Errors);
        Assert.Equal(0, await CountQuestionsByMarkerAsync(marker));

        var acceptedItems = tooMany.Take(30).ToList();
        var accepted = await CreateAsync($"Limit ok {marker}", SurveyStatus.Active, acceptedItems);
        Assert.True(accepted.Succeeded);
        var config = await GetConfigAsync(accepted.Result!.ConfigAccessToken);
        Assert.Equal(30, config.AssignedQuestions.Count);
        Assert.Equal(Enumerable.Range(1, 30), config.AssignedQuestions.OrderBy(q => q.SortOrder).Select(q => q.SortOrder));
    }

    [Fact]
    public async Task Update_rejects_31_items()
    {
        var survey = await fixture.CreateSurveyAsync(questions: [new TestQuestion(QuestionType.Open)]);
        var marker = Id();
        var items = Enumerable.Range(1, 31).Select(i => NewItem($"Update {marker} {i}", QuestionType.Scale)).ToList();
        var before = await GetConfigAsync(survey.ConfigToken);

        var updated = await UpdateAsync(survey.ConfigToken, items: items);
        Assert.False(updated.Succeeded);
        Assert.Contains(TooManyItems, updated.Validation.Errors);

        var after = await GetConfigAsync(survey.ConfigToken);
        Assert.Equal(before.AssignedQuestions.Select(q => q.QuestionId), after.AssignedQuestions.Select(q => q.QuestionId));
        Assert.Equal(0, await CountQuestionsByMarkerAsync(marker));
    }

    [Fact]
    public async Task Items_with_duplicate_question_id_rejected()
    {
        var survey = await fixture.CreateSurveyAsync(questions: [new TestQuestion(QuestionType.Scale)]);
        var id = survey.QuestionIds[0];
        var created = await CreateAsync($"Powtorka {Id()}", SurveyStatus.Active, [ExistingItem(id), ExistingItem(id)]);
        Assert.False(created.Succeeded);

        var updated = await UpdateAsync(survey.ConfigToken, items: [ExistingItem(id), ExistingItem(id, false)]);
        Assert.False(updated.Succeeded);
        Assert.Equal([id], (await GetConfigAsync(survey.ConfigToken)).AssignedQuestions.Select(q => q.QuestionId));
    }

    [Fact]
    public async Task Items_with_unknown_question_id_rejected()
    {
        int inactiveId;
        await using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SurveyDbContext>();
            var inactive = new Question { Text = $"Nieaktywne {Id()}", Type = QuestionType.Open, IsActive = false };
            db.Questions.Add(inactive);
            await db.SaveChangesAsync();
            inactiveId = inactive.Id;
        }

        var unknown = await CreateAsync($"Nieznane {Id()}", SurveyStatus.Active, [ExistingItem(int.MaxValue)]);
        Assert.False(unknown.Succeeded);

        var inactiveResult = await CreateAsync($"Nieaktywne {Id()}", SurveyStatus.Active, [ExistingItem(inactiveId)]);
        Assert.False(inactiveResult.Succeeded);
    }

    [Fact]
    public async Task Items_with_both_or_neither_id_and_new_question_rejected()
    {
        var poolId = (await PoolAsync())[0].QuestionId;
        var both = new SurveyItemInputDto(poolId, new NewQuestionInputDto($"Oba {Id()}", QuestionType.Open), true);
        var neither = new SurveyItemInputDto(null, null, true);

        Assert.False((await CreateAsync($"Oba {Id()}", SurveyStatus.Active, [both])).Succeeded);
        Assert.False((await CreateAsync($"Zadne {Id()}", SurveyStatus.Draft, [neither])).Succeeded);

        var survey = await fixture.CreateSurveyAsync(questions: [new TestQuestion(QuestionType.Scale)]);
        var before = await GetConfigAsync(survey.ConfigToken);
        Assert.False((await UpdateAsync(survey.ConfigToken, items: [both])).Succeeded);
        Assert.Equal(before.AssignedQuestions.Select(q => q.QuestionId), (await GetConfigAsync(survey.ConfigToken)).AssignedQuestions.Select(q => q.QuestionId));
    }

    [Fact]
    public async Task Items_with_two_identical_new_questions_rejected()
    {
        var text = $"  Takie samo {Id()}  ";
        var items = new List<SurveyItemInputDto>
        {
            NewItem(text, QuestionType.Open),
            NewItem(text.ToUpperInvariant(), QuestionType.Open)
        };
        var before = await CountsAsync();
        var created = await CreateAsync($"Dwa {Id()}", SurveyStatus.Active, items);
        Assert.False(created.Succeeded);
        Assert.Contains(created.Validation.Errors, e => e.Contains(PoolDuplicateFragment, StringComparison.Ordinal));
        Assert.Equal(before, await CountsAsync());
    }

    [Fact]
    public async Task New_question_duplicate_of_pool_case_and_whitespace_insensitive_rejected()
    {
        var text = "  JAK OCENIASZ OGÓLNE WRAŻENIA Z KORZYSTANIA Z NASZEJ USŁUGI?  ";
        var rejected = await CreateAsync($"Wielkosc {Id()}", SurveyStatus.Active, [NewItem(text, QuestionType.Scale)]);
        Assert.False(rejected.Succeeded);
        Assert.Contains(PoolMessage(text.Trim()), rejected.Validation.Errors);

        var otherType = await CreateAsync($"Inny typ {Id()}", SurveyStatus.Active, [NewItem(text, QuestionType.Open)]);
        Assert.True(otherType.Succeeded);
        Assert.Equal(text.Trim(), (await GetConfigAsync(otherType.Result!.ConfigAccessToken)).AssignedQuestions.Single().Text);
    }

    [Fact]
    public async Task Items_empty_allowed_for_draft_only()
    {
        var draft = await CreateAsync($"Pusty szkic {Id()}", SurveyStatus.Draft);
        Assert.True(draft.Succeeded);
        Assert.Empty((await GetConfigAsync(draft.Result!.ConfigAccessToken)).AssignedQuestions);

        var active = await CreateAsync($"Pusta aktywna {Id()}", SurveyStatus.Active);
        Assert.False(active.Succeeded);
        Assert.Contains(ActiveNeedsQuestion, active.Validation.Errors);

        var closed = await CreateAsync($"Pusta zamknieta {Id()}", SurveyStatus.Closed);
        Assert.False(closed.Succeeded);

        var survey = await fixture.CreateSurveyAsync();
        Assert.True((await UpdateAsync(survey.ConfigToken, status: SurveyStatus.Draft, items: [])).Succeeded);
        Assert.Empty((await GetConfigAsync(survey.ConfigToken)).AssignedQuestions);
        Assert.False((await UpdateAsync(survey.ConfigToken, status: SurveyStatus.Closed, items: [])).Succeeded);
        Assert.Equal(SurveyStatus.Draft, (await GetConfigAsync(survey.ConfigToken)).Status);
    }

    [Fact]
    public async Task Text_length_1_500_ok_0_501_rejected()
    {
        var one = new string('ą', 1);
        var max = new string('ą', 500);
        var over = new string('ą', 501);

        Assert.False((await CreateAsync($"Puste pytanie {Id()}", SurveyStatus.Active, [NewItem("   ", QuestionType.Open)])).Succeeded);
        Assert.False((await CreateAsync($"Za dlugie {Id()}", SurveyStatus.Active, [NewItem(over, QuestionType.Open)])).Succeeded);

        var shortOk = await CreateAsync($"Krotkie {Id()}", SurveyStatus.Active, [NewItem(one, QuestionType.Open)]);
        var longOk = await CreateAsync($"Dlugie {Id()}", SurveyStatus.Active, [NewItem(max, QuestionType.Scale)]);
        Assert.True(shortOk.Succeeded);
        Assert.True(longOk.Succeeded);
        Assert.Equal(one, (await GetConfigAsync(shortOk.Result!.ConfigAccessToken)).AssignedQuestions.Single().Text);
        Assert.Equal(max, (await GetConfigAsync(longOk.Result!.ConfigAccessToken)).AssignedQuestions.Single().Text);
    }

    [Fact]
    public async Task Title_and_description_boundaries()
    {
        Assert.False((await CreateAsync("   ", SurveyStatus.Draft)).Succeeded);
        Assert.False((await CreateAsync(new string('a', 201), SurveyStatus.Draft)).Succeeded);
        Assert.False((await CreateAsync($"Opis {Id()}", SurveyStatus.Draft, description: new string('b', 1001))).Succeeded);

        var minimal = await CreateAsync("ą", SurveyStatus.Draft, description: "   ");
        Assert.True(minimal.Succeeded);
        var minimalConfig = await GetConfigAsync(minimal.Result!.ConfigAccessToken);
        Assert.Equal("ą", minimalConfig.Title);
        Assert.Null(minimalConfig.Description);

        var maximal = await CreateAsync(new string('c', 200), SurveyStatus.Draft, description: new string('d', 1000));
        Assert.True(maximal.Succeeded);
        var maximalConfig = await GetConfigAsync(maximal.Result!.ConfigAccessToken);
        Assert.Equal(200, maximalConfig.Title.Length);
        Assert.Equal(1000, maximalConfig.Description!.Length);

        var survey = await fixture.CreateSurveyAsync();
        Assert.False((await UpdateAsync(survey.ConfigToken, title: "  ")).Succeeded);
        Assert.Equal(survey.Title, (await GetConfigAsync(survey.ConfigToken)).Title);
    }

    [Fact]
    public async Task Unicode_polish_and_emoji_stored_verbatim()
    {
        var title = $"Zażółć gęślą jaźń {Id()} 😀";
        var description = "Opis — ąęćłńóśźż oraz emoji 🎉";
        var question = "Pytanie „czy działa?” 👍";
        var created = await CreateAsync(title, SurveyStatus.Active, [NewItem(question, QuestionType.Open)], description: description);

        Assert.True(created.Succeeded);
        var config = await GetConfigAsync(created.Result!.ConfigAccessToken);
        Assert.Equal(title, config.Title);
        Assert.Equal(description, config.Description);
        Assert.Equal(question, config.AssignedQuestions.Single().Text);

        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<SurveyDbContext>();
        var stored = await db.Questions.SingleAsync(q => q.Text == question);
        Assert.Equal(question, stored.Text);
    }

    [Fact]
    public async Task Special_characters_stored_verbatim_not_executed()
    {
        var title = $"<script>alert(1)</script> {Id()}";
        var description = "{{7*7}}";
        var question = "'; DROP TABLE dbo.Survey; --";
        var created = await CreateAsync(title, SurveyStatus.Draft, [NewItem(question, QuestionType.Open)], slug: $"spec-{Id()}", description: description);

        Assert.True(created.Succeeded);
        var config = await GetConfigAsync(created.Result!.ConfigAccessToken);
        Assert.Equal(title, config.Title);
        Assert.Equal("{{7*7}}", config.Description);
        Assert.Equal(question, config.AssignedQuestions.Single().Text);
        Assert.NotEqual("49", config.Description);

        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<SurveyDbContext>();
        Assert.True(await db.Surveys.AnyAsync());
        Assert.Equal(question, await db.Questions.Where(q => q.Id == config.AssignedQuestions[0].QuestionId).Select(q => q.Text).SingleAsync());
    }

    [Fact]
    public async Task Slug_collision_with_60_char_base_trims_to_fit_suffix()
    {
        var title = Guid.NewGuid().ToString("N") + new string('q', 28);
        Assert.Equal(60, SlugGenerator.FromTitle(title).Length);

        var first = await CreateAsync(title, SurveyStatus.Draft);
        var second = await CreateAsync(title, SurveyStatus.Draft);
        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal(title, first.Result!.Slug);
        Assert.True(second.Result!.Slug.Length <= 60);
        Assert.EndsWith("-2", second.Result.Slug);
        Assert.True(SlugGenerator.IsValid(second.Result.Slug));
        Assert.Equal(title[..58] + "-2", second.Result.Slug);
    }

    [Fact]
    public async Task Slug_lookup_is_case_insensitive_and_returns_canonical()
    {
        var slug = $"kanon-{Id()}";
        var created = await CreateAsync($"Kanon {Id()}", SurveyStatus.Active, [NewItem($"K {Id()}", QuestionType.YesNo)], slug: slug);
        Assert.True(created.Succeeded);

        var found = await BySlugAsync(slug.ToUpperInvariant());
        Assert.NotNull(found);
        Assert.Equal(slug, found!.Slug);
        Assert.Equal(created.Result!.SurveyId, found.SurveyId);
        Assert.Null(await BySlugAsync($"brak-{Id()}"));
    }

    [Fact]
    public async Task Slug_emoji_only_title_falls_back_to_ankieta_with_suffix()
    {
        var first = await CreateAsync("🎉🎉", SurveyStatus.Draft);
        var second = await CreateAsync("😊", SurveyStatus.Draft);
        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.StartsWith("ankieta", first.Result!.Slug);
        Assert.StartsWith("ankieta", second.Result!.Slug);
        Assert.NotEqual(first.Result.Slug, second.Result.Slug);
        Assert.Contains('-', second.Result.Slug);
        Assert.True(SlugGenerator.IsValid(first.Result.Slug));
        Assert.True(SlugGenerator.IsValid(second.Result.Slug));
        Assert.DoesNotContain("🎉", first.Result.Slug);
    }

    [Fact]
    public async Task Readding_removed_question_reattaches_history_and_new_required_flag()
    {
        var survey = await fixture.CreateSurveyAsync(questions:
        [
            new TestQuestion(QuestionType.Scale),
            new TestQuestion(QuestionType.Open)
        ]);
        Assert.True((await SubmitAsync(survey.Id, await QuestionsAsync(survey.Id))).Succeeded);
        var current = await GetConfigAsync(survey.ConfigToken);
        var removed = current.AssignedQuestions.OrderBy(q => q.SortOrder).Last();
        var kept = AssignedItems(current).Where(i => i.QuestionId != removed.QuestionId).ToList();

        Assert.True((await UpdateAsync(survey.ConfigToken, items: kept)).Succeeded);
        Assert.True((await UpdateAsync(survey.ConfigToken, items: kept.Append(ExistingItem(removed.QuestionId, false)).ToList())).Succeeded);

        var config = await GetConfigAsync(survey.ConfigToken);
        var readded = config.AssignedQuestions.Single(q => q.QuestionId == removed.QuestionId);
        Assert.False(readded.IsRequired);
        Assert.Equal(1, readded.AnswerCount);
        Assert.Equal(1, await CountSessionsAsync(survey.Id));
        Assert.Equal(2, await CountAnswersAsync(survey.Id));

        var metrics = Metrics(await ResultsAsync(survey.ResultsToken), removed.QuestionId);
        Assert.True(metrics.Assigned);
        Assert.Equal(1, metrics.Answered);
    }

    [Fact]
    public async Task Reorder_persists_and_sort_order_has_no_duplicates()
    {
        var survey = await fixture.CreateSurveyAsync();
        var current = await GetConfigAsync(survey.ConfigToken);
        var reversed = AssignedItems(current);
        reversed.Reverse();

        Assert.True((await UpdateAsync(survey.ConfigToken, items: reversed)).Succeeded);
        var questions = await QuestionsAsync(survey.Id);
        Assert.Equal(reversed.Select(i => i.QuestionId!.Value), questions.Select(q => q.QuestionId));
        Assert.Equal(Enumerable.Range(1, questions.Count), questions.Select(q => q.SortOrder));
        Assert.Equal(questions.Count, questions.Select(q => q.SortOrder).Distinct().Count());
    }

    [Fact]
    public async Task Required_flag_change_with_existing_responses_keeps_results()
    {
        var survey = await fixture.CreateSurveyAsync(questions: [new TestQuestion(QuestionType.Scale)]);
        Assert.True((await SubmitAsync(survey.Id, await QuestionsAsync(survey.Id))).Succeeded);
        var questionId = survey.QuestionIds[0];
        var answersBefore = await CountAnswersAsync(survey.Id);

        var items = AssignedItems(await GetConfigAsync(survey.ConfigToken));
        items[0] = items[0] with { IsRequired = false };
        Assert.True((await UpdateAsync(survey.ConfigToken, items: items)).Succeeded);

        Assert.Equal(answersBefore, await CountAnswersAsync(survey.Id));
        var metrics = Metrics(await ResultsAsync(survey.ResultsToken), questionId);
        Assert.Equal(1, metrics.Answered);
        Assert.True(metrics.Assigned);
        Assert.False((await QuestionsAsync(survey.Id)).Single().IsRequired);
    }

    [Fact]
    public async Task Added_at_is_kept_for_existing_assignment_and_new_for_new()
    {
        var survey = await fixture.CreateSurveyAsync(questions: [new TestQuestion(QuestionType.Scale)]);
        var past = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SurveyDbContext>();
            await db.SurveyQuestions.Where(sq => sq.SurveyId == survey.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.AddedAtUtc, past));
        }

        var text = $"Nowe przypisanie {Id()}";
        var items = AssignedItems(await GetConfigAsync(survey.ConfigToken));
        items[0] = items[0] with { IsRequired = false };
        items.Add(NewItem(text, QuestionType.Open, false));
        Assert.True((await UpdateAsync(survey.ConfigToken, items: items)).Succeeded);

        await using var read = NewScope();
        var rows = await read.ServiceProvider.GetRequiredService<SurveyDbContext>().SurveyQuestions
            .Where(sq => sq.SurveyId == survey.Id)
            .Select(sq => new { sq.QuestionId, sq.AddedAtUtc, sq.Question.Text })
            .ToListAsync();
        Assert.Equal(past, rows.Single(r => r.QuestionId == survey.QuestionIds[0]).AddedAtUtc);
        Assert.True(rows.Single(r => r.Text == text).AddedAtUtc > past);
    }

    [Fact]
    public async Task Eligible_count_excludes_sessions_before_question_was_added()
    {
        var survey = await fixture.CreateSurveyAsync(questions: [new TestQuestion(QuestionType.Scale)]);
        var first = await SubmitAsync(survey.Id, await QuestionsAsync(survey.Id));
        Assert.True(first.Succeeded);
        await SetSubmittedAsync(first.SessionId!.Value, new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var text = $"Pozniejsze {Id()}";
        var items = AssignedItems(await GetConfigAsync(survey.ConfigToken));
        items.Add(NewItem(text, QuestionType.Open, false));
        Assert.True((await UpdateAsync(survey.ConfigToken, items: items)).Succeeded);

        var questions = await QuestionsAsync(survey.Id);
        var second = await SubmitAsync(survey.Id, questions.Where(q => q.IsRequired));
        Assert.True(second.Succeeded);

        var added = (await GetConfigAsync(survey.ConfigToken)).AssignedQuestions.Single(q => q.Text == text);
        var metrics = Metrics(await ResultsAsync(survey.ResultsToken), added.QuestionId);
        Assert.Equal(0, metrics.Answered);
        Assert.Equal(1, metrics.Eligible);
        Assert.Equal(2, (await ResultsAsync(survey.ResultsToken)).TotalSessions);
    }

    [Fact]
    public async Task Eligible_count_includes_sessions_that_answered_after_removal_and_readd()
    {
        var survey = await fixture.CreateSurveyAsync(questions: [new TestQuestion(QuestionType.Scale)]);
        var early = await SubmitAsync(survey.Id, await QuestionsAsync(survey.Id));
        Assert.True(early.Succeeded);
        await SetSubmittedAsync(early.SessionId!.Value, new DateTime(2018, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var text = $"Historia {Id()}";
        var withOpen = AssignedItems(await GetConfigAsync(survey.ConfigToken));
        withOpen.Add(NewItem(text, QuestionType.Open, false));
        Assert.True((await UpdateAsync(survey.ConfigToken, items: withOpen)).Succeeded);

        var questions = await QuestionsAsync(survey.Id);
        var answered = await SubmitAsync(survey.Id, questions);
        Assert.True(answered.Succeeded);
        await SetSubmittedAsync(answered.SessionId!.Value, new DateTime(2018, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        var openId = questions.Single(q => q.Text == text).QuestionId;
        var scaleOnly = AssignedItems(await GetConfigAsync(survey.ConfigToken)).Where(i => i.QuestionId != openId).ToList();
        Assert.True((await UpdateAsync(survey.ConfigToken, items: scaleOnly)).Succeeded);
        Assert.True((await UpdateAsync(survey.ConfigToken, items: scaleOnly.Append(ExistingItem(openId, false)).ToList())).Succeeded);

        var afterReadd = await QuestionsAsync(survey.Id);
        var latest = await SubmitAsync(survey.Id, afterReadd.Where(q => q.IsRequired));
        Assert.True(latest.Succeeded);

        var metrics = Metrics(await ResultsAsync(survey.ResultsToken), openId);
        Assert.Equal(1, metrics.Answered);
        Assert.Equal(2, metrics.Eligible);
        Assert.Equal(3, (await ResultsAsync(survey.ResultsToken)).TotalSessions);
        Assert.True(metrics.Assigned);
    }

    [Fact]
    public async Task Scale_average_uses_only_answered()
    {
        var survey = await fixture.CreateSurveyAsync(questions:
        [
            new TestQuestion(QuestionType.Scale, false),
            new TestQuestion(QuestionType.YesNo)
        ]);
        var questions = await QuestionsAsync(survey.Id);
        var scale = questions.Single(q => q.Type == QuestionType.Scale);
        Assert.True((await SubmitAsync(survey.Id, questions)).Succeeded);
        Assert.True((await SubmitAsync(survey.Id, questions.Where(q => q.QuestionId != scale.QuestionId))).Succeeded);

        var results = await ResultsAsync(survey.ResultsToken);
        var aggregate = results.ScaleAggregates.Single(q => q.QuestionId == scale.QuestionId);
        Assert.Equal(4d, aggregate.Average);
        Assert.Equal(1, aggregate.AnsweredCount);
        Assert.Equal(2, aggregate.EligibleCount);
        Assert.Equal(1, aggregate.Distribution[4]);
        Assert.Equal(0, aggregate.Distribution[1]);
    }

    [Fact]
    public async Task Results_for_survey_without_responses_do_not_divide_by_zero()
    {
        var survey = await fixture.CreateSurveyAsync();
        var results = await ResultsAsync(survey.ResultsToken);
        Assert.Equal(0, results.TotalSessions);
        Assert.Empty(results.RawResponses);
        Assert.All(results.ScaleAggregates, aggregate =>
        {
            Assert.Equal(0d, aggregate.Average);
            Assert.Equal(0, aggregate.AnsweredCount);
            Assert.Equal(0, aggregate.EligibleCount);
            Assert.True(aggregate.IsCurrentlyAssigned);
        });
        Assert.All(results.YesNoAggregates, aggregate => Assert.Equal(0, aggregate.EligibleCount));
        Assert.All(results.OpenAggregates, aggregate => Assert.Equal(0, aggregate.EligibleCount));
    }

    [Fact]
    public async Task Removed_question_without_answers_is_not_in_results()
    {
        var survey = await fixture.CreateSurveyAsync(questions: [new TestQuestion(QuestionType.Scale)]);
        var text = $"Bez odpowiedzi {Id()}";
        var items = AssignedItems(await GetConfigAsync(survey.ConfigToken));
        items.Add(NewItem(text, QuestionType.Open, false));
        Assert.True((await UpdateAsync(survey.ConfigToken, items: items)).Succeeded);
        var addedId = (await GetConfigAsync(survey.ConfigToken)).AssignedQuestions.Single(q => q.Text == text).QuestionId;

        var kept = AssignedItems(await GetConfigAsync(survey.ConfigToken)).Where(i => i.QuestionId != addedId).ToList();
        Assert.True((await UpdateAsync(survey.ConfigToken, items: kept)).Succeeded);

        var results = await ResultsAsync(survey.ResultsToken);
        Assert.DoesNotContain(results.OpenAggregates, q => q.QuestionId == addedId);
        Assert.DoesNotContain(results.ScaleAggregates, q => q.QuestionId == addedId);
        Assert.DoesNotContain(results.YesNoAggregates, q => q.QuestionId == addedId);
        Assert.Contains(results.ScaleAggregates, q => q.QuestionId == survey.QuestionIds[0]);
    }

    [Fact]
    public async Task Results_token_of_a_does_not_open_b()
    {
        var a = await fixture.CreateSurveyAsync();
        var b = await fixture.CreateSurveyAsync();
        Assert.Equal(a.Id, (await ResultsAsync(a.ResultsToken)).SurveyId);
        Assert.Equal(b.Id, (await ResultsAsync(b.ResultsToken)).SurveyId);
        Assert.NotEqual(a.Id, (await ResultsAsync(b.ResultsToken)).SurveyId);
    }

    [Fact]
    public async Task Config_token_of_a_cannot_update_b()
    {
        var a = await fixture.CreateSurveyAsync();
        var b = await fixture.CreateSurveyAsync();
        var configB = await GetConfigAsync(b.ConfigToken);
        var updated = await UpdateAsync(a.ConfigToken, title: $"Cudzy {Id()}", items: AssignedItems(configB), rowVersion: configB.RowVersion, surveyId: b.Id);

        Assert.False(updated.Succeeded);
        Assert.Contains(InvalidToken, updated.Validation.Errors);
        Assert.Equal(b.Title, (await GetConfigAsync(b.ConfigToken)).Title);
        Assert.Equal(a.Title, (await GetConfigAsync(a.ConfigToken)).Title);
    }

    [Fact]
    public async Task Results_token_is_not_a_config_token_and_vice_versa()
    {
        var survey = await fixture.CreateSurveyAsync();
        Assert.Null(await ConfigOrNullAsync(survey.ResultsToken));
        Assert.Null(await ResultsOrNullAsync(survey.ConfigToken));
    }

    [Fact]
    public async Task Empty_whitespace_wrong_length_uppercase_tokens_rejected()
    {
        var survey = await fixture.CreateSurveyAsync();
        string[] tokens =
        [
            "",
            " ",
            "\t\n",
            "wyniki-",
            "konfiguracja-",
            survey.ResultsToken.ToUpperInvariant(),
            survey.ConfigToken.ToUpperInvariant(),
            new string('a', 64),
            survey.ResultsToken + "a",
            survey.ConfigToken[..^1]
        ];

        foreach (var token in tokens)
        {
            Assert.Null(await ResultsOrNullAsync(token));
            Assert.Null(await ConfigOrNullAsync(token));
        }
    }

    [Fact]
    public async Task Tokens_work_for_draft_and_closed()
    {
        var draft = await fixture.CreateSurveyAsync(SurveyStatus.Draft);
        var closed = await fixture.CreateSurveyAsync(SurveyStatus.Closed);

        Assert.Equal(draft.Id, (await GetConfigAsync(draft.ConfigToken)).SurveyId);
        Assert.Equal(draft.Id, (await ResultsAsync(draft.ResultsToken)).SurveyId);
        Assert.Equal(SurveyStatus.Draft, (await GetConfigAsync(draft.ConfigToken)).Status);

        Assert.Equal(closed.Id, (await GetConfigAsync(closed.ConfigToken)).SurveyId);
        Assert.Equal(closed.Id, (await ResultsAsync(closed.ResultsToken)).SurveyId);
        Assert.Equal(0, (await ResultsAsync(closed.ResultsToken)).TotalSessions);
    }

    [Fact]
    public async Task Token_comparison_is_ordinal_case_sensitive()
    {
        var survey = await fixture.CreateSurveyAsync();
        Assert.Null(await ResultsOrNullAsync(survey.ResultsToken.ToUpperInvariant()));
        Assert.Null(await ConfigOrNullAsync(survey.ConfigToken.ToUpperInvariant()));
        Assert.Equal(survey.Id, (await ResultsAsync(survey.ResultsToken)).SurveyId);
        Assert.Equal(survey.Id, (await GetConfigAsync(survey.ConfigToken)).SurveyId);
    }

    [Fact]
    public async Task Parallel_create_same_title_both_succeed_with_distinct_slugs()
    {
        var title = $"Rownolegle {Id()}";
        var results = await Task.WhenAll(CreateInNewScopeAsync(title), CreateInNewScopeAsync(title));

        Assert.All(results, result => Assert.True(result.Succeeded));
        Assert.Equal(2, results.Select(r => r.Result!.Slug).Distinct().Count());
        Assert.All(results, result => Assert.True(SlugGenerator.IsValid(result.Result!.Slug)));
        Assert.Equal(2, await CountSurveysByTitleAsync(title));
    }

    [Fact]
    public async Task Parallel_create_same_new_question_yields_single_pool_row_and_clean_errors()
    {
        var text = $"Wspolne pytanie {Id()}";
        var titleA = $"Rownolegle A {Id()}";
        var titleB = $"Rownolegle B {Id()}";
        var results = await Task.WhenAll(
            CreateInNewScopeAsync(titleA, [NewItem(text, QuestionType.Open)]),
            CreateInNewScopeAsync(titleB, [NewItem(text, QuestionType.Open)]));

        Assert.Single(results, result => result.Succeeded);
        var failed = Assert.Single(results, result => !result.Succeeded);
        Assert.Contains(failed.Validation.Errors, e => e.Contains(PoolDuplicateFragment, StringComparison.Ordinal));
        Assert.Equal(1, await CountQuestionsByMarkerAsync(text));
        Assert.Equal(1, await CountSurveysByTitleAsync(titleA) + await CountSurveysByTitleAsync(titleB));
    }

    [Fact]
    public async Task Parallel_set_default_yields_single_default()
    {
        try
        {
            await ResetSeedDefaultActiveAsync();
            var first = await fixture.CreateSurveyAsync();
            var second = await fixture.CreateSurveyAsync();
            var outcomes = await Task.WhenAll(
                UpdateInNewScopeAsync(first.ConfigToken, isDefault: true),
                UpdateInNewScopeAsync(second.ConfigToken, isDefault: true));

            Assert.All(outcomes, outcome => Assert.NotNull(outcome));
            await using var scope = NewScope();
            var defaults = await scope.ServiceProvider.GetRequiredService<SurveyDbContext>().Surveys
                .Where(s => s.IsDefault)
                .Select(s => s.Id)
                .ToListAsync();
            var winner = Assert.Single(defaults);
            Assert.True(winner == first.Id || winner == second.Id);
        }
        finally
        {
            await ResetSeedDefaultActiveAsync();
        }
    }

    [Fact]
    public async Task Concurrent_config_updates_second_gets_conflict_and_changes_not_applied()
    {
        var survey = await fixture.CreateSurveyAsync();
        var current = await GetConfigAsync(survey.ConfigToken);
        var stale = current.RowVersion.ToArray();
        var reversed = AssignedItems(current);
        reversed.Reverse();

        var first = await UpdateAsync(survey.ConfigToken, items: reversed, rowVersion: stale);
        Assert.True(first.Succeeded);

        var intruder = $"Intruz {Id()}";
        var second = await UpdateAsync(survey.ConfigToken, title: intruder, items: AssignedItems(current), rowVersion: stale);
        Assert.True(second.IsConcurrencyConflict);
        Assert.False(second.Succeeded);
        Assert.Contains(ConcurrencyMessage, second.Validation.Errors);

        var after = await GetConfigAsync(survey.ConfigToken);
        Assert.Equal(survey.Title, after.Title);
        Assert.NotEqual(intruder, after.Title);
        Assert.Equal(reversed.Select(i => i.QuestionId!.Value), after.AssignedQuestions.OrderBy(q => q.SortOrder).Select(q => q.QuestionId));
        Assert.NotEqual(stale, after.RowVersion);
    }

    [Fact]
    public async Task Submit_during_close_or_question_removal_or_required_change_yields_conflict()
    {
        var closed = await fixture.CreateSurveyAsync(questions: [new TestQuestion(QuestionType.Scale)]);
        var closedQuestions = await QuestionsAsync(closed.Id);
        var closedSubmit = await RunAfterConfigAsync(
            _ => UpdateAsync(closed.ConfigToken, status: SurveyStatus.Closed),
            async scope => await scope.ServiceProvider.GetRequiredService<ISurveySubmissionService>()
                .SubmitAsync(new SubmitSurveyResponseDto(closed.Id, closedQuestions.Select(ValidAnswer).ToList())));
        Assert.Equal(SubmissionConflict.SurveyClosed, closedSubmit.Conflict);
        Assert.Equal(0, await CountSessionsAsync(closed.Id));

        var removed = await fixture.CreateSurveyAsync(questions:
        [
            new TestQuestion(QuestionType.Scale),
            new TestQuestion(QuestionType.Open)
        ]);
        var removedQuestions = await QuestionsAsync(removed.Id);
        var keepScale = AssignedItems(await GetConfigAsync(removed.ConfigToken))
            .Where(i => removedQuestions.Single(q => q.QuestionId == i.QuestionId).Type == QuestionType.Scale)
            .ToList();
        // Saver, nie serwis walidacji: po zmianie formularza SubmitAsync czyta TVF od nowa i odrzuca
        // nieaktualny payload jako błąd walidacji, zanim dojdzie do SP. Kody 50003/50004 powstają,
        // gdy zapis (stary zestaw odpowiedzi) trafi do procedury — to mapuje saver na FormChanged.
        var removedConflict = await RunAfterConfigAsync(
            _ => UpdateAsync(removed.ConfigToken, items: keepScale),
            scope => SaveConflictAsync(scope, removed.Id, removedQuestions));
        Assert.Equal(SubmissionConflict.FormChanged, removedConflict);
        Assert.Equal(0, await CountSessionsAsync(removed.Id));

        var required = await fixture.CreateSurveyAsync(questions:
        [
            new TestQuestion(QuestionType.Scale),
            new TestQuestion(QuestionType.Open, false)
        ]);
        var requiredQuestions = await QuestionsAsync(required.Id);
        var makeRequired = AssignedItems(await GetConfigAsync(required.ConfigToken), required: true);
        var staleAnswers = requiredQuestions.Where(q => q.IsRequired).ToList();
        var requiredConflict = await RunAfterConfigAsync(
            _ => UpdateAsync(required.ConfigToken, items: makeRequired),
            scope => SaveConflictAsync(scope, required.Id, staleAnswers));
        Assert.Equal(SubmissionConflict.FormChanged, requiredConflict);
        Assert.Equal(0, await CountSessionsAsync(required.Id));
    }

    private static async Task<SubmissionConflict> SaveConflictAsync(
        AsyncServiceScope scope,
        int surveyId,
        IEnumerable<SurveyQuestionDto> questions)
    {
        try
        {
            await scope.ServiceProvider.GetRequiredService<ISurveyResponseSaver>()
                .SaveAsync(new SubmitSurveyResponseDto(surveyId, questions.Select(ValidAnswer).ToList()));
            return SubmissionConflict.None;
        }
        catch (SurveyChangedException ex)
        {
            return ex.Conflict;
        }
    }

    [Fact]
    public async Task Create_failure_midway_leaves_no_rows()
    {
        var before = await CountsAsync();
        var title = $"Polowa {Id()}";
        var text = $"Zostanie cofniete {Id()}";
        var created = await CreateAsync(title, SurveyStatus.Active,
        [
            NewItem(text, QuestionType.Open),
            NewItem("   ", QuestionType.Scale)
        ]);

        Assert.False(created.Succeeded);
        Assert.Null(created.Result);
        Assert.Equal(before, await CountsAsync());
        Assert.Equal(0, await CountSurveysByTitleAsync(title));
        Assert.Equal(0, await CountQuestionsByMarkerAsync(text));
    }

    [Fact]
    public async Task Update_failure_midway_leaves_no_changes()
    {
        var survey = await fixture.CreateSurveyAsync();
        Assert.True((await SubmitAsync(survey.Id, await QuestionsAsync(survey.Id))).Succeeded);
        var before = await CountsAsync();
        var sessions = await CountSessionsAsync(survey.Id);
        var answers = await CountAnswersAsync(survey.Id);
        var current = await GetConfigAsync(survey.ConfigToken);
        var items = AssignedItems(current);
        items[0] = items[0] with { IsRequired = false };
        items.Add(NewItem("   ", QuestionType.Open));
        var title = $"Nie zapisuj {Id()}";

        var updated = await UpdateAsync(survey.ConfigToken, title: title, items: items);
        Assert.False(updated.Succeeded);

        var after = await GetConfigAsync(survey.ConfigToken);
        Assert.Equal(survey.Title, after.Title);
        Assert.Equal(current.AssignedQuestions.Select(q => (q.QuestionId, q.IsRequired, q.SortOrder)), after.AssignedQuestions.Select(q => (q.QuestionId, q.IsRequired, q.SortOrder)));
        Assert.Equal(before, await CountsAsync());
        Assert.Equal(sessions, await CountSessionsAsync(survey.Id));
        Assert.Equal(answers, await CountAnswersAsync(survey.Id));
    }

    [Fact]
    public async Task Saver_propagates_50005_without_wrapping()
    {
        var survey = await fixture.CreateSurveyAsync(questions: [new TestQuestion(QuestionType.Scale, false)]);
        var before = await CountSessionsAsync(survey.Id);
        await using var scope = NewScope();

        var error = await Assert.ThrowsAsync<SqlException>(() =>
            scope.ServiceProvider.GetRequiredService<ISurveyResponseSaver>()
                .SaveAsync(new SubmitSurveyResponseDto(survey.Id, [])));

        Assert.Equal(50005, error.Number);
        Assert.Equal(before, await CountSessionsAsync(survey.Id));
    }

    private async Task<CreateSurveyOutcome> CreateAsync(
        string title,
        SurveyStatus status,
        IReadOnlyList<SurveyItemInputDto>? items = null,
        string? slug = null,
        string? description = null)
    {
        await using var scope = NewScope();
        return await scope.ServiceProvider.GetRequiredService<ISurveyAuthoringService>()
            .CreateAsync(new CreateSurveyDto(title, description, slug, status, items ?? []));
    }

    private async Task<CreateSurveyOutcome> CreateInNewScopeAsync(string title, IReadOnlyList<SurveyItemInputDto>? items = null)
    {
        await using var scope = NewScope();
        var status = items is { Count: > 0 } ? SurveyStatus.Active : SurveyStatus.Draft;
        return await scope.ServiceProvider.GetRequiredService<ISurveyAuthoringService>()
            .CreateAsync(new CreateSurveyDto(title, null, null, status, items ?? []));
    }

    private async Task<UpdateSurveyConfigOutcome> UpdateAsync(
        string token,
        string? title = null,
        string? description = null,
        bool descriptionSet = false,
        SurveyStatus? status = null,
        bool? isDefault = null,
        IReadOnlyList<SurveyItemInputDto>? items = null,
        byte[]? rowVersion = null,
        int? surveyId = null)
    {
        await using var scope = NewScope();
        var service = scope.ServiceProvider.GetRequiredService<ISurveyConfigService>();
        var current = await service.GetByConfigTokenAsync(token);
        Assert.NotNull(current);
        var dto = new UpdateSurveyConfigDto(
            surveyId ?? current!.SurveyId,
            title ?? current.Title,
            descriptionSet ? description : current.Description,
            status ?? current.Status,
            isDefault ?? current.IsDefault,
            rowVersion ?? current.RowVersion,
            items ?? AssignedItems(current));
        return await service.UpdateAsync(token, dto);
    }

    private async Task<UpdateSurveyConfigOutcome> UpdateInNewScopeAsync(string token, bool isDefault)
    {
        await using var scope = NewScope();
        var service = scope.ServiceProvider.GetRequiredService<ISurveyConfigService>();
        var current = (await service.GetByConfigTokenAsync(token))!;
        return await service.UpdateAsync(token, new UpdateSurveyConfigDto(
            current.SurveyId,
            current.Title,
            current.Description,
            current.Status,
            isDefault,
            current.RowVersion,
            AssignedItems(current)));
    }

    private async Task<T> RunAfterConfigAsync<T>(
        Func<AsyncServiceScope, Task> configChange,
        Func<AsyncServiceScope, Task<T>> submit)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var configDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var configTask = Task.Run(async () =>
        {
            await using var scope = NewScope();
            await started.Task;
            try
            {
                await configChange(scope);
                configDone.TrySetResult();
            }
            catch (Exception ex)
            {
                configDone.TrySetException(ex);
                throw;
            }
        });

        var submitTask = Task.Run(async () =>
        {
            await using var scope = NewScope();
            started.TrySetResult();
            await configDone.Task;
            return await submit(scope);
        });

        await Task.WhenAll(configTask, submitTask);
        return await submitTask;
    }

    private async Task<SurveyConfigDto> GetConfigAsync(string token)
    {
        var config = await ConfigOrNullAsync(token);
        Assert.NotNull(config);
        return config!;
    }

    private async Task<SurveyConfigDto?> ConfigOrNullAsync(string token)
    {
        await using var scope = NewScope();
        return await scope.ServiceProvider.GetRequiredService<ISurveyConfigService>().GetByConfigTokenAsync(token);
    }

    private async Task<SurveyResultsDto> ResultsAsync(string token)
    {
        var results = await ResultsOrNullAsync(token);
        Assert.NotNull(results);
        return results!;
    }

    private async Task<SurveyResultsDto?> ResultsOrNullAsync(string token)
    {
        await using var scope = NewScope();
        return await scope.ServiceProvider.GetRequiredService<ISurveyResultsQuery>().GetByResultsTokenAsync(token);
    }

    private async Task<IReadOnlyList<SurveyQuestionDto>> QuestionsAsync(int surveyId)
    {
        await using var scope = NewScope();
        return await scope.ServiceProvider.GetRequiredService<ISurveyQuestionQuery>().GetQuestionsAsync(surveyId);
    }

    private async Task<IReadOnlyList<QuestionPoolItemDto>> PoolAsync()
    {
        await using var scope = NewScope();
        return await scope.ServiceProvider.GetRequiredService<ISurveyAuthoringService>().GetQuestionPoolAsync();
    }

    private async Task<SurveyPublicDto?> BySlugAsync(string slug)
    {
        await using var scope = NewScope();
        return await scope.ServiceProvider.GetRequiredService<ISurveyCatalogQuery>().GetBySlugAsync(slug);
    }

    private async Task<string?> DefaultSlugAsync()
    {
        await using var scope = NewScope();
        return await scope.ServiceProvider.GetRequiredService<ISurveyCatalogQuery>().GetDefaultSlugAsync();
    }

    private async Task<SubmissionResult> SubmitAsync(int surveyId, IEnumerable<SurveyQuestionDto> questions)
    {
        await using var scope = NewScope();
        return await scope.ServiceProvider.GetRequiredService<ISurveySubmissionService>()
            .SubmitAsync(new SubmitSurveyResponseDto(surveyId, questions.Select(ValidAnswer).ToList()));
    }

    private static AnswerInputDto ValidAnswer(SurveyQuestionDto question) => question.Type switch
    {
        QuestionType.Scale => new AnswerInputDto(question.QuestionId, 4, null, null),
        QuestionType.YesNo => new AnswerInputDto(question.QuestionId, null, true, null),
        _ => new AnswerInputDto(question.QuestionId, null, null, "odpowiedź")
    };

    private static SurveyItemInputDto NewItem(string text, QuestionType type, bool required = true) =>
        new(null, new NewQuestionInputDto(text, type), required);

    private static SurveyItemInputDto ExistingItem(int questionId, bool required = true) =>
        new(questionId, null, required);

    private static List<SurveyItemInputDto> AssignedItems(SurveyConfigDto config, bool? required = null) =>
        config.AssignedQuestions
            .OrderBy(q => q.SortOrder)
            .Select(q => new SurveyItemInputDto(q.QuestionId, null, required ?? q.IsRequired))
            .ToList();

    private static string PoolMessage(string text) =>
        $"Pytanie „{text}” już istnieje w puli — dodaj je z listy.";

    private static (int Answered, int Eligible, bool Assigned) Metrics(SurveyResultsDto results, int questionId)
    {
        var scale = results.ScaleAggregates.FirstOrDefault(q => q.QuestionId == questionId);
        if (scale is not null)
        {
            return (scale.AnsweredCount, scale.EligibleCount, scale.IsCurrentlyAssigned);
        }

        var yesNo = results.YesNoAggregates.FirstOrDefault(q => q.QuestionId == questionId);
        if (yesNo is not null)
        {
            return (yesNo.AnsweredCount, yesNo.EligibleCount, yesNo.IsCurrentlyAssigned);
        }

        var open = results.OpenAggregates.Single(q => q.QuestionId == questionId);
        return (open.AnsweredCount, open.EligibleCount, open.IsCurrentlyAssigned);
    }

    private async Task<Dictionary<int, List<int>>> AssignmentMapAsync()
    {
        await using var scope = NewScope();
        var rows = await scope.ServiceProvider.GetRequiredService<SurveyDbContext>().SurveyQuestions.AsNoTracking()
            .Select(sq => new { sq.SurveyId, sq.QuestionId, sq.SortOrder })
            .ToListAsync();
        return rows
            .GroupBy(row => row.SurveyId)
            .ToDictionary(group => group.Key, group => group.OrderBy(row => row.SortOrder).Select(row => row.QuestionId).ToList());
    }

    private async Task<int> CountLinksAsync(int questionId)
    {
        await using var scope = NewScope();
        return await scope.ServiceProvider.GetRequiredService<SurveyDbContext>().SurveyQuestions.CountAsync(sq => sq.QuestionId == questionId);
    }

    private async Task<int> CountSessionsAsync(int surveyId)
    {
        await using var scope = NewScope();
        return await scope.ServiceProvider.GetRequiredService<SurveyDbContext>().ResponseSessions.CountAsync(s => s.SurveyId == surveyId);
    }

    private async Task<int> CountAnswersAsync(int surveyId)
    {
        await using var scope = NewScope();
        return await scope.ServiceProvider.GetRequiredService<SurveyDbContext>().Answers.CountAsync(a => a.Session.SurveyId == surveyId);
    }

    private async Task<int> CountDefaultsAsync()
    {
        await using var scope = NewScope();
        return await scope.ServiceProvider.GetRequiredService<SurveyDbContext>().Surveys.CountAsync(s => s.IsDefault);
    }

    private async Task<int> CountSurveysByTitleAsync(string title)
    {
        await using var scope = NewScope();
        return await scope.ServiceProvider.GetRequiredService<SurveyDbContext>().Surveys.CountAsync(s => s.Title == title);
    }

    private async Task<int> CountQuestionsByMarkerAsync(string marker)
    {
        await using var scope = NewScope();
        return await scope.ServiceProvider.GetRequiredService<SurveyDbContext>().Questions.CountAsync(q => q.Text.Contains(marker));
    }

    private async Task<DbCounts> CountsAsync()
    {
        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<SurveyDbContext>();
        return new DbCounts(
            await db.Surveys.CountAsync(),
            await db.Questions.CountAsync(),
            await db.SurveyQuestions.CountAsync(),
            await db.ResponseSessions.CountAsync(),
            await db.Answers.CountAsync());
    }

    private async Task SetSubmittedAsync(Guid sessionId, DateTime when)
    {
        await using var scope = NewScope();
        await scope.ServiceProvider.GetRequiredService<SurveyDbContext>().ResponseSessions
            .Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.SubmittedAtUtc, when));
    }

    private async Task SetCreatedAtAsync(int surveyId, DateTime when)
    {
        await using var scope = NewScope();
        await scope.ServiceProvider.GetRequiredService<SurveyDbContext>().Surveys
            .Where(s => s.Id == surveyId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAtUtc, when));
    }

    private async Task CloseAllAsync()
    {
        await using var scope = NewScope();
        await scope.ServiceProvider.GetRequiredService<SurveyDbContext>().Surveys
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, SurveyStatus.Closed));
    }

    private async Task NormalizeAncientSurveysAsync()
    {
        await using var scope = NewScope();
        var cutoff = new DateTime(2000, 1, 1);
        await scope.ServiceProvider.GetRequiredService<SurveyDbContext>().Surveys
            .Where(s => s.CreatedAtUtc < cutoff)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAtUtc, DateTime.UtcNow));
    }

    private async Task ResetSeedDefaultActiveAsync()
    {
        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<SurveyDbContext>();
        await db.Surveys.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDefault, false));
        await db.Surveys.Where(s => s.ConfigAccessToken == LocalDbFixture.ConfigToken)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.IsDefault, true)
                .SetProperty(x => x.Status, SurveyStatus.Active));
    }

    private async Task<List<SurveyState>> SnapshotAsync()
    {
        await using var scope = NewScope();
        var rows = await scope.ServiceProvider.GetRequiredService<SurveyDbContext>().Surveys.AsNoTracking()
            .Select(s => new { s.Id, s.Status, s.IsDefault, s.CreatedAtUtc })
            .ToListAsync();
        return rows.Select(s => new SurveyState(s.Id, s.Status, s.IsDefault, s.CreatedAtUtc)).ToList();
    }

    private async Task RestoreSnapshotAsync(IReadOnlyList<SurveyState> snapshot)
    {
        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<SurveyDbContext>();
        await db.Surveys.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDefault, false));
        foreach (var state in snapshot)
        {
            await db.Surveys.Where(s => s.Id == state.Id).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, state.Status)
                .SetProperty(x => x.IsDefault, state.IsDefault)
                .SetProperty(x => x.CreatedAtUtc, state.CreatedAtUtc));
        }
    }

    private sealed record DbCounts(int Surveys, int Questions, int Links, int Sessions, int Answers);

    private sealed record SurveyState(int Id, SurveyStatus Status, bool IsDefault, DateTime CreatedAtUtc);
}
