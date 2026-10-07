const {chromium}=require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const assert=require('node:assert/strict');
(async()=>{
 const browser=await chromium.launch({channel:'chrome',headless:true});
 try {
 const page=await browser.newPage({viewport:{width:1400,height:1000}});page.setDefaultTimeout(15000);
 let jobs=[],writes=[],docs=[],ledger=[]; let available=5; const errors=[];page.on('pageerror',e=>errors.push(e.message));
 const assets=[{id:'a1',assetNumber:'AUDIT-01',name:'Audit vehicle',branchName:'Test branch',status:'Available',isActive:true,currentMeterReading:100,nextServiceDate:'2020-01-01'},{id:'a2',assetNumber:'HIRED-02',name:'Hired vehicle',branchName:'Test branch',status:'Rented',isActive:true}];
 await page.route('**/api/**',async r=>{
 const path=new URL(r.request().url()).pathname;const method=r.request().method();
 if(path==='/api/assets')return r.fulfill({json:assets,headers:{'x-total-count':'2'}});
 if(path.includes('/documents')){if(method==='POST'){docs.push({id:'d1',fileName:'invoice.pdf',type:'FaultPhoto'});return r.fulfill({json:docs[0]});}return r.fulfill({json:docs});}
 if(path.includes('/parts')){
 if(method==='GET')return r.fulfill({json:{stock:[{id:'p1',partNumber:'FILTER',name:'Filter',available,unitCost:10}],ledger,canManage:true,canIssue:true}});
 const p=r.request().postDataJSON();const returning=path.endsWith('/return');
 ledger.push({id:'usage'+ledger.length,inventoryPartId:'p1',partNumber:'FILTER',quantity:returning?-p.quantity:p.quantity,unitCost:10,createdAt:new Date().toISOString()});available+=returning?p.quantity:-p.quantity;
 return r.fulfill({json:{}});
 }
 if(path.startsWith('/api/maintenance-jobs')){
 if(method==='GET')return r.fulfill({json:jobs});
 const p=r.request().postDataJSON();writes.push(p);
 if(method==='PUT' && p.expectedVersion!==jobs[0].version)return r.fulfill({status:409,json:{message:'This maintenance job changed since you opened it. Refresh and reopen the job before saving.'}});
 if(method==='POST')jobs.push({...p,id:'j1',version:'2026-10-06T00:00:00.000Z',jobNumber:'MNT-AUDIT',assetNumber:'AUDIT-01',assetName:'Audit vehicle',branchName:'Test branch',reportedAt:new Date().toISOString(),status:'Open'});
 else jobs[0]={...jobs[0],...p,version:new Date(Date.UTC(2026,9,6,0,0,writes.length)).toISOString(),actualCost:p.useDetailedCosts?p.partsCost+p.labourCost+p.transportCost+p.externalServiceCost+(p.taxMode==='Inclusive'?0:p.taxCost)+p.otherCost:p.actualCost};return r.fulfill({json:{id:'j1'}});
 }
 if(!path.startsWith('/api/'))return r.continue();
 throw new Error('Unexpected API request: '+method+' '+path);
 });
 await page.route('**/__maintenance_audit',r=>r.fulfill({contentType:'text/html',body:`<div id="root"></div><script type="module">import RefreshRuntime from '/@react-refresh';RefreshRuntime.injectIntoGlobalHook(window);window.$RefreshReg$=()=>{};window.$RefreshSig$=()=>type=>type;window.__vite_plugin_react_preamble_installed__=true;const {default:React}=await import('/node_modules/.vite/deps/react.js');const {default:{createRoot}}=await import('/node_modules/.vite/deps/react-dom_client.js');const {MaintenancePage}=await import('/src/pages/MaintenancePage.tsx');createRoot(document.getElementById('root')).render(React.createElement(MaintenancePage));</script>`}));
 await page.goto((process.env.CREMS_TEST_URL || 'http://localhost:5173') + '/__maintenance_audit');await page.getByText('No maintenance jobs found.').waitFor();await page.getByText(/Service due: AUDIT-01/).waitFor();
 await page.getByRole('button',{name:'Log job',exact:true}).click();await page.getByRole('dialog').getByRole('combobox').first().click();assert.equal(await page.getByRole('option').count(),1);await page.getByRole('option').click();
 await page.getByLabel('Fault description / work required').fill('Audit repair');await page.getByLabel('Assigned technician').fill('Audit Technician');await page.getByLabel('Estimated cost (FJD)').fill('50');await page.getByRole('button',{name:'Save maintenance job'}).click();await page.getByText('MNT-AUDIT',{exact:true}).waitFor();assert.equal(writes[0].estimatedCost,50);console.log('PASS create, asset eligibility, date service alert');
 for(const status of ['In Progress','Waiting For Parts','Completed','Cancelled','Open']){
 await page.getByRole('tab',{name:'All job history'}).click();await page.getByRole('button',{name:'Edit maintenance job'}).click();await page.getByLabel('Status',{exact:true}).click();await page.getByRole('option',{name:status,exact:true}).click();await page.getByRole('button',{name:'Save maintenance job'}).click();await page.getByRole('dialog').waitFor({state:'hidden'});assert.equal(writes.at(-1).status,status.replaceAll(' ',''));
 }console.log('PASS all five status payloads');
 await page.getByRole('button',{name:'Edit maintenance job'}).click();await page.getByLabel('Calculate total from expense breakdown').check();await page.getByLabel('Parts (FJD)',{exact:true}).fill('10');await page.getByLabel('Labour (FJD)',{exact:true}).fill('20');await page.getByLabel('Completion notes / work performed').fill('Audit work');await page.locator('input[type=file]').setInputFiles({name:'invoice.pdf',mimeType:'application/pdf',buffer:Buffer.from('%PDF-1.7\nAudit')});await page.getByRole('button',{name:/invoice.pdf/}).waitFor();await page.getByRole('button',{name:'Save maintenance job'}).click();await page.getByRole('dialog').waitFor({state:'hidden'});assert.equal(writes.at(-1).partsCost,10);assert.equal(writes.at(-1).labourCost,20);console.log('PASS cost payload, notes, attachment upload UI');
 await page.getByPlaceholder('Search job, asset, branch or technician').fill('missing');await page.getByText('No maintenance jobs found.').waitFor();await page.getByPlaceholder('Search job, asset, branch or technician').fill('Audit Technician');await page.getByText('MNT-AUDIT',{exact:true}).waitFor();console.log('PASS search and history');
 await page.getByRole('button',{name:'Edit maintenance job'}).click();
 await page.getByRole('status').filter({hasText:'Actual total: FJD 30.00'}).waitFor();
 await page.getByLabel('Parts (FJD)',{exact:true}).fill('0');
 await page.getByLabel('Labour (FJD)',{exact:true}).fill('0');
 await page.getByRole('status').filter({hasText:'Actual total: FJD 0.00'}).waitFor();
 await page.getByRole('button',{name:'Save maintenance job'}).click();await page.getByRole('dialog').waitFor({state:'hidden'});
 assert.equal(writes.at(-1).useDetailedCosts,true);assert.equal(jobs[0].actualCost,0);
 await page.getByRole('button',{name:'Edit maintenance job'}).click();
 await page.getByLabel('Calculate total from expense breakdown').uncheck();
 await page.getByLabel('Total cost without breakdown (FJD)').fill('45');
 await page.getByRole('status').filter({hasText:'Actual total: FJD 45.00'}).waitFor();
 await page.getByRole('button',{name:'Save maintenance job'}).click();await page.getByRole('dialog').waitFor({state:'hidden'});
 assert.equal(writes.at(-1).useDetailedCosts,false);assert.equal(jobs[0].actualCost,45);
 console.log('PASS calculated total, clearing detailed costs, and manual invoice total');
 await page.getByRole('button',{name:'Edit maintenance job'}).click();
 jobs[0]={...jobs[0],version:'2026-10-07T00:00:00.000Z'};
 await page.getByRole('button',{name:'Save maintenance job'}).click();
 await page.getByRole('alert').filter({hasText:'This maintenance job changed since you opened it.'}).waitFor();
 assert.equal(await page.getByRole('dialog').isVisible(),true);
 await page.getByRole('button',{name:'Cancel',exact:true}).click();
 console.log('PASS stale-form version is submitted and conflicts retain the editor');
 await page.reload();await page.getByText('MNT-AUDIT',{exact:true}).waitFor();
 await page.getByRole('button',{name:'Edit maintenance job'}).click();
 await page.getByLabel('Calculate total from expense breakdown').check();
 await page.getByLabel('Parts (FJD)',{exact:true}).fill('500');await page.getByLabel('Labour (FJD)',{exact:true}).fill('50');
 await page.getByRole('combobox',{name:/Tax treatment/}).click();await page.getByRole('option',{name:/Tax-exclusive/}).click();
 await page.getByRole('spinbutton',{name:/Tax rate/}).fill('12.5');await page.getByLabel('Supplier labour',{exact:true}).check();
 await page.getByRole('status').filter({hasText:'Actual total: FJD 618.75'}).waitFor();
 await page.getByRole('combobox',{name:/Tax treatment/}).click();await page.getByRole('option',{name:/Tax-inclusive/}).click();
 await page.getByRole('status').filter({hasText:'Actual total: FJD 550.00'}).waitFor();await page.getByRole('status').filter({hasText:'Included tax: FJD 61.11'}).waitFor();
 await page.getByRole('button',{name:'Save maintenance job'}).click();await page.getByRole('dialog').waitFor({state:'hidden'});assert.equal(jobs[0].actualCost,550);assert.equal(jobs[0].taxMode,'Inclusive');
 await page.getByRole('button',{name:'Edit maintenance job'}).click();await page.getByRole('combobox',{name:/Tax treatment/}).click();await page.getByRole('option',{name:'No tax',exact:true}).click();
 await page.getByRole('status').filter({hasText:'Tax: FJD 0.00'}).waitFor();
 await page.getByRole('combobox',{name:/Tax treatment/}).click();await page.getByRole('option',{name:/Manual tax override/}).click();await page.getByLabel('Tax (FJD)',{exact:true}).fill('70');
 await page.getByRole('button',{name:'Save maintenance job'}).click();await page.getByText('Enter a reason for the manual tax override.',{exact:true}).waitFor();
 await page.getByLabel('Tax override reason',{exact:true}).fill('Match supplier invoice');await page.getByRole('button',{name:'Save maintenance job'}).click();await page.getByRole('dialog').waitFor({state:'hidden'});
 assert.equal(jobs[0].actualCost,620);assert.equal(jobs[0].taxOverrideReason,'Match supplier invoice');
 await page.getByRole('tab',{name:/Service schedules/}).click();await page.getByText(/Non-cancelled preventive jobs and jobs/).waitFor();
 await page.getByRole('tab',{name:/Completed jobs/}).click();await page.getByText(/An asset may still be unavailable/).waitFor();
 await page.getByRole('tab',{name:'All job history',exact:true}).click();
 console.log('PASS exclusive, inclusive, no-tax, override reason, persistence, and tab explanations');

 await page.getByRole('button',{name:'Parts',exact:true}).click();
 await page.getByLabel('Issue stock',{exact:true}).click();await page.getByRole('option',{name:/FILTER/}).click();
 await page.getByRole('button',{name:'Issue parts',exact:true}).click();await page.getByText(/FILTER: Issued 1/).waitFor();assert.equal(available,4);
 await page.getByLabel('Return unused stock',{exact:true}).click();await page.getByRole('option',{name:/FILTER/}).click();
 await page.getByLabel('Return reason',{exact:true}).fill('Unused sealed filter');await page.getByRole('button',{name:'Return parts to stock',exact:true}).click();
 await page.getByText(/FILTER: Returned 1/).waitFor();assert.equal(available,5);await page.getByRole('button',{name:'Close',exact:true}).click();
 console.log('PASS stock issue, unused-parts return, and movement history');
 await page.setViewportSize({width:390,height:844});await page.getByRole('button',{name:'Edit maintenance job'}).click();await page.getByLabel('Fault description / work required').waitFor();assert.equal(await page.getByRole('button',{name:'Save maintenance job'}).isEnabled(),true);assert.deepEqual(errors,[]);console.log('PASS mobile dialog and no JS errors');
 } finally {await browser.close();}
})().catch(e=>{console.error(e);process.exit(1)});
