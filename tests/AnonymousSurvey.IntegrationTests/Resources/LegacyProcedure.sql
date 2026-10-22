CREATE OR ALTER PROCEDURE dbo.usp_SaveSurveyResponse
    @SurveyId    INT,
    @SessionId   UNIQUEIDENTIFIER,
    @AnswersJson NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.Survey WHERE Id = @SurveyId AND IsActive = 1)
        THROW 50001, N'Ankieta nie istnieje lub jest nieaktywna.', 1;

    IF @AnswersJson IS NULL OR ISJSON(@AnswersJson) <> 1
        THROW 50002, N'Nieprawidłowy format odpowiedzi.', 1;

    BEGIN TRY
        BEGIN TRANSACTION;

        INSERT INTO dbo.ResponseSession (Id, SurveyId, SubmittedAtUtc)
        VALUES (@SessionId, @SurveyId, SYSUTCDATETIME());

        INSERT INTO dbo.Answer (SessionId, QuestionId, ScaleValue, BoolValue, TextValue)
        SELECT @SessionId, a.QuestionId, a.ScaleValue, a.BoolValue, a.TextValue
        FROM OPENJSON(@AnswersJson)
        WITH
        (
            QuestionId INT            '$.questionId',
            ScaleValue INT            '$.scaleValue',
            BoolValue  BIT            '$.boolValue',
            TextValue  NVARCHAR(2000) '$.textValue'
        ) AS a
        INNER JOIN dbo.fn_GetSurveyQuestions(@SurveyId) AS q ON q.QuestionId = a.QuestionId;

        IF (SELECT COUNT(*) FROM dbo.Answer WHERE SessionId = @SessionId)
           <> (SELECT COUNT(*) FROM dbo.fn_GetSurveyQuestions(@SurveyId))
           OR (SELECT COUNT(*) FROM OPENJSON(@AnswersJson))
           <> (SELECT COUNT(*) FROM dbo.Answer WHERE SessionId = @SessionId)
            THROW 50003, N'Odpowiedzi nie pasują do pytań ankiety.', 1;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;

        THROW;
    END CATCH;
END;
