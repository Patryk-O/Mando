SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.Survey', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Survey
    (
        Id                 INT IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Survey PRIMARY KEY,
        Title              NVARCHAR(200) NOT NULL,
        IsActive           BIT NOT NULL CONSTRAINT DF_Survey_IsActive DEFAULT (1),
        ResultsAccessToken NVARCHAR(64) NOT NULL,
        ConfigAccessToken  NVARCHAR(64) NOT NULL
    );

    CREATE UNIQUE INDEX UX_Survey_ResultsAccessToken ON dbo.Survey (ResultsAccessToken);
    CREATE UNIQUE INDEX UX_Survey_ConfigAccessToken ON dbo.Survey (ConfigAccessToken);
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
END;

IF OBJECT_ID(N'dbo.SurveyQuestion', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SurveyQuestion
    (
        SurveyId   INT NOT NULL,
        QuestionId INT NOT NULL,
        SortOrder  INT NOT NULL,
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
        CONSTRAINT CK_Answer_ScaleValue CHECK (ScaleValue IS NULL OR ScaleValue BETWEEN 1 AND 5)
    );
END;
