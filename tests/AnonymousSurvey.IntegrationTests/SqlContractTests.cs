using AnonymousSurvey.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AnonymousSurvey.IntegrationTests;

public sealed class SqlContractFixture : IAsyncLifetime
{
    public SqlTestDatabase Db { get; private set; } = null!;

    public async Task InitializeAsync() => Db = await SqlTestDatabase.CreateInitializedAsync();

    public async Task DisposeAsync() => await Db.DisposeAsync();
}

/// <summary>
/// Kontrakt schematu, TVF dbo.fn_GetSurveyQuestions i SP dbo.usp_SaveSurveyResponse. Baza na klasę; każdy test
/// zakłada własną ankietę z unikalnym slugiem i nie zależy od seeda (poza testami seeda).
/// Testy zmieniające stan globalny (np. ankieta domyślna) mają własną bazę.
/// </summary>
public class SqlContractTests(SqlContractFixture fixture) : IClassFixture<SqlContractFixture>
{
    private const int Scale = 1;
    private const int Open = 2;
    private const int YesNo = 3;

    private const int Draft = 1;
    private const int Active = 2;
    private const int Closed = 3;

    private SqlTestDatabase Db => fixture.Db;

    private static string Json(params (int QuestionId, int? Scale, bool? Bool, string? Text)[] answers) =>
        SqlTestDatabase.Json(answers);

    private static async Task<SqlException> AssertSqlErrorAsync(int number, Func<Task> action)
    {
        var exception = await Assert.ThrowsAsync<SqlException>(action);
        Assert.Equal(number, exception.Number);
        return exception;
    }

    private async Task<(int SurveyId, int[] QuestionIds)> NewSurveyAsync(
        int status = Active,
        params (int Type, bool Required)[] questions)
    {
        var surveyId = await Db.InsertSurveyAsync(status);
        var ids = new int[questions.Length];
        for (var i = 0; i < questions.Length; i++)
        {
            ids[i] = await Db.InsertQuestionAsync(questions[i].Type);
            await Db.AssignAsync(surveyId, ids[i], i + 1, questions[i].Required);
        }

        return (surveyId, ids);
    }

    private async Task AssertNothingStoredAsync(int surveyId, Guid? sessionId = null)
    {
        if (sessionId is null)
        {
            Assert.Equal(0, await Db.CountAsync("ResponseSession", $"SurveyId = {surveyId}"));
            return;
        }

        Assert.Equal(0, await Db.CountAsync("ResponseSession", $"Id = '{sessionId}'"));
        Assert.Equal(0, await Db.CountAsync("Answer", $"SessionId = '{sessionId}'"));
    }

    // ---------- TVF ----------

    [Fact]
    public async Task Tvf_returns_is_required_flag()
    {
        var (surveyId, ids) = await NewSurveyAsync(Active, (Scale, true), (Open, false), (YesNo, true));

        var rows = await Db.QueryAsync(
            "SELECT QuestionId, IsRequired FROM dbo.fn_GetSurveyQuestions(@s) ORDER BY SortOrder",
            new SqlParameter("@s", surveyId));

        Assert.Equal(
            new[] { (ids[0], true), (ids[1], false), (ids[2], true) },
            rows.Select(r => ((int)r[0]!, (bool)r[1]!)));
    }

    [Fact]
    public async Task Tvf_returns_expected_columns()
    {
        var columns = await Db.QueryAsync(
            """
            SELECT name FROM sys.dm_exec_describe_first_result_set(
                N'SELECT * FROM dbo.fn_GetSurveyQuestions(1)', NULL, 0) ORDER BY column_ordinal
            """);

        Assert.Equal(
            new[] { "SurveyId", "QuestionId", "Text", "Type", "SortOrder", "IsRequired" },
            columns.Select(c => (string)c[0]!));
    }

    [Fact]
    public async Task Tvf_orders_by_sort_order_and_is_empty_for_unknown_survey()
    {
        var surveyId = await Db.InsertSurveyAsync();
        var q1 = await Db.InsertQuestionAsync(Scale);
        var q2 = await Db.InsertQuestionAsync(Open);
        var q3 = await Db.InsertQuestionAsync(YesNo);
        await Db.AssignAsync(surveyId, q1, 30);
        await Db.AssignAsync(surveyId, q2, 10);
        await Db.AssignAsync(surveyId, q3, 20);

        var ordered = await Db.QueryAsync(
            "SELECT QuestionId, SortOrder FROM dbo.fn_GetSurveyQuestions(@s) ORDER BY SortOrder",
            new SqlParameter("@s", surveyId));
        var unknown = await Db.QueryAsync(
            "SELECT * FROM dbo.fn_GetSurveyQuestions(@s)", new SqlParameter("@s", int.MaxValue));

        Assert.Equal(new[] { (q2, 10), (q3, 20), (q1, 30) }, ordered.Select(r => ((int)r[0]!, (int)r[1]!)));
        Assert.Empty(unknown);
    }

