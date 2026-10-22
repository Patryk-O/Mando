import fs from 'node:fs';
import path from 'node:path';
import type { Page } from '@playwright/test';
import { test, expect } from '../fixtures';

const OUT = path.join(__dirname, '..', 'out');
const DIRTY = 'Są niezapisane zmiany.';

test.beforeAll(() => {
  fs.mkdirSync(OUT, { recursive: true });
});

test.describe('editor.spec', () => {
  test('nowe pytanie i flaga Wymagane zostają po zapisie i nie wchodzą do innej ankiety', async ({ page }) => {
    const marker = `Tylko w pierwszej ${Date.now()}`;
    const first = await createSurvey(page, `Pierwsza ${Date.now()}`, {
      status: 'Aktywna',
      poolQuestion: 'Jak oceniasz jakość obsługi?',
    });

    await openConfig(page, first.configUrl);
    const assigned = page.getByRole('region', { name: 'Pytania w ankiecie' });
    await assigned.getByRole('checkbox', { name: 'Wymagane' }).first().uncheck();
    await expect(page.getByText(DIRTY)).toBeVisible();
    await page.getByLabel('Treść nowego pytania').fill(marker);
    await page.getByRole('radio', { name: 'Otwarte', exact: true }).click();
    await page.locator('.new-question').getByRole('checkbox', { name: 'Wymagane' }).uncheck();
    await page.getByRole('button', { name: 'Dodaj pytanie' }).click();
    await expect(assigned).toContainText('zapisz, aby dodać do puli');
    await page.getByRole('button', { name: 'Zapisz konfigurację' }).click();
    await expect(page.getByText('Konfiguracja została zapisana.')).toBeVisible();
    await expect(assigned).toContainText(marker);
    await expect(page.getByText('zapisz, aby dodać do puli')).toHaveCount(0);

    await page.reload();
    await expect(page.getByRole('button', { name: 'Zapisz konfigurację' })).toBeVisible();
    await expect(page.getByRole('region', { name: 'Pytania w ankiecie' }).getByRole('checkbox', { name: 'Wymagane' }).first()).not.toBeChecked();
    await expect(
      page.getByRole('region', { name: 'Pytania w ankiecie' }).locator('.config-item', { hasText: marker }).getByRole('checkbox', { name: 'Wymagane' }),
    ).not.toBeChecked();
    await page.screenshot({ path: path.join(OUT, 'editor-konfiguracja.png'), fullPage: true });

    const second = await createSurvey(page, `Druga ${Date.now()}`, {
      status: 'Aktywna',
      poolQuestion: 'Czy poleciłbyś nas znajomym?',
    });
    await openConfig(page, second.configUrl);
    await expect(page.getByRole('region', { name: 'Pytania w ankiecie' })).not.toContainText(marker);
    await expect(page.getByRole('region', { name: 'Pula pytań' })).toContainText(marker);
  });

  test('Enter w polu nowego pytania nie zapisuje konfiguracji', async ({ page }) => {
    const created = await createSurvey(page, `Enter ${Date.now()}`, { status: 'Szkic' });
    await openConfig(page, created.configUrl);
    const title = page.getByLabel('Tytuł');
    const original = await title.inputValue();
    await title.fill(`${original} zmieniony`);
    await expect(page.getByText(DIRTY)).toBeVisible();
    const field = page.getByLabel('Treść nowego pytania');
    await field.fill('Nie zapisuj enterem');
    await field.press('Enter');
    await expect(page.getByText(DIRTY)).toBeVisible();
    await expect(page.getByText('Konfiguracja została zapisana.')).toHaveCount(0);
    await expect(page.getByText('Ankieta została utworzona')).toHaveCount(0);

    page.once('dialog', (dialog) => dialog.accept());
    await page.reload();
    await expect(page.getByLabel('Tytuł')).toHaveValue(original);
    await expect(page.getByRole('region', { name: 'Pytania w ankiecie' })).not.toContainText('Nie zapisuj enterem');
  });

  test('strzałki na granicach nie zmieniają kolejności', async ({ page }) => {
    await page.goto('/nowa-ankieta');
    await expect(page.getByRole('button', { name: 'Utwórz ankietę' })).toBeVisible();
    const pool = page.getByRole('region', { name: 'Pula pytań' });
    const assigned = page.getByRole('region', { name: 'Pytania w ankiecie' });
    for (const text of ['Jak oceniasz jakość obsługi?', 'Czy poleciłbyś nas znajomym?']) {
      await pool.locator('.config-item', { hasText: text }).getByRole('button', { name: 'Dodaj', exact: true }).click();
      await expect(assigned).toContainText(text);
    }

    const up = assigned.getByRole('button', { name: 'Przesuń w górę' });
    const down = assigned.getByRole('button', { name: 'Przesuń w dół' });
    await expect(up.first()).toBeDisabled();
    await expect(down.last()).toBeDisabled();
    await down.first().click();
    await expect(assigned.locator('.config-item').first()).toContainText('znajomym');
    await expect(up.first()).toBeDisabled();
    await up.nth(1).click();
    await expect(assigned.locator('.config-item').first()).toContainText('jakość obsługi');
  });

  test('usuwa nowe pytanie i pytanie dodane z puli', async ({ page }) => {
    await page.goto('/nowa-ankieta');
    const assigned = page.getByRole('region', { name: 'Pytania w ankiecie' });
    const pool = page.getByRole('region', { name: 'Pula pytań' });
    const marker = 'Pytanie do usunięcia z edytora';
    await page.getByLabel('Treść nowego pytania').fill(marker);
    await page.getByRole('button', { name: 'Dodaj pytanie' }).click();
    await expect(assigned).toContainText('zapisz, aby dodać do puli');
    await assigned.locator('.config-item', { hasText: marker }).getByRole('button', { name: 'Usuń' }).click();
    await expect(assigned).not.toContainText(marker);
    await expect(pool).not.toContainText(marker);

    const poolText = 'Czy chcesz dodać dodatkowe uwagi?';
    await pool.locator('.config-item', { hasText: poolText }).getByRole('button', { name: 'Dodaj', exact: true }).click();
    await expect(assigned).toContainText(poolText);
    await assigned.locator('.config-item', { hasText: poolText }).getByRole('button', { name: 'Usuń' }).click();
    await expect(assigned).not.toContainText(poolText);
    await expect(pool).toContainText(poolText);
  });

  test('Cofnij zmiany przywraca listę', async ({ page }) => {
    const created = await createSurvey(page, `Cofnij ${Date.now()}`, {
      status: 'Aktywna',
      poolQuestion: 'Jak oceniasz jakość obsługi?',
    });
    await openConfig(page, created.configUrl);
    const assigned = page.getByRole('region', { name: 'Pytania w ankiecie' });
    const pool = page.getByRole('region', { name: 'Pula pytań' });
    const extra = 'Czy skorzystasz z naszej usługi ponownie?';
    await pool.locator('.config-item', { hasText: extra }).getByRole('button', { name: 'Dodaj', exact: true }).click();
    await expect(assigned).toContainText(extra);
    await page.getByRole('button', { name: 'Cofnij zmiany' }).click();
    await expect(assigned).not.toContainText(extra);
    await expect(pool).toContainText(extra);
    await expect(page.getByText(DIRTY)).toHaveCount(0);
  });

  test('limit 30 wyłącza Dodaj i Dodaj pytanie', async ({ page }) => {
    test.setTimeout(180_000);
    await page.goto('/nowa-ankieta');
    const text = page.getByLabel('Treść nowego pytania');
    const add = page.getByRole('button', { name: 'Dodaj pytanie' });
    const assigned = page.getByRole('region', { name: 'Pytania w ankiecie' });
    await expect(add).toBeEnabled();

    for (let i = 1; i <= 30; i++) {
      const label = `Limit ${i} unikalne pytanie`;
      await text.fill(label);
      await add.click();
      await expect(assigned).toContainText(label);
    }

    await expect(page.getByText('Osiągnięto limit 30 pytań w ankiecie.')).toBeVisible();
    await expect(add).toBeDisabled();
    await expect(page.getByRole('region', { name: 'Pula pytań' }).getByRole('button', { name: 'Dodaj', exact: true }).first()).toBeDisabled();
  });

  test('wyszukiwarka puli filtruje bez względu na wielkość liter i spacje', async ({ page }) => {
    await page.goto('/nowa-ankieta');
    const pool = page.getByRole('region', { name: 'Pula pytań' });
    await expect(pool).toContainText('znajomym');
    await page.getByLabel('Szukaj w puli').fill('  CZYTELNOŚĆ ');
    await expect(pool).toContainText('czytelność');
    await expect(pool).not.toContainText('znajomym');
    await page.getByLabel('Szukaj w puli').fill('   ');
    await expect(pool).toContainText('znajomym');
  });

  test('NavigationLock pyta o niezapisane zmiany i milczy po zapisie', async ({ page }) => {
    const created = await createSurvey(page, `Zamek ${Date.now()}`, { status: 'Szkic' });
    await openConfig(page, created.configUrl);
    const title = page.getByLabel('Tytuł');
    const original = await title.inputValue();
    const changed = `${original} x`;
    await title.fill(changed);
    await expect(page.getByText(DIRTY)).toBeVisible();

    const dialogPromise = page.waitForEvent('dialog');
    await page.getByRole('link', { name: 'Nowa ankieta' }).click();
    const dialog = await dialogPromise;
    expect(dialog.message()).toContain('Opuścić stronę?');
    await dialog.dismiss();
    await expect(page).toHaveURL(/\/konfiguracja\//);
    await expect(title).toHaveValue(changed);

    await page.getByRole('button', { name: 'Zapisz konfigurację' }).click();
    await expect(page.getByText('Konfiguracja została zapisana.')).toBeVisible();
    await expect(page.getByText(DIRTY)).toHaveCount(0);

    let unexpected = false;
    const listener = () => {
      unexpected = true;
    };
    page.on('dialog', listener);
    await page.getByRole('link', { name: 'Nowa ankieta' }).click();
    await expect(page).toHaveURL(/\/nowa-ankieta$/);
    page.off('dialog', listener);
    expect(unexpected).toBe(false);
  });

  test('druga karta dostaje konflikt i wczytuje aktualną konfigurację', async ({ page }) => {
    const created = await createSurvey(page, `Konflikt ${Date.now()}`, { status: 'Szkic' });
    await openConfig(page, created.configUrl);
    const page2 = await page.context().newPage();
    try {
      await page2.goto(pathOf(created.configUrl));
      await expect(page2.getByRole('button', { name: 'Zapisz konfigurację' })).toBeVisible();
      const original = await page.getByLabel('Tytuł').inputValue();
      await page.getByLabel('Tytuł').fill(`${original} A`);
      await expect(page.getByText(DIRTY)).toBeVisible();
      await page.getByRole('button', { name: 'Zapisz konfigurację' }).click();
      await expect(page.getByText('Konfiguracja została zapisana.')).toBeVisible();

      await page2.getByLabel('Tytuł').fill(`${original} B`);
      await expect(page2.getByText(DIRTY)).toBeVisible();
      await page2.getByRole('button', { name: 'Zapisz konfigurację' }).click();
      await expect(page2.getByText('Konfiguracja została zmieniona przez kogoś innego. Odśwież stronę.')).toBeVisible();
      await expect(page2.getByRole('button', { name: 'Wczytaj aktualną konfigurację' })).toBeVisible();
      await page2.screenshot({ path: path.join(OUT, 'editor-konflikt.png'), fullPage: true });

      page2.once('dialog', (dialog) => dialog.accept());
      await page2.getByRole('button', { name: 'Wczytaj aktualną konfigurację' }).click();
      await expect(page2.getByLabel('Tytuł')).toHaveValue(`${original} A`);
      await expect(page2.getByText('Konfiguracja została zmieniona przez kogoś innego. Odśwież stronę.')).toHaveCount(0);
    } finally {
      await page2.close();
    }
  });

  test('tworzenie pokazuje trzy linki, kopiowanie i błędy walidacji', async ({ page, context, baseURL }) => {
    await context.grantPermissions(['clipboard-read', 'clipboard-write'], { origin: baseURL });
    await page.goto('/nowa-ankieta');
    await page.getByRole('button', { name: 'Utwórz ankietę' }).click();
    await expect(page.getByText('Podaj tytuł ankiety.')).toBeVisible();

    const slug = `zajety-${Date.now()}`;
    await createSurvey(page, `Slug ${Date.now()}`, { slug, status: 'Szkic' });

    await page.goto('/nowa-ankieta');
    await page.getByLabel('Tytuł').fill(`Inny ${Date.now()}`);
    await page.getByLabel('Własny adres (opcjonalnie)').fill(slug);
    await expect(page.locator('.slug-preview')).toContainText(`/ankieta/${slug}`);
    await page.getByRole('button', { name: 'Utwórz ankietę' }).click();
    await expect(page.getByText('Ten adres jest już zajęty.')).toBeVisible();

    const nasty = '<script>alert(1)</script>';
    page.on('dialog', (dialog) => {
      throw new Error(`Nieoczekiwany dialog: ${dialog.message()}`);
    });
    await page.getByLabel('Tytuł').fill(nasty);
    await page.getByLabel('Własny adres (opcjonalnie)').fill('');
    await expect(page.locator('.slug-preview')).toContainText('/ankieta/script-alert');
    await page.getByRole('button', { name: 'Utwórz ankietę' }).click();
    await expect(page.getByRole('heading', { name: 'Ankieta została utworzona' })).toBeVisible();
    await expect(page.locator('.created-title')).toHaveText(nasty);
    await expect(page.getByText(/Zapisz te linki/)).toBeVisible();
    await expect(page.getByText(/Kto zna link do konfiguracji/)).toBeVisible();
    expect(page.url()).toContain('/nowa-ankieta');
    expect(page.url()).not.toContain('konfiguracja-');

    for (const [group, fragment] of [
      ['Link publiczny', '/ankieta/'],
      ['Wyniki', '/wyniki/'],
      ['Konfiguracja', '/konfiguracja/'],
    ] as const) {
      await expect(page.getByRole('group', { name: group })).toContainText(fragment);
    }

    const publicGroup = page.getByRole('group', { name: 'Link publiczny' });
    const expected = (await publicGroup.locator('a').innerText()).trim();
    await publicGroup.getByRole('button', { name: 'Kopiuj' }).click();
    await expect(publicGroup.getByRole('button', { name: 'Skopiowano' })).toBeVisible();
    await expect.poll(() => page.evaluate(() => navigator.clipboard.readText())).toBe(expected);
    await page.screenshot({ path: path.join(OUT, 'editor-utworzona.png'), fullPage: true });
  });

  test('szkic, podpowiedź domyślnej i blokada usunięcia ostatniego pytania', async ({ page }) => {
    const created = await createSurvey(page, `Statusy ${Date.now()}`, {
      status: 'Aktywna',
      poolQuestion: 'Jak oceniasz jakość obsługi?',
    });
    await openConfig(page, created.configUrl);
    await page.getByRole('radio', { name: 'Szkic', exact: true }).click();
    await expect(page.getByText('Zmień status na Aktywna, aby opublikować.')).toBeVisible();
    await page.getByRole('checkbox', { name: /Ankieta domyślna/ }).check();
    await expect(page.getByText(/przekieruje do innej aktywnej ankiety/)).toBeVisible();
    await page.getByRole('button', { name: 'Cofnij zmiany' }).click();

    await page.getByRole('region', { name: 'Pytania w ankiecie' }).getByRole('button', { name: 'Usuń' }).click();
    await expect(page.getByText('Ankieta aktywna musi mieć co najmniej jedno pytanie.')).toBeVisible();
    await expect(page.getByRole('region', { name: 'Pytania w ankiecie' })).toContainText('jakość obsługi');
  });

  test('widok 375 px nie przewija się w poziomie', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 812 });
    await page.goto('/nowa-ankieta');
    await expect(page.getByRole('button', { name: 'Utwórz ankietę' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Nowa ankieta' })).toBeVisible();
    await expectNoHorizontalScroll(page);
    await page.screenshot({ path: path.join(OUT, 'editor-375-nowa.png'), fullPage: true });

    const created = await createSurvey(page, `Wasko ${Date.now()}`, { status: 'Szkic' });
    await page.setViewportSize({ width: 375, height: 812 });
    await openConfig(page, created.configUrl);
    await expectNoHorizontalScroll(page);
    await page.screenshot({ path: path.join(OUT, 'editor-375-konfiguracja.png'), fullPage: true });
  });
});

type Created = { configUrl: string };

async function createSurvey(
  page: Page,
  title: string,
  options?: { slug?: string; status?: 'Szkic' | 'Aktywna' | 'Zamknięta'; poolQuestion?: string },
): Promise<Created> {
  await page.goto('/nowa-ankieta');
  await expect(page.getByRole('button', { name: 'Utwórz ankietę' })).toBeVisible();
  await page.getByLabel('Tytuł').fill(title);
  if (options?.slug) {
    await page.getByLabel('Własny adres (opcjonalnie)').fill(options.slug);
    await expect(page.locator('.slug-preview')).toContainText(`/ankieta/${options.slug}`);
  } else {
    await expect(page.getByText(DIRTY)).toBeVisible();
  }

  if (options?.status && options.status !== 'Szkic') {
    await page.getByRole('radio', { name: options.status, exact: true }).click();
    await expect(page.getByText('Zmień status na Aktywna, aby opublikować.')).toHaveCount(0);
  }

  if (options?.poolQuestion) {
    const pool = page.getByRole('region', { name: 'Pula pytań' });
    await pool.locator('.config-item', { hasText: options.poolQuestion }).getByRole('button', { name: 'Dodaj', exact: true }).click();
    await expect(page.getByRole('region', { name: 'Pytania w ankiecie' })).toContainText(options.poolQuestion);
  }

  await page.getByRole('button', { name: 'Utwórz ankietę' }).click();
  await expect(page.getByText(/Zapisz te linki/)).toBeVisible();
  const configUrl = await page.getByRole('group', { name: 'Konfiguracja' }).locator('a').getAttribute('href');
  if (!configUrl) {
    throw new Error('Brak linku konfiguracji po utworzeniu ankiety.');
  }

  return { configUrl };
}

async function openConfig(page: Page, url: string): Promise<void> {
  await page.goto(pathOf(url));
  await expect(page.getByRole('button', { name: 'Zapisz konfigurację' })).toBeVisible();
}

function pathOf(url: string): string {
  return new URL(url, 'http://localhost').pathname;
}

async function expectNoHorizontalScroll(page: Page): Promise<void> {
  const fits = await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth);
  expect(fits).toBe(true);
}
