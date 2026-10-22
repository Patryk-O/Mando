import type { Page } from '@playwright/test';
import { test, expect } from '../fixtures';
import { countRows } from '../db';
import { SEED_CONFIG, SEED_QUESTIONS, SEED_RESULTS, SEED_SLUG, restoreSeedSurvey, seedSurveyId } from '../seed';

// Smoke seedowej ankiety. Flaga zostaje, żeby dało się świadomie wyłączyć ten opis,
// ale po merge'u C/D/E scenariusze działają (trasy /ankieta/{slug} i /nowa-ankieta istnieją).
const SEED_SMOKE_PENDING_MERGE_C = false;

const SUBMIT = 'Wyślij odpowiedzi';

test.describe('smoke: ankieta z seeda', () => {
  test.fixme(
    SEED_SMOKE_PENDING_MERGE_C && process.env.E2E_SEED_SMOKE !== '1',
    'Po merge A (schemat SQL zgodny z modelem EF) i po merge C (katalog przestaje być stubem). Odblokowane w Zadaniu G.',
  );

  test('strona główna przekierowuje na ankietę domyślną i pokazuje formularz', async ({ page }) => {
    await page.goto('/');

    await expect(page).toHaveURL(new RegExp(`/ankieta/${SEED_SLUG}$`));
    await expect(page.locator('.question-card')).toHaveCount(SEED_QUESTIONS.length);
    await expect(page.locator('.question-card').first()).toBeVisible();
  });

  test('pusty formularz oznacza wszystkie pytania seeda i nie zapisuje odpowiedzi', async ({ page }) => {
    const before = seedSessions();
    await page.goto('/');
    await expect(page).toHaveURL(new RegExp(`/ankieta/${SEED_SLUG}$`));
    await expect(page.locator('.question-card')).toHaveCount(SEED_QUESTIONS.length);

    await page.getByRole('button', { name: SUBMIT }).click();

    await expect(page.locator('.question-card--invalid')).toHaveCount(SEED_QUESTIONS.length);
    await expect(page).not.toHaveURL(/\/dziekujemy$/);
    expect(seedSessions()).toBe(before);
  });

  test('poprawne wysłanie kończy się podziękowaniem pod slugiem i +1 w wynikach oraz w bazie', async ({ page }) => {
    await page.goto(SEED_RESULTS);
    const total = page.locator('.stat__value').first();
    await expect(total).toBeVisible();
    const before = Number(await total.innerText());
    const sessionsBefore = seedSessions();

    await page.goto(`/ankieta/${SEED_SLUG}`);
    const cards = page.locator('.question-card');
    await expect(cards).toHaveCount(SEED_QUESTIONS.length);
    await cards.nth(0).locator('label', { hasText: /^4$/ }).click();
    await cards.nth(1).locator('label', { hasText: /^5$/ }).click();
    await cards.nth(2).locator('label', { hasText: 'Tak' }).click();
    await cards.nth(3).locator('label', { hasText: 'Nie' }).click();
    await cards.nth(4).locator('textarea').fill('Smoke test: wszystko działa.');
    await page.getByRole('button', { name: SUBMIT }).click();

    await expect(page).toHaveURL(new RegExp(`/ankieta/${SEED_SLUG}/dziekujemy$`));
    expect(seedSessions()).toBe(sessionsBefore + 1);

    await page.goto(SEED_RESULTS);
    await expect(page.locator('.stat__value').first()).toHaveText(String(before + 1));
    await expect(page.getByText('Smoke test: wszystko działa.').first()).toBeVisible();
  });

  test('błędny token wyników i konfiguracji pokazuje „nie znaleziono”', async ({ page }) => {
    for (const path of ['/wyniki/zly-token', '/konfiguracja/zly-token']) {
      await page.goto(path);
      await expect(page.locator('.app-main, .page-shell').first()).toContainText(/Nie znaleziono/i);
    }
  });

  test('konfiguracja z poprawnym tokenem się ładuje', async ({ page }) => {
    await page.goto(SEED_CONFIG);

    await expect(page.locator('.app-main, .page-shell').first()).not.toContainText(/Nie znaleziono/i);
    await expect(page.locator('.config-grid')).toBeVisible();
    await expect(page.getByRole('region', { name: 'Pytania w ankiecie' }).locator('.config-item')).toHaveCount(
      SEED_QUESTIONS.length,
    );
  });

  test('/nowa-ankieta pokazuje formularz tworzenia ankiety', async ({ page }) => {
    await page.goto('/nowa-ankieta');

    await expect(page.getByRole('heading', { name: 'Nowa ankieta' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Utwórz ankietę' })).toBeVisible();
    await expect(page.getByRole('region', { name: 'Pula pytań' })).toBeVisible();
  });

  test('zamiana pytań w konfiguracji zmienia formularz, a potem da się przywrócić zestaw', async ({ page }) => {
    try {
      await openConfig(page);
      const assigned = page.getByRole('region', { name: 'Pytania w ankiecie' });
      const pool = page.getByRole('region', { name: 'Pula pytań' });
      await assigned.locator('.config-item', { hasText: 'Czy znalazłeś' }).getByRole('button', { name: 'Usuń' }).click();
      await pool.locator('.config-item', { hasText: 'czytelność' }).getByRole('button', { name: 'Dodaj', exact: true }).click();
      await save(page);

      let texts = await formTexts(page);
      expect(texts).toHaveLength(SEED_QUESTIONS.length);
      expect(texts.some((text) => text.includes('Czy znalazłeś'))).toBe(false);
      expect(texts.some((text) => text.includes('czytelność'))).toBe(true);

      await openConfig(page);
      await page
        .getByRole('region', { name: 'Pytania w ankiecie' })
        .locator('.config-item', { hasText: 'czytelność' })
        .getByRole('button', { name: 'Usuń' })
        .click();
      await page
        .getByRole('region', { name: 'Pula pytań' })
        .locator('.config-item', { hasText: 'Czy znalazłeś' })
        .getByRole('button', { name: 'Dodaj', exact: true })
        .click();
      const restored = page.getByRole('region', { name: 'Pytania w ankiecie' });
      await restored.locator('.config-item', { hasText: 'Czy znalazłeś' }).getByRole('button', { name: 'Przesuń w górę' }).click();
      await expect(restored.locator('.config-item').nth(3)).toContainText('Czy znalazłeś');
      await save(page);

      texts = await formTexts(page);
      expect(texts.map((text) => text.trim())).toEqual(SEED_QUESTIONS);
    } finally {
      restoreSeedSurvey();
    }
  });
});

function seedSessions(): number {
  return countRows('ResponseSession', `SurveyId = ${seedSurveyId()}`);
}

async function openConfig(page: Page): Promise<void> {
  await page.goto(SEED_CONFIG);
  await expect(page.getByRole('button', { name: 'Zapisz konfigurację' })).toBeVisible();
}

async function save(page: Page): Promise<void> {
  await page.getByRole('button', { name: 'Zapisz konfigurację' }).click();
  await expect(page.getByText('Konfiguracja została zapisana.')).toBeVisible();
}

async function formTexts(page: Page): Promise<string[]> {
  await page.goto(`/ankieta/${SEED_SLUG}`);
  await expect(page.locator('.question-card').first()).toBeVisible();
  return page.locator('.question-card__text').allInnerTexts();
}
