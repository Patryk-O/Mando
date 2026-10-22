using Microsoft.Data.SqlClient;

namespace AnonymousSurvey.IntegrationTests;

/// <summary>
/// Upgrade bazy ze starego schematu (commit 9334b2f: Survey.IsActive, bez Slug/Status/IsRequired) do nowego kształtu
/// wykonywany przez DatabaseInitializer. Każdy test ma własną bazę.
/// </summary>
public class SqlUpgradeTests
{
    private const int Scale = 1;
    private const int Open = 2;
    private const int YesNo = 3;

    private static async Task<int> LegacySurveyAsync(TempDatabase db, string title, bool isActive)
    {
        var token = Guid.NewGuid().ToString("N");
        return await db.ScalarAsync<int>(
            """
            INSERT INTO dbo.Survey (Title, IsActive, ResultsAccessToken, ConfigAccessToken) VALUES (@t, @a, @r, @c);
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """,
            new SqlParameter("@t", title),
            new SqlParameter("@a", isActive),
            new SqlParameter("@r", $"wyniki-{token}"),
            new SqlParameter("@c", $"konfiguracja-{token}"));
    }

    private static async Task<int> LegacyQuestionAsync(TempDatabase db, int surveyId, int type, int sortOrder)
    {
        var questionId = await db.InsertQuestionAsync(type);
        await db.ExecuteAsync(
            "INSERT INTO dbo.SurveyQuestion (SurveyId, QuestionId, SortOrder) VALUES (@s, @q, @o)",
            new SqlParameter("@s", surveyId), new SqlParameter("@q", questionId), new SqlParameter("@o", sortOrder));
        return questionId;
    }

    private static async Task<Guid> LegacySessionAsync(
        TempDatabase db,
        int surveyId,
        params (int QuestionId, int? Scale, bool? Bool, string? Text)[] answers)
    {
        var session = Guid.NewGuid();
        await db.ExecuteAsync(
            "INSERT INTO dbo.ResponseSession (Id, SurveyId, SubmittedAtUtc) VALUES (@id, @s, '2025-03-01T10:00:00')",
            new SqlParameter("@id", session), new SqlParameter("@s", surveyId));
        foreach (var a in answers)
        {
            await db.ExecuteAsync(
                "INSERT INTO dbo.Answer (SessionId, QuestionId, ScaleValue, BoolValue, TextValue) VALUES (@id, @q, @sc, @b, @t)",
                new SqlParameter("@id", session),
                new SqlParameter("@q", a.QuestionId),
                new SqlParameter("@sc", (object?)a.Scale ?? DBNull.Value),
                new SqlParameter("@b", (object?)a.Bool ?? DBNull.Value),
                new SqlParameter("@t", (object?)a.Text ?? DBNull.Value));
        }

        return session;
    }

    private static async Task<string> ResponsesSnapshotAsync(TempDatabase db)
    {
        var rows = await db.QueryAsync(
            """
            SELECT s.Id, s.SurveyId, CONVERT(VARCHAR(19), s.SubmittedAtUtc, 120), a.QuestionId, a.ScaleValue, a.BoolValue, a.TextValue
            FROM dbo.ResponseSession AS s LEFT JOIN dbo.Answer AS a ON a.SessionId = s.Id
            ORDER BY s.Id, a.QuestionId
            """);
        return string.Join("\n", rows.Select(r => string.Join("|", r.Select(v => v ?? "NULL"))));
    }

