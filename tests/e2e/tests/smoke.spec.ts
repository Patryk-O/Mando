import { test, expect } from '../fixtures';
import { countRows } from '../db';

// Smoke seedowej ankiety (jedyne miejsce, gdzie E2E korzysta z seeda — pozostałe testy tworzą własne dane).
const RESULTS = '/wyniki/wyniki-4f9c2a7e1b3d48a69c5e0d7f2a8b6c13';
const CONFIG = '/konfiguracja/konfiguracja-8e1d5b3a9c7f42d6b0a4e2c9f7d1b538';
const SUBMIT = 'Wyślij odpowiedzi';

// Na gałęzi strumienia T serwisy aplikacji są jeszcze stubami (NotImplementedException z kontraktów) i schemat jest stary,
// więc seedowy smoke nie ma jak przejść. Zadanie G ustawia to na false (albo E2E_SEED_SMOKE=1 wymusza uruchomienie od razu).
const SEED_SMOKE_PENDING_MERGE_C = true;

test.describe('smoke: ankieta z seeda', () => {
  test.fixme(
    SEED_SMOKE_PENDING_MERGE_C && process.env.E2E_SEED_SMOKE !== '1',
    'Serwisy katalogu/konfiguracji to stuby do merge strumienia C; odblokować w Zadaniu G.',
  );

  test('strona główna pokazuje formularz z pytaniami', async ({ page }) => {
    await page.goto('/');

    await expect(page.locator('.question-card').first()).toBeVisible();
    expect(await page.locator('.question-card').count()).toBeGreaterThan(0);
  });

  test('pusty formularz jest odrzucony, a pytania wymagane oznaczone', async ({ page }) => {
    await page.goto('/');
    await expect(page.locator('.question-card').first()).toBeVisible();

    await page.getByRole('button', { name: SUBMIT }).click();

    await expect(page.locator('.question-card--invalid').first()).toBeVisible();
    await expect(page).not.toHaveURL(/dziekujemy/);
    expect(countRows('ResponseSession')).toBe(0);
  });

  test('poprawne wysłanie kończy się podziękowaniem i +1 w wynikach oraz w bazie', async ({ page }) => {
    await page.goto(RESULTS);
    const total = page.locator('.stat__value').first();
    await expect(total).toBeVisible();
    const before = Number(await total.innerText());
    const sessionsBefore = countRows('ResponseSession');

    await page.goto('/');
    const cards = page.locator('.question-card');
    await expect(cards.first()).toBeVisible();
    await cards.nth(0).locator('label', { hasText: /^4$/ }).click();
    await cards.nth(1).locator('label', { hasText: /^5$/ }).click();
    await cards.nth(2).locator('label', { hasText: 'Tak' }).click();
    await cards.nth(3).locator('label', { hasText: 'Nie' }).click();
    await cards.nth(4).locator('textarea').fill('Smoke test: wszystko działa.');
    await page.getByRole('button', { name: SUBMIT }).click();

    await expect(page).toHaveURL(/\/dziekujemy$/);
    expect(countRows('ResponseSession')).toBe(sessionsBefore + 1);

    await page.goto(RESULTS);
    await expect(page.locator('.stat__value').first()).toHaveText(String(before + 1));
    await expect(page.getByText('Smoke test: wszystko działa.').first()).toBeVisible();
  });

  test('błędny token wyników i konfiguracji pokazuje „nie znaleziono”', async ({ page }) => {
    for (const path of ['/wyniki/zly-token', '/konfiguracja/zly-token']) {
      await page.goto(path);
      await expect(page.locator('.app-main')).toContainText(/Nie znaleziono/i);
    }
  });

  test('konfiguracja z poprawnym tokenem się ładuje', async ({ page }) => {
    await page.goto(CONFIG);

    await expect(page.locator('.app-main')).not.toContainText(/Nie znaleziono/i);
    await expect(page.locator('.config-grid')).toBeVisible();
  });
});

// Trasy z funkcji zarządzania ankietami — do czasu zmergowania strumieni C/D/E nie istnieją.
test.describe('smoke: zarządzanie ankietami (po strumieniach C/D/E)', () => {
  test.fixme('/ przekierowuje do ankiety domyślnej pod /ankieta/{slug}', async ({ page }) => {
    // Trasa /ankieta/{slug} powstaje w strumieniu E.
    await page.goto('/');
    await expect(page).toHaveURL(/\/ankieta\/[a-z0-9-]+$/);
    await expect(page.locator('.question-card').first()).toBeVisible();
  });

  test.fixme('/nowa-ankieta pokazuje formularz tworzenia ankiety', async ({ page }) => {
    // Trasa /nowa-ankieta powstaje w strumieniu D.
    await page.goto('/nowa-ankieta');
    await expect(page.getByRole('heading', { name: /ankiet/i }).first()).toBeVisible();
  });

  test.fixme('wysłanie przekierowuje na /ankieta/{slug}/dziekujemy', async ({ page }) => {
    // Trasa podziękowania z slugiem powstaje w strumieniu E.
    await page.goto('/');
    await expect(page).toHaveURL(/\/ankieta\/[a-z0-9-]+\/dziekujemy$/);
  });

  test.fixme('zamiana pytań w konfiguracji zmienia formularz (przepisany edytor — strumień D)', async ({ page }) => {
    // Poprzedni scenariusz (tests/smoke-e2e/config-smoke.js) opierał się na starym ConfigPage, który D przepisuje.
    await page.goto(CONFIG);
  });
});
