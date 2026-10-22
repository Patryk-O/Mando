const { chromium } = require('playwright-core');
const BASE = 'http://localhost:5202';
const CFG = '/konfiguracja/konfiguracja-8e1d5b3a9c7f42d6b0a4e2c9f7d1b538';
const ok = (c, m) => { console.log((c ? 'PASS ' : 'FAIL ') + m); if (!c) process.exitCode = 1; };

async function formTexts(page) {
  await page.goto(BASE + '/');
  await page.waitForSelector('.question-card');
  return page.locator('.question-card__text').allInnerTexts();
}
async function openCfg(page) {
  await page.goto(BASE + CFG);
  await page.waitForSelector('.config-grid');
}
const assignedItem = (page, text) => page.locator('.config-panel').first().locator('.config-item', { hasText: text });
const poolItem = (page, text) => page.locator('.config-panel').nth(1).locator('.config-item', { hasText: text });
async function save(page) {
  await page.getByRole('button', { name: 'Zapisz konfigurację' }).click();
  await page.waitForSelector('.state-box--success');
}

(async () => {
  const browser = await chromium.launch({ channel: 'msedge', headless: true });
  const page = await browser.newPage({ viewport: { width: 1100, height: 1000 } });
  await openCfg(page);
  if (await assignedItem(page, 'czytelność').count() > 0) {
    await assignedItem(page, 'czytelność').getByRole('button', { name: 'Usuń' }).click();
    await save(page);
  }
  const original = await formTexts(page);
  console.log('original:', original.length);

  await openCfg(page);
  await assignedItem(page, 'Czy znalazłeś').getByRole('button', { name: 'Usuń' }).click();
  await poolItem(page, 'czytelność').getByRole('button', { name: 'Dodaj' }).click();
  await save(page);
  let t = await formTexts(page);
  ok(t.length === original.length, `still ${original.length} questions after swap (${t.length})`);
  ok(!t.some(x => x.includes('Czy znalazłeś')), 'removed question gone from form');
  ok(t.some(x => x.includes('czytelność')), 'added question appears on form');

  await openCfg(page);
  await assignedItem(page, 'czytelność').getByRole('button', { name: 'Usuń' }).click();
  await poolItem(page, 'Czy znalazłeś').getByRole('button', { name: 'Dodaj' }).click();
  await save(page);
  await openCfg(page);
  const target = ['ogólne', 'jakość', 'poleciłbyś', 'znalazłeś', 'lepiej'];
  for (let i = 0; i < target.length; i++) {
    for (let guard = 0; guard < 6; guard++) {
      const texts = await page.locator('.config-panel').first().locator('.config-item__text').allInnerTexts();
      const idx = texts.findIndex(x => x.includes(target[i]));
      if (idx <= i) break;
      await page.locator('.config-panel').first().locator('.config-item').nth(idx).getByRole('button', { name: 'Przesuń w górę' }).click();
      await page.waitForTimeout(150);
    }
  }
  await save(page);
  t = await formTexts(page);
  console.log('restored order:', t.map(x => x.slice(0, 25)).join(' | '));
  ok(t.length === original.length, 'question count restored');

  await browser.close();
})();
