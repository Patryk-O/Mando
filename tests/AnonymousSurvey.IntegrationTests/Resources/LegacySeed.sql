SET NOCOUNT ON;

IF NOT EXISTS (SELECT 1 FROM dbo.Survey)
BEGIN
    BEGIN TRANSACTION;

    DECLARE @SurveyId INT;
    DECLARE @Q1 INT, @Q2 INT, @Q3 INT, @Q4 INT, @Q5 INT;

    INSERT INTO dbo.Survey (Title, IsActive, ResultsAccessToken, ConfigAccessToken)
    VALUES (N'Ankieta satysfakcji', 1, N'wyniki-4f9c2a7e1b3d48a69c5e0d7f2a8b6c13', N'konfiguracja-8e1d5b3a9c7f42d6b0a4e2c9f7d1b538');

    SET @SurveyId = SCOPE_IDENTITY();

    INSERT INTO dbo.Question ([Text], [Type], IsActive) VALUES (N'Jak oceniasz ogólne wrażenia z korzystania z naszej usługi?', 1, 1);
    SET @Q1 = SCOPE_IDENTITY();
    INSERT INTO dbo.Question ([Text], [Type], IsActive) VALUES (N'Jak oceniasz jakość obsługi?', 1, 1);
    SET @Q2 = SCOPE_IDENTITY();
    INSERT INTO dbo.Question ([Text], [Type], IsActive) VALUES (N'Czy poleciłbyś nas znajomym?', 3, 1);
    SET @Q3 = SCOPE_IDENTITY();
    INSERT INTO dbo.Question ([Text], [Type], IsActive) VALUES (N'Czy znalazłeś wszystko, czego szukałeś?', 3, 1);
    SET @Q4 = SCOPE_IDENTITY();
    INSERT INTO dbo.Question ([Text], [Type], IsActive) VALUES (N'Co moglibyśmy zrobić lepiej?', 2, 1);
    SET @Q5 = SCOPE_IDENTITY();

    -- Pytania w puli, domyślnie nieprzypisane do ankiety (do wyboru w konfiguracji).
    INSERT INTO dbo.Question ([Text], [Type], IsActive) VALUES (N'Jak oceniasz czytelność naszej strony?', 1, 1);
    INSERT INTO dbo.Question ([Text], [Type], IsActive) VALUES (N'Czy chcesz dodać dodatkowe uwagi?', 2, 1);
    INSERT INTO dbo.Question ([Text], [Type], IsActive) VALUES (N'Czy skorzystasz z naszej usługi ponownie?', 3, 1);

    INSERT INTO dbo.SurveyQuestion (SurveyId, QuestionId, SortOrder)
    VALUES (@SurveyId, @Q1, 1),
           (@SurveyId, @Q2, 2),
           (@SurveyId, @Q3, 3),
           (@SurveyId, @Q4, 4),
           (@SurveyId, @Q5, 5);

    COMMIT TRANSACTION;
END;
