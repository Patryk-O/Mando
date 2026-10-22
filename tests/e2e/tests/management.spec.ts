import type { Page } from '@playwright/test';
import { test, expect } from '../fixtures';
import { countRows, scalar } from '../db';
import { SEED_CONFIG, SEED_QUESTIONS, SEED_SLUG, restoreSeedSurvey, seedSurveyId } from '../seed';

const NEW_QUESTION = 'Dodatkowe uwagi tylko dla Test A';
const POOL_SCALE = 'Jak oceniasz jakość obsługi?';
const POOL_YESNO = 'Czy poleciłbyś nas znajomym?';
const OPTIONAL_SEED = 'Jak oceniasz ogólne wrażenia z korzystania z naszej usługi?';
const SUBMIT = 'Wyślij odpowiedzi';

const links = { publicUrl: '', resultsUrl: '', configUrl: '' };

test.describe('management: cykl życia ankiety na czystej bazie', () => {
  test.describe.configure({ mode: 'serial' });
  test.setTimeout(90_000);

  test.afterAll(() => {
    restoreSeedSurvey();
  });

  test('strona główna prowadzi do ankiety domyślnej', async ({ page }) => {
    await page.goto('/');
    await expect(page).toHaveURL(/\/ankieta\/ankieta-satysfakcji$/);
    await expect(page.locator('h1')).toHaveText('Ankieta satysfakcji');
    await expect(page.locator('.question-card')).toHaveCount(5);
  });

  test('nowa ankieta Test A dostaje dwa pytania z puli, jedno nowe i trzy linki', async ({ page, context, baseURL }) => {
    await context.grantPermissions(['clipboard-read', 'clipboard-write'], { origin: baseURL });
    await page.goto('/nowa-ankieta');
    await expect(page.getByRole('button', { name: 'Utwórz ankietę' })).toBeVisible();

    await page.getByLabel('Tytuł').fill('Test A');
    await expect(page.locator('.slug-preview')).toContainText('/ankieta/test-a');
    await page.getByRole('radio', { name: 'Aktywna', exact: true }).click();

    const pool = page.getByRole('region', { name: 'Pula pytań' });
    const assigned = page.getByRole('region', { name: 'Pytania w ankiecie' });
    for (const text of [POOL_SCALE, POOL_YESNO]) {
      await pool.locator('.config-item', { hasText: text }).getByRole('button', { name: 'Dodaj', exact: true }).click();
      await expect(assigned).toContainText(text);
    }

    await page.getByLabel('Treść nowego pytania').fill(NEW_QUESTION);
    await page.getByRole('radio', { name: 'Otwarte', exact: true }).click();
    await page.locator('.new-question').getByRole('checkbox', { name: 'Wymagane' }).uncheck();
    await page.getByRole('button', { name: 'Dodaj pytanie' }).click();
    await expect(assigned.locator('.config-item', { hasText: NEW_QUESTION })).toContainText('zapisz, aby dodać do puli');
    await expect(
      assigned.locator('.config-item', { hasText: NEW_QUESTION }).getByRole('checkbox', { name: 'Wymagane' }),
    ).not.toBeChecked();

    await page.getByRole('button', { name: 'Utwórz ankietę' }).click();
    await expect(page.getByRole('heading', { name: 'Ankieta została utworzona' })).toBeVisible();
    await expect(page.getByText(/Zapisz te linki/)).toBeVisible();
    await expect(page.getByText(/Kto zna link do konfiguracji/)).toBeVisible();

    for (const [group, fragment] of [
      ['Link publiczny', '/ankieta/test-a'],
      ['Wyniki', '/wyniki/'],
      ['Konfiguracja', '/konfiguracja/'],
    ] as const) {
      await expect(page.getByRole('group', { name: group })).toContainText(fragment);
    }

    links.publicUrl = (await page.getByRole('group', { name: 'Link publiczny' }).locator('a').getAttribute('href')) ?? '';
    links.resultsUrl = (await page.getByRole('group', { name: 'Wyniki' }).locator('a').getAttribute('href')) ?? '';
    links.configUrl = (await page.getByRole('group', { name: 'Konfiguracja' }).locator('a').getAttribute('href')) ?? '';
    expect(links.publicUrl).toContain('/ankieta/test-a');
    expect(links.resultsUrl).toContain('/wyniki/');
    expect(links.configUrl).toContain('/konfiguracja/');
    expect(page.url()).not.toContain('konfiguracja-');

    const publicGroup = page.getByRole('group', { name: 'Link publiczny' });
    const expected = (await publicGroup.locator('a').innerText()).trim();
    await publicGroup.getByRole('button', { name: 'Kopiuj' }).click();
    await expect(publicGroup.getByRole('button', { name: 'Skopiowano' })).toBeVisible();
    await expect.poll(() => page.evaluate(() => navigator.clipboard.readText())).toBe(expected);
  });

  test('wypełnienie Test A pomija pytanie opcjonalne, a wyniki pokazują 0 z 1 i 1 z 1', async ({ page }) => {
    await page.goto('/ankieta/test-a');
    await expect(page.locator('.question-card')).toHaveCount(3);
    await expect(card(page, NEW_QUESTION).locator('.question-card__required')).toHaveCount(0);
    await expect(card(page, POOL_SCALE).locator('.question-card__required')).toHaveCount(1);
    await expect(card(page, POOL_YESNO).locator('.question-card__required')).toHaveCount(1);

    await card(page, POOL_SCALE).locator('.question-card__body').getByText('4', { exact: true }).click();
    await card(page, POOL_YESNO).locator('.question-card__body').getByText('Tak', { exact: true }).click();
    await page.getByRole('button', { name: SUBMIT }).click();
    await expect(page).toHaveURL(/\/ankieta\/test-a\/dziekujemy$/);

    await page.goto(pathOf(links.resultsUrl));
    await expect(page.locator('.stat__value').first()).toHaveText('1');
    await expect(page.locator('.aggregate', { hasText: POOL_SCALE })).toContainText('Odpowiedziało 1 z 1');
    await expect(page.locator('.aggregate', { hasText: POOL_YESNO })).toContainText('Odpowiedziało 1 z 1');
    await expect(page.locator('.aggregate', { hasText: NEW_QUESTION })).toContainText('Odpowiedziało 0 z 1');
    await expect(page.locator('.results-grid').getByText(NEW_QUESTION, { exact: true })).toHaveCount(0);
  });

  test('nowe pytanie z Test A nie wchodzi do seedowej, dopóki nie zostanie dodane ręcznie', async ({ page }) => {
    await page.goto(`/ankieta/${SEED_SLUG}`);
    await expect(page.locator('.question-card')).toHaveCount(5);
    await expect(page.locator('.question-card__text', { hasText: NEW_QUESTION })).toHaveCount(0);

    await openConfig(page, SEED_CONFIG);
    const assigned = page.getByRole('region', { name: 'Pytania w ankiecie' });
    const pool = page.getByRole('region', { name: 'Pula pytań' });
    await expect(assigned.locator('.config-item')).toHaveCount(5);
    await expect(assigned).not.toContainText(NEW_QUESTION);
    await expect(pool).toContainText(NEW_QUESTION);

    await pool.locator('.config-item', { hasText: NEW_QUESTION }).getByRole('button', { name: 'Dodaj', exact: true }).click();
    await assigned.locator('.config-item', { hasText: NEW_QUESTION }).getByRole('checkbox', { name: 'Wymagane' }).uncheck();
    await assigned.locator('.config-item', { hasText: OPTIONAL_SEED }).getByRole('checkbox', { name: 'Wymagane' }).uncheck();
    await page.getByRole('button', { name: 'Zapisz konfigurację' }).click();
    await expect(page.getByText('Konfiguracja została zapisana.')).toBeVisible();

    await page.goto(`/ankieta/${SEED_SLUG}`);
    await expect(page.locator('.question-card')).toHaveCount(6);
    await expect(card(page, NEW_QUESTION).locator('.question-card__required')).toHaveCount(0);
    await expect(card(page, OPTIONAL_SEED).locator('.question-card__required')).toHaveCount(0);
    await expect(card(page, POOL_SCALE).locator('.question-card__required')).toHaveCount(1);
  });

  test('zamknięcie Test A pokazuje komunikat, a szkic znika z katalogu', async ({ page }) => {
    await openConfig(page, links.configUrl);
    await page.getByRole('radio', { name: 'Zamknięta', exact: true }).click();
    await page.getByRole('button', { name: 'Zapisz konfigurację' }).click();
    await expect(page.getByText('Konfiguracja została zapisana.')).toBeVisible();

    await page.goto('/ankieta/test-a');
    await expect(page.locator('.state-box')).toContainText('Ta ankieta jest zamknięta i nie przyjmuje już odpowiedzi.');
    await expect(page.locator('.question-card')).toHaveCount(0);
    await expect(page.getByRole('button', { name: SUBMIT })).toHaveCount(0);

    await openConfig(page, links.configUrl);
    await page.getByRole('radio', { name: 'Szkic', exact: true }).click();
    await page.getByRole('button', { name: 'Zapisz konfigurację' }).click();
    await expect(page.getByText('Konfiguracja została zapisana.')).toBeVisible();

    await page.goto('/ankieta/test-a');
    await expect(page.locator('.app-main, .page-shell').first()).toContainText(/Nie znaleziono/);
    await expect(page.locator('.question-card')).toHaveCount(0);
  });

  test('domyślna ankieta przełącza się na Test A i wraca na seedową', async ({ page }) => {
    await openConfig(page, links.configUrl);
    await page.getByRole('radio', { name: 'Aktywna', exact: true }).click();
    await page.getByRole('checkbox', { name: /Ankieta domyślna/ }).check();
    await expect(page.getByText(/przekieruje do innej aktywnej ankiety/)).toHaveCount(0);
    await page.getByRole('button', { name: 'Zapisz konfigurację' }).click();
    await expect(page.getByText('Konfiguracja została zapisana.')).toBeVisible();

    await page.goto('/');
    await expect(page).toHaveURL(/\/ankieta\/test-a$/);
    await expect(page.locator('h1')).toHaveText('Test A');

    await openConfig(page, SEED_CONFIG);
    await page.getByRole('checkbox', { name: /Ankieta domyślna/ }).check();
    await page.getByRole('button', { name: 'Zapisz konfigurację' }).click();
    await expect(page.getByText('Konfiguracja została zapisana.')).toBeVisible();

    await page.goto('/');
    await expect(page).toHaveURL(/\/ankieta\/ankieta-satysfakcji$/);
  });

  test('sprzątanie przywraca seed; baza E2E jest i tak usuwana przez run-e2e.ps1', async ({ page }) => {
    restoreSeedSurvey();

    expect(scalar(
      `SELECT COUNT(*) FROM dbo.Survey WHERE Slug = N'${SEED_SLUG}' AND IsDefault = 1 AND [Status] = 2`,
    )).toBe(1);
    expect(countRows('SurveyQuestion', `SurveyId = ${seedSurveyId()} AND IsRequired = 1`)).toBe(SEED_QUESTIONS.length);

    await page.goto('/');
    await expect(page).toHaveURL(/\/ankieta\/ankieta-satysfakcji$/);
    await expect(page.locator('.question-card__text')).toHaveText(SEED_QUESTIONS);

    await page.goto('/nowa-ankieta');
    await expect(page.getByRole('region', { name: 'Pula pytań' })).toContainText(NEW_QUESTION);
    await expect(page.getByRole('region', { name: 'Pytania w ankiecie' })).not.toContainText(NEW_QUESTION);
  });
});

function card(page: Page, text: string) {
  return page.locator('.question-card', { hasText: text });
}

async function openConfig(page: Page, url: string): Promise<void> {
  await page.goto(pathOf(url));
  await expect(page.getByRole('button', { name: 'Zapisz konfigurację' })).toBeVisible();
}

function pathOf(url: string): string {
  return new URL(url, 'http://localhost').pathname;
}
