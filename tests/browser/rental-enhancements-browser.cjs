const {chromium}=require(process.env.PLAYWRIGHT_MODULE||'playwright');
const assert=require('node:assert/strict');
const base=process.env.CREMS_TEST_URL||'http://127.0.0.1:5177';
const date=n=>{const d=new Date();d.setDate(d.getDate()+n);return d.toISOString().slice(0,10)};
async function mount(page,name,component,props='{}'){
 await page.route(`**/__${name}`,r=>r.fulfill({contentType:'text/html',body:`<div id="root"></div><script type="module">
 import RefreshRuntime from '/@react-refresh';RefreshRuntime.injectIntoGlobalHook(window);window.$RefreshReg$=()=>{};window.$RefreshSig$=()=>type=>type;window.__vite_plugin_react_preamble_installed__=true;
 const {default:React}=await import('/node_modules/.vite/deps/react.js');const {default:{createRoot}}=await import('/node_modules/.vite/deps/react-dom_client.js');const {${component.split('/').at(-1)}}=await import('/src/${component}.tsx');createRoot(document.getElementById('root')).render(React.createElement(${component.split('/').at(-1)},${props}));</script>`}));
 await page.goto(base+`/__${name}`);
}
(async()=>{
 const browser=await chromium.launch({channel:'chrome',headless:true});
 try{
  const page=await browser.newPage({viewport:{width:1440,height:1000}}),errors=[],submissions=[];
  page.setDefaultTimeout(12000);page.on('pageerror',e=>{errors.push(e.message);console.error('Page error:',e.message)});
  const asset={id:'projector',assetNumber:'CE-PRO-001',name:'Epson EB-X49 Projector',type:'LightEquipment',personnelRequirement:'None',branchId:'suva',branchName:'Suva',divisionId:'events',divisionName:'Carpenters Events',assetCategoryId:'projectors',category:'Projector',isAvailable:true,dailyRate:250,hireUnit:'Day',bondAmount:0,requiresDelivery:false,fulfilmentMode:'PickupOnly',requiresQuote:false,isBookableOnline:true,allowsQuoteRequests:true,photoUrlsJson:'[]',attributes:[]};
  await page.route('**/api/**',async r=>{
   const path=new URL(r.request().url()).pathname;if(!path.startsWith('/api/'))return r.continue();
   if(path==='/api/public/catalogue-bootstrap')return r.fulfill({json:{assets:[asset],branches:[{id:'suva',name:'Suva'}],divisions:[{id:'events',name:'Carpenters Events',categories:[{id:'projectors',name:'Projector'}]}]}});
   if(path==='/api/public/assets')return r.fulfill({json:[asset]});
   if(path==='/api/customer-account/session')return r.fulfill({json:{fullName:'Customer One',customerName:'Customer One',email:'customer@example.test',phone:'9123456',address:'Suva',type:'Individual',driverLicenceClasses:[]}});
   if(path==='/api/public/assets/projector')return r.fulfill({json:{...asset,bookingFlow:{minimumHireDays:1,maximumHireDays:366},branch:{name:'Suva'},hireRate:{rate:250,unit:'Day',isTaxable:true},taxRate:0,service:{requiredDocuments:['Event Approval']},customerQuestions:[],charges:[{id:'electricity',name:'Electricity Charge',category:'Other',unit:'Unit',isRequired:true,defaultSellingRate:10,isTaxable:true}]}});
   if(path==='/api/customer-account/documents/upload'){assert.match(r.request().postData(),/Event Approval/);return r.fulfill({json:{id:'approval-doc',type:'Event Approval',fileName:'approval.pdf'}})}
   if(path==='/api/public/booking-requests'){submissions.push(r.request().postDataJSON());return r.fulfill({json:{reference:'REQ-123',requestType:'Booking'}})}
   return r.fulfill({json:[]});
  });
  await mount(page,'rental_features','pages/PublicRentalPage',"{onCustomerAccount:()=>{},customerAuthenticated:true,customerName:'Customer One'}");
  await page.getByLabel('Return date',{exact:true}).fill(date(2));
  await page.getByRole('button',{name:'Search rentals',exact:true}).click();
  await page.getByRole('button',{name:'Book now',exact:true}).first().click();

  const modal=page.getByRole('dialog');
  await modal.getByRole('button',{name:'Continue',exact:true}).click();
  const quantity=modal.getByRole('spinbutton',{name:/Electricity Charge quantity/});
  await quantity.fill('4');
  assert.equal(await modal.getByLabel('Driver licence number',{exact:true}).count(),0);
  await modal.getByRole('button',{name:'Continue',exact:true}).click();
  await modal.getByText('Upload the required documents: Event Approval.',{exact:true}).waitFor();
  await modal.getByLabel('Upload Event Approval',{exact:true}).setInputFiles({name:'approval.pdf',mimeType:'application/pdf',buffer:Buffer.from('%PDF-1.4 test')});
  await modal.getByText('approval.pdf',{exact:true}).waitFor();
  await modal.getByRole('button',{name:'Continue',exact:true}).click();
  assert.match(await modal.innerText(),/40\.00/);
  await modal.getByRole('checkbox').check();await modal.getByRole('button',{name:'Submit booking',exact:true}).click();
  await modal.getByText('Request received',{exact:true}).waitFor();
  assert.equal(submissions[0].extras[0].quantity,4);assert.deepEqual(submissions[0].documentIds,['approval-doc']);
  await modal.getByRole('button',{name:'Close',exact:true}).click();
  await page.getByRole('button',{name:/Advanced filters/i}).click();
  await page.getByRole('combobox',{name:'Hire rate',exact:true}).click();
  await page.getByRole('option',{name:/Under.*200/i}).click();await page.keyboard.press('Escape');
  await page.getByText('No rentals match this search',{exact:true}).waitFor();
  await page.getByText('Available alternatives to consider',{exact:true}).waitFor();
  assert(await page.getByRole('button',{name:'Book now',exact:true}).first().isVisible());
  await page.screenshot({path:'tmp/rental-enhancements-customer.png',fullPage:true});
  const audit=await browser.newPage({viewport:{width:1400,height:1000}});audit.on('pageerror',e=>errors.push(e.message));const calls=[];
  await audit.route('**/api/administration/audit-history**',async r=>{const q=new URL(r.request().url()).searchParams;calls.push(Object.fromEntries(q));const pageNo=Number(q.get('page'));const size=Number(q.get('pageSize'));const filtered=q.get('actor')==='Kavish';const total=filtered?1:55;const start=(pageNo-1)*size;const items=Array.from({length:Math.max(0,Math.min(size,total-start))},(_,i)=>({id:String(start+i),occurredAt:new Date().toISOString(),userName:filtered?'Kavish':'Staff '+(start+i),action:'Booking updated',entityType:'Booking',entityId:'record-'+(start+i),summary:'Inspection and booking updated',previousValues:'old',newValues:'new'}));return r.fulfill({json:{items,total,page:pageNo,pageSize:size,hasMore:start+size<total}})});
  await mount(audit,'audit_features','components/AuditLogPanel');
  await audit.getByText('Showing 20 of 55 matching records · Fiji time',{exact:true}).waitFor();
  await audit.getByRole('button',{name:'Load more logs',exact:true}).click();
  await audit.getByText('Showing 40 of 55 matching records · Fiji time',{exact:true}).waitFor();
  assert.equal(calls[0].asOf,calls[1].asOf);
  await audit.getByLabel('Staff name',{exact:true}).fill('Kavish');await audit.getByRole('button',{name:'Apply filters',exact:true}).click();
  await audit.getByText('Showing 1 of 1 matching records · Fiji time',{exact:true}).waitFor();
  await audit.getByText('View recorded changes',{exact:true}).click();await audit.getByText(/Before: old/).waitFor();
  assert.equal(calls.at(-1).page,'1');
  await audit.screenshot({path:'tmp/rental-enhancements-audit.png',fullPage:true});
  const staff=await browser.newPage();let downloaded=false;
  await staff.route('**/api/bookings/booking/documents/approval-doc/download',r=>{downloaded=true;return r.fulfill({contentType:'application/pdf',body:'%PDF-1.4 test'})});
  await mount(staff,'booking_docs','components/BookingDocumentUploads',"{required:['Event Approval'],documents:[{id:'approval-doc',type:'Event Approval',fileName:'approval.pdf'}],bookingId:'booking',onUploaded:()=>{}}");
  await staff.getByText('Event Approval · Attached',{exact:true}).waitFor();
  await staff.getByRole('button',{name:'Download',exact:true}).click();
  await staff.waitForTimeout(300);assert(downloaded);
  assert.deepEqual(errors,[]);
  console.log('PASS: compulsory unit fee visible, 4 units saved and priced at $40, no projector licence, available alternatives after empty search, audit load more and filters reset pagination and reveal recorded changes. Required Event Approval upload is enforced, sent with booking, and shown with download on staff documents. Fictional API fixtures.');
 }finally{await browser.close()}
})().catch(e=>{console.error(e);process.exitCode=1});
