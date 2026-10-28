const { chromium }=require(process.env.PLAYWRIGHT_MODULE||'playwright');
const assert=require('node:assert/strict'),fs=require('node:fs');
const output='tmp/customer-rental-stages-uat';
const day=n=>{const d=new Date();d.setDate(d.getDate()+n);return d.toISOString()};
(async()=>{
 fs.mkdirSync(output,{recursive:true});const browser=await chromium.launch({channel:'chrome',headless:true});
 try{
  const page=await browser.newPage({viewport:{width:1366,height:768}});const errors=[],writes=[];
  page.on('pageerror',error=>errors.push(error.message));
  await page.addInitScript(()=>{sessionStorage.setItem('crems.customerSection','bookings');sessionStorage.removeItem('crems.customerReferenceSearch')});
  let quoted=true,bookStatus='Draft',quoteStatus='Draft',cancelled=false,stale=false;
  const booking=()=>({id:'booking',reference:'BK-STAGES',status:bookStatus,...(cancelled?{customerStatus:'Cancelled'}:{}),createdAt:day(-1),branchName:'Suva',items:[{name:'Configured hire',divisionName:'Configured merchant',startAt:day(2),endAt:day(4),dailyRate:95}]});
  const offer=()=>({id:'quote',quoteNumber:'QUO-STAGES',status:quoteStatus,hasOffer:quoteStatus!=='Draft',createdAt:day(-1),validUntil:stale?day(-1):day(10),branchName:'Suva',divisionName:'Configured merchant',subtotal:190,discount:0,tax:28.5,total:218.5,depositRequired:0,version:1,lines:[{description:'Configured hire',quantity:2,rate:95,unit:'Day'}],convertedBookingId:'booking'});
  const detail=()=>({...booking(),branch:{name:'Suva',address:'Test branch',phone:'1234567'},pricing:{baseSubtotal:190,discountAmount:0,charges:[{description:'Extra cleaning',quantity:1,unitRate:20,amount:20}],chargeTotal:20,taxRate:15,taxAmount:31.5,total:241.5,depositRequired:0},bond:{required:0,held:0,refund:0,deduction:0,status:'NotRequired'},agreement:['ConvertedToRental','Completed'].includes(bookStatus)?{agreementNumber:'AGR-TEST',status:'Signed',customerSignedAt:day(-1)}:null,invoice:bookStatus==='Completed'?{invoiceNumber:'INV-TEST',issuedAt:day(0),status:'Issued',subtotal:210,taxAmount:31.5,total:241.5,amountPaid:241.5,balanceDue:0,lines:[{description:'Hire',quantity:2,unitPrice:95},{description:'Extra cleaning',quantity:1,unitPrice:20}]}:null,payments:[{receiptNumber:'RCT-TEST',type:'RentalCharge',status:'Recorded',amount:241.5,createdAt:day(0)}],documents:[],dispatches:[],readiness:{},cases:[{caseNumber:'CASE-EXT',subject:'Extension request',status:'Resolved',description:'Customer needs an extra day',resolution:'Extension approved by branch',createdAt:day(-1)}],incidents:[{incidentNumber:'INC-TEST',type:'Damage',status:'UnderReview',occurredAt:day(-1),description:'Reported scratch',location:'Test worksite'}],inspections:[{type:'Return',completedAt:day(0),damageNotes:'Rear panel scratch'}],history:[{title:'Booking confirmed',message:'The branch confirmed your booking.',createdAt:day(-1)}]});
  await page.route('**/api/**',async route=>{
   const p=new URL(route.request().url()).pathname;if(!p.startsWith('/api/'))return route.continue();
   if(p==='/api/customer-account/session')return route.fulfill({json:{fullName:'Test Customer',email:'test@example.test',phone:'1234567',address:'Suva',emailConfirmed:true,hirePreferences:[]}});
   if(p==='/api/customer-account/licence')return route.fulfill({json:{status:'Required',classes:[],hasImage:false}});
   if(p==='/api/customer-account/bookings')return route.fulfill({json:[booking()]});
   if(p==='/api/customer-account/quotes')return route.fulfill({json:quoted?[offer()]:[]});
   if(p==='/api/customer-account/bookings/booking')return route.fulfill({json:detail()});
   if(p==='/api/customer-account/documents')return route.fulfill({json:{registered:[],agreements:[],invoices:[]}});
   if(p.includes('notifications'))return route.fulfill({json:{items:[],unreadCount:0}});
   if(route.request().method()!=='GET'){
    writes.push({path:p,body:route.request().postDataJSON()});
    if(p.endsWith('/cancel')||p.endsWith('/cancellation-request')&&bookStatus==='Draft'){bookStatus='Cancelled';quoteStatus='Rejected';cancelled=true}
    if(p.endsWith('/decision'))quoteStatus=route.request().postDataJSON().accepted?'Accepted':'Rejected';
    return route.fulfill({json:{status:'Saved'}});
   }
   return route.fulfill({json:[]});
  });
  const load=async()=>{await page.goto('http://localhost:5173/account');await page.getByTestId('customer-rental-journey').first().waitFor();assert.equal(await page.getByTestId('customer-rental-journey').count(),1,'Quote and booking must be one entry')};
  const card=()=>page.getByTestId('customer-rental-journey');
  const assertStage=async(stage,label,color,actions)=>{
   await load();assert.equal(await card().getAttribute('data-stage'),stage);assert(await card().getByText(label,{exact:true}).isVisible());
   assert(await card().locator('.MuiChip-root').evaluate((chip,color)=>chip.classList.contains('MuiChip-color'+color),color));
   assert.deepEqual(await card().getByRole('button').allTextContents(),actions);
  };
  await assertStage('AwaitingQuotation','Awaiting Quotation','Default',['Cancel request']);
  assert.equal(await card().getByText(/FJD/).count(),0,'Unsent quotations must not show a price');
  await card().getByRole('button',{name:'Cancel request',exact:true}).click();await page.getByRole('dialog').getByRole('button',{name:'Keep request',exact:true}).click();assert.equal(writes.length,0);
  await card().getByRole('button',{name:'Cancel request',exact:true}).click();await page.getByRole('dialog').getByRole('button',{name:'Cancel request',exact:true}).click();
  await page.getByRole('dialog').waitFor({state:'hidden'});await card().getByText('Cancelled',{exact:true}).waitFor();assert.equal(writes.at(-1).path,'/api/customer-account/quotes/quote/cancel');
  cancelled=false;bookStatus='Draft';quoteStatus='Sent';
  await assertStage('ReviewQuotation','Review Quotation','Default',['Review quotation']);
  await card().getByRole('button',{name:'Review quotation',exact:true}).click();const modal=page.getByRole('dialog');
  const download=page.waitForEvent('download');await modal.getByRole('button',{name:'Download quotation',exact:true}).click();assert((await download).suggestedFilename().includes('QUO-STAGES'));
  await modal.getByRole('button',{name:'Decline',exact:true}).click();assert(await page.getByRole('dialog').getByRole('button',{name:'Decline quotation',exact:true}).isDisabled());
  await page.getByRole('dialog').getByRole('textbox',{name:'Why are you declining?'}).fill('Hire no longer needed');await page.getByRole('dialog').getByRole('button',{name:'Decline quotation',exact:true}).click();
  await page.getByRole('dialog').waitFor({state:'hidden'});assert.equal(writes.at(-1).body.note,'Hire no longer needed');
  quoteStatus='Sent';await load();await card().getByRole('button',{name:'Review quotation',exact:true}).click();await page.getByRole('dialog').getByRole('button',{name:'Accept quote',exact:true}).click();await page.getByRole('dialog').waitFor({state:'hidden'});
  await card().getByText('Awaiting Confirmation',{exact:true}).waitFor();assert.deepEqual(await card().getByRole('button').allTextContents(),['View summary','Cancel booking']);
  await card().getByRole('button',{name:'Cancel booking',exact:true}).click();await page.getByRole('dialog').getByRole('button',{name:'Cancel booking',exact:true}).click();await page.getByRole('dialog').waitFor({state:'hidden'});assert.equal(writes.at(-1).path,'/api/customer-account/bookings/booking/cancellation-request');
  cancelled=false;quoteStatus='Accepted';bookStatus='Confirmed';
  for(const quotation of [false,true]){
   quoted=quotation;bookStatus='Draft';
   await assertStage('AwaitingConfirmation','Awaiting Confirmation','Default',quotation?['View summary','Cancel booking']:['View summary','Change dates','Cancel booking']);
   bookStatus='Confirmed';await assertStage('Confirmed','Confirmed','Success',['View summary','Request cancellation','Request extension']);
   await card().getByRole('button',{name:'Request cancellation',exact:true}).click();assert(await page.getByRole('dialog').getByRole('button',{name:'Submit request',exact:true}).isDisabled());await page.getByRole('dialog').getByRole('textbox',{name:'Reason or additional information'}).fill('Plans changed');await page.getByRole('dialog').getByRole('button',{name:'Submit request',exact:true}).click();await page.getByRole('dialog').waitFor({state:'hidden'});assert.equal(writes.at(-1).body.reason,'Plans changed');
   await card().getByRole('button',{name:'Request extension',exact:true}).click();await page.getByRole('dialog').getByLabel('Requested return date',{exact:false}).fill(new Date(day(5)).toISOString().slice(0,10));await page.getByRole('dialog').getByRole('textbox',{name:'Reason or additional information'}).fill('Project delayed');await page.getByRole('dialog').getByRole('button',{name:'Submit request',exact:true}).click();await page.getByRole('dialog').waitFor({state:'hidden'});assert.equal(writes.at(-1).body.reason,'Project delayed');
   bookStatus='ConvertedToRental';await assertStage('OnHire','On Hire','Warning',['View summary','Request extension','Emergency / incident']);
   await card().getByRole('button',{name:'View summary',exact:true}).click();await page.getByRole('dialog').getByText('Rental agreement',{exact:true}).waitFor();if(quoted)assert(await page.getByRole('dialog').getByText('Quotation',{exact:true}).isVisible());await page.getByRole('dialog').getByRole('button',{name:'Close',exact:true}).click();
   bookStatus='Completed';await assertStage('Completed','Completed','Info',['View summary']);await card().getByRole('button',{name:'View summary',exact:true}).click();
   for(const text of ['Rental agreement','Invoice and receipt','Requests and incidents','Extension approved by branch','Reported scratch','Rear panel scratch','Charges and payments'])assert(await page.getByRole('dialog').getByText(text,{exact:!['Rear panel scratch','Extension approved by branch'].includes(text)}).isVisible(),text);
   const summaryDialog=page.getByRole('dialog');
   assert.equal(await summaryDialog.getByText('Rental history',{exact:true}).count(),0);
   await summaryDialog.locator('summary').filter({hasText:'Request details'}).first().click();
   assert(await summaryDialog.getByText('Customer needs an extra day',{exact:true}).isVisible());
   await summaryDialog.locator('summary').filter({hasText:'Request details'}).first().click();
   await summaryDialog.locator('summary').filter({hasText:'View payment breakdown'}).click();
   assert(await summaryDialog.getByText('Hire',{exact:true}).isVisible());
   await summaryDialog.locator('summary').filter({hasText:'View payment breakdown'}).click();
   if(quoted)assert(await page.getByRole('dialog').getByText('Quotation',{exact:true}).isVisible());await page.waitForTimeout(400);await page.screenshot({path:output+'/completed-'+(quoted?'quoted':'direct')+'.png'});await page.getByRole('dialog').getByRole('button',{name:'Close',exact:true}).click();
  }
  quoted=true;bookStatus='Draft';quoteStatus='Sent';stale=true;await assertStage('Expired','Expired','Error',['View summary']);stale=false;quoteStatus='Rejected';await assertStage('Declined','Declined','Error',['View summary']);
  await page.setViewportSize({width:390,height:844});await page.screenshot({path:output+'/mobile.png'});assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));
  assert.deepEqual(errors,[]);console.log('PASS: One customer journey per linked booking/quotation; exact statuses, colours and action sets; draft offer hidden; cancel confirmations; quotation download and reasoned decline/accept; confirmed cancellation/extension requests; active/completed summary with quote/agreement/invoice, requests, incidents, damage, charges and payments; expiry/decline; mobile width. Existing frontend with fictional mocked APIs.');
 }finally{await browser.close()}
})().catch(error=>{console.error(error);process.exitCode=1});
