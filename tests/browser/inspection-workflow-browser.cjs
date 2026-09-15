const {chromium}=require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const assert=require('node:assert/strict');
(async()=>{
const browser=await chromium.launch({channel:'chrome',headless:true});const page=await browser.newPage({viewport:{width:1400,height:1000}});
let phase='pickup';const submissions=[];
const row={id:'00000000-0000-0000-0000-000000000001',bookingNumber:'TEST-001',status:'Confirmed',customerName:'Test Customer',branchName:'Test Branch',assetNumber:'TEST-ASSET',assetName:'Test bin',assetCategoryCode:'BIG_BIN',assetCategory:'Big bin',hasProfessionalPersonnel:false,startAt:'2026-09-15T00:00:00Z',endAt:'2026-09-17T00:00:00Z',hasAgreement:false,agreementStatus:'Not signed',amountPaid:0,depositRequired:0,bondStatus:'NotRequired',bondAmountHeld:0,bondDeductionAmount:0,bondRefundAmount:0,preHireInspectionComplete:false,returnInspectionComplete:false};
await page.route('**/api/**',async r=>{
const url=r.request().url();if(!new URL(url).pathname.startsWith("/api/")){await r.continue();return}
if(r.request().method()==='POST'){submissions.push({url,body:r.request().postDataJSON()});await r.fulfill({json:{}});return}
if(url.includes('inspection-context')){await r.fulfill({json:{templates:[{id:'template',name:'Configured inspection checklist',stage:phase==='pickup'?'PreHire':'PostHire',checklistJson:JSON.stringify([{section:'Accessories',items:[{label:'Keys'},{label:'Tools'}]}])}],preHire:phase==='return'?{evidenceJson:JSON.stringify({photos:submissions[0].body.evidenceDataUrls})}:null}});return}
if(url.includes('work-queue')){await r.fulfill({json:{items:[{...row,status:phase==='pickup'?'Confirmed':'ConvertedToRental'}],counts:{pickupToday:1,onHire:1,dueToday:0,overdue:0,returnInProgress:0,recentlyCompleted:0},page:1,pageSize:25,total:1}});return}
if(url.includes('rental-agreements')){await r.fulfill({json:{customer:{name:'Test Customer'},asset:{name:'Test bin',assetNumber:'TEST-ASSET'},rental:{startAt:row.startAt,endAt:row.endAt},pricing:{total:100,depositRequired:0}}});return}
await r.fulfill({json:[]});
});
await page.route('**/__workflow_test',r=>r.fulfill({contentType:'text/html',body:`<div id="root"></div><script type="module">
import RefreshRuntime from '/@react-refresh';RefreshRuntime.injectIntoGlobalHook(window);window.$RefreshReg$=()=>{};window.$RefreshSig$=()=>type=>type;window.__vite_plugin_react_preamble_installed__=true;
const {default:React}=await import('/node_modules/.vite/deps/react.js');const {default:{createRoot}}=await import('/node_modules/.vite/deps/react-dom_client.js');const {RentalsPage}=await import('/src/pages/RentalsPage.tsx');createRoot(document.getElementById('root')).render(React.createElement(RentalsPage));
</script>`}));
const next=()=>page.getByRole('button',{name:'Continue',exact:true}).click();
await page.goto('http://localhost:5173/__workflow_test');
await page.getByRole('button',{name:'Begin pickup'}).first().click();await next();
await page.getByLabel('Scan or enter asset QR number').fill('TEST-ASSET');await next();
await page.getByLabel('Customer identification sighted and matches booking').check();await next();await next();
for(const box of await page.getByRole('checkbox').all())await box.check();
await page.getByText('Configured inspection checklist',{exact:true}).waitFor();await page.getByLabel('Pre-hire condition and checklist result').fill('All items checked');
await page.getByLabel('Inspection photo files').setInputFiles('src/crems-web/public/catalog/suv.jpg');
await page.getByAltText('Inspection photo 1',{exact:true}).waitFor();await next();await next();
await page.getByLabel('Customer full legal name').fill('Test Customer');
const rect=await page.locator('canvas').boundingBox();await page.mouse.move(rect.x+20,rect.y+20);await page.mouse.down();await page.mouse.move(rect.x+100,rect.y+60);await page.mouse.up();
await page.getByLabel('Customer accepts the agreement, charges and recorded asset condition').check();await next();
await page.getByLabel('I witnessed the customer signature, completed the checks and authorize asset release').check();
await page.getByRole('button',{name:'Approve & check out asset'}).click();await page.waitForTimeout(300);
assert.equal(submissions.length,1);assert.equal(submissions[0].body.licenceExpiry,null);assert.equal(submissions[0].body.meterReading,null);assert(submissions[0].body.evidenceDataUrls[0].startsWith('data:image/jpeg'));assert(submissions[0].body.customerSignatureDataUrl);
phase='return';await page.reload();await page.getByRole('button',{name:'Begin return'}).first().click();
await page.getByLabel('Scan or enter returned asset QR number').fill('TEST-ASSET');await next();await next();await next();
for(const box of await page.getByRole('checkbox').all())await box.check();
await page.getByLabel('Post-hire inspection result').fill('All returned');await page.getByRole('button',{name:'Front left',exact:true}).click();await page.getByLabel('New damage or fault').fill('Scratch on front left');await page.getByLabel('Accessories and quantities').fill('2 keys, 1 tool kit');
await page.getByLabel('Inspection photo files').setInputFiles('src/crems-web/public/catalog/sedan.jpg');await page.getByAltText('Inspection photo 1',{exact:true}).waitFor();
await next();await page.getByAltText('Pre-hire photos 1',{exact:true}).waitFor();await page.getByAltText('Return photos 1',{exact:true}).waitFor();await next();await next();await next();
await page.getByLabel('Customer acknowledgement name').fill('Test Customer');const returnRect=await page.locator('canvas').boundingBox();await page.mouse.move(returnRect.x+20,returnRect.y+20);await page.mouse.down();await page.mouse.move(returnRect.x+100,returnRect.y+50);await page.mouse.up();await page.getByLabel('Customer acknowledges the return record, charges and refundable bond settlement').check();await next();
await page.getByRole('button',{name:'Complete return & create invoice'}).click();await page.waitForTimeout(300);
assert.equal(submissions.length,2);assert.deepEqual(submissions[1].body.damageZones,['Front left']);assert.equal(submissions[1].body.damageNotes,'Scratch on front left');assert(submissions[1].body.signatureDataUrl);assert.equal(submissions[1].body.accessoryNotes,'2 keys, 1 tool kit');assert.equal(submissions[1].body.meterReading,null);assert(submissions[1].body.evidenceDataUrls[0].startsWith('data:image/jpeg'));assert(submissions[1].body.checklistItems.length>0);
console.log('PASS: complete pickup and return UI with photo evidence, checklist, drawn pickup signature, acknowledgement and nullable non-metered fields. API writes mocked.');
await browser.close();
})().catch(e=>{console.error(e);process.exit(1)});