    [Fact]
    public async Task Tvf_returns_only_questions_of_requested_survey()
    {
        var (first, firstIds) = await NewSurveyAsync(Active, (Scale, true), (Open, true));
        var (second, secondIds) = await NewSurveyAsync(Active, (YesNo, true));

        var forFirst = await Db.QueryAsync(
            "SELECT QuestionId FROM dbo.fn_GetSurveyQuestions(@s) ORDER BY SortOrder", new SqlParameter("@s", first));
        var forSecond = await Db.QueryAsync(
            "SELECT QuestionId FROM dbo.fn_GetSurveyQuestions(@s)", new SqlParameter("@s", second));

        Assert.Equal(firstIds, forFirst.Select(r => (int)r[0]!));
        Assert.Equal(secondIds, forSecond.Select(r => (int)r[0]!));
    }

    [Fact]
    public async Task Tvf_excludes_inactive_questions()
    {
        var surveyId = await Db.InsertSurveyAsync();
        var visible = await Db.InsertQuestionAsync(Scale);
        var hidden = await Db.InsertQuestionAsync(Scale, isActive: false);
        await Db.AssignAsync(surveyId, visible, 1);
        await Db.AssignAsync(surveyId, hidden, 2);

        var rows = await Db.QueryAsync(
            "SELECT QuestionId FROM dbo.fn_GetSurveyQuestions(@s)", new SqlParameter("@s", surveyId));

        Assert.Equal(new[] { visible }, rows.Select(r => (int)r[0]!));
    }

    [Fact]
    public async Task Tvf_returns_questions_regardless_of_survey_status()
    {
        foreach (var status in new[] { Draft, Active, Closed })
        {
            var (surveyId, ids) = await NewSurveyAsync(status, (Scale, true));
            var rows = await Db.QueryAsync(
                "SELECT QuestionId FROM dbo.fn_GetSurveyQuestions(@s)", new SqlParameter("@s", surveyId));
            Assert.Equal(ids, rows.Select(r => (int)r[0]!));
        }
    }

    // ---------- SP: ścieżka poprawna ----------

    [Fact]
    public async Task Sp_saves_all_answers_for_active_survey_and_does_not_touch_survey_row()
    {
        var (surveyId, ids) = await NewSurveyAsync(Active, (Scale, true), (YesNo, true), (Open, true));
        var versionBefore = await Db.ScalarAsync<long>(
            "SELECT CAST(RowVersion AS BIGINT) FROM dbo.Survey WHERE Id = @s", new SqlParameter("@s", surveyId));

        var session = await Db.SaveResponseAsync(
            surveyId,
            Json((ids[0], 4, null, null), (ids[1], null, true, null), (ids[2], null, null, "Więcej opcji płatności")));

        Assert.Equal(1, await Db.CountAsync("ResponseSession", $"Id = '{session}' AND SurveyId = {surveyId}"));
        var answers = await Db.QueryAsync(
            "SELECT QuestionId, ScaleValue, BoolValue, TextValue FROM dbo.Answer WHERE SessionId = @id ORDER BY QuestionId",
            new SqlParameter("@id", session));
        Assert.Equal(3, answers.Count);
        Assert.Equal(4, answers[0][1]);
        Assert.Equal(true, answers[1][2]);
        Assert.Equal("Więcej opcji płatności", answers[2][3]);

        var versionAfter = await Db.ScalarAsync<long>(
            "SELECT CAST(RowVersion AS BIGINT) FROM dbo.Survey WHERE Id = @s", new SqlParameter("@s", surveyId));
        Assert.Equal(versionBefore, versionAfter);
    }

    [Fact]
    public async Task Sp_accepts_missing_optional_answer()
    {
        var (surveyId, ids) = await NewSurveyAsync(Active, (Scale, true), (Open, false));

        var session = await Db.SaveResponseAsync(surveyId, Json((ids[0], 3, null, null)));

        Assert.Equal(1, await Db.CountAsync("ResponseSession", $"Id = '{session}'"));
        Assert.Equal(1, await Db.CountAsync("Answer", $"SessionId = '{session}'"));
        Assert.Equal(0, await Db.CountAsync("Answer", $"SessionId = '{session}' AND QuestionId = {ids[1]}"));
    }

    [Fact]
    public async Task Sp_accepts_optional_answer_when_given()
    {
        var (surveyId, ids) = await NewSurveyAsync(Active, (Scale, true), (Open, false));

        var session = await Db.SaveResponseAsync(surveyId, Json((ids[0], 3, null, null), (ids[1], null, null, "ok")));

        Assert.Equal(2, await Db.CountAsync("Answer", $"SessionId = '{session}'"));
    }

    [Fact]
    public async Task Sp_accepts_when_all_required_present_and_only_optional_missing()
    {
        var (surveyId, ids) = await NewSurveyAsync(Active, (Scale, false), (Open, true), (YesNo, false));

        var session = await Db.SaveResponseAsync(surveyId, Json((ids[1], null, null, "tylko wymagane")));

        Assert.Equal(1, await Db.CountAsync("Answer", $"SessionId = '{session}'"));
    }

    // ---------- SP: odrzucenia ----------

    [Fact]
    public async Task Sp_rejects_missing_required_answer_with_50004()
    {
        var (surveyId, ids) = await NewSurveyAsync(Active, (Scale, true), (Open, true));
        var session = Guid.NewGuid();

        await AssertSqlErrorAsync(
            50004, () => Db.SaveResponseAsync(surveyId, Json((ids[0], 3, null, null)), session));

        await AssertNothingStoredAsync(surveyId, session);
    }

