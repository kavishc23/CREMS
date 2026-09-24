const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const assert = require('node:assert/strict');
(async () => {
  const browser = await chromium.launch({ channel: 'chrome', headless: true });
  try {
    const page = await browser.newPage();
    let healthCalls = 0, branchCalls = 0;
    page.on('pageerror', error => console.error(error.message));
    await page.route('**/api/**', async route => {
      const path = new URL(route.request().url()).pathname;
      if (!path.startsWith('/api/')) return route.continue();
      if (path === '/api/health') { healthCalls++; return route.fulfill({ status: 401, json: { detail: 'Old staff session expired' } }); }
      if (path === '/api/public/branches' && ++branchCalls === 1) return route.fulfill({ status: 503, json: {} });
      if (path === '/api/customer-account/session') return route.fulfill({ json: { fullName: 'Test Customer', hirePreferences: [] } });
      if (path.startsWith('/api/notifications') || path === '/api/customer-account/notifications') return route.fulfill({ json: { items: [], unreadCount: 0 } });
      if (route.request().method() !== 'GET') throw new Error('Unexpected mutation in read-only test');
      return route.continue();
    });
    await page.route('**/__catalogue_session_test', route => route.fulfill({ contentType: 'text/html', body: `<div id="root"></div><script type="module">
import RefreshRuntime from '/@react-refresh';RefreshRuntime.injectIntoGlobalHook(window);window.$RefreshReg$=()=>{};window.$RefreshSig$=()=>type=>type;window.__vite_plugin_react_preamble_installed__=true;
window.sessionExpiryEvents=0;window.addEventListener('crems:customer-session-expired',()=>window.sessionExpiryEvents++);window.addEventListener('crems:staff-session-expired',()=>window.sessionExpiryEvents++);
const {default:React}=await import('/node_modules/.vite/deps/react.js');const {default:{createRoot}}=await import('/node_modules/.vite/deps/react-dom_client.js');const {PublicRentalPage}=await import('/src/pages/PublicRentalPage.tsx');createRoot(document.getElementById('root')).render(React.createElement(PublicRentalPage,{customerAuthenticated:true,customerName:'Test Customer',onCustomerAccount:()=>{}}));
</script>` }));
    await page.goto(`${process.env.CREMS_TEST_URL || 'http://localhost:5173'}/__catalogue_session_test`);
    await page.getByRole('button', { name: 'Try again' }).click();
    await page.getByRole('combobox').first().click();
    await page.getByRole('option', { name: 'Suva', exact: true }).waitFor();
    assert.equal(await page.getByText('Your customer session has expired.', { exact: false }).count(), 0);
    assert.equal(await page.evaluate(() => window.sessionExpiryEvents), 0);
    assert.equal(healthCalls, 0);
    assert(branchCalls >= 2);
    console.log('PASS: signed-in customer catalogue loads independently of stale staff health session and recovers using Try again. No API writes.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exit(1); });
