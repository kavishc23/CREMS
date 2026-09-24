const {chromium}=require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const assert=require('node:assert/strict');
(async()=>{
const browser=await chromium.launch({channel:'chrome',headless:true});const page=await browser.newPage({viewport:{width:1400,height:1000}});
let phase='pickup';const submissions=[];
const row={id:'00000000-0000-0000-0000-000000000001',bookingNumber:'TEST-001',status:'Confirmed',customerName:'Test Customer',branchName:'Test Branch',assetNumber:'TEST-ASSET',assetName:'Test bin',assetCategoryCode:'BIG_BIN',assetCategory:'Big bin',hasProfessionalPersonnel:false,startAt:'2026-09-15T00:00:00Z',endAt:'2026-09-17T00:00:00Z',hasAgreement:true,agreementStatus:'Not signed',amountPaid:0,depositRequired:0,bondStatus:'NotRequired',bondAmountHeld:0,bondDeductionAmount:0,bondRefundAmount:0,preHireInspectionComplete:false,returnInspectionComplete:false};
await page.route('**/api/**',async r=>{
const url=r.request().url();if(!new URL(url).pathname.startsWith("/api/")){await r.continue();return}
if(r.request().method()==='POST'){submissions.push({url,body:r.request().postDataJSON()});await r.fulfill({json:{}});return}
if(url.includes('return-charges')){await r.fulfill({json:{returnedAt:new Date().toISOString(),lateFee:0}});return}
if(url.includes('inspection-context')){await r.fulfill({json:{templates:[{id:'template',name:'Configured inspection checklist',stage:phase==='pickup'?'PreHire':'PostHire',checklistJson:JSON.stringify([{section:'Accessories',items:[{label:'Keys'},{label:'Tools'}]}])}],preHire:submissions.length?{conditionNotes:submissions[0].body.conditionNotes,meterReading:null,fuelLevelPercent:null,evidenceJson:JSON.stringify({photos:submissions[0].body.evidenceDataUrls,checklist:submissions[0].body.checklistItems})}:null}});return}
if(url.includes('work-queue')){assert.equal(new URL(url).searchParams.get('bookingId'),row.id);await r.fulfill({json:{items:[{...row,status:phase==='pickup'?'Confirmed':'ConvertedToRental'}],counts:{pickupToday:1,onHire:1,dueToday:0,overdue:0,returnInProgress:0,recentlyCompleted:0},page:1,pageSize:25,total:1}});return}
if(url.includes('rental-agreements')){await r.fulfill({json:{customer:{name:'Test Customer'},asset:{name:'Test bin',assetNumber:'TEST-ASSET'},rental:{startAt:row.startAt,endAt:row.endAt},pricing:{total:100,depositRequired:0}}});return}
await r.fulfill({json:[]});
});
await page.route('**/__workflow_test*',r=>r.fulfill({contentType:'text/html',body:`<div id="root"></div><script type="module">
import RefreshRuntime from '/@react-refresh';RefreshRuntime.injectIntoGlobalHook(window);window.$RefreshReg$=()=>{};window.$RefreshSig$=()=>type=>type;window.__vite_plugin_react_preamble_installed__=true;
const {default:React}=await import('/node_modules/.vite/deps/react.js');const {default:{createRoot}}=await import('/node_modules/.vite/deps/react-dom_client.js');const {RentalsPage}=await import('/src/pages/RentalsPage.tsx');createRoot(document.getElementById('root')).render(React.createElement(RentalsPage));
</script>`}));
const next=()=>page.getByRole('button',{name:'Continue',exact:true}).click();
await page.goto('http://localhost:5173/__workflow_test?bookingId=00000000-0000-0000-0000-000000000001');await page.getByRole('button',{name:'Begin pickup'}).first().click();await next();
await page.getByLabel('Scan or enter asset QR number').fill('TEST-ASSET');await next();await page.getByLabel('Customer identification sighted and matches booking').check();await next();await next();
for(const box of await page.getByRole('checkbox').all())await box.check();
await page.getByLabel('Pre-hire condition and checklist result').fill('Prepared ahead of pickup');
await page.getByLabel('Inspection photo files').setInputFiles('src/crems-web/public/catalog/suv.jpg');
await page.getByAltText('Inspection photo 1',{exact:true}).waitFor();
await page.getByRole('button',{name:'Save pre-hire inspection',exact:true}).click();
await page.waitForTimeout(200);
assert.equal(submissions.length,1);
assert(submissions[0].url.endsWith('/pre-hire-inspection'));
assert.equal(submissions[0].body.conditionNotes,'Prepared ahead of pickup');
assert(submissions[0].body.evidenceDataUrls.length===1);
assert.equal(submissions[0].body.agentApproved,undefined);
await page.reload();await page.getByRole('button',{name:'Begin pickup'}).first().click();await next();
await page.getByLabel('Scan or enter asset QR number').fill('TEST-ASSET');await next();await page.getByLabel('Customer identification sighted and matches booking').check();await next();await next();
await page.getByAltText('Inspection photo 1',{exact:true}).waitFor();
assert.equal(await page.getByLabel('Pre-hire condition and checklist result').inputValue(),'Prepared ahead of pickup');
console.log('PASS: existing hire workflow saves inspection with an existing agreement independently of checkout and reloads photos/checklist/notes. API writes mocked.');
await browser.close();
})().catch(e=>{console.error(e);process.exit(1)});
