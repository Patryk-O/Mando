import { type Locator, type Page } from '@playwright/test';
import { test, expect } from '../fixtures';
import { rows, scalar } from '../db';

const SUBMIT = 'Wyślij odpowiedzi';
const SCRIPT = '<script>alert(1)</script>';

function quote(value: string): string {
  return `N'${value.replaceAll("'", "''")}'`;
}

/** Filtered index na Survey.IsDefault wymaga QUOTED_IDENTIFIER, którego sam sqlcmd nie włącza. */
function exec(query: string): void {
  rows(`SET QUOTED_IDENTIFIER ON; ${query}`);
}

function createSurvey(options: {
  slug: string;
  title: string;
  description?: string;
  status?: number;
  createdAt?: string;
  items: { text: string; type: number; required: boolean }[];
}): number {
  const description = options.description == null ? 'NULL' : quote(options.description);
  const createdAt = options.createdAt == null ? 'SYSUTCDATETIME()' : quote(options.createdAt);
  const items = options.items
    .map(
      (item, index) =>
        `SELECT ${quote(item.text)} AS [Text], ${item.type} AS [Type], ${index + 1} AS SortOrder, ${item.required ? 1 : 0} AS IsRequired`,
    )
    .join(' UNION ALL ');

  return scalar(`SET QUOTED_IDENTIFIER ON;
DECLARE @SurveyId INT;
INSERT INTO dbo.Survey (Slug, Title, [Description], [Status], IsDefault, CreatedAtUtc, ResultsAccessToken, ConfigAccessToken)
VALUES (${quote(options.slug)}, ${quote(options.title)}, ${description}, ${options.status ?? 2}, 0, ${createdAt}, ${quote(`wyniki-${options.slug}`)}, ${quote(`konfiguracja-${options.slug}`)});
SET @SurveyId = SCOPE_IDENTITY();

DECLARE @Items TABLE ([Text] NVARCHAR(500) NOT NULL, [Type] INT NOT NULL, SortOrder INT NOT NULL, IsRequired BIT NOT NULL);
INSERT INTO @Items ([Text], [Type], SortOrder, IsRequired)
${items};

INSERT INTO dbo.Question ([Text], [Type], IsActive)
SELECT i.[Text], i.[Type], 1
FROM @Items AS i
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.Question AS q WHERE q.[Type] = i.[Type] AND q.[Text] = i.[Text]
);

INSERT INTO dbo.SurveyQuestion (SurveyId, QuestionId, SortOrder, IsRequired, AddedAtUtc)
SELECT @SurveyId, q.Id, i.SortOrder, i.IsRequired, '2000-01-01'
FROM @Items AS i
INNER JOIN dbo.Question AS q ON q.[Type] = i.[Type] AND q.[Text] = i.[Text];

SELECT @SurveyId;
`);
}

function countSessions(surveyId: number): number {
  return scalar(`SELECT COUNT(*) FROM dbo.ResponseSession WHERE SurveyId = ${surveyId}`);
}

function card(page: Page, text: string): Locator {
  return page.locator('.question-card', { hasText: text });
}

async function chooseScale(page: Page, text: string, value: number): Promise<void> {
  await card(page, text).locator('.question-card__body').getByText(String(value), { exact: true }).click();
}

async function chooseYesNo(page: Page, text: string, label: 'Tak' | 'Nie'): Promise<void> {
  await card(page, text).locator('.question-card__body').getByText(label, { exact: true }).click();
}

async function openSurvey(page: Page, slug: string): Promise<void> {
  await page.goto(`/ankieta/${slug}`);
  await expect(page.locator('.question-card').first()).toBeVisible();
}

async function expectChoice(page: Page, text: string, label: string, checked: boolean): Promise<void> {
  const radio = card(page, text).getByRole('radio', { name: label, exact: true });
  if (checked) {
    await expect(radio).toBeChecked();
  } else {
    await expect(radio).not.toBeChecked();
  }
}

test('strona główna przekierowuje do ankiety domyślnej', async ({ page }) => {
  await page.goto('/');
  await expect(page).toHaveURL(/\/ankieta\/ankieta-satysfakcji$/);
  await expect(page.locator('.question-card')).toHaveCount(5);
  await expect(page.getByText('Pola oznaczone * są wymagane.')).toBeVisible();
});

test('slug w adresie nie jest przekierowywany na małe litery', async ({ page }) => {
  await page.goto('/ankieta/ANKIETA-SATYSFAKCJI');
  await expect(page).toHaveURL(/\/ankieta\/ANKIETA-SATYSFAKCJI$/);
  await expect(page.locator('.question-card')).toHaveCount(5);
  await expect(page.locator('h1')).toHaveText('Ankieta satysfakcji');
});

