import { test as plain, expect as plainExpect, type Page } from '@playwright/test';
import { PageGuards } from '../guards';
import { test, expect } from '../fixtures';

/**
 * Dowód, że globalne asercje (guards.ts / fixtures.ts) naprawdę failują. Strony testowe są
 * serwowane przez page.route pod origin aplikacji, więc nie wymagają działającej aplikacji.
 */
const PROBE_PATH = '/__guard-probe';

async function openProbe(page: Page, baseURL: string, extraBody = ''): Promise<PageGuards> {
  const origin = new URL(baseURL).origin;
  const guards = new PageGuards(page, origin);
  await page.route(`${origin}${PROBE_PATH}`, (route) =>
    route.fulfill({
      contentType: 'text/html; charset=utf-8',
      body: `<!doctype html><html><body><div id="blazor-error-ui" style="display:none">Błąd</div>${extraBody}</body></html>`,
    }),
  );
  await page.goto(`${origin}${PROBE_PATH}`);
  return guards;
}

plain.describe('guards: PageGuards wykrywa problemy (klasa)', () => {
  plain('console.error jest raportowany i assertClean rzuca', async ({ page, baseURL }) => {
    const guards = await openProbe(page, baseURL!);
    await page.evaluate(() => console.error('boom z aplikacji'));

    plainExpect(guards.problems).toHaveLength(1);
    await plainExpect(guards.assertClean()).rejects.toThrow(/console\.error: boom z aplikacji/);
  });

  plain('baner DX1000 / DevExpress trial w console.error jest ignorowany', async ({ page, baseURL }) => {
    const guards = await openProbe(page, baseURL!);
    await page.evaluate(() => {
      console.error('For evaluation purposes only (DX1000)');
      console.error('DevExpress trial version');
    });

    plainExpect(guards.problems).toEqual([]);
    await guards.assertClean();
  });

  plain('404 zasobu jest raportowany, a 404 favicon.ico ignorowany', async ({ page, baseURL }) => {
    const origin = new URL(baseURL!).origin;
    const guards = await openProbe(page, baseURL!);
    await page.route(`${origin}/favicon.ico`, (route) => route.fulfill({ status: 404, body: '' }));
    await page.route(`${origin}/brak.png`, (route) => route.fulfill({ status: 404, body: '' }));
    const loadImage = (url: string) =>
      page.evaluate(
        (src) =>
          new Promise<void>((resolve) => {
            const img = new Image();
            img.onerror = () => resolve();
            img.onload = () => resolve();
            img.src = src;
          }),
        url,
      );

    await loadImage(`${origin}/favicon.ico`);
    await page.waitForTimeout(100);
    plainExpect(guards.problems).toEqual([]);

    await loadImage(`${origin}/brak.png`);
    await plainExpect.poll(() => guards.problems.length).toBe(1);
    await plainExpect(guards.assertClean()).rejects.toThrow(/brak\.png/);
  });

  plain('console.warn i console.log nie są błędami', async ({ page, baseURL }) => {
    const guards = await openProbe(page, baseURL!);
    await page.evaluate(() => {
      console.warn('ostrzeżenie');
      console.log('info');
    });

    await guards.assertClean();
  });

  plain('nieobsłużony wyjątek strony jest raportowany', async ({ page, baseURL }) => {
    const guards = await openProbe(page, baseURL!);
    await page.evaluate(() => {
      setTimeout(() => {
        throw new Error('wyjątek w timerze');
      }, 0);
    });
    await plainExpect.poll(() => guards.problems.length).toBe(1);

    await plainExpect(guards.assertClean()).rejects.toThrow(/wyjątek w timerze/);
  });

  plain('nieudany request jest raportowany', async ({ page, baseURL }) => {
    const origin = new URL(baseURL!).origin;
    const guards = await openProbe(page, baseURL!);
    await page.route(`${origin}/api/niedostepne`, (route) => route.abort('connectionrefused'));
    await page.evaluate((url) => fetch(url).catch(() => undefined), `${origin}/api/niedostepne`);
    // „Failed to load resource” dodatkowo trafia do console.error — liczy się, że requestfailed jest wśród problemów.
    await plainExpect.poll(() => guards.problems.some((p) => p.startsWith('requestfailed:'))).toBe(true);

    await plainExpect(guards.assertClean()).rejects.toThrow(/requestfailed/);
  });

  plain('odpowiedź 500 z aplikacji jest raportowana, a z obcego origin nie', async ({ page, baseURL }) => {
    const origin = new URL(baseURL!).origin;
    const guards = await openProbe(page, baseURL!);
    await page.route('http://obcy-host.test/zasob', (route) => route.fulfill({ status: 500, body: 'x' }));
    await page.route(`${origin}/api/awaria`, (route) => route.fulfill({ status: 500, body: 'x' }));

    await page.evaluate(() => fetch('http://obcy-host.test/zasob', { mode: 'no-cors' }).catch(() => undefined));
    await page.waitForTimeout(100);
    plainExpect(guards.problems.filter((p) => p.startsWith('odpowiedź 500'))).toEqual([]);

    await page.evaluate((url) => fetch(url), `${origin}/api/awaria`);
    await plainExpect.poll(() => guards.problems.some((p) => p.includes('odpowiedź 500'))).toBe(true);
    await plainExpect(guards.assertClean()).rejects.toThrow(/odpowiedź 500/);
  });

  plain('widoczny #blazor-error-ui jest raportowany, ukryty nie', async ({ page, baseURL }) => {
    const guards = await openProbe(page, baseURL!);
    await guards.assertClean();

    await page.evaluate(() => {
      document.getElementById('blazor-error-ui')!.style.display = 'block';
    });

    await plainExpect(guards.assertClean()).rejects.toThrow(/#blazor-error-ui/);
  });

  plain('allowedProblems pozwala świadomie dopuścić znany błąd', async ({ page, baseURL }) => {
    const origin = new URL(baseURL!).origin;
    const guards = new PageGuards(page, origin, [/oczekiwany błąd/]);
    await page.route(`${origin}${PROBE_PATH}`, (route) =>
      route.fulfill({ contentType: 'text/html', body: '<html><body></body></html>' }),
    );
    await page.goto(`${origin}${PROBE_PATH}`);
    await page.evaluate(() => console.error('oczekiwany błąd'));

    await guards.assertClean();
  });
});

test.describe('guards: auto-fixture z fixtures.ts realnie failuje test (test.fail = oczekiwana porażka)', () => {
  test('console.error w teście powoduje porażkę', async ({ page, baseURL }) => {
    test.fail();
    await openProbe(page, baseURL!);
    await page.evaluate(() => console.error('boom'));
  });

  test('widoczny #blazor-error-ui powoduje porażkę', async ({ page, baseURL }) => {
    test.fail();
    await openProbe(page, baseURL!);
    await page.evaluate(() => {
      document.getElementById('blazor-error-ui')!.style.display = 'block';
    });
  });

  test('odpowiedź 500 z aplikacji powoduje porażkę', async ({ page, baseURL }) => {
    test.fail();
    const origin = new URL(baseURL!).origin;
    await openProbe(page, baseURL!);
    await page.route(`${origin}/api/awaria`, (route) => route.fulfill({ status: 500, body: 'x' }));
    await page.evaluate((url) => fetch(url), `${origin}/api/awaria`);
  });

  test('czysta strona przechodzi (kontrola, że asercje nie failują bez powodu)', async ({ page, baseURL }) => {
    await openProbe(page, baseURL!);
    await expect(page.locator('#blazor-error-ui')).toBeHidden();
  });

  test.describe('z opcją allowedProblems', () => {
    test.use({ allowedProblems: [/dopuszczony/] });

    test('dopuszczony błąd nie powoduje porażki', async ({ page, baseURL }) => {
      await openProbe(page, baseURL!);
      await page.evaluate(() => console.error('dopuszczony błąd'));
    });
  });
});
