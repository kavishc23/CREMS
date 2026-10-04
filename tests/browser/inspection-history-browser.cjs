const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const assert = require('node:assert/strict');
(async () => {
  const browser = await chromium.launch({ channel: 'chrome', headless: true });
  try {
    const page = await browser.newPage({ viewport: { width: 1400, height: 1000 } });
    const photo = 'data:image/jpeg;base64,' + require('node:fs').readFileSync('src/crems-web/public/catalog/suv.jpg').toString('base64');
    let mode = 'saved';
    await page.route('**/api/rentals/test', route => route.fulfill(mode === 'error' ? { status: 403, json: {} } : { json: { inspections: mode === 'empty' ? [] : [
      { id: 'pre', type: 'Handover', completedAt: '2026-09-01T00:00:00Z', completedByName: 'Pickup Staff', conditionNotes: 'Existing scratch', meterReading: 1200, fuelLevelPercent: 80, signatureName: 'Test Customer', signatureDataUrl: photo, evidenceJson: JSON.stringify({ photos: [photo], checklist: ['Tyres checked'], damageZones: ['Front'], accessories: 'Two keys' }) },
      { id: 'post', type: 'Return', completedAt: '2026-09-03T00:00:00Z', completedByName: 'Return Staff', conditionNotes: 'Returned clean', damageNotes: 'New dent', meterReading: 1300, fuelLevelPercent: 50, evidenceJson: '{invalid' }
    ] } }));
    await page.route('**/__inspection_history_test', route => route.fulfill({ contentType: 'text/html', body: `<div id="root"></div><script type="module">
      import RefreshRuntime from '/@react-refresh';RefreshRuntime.injectIntoGlobalHook(window);window.$RefreshReg$=()=>{};window.$RefreshSig$=()=>type=>type;window.__vite_plugin_react_preamble_installed__=true;
      const {default:React}=await import('/node_modules/.vite/deps/react.js');const {default:{createRoot}}=await import('/node_modules/.vite/deps/react-dom_client.js');const {RentalInspectionHistory}=await import('/src/components/RentalInspectionHistory.tsx');createRoot(document.getElementById('root')).render(React.createElement(RentalInspectionHistory,{bookingId:'test',bookingNumber:'TEST-001',assetName:'Test asset',onClose:()=>{}}));
    </script>` }));
    const url = (process.env.CREMS_TEST_URL || 'http://localhost:5173') + '/__inspection_history_test';
    await page.goto(url);
    await page.getByText('Existing scratch', { exact: true }).waitFor();
    await page.getByText('Returned clean', { exact: true }).waitFor();
    await page.getByText('Tyres checked', { exact: true }).waitFor();
    await page.getByText('Two keys', { exact: true }).waitFor();
    await page.getByText(/saved evidence could not be read/).waitFor();
    await page.getByRole('button', { name: 'Enlarge inspection photo 1' }).click();
    await page.getByAltText('Inspection evidence enlarged').waitFor();
    await page.getByRole('button', { name: 'Close photo' }).click();
    await page.setViewportSize({ width: 390, height: 844 });
    assert(await page.getByText('Returned clean', { exact: true }).isVisible());
    mode = 'empty'; await page.reload();
    await page.getByText('No pre-hire inspection has been saved.').waitFor();
    await page.getByText('No post-hire inspection has been saved.').waitFor();
    mode = 'error'; await page.reload();
    await page.getByText(/Inspection records could not be loaded/).waitFor();
    mode = 'saved'; await page.getByRole('button', { name: 'Retry' }).click();
    await page.getByText('Returned clean', { exact: true }).waitFor();
    console.log('PASS: Both inspection stages, notes, checklist, photos, enlargement, mobile layout, malformed evidence, empty records and access error retry. API reads mocked.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exit(1); });