    [Fact]
    public async Task Sp_rejects_when_no_answers_with_50005()
    {
        var (surveyId, _) = await NewSurveyAsync(Active, (Scale, false), (Open, false));
        var session = Guid.NewGuid();

        await AssertSqlErrorAsync(50005, () => Db.SaveResponseAsync(surveyId, "[]", session));

        await AssertNothingStoredAsync(surveyId, session);
    }

    [Fact]
    public async Task Sp_rejects_empty_answers_with_50004_when_survey_has_required_question()
    {
        var (surveyId, _) = await NewSurveyAsync(Active, (Scale, true));

        await AssertSqlErrorAsync(50004, () => Db.SaveResponseAsync(surveyId, "[]"));

        await AssertNothingStoredAsync(surveyId);
    }

    [Fact]
    public async Task Sp_rejects_answer_for_question_outside_survey_with_50003()
    {
        var (surveyId, ids) = await NewSurveyAsync(Active, (Scale, true));
        var (_, otherIds) = await NewSurveyAsync(Active, (Scale, true));
        var session = Guid.NewGuid();

        await AssertSqlErrorAsync(
            50003,
            () => Db.SaveResponseAsync(surveyId, Json((ids[0], 3, null, null), (otherIds[0], 3, null, null)), session));

        await AssertNothingStoredAsync(surveyId, session);
    }

    [Fact]
    public async Task Sp_rejects_answer_for_nonexistent_question_with_50003()
    {
        var (surveyId, ids) = await NewSurveyAsync(Active, (Scale, true));

        await AssertSqlErrorAsync(
            50003, () => Db.SaveResponseAsync(surveyId, Json((ids[0], 3, null, null), (int.MaxValue, 3, null, null))));

        await AssertNothingStoredAsync(surveyId);
    }

    [Fact]
    public async Task Sp_rejects_answer_item_without_question_id_with_50003()
    {
        var (surveyId, ids) = await NewSurveyAsync(Active, (Scale, true));
        var json = $"[{{\"questionId\":{ids[0]},\"scaleValue\":3}},{{\"scaleValue\":3}}]";

        await AssertSqlErrorAsync(50003, () => Db.SaveResponseAsync(surveyId, json));

        await AssertNothingStoredAsync(surveyId);
    }

    [Fact]
    public async Task Sp_rejects_answer_for_deactivated_question_with_50003()
    {
        var (surveyId, ids) = await NewSurveyAsync(Active, (Scale, true), (Open, false));
        await Db.ExecuteAsync("UPDATE dbo.Question SET IsActive = 0 WHERE Id = @q", new SqlParameter("@q", ids[1]));

        await AssertSqlErrorAsync(
            50003, () => Db.SaveResponseAsync(surveyId, Json((ids[0], 3, null, null), (ids[1], null, null, "x"))));

        await AssertNothingStoredAsync(surveyId);
    }

    [Theory]
    [InlineData(Draft)]
    [InlineData(Closed)]
    public async Task Sp_rejects_draft_and_closed_survey_with_50001(int status)
    {
        var (surveyId, ids) = await NewSurveyAsync(status, (Scale, true));

        await AssertSqlErrorAsync(50001, () => Db.SaveResponseAsync(surveyId, Json((ids[0], 3, null, null))));

        await AssertNothingStoredAsync(surveyId);
    }

