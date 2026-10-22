CREATE OR ALTER FUNCTION dbo.fn_GetSurveyQuestions (@SurveyId INT)
RETURNS TABLE
AS
RETURN
(
    SELECT
        sq.SurveyId,
        q.Id AS QuestionId,
        q.[Text],
        q.[Type],
        sq.SortOrder,
        sq.IsRequired
    FROM dbo.SurveyQuestion AS sq
    INNER JOIN dbo.Question AS q ON q.Id = sq.QuestionId
    WHERE sq.SurveyId = @SurveyId
      AND q.IsActive = 1
);
