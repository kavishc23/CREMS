const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const assert = require('node:assert/strict');
(async () => {
  const browser = await chromium.launch({ channel: 'chrome', headless: true });
  try {
    for (const override of [null, '0', '25']) {
      const page = await browser.newPage({ viewport: { width: 1400, height: 1000 } });
      page.on('pageerror', error => console.error('Browser error:', error.message));
      const submissions = [];
      const returnedAt = '2026-09-20T06:00:00Z';
      const row = { id: '00000000-0000-0000-0000-000000000001', bookingNumber: 'TEST-001', status: 'ConvertedToRental', customerName: 'Test Customer', branchName: 'Test Branch', assetNumber: 'TEST-ASSET', assetName: 'Test bin', assetCategoryCode: 'BIG_BIN', assetCategory: 'Big bin', hasProfessionalPersonnel: false, startAt: '2026-09-19T00:00:00Z', endAt: '2026-09-20T00:00:00Z', hasAgreement: true, agreementStatus: 'Signed', amountPaid: 0, depositRequired: 100, bondStatus: 'Held', bondAmountHeld: 100, bondDeductionAmount: 0, bondRefundAmount: 0, preHireInspectionComplete: true, returnInspectionComplete: false };
      await page.route('**/api/**', async route => {
        const url = route.request().url();
        if (!new URL(url).pathname.startsWith('/api/')) { await route.continue(); return; }
        if (route.request().method() === 'POST') { submissions.push(route.request().postDataJSON()); await route.fulfill({ json: {} }); return; }
        if (url.includes('return-charges')) { await route.fulfill({ json: { returnedAt, lateFee: 60 } }); return; }
        if (url.includes('inspection-context')) { await route.fulfill({ json: { templates: [{ id: 't', name: 'Return checklist', stage: 'PostHire', checklistJson: JSON.stringify([{ section: 'Condition', items: [{ label: 'Clean' }] }]) }], preHire: null } }); return; }
        if (url.includes('work-queue')) { await route.fulfill({ json: { items: [row], counts: { pickupToday: 0, onHire: 1, dueToday: 0, overdue: 1, returnInProgress: 0, recentlyCompleted: 0 }, page: 1, pageSize: 25, total: 1 } }); return; }
        await route.fulfill({ json: [] });
      });
      await page.route('**/__return_charge_test', route => route.fulfill({ contentType: 'text/html', body: `<div id="root"></div><script type="module">
import RefreshRuntime from '/@react-refresh';RefreshRuntime.injectIntoGlobalHook(window);window.$RefreshReg$=()=>{};window.$RefreshSig$=()=>type=>type;window.__vite_plugin_react_preamble_installed__=true;
const {default:React}=await import('/node_modules/.vite/deps/react.js');const {default:{createRoot}}=await import('/node_modules/.vite/deps/react-dom_client.js');const {RentalsPage}=await import('/src/pages/RentalsPage.tsx');createRoot(document.getElementById('root')).render(React.createElement(RentalsPage));
</script>` }));
      await page.goto(`${process.env.CREMS_TEST_URL || 'http://localhost:5173'}/__return_charge_test`);
      try { await page.getByRole('button', { name: 'Begin return' }).first().click({timeout:15000}); } catch (error) { console.error((await page.locator('body').innerText()).slice(0,3000)); throw error; }
      const next = () => page.getByRole('button', { name: 'Continue', exact: true }).click();
      await page.getByLabel('Scan or enter returned asset QR number').fill('TEST-ASSET');
      await next(); await next();
      await page.getByRole('button', { name: 'Check all checklist items' }).click();
      await page.getByLabel('Post-hire inspection result').fill('Returned intact');
      await page.getByLabel('Inspection photo files').setInputFiles('src/crems-web/public/catalog/sedan.jpg');
      await page.getByAltText('Inspection photo 1', { exact: true }).waitFor();
      await next(); await next();
      const late = page.getByLabel('Late return (FJD)', { exact: true });
      assert.equal(await late.inputValue(), '60');
      await late.fill('-1');
      assert.equal(await page.getByRole('button', { name: 'Continue', exact: true }).isDisabled(), true);
      await page.getByRole('button', { name: 'Use calculated late fee' }).click();
      assert.equal(await late.inputValue(), '60');
      if (override !== null) await late.fill(override);
      await next(); await next();
      await page.getByLabel('Customer acknowledgement name').fill('Test Customer');
      const rect = await page.locator('canvas').boundingBox();
      await page.mouse.move(rect.x + 20, rect.y + 20); await page.mouse.down(); await page.mouse.move(rect.x + 100, rect.y + 50); await page.mouse.up();
      await page.getByLabel('Customer acknowledges the return record, charges and bond settlement', { exact: true }).check();
      await next();
      const submitted = page.waitForRequest(r => r.method() === 'POST' && r.url().endsWith('/return'));
      await page.getByRole('button', { name: 'Complete return & create invoice' }).click();
      await submitted;
      assert.equal(submissions.length, 1);
      assert.equal(submissions[0].lateFee, override === null ? null : Number(override));
      assert.equal(submissions[0].returnedAt, returnedAt);
      await page.close();
    }
    console.log('PASS: automatic fee, negative-value validation, reset, waiver, edited fee, and return timestamp. API writes mocked.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exit(1); });