    private static async Task<bool> ColumnExistsAsync(TempDatabase db, string table, string column) =>
        await db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(@t) AND name = @c",
            new SqlParameter("@t", $"dbo.{table}"), new SqlParameter("@c", column)) == 1;

    private static async Task<bool> IndexExistsAsync(TempDatabase db, string table, string index) =>
        await db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(@t) AND name = @i",
            new SqlParameter("@t", $"dbo.{table}"), new SqlParameter("@i", index)) == 1;

    [Fact]
    public async Task Upgrade_migrates_legacy_schema()
    {
        await using var db = await TempDatabase.CreateLegacyAsync();
        var surveyId = await LegacySurveyAsync(db, "Stara ankieta", isActive: true);
        var q1 = await LegacyQuestionAsync(db, surveyId, Scale, 1);
        var q2 = await LegacyQuestionAsync(db, surveyId, YesNo, 2);
        var q3 = await LegacyQuestionAsync(db, surveyId, Open, 3);
        await LegacySessionAsync(db, surveyId, (q1, 4, null, null), (q2, null, true, null), (q3, null, null, "Zażółć"));
        await LegacySessionAsync(db, surveyId, (q1, 2, null, null), (q2, null, false, null), (q3, null, null, "Dobrze"));
        var snapshotBefore = await ResponsesSnapshotAsync(db);
        var tokens = await db.QueryAsync("SELECT ResultsAccessToken, ConfigAccessToken FROM dbo.Survey");

        await db.InitializeSchemaAsync();

        var survey = (await db.QueryAsync(
            "SELECT Slug, [Status], IsDefault, Title, ResultsAccessToken, ConfigAccessToken, [Description] FROM dbo.Survey"))[0];
        Assert.Equal("ankieta-1", survey[0]);
        Assert.Equal(2, survey[1]);
        Assert.Equal(true, survey[2]);
        Assert.Equal("Stara ankieta", survey[3]);
        Assert.Equal(tokens[0][0], survey[4]);
        Assert.Equal(tokens[0][1], survey[5]);
        Assert.Null(survey[6]);
        Assert.False(await ColumnExistsAsync(db, "Survey", "IsActive"));
        Assert.Equal(snapshotBefore, await ResponsesSnapshotAsync(db));
        Assert.Equal(2, await db.CountAsync("ResponseSession"));
        Assert.Equal(6, await db.CountAsync("Answer"));

        var tvf = await db.QueryAsync(
            "SELECT QuestionId, IsRequired FROM dbo.fn_GetSurveyQuestions(@s) ORDER BY SortOrder", new SqlParameter("@s", surveyId));
        Assert.Equal(new[] { q1, q2, q3 }, tvf.Select(r => (int)r[0]!));
        Assert.All(tvf, r => Assert.Equal(true, r[1]));
    }

    [Fact]
    public async Task Upgrade_marks_existing_assignments_as_added_from_the_beginning_and_resets_default_afterwards()
    {
        await using var db = await TempDatabase.CreateLegacyAsync();
        var surveyId = await LegacySurveyAsync(db, "Stara", isActive: true);
        await LegacyQuestionAsync(db, surveyId, Scale, 1);
        await LegacyQuestionAsync(db, surveyId, Open, 2);

        await db.InitializeSchemaAsync();

        Assert.Equal(2, await db.CountAsync("SurveyQuestion", "AddedAtUtc = '2000-01-01' AND IsRequired = 1"));

        var newQuestion = await db.InsertQuestionAsync(YesNo);
        await db.ExecuteAsync(
            "INSERT INTO dbo.SurveyQuestion (SurveyId, QuestionId, SortOrder) VALUES (@s, @q, 3)",
            new SqlParameter("@s", surveyId), new SqlParameter("@q", newQuestion));
        var added = await db.ScalarAsync<int>(
            "SELECT DATEDIFF(SECOND, AddedAtUtc, SYSUTCDATETIME()) FROM dbo.SurveyQuestion WHERE QuestionId = @q",
            new SqlParameter("@q", newQuestion));
        Assert.InRange(added, -1, 30);
    }

    [Fact]
    public async Task Upgrade_inactive_survey_becomes_closed()
    {
        await using var db = await TempDatabase.CreateLegacyAsync();
        await LegacySurveyAsync(db, "Wyłączona", isActive: false);

        await db.InitializeSchemaAsync();

        var row = (await db.QueryAsync("SELECT [Status], IsDefault FROM dbo.Survey"))[0];
        Assert.Equal(3, row[0]);
        Assert.Equal(true, row[1]);
    }

    [Fact]
    public async Task Upgrade_multiple_surveys_get_unique_slugs_and_single_default()
    {
        await using var db = await TempDatabase.CreateLegacyAsync();
        await LegacySurveyAsync(db, "Pierwsza nieaktywna", isActive: false);
        await LegacySurveyAsync(db, "Druga aktywna", isActive: true);
        await LegacySurveyAsync(db, "Trzecia aktywna", isActive: true);
        await LegacySurveyAsync(db, "Czwarta nieaktywna", isActive: false);

        await db.InitializeSchemaAsync();

        var rows = await db.QueryAsync("SELECT Id, Slug, [Status], IsDefault FROM dbo.Survey ORDER BY Id");
        Assert.Equal(
            new[] { "ankieta-1", "ankieta-2", "ankieta-3", "ankieta-4" },
            rows.Select(r => (string)r[1]!));
        Assert.Equal(new[] { 3, 2, 2, 3 }, rows.Select(r => (int)r[2]!));
        Assert.Equal(new[] { false, true, false, false }, rows.Select(r => (bool)r[3]!));
    }

    [Fact]
    public async Task Upgrade_picks_earliest_survey_as_default_when_none_is_active()
    {
        await using var db = await TempDatabase.CreateLegacyAsync();
        await LegacySurveyAsync(db, "A", isActive: false);
        await LegacySurveyAsync(db, "B", isActive: false);

        await db.InitializeSchemaAsync();

        var rows = await db.QueryAsync("SELECT [Status], IsDefault FROM dbo.Survey ORDER BY Id");
        Assert.Equal(new[] { true, false }, rows.Select(r => (bool)r[1]!));
        Assert.All(rows, r => Assert.Equal(3, r[0]));
    }

    [Fact]
    public async Task Upgrade_of_legacy_database_without_surveys_creates_seed()
    {
        await using var db = await TempDatabase.CreateLegacyAsync();

        await db.InitializeSchemaAsync();

        Assert.Equal(1, await db.CountAsync("Survey", "Slug = N'ankieta-satysfakcji' AND IsDefault = 1 AND [Status] = 2"));
    }

    [Fact]
    public async Task Upgrade_twice_is_idempotent()
    {
        await using var db = await TempDatabase.CreateLegacyAsync();
        var surveyId = await LegacySurveyAsync(db, "Stara", isActive: true);
        var q1 = await LegacyQuestionAsync(db, surveyId, Scale, 1);
        await LegacySessionAsync(db, surveyId, (q1, 3, null, null));
        await db.InitializeSchemaAsync();
        var schema = await SchemaFingerprint.CaptureAsync(db);
        var snapshot = await ResponsesSnapshotAsync(db);
        var survey = await db.QueryAsync("SELECT Id, Slug, [Status], IsDefault, CAST(RowVersion AS BIGINT) FROM dbo.Survey");

        await db.InitializeSchemaAsync();
        await db.InitializeSchemaAsync();

        Assert.Equal(schema, await SchemaFingerprint.CaptureAsync(db));
        Assert.Equal(snapshot, await ResponsesSnapshotAsync(db));
        var after = await db.QueryAsync("SELECT Id, Slug, [Status], IsDefault, CAST(RowVersion AS BIGINT) FROM dbo.Survey");
        Assert.Equal(survey.Count, after.Count);
        for (var i = 0; i < survey.Count; i++)
        {
            Assert.Equal(survey[i], after[i]);
        }

        Assert.Equal(1, await db.CountAsync("SurveyQuestion"));
    }

    [Fact]
    public async Task Upgrade_result_has_same_schema_as_fresh_database()
    {
        await using var legacy = await TempDatabase.CreateLegacyAsync();
        await LegacySurveyAsync(legacy, "Stara", isActive: true);
        await legacy.InitializeSchemaAsync();
        await using var fresh = await TempDatabase.CreateInitializedAsync();

        Assert.Equal(await SchemaFingerprint.CaptureAsync(fresh), await SchemaFingerprint.CaptureAsync(legacy));
    }

    [Fact]
    public async Task Upgrade_of_real_legacy_seed_keeps_seed_content()
    {
        await using var db = await TempDatabase.CreateLegacyAsync(withLegacySeed: true);

        await db.InitializeSchemaAsync();

        var survey = (await db.QueryAsync(
            "SELECT Slug, [Status], IsDefault, Title, ResultsAccessToken, ConfigAccessToken FROM dbo.Survey"))[0];
        Assert.Equal("ankieta-1", survey[0]);
        Assert.Equal(2, survey[1]);
        Assert.Equal(true, survey[2]);
        Assert.Equal("Ankieta satysfakcji", survey[3]);
        Assert.Equal("wyniki-4f9c2a7e1b3d48a69c5e0d7f2a8b6c13", survey[4]);
        Assert.Equal("konfiguracja-8e1d5b3a9c7f42d6b0a4e2c9f7d1b538", survey[5]);
        Assert.Equal(1, await db.CountAsync("Survey"));
        Assert.Equal(8, await db.CountAsync("Question"));
        Assert.Equal(5, await db.CountAsync("SurveyQuestion", "IsRequired = 1 AND AddedAtUtc = '2000-01-01'"));

        var tvf = await db.QueryAsync("SELECT SortOrder, IsRequired FROM dbo.fn_GetSurveyQuestions(1) ORDER BY SortOrder");
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, tvf.Select(r => (int)r[0]!));
        Assert.All(tvf, r => Assert.Equal(true, r[1]));
    }

    [Fact]
    public async Task Upgraded_database_accepts_responses_through_new_stored_procedure()
    {
        await using var db = await TempDatabase.CreateLegacyAsync(withLegacySeed: true);
        await db.InitializeSchemaAsync();

        var session = await db.SaveResponseAsync(
            1,
            TempDatabase.Json((1, 4, null, null), (2, 5, null, null), (3, null, true, null), (4, null, false, null), (5, null, null, "Nic")));

        Assert.Equal(5, await db.CountAsync("Answer", $"SessionId = '{session}'"));
        await Assert.ThrowsAsync<SqlException>(() => db.SaveResponseAsync(1, TempDatabase.Json((1, 4, null, null))));
    }

    [Fact]
    public async Task Upgrade_failure_on_duplicate_questions_throws_50010_and_leaves_database_unchanged()
    {
        await using var db = await TempDatabase.CreateLegacyAsync();
        var surveyId = await LegacySurveyAsync(db, "Stara", isActive: true);
        await db.ExecuteAsync(
            "INSERT INTO dbo.Question ([Text], [Type]) VALUES (N'Duplikat', 1), (N'DUPLIKAT', 1)");
        var schemaBefore = await SchemaFingerprint.CaptureAsync(db);

        var error = await Assert.ThrowsAsync<SqlException>(() => db.InitializeSchemaAsync());

        Assert.Equal(50010, error.Number);
        Assert.Equal(schemaBefore, await SchemaFingerprint.CaptureAsync(db));
        Assert.True(await ColumnExistsAsync(db, "Survey", "IsActive"));
        Assert.False(await ColumnExistsAsync(db, "Survey", "Slug"));
        Assert.False(await ColumnExistsAsync(db, "SurveyQuestion", "IsRequired"));
        Assert.Equal(1, await db.CountAsync("Survey", $"Id = {surveyId} AND IsActive = 1"));
    }

    [Fact]
    public async Task Upgrade_succeeds_after_duplicates_are_removed()
    {
        await using var db = await TempDatabase.CreateLegacyAsync();
        await LegacySurveyAsync(db, "Stara", isActive: true);
        await db.ExecuteAsync("INSERT INTO dbo.Question ([Text], [Type]) VALUES (N'Duplikat', 1), (N'Duplikat', 1)");
        await Assert.ThrowsAsync<SqlException>(() => db.InitializeSchemaAsync());

        await db.ExecuteAsync("DELETE TOP (1) FROM dbo.Question WHERE [Text] = N'Duplikat'");
        await db.InitializeSchemaAsync();

        Assert.True(await IndexExistsAsync(db, "Question", "UX_Question_Type_Text"));
        Assert.False(await ColumnExistsAsync(db, "Survey", "IsActive"));
    }

    [Fact]
    public async Task Upgrade_failure_after_ddl_rolls_back_everything()
    {
        await using var db = await TempDatabase.CreateLegacyAsync();
        var surveyId = await LegacySurveyAsync(db, "Stara", isActive: true);
        var questionId = await LegacyQuestionAsync(db, surveyId, Open, 1);
        var session = await LegacySessionAsync(db, surveyId, (questionId, null, null, null));
        var schemaBefore = await SchemaFingerprint.CaptureAsync(db);

        var error = await Assert.ThrowsAsync<SqlException>(() => db.InitializeSchemaAsync());

        Assert.Equal(547, error.Number);
        Assert.Equal(schemaBefore, await SchemaFingerprint.CaptureAsync(db));
        Assert.True(await ColumnExistsAsync(db, "Survey", "IsActive"));
        Assert.False(await ColumnExistsAsync(db, "Survey", "Slug"));
        Assert.False(await ColumnExistsAsync(db, "SurveyQuestion", "AddedAtUtc"));
        Assert.False(await IndexExistsAsync(db, "Question", "UX_Question_Type_Text"));
        Assert.Equal(1, await db.CountAsync("Answer", $"SessionId = '{session}'"));

        await db.ExecuteAsync("UPDATE dbo.Answer SET TextValue = N'naprawione'");
        await db.InitializeSchemaAsync();
        Assert.False(await ColumnExistsAsync(db, "Survey", "IsActive"));
        Assert.Equal("ankieta-1", await db.ScalarAsync<string>("SELECT Slug FROM dbo.Survey"));
    }

    [Fact]
    public async Task Upgrade_replaces_legacy_routines()
    {
        await using var db = await TempDatabase.CreateLegacyAsync();
        var surveyId = await LegacySurveyAsync(db, "Stara", isActive: true);
        await LegacyQuestionAsync(db, surveyId, Scale, 1);
        await db.InitializeSchemaAsync();

        Assert.Equal(1, await db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_GetSurveyQuestions') AND name = N'IsRequired'"));
        Assert.Equal(0, await db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.sql_modules WHERE object_id = OBJECT_ID(N'dbo.usp_SaveSurveyResponse') AND definition LIKE N'%IsActive%'"));
    }
}
