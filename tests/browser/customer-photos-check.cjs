const assert = require('node:assert/strict');
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || '/Users/kavishchandra/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
(async () => {
  const browser = await chromium.launch({ channel: 'chrome', headless: true });
  try {
    const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
    await page.goto(process.env.CREMS_TEST_URL || 'http://localhost:5173', { waitUntil: 'networkidle' });
    for (const tag of ['CM-SED-001', 'CE-PJ-001']) {
      const card = page.getByTestId('rental-catalogue-card').filter({ hasText: tag });
      await card.waitFor();
      const photo = card.locator('img');
      await photo.waitFor();
      await photo.evaluate(img => img.decode());
      assert(await photo.evaluate(img => img.naturalWidth > 0));
      await card.getByRole('button', { name: 'View details', exact: true }).click();
      const dialog = page.getByRole('dialog');
      const detailPhoto = dialog.locator('img');
      await detailPhoto.waitFor();
      await detailPhoto.evaluate(img => img.decode());
      assert(await detailPhoto.evaluate(img => img.naturalWidth > 0));
      await dialog.getByRole('button', { name: 'Close', exact: true }).click();
      await dialog.waitFor({ state: 'hidden' });
    }
    console.log('PASS: vehicle and projector images visibly load on actual customer cards and detail dialogs.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exit(1); });
