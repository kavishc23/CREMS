const {chromium}=require(process.env.PLAYWRIGHT_MODULE||'playwright');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const base=process.env.CREMS_TEST_URL||'http://localhost:5173';
(async()=>{
 const browser=await chromium.launch({channel:'chrome',headless:true});
 try{
  const page=await browser.newPage({viewport:{width:1440,height:1000}});let phase='pickup';const writes=[];page.on('pageerror',error=>console.error('PAGE ERROR:',error.message));
  const row={id:'progress-vehicle',bookingNumber:'CM-BOOK-1001',status:'Confirmed',customerName:'Customer One',branchName:'Suva',assetNumber:'CM-SED-001',assetName:'Toyota Corolla Sedan',assetCategoryCode:'SEDAN',assetCategory:'Sedan',hasProfessionalPersonnel:false,startAt:new Date().toISOString(),endAt:new Date(Date.now()+86400000).toISOString(),hasAgreement:false,agreementStatus:'Not signed',amountPaid:0,depositRequired:0,bondStatus:'NotRequired',bondAmountHeld:0,bondDeductionAmount:0,bondRefundAmount:0,preHireInspectionComplete:false,returnInspectionComplete:false,preHireMeterReading:38000,preHireFuelLevelPercent:90};
  await page.route(/^https?:\/\/[^/]+\/api\//,async route=>{
   const path=new URL(route.request().url()).pathname;
   if(route.request().method()==='POST'){writes.push(path);await route.fulfill({json:{}});return;}
   let data=[];
   if(path.endsWith('/work-queue'))data={items:[{...row,status:phase==='pickup'?'Confirmed':'ConvertedToRental'}],counts:{pickupToday:1,onHire:phase==='return'?1:0,dueToday:0,overdue:0,returnInProgress:0,recentlyCompleted:0},page:1,pageSize:25,total:1};
   else if(path.endsWith('/pickup-readiness'))data={canPickup:true,reasons:[]};
   else if(path.endsWith('/inspection-context'))data={templates:[{id:'checklist',name:'Sedan condition checklist',stage:phase==='pickup'?'PreHire':'PostHire',checklistJson:JSON.stringify([{section:'Vehicle',items:[{label:'Tyres checked'},{label:'Lights checked'}]}])}],requiresDrivingLicence:true,tracksMeter:true,tracksFuel:true,meterUnit:'km',assetCurrentMeterReading:38000,preHire:null};
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
  await page.goto(base+'/__rental_progress_test');await open();
  await page.getByLabel('Customer identification sighted and matches booking').check();
  await page.getByLabel('Driver licence sighted and valid').check();
  await page.getByLabel('Licence number').fill('1234567');
  await page.getByLabel('Licence classes').click();await page.getByRole('option').first().click();await page.keyboard.press('Escape');
  await page.getByLabel('Expiry').fill('2028-12-31');await next();
  await page.getByLabel('Usage reading (km)').fill('38100');await page.getByLabel('Fuel level (%)').fill('90');
  await page.getByText('Tyres checked',{exact:true}).locator('..').getByRole('combobox').click();await page.getByRole('option',{name:'Passed',exact:true}).click();await page.getByRole('listbox').waitFor({state:'hidden'});
  await page.getByLabel('Pre-hire condition and checklist result').fill('Body inspected; lights pending.');
  await page.getByLabel('Accessories and quantities').fill('Two keys, spare tyre and jack.');
  await page.getByLabel('Inspection photo files').setInputFiles('src/crems-web/public/catalog/sedan.jpg');await page.getByAltText('Inspection photo 1').waitFor();
  await save();fs.mkdirSync('tmp/rental-progress-qa',{recursive:true});await page.screenshot({path:'tmp/rental-progress-qa/pickup-saved.png'});
  await page.reload();await open();
  assert.equal(await page.getByLabel('Pre-hire condition and checklist result').inputValue(),'Body inspected; lights pending.');
  assert.equal(await page.getByLabel('Usage reading (km)').inputValue(),'38100');
  await page.getByAltText('Inspection photo 1',{exact:true}).waitFor();
  const pickup=await page.evaluate(async()=> (await import('/src/components/rentalDrafts.ts')).readRentalDraft('pickup:progress-vehicle'));
  assert.equal(pickup.savedStep,4);assert.equal(pickup.licenceNumber,'1234567');assert.equal(pickup.checklistItems.length,1);
  phase='return';await page.reload();await open();await next();
  await page.getByLabel('Return usage reading (km)').fill('38520');await page.getByLabel('Returned fuel / battery level (%)').fill('80');
  await page.getByText('Tyres checked',{exact:true}).locator('..').getByRole('combobox').click();await page.getByRole('option',{name:'Passed',exact:true}).click();await page.getByRole('listbox').waitFor({state:'hidden'});await page.getByText('Lights checked',{exact:true}).locator('..').getByRole('combobox').click();await page.getByRole('option',{name:'Passed',exact:true}).click();await page.getByRole('listbox').waitFor({state:'hidden'});
  await page.getByLabel('Post-hire inspection result').fill('Returned clean and undamaged.');
  await page.getByLabel('Accessories and quantities').fill('Both keys, spare tyre and jack returned.');
  await page.getByLabel('Inspection photo files').setInputFiles('src/crems-web/public/catalog/sedan.jpg');await page.getByAltText('Inspection photo 1').waitFor();
  await save();await page.reload();await open();
  assert.equal(await page.getByLabel('Post-hire inspection result').inputValue(),'Returned clean and undamaged.');
  assert.equal(await page.getByLabel('Return usage reading (km)').inputValue(),'38520');
  await next();await next();await next();await next();
  await page.getByLabel('Customer acknowledgement name').fill('Customer One');
  const canvas=await page.locator('canvas').boundingBox();await page.mouse.move(canvas.x+20,canvas.y+20);await page.mouse.down();await page.mouse.move(canvas.x+110,canvas.y+60);await page.mouse.up();
  await page.getByLabel('Customer acknowledges the return record, charges and bond settlement',{exact:true}).check();await save();
  await page.reload();await open();assert.equal(await page.getByLabel('Customer acknowledgement name').inputValue(),'Customer One');
  const returned=await page.evaluate(async()=> (await import('/src/components/rentalDrafts.ts')).readRentalDraft('return:progress-vehicle'));
  assert.equal(returned.savedStep,6);assert(returned.signatureDataUrl.startsWith('data:image/'));assert.equal(returned.checklistItems.length,2);assert(returned.evidenceDataUrls[0].startsWith('data:image/'));
  assert.equal(writes.length,0,'Saving progress must not release an asset, record money or complete a return');
  const ordering=await page.evaluate(async()=>{const m=await import('/src/components/rentalDrafts.ts');await Promise.all([m.saveRentalDraft('ordering',{version:1}),m.saveRentalDraft('ordering',{version:2})]);const newest=await m.readRentalDraft('ordering');await Promise.all([m.saveRentalDraft('ordering',{version:3}),m.clearRentalDraft('ordering')]);return {newest,discarded:await m.readRentalDraft('ordering')};});
  assert.equal(ordering.newest.version,2);assert.equal(ordering.discarded,undefined);
  await page.setViewportSize({width:390,height:844});await page.screenshot({path:'tmp/rental-progress-qa/return-mobile.png'});
  console.log('PASS: custom Sedan licence checks, partial pickup save, pickup/return separation, checklist step, notes/readings/photos/signature restoration after reload, no finalisation writes, and ordered save/discard. API fixtures, real IndexedDB.');
 }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exit(1)});