test('nieznany slug i szkic pokazują nie znaleziono', async ({ page }) => {
  createSurvey({
    slug: 'e-szkic',
    title: 'Szkic E',
    status: 1,
    items: [{ text: 'E szkic skala', type: 1, required: true }],
  });

  for (const path of ['/ankieta/nie-ma-takiej-ankiety', '/ankieta/e-szkic']) {
    await page.goto(path);
    await expect(page.locator('.app-main')).toContainText(/Nie znaleziono/);
    await expect(page.locator('.question-card')).toHaveCount(0);
    await expect(page.getByRole('button', { name: SUBMIT })).toHaveCount(0);
  }
});

test('zamknięta ankieta pokazuje komunikat bez formularza', async ({ page }) => {
  createSurvey({
    slug: 'e-zamknieta',
    title: 'Zamknięta E',
    status: 3,
    items: [{ text: 'E zamknieta skala', type: 1, required: true }],
  });

  await page.goto('/ankieta/e-zamknieta');
  await expect(page.locator('.state-box')).toContainText(
    'Ta ankieta jest zamknięta i nie przyjmuje już odpowiedzi.',
  );
  await expect(page.locator('.question-card')).toHaveCount(0);
  await expect(page.getByRole('button', { name: SUBMIT })).toHaveCount(0);
});

test('gwiazdki, Wyczyść i pominięcie opcjonalnego pytania', async ({ page }) => {
  const surveyId = createSurvey({
    slug: 'e-wymagane',
    title: 'Wymagane E',
    description: 'Opis publicznej ankiety',
    items: [
      { text: 'E wymagana skala', type: 1, required: true },
      { text: 'E opcjonalna skala', type: 1, required: false },
      { text: 'E opcjonalne taknie', type: 3, required: false },
      { text: 'E opcjonalne otwarte', type: 2, required: false },
    ],
  });

  await openSurvey(page, 'e-wymagane');
  await expect(page.getByText('Opis publicznej ankiety')).toBeVisible();
  await expect(page.locator('.question-card__required')).toHaveCount(1);
  await expect(card(page, 'E wymagana skala').locator('.question-card__required')).toHaveCount(1);
  await expect(card(page, 'E wymagana skala').getByRole('button', { name: 'Wyczyść' })).toHaveCount(0);
  await expect(card(page, 'E opcjonalna skala').getByRole('button', { name: 'Wyczyść' })).toHaveCount(1);
  await expect(card(page, 'E opcjonalne taknie').getByRole('button', { name: 'Wyczyść' })).toHaveCount(1);
  await expect(card(page, 'E opcjonalne otwarte').getByRole('button', { name: 'Wyczyść' })).toHaveCount(0);

  await chooseScale(page, 'E opcjonalna skala', 3);
  await expectChoice(page, 'E opcjonalna skala', '3', true);
  await card(page, 'E opcjonalna skala').getByRole('button', { name: 'Wyczyść' }).click();
  await expectChoice(page, 'E opcjonalna skala', '3', false);

  await chooseYesNo(page, 'E opcjonalne taknie', 'Tak');
  await expectChoice(page, 'E opcjonalne taknie', 'Tak', true);
  await card(page, 'E opcjonalne taknie').getByRole('button', { name: 'Wyczyść' }).click();
  await expectChoice(page, 'E opcjonalne taknie', 'Tak', false);

  await page.getByRole('button', { name: SUBMIT }).click();
  await expect(page.locator('.state-box--error')).toBeVisible();
  await expect(page).not.toHaveURL(/dziekujemy/);
  expect(countSessions(surveyId)).toBe(0);

  await chooseScale(page, 'E wymagana skala', 4);
  await page.getByRole('button', { name: SUBMIT }).click();
  await expect(page).toHaveURL(/\/ankieta\/e-wymagane\/dziekujemy$/);
  expect(countSessions(surveyId)).toBe(1);
  expect(
    scalar(
      `SELECT COUNT(*) FROM dbo.Answer AS a INNER JOIN dbo.ResponseSession AS s ON s.Id = a.SessionId WHERE s.SurveyId = ${surveyId}`,
    ),
  ).toBe(1);
});

