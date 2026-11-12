const {chromium}=require(process.env.PLAYWRIGHT_MODULE||'playwright');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const base=process.env.CREMS_TEST_URL||'http://localhost:5173';
(async()=>{
 const browser=await chromium.launch({channel:'chrome',headless:true});
 try{
  const page=await browser.newPage({viewport:{width:1440,height:1000}});let phase='pickup';page.setDefaultTimeout(12000);const writes=[];page.on('pageerror',error=>console.error('PAGE ERROR:',error.message));
  const row={id:'progress-vehicle',bookingNumber:'CM-BOOK-1001',status:'Confirmed',customerName:'Customer One',branchName:'Suva',assetNumber:'CM-SED-001',assetName:'Toyota Corolla Sedan',assetCategoryCode:'SEDAN',assetCategory:'Sedan',hasProfessionalPersonnel:false,startAt:new Date().toISOString(),endAt:new Date(Date.now()+86400000).toISOString(),hasAgreement:false,agreementStatus:'Not signed',amountPaid:0,depositRequired:0,bondStatus:'NotRequired',bondAmountHeld:0,bondDeductionAmount:0,bondRefundAmount:0,preHireInspectionComplete:false,returnInspectionComplete:false,preHireMeterReading:38000,preHireFuelLevelPercent:90};
  await page.route(/^https?:\/\/[^/]+\/api\//,async route=>{
   const path=new URL(route.request().url()).pathname;
   if(route.request().method()==='POST'){writes.push(path);await route.fulfill({json:{}});return;}
   let data=[];
   if(path.endsWith('/work-queue'))data={items:[{...row,status:phase==='pickup'?'Confirmed':'ConvertedToRental'}],counts:{pickupToday:1,onHire:phase==='return'?1:0,dueToday:0,overdue:0,returnInProgress:0,recentlyCompleted:0},page:1,pageSize:25,total:1};
   else if(path.endsWith('/replacement-assets'))data=[{id:'replacement',assetNumber:'CM-SED-002',name:'Toyota Corolla'}];
   else if(path.endsWith('/pickup-readiness'))data={canPickup:true,reasons:[]};
   else if(path.endsWith('/inspection-context'))data={templates:[{id:'checklist',name:'Sedan condition checklist',stage:phase==='pickup'?'PreHire':'PostHire',checklistJson:JSON.stringify([{section:'Vehicle',items:[{label:'Tyres checked'},{label:'Body panels checked'}]}])}],driverLicence:{number:'1234567',licenceClass:'2',expiry:'2028-12-31'},requiresDrivingLicence:true,tracksMeter:true,tracksFuel:true,meterUnit:'km',assetCurrentMeterReading:38000,preHire:null};
   else if(path.endsWith('/return-charges'))data={returnedAt:new Date().toISOString(),lateFee:0,multiplier:1,hireSubtotal:0,taxableSubtotal:0,taxRate:12.5,paidHire:0};
   else if(path.includes('/rental-agreements/'))data={customer:{name:row.customerName},asset:{name:row.assetName,assetNumber:row.assetNumber},rental:{startAt:row.startAt,endAt:row.endAt},pricing:{total:0,depositRequired:0}};
   else if(path==='/api/assets')data=[{assetNumber:row.assetNumber,currentMeterReading:38000}];
   await route.fulfill({json:data});
  });
  await page.route('**/__rental_progress_test',route=>route.fulfill({contentType:'text/html',body:`<div id="root"></div><script type="module">
  import RefreshRuntime from '/@react-refresh';RefreshRuntime.injectIntoGlobalHook(window);window.$RefreshReg$=()=>{};window.$RefreshSig$=()=>type=>type;window.__vite_plugin_react_preamble_installed__=true;
  const {default:React}=await import('/node_modules/.vite/deps/react.js');const {default:{createRoot}}=await import('/node_modules/.vite/deps/react-dom_client.js');const {RentalsPage}=await import('/src/pages/RentalsPage.tsx');createRoot(document.getElementById('root')).render(React.createElement(RentalsPage));</script>`}));
  const open=async()=>{await page.getByRole('button',{name:phase==='pickup'?'Begin pickup':'Begin return',exact:true}).first().click();await page.getByRole('button',{name:'Save progress',exact:true}).waitFor();};
  const next=()=>page.getByRole('button',{name:'Continue',exact:true}).click();
  const save=async()=>{await page.getByRole('button',{name:'Save progress',exact:true}).click();await page.getByText(/Progress saved on this device/).waitFor();};
  await page.goto(base+'/__rental_progress_test');await open();console.log('Pickup opened');
  await page.getByLabel('Customer identification sighted and matches booking').check();
  await page.getByLabel('Driver licence sighted and valid').check();
  await page.getByLabel('Licence number').fill('1234567');
  await page.getByLabel('Licence classes').click();await page.getByRole('option').first().click();await page.keyboard.press('Escape');
  await page.getByLabel('Expiry').fill('2028-12-31');await next();console.log('Inspection opened');
  await page.getByLabel('Usage reading (km)').fill('38100');await page.getByLabel('Fuel level (%)').fill('90');
  await page.getByText('Tyres checked',{exact:true}).locator('..').getByRole('combobox').click();await page.getByRole('option',{name:'Passed',exact:true}).click();await page.getByRole('listbox').waitFor({state:'hidden'});
  await page.getByLabel('Pre-hire condition and checklist result').fill('Body inspected; lights pending.');
  await page.getByLabel('Accessories and quantities').fill('Two keys, spare tyre and jack.');
  await page.getByLabel('Inspection photo files').setInputFiles('src/crems-web/public/catalog/sedan.jpg');await page.getByAltText('Inspection photo 1').waitFor();

  await page.getByText('Body panels checked',{exact:true}).locator('..').getByRole('combobox').click();await page.getByRole('option',{name:'Issue found',exact:true}).click();
  await page.getByLabel('Existing damage',{exact:true}).fill('Existing small cosmetic scratch on door.');
  assert(await page.getByRole('button',{name:'Continue',exact:true}).isDisabled());
  await page.getByLabel('Customer agrees to continue with the recorded existing damage').check();
  await page.getByLabel('I have checked the faults and confirm the asset is safe and suitable for this hire').check();
  await page.getByLabel('Existing damage acceptance / customer arrangement').fill('Done');
  assert(await page.getByRole('button',{name:'Continue',exact:true}).isDisabled());
  await page.getByText(/Add an acceptance note of at least 10 characters/).waitFor();
  await page.getByLabel('Existing damage acceptance / customer arrangement').fill('Customer inspected and accepts the old cosmetic door scratch.');
  assert(await page.getByRole('button',{name:'Continue',exact:true}).isEnabled());
  console.log('Damage acceptance checked');await page.getByRole('button',{name:'Find an available replacement',exact:true}).click();
  await page.getByLabel('Replacement asset').click();await page.getByRole('option',{name:'CM-SED-002 · Toyota Corolla',exact:true}).click();
  await page.getByLabel('Replacement reason and customer agreement').fill('Customer agrees to the alternative inspected Corolla.');
  await page.getByRole('button',{name:'Transfer booking to this asset',exact:true}).click();
  await page.getByRole('dialog').waitFor({state:'hidden'});
  assert(writes.some(path=>path.endsWith('/exception-resolution')));
  assert(!writes.some(path=>path.endsWith('/pickup')),'Selecting a replacement must not check it out automatically');
  console.log('PASS: pickup licence context, failed-check handover decision requires note and safety confirmation, replacement selector and booking transfer. Fictional API fixtures.');
 }finally{await browser.close()}
})().catch(error=>{console.error(error);process.exit(1)});
