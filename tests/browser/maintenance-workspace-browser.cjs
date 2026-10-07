const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');

(async () => {
 const browser = await chromium.launch({ channel: 'chrome', headless: true });
 try {
  const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } }); page.setDefaultTimeout(15000);
  const errors = []; page.on('pageerror', e => { errors.push(e.message); console.error('PAGE ERROR:', e.message); });
  page.on('response', r => { if (r.status() >= 400) console.error('HTTP', r.status(), r.url()); });
  let jobs = [], documents = [], ledger = [], available = 5, sequence = 0, denied = false;
  const writes = [], queries = [];
  const asset = { id: 'a1', assetNumber: 'VEH-SUV-1001', name: 'Nissan Navara VL 4x4', branchId: 'b1', divisionId: 'v1', branchName: 'Suva', status: 'Available', meterUnit: 'km', currentMeterReading: 45000, nextServiceDate: '2026-10-01' };
  const plan = () => ({...asset, nextServiceMeter: 50000, meterInterval: 5000, serviceIntervalMonths: 3, meterTargetSource: 'Completed service target', isDue: true, isOverdue: true});
  const permissions = () => ({ canComplete: !denied, canInspect: !denied, canFinancial: !denied });
  const checks = ['Repair verified and test run passed', 'Brakes, tyres, lights and steering safe', 'No unresolved fault or damage'];
  const version = () => new Date(Date.UTC(2026, 9, 7, 0, 0, ++sequence)).toISOString();
  const shape = job => { const record = { ...job, assetStatus: asset.status, currentMeterReading: asset.currentMeterReading, defaultTaxRate: 12.5, issuedStockCost: ledger.reduce((sum,x) => sum + x.quantity * x.unitCost, 0) }; if (denied) for (const key of ['hasEstimate','estimatedCost','actualCost','partsCost','labourCost','labourHours','labourRate','fuelCost','transportCost','taxCost','otherCost','externalServiceCost','invoiceNumber','taxMode','taxRate','taxableCosts','taxOverrideReason','defaultTaxRate','issuedStockCost']) delete record[key]; return record; };
  await page.route('**/api/**', async route => {
   const request = route.request(), url = new URL(request.url()), pathname = url.pathname, method = request.method();
   if (!pathname.startsWith('/api/')) return route.continue();
   const json = value => route.fulfill({ json: value });
   if (pathname === '/api/auth/session') return json({ id: 'user', fullName: 'Maintenance supervisor', roles: ['SuperAdministrator'], branchName: 'Suva', divisionName: 'Carpenters Fiji' });
   if (pathname === '/api/notifications') return json({ items: [], unreadCount: 0, total: 0, page: 1, pageSize: 25 });
   if (pathname === '/api/maintenance-jobs/options') return json({ branches: [{ branchId: 'b1', name: 'Suva' }], technicians: [{ id: 't1', fullName: 'Ravi Singh', employeeNumber: 'EMP-011', branchId: 'b1', divisionId: 'v1', availability: 'Available' }], suppliers: [{ id: 's1', name: 'Suva Fleet Services', supplierNumber: 'SUP-001' }], technicianNames: ['Ravi Singh'] });
   if (pathname === '/api/maintenance-jobs/assets') return json([asset]);
   if (pathname === '/api/maintenance-jobs/inspections/in1') return json({id:'in1', assetNumber:asset.assetNumber, stage:'PreHire', outcome:'Failed', completedAt:'2026-10-06T00:00:00Z', completedByName:'Inspector', notes:'Leak identified at pre-hire inspection', meterReading:45000, meterUnit:'km', fuelPercent:80, responsesJson:JSON.stringify([{label:'Oil leak check',passed:false}])});
   if (pathname === '/api/maintenance-jobs/inspections') return json([]);
   if (pathname === '/api/maintenance-jobs/schedule') return json({ items: [{ ...plan(), activeJobId: jobs.find(j => !['Completed','Cancelled'].includes(j.status))?.id }], total: 1, dueCount: 1, overdueCount: 1, today: '2026-10-07', page: 1, pageSize: 25 });
   if (pathname === '/api/maintenance-jobs/workspace') {
    queries.push(Object.fromEntries(url.searchParams));
    const all = jobs.map(shape), queue = url.searchParams.get('queue'), search = (url.searchParams.get('search') || '').toLowerCase();
    let items = all.filter(j => queue === 'History' || queue === 'AwaitingRelease' ? queue === 'History' || ['Completed','Cancelled'].includes(j.status) && !j.releasedAt : !['Completed','Cancelled'].includes(j.status));
    if (url.searchParams.get('overdueRepairs') === 'true') items = items.filter(j => j.expectedReleaseAt && new Date(j.expectedReleaseAt) < new Date() && !j.releasedAt && j.status !== 'Cancelled');
    if (search) items = items.filter(j => JSON.stringify(j).toLowerCase().includes(search));
    if (url.searchParams.get('status')) items = items.filter(j => j.status === url.searchParams.get('status'));
    return json({ items, total: items.length, page: 1, pageSize: 25, permissions: permissions(), counts: { active: all.filter(j => !['Completed','Cancelled'].includes(j.status)).length, unassigned: all.filter(j => !j.assignedTo && !['Completed','Cancelled'].includes(j.status)).length, inProgress: all.filter(j => j.status === 'InProgress').length, waiting: all.filter(j => j.status === 'WaitingForParts').length, awaitingRelease: all.filter(j => ['Completed','Cancelled'].includes(j.status) && !j.releasedAt).length, preventive: 0, overdueRepairs: all.filter(j => j.expectedReleaseAt && new Date(j.expectedReleaseAt) < new Date() && !j.releasedAt && j.status !== 'Cancelled').length } });
   }
   const job = jobs.find(j => pathname.includes('/' + j.id));
   if (pathname.endsWith('/workspace') && job) return json({ servicePlan: plan(), job: shape(job), history: jobs.map(shape), audits: [{ id: 'audit1', action: 'Maintenance updated', userName: 'Maintenance supervisor', occurredAt: job.version, summary: 'Repair details recorded' }], meters: [], inspections: [], metrics: { completedJobs: job.status === 'Completed' ? 1 : 0, downtimeHours: 4, repeatFailures: 0, ...(denied ? {} : { totalCost: job.actualCost || 0, bookValue: 48000, costToBookValuePercent: 0.2 }) }, safetyChecks: checks, permissions: permissions() });
   if (pathname.endsWith('/release') && job) { const body = request.postDataJSON(); writes.push(body); assert.equal(body.expectedVersion, job.version); assert.deepEqual(body.passedChecks, checks); job.releasedAt = version(); job.releasedByName = 'Maintenance supervisor'; job.version = version(); asset.status = 'Available'; return json({ message: `${asset.assetNumber} passed its safety check and is available.` }); }
   if (pathname.includes('/documents') && job) { if (method === 'POST') documents.push({ id: 'd1', fileName: 'repair-photo.png', type: 'CompletionEvidence' }); return json(documents); }
   if (pathname.includes('/parts') && job) {
    if (method === 'GET') return json({ stock: [{ id: 'p1', partNumber: 'OIL-FILTER', name: 'Oil filter', available, unitCost: 10 }], ledger, canManage: true, canIssue: !['Completed','Cancelled'].includes(job.status), canFinancial: !denied });
    const body = request.postDataJSON(), returning = pathname.endsWith('/return'); ledger.push({ id: 'usage' + ledger.length, inventoryPartId: 'p1', partNumber: 'OIL-FILTER', quantity: returning ? -body.quantity : body.quantity, unitCost: 10, batchKey: 'batch1', createdAt: version() }); available += returning ? body.quantity : -body.quantity; job.partsCost = ledger.reduce((total, x) => total + x.quantity * x.unitCost, 0); job.actualCost = job.partsCost + (job.labourCost || 0) + (job.fuelCost || 0); job.version = version(); return json({});
   }
   if (pathname === '/api/maintenance-jobs' && method === 'POST') {
    const body = request.postDataJSON(); writes.push(body); asset.status = 'Maintenance';
    jobs.push({ ...body, id: 'j1', jobNumber: 'MNT-2026-TEST01', assetNumber: asset.assetNumber, assetName: asset.name, branchName: asset.branchName, reportedAt: version(), version: version(), status: 'Open', taxMode: 'Exclusive', taxRate: 12.5, taxableCosts: 9, sourceInspectionId: 'in1', expectedReleaseAt:'2026-10-01T00:00:00Z', releasedAt: null, reportedByName: 'Maintenance supervisor', meterUnit: asset.meterUnit, currentMeterReading: asset.currentMeterReading });
    return json({ id: 'j1', jobNumber: jobs[0].jobNumber });
   }
   if (job && method === 'PUT') {
    const body = request.postDataJSON(); writes.push(body);
    if (body.expectedVersion !== job.version) return route.fulfill({ status: 409, json: { message: 'This maintenance job changed. Refresh before saving.' } });
    Object.assign(job, body, { version: version(), actualCost: body.useDetailedCosts ? body.partsCost + body.labourCost + body.fuelCost + body.transportCost + (body.taxMode === 'Inclusive' ? 0 : body.taxCost) + body.otherCost + body.externalServiceCost : body.actualCost });
    if (body.status === 'Completed') { asset.status = 'Inspection'; asset.nextServiceDate = body.nextServiceDate; asset.currentMeterReading = body.meterReading; }
    return json({});
   }
   throw Error(`Unexpected API request: ${method} ${pathname}`);
  });
  await page.route('**/__maintenance_workspace', route => route.fulfill({ contentType: 'text/html', body: `<html><body><div id="root"></div><script type="module">
    import RefreshRuntime from '/@react-refresh'; RefreshRuntime.injectIntoGlobalHook(window); window.$RefreshReg$=()=>{}; window.$RefreshSig$=()=>type=>type; window.__vite_plugin_react_preamble_installed__=true;
    const {default:React}=await import('/node_modules/.vite/deps/react.js'); const {default:ReactDOM}=await import('/node_modules/.vite/deps/react-dom_client.js');
    const {ThemeProvider,CssBaseline}=await import('/node_modules/.vite/deps/@mui_material.js'); const {staffTheme}=await import('/src/layout/staffTheme.ts');
    const {MaintenancePage}=await import('/src/pages/MaintenancePage.tsx'); const {AppShell}=await import('/src/layout/AppShell.tsx'); const {AuthProvider}=await import('/src/auth/AuthContext.tsx'); const {StableViewport}=await import('/src/components/StableViewport.tsx');
    ReactDOM.createRoot(document.getElementById('root')).render(React.createElement(ThemeProvider,{theme:staffTheme},React.createElement(CssBaseline),React.createElement(StableViewport),React.createElement(AuthProvider,null,React.createElement(AppShell,{activePage:'maintenance',onNavigate:()=>{},userName:'Maintenance supervisor',userRoles:['SuperAdministrator'],branchName:'Suva',divisionName:'Carpenters Fiji',onLogout:async()=>{}},React.createElement(MaintenancePage)))));
   </script></body></html>` }));
  await page.goto((process.env.CREMS_TEST_URL || 'http://localhost:5173') + '/__maintenance_workspace');
  await page.getByText('No jobs in this queue.', { exact: false }).waitFor();
  await page.getByRole('button', {name:/Overdue service/}).click();
  await page.getByRole('button', {name:'View service rules',exact:true}).click();
  await page.getByRole('heading', {name:'Preventive service rules'}).waitFor();
  await page.getByText('Configured meter interval: 5000 km', {exact:true}).waitFor();
  await page.getByRole('button', {name:'Close service rules'}).click();
  await page.getByRole('tab', {name:/Active work/}).click();
  console.log('PASS overdue-service action and readable preventive rules');
  await page.getByRole('button', { name: 'Log maintenance job', exact: true }).click();
  assert.equal(await page.getByRole('button', { name: 'Save maintenance job' }).isEnabled(), false);
  await page.getByLabel('Search or scan asset number').fill('VEH'); await page.getByRole('option', { name: /VEH-SUV-1001/ }).click();
  await page.getByLabel('Fault description / work required').fill('Oil leak reported during pre-hire inspection. Check filter seal and retest.');
  await page.getByLabel('Assigned technician').click(); await page.getByRole('option', { name: /Ravi Singh/ }).click();
  await page.getByRole('tab', { name: 'Expenses', exact: true }).click(); await page.getByLabel('Estimated cost (FJD)').fill('150');
  await page.getByRole('button', { name: 'Save maintenance job' }).click(); await page.getByRole('heading', { name: 'MNT-2026-TEST01' }).waitFor();
  await page.getByRole('tab', {name:'Job details',exact:true}).click();
  await page.getByRole('button', {name:'View originating inspection'}).click();
  await page.getByText('Leak identified at pre-hire inspection', {exact:true}).waitFor();
  await page.getByText('Oil leak check: Failed', {exact:true}).waitFor();
  await page.getByRole('button', {name:'Close inspection record'}).click();
  await page.getByRole('tab', {name:'Asset history',exact:true}).click();
  await page.getByText('Upcoming service and preventive rules', {exact:true}).waitFor();
  await page.getByText('Job audit trail', {exact:true}).click();
  await page.getByRole('button', {name:'Maintenance updated · Maintenance supervisor',exact:true}).click();
  await page.getByRole('heading', {name:'Audit record'}).waitFor();
  await page.getByText('Repair details recorded', {exact:true}).last().waitFor();
  await page.getByRole('button', {name:'Close audit record'}).click();
  await page.getByRole('tab', {name:'Job details',exact:true}).click();
  console.log('PASS originating inspection, named checklist responses, upcoming targets and audit record links');
  assert.equal(writes[0].assignedPersonnelId, 't1'); assert.equal(writes[0].hasEstimate, true); console.log('PASS searchable explicit asset selection, master technician and intake');
  await page.getByRole('button', { name: 'Start work', exact: true }).click(); await page.getByRole('button', { name: 'Confirm', exact: true }).click(); await page.getByRole('button', { name: 'Start work', exact: true }).waitFor({ state: 'hidden' });
  await page.getByRole('button', { name: 'Wait for parts', exact: true }).click(); await page.getByLabel('Reason for status change').fill('Filter delivery expected this afternoon.'); await page.getByRole('button', { name: 'Confirm', exact: true }).click(); await page.getByRole('button', { name: 'Wait for parts', exact: true }).waitFor({ state: 'hidden' });
  assert.equal(writes.at(-1).transitionReason, 'Filter delivery expected this afternoon.'); await page.getByRole('button',{name:'Resume work',exact:true}).waitFor(); console.log('PASS reasoned status actions and resume label');
  await page.getByRole('tab', { name: 'Expenses', exact: true }).click();
  await page.getByLabel('Labour hours', { exact: true }).fill('2'); await page.getByLabel('Labour hourly rate (FJD)').fill('30'); await page.getByLabel('Fuel (FJD)', { exact: true }).fill('5');
  await page.getByRole('button', { name: 'Save details', exact: true }).click(); await page.getByText('Record loaded', { exact: true }).waitFor(); assert.equal(jobs[0].actualCost, 65);
  await page.getByLabel('Calculate total from expense breakdown',{exact:true}).uncheck();
  await page.getByLabel('Total cost without breakdown (FJD)',{exact:true}).fill('100');
  await page.getByRole('button',{name:'Save details',exact:true}).click(); await page.getByText('Record loaded',{exact:true}).waitFor();
  assert.equal(writes.at(-1).labourHours,null); assert.equal(writes.at(-1).labourRate,null); assert.equal(jobs[0].actualCost,100);
  await page.getByLabel('Calculate total from expense breakdown',{exact:true}).check();
  console.log('PASS invoice-only total clears hidden labour inputs and persists');
  await page.getByLabel('Parts (FJD)', {exact:true}).fill('500');
  await page.getByLabel('Labour hours', {exact:true}).fill(''); await page.getByLabel('Labour hourly rate (FJD)').fill('');
  await page.getByLabel('Labour total (FJD)', {exact:true}).fill('50'); await page.getByLabel('Fuel (FJD)', {exact:true}).fill('0');
  await page.getByLabel('Supplier labour', {exact:true}).check();
  await page.getByText('Tax: FJD 68.75', {exact:true}).waitFor();
  const saveTax = async (mode, tax, total) => {
   await page.getByRole('button', {name:'Save details',exact:true}).click(); await page.getByText('Record loaded',{exact:true}).waitFor();
   assert.equal(writes.at(-1).taxMode, mode); assert.equal(writes.at(-1).taxCost,tax); assert.equal(jobs[0].actualCost,total);
  };
  await saveTax('Exclusive',68.75,618.75);
  await page.getByLabel('Tax treatment',{exact:true}).click(); await page.getByRole('option',{name:/Tax-inclusive/}).click();
  await page.getByText('Included tax: FJD 61.11 (already in expenses; not added again)',{exact:true}).waitFor(); await saveTax('Inclusive',61.11,550);
  await page.getByLabel('Tax treatment',{exact:true}).click(); await page.getByRole('option',{name:'No tax',exact:true}).click(); await saveTax('None',0,550);
  await page.getByLabel('Tax treatment',{exact:true}).click(); await page.getByRole('option',{name:/Manual tax override/}).click();
  await page.getByLabel('Tax (FJD)',{exact:true}).fill('70'); await page.getByLabel('Tax override reason',{exact:true}).fill('Match supplier invoice');
  await saveTax('Manual',70,620); assert.equal(writes.at(-1).taxOverrideReason,'Match supplier invoice');
  await page.getByLabel('Tax treatment',{exact:true}).click(); await page.getByRole('option',{name:/Tax-exclusive/}).click();
  await page.getByLabel('Tax rate (%)').fill('10'); await page.getByRole('button',{name:'Use division rate (12.5%)',exact:true}).click();
  assert.equal(await page.getByLabel('Tax rate (%)').inputValue(),'12.5');
  await page.getByLabel('Supplier labour',{exact:true}).uncheck(); await page.getByLabel('Parts (FJD)',{exact:true}).fill('0');
  await page.getByLabel('Labour hours',{exact:true}).fill('2'); await page.getByLabel('Labour hourly rate (FJD)').fill('30'); await page.getByLabel('Fuel (FJD)',{exact:true}).fill('5');
  await page.getByLabel('Purchased fuel',{exact:true}).check(); await saveTax('Exclusive',0.63,65.63);
  await page.getByLabel('Purchased fuel',{exact:true}).uncheck(); await saveTax('Exclusive',0,65);
  console.log('PASS persisted exclusive/inclusive/no-tax/manual tax, division default and optional fuel tax');
  await page.getByRole('tab', { name: 'Work and service targets' }).click(); await page.getByRole('button', { name: 'Manage parts and stock movements' }).click();
  await page.getByLabel('Issue stock', { exact: true }).click(); await page.getByRole('option', { name: /OIL-FILTER/ }).click(); await page.getByRole('button', { name: 'Issue parts', exact: true }).click(); await page.getByText(/OIL-FILTER: Issued 1/).waitFor(); assert.equal(available, 4);
  await page.getByLabel('Return unused stock', { exact: true }).click(); await page.getByRole('option', { name: /OIL-FILTER/ }).click(); await page.getByLabel('Return reason').fill('Unused sealed filter'); await page.getByRole('button', { name: 'Return parts to stock' }).click(); await page.getByText(/OIL-FILTER: Returned 1/).waitFor(); assert.equal(available, 5); await page.getByRole('button', { name: 'Close', exact: true }).click(); console.log('PASS labour and fuel totals, stock issue and audited returns');
  await page.getByLabel('Completion notes / work performed').fill('Replaced filter seal. Leak test and road test passed.'); await page.getByLabel('Final meter reading (km)', { exact: true }).fill('45020'); await page.getByLabel('Next service date').fill('2027-01-07'); await page.getByLabel('Next service threshold (km)').fill('50000');
  await page.getByRole('button', { name: 'Save details', exact: true }).click(); await page.getByText('Record loaded', { exact: true }).waitFor();
  await page.getByRole('tab', { name: 'Evidence', exact: true }).click(); await page.locator('input[type=file]').setInputFiles({ name: 'repair-photo.png', mimeType: 'image/png', buffer: Buffer.from('test-image') }); await page.getByRole('button', { name: /repair-photo.png/ }).waitFor();
  await page.getByRole('button', { name: 'Complete work', exact: true }).click(); await page.getByRole('button', { name: 'Confirm', exact: true }).click(); await page.getByText('Work completed', { exact: true }).waitFor(); assert.equal(asset.status, 'Inspection');
  await page.getByRole('tab', { name: 'Safety check', exact: true }).click(); assert.equal(await page.getByRole('button', { name: 'Confirm safety and return to service' }).isEnabled(), false);
  for (const check of checks) await page.getByLabel(check, { exact: true }).check(); await page.getByLabel('Safety check / test run notes').fill('Supervisor checked repairs and road test. Safe for hire.'); await page.getByRole('button', { name: 'Confirm safety and return to service' }).click(); await page.getByText(/^Safety passed:/).first().waitFor(); assert.equal(asset.status, 'Available'); console.log('PASS evidence, work completion and separate safety release');
  await page.getByRole('tab', { name: 'Job details', exact: true }).click(); jobs[0].version = version(); await page.getByRole('button', { name: 'Save details' }).click(); await page.getByRole('alert').filter({ hasText: 'This maintenance job changed.' }).waitFor();
  await page.getByRole('button', { name: 'Close maintenance workspace' }).click();
  await page.getByRole('tab', { name: 'Service history', exact: true }).click(); await page.getByRole('button', { name: /View history MNT/ }).waitFor();
  await page.getByLabel('Search maintenance jobs').fill('missing'); await page.getByText('No jobs match these filters.', { exact: false }).waitFor(); await page.getByRole('button', { name: 'Clear filters', exact: true }).click(); await page.getByRole('button', { name: /View history MNT/ }).waitFor();
  await page.reload(); await page.getByRole('button', { name: /View history MNT/ }).waitFor(); assert.equal(queries.at(-1).queue, 'History'); console.log('PASS stale edit, server filtering and session persistence');
  const output = process.env.CREMS_TEST_ARTIFACTS || path.join(process.cwd(), 'tmp', 'maintenance-browser'); await fs.mkdir(output, { recursive: true });
  await page.screenshot({ path: path.join(output, 'maintenance-desktop.png'), fullPage: true });
  for (const width of [768, 390]) {
   await page.setViewportSize({ width, height: 900 }); await page.getByRole('button', { name: /View history/ }).click(); await page.getByRole('tab', { name: 'Job details', exact: true }).waitFor();
   await page.waitForTimeout(350);
   if (await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth)) { console.error('OVERFLOW', width, await page.evaluate(() => [...document.querySelectorAll('body *')].filter(e => e.getBoundingClientRect().right > window.innerWidth + 1 && getComputedStyle(e).position !== 'fixed').slice(0,12).map(e => ({tag:e.tagName,classes:e.className,right:e.getBoundingClientRect().right,text:e.textContent?.slice(0,75)})))); await page.screenshot({path:path.join(output,'overflow-'+width+'.png'),fullPage:true}); }
   assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth), false); await page.getByLabel('Fault description / work required').fill('Unsaved change'); await page.getByRole('button', { name: 'Close maintenance workspace' }).click(); await page.getByRole('heading', { name: 'Discard unsaved changes?' }).waitFor(); await page.getByRole('button', { name: 'Discard changes', exact: true }).click();
   assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth), false);
  }
  await page.evaluate(() => window.scrollTo(0,0)); await page.screenshot({ path: path.join(output, 'maintenance-mobile.png'), fullPage: true }); console.log('PASS tablet/mobile cards, drawer fit, unsaved-change protection and no horizontal overflow');
  await page.setViewportSize({width:1440,height:900});
  const historyJob = {...jobs[0]};
  jobs = Array.from({length:12}, (_,i) => ({...historyJob,id:'row'+i,jobNumber:'MNT-LAYOUT-'+i,status:'Open',releasedAt:null}));
  await page.getByRole('button', {name:/Missed repair deadlines/}).click();
  await page.getByText('12 matching jobs', {exact:false}).waitFor();
  assert.equal(queries.at(-1).overdueRepairs,'true');
  const geometry = await page.locator('.MuiTableContainer-root:visible').evaluate(el => ({rows:el.querySelectorAll('tbody tr').length,height:el.clientHeight,scrollHeight:el.scrollHeight}));
  assert.equal(geometry.rows,12); assert.equal(geometry.height,geometry.scrollHeight); assert.ok(geometry.height > 900);
  console.log('PASS missed repair deadline filter and all 12 rows in page flow',geometry);
  await page.getByRole('button', {name:'Plan work MNT-LAYOUT-0',exact:true}).click();
  await page.getByRole('tab', {name:'Asset history',exact:true}).click();
  await page.getByRole('button', {name:'MNT-LAYOUT-1 · Open',exact:true}).click();
  await page.getByRole('heading', {name:'MNT-LAYOUT-1',exact:true}).waitFor();
  await page.getByLabel('Fault description / work required').fill('Unsaved related-record change');
  await page.getByRole('tab', {name:'Asset history',exact:true}).click();
  await page.getByRole('button', {name:'MNT-LAYOUT-2 · Open',exact:true}).click();
  await page.getByRole('heading', {name:'Discard unsaved changes?'}).waitFor();
  await page.getByRole('button', {name:'Discard changes',exact:true}).click();
  await page.getByRole('heading', {name:'MNT-LAYOUT-2',exact:true}).waitFor();
  await page.getByRole('button', {name:'Close maintenance workspace'}).click();
  console.log('PASS related service-record navigation and its unsaved-change guard');
  jobs = [historyJob];
  await page.getByRole('tab', {name:'Service history',exact:true}).click();
  await page.getByRole('button', {name:'Clear filters',exact:true}).click();
  await page.getByRole('button', {name:/View history/}).waitFor();
  jobs[0].status = 'Cancelled';
  await page.reload(); await page.getByRole('button',{name:/View history/}).click();
  await page.getByRole('dialog').getByText('Cancelled',{exact:true}).first().waitFor();
  await page.getByRole('dialog').getByText(/^Safety passed:/).first().waitFor();
  await page.getByRole('button',{name:'Close maintenance workspace'}).click();
  console.log('PASS cancellation stays visible alongside historical safety release');
  denied = true; await page.reload(); await page.getByRole('button', { name: /View history/ }).click(); await page.getByRole('tab', { name: 'Job details', exact: true }).waitFor(); assert.equal(await page.getByRole('tab', { name: 'Expenses', exact: true }).count(), 0); assert.equal(await page.getByRole('button', { name: 'Reopen work', exact: true }).count(), 0); console.log('PASS permission-controlled expenses and completion actions');
  assert.deepEqual(errors, []); console.log('PASS no uncaught JavaScript errors');
 } finally { await browser.close(); }
})().catch(e => { console.error(e); process.exit(1); });