    [Fact]
    public async Task Sp_rejects_unknown_survey_with_50001()
    {
        await AssertSqlErrorAsync(50001, () => Db.SaveResponseAsync(int.MaxValue, "[]"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("to nie jest json")]
    [InlineData("{\"questionId\":1")]
    public async Task Sp_rejects_invalid_json_with_50002(string? json)
    {
        var (surveyId, _) = await NewSurveyAsync(Active, (Scale, true));

        await AssertSqlErrorAsync(50002, () => Db.SaveResponseAsync(surveyId, json!));

        await AssertNothingStoredAsync(surveyId);
    }

    [Fact]
    public async Task Sp_rejects_answer_item_without_value()
    {
        var (surveyId, ids) = await NewSurveyAsync(Active, (Scale, true), (Open, false));
        var session = Guid.NewGuid();

        await AssertSqlErrorAsync(
            547, () => Db.SaveResponseAsync(surveyId, Json((ids[0], 3, null, null), (ids[1], null, null, null)), session));

        await AssertNothingStoredAsync(surveyId, session);
    }

    [Fact]
    public async Task Sp_rejects_duplicate_answers_for_the_same_question()
    {
        var (surveyId, ids) = await NewSurveyAsync(Active, (Scale, true));
        var session = Guid.NewGuid();

        await AssertSqlErrorAsync(
            2627, () => Db.SaveResponseAsync(surveyId, Json((ids[0], 3, null, null), (ids[0], 4, null, null)), session));

        await AssertNothingStoredAsync(surveyId, session);
    }

    [Fact]
    public async Task Sp_rejects_reused_session_id_and_keeps_first_answers()
    {
        var (surveyId, ids) = await NewSurveyAsync(Active, (Scale, true));
        var session = await Db.SaveResponseAsync(surveyId, Json((ids[0], 2, null, null)));

        await AssertSqlErrorAsync(
            2627, () => Db.SaveResponseAsync(surveyId, Json((ids[0], 5, null, null)), session));

        Assert.Equal(1, await Db.CountAsync("ResponseSession", $"Id = '{session}'"));
        Assert.Equal(2, await Db.ScalarAsync<int>(
            "SELECT ScaleValue FROM dbo.Answer WHERE SessionId = @id", new SqlParameter("@id", session)));
    }

    // ---------- SP: granice wartości ----------

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    [InlineData(-1, false)]
    public async Task Sp_scale_boundaries(int value, bool accepted)
    {
        var (surveyId, ids) = await NewSurveyAsync(Active, (Scale, true));
        var session = Guid.NewGuid();

        if (accepted)
        {
            await Db.SaveResponseAsync(surveyId, Json((ids[0], value, null, null)), session);
            Assert.Equal(1, await Db.CountAsync("Answer", $"SessionId = '{session}'"));
        }
        else
        {
            await AssertSqlErrorAsync(
                547, () => Db.SaveResponseAsync(surveyId, Json((ids[0], value, null, null)), session));
            await AssertNothingStoredAsync(surveyId, session);
        }
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2000, true)]
    [InlineData(2001, false)]
    public async Task Sp_open_answer_boundaries_2000_ok_2001_rejected(int length, bool accepted)
    {
        var (surveyId, ids) = await NewSurveyAsync(Active, (Open, true));
        var text = new string('a', length);
        var session = Guid.NewGuid();

        if (accepted)
        {
            await Db.SaveResponseAsync(surveyId, Json((ids[0], null, null, text)), session);
            Assert.Equal(length, await Db.ScalarAsync<int>(
                "SELECT LEN(TextValue) FROM dbo.Answer WHERE SessionId = @id", new SqlParameter("@id", session)));
        }
        else
        {
            await Assert.ThrowsAsync<SqlException>(
                () => Db.SaveResponseAsync(surveyId, Json((ids[0], null, null, text)), session));
            await AssertNothingStoredAsync(surveyId, session);
        }
    }

    [Fact]
    public async Task Sp_unicode_roundtrip_polish_and_emoji()
    {
        var (surveyId, ids) = await NewSurveyAsync(Active, (Open, true), (Open, true));
        const string polish = "Zażółć gęślą jaźń ĄĆĘŁŃÓŚŹŻ <script>alert(1)</script> '; DROP TABLE Answer;-- {{7*7}}";
        const string emoji = "Świetnie 😀👍🏽 koniec";

        var session = await Db.SaveResponseAsync(surveyId, Json((ids[0], null, null, polish), (ids[1], null, null, emoji)));

        var texts = await Db.QueryAsync(
            "SELECT TextValue FROM dbo.Answer WHERE SessionId = @id ORDER BY QuestionId", new SqlParameter("@id", session));
        Assert.Equal(polish, texts[0][0]);
        Assert.Equal(emoji, texts[1][0]);
        Assert.True(await Db.CountAsync("Answer") > 0, "Tabela Answer nie może zniknąć.");
    }

    [Fact]
    public async Task Sp_open_answer_limit_counts_utf16_code_units_not_characters()
    {
        var (surveyId, ids) = await NewSurveyAsync(Active, (Open, true));
        var thousandEmoji = string.Concat(Enumerable.Repeat("😀", 1000));
        var oneMore = thousandEmoji + "😀";
        var session = Guid.NewGuid();

        await Db.SaveResponseAsync(surveyId, Json((ids[0], null, null, thousandEmoji)), session);
        Assert.Equal(2000, await Db.ScalarAsync<int>(
            "SELECT DATALENGTH(TextValue) / 2 FROM dbo.Answer WHERE SessionId = @id", new SqlParameter("@id", session)));

        var rejected = Guid.NewGuid();
        await Assert.ThrowsAsync<SqlException>(
            () => Db.SaveResponseAsync(surveyId, Json((ids[0], null, null, oneMore)), rejected));
        await AssertNothingStoredAsync(surveyId, rejected);
    }

    // ---------- SP: atomowość i współbieżność ----------

    [Fact]
    public async Task Sp_is_atomic_when_second_answer_invalid()
    {
        var (surveyId, ids) = await NewSurveyAsync(Active, (Scale, true), (Scale, true));
        var session = Guid.NewGuid();

        await AssertSqlErrorAsync(
            547, () => Db.SaveResponseAsync(surveyId, Json((ids[0], 3, null, null), (ids[1], 9, null, null)), session));

        await AssertNothingStoredAsync(surveyId, session);
    }

    [Fact]
    public async Task Sp_concurrent_submissions_all_succeed_with_unique_sessions()
    {
        var (surveyId, ids) = await NewSurveyAsync(Active, (Scale, true), (Open, false));

        await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
            Db.SaveResponseAsync(surveyId, Json((ids[0], 1 + i % 5, null, null), (ids[1], null, null, $"odp {i}")))));