test('ankieta z samych opcjonalnych: pusta jest odrzucona, jedna odpowiedź przechodzi', async ({ page }) => {
  const surveyId = createSurvey({
    slug: 'e-opcjonalne',
    title: 'Opcjonalne E',
    items: [
      { text: 'E sama skala', type: 1, required: false },
      { text: 'E same taknie', type: 3, required: false },
      { text: 'E same otwarte', type: 2, required: false },
    ],
  });

  await openSurvey(page, 'e-opcjonalne');
  await expect(page.locator('.question-card__required')).toHaveCount(0);
  await page.getByRole('button', { name: SUBMIT }).click();
  await expect(page.locator('.state-box--error')).toContainText('Odpowiedz na co najmniej jedno pytanie.');
  expect(countSessions(surveyId)).toBe(0);

  await chooseYesNo(page, 'E same taknie', 'Nie');
  await page.getByRole('button', { name: SUBMIT }).click();
  await expect(page).toHaveURL(/\/ankieta\/e-opcjonalne\/dziekujemy$/);
  expect(countSessions(surveyId)).toBe(1);
  expect(
    scalar(
      `SELECT COUNT(*) FROM dbo.Answer AS a INNER JOIN dbo.ResponseSession AS s ON s.Id = a.SessionId WHERE s.SurveyId = ${surveyId}`,
    ),
  ).toBe(1);
});

test('podwójny klik Wyślij zapisuje dokładnie jedną odpowiedź', async ({ page }) => {
  const surveyId = createSurvey({
    slug: 'e-podwojny',
    title: 'Podwójny E',
    items: [{ text: 'E podwojna skala', type: 1, required: true }],
  });

  await openSurvey(page, 'e-podwojny');
  await chooseScale(page, 'E podwojna skala', 5);
  await page.evaluate(() => {
    const button = [...document.querySelectorAll('button')].find((element) =>
      (element.textContent ?? '').includes('Wyślij odpowiedzi'),
    );
    if (!button) {
      throw new Error('Nie znaleziono przycisku Wyślij odpowiedzi.');
    }
    button.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
    button.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
  });

  await expect(page).toHaveURL(/\/ankieta\/e-podwojny\/dziekujemy$/);
  expect(countSessions(surveyId)).toBe(1);
});

test('odświeżenie podziękowania, Wstecz i stare /dziekujemy nie dublują odpowiedzi', async ({ page }) => {
  const surveyId = createSurvey({
    slug: 'e-dziekujemy',
    title: 'Dziękujemy E',
    items: [{ text: 'E dziekujemy skala', type: 1, required: true }],
  });

  await openSurvey(page, 'e-dziekujemy');
  await chooseScale(page, 'E dziekujemy skala', 2);
  await page.getByRole('button', { name: SUBMIT }).click();
  await expect(page).toHaveURL(/\/ankieta\/e-dziekujemy\/dziekujemy$/);
  expect(countSessions(surveyId)).toBe(1);

  await page.reload();
  await expect(page.getByText('Twoje odpowiedzi zostały zapisane anonimowo.')).toBeVisible();
  expect(countSessions(surveyId)).toBe(1);

  await page.goBack();
  await expect(page.locator('.question-card').first()).toBeVisible();
  await expect.poll(() => countSessions(surveyId)).toBe(1);

  await page.goto('/dziekujemy');
  await expect(page).toHaveURL(/\/ankieta\/ankieta-satysfakcji$/);
  expect(countSessions(surveyId)).toBe(1);
});

test('zamknięcie ankiety w trakcie wypełniania zastępuje formularz i nie zapisuje', async ({ page }) => {
  const surveyId = createSurvey({
    slug: 'e-zamykana',
    title: 'Zamykana E',
    items: [{ text: 'E zamykana skala', type: 1, required: true }],
  });

  await openSurvey(page, 'e-zamykana');
  await chooseScale(page, 'E zamykana skala', 4);
  exec(`UPDATE dbo.Survey SET [Status] = 3 WHERE Id = ${surveyId}`);
  await page.getByRole('button', { name: SUBMIT }).click();

  await expect(page.locator('.state-box')).toContainText(/zamknięta/i);
  await expect(page.locator('.question-card')).toHaveCount(0);
  expect(countSessions(surveyId)).toBe(0);
});

