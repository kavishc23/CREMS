// Isolated React checks: API responses are intercepted; no customer data or emails are changed.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || path.resolve(__dirname, '../../tmp/customer-test-tools/node_modules/playwright'));
const photo = 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jO1cAAAAASUVORK5CYII=';
const checks = [{code:'body',label:'Body and glass',safetyCritical:false},{code:'brakes',label:'Tyres and brakes',safetyCritical:true},{code:'safety',label:'Lights and safety equipment',safetyCritical:true},{code:'operation',label:'Engine and warning lights',safetyCritical:true}];
(async()=>{
 const browser = await chromium.launch({channel:'chrome',headless:true}); const results=[];
 try {
  async function fixture() {
   const page = await browser.newPage({viewport:{width:1280,height:1000}}); const writes=[],errors=[];
   page.on('pageerror',e=>errors.push(e.message));
   await page.route('**/api/**',async route=>{
    const req=route.request(),url=new URL(req.url()).pathname;if(!url.startsWith('/api/'))return route.continue();
    if(req.method()==='POST'){writes.push(req.postDataJSON());return route.fulfill({json:{outcome:req.postDataJSON().outcome}})}
    if(url.endsWith('/incidents/context'))return route.fulfill({json:{id:'asset-1',assetNumber:'LR6000',name:'Toyota Corolla',currentMeterReading:100000,tracksMeter:true,tracksFuel:true,meterUnit:'km',customerName:'Customer',checks}});
    if(url.endsWith('/incidents/replacement-assets'))return route.fulfill({json:[{id:'asset-2',assetNumber:'LR6001',name:'Toyota Corolla',currentMeterReading:1000,tracksMeter:true,tracksFuel:true,meterUnit:'km',checklist:['Identity checked','Safety checked']}]});
    if(url.endsWith('/lifecycle'))return route.fulfill({json:{incidents:[{id:'incident-1',incidentNumber:'INC-CUSTOMER',type:'Damage',occurredAt:new Date(Date.now()-3600000).toISOString(),description:'Customer reports a cosmetic scratch.',location:'Suva depot',estimatedCost:30,evidenceJson:JSON.stringify([photo]),reviewedAt:null,status:'Open'}]}});
    throw Error('Unexpected request '+url);
   });
   await page.route('**/__incident_report_fixture',route=>route.fulfill({contentType:'text/html',body:`<div id="root"></div><script type="module">import RefreshRuntime from '/@react-refresh';RefreshRuntime.injectIntoGlobalHook(window);window.$RefreshReg$=()=>{};window.$RefreshSig$=()=>type=>type;window.__vite_plugin_react_preamble_installed__=true;const {default:React}=await import('/node_modules/.vite/deps/react.js');const {default:{createRoot}}=await import('/node_modules/.vite/deps/react-dom_client.js');const {EndHireAfterIncidentDialog}=await import('/src/components/EndHireAfterIncidentDialog.tsx');createRoot(document.getElementById('root')).render(React.createElement(EndHireAfterIncidentDialog,{bookingId:'booking-1',bookingNumber:'BK-1',assetName:'Toyota Corolla',initialIncidentId:'incident-1',onClose:()=>{},onSaved:outcome=>window.saved=outcome}));</script>`}));
   await page.goto((process.env.CREMS_TEST_URL||'http://127.0.0.1:5173')+'/__incident_report_fixture');
   await page.getByText('Incident and damage report',{exact:true}).waitFor();await page.getByLabel('Current fuel (%)',{exact:true}).waitFor().catch(async e=>{console.log('BODY',await page.locator('body').innerText());console.log('ERRORS',errors);throw e});
   async function select(name,option){await page.getByRole('combobox',{name:new RegExp('^'+name)}).click();await page.getByRole('option',{name:option,exact:true}).click()}
   async function completeInspection(unsafe=false){for(const check of checks)await select(check.label,unsafe&&check.code==='brakes'?'Issue found':check.code==='body'?'Issue found':'Checked — satisfactory');await page.getByLabel('Current fuel (%)',{exact:true}).fill('80');await page.getByLabel(/^Staff damage assessment and customer arrangement/).fill('Cosmetic scratch assessed and the customer arrangement is recorded.');}
   return {page,writes,errors,select,completeInspection};
  }
  {
   const f=await fixture();await f.completeInspection();await f.page.getByLabel(/All safety checks passed/).check();await f.page.getByLabel('I confirm the inspection and selected customer arrangement are complete').check();await f.page.getByRole('button',{name:'Save report & keep rental active'}).click();await f.page.waitForFunction(()=>window.saved==='ContinueRental');assert.equal(f.writes.length,1);assert.equal(f.writes[0].evidenceDataUrls[0],photo);assert.equal(f.writes[0].expectedReviewedAt,null);assert.equal(f.writes[0].expectedAssetId,'asset-1');assert.deepEqual(f.errors,[]);await f.page.screenshot({path:'output/incident-workflow/keep-active.png',fullPage:true});await f.page.close();console.log('Browser case passed');results.push('Existing customer incident is reused, evidence retained, and minor outcome submitted.');
  }
  {
   const f=await fixture();await f.completeInspection(true);await f.page.getByLabel(/All safety checks passed/).check();await f.page.getByLabel('I confirm the inspection and selected customer arrangement are complete').check();await f.page.getByRole('button',{name:'Save report & keep rental active'}).click();await f.page.getByText(/To continue, every safety check must pass/).waitFor();assert.equal(f.writes.length,0);assert.deepEqual(f.errors,[]);await f.page.close();console.log('Browser case passed');results.push('Unsafe inspection cannot keep the rental active and shows validation.');
  }
  {
   const f=await fixture();await f.completeInspection();await f.select('Rental outcome','Provide a replacement — keep booking active');await f.page.getByLabel(/^Damaged asset location or custody/).fill('With recovery provider in Suva');await f.select('Available replacement asset','LR6001 · Toyota Corolla');await f.page.getByLabel('Identity checked',{exact:true}).check();await f.page.getByLabel('Safety checked',{exact:true}).check();await f.page.getByLabel(/^Replacement fuel/).fill('100');
   const chooser=f.page.getByText('Replacement inspection photos',{exact:true}).locator('..').locator('input[type=file]');await chooser.setInputFiles({name:'inspection.png',mimeType:'image/png',buffer:Buffer.from(photo.split(',')[1],'base64')});
   await f.page.getByLabel('The replacement passed inspection and is safe and suitable for this hire').check();const canvas=f.page.locator('canvas');await canvas.scrollIntoViewIfNeeded();const box=await canvas.boundingBox();await f.page.mouse.move(box.x+30,box.y+35);await f.page.mouse.down();await f.page.mouse.move(box.x+120,box.y+70,{steps:8});await f.page.mouse.up();await f.page.getByLabel('The customer accepts the replacement and recorded starting condition, with the agreed charges and bond unchanged').check();await f.page.getByLabel('I confirm the inspection and selected customer arrangement are complete').check();await f.page.getByRole('button',{name:'Save report & hand over replacement'}).click();await f.page.waitForFunction(()=>window.saved==='ReplaceAsset');assert.equal(f.writes[0].replacementAssetId,'asset-2');assert.equal(f.writes[0].replacementHandover.meterReading,1000);assert.equal(f.writes[0].replacementHandover.photos.length,1);assert.equal(f.writes[0].evidenceDataUrls.length,1);assert.deepEqual(f.errors,[]);await f.page.screenshot({path:'output/incident-workflow/replacement.png',fullPage:true});await f.page.close();console.log('Browser case passed');results.push('Replacement requires inspection, photo and customer signature and sends its own starting readings.');
  }
  {
   const f=await fixture();await f.completeInspection();await f.select('Rental outcome','Serious incident — end hire and proceed to settlement');await f.page.getByLabel(/^Damaged asset location or custody/).fill('Police recovery yard');await f.page.getByLabel('I confirm this hire must end without the normal return and custody is recorded').check();await f.page.getByRole('button',{name:'End hire & open settlement'}).click();await f.page.waitForFunction(()=>window.saved==='EndHire');assert.equal(f.writes[0].outcome,'EndHire');assert.equal(f.writes[0].custody,'Police recovery yard');assert.deepEqual(f.errors,[]);await f.page.setViewportSize({width:390,height:844});assert.equal(await f.page.evaluate(()=>document.documentElement.scrollWidth>innerWidth),false);await f.page.screenshot({path:'output/incident-workflow/mobile.png',fullPage:true});await f.page.close();console.log('Browser case passed');results.push('Serious incident preserves the report and selects settlement; mobile layout fits.');
  }
  fs.writeFileSync('output/incident-workflow/browser-results.json',JSON.stringify(results,null,2));console.log(JSON.stringify({passed:results.length,results}));
 }finally{await browser.close()}
})().catch(e=>{console.error(e.stack);process.exitCode=1});
