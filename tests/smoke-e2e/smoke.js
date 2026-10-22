const { chromium } = require('playwright-core');
const BASE = 'http://localhost:5202';
const RES = '/wyniki/wyniki-4f9c2a7e1b3d48a69c5e0d7f2a8b6c13';
const CFG = '/konfiguracja/konfiguracja-8e1d5b3a9c7f42d6b0a4e2c9f7d1b538';
const ok = (c, m) => { console.log((c ? 'PASS ' : 'FAIL ') + m); if (!c) process.exitCode = 1; };

(async () => {
  const browser = await chromium.launch({ channel: 'msedge', headless: true });
  const page = await browser.newPage({ viewport: { width: 1100, height: 1400 } });
  page.on('response', r => { if (r.status() >= 400) console.log('HTTP', r.status(), r.url()); });
  page.on('pageerror', e => console.log('pageerror', e.message));

  await page.goto(BASE + RES);
  await page.waitForSelector('.stat-row, .state-box--error', { timeout: 20000 });
  const before = await page.locator('.stat__value').first().innerText();
  await page.screenshot({ path: 'out/results-before.png', fullPage: true });
  console.log('responses before:', before);

  await page.goto(BASE + '/');
  await page.waitForSelector('.question-card');
  await page.getByRole('button', { name: 'Wyślij odpowiedzi' }).click();
  await page.waitForSelector('.question-card--invalid');
  ok((await page.locator('.question-card--invalid').count()) === 5, 'empty submit flags 5 invalid questions');
  await page.screenshot({ path: 'out/form-invalid.png', fullPage: true });

  const cards = page.locator('.question-card');
  await cards.nth(0).locator('label', { hasText: /^4$/ }).click();
  await cards.nth(1).locator('label', { hasText: /^5$/ }).click();
  await cards.nth(2).locator('label', { hasText: 'Tak' }).click();
  await cards.nth(3).locator('label', { hasText: 'Nie' }).click();
  await cards.nth(4).locator('textarea').fill('Smoke test: wszystko działa.');
  await page.getByRole('button', { name: 'Wyślij odpowiedzi' }).click();
  await page.waitForURL('**/dziekujemy', { timeout: 15000 });
  ok(true, 'redirected to /dziekujemy after valid submit');
  await page.screenshot({ path: 'out/thanks.png', fullPage: true });

  await page.goto(BASE + RES);
  await page.waitForSelector('.stat-row');
  const after = await page.locator('.stat__value').first().innerText();
  console.log('responses after:', after);
  ok(Number(after) === Number(before) + 1, 'results show +1 response');
  await page.waitForSelector('.dxbl-grid, .results-grid', { timeout: 10000 });
  await page.screenshot({ path: 'out/results-after.png', fullPage: true });
  ok((await page.content()).includes('Smoke test: wszystko działa.'), 'open answer visible in results');

  await page.goto(BASE + '/wyniki/zly-token');
  await page.waitForSelector('.state-box, h1', { timeout: 10000 });
  await page.waitForTimeout(500);
  console.log('bad token text:', (await page.locator('.app-main').innerText()).replace(/\n+/g, ' | '));
  await page.goto(BASE + '/konfiguracja/zly-token');
  await page.waitForTimeout(1500);
  console.log('bad cfg token text:', (await page.locator('.app-main').innerText()).replace(/\n+/g, ' | '));

  await page.goto(BASE + CFG);
  await page.waitForSelector('.config-grid', { timeout: 15000 });
  await page.screenshot({ path: 'out/config.png', fullPage: true });
  const assignedBefore = await page.locator('.config-panel').first().locator('.config-item').count();
  console.log('assigned before:', assignedBefore);

  await page.close();
  await browser.close();
})();