test('usunięcie pytania w trakcie wypełniania zachowuje pozostałe odpowiedzi i nie zapisuje', async ({ page }) => {
  const surveyId = createSurvey({
    slug: 'e-usuniete',
    title: 'Usuwane E',
    items: [
      { text: 'E usuń skala', type: 1, required: true },
      { text: 'E usuń taknie', type: 3, required: true },
    ],
  });

  await openSurvey(page, 'e-usuniete');
  await chooseScale(page, 'E usuń skala', 4);
  await chooseYesNo(page, 'E usuń taknie', 'Tak');
  exec(`
DELETE sq
FROM dbo.SurveyQuestion AS sq
INNER JOIN dbo.Question AS q ON q.Id = sq.QuestionId
WHERE sq.SurveyId = ${surveyId} AND q.[Text] = N'E usuń taknie';
`);
  await page.getByRole('button', { name: SUBMIT }).click();

  await expect(page.locator('.state-box--error')).toContainText('zmieniona w trakcie wypełniania');
  await expect(card(page, 'E usuń taknie')).toHaveCount(0);
  await expectChoice(page, 'E usuń skala', '4', true);
  expect(countSessions(surveyId)).toBe(0);
});

test('oznaczenie pytania jako wymagane w trakcie wypełniania zostawia wpisane odpowiedzi', async ({ page }) => {
  const surveyId = createSurvey({
    slug: 'e-flaga',
    title: 'Flaga E',
    items: [
      { text: 'E flaga skala', type: 1, required: true },
      { text: 'E flaga otwarte', type: 2, required: false },
      { text: 'E flaga taknie', type: 3, required: false },
    ],
  });

  await openSurvey(page, 'e-flaga');
  await chooseScale(page, 'E flaga skala', 3);
  await card(page, 'E flaga otwarte').locator('textarea').fill('zostaje');
  exec(`
UPDATE sq
SET IsRequired = 1
FROM dbo.SurveyQuestion AS sq
INNER JOIN dbo.Question AS q ON q.Id = sq.QuestionId
WHERE sq.SurveyId = ${surveyId} AND q.[Text] = N'E flaga taknie';
`);
  await page.getByRole('button', { name: SUBMIT }).click();

  await expect(page.locator('.state-box--error')).toContainText('zmieniona w trakcie wypełniania');
  await expectChoice(page, 'E flaga skala', '3', true);
  await expect(card(page, 'E flaga otwarte').locator('textarea')).toHaveValue('zostaje');
  await expect(card(page, 'E flaga taknie').locator('.question-card__required')).toHaveCount(1);
  await expect(card(page, 'E flaga taknie').getByRole('button', { name: 'Wyczyść' })).toHaveCount(0);
  expect(countSessions(surveyId)).toBe(0);
});

test('wyniki pokazują X z N, badge usunięte i tekst skryptu bez wykonania', async ({ page }) => {
  const surveyId = createSurvey({
    slug: 'e-wyniki',
    title: `Wyniki ${SCRIPT}`,
    description: SCRIPT,
    items: [
      { text: 'Wynik skala', type: 1, required: false },
      { text: 'Wynik taknie', type: 3, required: false },
      { text: SCRIPT, type: 2, required: false },
      { text: 'Wynik usunieta', type: 1, required: false },
    ],
  });

  await openSurvey(page, 'e-wyniki');
  await expect(page.locator('h1')).toContainText(SCRIPT);
  await expect(page.locator('.survey-description')).toHaveText(SCRIPT);
  await expect(card(page, SCRIPT)).toBeVisible();
  await expect(page.locator('script', { hasText: 'alert(1)' })).toHaveCount(0);

  await chooseScale(page, 'Wynik skala', 4);
  await card(page, SCRIPT).locator('textarea').fill(SCRIPT);
  await chooseScale(page, 'Wynik usunieta', 2);
  await page.getByRole('button', { name: SUBMIT }).click();
  await expect(page).toHaveURL(/\/ankieta\/e-wyniki\/dziekujemy$/);

  await openSurvey(page, 'e-wyniki');
  await chooseYesNo(page, 'Wynik taknie', 'Tak');
  await chooseScale(page, 'Wynik usunieta', 5);
  await page.getByRole('button', { name: SUBMIT }).click();
  await expect(page).toHaveURL(/\/ankieta\/e-wyniki\/dziekujemy$/);

  exec(`
DECLARE @QuestionId INT = (SELECT Id FROM dbo.Question WHERE [Type] = 2 AND [Text] = N'Wynik przyszle');
IF @QuestionId IS NULL
BEGIN
    INSERT INTO dbo.Question ([Text], [Type], IsActive) VALUES (N'Wynik przyszle', 2, 1);
    SET @QuestionId = SCOPE_IDENTITY();
END;
INSERT INTO dbo.SurveyQuestion (SurveyId, QuestionId, SortOrder, IsRequired, AddedAtUtc)
VALUES (${surveyId}, @QuestionId, 5, 0, '2999-01-01');
DELETE sq
FROM dbo.SurveyQuestion AS sq
INNER JOIN dbo.Question AS q ON q.Id = sq.QuestionId
WHERE sq.SurveyId = ${surveyId} AND q.[Text] = N'Wynik usunieta';
`);

  await page.goto('/wyniki/wyniki-e-wyniki');
  await expect(page.locator('.aggregate', { hasText: 'Wynik skala' })).toContainText('Odpowiedziało 1 z 2');
  await expect(page.locator('.aggregate', { hasText: 'Wynik skala' })).toContainText(/Średnia:\s*4[,.]00/);
  await expect(page.locator('.aggregate', { hasText: 'Wynik taknie' })).toContainText('Odpowiedziało 1 z 2');
  await expect(page.locator('.aggregate', { hasText: SCRIPT }).first()).toContainText('Odpowiedziało 1 z 2');
  const removed = page.locator('.aggregate', { hasText: 'Wynik usunieta' });
  await expect(removed).toContainText('usunięte z ankiety');
  await expect(removed).toContainText('2 odpowiedzi');
  await expect(removed).not.toContainText('Odpowiedziało');
  await expect(removed).toContainText(/Średnia:\s*3[,.]50/);
  await expect(page.locator('.aggregate', { hasText: 'Wynik przyszle' })).toContainText('Brak odpowiedzi');
  await expect(page.locator('.aggregate', { hasText: 'Wynik przyszle' })).not.toContainText('z 0');

  const grid = page.locator('.results-grid');
  // Jedna udzielona odpowiedź: tekst pytania i treść odpowiedzi, bez drugiego wiersza z pominięcia.
  await expect(grid.getByText(SCRIPT, { exact: true })).toHaveCount(2);
  await expect(grid.getByText('Wynik taknie', { exact: true })).toHaveCount(1);
  await expect(grid.getByText('Wynik skala', { exact: true })).toHaveCount(1);
  await expect(grid.getByText('Wynik usunieta', { exact: true })).toHaveCount(2);
  await expect(page.locator('script', { hasText: 'alert(1)' })).toHaveCount(0);
  expect(countSessions(surveyId)).toBe(2);
});