        Assert.Equal(20, await Db.CountAsync("ResponseSession", $"SurveyId = {surveyId}"));
        Assert.Equal(40, await Db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Answer a JOIN dbo.ResponseSession s ON s.Id = a.SessionId WHERE s.SurveyId = @s",
            new SqlParameter("@s", surveyId)));
    }

    [Fact]
    public async Task Sp_concurrent_submissions_with_same_session_id_store_exactly_one()
    {
        var (surveyId, ids) = await NewSurveyAsync(Active, (Scale, true));
        var session = Guid.NewGuid();

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            try
            {
                await Db.SaveResponseAsync(surveyId, Json((ids[0], 3, null, null)), session);
                return (Ok: true, Number: 0);
            }
            catch (SqlException ex)
            {
                return (Ok: false, ex.Number);
            }
        }));

        Assert.Single(outcomes, o => o.Ok);
        Assert.All(outcomes.Where(o => !o.Ok), o => Assert.Equal(2627, o.Number));
        Assert.Equal(1, await Db.CountAsync("ResponseSession", $"Id = '{session}'"));
        Assert.Equal(1, await Db.CountAsync("Answer", $"SessionId = '{session}'"));
    }

    // ---------- SP: konfiguracja zmieniona po załadowaniu formularza ----------

    [Fact]
    public async Task Sp_rejects_with_50001_when_survey_closed_after_form_loaded()
    {
        var (surveyId, ids) = await NewSurveyAsync(Active, (Scale, true));
        var json = Json((ids[0], 3, null, null));
        await Db.ExecuteAsync("UPDATE dbo.Survey SET [Status] = 3 WHERE Id = @s", new SqlParameter("@s", surveyId));

        await AssertSqlErrorAsync(50001, () => Db.SaveResponseAsync(surveyId, json));
    }

    [Fact]
    public async Task Sp_rejects_with_50003_when_question_unassigned_after_form_loaded()
    {
        var (surveyId, ids) = await NewSurveyAsync(Active, (Scale, true), (Scale, true));
        var json = Json((ids[0], 3, null, null), (ids[1], 3, null, null));
        await Db.ExecuteAsync(
            "DELETE FROM dbo.SurveyQuestion WHERE SurveyId = @s AND QuestionId = @q",
            new SqlParameter("@s", surveyId), new SqlParameter("@q", ids[1]));

        await AssertSqlErrorAsync(50003, () => Db.SaveResponseAsync(surveyId, json));
    }

    [Fact]
    public async Task Sp_rejects_with_50004_when_optional_question_became_required_after_form_loaded()
    {
        var (surveyId, ids) = await NewSurveyAsync(Active, (Scale, true), (Open, false));
        var json = Json((ids[0], 3, null, null));
        await Db.ExecuteAsync(
            "UPDATE dbo.SurveyQuestion SET IsRequired = 1 WHERE SurveyId = @s AND QuestionId = @q",
            new SqlParameter("@s", surveyId), new SqlParameter("@q", ids[1]));

        await AssertSqlErrorAsync(50004, () => Db.SaveResponseAsync(surveyId, json));
    }

    [Fact]
    public async Task Sp_rejects_with_50004_when_question_added_after_form_loaded()
    {
        var (surveyId, ids) = await NewSurveyAsync(Active, (Scale, true));
        var json = Json((ids[0], 3, null, null));
        var added = await Db.InsertQuestionAsync(YesNo);
        await Db.AssignAsync(surveyId, added, 2, isRequired: true);

        await AssertSqlErrorAsync(50004, () => Db.SaveResponseAsync(surveyId, json));
    }

    [Fact]
    public async Task Sp_accepts_when_required_question_became_optional_after_form_loaded()
    {
        var (surveyId, ids) = await NewSurveyAsync(Active, (Scale, true), (Open, true));
        var json = Json((ids[0], 3, null, null));
        await Db.ExecuteAsync(
            "UPDATE dbo.SurveyQuestion SET IsRequired = 0 WHERE SurveyId = @s AND QuestionId = @q",
            new SqlParameter("@s", surveyId), new SqlParameter("@q", ids[1]));

        var session = await Db.SaveResponseAsync(surveyId, json);

        Assert.Equal(1, await Db.CountAsync("ResponseSession", $"Id = '{session}'"));
    }

    // ---------- Schemat ----------

    [Fact]
    public async Task Check_constraint_rejects_all_null_answer()
    {
        var (surveyId, ids) = await NewSurveyAsync(Active, (Open, true));
        var session = Guid.NewGuid();
        await Db.ExecuteAsync(
            "INSERT INTO dbo.ResponseSession (Id, SurveyId, SubmittedAtUtc) VALUES (@id, @s, SYSUTCDATETIME())",
            new SqlParameter("@id", session), new SqlParameter("@s", surveyId));

        await AssertSqlErrorAsync(547, () => Db.ExecuteAsync(
            "INSERT INTO dbo.Answer (SessionId, QuestionId) VALUES (@id, @q)",
            new SqlParameter("@id", session), new SqlParameter("@q", ids[0])));
    }

    [Fact]
    public async Task Initializer_is_idempotent_on_new_schema()
    {
        await using var db = await SqlTestDatabase.CreateInitializedAsync();
        var schemaBefore = await SchemaFingerprint.CaptureAsync(db);
        var surveysBefore = await db.CountAsync("Survey");
        var questionsBefore = await db.CountAsync("Question");
        var assignmentsBefore = await db.CountAsync("SurveyQuestion");

        await db.InitializeSchemaAsync();
        await db.InitializeSchemaAsync();

        Assert.Equal(schemaBefore, await SchemaFingerprint.CaptureAsync(db));
        Assert.Equal(surveysBefore, await db.CountAsync("Survey"));
        Assert.Equal(questionsBefore, await db.CountAsync("Question"));
        Assert.Equal(assignmentsBefore, await db.CountAsync("SurveyQuestion"));
    }

    [Fact]
    public async Task Initializer_keeps_user_data_when_run_again()
    {
        await using var db = await SqlTestDatabase.CreateInitializedAsync();
        var (surveyId, ids) = await NewSurveyOnAsync(db, (Scale, true));
        var session = await db.SaveResponseAsync(surveyId, Json((ids[0], 4, null, null)));

        await db.InitializeSchemaAsync();

        Assert.Equal(1, await db.CountAsync("ResponseSession", $"Id = '{session}'"));
        Assert.Equal(1, await db.CountAsync("Answer", $"SessionId = '{session}'"));
    }

    [Fact]
    public async Task Unique_index_rejects_duplicate_question_type_and_text_case_insensitive()
    {
        var text = $"Pytanie unikalne {Guid.NewGuid():N}";
        await Db.InsertQuestionAsync(Scale, text);

        await AssertSqlErrorAsync(2601, () => Db.InsertQuestionAsync(Scale, text));
        await AssertSqlErrorAsync(2601, () => Db.InsertQuestionAsync(Scale, text.ToUpperInvariant()));
    }

    [Fact]
    public async Task Unique_question_index_allows_same_text_with_different_type()
    {
        var text = $"Ten sam tekst {Guid.NewGuid():N}";

        var scale = await Db.InsertQuestionAsync(Scale, text);
        var yesNo = await Db.InsertQuestionAsync(YesNo, text);

        Assert.NotEqual(scale, yesNo);
    }

    [Fact]
    public async Task Question_text_500_ok_501_rejected_and_type_outside_1_to_3_rejected()
    {
        await Db.InsertQuestionAsync(Open, new string('x', 500));
        await Assert.ThrowsAsync<SqlException>(() => Db.InsertQuestionAsync(Open, new string('y', 501)));
        await AssertSqlErrorAsync(547, () => Db.InsertQuestionAsync(0));
        await AssertSqlErrorAsync(547, () => Db.InsertQuestionAsync(4));
    }

    [Fact]
    public async Task Unique_default_index_rejects_two_defaults_but_allows_many_non_defaults()
    {
        await using var db = await SqlTestDatabase.CreateInitializedAsync();
        await db.ExecuteAsync("UPDATE dbo.Survey SET IsDefault = 0");

        await db.InsertSurveyAsync(isDefault: false);
        await db.InsertSurveyAsync(isDefault: false);
        await db.InsertSurveyAsync(isDefault: true);

        await AssertSqlErrorAsync(2601, () => db.InsertSurveyAsync(isDefault: true));
        Assert.Equal(1, await db.CountAsync("Survey", "IsDefault = 1"));
    }

    [Fact]
    public async Task Unique_default_index_rejects_switching_default_without_clearing_previous()
    {
        await using var db = await SqlTestDatabase.CreateInitializedAsync();
        var other = await db.InsertSurveyAsync();

        await AssertSqlErrorAsync(2601, () => db.ExecuteAsync(
            "UPDATE dbo.Survey SET IsDefault = 1 WHERE Id = @id", new SqlParameter("@id", other)));
    }

    [Fact]
    public async Task Slug_unique_index_is_case_insensitive()
    {
        var slug = $"slug-{Guid.NewGuid():N}";
        await InsertSurveyWithSlugAsync(slug, $"r1-{slug}", $"c1-{slug}");

        await AssertSqlErrorAsync(2601, () => InsertSurveyWithSlugAsync(slug, $"r2-{slug}", $"c2-{slug}"));
        await AssertSqlErrorAsync(2601, () => InsertSurveyWithSlugAsync(slug.ToUpperInvariant(), $"r3-{slug}", $"c3-{slug}"));
    }

    [Fact]
    public async Task Tokens_are_unique_across_surveys()
    {
        var id = Guid.NewGuid().ToString("N");
        await InsertSurveyWithSlugAsync($"tok-a-{id}", $"results-{id}", $"config-{id}");

        await AssertSqlErrorAsync(2601, () => InsertSurveyWithSlugAsync($"tok-b-{id}", $"results-{id}", $"config-b-{id}"));
        await AssertSqlErrorAsync(2601, () => InsertSurveyWithSlugAsync($"tok-c-{id}", $"results-c-{id}", $"config-{id}"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(-1)]
    public async Task Survey_status_outside_1_to_3_is_rejected(int status)
    {
        await AssertSqlErrorAsync(547, () => Db.InsertSurveyAsync(status));
    }

    [Fact]
    public async Task Survey_defaults_are_draft_not_default_and_timestamps_now()
    {
        var id = Guid.NewGuid().ToString("N");

        await Db.ExecuteAsync(
            "INSERT INTO dbo.Survey (Slug, Title, ResultsAccessToken, ConfigAccessToken) VALUES (@s, N'Domyślne', @r, @c)",
            new SqlParameter("@s", $"def-{id}"), new SqlParameter("@r", $"r-{id}"), new SqlParameter("@c", $"c-{id}"));

        var row = (await Db.QueryAsync(
            """
            SELECT [Status], IsDefault, DATEDIFF(SECOND, CreatedAtUtc, SYSUTCDATETIME()), DATEDIFF(SECOND, UpdatedAtUtc, SYSUTCDATETIME()), [Description]
            FROM dbo.Survey WHERE Slug = @s
            """,
            new SqlParameter("@s", $"def-{id}")))[0];
        Assert.Equal(1, row[0]);
        Assert.Equal(false, row[1]);
        Assert.InRange((int)row[2]!, -1, 30);
        Assert.InRange((int)row[3]!, -1, 30);
        Assert.Null(row[4]);
    }

    [Fact]
    public async Task Survey_question_defaults_are_required_and_added_now()
    {
        var surveyId = await Db.InsertSurveyAsync();
        var questionId = await Db.InsertQuestionAsync(Scale);

        await Db.ExecuteAsync(
            "INSERT INTO dbo.SurveyQuestion (SurveyId, QuestionId, SortOrder) VALUES (@s, @q, 1)",
            new SqlParameter("@s", surveyId), new SqlParameter("@q", questionId));

        var row = (await Db.QueryAsync(
            "SELECT IsRequired, DATEDIFF(SECOND, AddedAtUtc, SYSUTCDATETIME()) FROM dbo.SurveyQuestion WHERE SurveyId = @s",
            new SqlParameter("@s", surveyId)))[0];
        Assert.Equal(true, row[0]);
        Assert.InRange((int)row[1]!, -1, 30);
    }

    [Fact]
    public async Task Rowversion_changes_on_survey_update()
    {
        var surveyId = await Db.InsertSurveyAsync();
        var before = await Db.ScalarAsync<long>(
            "SELECT CAST(RowVersion AS BIGINT) FROM dbo.Survey WHERE Id = @s", new SqlParameter("@s", surveyId));

        await Db.ExecuteAsync("UPDATE dbo.Survey SET Title = N'Zmieniony' WHERE Id = @s", new SqlParameter("@s", surveyId));

        var after = await Db.ScalarAsync<long>(
            "SELECT CAST(RowVersion AS BIGINT) FROM dbo.Survey WHERE Id = @s", new SqlParameter("@s", surveyId));
        Assert.NotEqual(before, after);
    }

    [Fact]
    public async Task Schema_has_no_personal_data_columns()
    {
        var sessionColumns = await Db.QueryAsync(
            "SELECT name FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.ResponseSession') ORDER BY name");
        var answerColumns = await Db.QueryAsync(
            "SELECT name FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Answer') ORDER BY name");

        Assert.Equal(
            new[] { "Id", "SubmittedAtUtc", "SurveyId" },
            sessionColumns.Select(c => (string)c[0]!));
        Assert.Equal(
            new[] { "BoolValue", "QuestionId", "ScaleValue", "SessionId", "TextValue" },
            answerColumns.Select(c => (string)c[0]!));
    }

    [Fact]
    public async Task Survey_has_no_is_active_column_after_initialization()
    {
        Assert.Equal(0, await Db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Survey') AND name = N'IsActive'"));
    }

    [Fact]
    public async Task Ef_model_reads_all_tables_after_initialization()
    {
        var (surveyId, ids) = await NewSurveyAsync(Active, (Scale, true), (Open, false));
        await Db.SaveResponseAsync(surveyId, Json((ids[0], 5, null, null)));

        await using var provider = Db.BuildServices();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SurveyDbContext>();

        var survey = await context.Surveys
            .Include(s => s.SurveyQuestions)
            .Include(s => s.ResponseSessions).ThenInclude(r => r.Answers)
            .SingleAsync(s => s.Id == surveyId);
        var rows = await context.GetSurveyQuestions(surveyId).OrderBy(r => r.SortOrder).ToListAsync();

        Assert.Equal(Active, (int)survey.Status);
        Assert.NotEmpty(survey.RowVersion);
        Assert.Equal(2, survey.SurveyQuestions.Count);
        Assert.Single(survey.ResponseSessions);
        Assert.Single(survey.ResponseSessions.Single().Answers);
        Assert.Equal(new[] { true, false }, rows.Select(r => r.IsRequired));
    }

    // ---------- Seed ----------

    [Fact]
    public async Task Seed_creates_default_active_survey_with_known_slug_and_tokens()
    {
        var row = (await Db.QueryAsync(
            """
            SELECT Id, Slug, Title, [Status], IsDefault, ResultsAccessToken, ConfigAccessToken
            FROM dbo.Survey WHERE ResultsAccessToken = N'wyniki-4f9c2a7e1b3d48a69c5e0d7f2a8b6c13'
            """))[0];

        Assert.Equal("ankieta-satysfakcji", row[1]);
        Assert.Equal("Ankieta satysfakcji", row[2]);
        Assert.Equal(Active, row[3]);
        Assert.Equal(true, row[4]);
        Assert.Equal("konfiguracja-8e1d5b3a9c7f42d6b0a4e2c9f7d1b538", row[6]);
    }

    [Fact]
    public async Task Seed_assigns_five_required_questions_in_order_with_original_added_date()
    {
        var surveyId = await Db.ScalarAsync<int>(
            "SELECT Id FROM dbo.Survey WHERE Slug = N'ankieta-satysfakcji'");

        var rows = await Db.QueryAsync(
            """
            SELECT f.SortOrder, f.IsRequired, CONVERT(VARCHAR(10), sq.AddedAtUtc, 120), q.[Type]
            FROM dbo.fn_GetSurveyQuestions(@s) AS f
            JOIN dbo.SurveyQuestion AS sq ON sq.SurveyId = f.SurveyId AND sq.QuestionId = f.QuestionId
            JOIN dbo.Question AS q ON q.Id = f.QuestionId
            ORDER BY f.SortOrder
            """,
            new SqlParameter("@s", surveyId));

        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, rows.Select(r => (int)r[0]!));
        Assert.All(rows, r =>
        {
            Assert.Equal(true, r[1]);
            Assert.Equal("2000-01-01", r[2]);
        });
        Assert.Equal(new[] { Scale, Scale, YesNo, YesNo, Open }, rows.Select(r => (int)r[3]!));
    }

    [Fact]
    public async Task Seed_leaves_three_questions_in_pool_unassigned()
    {
        var pool = await Db.ScalarAsync<int>(
            """
            SELECT COUNT(*) FROM dbo.Question AS q
            WHERE q.[Text] IN (N'Jak oceniasz czytelność naszej strony?', N'Czy chcesz dodać dodatkowe uwagi?', N'Czy skorzystasz z naszej usługi ponownie?')
              AND NOT EXISTS (SELECT 1 FROM dbo.SurveyQuestion AS sq WHERE sq.QuestionId = q.Id)
            """);

        Assert.Equal(3, pool);
    }

    [Fact]
    public async Task Seed_is_idempotent_when_questions_already_exist()
    {
        await using var db = await SqlTestDatabase.CreateInitializedAsync();
        var questionsBefore = await db.CountAsync("Question");
        await db.ExecuteAsync("DELETE FROM dbo.SurveyQuestion; DELETE FROM dbo.Survey;");

        await db.InitializeSchemaAsync();

        Assert.Equal(1, await db.CountAsync("Survey"));
        Assert.Equal(1, await db.CountAsync("Survey", "IsDefault = 1 AND [Status] = 2"));
        Assert.Equal(5, await db.CountAsync("SurveyQuestion"));
        Assert.Equal(questionsBefore, await db.CountAsync("Question"));
    }

    private async Task InsertSurveyWithSlugAsync(string slug, string resultsToken, string configToken) =>
        await Db.ExecuteAsync(
            "INSERT INTO dbo.Survey (Slug, Title, ResultsAccessToken, ConfigAccessToken) VALUES (@s, N'T', @r, @c)",
            new SqlParameter("@s", slug), new SqlParameter("@r", resultsToken), new SqlParameter("@c", configToken));

    private static async Task<(int SurveyId, int[] QuestionIds)> NewSurveyOnAsync(
        SqlTestDatabase db,
        params (int Type, bool Required)[] questions)
    {
        var surveyId = await db.InsertSurveyAsync();
        var ids = new int[questions.Length];
        for (var i = 0; i < questions.Length; i++)
        {
            ids[i] = await db.InsertQuestionAsync(questions[i].Type);
            await db.AssignAsync(surveyId, ids[i], i + 1, questions[i].Required);
        }

        return (surveyId, ids);
    }
}

/// <summary>Odcisk struktury bazy (kolumny, indeksy, ograniczenia, klucze obce) do porównań schematów.</summary>
public static class SchemaFingerprint
{
    private const string Sql =
        """
        SELECT x FROM (
            SELECT CONCAT('col|', t.name, '|', c.name, '|', ty.name, '|', c.max_length, '|', c.precision, '|', c.scale, '|', c.is_nullable, '|', c.is_identity) AS x
            FROM sys.columns AS c
            JOIN sys.tables AS t ON t.object_id = c.object_id
            JOIN sys.types AS ty ON ty.user_type_id = c.user_type_id
            UNION ALL
            SELECT CONCAT('idx|', t.name, '|', i.name, '|', i.is_unique, '|', i.is_primary_key, '|', ISNULL(i.filter_definition, ''), '|',
                   (SELECT STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY ic.key_ordinal)
                    FROM sys.index_columns AS ic JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                    WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id))
            FROM sys.indexes AS i JOIN sys.tables AS t ON t.object_id = i.object_id WHERE i.name IS NOT NULL
            UNION ALL
            SELECT CONCAT('ck|', t.name, '|', k.name, '|', k.definition)
            FROM sys.check_constraints AS k JOIN sys.tables AS t ON t.object_id = k.parent_object_id
            UNION ALL
            SELECT CONCAT('df|', t.name, '|', c.name, '|', d.name, '|', d.definition)
            FROM sys.default_constraints AS d
            JOIN sys.tables AS t ON t.object_id = d.parent_object_id
            JOIN sys.columns AS c ON c.object_id = d.parent_object_id AND c.column_id = d.parent_column_id
            UNION ALL
            SELECT CONCAT('fk|', pt.name, '|', f.name, '|', rt.name)
            FROM sys.foreign_keys AS f
            JOIN sys.tables AS pt ON pt.object_id = f.parent_object_id
            JOIN sys.tables AS rt ON rt.object_id = f.referenced_object_id
        ) AS s ORDER BY x
        """;

    public static async Task<string> CaptureAsync(SqlTestDatabase db) =>
        string.Join("\n", (await db.QueryAsync(Sql)).Select(r => (string)r[0]!));
}
