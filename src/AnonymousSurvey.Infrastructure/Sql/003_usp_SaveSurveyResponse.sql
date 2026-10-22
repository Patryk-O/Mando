CREATE OR ALTER PROCEDURE dbo.usp_SaveSurveyResponse
    @SurveyId    INT,
    @SessionId   UNIQUEIDENTIFIER,
    @AnswersJson NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.Survey WHERE Id = @SurveyId AND [Status] = 2)
        THROW 50001, N'Ankieta nie istnieje lub nie jest aktywna.', 1;

    IF @AnswersJson IS NULL OR ISJSON(@AnswersJson) <> 1
        THROW 50002, N'Nieprawidłowy format odpowiedzi.', 1;

    BEGIN TRY
        BEGIN TRANSACTION;

        INSERT INTO dbo.ResponseSession (Id, SurveyId, SubmittedAtUtc)
        VALUES (@SessionId, @SurveyId, SYSUTCDATETIME());

        -- TextValue jako NVARCHAR(MAX): OPENJSON z NVARCHAR(2000) cicho ucina dłuższy tekst; z MAX odpowiedź > 2000
        -- zostaje odrzucona przez kolumnę Answer.TextValue (błąd obcięcia), a transakcja się wycofuje.
        INSERT INTO dbo.Answer (SessionId, QuestionId, ScaleValue, BoolValue, TextValue)
        SELECT @SessionId, a.QuestionId, a.ScaleValue, a.BoolValue, a.TextValue
        FROM OPENJSON(@AnswersJson)
        WITH
        (
            QuestionId INT            '$.questionId',
            ScaleValue INT            '$.scaleValue',
            BoolValue  BIT            '$.boolValue',
            TextValue  NVARCHAR(MAX)  '$.textValue'
        ) AS a
        INNER JOIN dbo.fn_GetSurveyQuestions(@SurveyId) AS q ON q.QuestionId = a.QuestionId;

        IF (SELECT COUNT(*) FROM OPENJSON(@AnswersJson))
           <> (SELECT COUNT(*) FROM dbo.Answer WHERE SessionId = @SessionId)
            THROW 50003, N'Odpowiedzi zawierają pytanie spoza ankiety.', 1;

        IF EXISTS
        (
            SELECT 1
            FROM dbo.fn_GetSurveyQuestions(@SurveyId) AS q
            WHERE q.IsRequired = 1
              AND NOT EXISTS (SELECT 1 FROM dbo.Answer AS a
                              WHERE a.SessionId = @SessionId AND a.QuestionId = q.QuestionId)
        )
            THROW 50004, N'Brak odpowiedzi na pytanie wymagane.', 1;

        IF NOT EXISTS (SELECT 1 FROM dbo.Answer WHERE SessionId = @SessionId)
            THROW 50005, N'Ankieta nie zawiera żadnej odpowiedzi.', 1;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;

        THROW;
    END CATCH;
END;
