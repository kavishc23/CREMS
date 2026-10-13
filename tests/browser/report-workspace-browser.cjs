const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');
(async () => {
  const browser = await chromium.launch({ channel: 'chrome', headless: true });
  try {
    const page = await browser.newPage({ viewport: { width: 1400, height: 1000 } });
    await page.route('**/__reports_test', route => route.fulfill({ contentType: 'text/html', body: `<div id="root"></div><script type="module">
      import RefreshRuntime from '/@react-refresh';RefreshRuntime.injectIntoGlobalHook(window);window.$RefreshReg$=()=>{};window.$RefreshSig$=()=>type=>type;window.__vite_plugin_react_preamble_installed__=true;
      const {default:React}=await import('/node_modules/.vite/deps/react.js');const {default:{createRoot}}=await import('/node_modules/.vite/deps/react-dom_client.js');const {ReportButton}=await import('/src/components/ReportButton.tsx');
      window.report={title:'TEST-ASSET-001 Detailed asset report',description:'Fictional asset used for report verification',coverage:'Test fixture: 23 maintenance records.',sections:[{title:'Maintenance history',columns:['reference','date','status','notes'],rows:Array.from({length:23},(_,i)=>({reference:'JOB-'+String(i+1).padStart(3,'0'),date:i<12?'2026-10-01':'2026-10-10',status:'Completed',notes:i===0?'Long evidence note. '.repeat(150):'Inspected and released for hire.'}))}]};
      createRoot(document.getElementById('root')).render(React.createElement(ReportButton,{reports:[window.report]}));
    </script>` }));
    await page.goto((process.env.CREMS_TEST_URL || 'http://127.0.0.1:5173') + '/__reports_test');
    await page.getByRole('button', { name: 'Generate report', exact: true }).click();
    await page.getByText('JOB-001', { exact: true }).waitFor();
    assert.equal(await page.locator('tbody tr').count(), 10);
    await page.getByRole('button', { name: 'Next', exact: true }).click();
    await page.getByText('JOB-011', { exact: true }).waitFor();
    await page.getByRole('button', { name: 'Next', exact: true }).click();
    assert.equal(await page.locator('tbody tr').count(), 3);
    assert(await page.getByRole('button', { name: 'Next', exact: true }).isDisabled());
    await page.getByLabel('Search report').fill('JOB-023');
    assert.equal(await page.locator('tbody tr').count(), 1);
    await page.getByLabel('Search report').fill('');
    await page.getByLabel('From', { exact: true }).fill('2026-10-05');
    assert.equal(await page.getByText('11 records', { exact: true }).count(), 1);
    await page.getByLabel('To', { exact: true }).fill('2026-10-01');
    assert(await page.getByRole('button', { name: 'Download PDF' }).isDisabled());
    await page.getByLabel('From', { exact: true }).fill(''); await page.getByLabel('To', { exact: true }).fill('');
    fs.mkdirSync('tmp/reports-qa', { recursive: true });
    const downloadPromise = page.waitForEvent('download');
    await page.getByRole('button', { name: 'Download PDF' }).click();
    await (await downloadPromise).saveAs('tmp/reports-qa/asset-report.pdf');
    const safe = await page.evaluate(async () => { const { reportCsv } = await import('/src/utils/reportDocument.ts'); return reportCsv({ ...window.report, sections: [{ title: 'CSV safety', columns: ['value'], rows: [{value:'=1+1'}, {value:'Quoted "text", comma'}] }] }); });
    assert(safe.includes("'=1+1")); assert(safe.includes('Quoted ""text"", comma'));
    await page.route('**/api/assets/test/profile', route => route.fulfill({ json: { asset: { assetNumber: 'TEST-001', name: 'Test excavator', attributeValues: [] }, availability: { bookings: [], transfers: [] }, inspections: [], rentalInspections: [], maintenance: [], meters: [], lifecycle: [], documents: [{documentNumber:'DOC-001',fileName:'Service record.pdf',type:'Maintenance'}], audits: [] } }));
    await page.route('**/api/assets/test/performance', route => route.fulfill({ status: 403, json: {} }));
    const assetReport = await page.evaluate(async () => (await (await import('/src/utils/moduleReports.ts')).detailedAssetReports('test'))[0]);
    assert(assetReport.title.includes('TEST-001'));
    assert(!assetReport.sections.some(section => section.title.includes('financial')));
    assert(assetReport.sections.find(section => section.title === 'Document register').rows[0].fileName === 'Service record.pdf');
    await page.screenshot({ path: 'tmp/reports-qa/desktop.png' });
    await page.setViewportSize({ width: 390, height: 844 });
    await page.screenshot({ path: 'tmp/reports-qa/mobile.png' });
    assert(await page.getByRole('button', { name: 'Download PDF' }).isVisible());
    console.log('PASS: 10-row paging, boundaries, search reset, date filtering, invalid dates, PDF download, CSV escaping and mobile controls. Fixture data only.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exit(1); });
