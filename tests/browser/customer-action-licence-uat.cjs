const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const output = process.env.CREMS_TEST_ARTIFACTS || 'tmp/customer-action-licence-uat';
const date = n => { const d = new Date(); d.setDate(d.getDate() + n); return [d.getFullYear(), String(d.getMonth()+1).padStart(2,'0'), String(d.getDate()).padStart(2,'0')].join('-') };
const assets = [
  ['self', 'Test self-drive vehicle', 'PassengerVehicle', 'None', true],
  ['operator', 'Test vehicle with operator', 'CommercialVehicle', 'Required', true],
  ['equipment', 'Test custom merchant equipment', 'LightEquipment', 'None', true],
  ['policy', 'Test vehicle licence policy disabled', 'PassengerVehicle', 'None', false],
].map(([id,name,type,personnelRequirement,licencePolicy]) => ({id,name,type,personnelRequirement,licencePolicy,assetNumber:'TEST-'+id,branchId:'branch',branchName:'Suva',divisionId:'division',divisionName:'Configured merchant',assetCategoryId:'category',category:'Configured hire',status:'Available',isAvailable:true,dailyRate:95,hireUnit:'Day',bondAmount:0,requiresDelivery:false,fulfilmentMode:'PickupOnly',requiresQuote:personnelRequirement==='Required',isBookableOnline:true,allowsQuoteRequests:true,photoUrlsJson:'[]',attributes:[]}));
(async () => {
  fs.mkdirSync(output,{recursive:true});
  const browser = await chromium.launch({channel:'chrome',headless:true});
  try {
    const page = await browser.newPage({viewport:{width:1366,height:768}});
    const errors=[]; page.on('pageerror',e=>errors.push(e.message));
    await page.addInitScript(()=>sessionStorage.setItem('crems.customerSection','bookings'));
    let accepted=false, confirmed=false, decisions=0; const submissions=[];
    const quote=()=>({id:'quote',quoteNumber:'QUO-TEST',status:accepted?'Accepted':'Sent',validUntil:date(12),jobSite:null,purchaseOrderNumber:null,subtotal:190,discount:0,tax:28.5,total:218.5,depositRequired:0,version:1,branchName:'Suva',divisionName:'Configured merchant',lines:[{description:'Vehicle hire',quantity:2,rate:95,unit:'Day'}],convertedBookingId:'booking'});
    await page.route('**/api/**',async route=>{
      const p=new URL(route.request().url()).pathname; if(!p.startsWith('/api/'))return route.continue();
      if(p==='/api/customer-account/session')return route.fulfill({json:{fullName:'Test Customer',customerName:'Test Customer',email:'customer@example.test',phone:'1234567',address:'Suva',type:'Individual',emailConfirmed:true,hirePreferences:[],preferredAssetCategoryIds:[],driverLicenceNumber:'TEST-123',driverLicenceClasses:[2,3]}});
      if(p==='/api/customer-account/licence')return route.fulfill({json:{status:'Verified',licenceNumber:'TEST-123',classes:[2,3],hasImage:true}});
      if(p==='/api/customer-account/profile')return route.fulfill({json:{hirePreferences:[]}});
      if(p==='/api/customer-account/bookings')return route.fulfill({json:[{id:'booking',reference:'BK-TEST',status:confirmed?'Confirmed':'Draft',createdAt:date(0),branchName:'Suva',items:[{name:'Vehicle hire',startAt:date(2),endAt:date(4),dailyRate:95,divisionName:'Configured merchant'}]}]});
      if(p==='/api/customer-account/quotes')return route.fulfill({json:[quote()]});
      if(p==='/api/customer-account/quotes/quote/decision'){decisions++;assert.equal(route.request().postDataJSON().expectedVersion,1);accepted=true;return route.fulfill({json:{status:'Accepted'}})}
      if(p==='/api/customer-account/documents')return route.fulfill({json:{registered:[],agreements:[],invoices:[]}});
      if(p.includes('notifications'))return route.fulfill({json:{items:[],unreadCount:0}});
      if(p==='/api/public/catalogue-bootstrap')return route.fulfill({json:{divisions:[{id:'division',name:'Configured merchant',categories:[{id:'category',name:'Configured hire'}]}],branches:[{id:'branch',name:'Suva'}],assets}});
      if(p==='/api/public/assets')return route.fulfill({json:assets});
      if(p.startsWith('/api/public/assets/')){const asset=assets.find(a=>p.endsWith('/'+a.id));assert(asset);return route.fulfill({json:{...asset,bookingFlow:{requireVerifiedLicenceForVehicles:asset.licencePolicy,minimumHireDays:1,maximumHireDays:366},branch:{name:'Suva',address:'Test branch address'},service:{isBookableOnline:true,requiresQuote:asset.requiresQuote},hireRate:{rate:95,unit:'Day',isTaxable:true},taxRate:15,charges:[]}})}
      if(p==='/api/public/booking-requests'){submissions.push(route.request().postDataJSON());return route.fulfill({json:{reference:'REQUEST-TEST',requestType:submissions.at(-1).requestQuotation?'Quotation':'Booking'}})}
      if(route.request().method()!=='GET')throw Error('Unexpected mutation '+p);
      return route.fulfill({json:[]});
    });
    await page.goto('http://localhost:5173/account');
    await page.getByRole('button',{name:'Review quotation',exact:true}).first().click();
    const modal=page.getByRole('dialog');
    assert.equal(await modal.getByText(/Version\s+1/i).count(),0);
    await modal.getByRole('button',{name:'Accept quote',exact:true}).evaluate(button=>{button.click();button.click()});
    await modal.waitFor({state:'hidden'});assert.equal(decisions,1,'Only one quote decision may be submitted');
    await page.getByText('Quotation accepted. Awaiting branch review and confirmation.',{exact:true}).waitFor();
    const snackbar=page.locator('.MuiSnackbar-root');
    await snackbar.hover();await snackbar.waitFor({state:'hidden',timeout:4400});
    assert.equal(await page.getByRole('button',{name:'Review quotation',exact:true}).count(),0);
    assert((await page.getByText('Awaiting review',{exact:true}).count())>0);
    assert.equal(await page.getByText(/Version\s+1/i).count(),0);
    await page.screenshot({path:output+'/quote-accepted.png'});
    confirmed=true;await page.reload();await page.getByText('Confirmed',{exact:true}).first().waitFor();
    await page.getByRole('button',{name:'Test Customer',exact:true}).click();await page.getByRole('menuitem',{name:'Customer Profile',exact:false}).click();
    await page.getByRole('button',{name:'Save details',exact:true}).click();await snackbar.getByText('Your contact details have been updated.',{exact:true}).waitFor();
    assert.equal(await page.locator('.MuiAlert-root').filter({hasText:'Your contact details have been updated.'}).count(),1);
    await page.screenshot({path:output+'/profile-toast.png'});
    await snackbar.waitFor({state:'hidden',timeout:4400});
    await page.getByRole('button',{name:'Save details',exact:true}).click();await snackbar.waitFor();await snackbar.waitFor({state:'hidden',timeout:4400});
    await page.getByRole('button',{name:'Book a rental',exact:true}).click();
    await page.getByLabel('Return date',{exact:true}).fill(date(2));await page.getByRole('button',{name:'Show available rentals',exact:true}).click();
    for(const asset of assets){
      const card=page.getByTestId('rental-catalogue-card').filter({has:page.getByText(asset.name,{exact:true})});
      await card.getByRole('button',{name:asset.requiresQuote?'Get quote':'Book now',exact:true}).click();
      await modal.getByRole('button',{name:'Continue',exact:true}).click();
      const licence=modal.getByLabel('Driver licence number',{exact:true});
      if(asset.id==='self'){assert.equal(await licence.inputValue(),'TEST-123');assert(await licence.evaluate(e=>e.readOnly));assert(await modal.getByText(/Classes: 2, 3/).isVisible())}
      else assert.equal(await licence.count(),0,'Licence must not appear for '+asset.name);
      await page.screenshot({path:output+'/request-'+asset.id+'.png'});
      await modal.getByRole('button',{name:'Continue',exact:true}).click();
      await modal.getByRole('checkbox').check();
      await modal.getByRole('button',{name:asset.requiresQuote?'Request quotation':'Submit booking',exact:true}).click();
      await modal.getByText('Request received',{exact:true}).waitFor();
      assert.equal(submissions.at(-1).driverLicence,asset.id==='self'?'TEST-123':'');
      assert.equal(submissions.at(-1).driverName,asset.id==='self'?'Test Customer':'');
      await modal.getByRole('button',{name:'Close',exact:true}).click();
    }
    const adminPolicy={id:'division',name:'Configured merchant',defaultBondAmount:0,defaultTaxRate:15,defaultCurrency:'FJD',defaultRentalTerms:null,flow:{personnelRequirement:'None',quoteMode:'Automatic',onlineRequestsEnabled:true,businessCustomersRequireQuote:true,unpricedPersonnelRequiresQuote:true,unpricedDeliveryRequiresQuote:true,requireVerifiedLicenceForVehicles:true,minimumHireDays:1,maximumHireDays:366,quoteValidityDays:14}};
    await page.route('**/api/**',async route=>{
      const p=new URL(route.request().url()).pathname;
      if(p==='/api/auth/session')return route.fulfill({json:{id:'admin',fullName:'Test Administrator',email:'admin@example.test',roles:['SuperAdministrator'],mustChangePassword:false}});
      if(p==='/api/divisions')return route.fulfill({json:[{id:'division',name:'Configured merchant',code:'TEST',capabilities:1,isActive:true,services:[{id:'service',name:'Hire',type:'EquipmentHire',isActive:true,isBookableOnline:true,requiresQuote:false}]}]});
      if(p==='/api/divisions/division/booking-flow')return route.fulfill({json:adminPolicy});
      if(p==='/api/branches'||p==='/api/asset-categories')return route.fulfill({json:[]});
      return route.fallback();
    });
    await page.goto('http://localhost:5173/staff/organization');
    await page.getByRole('tab',{name:'Booking & quotations',exact:true}).waitFor({timeout:10000}).catch(async error=>{await page.screenshot({path:output+'/admin-failure.png'});console.log(errors,await page.locator('body').innerText());throw error});
    await page.getByRole('tab',{name:'Booking & quotations',exact:true}).click();
    await page.getByRole('button',{name:'Save division booking flow',exact:true}).click();
    await snackbar.getByText('Configured merchant booking flow saved. New customer requests now use these rules.',{exact:true}).waitFor();
    assert.equal(await page.locator('.MuiAlert-root').filter({hasText:'Configured merchant booking flow saved.'}).count(),1);
    await page.screenshot({path:output+'/admin-toast.png'});
    await snackbar.waitFor({state:'hidden',timeout:4400});
    assert.deepEqual(errors,[]);
    console.log('PASS: Single quote acceptance closes dialog; awaiting review then confirmed status; hidden version labels; repeatable profile save toast expires after three seconds including hover; conditional readonly licence/classes for configured self-drive rules; licence omitted from operator/equipment/disabled-policy requests. Existing frontend, fictional mocked APIs.');
  } finally { await browser.close() }
})().catch(e=>{console.error(e);process.exitCode=1});