test('formularz przy 375 px nie ma poziomego przewijania', async ({ page }) => {
  createSurvey({
    slug: 'e-waski',
    title: 'Wąski układ E',
    description: 'Opis, który ma się zawijać na wąskim ekranie bez poziomego suwaka.',
    items: [
      { text: 'E waska skala wymagana do przewiniecia w poziomie', type: 1, required: true },
      { text: 'E waskie taknie', type: 3, required: false },
      { text: 'E waskie otwarte z dluzszym podpisem pytania na waskim ekranie', type: 2, required: false },
    ],
  });

  await page.setViewportSize({ width: 375, height: 812 });
  await openSurvey(page, 'e-waski');
  const fits = await page.evaluate(() => {
    const root = document.documentElement;
    return root.scrollWidth <= root.clientWidth && document.body.scrollWidth <= document.body.clientWidth;
  });
  expect(fits).toBe(true);
});

test('brak aktywnej domyślnej prowadzi do najwcześniejszej, a brak aktywnych pokazuje link', async ({ page }) => {
  createSurvey({
    slug: 'e-pozniejsza',
    title: 'Późniejsza aktywna',
    createdAt: '2024-06-01T00:00:00',
    items: [{ text: 'E pozniejsza skala', type: 1, required: true }],
  });
  createSurvey({
    slug: 'e-wczesniejsza',
    title: 'Wcześniejsza aktywna',
    createdAt: '2020-01-01T00:00:00',
    items: [{ text: 'E wczesniejsza skala', type: 1, required: true }],
  });

  try {
    exec(`UPDATE dbo.Survey SET [Status] = 3 WHERE Slug = N'ankieta-satysfakcji'`);
    await page.goto('/');
    await expect(page).toHaveURL(/\/ankieta\/e-wczesniejsza$/);

    exec(`UPDATE dbo.Survey SET [Status] = 3 WHERE [Status] = 2`);
    await page.goto('/');
    await expect(page.getByText('Brak aktywnych ankiet.')).toBeVisible();
    await expect(page.getByRole('link', { name: 'Utwórz ankietę' })).toHaveAttribute('href', '/nowa-ankieta');
  } finally {
    exec(`
UPDATE dbo.Survey SET [Status] = 2 WHERE Slug = N'ankieta-satysfakcji';
UPDATE dbo.Survey SET [Status] = 3 WHERE Slug IN (N'e-pozniejsza', N'e-wczesniejsza');
`);
  }
});
