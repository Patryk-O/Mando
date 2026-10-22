SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.Survey', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Survey
    (
        Id                 INT IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Survey PRIMARY KEY,
        Slug               NVARCHAR(100) NOT NULL,
        Title              NVARCHAR(200) NOT NULL,
        [Description]      NVARCHAR(1000) NULL,
        [Status]           INT NOT NULL CONSTRAINT DF_Survey_Status DEFAULT (1)
                           CONSTRAINT CK_Survey_Status CHECK ([Status] IN (1, 2, 3)),
        IsDefault          BIT NOT NULL CONSTRAINT DF_Survey_IsDefault DEFAULT (0),
        CreatedAtUtc       DATETIME2(0) NOT NULL CONSTRAINT DF_Survey_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
        UpdatedAtUtc       DATETIME2(0) NOT NULL CONSTRAINT DF_Survey_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
        RowVersion         ROWVERSION NOT NULL,
        ResultsAccessToken NVARCHAR(64) NOT NULL,
        ConfigAccessToken  NVARCHAR(64) NOT NULL
    );

    -- EXEC: przy starej tabeli Survey (upgrade) kolumny Slug/IsDefault jeszcze nie istnieją, a cały skrypt kompiluje się naraz.
    EXEC (N'
        CREATE UNIQUE INDEX UX_Survey_Slug ON dbo.Survey (Slug);
        CREATE UNIQUE INDEX UX_Survey_IsDefault ON dbo.Survey (IsDefault) WHERE IsDefault = 1;
        CREATE UNIQUE INDEX UX_Survey_ResultsAccessToken ON dbo.Survey (ResultsAccessToken);
        CREATE UNIQUE INDEX UX_Survey_ConfigAccessToken ON dbo.Survey (ConfigAccessToken);
    ');
END;

IF OBJECT_ID(N'dbo.Question', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Question
    (
        Id       INT IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Question PRIMARY KEY,
        [Text]   NVARCHAR(500) NOT NULL,
        [Type]   INT NOT NULL CONSTRAINT CK_Question_Type CHECK ([Type] IN (1, 2, 3)),
        IsActive BIT NOT NULL CONSTRAINT DF_Question_IsActive DEFAULT (1)
    );

    CREATE UNIQUE INDEX UX_Question_Type_Text ON dbo.Question ([Type], [Text]);
END;

IF OBJECT_ID(N'dbo.SurveyQuestion', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SurveyQuestion
    (
        SurveyId   INT NOT NULL,
        QuestionId INT NOT NULL,
        SortOrder  INT NOT NULL,
        IsRequired BIT NOT NULL CONSTRAINT DF_SurveyQuestion_IsRequired DEFAULT (1),
        AddedAtUtc DATETIME2(0) NOT NULL CONSTRAINT DF_SurveyQuestion_AddedAtUtc DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_SurveyQuestion PRIMARY KEY (SurveyId, QuestionId),
        CONSTRAINT FK_SurveyQuestion_Survey FOREIGN KEY (SurveyId) REFERENCES dbo.Survey (Id),
        CONSTRAINT FK_SurveyQuestion_Question FOREIGN KEY (QuestionId) REFERENCES dbo.Question (Id)
    );
END;

IF OBJECT_ID(N'dbo.ResponseSession', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ResponseSession
    (
        Id             UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ResponseSession PRIMARY KEY,
        SurveyId       INT NOT NULL,
        SubmittedAtUtc DATETIME2(0) NOT NULL,
        CONSTRAINT FK_ResponseSession_Survey FOREIGN KEY (SurveyId) REFERENCES dbo.Survey (Id)
    );

    CREATE INDEX IX_ResponseSession_SurveyId ON dbo.ResponseSession (SurveyId);
END;

IF OBJECT_ID(N'dbo.Answer', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Answer
    (
        SessionId  UNIQUEIDENTIFIER NOT NULL,
        QuestionId INT NOT NULL,
        ScaleValue INT NULL,
        BoolValue  BIT NULL,
        TextValue  NVARCHAR(2000) NULL,
        CONSTRAINT PK_Answer PRIMARY KEY (SessionId, QuestionId),
        CONSTRAINT FK_Answer_ResponseSession FOREIGN KEY (SessionId) REFERENCES dbo.ResponseSession (Id),
        CONSTRAINT FK_Answer_Question FOREIGN KEY (QuestionId) REFERENCES dbo.Question (Id),
        CONSTRAINT CK_Answer_ScaleValue CHECK (ScaleValue IS NULL OR ScaleValue BETWEEN 1 AND 5),
        CONSTRAINT CK_Answer_HasValue CHECK (ScaleValue IS NOT NULL OR BoolValue IS NOT NULL OR TextValue IS NOT NULL)
    );
END;

-- Upgrade starego schematu (Survey.IsActive, bez Slug/Status/IsRequired/AddedAtUtc) do aktualnego kształtu.
-- Idempotentny i atomowy: albo wszystkie kroki, albo żaden (DDL w SQL Server jest transakcyjny).
-- Kroki odwołujące się do nowo dodanych kolumn są w EXEC, bo skrypt jest jednym batchem (bez GO).
IF COL_LENGTH(N'dbo.Survey', N'Slug') IS NULL
   OR COL_LENGTH(N'dbo.SurveyQuestion', N'IsRequired') IS NULL
   OR COL_LENGTH(N'dbo.SurveyQuestion', N'AddedAtUtc') IS NULL
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Question_Type_Text' AND object_id = OBJECT_ID(N'dbo.Question'))
   OR NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Answer_HasValue' AND parent_object_id = OBJECT_ID(N'dbo.Answer'))
BEGIN
    BEGIN TRY
        BEGIN TRANSACTION;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Question_Type_Text' AND object_id = OBJECT_ID(N'dbo.Question'))
           AND EXISTS (SELECT 1 FROM dbo.Question GROUP BY [Type], [Text] HAVING COUNT(*) > 1)
            THROW 50010, N'Duplikaty pytań w puli — usuń lub scal je przed upgrade''em.', 1;

        IF COL_LENGTH(N'dbo.Survey', N'Slug') IS NULL
        BEGIN
            ALTER TABLE dbo.Survey ADD
                Slug          NVARCHAR(100) NULL,
                [Description] NVARCHAR(1000) NULL,
                [Status]      INT NOT NULL CONSTRAINT DF_Survey_Status DEFAULT (1),
                IsDefault     BIT NOT NULL CONSTRAINT DF_Survey_IsDefault DEFAULT (0),
                CreatedAtUtc  DATETIME2(0) NOT NULL CONSTRAINT DF_Survey_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
                UpdatedAtUtc  DATETIME2(0) NOT NULL CONSTRAINT DF_Survey_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
                RowVersion    ROWVERSION NOT NULL;

            EXEC (N'
                UPDATE dbo.Survey
                SET [Status] = CASE WHEN IsActive = 1 THEN 2 ELSE 3 END,
                    Slug = CONCAT(N''ankieta-'', Id);

                UPDATE dbo.Survey SET IsDefault = 1
                WHERE Id = (SELECT TOP (1) Id FROM dbo.Survey ORDER BY CASE WHEN IsActive = 1 THEN 0 ELSE 1 END, Id);

                ALTER TABLE dbo.Survey ALTER COLUMN Slug NVARCHAR(100) NOT NULL;
                ALTER TABLE dbo.Survey ADD CONSTRAINT CK_Survey_Status CHECK ([Status] IN (1, 2, 3));
                CREATE UNIQUE INDEX UX_Survey_Slug ON dbo.Survey (Slug);
                CREATE UNIQUE INDEX UX_Survey_IsDefault ON dbo.Survey (IsDefault) WHERE IsDefault = 1;
                ALTER TABLE dbo.Survey DROP CONSTRAINT DF_Survey_IsActive;
                ALTER TABLE dbo.Survey DROP COLUMN IsActive;
            ');
        END;

        IF COL_LENGTH(N'dbo.SurveyQuestion', N'IsRequired') IS NULL
            ALTER TABLE dbo.SurveyQuestion ADD IsRequired BIT NOT NULL
                CONSTRAINT DF_SurveyQuestion_IsRequired DEFAULT (1);

        IF COL_LENGTH(N'dbo.SurveyQuestion', N'AddedAtUtc') IS NULL
        BEGIN
            -- Istniejące przypisania liczą się „od początku”: wszystkie dotychczasowe odpowiedzi wchodzą do mianownika „X z N”.
            ALTER TABLE dbo.SurveyQuestion ADD AddedAtUtc DATETIME2(0) NOT NULL
                CONSTRAINT DF_SurveyQuestion_AddedAtUtc DEFAULT ('2000-01-01');

            EXEC (N'
                ALTER TABLE dbo.SurveyQuestion DROP CONSTRAINT DF_SurveyQuestion_AddedAtUtc;
                ALTER TABLE dbo.SurveyQuestion ADD CONSTRAINT DF_SurveyQuestion_AddedAtUtc DEFAULT (SYSUTCDATETIME()) FOR AddedAtUtc;
            ');
        END;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Question_Type_Text' AND object_id = OBJECT_ID(N'dbo.Question'))
            CREATE UNIQUE INDEX UX_Question_Type_Text ON dbo.Question ([Type], [Text]);

        IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Answer_HasValue' AND parent_object_id = OBJECT_ID(N'dbo.Answer'))
            ALTER TABLE dbo.Answer WITH CHECK ADD CONSTRAINT CK_Answer_HasValue
                CHECK (ScaleValue IS NOT NULL OR BoolValue IS NOT NULL OR TextValue IS NOT NULL);

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;

        THROW;
    END CATCH;
END;
