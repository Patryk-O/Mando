import { rows, scalar } from './db';

export const SEED_SLUG = 'ankieta-satysfakcji';
export const SEED_CONFIG = '/konfiguracja/konfiguracja-8e1d5b3a9c7f42d6b0a4e2c9f7d1b538';
export const SEED_RESULTS = '/wyniki/wyniki-4f9c2a7e1b3d48a69c5e0d7f2a8b6c13';

/** Pięć pytań przypisanych w 004_Seed.sql, w oryginalnej kolejności. */
export const SEED_QUESTIONS = [
  'Jak oceniasz ogólne wrażenia z korzystania z naszej usługi?',
  'Jak oceniasz jakość obsługi?',
  'Czy poleciłbyś nas znajomym?',
  'Czy znalazłeś wszystko, czego szukałeś?',
  'Co moglibyśmy zrobić lepiej?',
];

function quote(value: string): string {
  return `N'${value.replaceAll("'", "''")}'`;
}

export function seedSurveyId(): number {
  return scalar(`SELECT Id FROM dbo.Survey WHERE Slug = N'${SEED_SLUG}'`);
}

/**
 * Przywraca ankietę z seeda: pięć oryginalnych pytań jako wymagane, status Aktywna, flaga domyślna.
 * Baza E2E i tak jest usuwana przez run-e2e.ps1; to sprzątanie jest po to, żeby kolejne pliki
 * w tym samym przebiegu widziały seed w stanie początkowym.
 */
export function restoreSeedSurvey(): void {
  const items = SEED_QUESTIONS.map(
    (text, index) => `SELECT ${quote(text)} AS [Text], ${index + 1} AS SortOrder`,
  ).join(' UNION ALL ');

  rows(`SET QUOTED_IDENTIFIER ON;
DECLARE @SurveyId INT = (SELECT Id FROM dbo.Survey WHERE Slug = N'${SEED_SLUG}');
IF @SurveyId IS NULL
    THROW 50000, N'Brak ankiety seedowej.', 1;

DELETE FROM dbo.SurveyQuestion WHERE SurveyId = @SurveyId;

DECLARE @Items TABLE ([Text] NVARCHAR(500) NOT NULL, SortOrder INT NOT NULL);
INSERT INTO @Items ([Text], SortOrder)
${items};

INSERT INTO dbo.SurveyQuestion (SurveyId, QuestionId, SortOrder, IsRequired, AddedAtUtc)
SELECT @SurveyId, q.Id, i.SortOrder, 1, '2000-01-01'
FROM @Items AS i
INNER JOIN dbo.Question AS q ON q.[Text] = i.[Text];

IF (SELECT COUNT(*) FROM dbo.SurveyQuestion WHERE SurveyId = @SurveyId) <> ${SEED_QUESTIONS.length}
    THROW 50000, N'Nie udało się przywrócić pytań seedowych.', 1;

UPDATE dbo.Survey SET IsDefault = 0;
UPDATE dbo.Survey SET IsDefault = 1, [Status] = 2 WHERE Id = @SurveyId;
`);
}
