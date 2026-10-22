SET NOCOUNT ON;

IF NOT EXISTS (SELECT 1 FROM dbo.Survey)
BEGIN
    BEGIN TRANSACTION;

    DECLARE @SurveyId INT;

    -- SortOrder = NULL oznacza pytanie z puli, domyślnie nieprzypisane do ankiety (do wyboru w konfiguracji).
    DECLARE @SeedQuestions TABLE
    (
        Ord       INT IDENTITY(1, 1) NOT NULL,
        [Text]    NVARCHAR(500) NOT NULL,
        [Type]    INT NOT NULL,
        SortOrder INT NULL
    );

    INSERT INTO @SeedQuestions ([Text], [Type], SortOrder)
    VALUES (N'Jak oceniasz ogólne wrażenia z korzystania z naszej usługi?', 1, 1),
           (N'Jak oceniasz jakość obsługi?', 1, 2),
           (N'Czy poleciłbyś nas znajomym?', 3, 3),
           (N'Czy znalazłeś wszystko, czego szukałeś?', 3, 4),
           (N'Co moglibyśmy zrobić lepiej?', 2, 5),
           (N'Jak oceniasz czytelność naszej strony?', 1, NULL),
           (N'Czy chcesz dodać dodatkowe uwagi?', 2, NULL),
           (N'Czy skorzystasz z naszej usługi ponownie?', 3, NULL);

    INSERT INTO dbo.Survey (Slug, Title, [Status], IsDefault, ResultsAccessToken, ConfigAccessToken)
    VALUES (N'ankieta-satysfakcji', N'Ankieta satysfakcji', 2, 1,
            N'wyniki-4f9c2a7e1b3d48a69c5e0d7f2a8b6c13', N'konfiguracja-8e1d5b3a9c7f42d6b0a4e2c9f7d1b538');

    SET @SurveyId = SCOPE_IDENTITY();

    -- Pula pytań jest unikalna po (Type, Text); gdy pytanie już istnieje, używamy go zamiast dublować.
    INSERT INTO dbo.Question ([Text], [Type], IsActive)
    SELECT s.[Text], s.[Type], 1
    FROM @SeedQuestions AS s
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Question AS q WHERE q.[Type] = s.[Type] AND q.[Text] = s.[Text])
    ORDER BY s.Ord;

    INSERT INTO dbo.SurveyQuestion (SurveyId, QuestionId, SortOrder, IsRequired, AddedAtUtc)
    SELECT @SurveyId, q.Id, s.SortOrder, 1, '2000-01-01'
    FROM @SeedQuestions AS s
    INNER JOIN dbo.Question AS q ON q.[Type] = s.[Type] AND q.[Text] = s.[Text]
    WHERE s.SortOrder IS NOT NULL;

    COMMIT TRANSACTION;
END;
