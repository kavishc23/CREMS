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
if(new URL(url).pathname==='/api/auth/session'){await r.fulfill({json:{ id:'test-staff', fullName:'Test Officer', email:'test@example.invalid', roles:['RentalOfficer'], pages:['rentals'] }});return}
if(new URL(url).pathname==='/api/notifications'){await r.fulfill({json:{items:[],unreadCount:0,total:0,page:1,pageSize:20}});return}
if(url.includes('inspection-context')){await r.fulfill({json:{templates:[{id:'template',name:'Configured inspection checklist',stage:phase==='pickup'?'PreHire':'PostHire',checklistJson:JSON.stringify([{section:'Accessories',items:[{label:'Keys'},{label:'Tools'}]}])}],preHire:submissions.length?{conditionNotes:submissions[0].body.conditionNotes,meterReading:null,fuelLevelPercent:null,evidenceJson:JSON.stringify({photos:submissions[0].body.evidenceDataUrls,checklist:submissions[0].body.checklistItems})}:null}});return}
if(url.includes('work-queue')){assert.equal(new URL(url).searchParams.get('bookingId'),row.id);await r.fulfill({json:{items:[{...row,status:phase==='pickup'?'Confirmed':'ConvertedToRental'}],counts:{pickupToday:1,onHire:1,dueToday:0,overdue:0,returnInProgress:0,recentlyCompleted:0},page:1,pageSize:25,total:1}});return}
if(url.includes('pickup-readiness')){await r.fulfill({json:{canPickup:true,reasons:[]}});return}
if(url.includes('rental-agreements')){await r.fulfill({json:{customer:{name:'Test Customer'},asset:{name:'Test bin',assetNumber:'TEST-ASSET'},rental:{startAt:row.startAt,endAt:row.endAt},pricing:{total:100,depositRequired:0}}});return}
await r.fulfill({json:[]});
});
const next=()=>page.getByRole('button',{name:'Continue',exact:true}).click();
await page.goto((process.env.CREMS_TEST_URL||'http://localhost:5173')+'/staff/hire-operations?bookingId=00000000-0000-0000-0000-000000000001');await page.getByRole('button',{name:'Begin pickup'}).first().click();await page.getByLabel('Customer identification sighted and matches the booking').check();await page.getByLabel(/I checked the physical label matches/).check();await next();
await page.getByRole('button',{name:'Mark all as passed',exact:true}).click();
await page.getByLabel('Pre-hire condition and checklist result').fill('Prepared ahead of pickup');
await page.getByLabel('Inspection photo files').setInputFiles('src/crems-web/public/catalog/suv.jpg');
await page.getByAltText('Inspection photo 1',{exact:true}).waitFor();
await page.getByRole('button',{name:'Save inspection and continue',exact:true}).click();
await page.waitForTimeout(200);
assert.equal(submissions.length,1);
assert(submissions[0].url.endsWith('/pre-hire-inspection'));
assert.equal(submissions[0].body.conditionNotes,'Prepared ahead of pickup');
assert(submissions[0].body.evidenceDataUrls.length===1);
assert.equal(submissions[0].body.agentApproved,undefined);
await page.getByRole("button",{name:"Close",exact:true}).last().click();
await page.getByRole("button",{name:/Discard/}).click();
await page.reload();await page.getByRole('button',{name:'Begin pickup'}).first().click();await page.getByLabel('Customer identification sighted and matches the booking').check();await page.getByLabel(/I checked the physical label matches/).check();await next();
await page.getByAltText('Inspection photo 1',{exact:true}).waitFor();
assert.equal(await page.getByLabel('Pre-hire condition and checklist result').inputValue(),'Prepared ahead of pickup');
console.log('PASS: existing hire workflow saves inspection with an existing agreement independently of checkout and reloads photos/checklist/notes. API writes mocked.');
await browser.close();
})().catch(e=>{console.error(e);process.exit(1)});
