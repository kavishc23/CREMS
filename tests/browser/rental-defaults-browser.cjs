const {chromium}=require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const assert=require('node:assert/strict');
(async()=>{
 const browser=await chromium.launch({channel:'chrome',headless:true});
 try {
 const page=await browser.newPage({viewport:{width:1440,height:1100}});
 const errors=[];page.on('pageerror',error=>{errors.push(error.message);console.error('PAGE ERROR:',error.message)});page.on('console',msg=>{if(msg.type()==='error')console.error('BROWSER:',msg.text())});
 const defaults={'data.retentionYears':'7','rentals.agreementPrefix':'RA','rentals.bookingPrefix':'BK','rentals.lateFeeMultiplier':'1','rentals.vatRate':'12.5','security.lockoutMinutes':'30','security.maxFailedAttempts':'5'};
 const values={...defaults};const writes=[];
 await page.route('**/api/**',async route=>{
  const path=new URL(route.request().url()).pathname;
  if(!path.startsWith('/api/')){await route.continue();return;}
  if(path.startsWith('/api/administration/settings/')&&route.request().method()==='PUT') {
   const key=decodeURIComponent(path.split('/').pop()),value=route.request().postDataJSON().value;
   if(key==='rentals.bookingPrefix'&&value==='1BAD'){await route.fulfill({status:400,json:{message:'Use 1–12 letters, numbers or hyphens, starting with a letter.'}});return;}
   values[key]=value;writes.push({key,value});await route.fulfill({status:204});return;
  }
  if(path==='/api/administration/settings'){await route.fulfill({json:Object.entries(values).map(([key,value])=>({id:key,key,value,category:'Rental',description:'Applies to new activity.',isSecret:false}))});return;}
  if(path==='/api/administration/retention-review'){
   const p=Number(new URL(route.request().url()).searchParams.get('page')||1);
   await route.fulfill({json:{years:Number(values['data.retentionYears']),cutoff:'2019-10-19T00:00:00Z',page:p,pageSize:25,bookingCount:26,auditCount:0,bookings:[{id:'old-'+p,reference:'OLD-'+p,status:'Completed',lastActivity:'2018-01-01'}],audits:[]}});return;
  }
  if(path==='/api/administration/health'){await route.fulfill({json:{status:'Healthy',databaseConnected:true,pendingMigrations:[]}});return;}
  await route.fulfill({json:[]});
 });
 await page.route('**/__settings_test',r=>r.fulfill({contentType:'text/html',body:`<div id="root"></div><script type="module">
 import RefreshRuntime from '/@react-refresh';RefreshRuntime.injectIntoGlobalHook(window);window.$RefreshReg$=()=>{};window.$RefreshSig$=()=>type=>type;window.__vite_plugin_react_preamble_installed__=true;
 const {default:React}=await import('/node_modules/.vite/deps/react.js');const {default:{createRoot}}=await import('/node_modules/.vite/deps/react-dom_client.js');const {SystemConfigurationPage}=await import('/src/pages/SystemConfigurationPage.tsx');createRoot(document.getElementById('root')).render(React.createElement(SystemConfigurationPage));</script>`}));
 await page.goto((process.env.CREMS_WEB_URL||'http://localhost:5173')+'/__settings_test');
 await page.getByRole('tab',{name:'Rental defaults',exact:true}).click();
 const field=label=>page.getByRole('textbox',{name:label,exact:true});
 const numeric=label=>page.getByRole('spinbutton',{name:label,exact:true});
 const save=label=>page.locator('.MuiCard-root').filter({has:page.getByLabel(label,{exact:true})}).getByRole('button',{name:/^Save/}).click();
 await field('Rental agreement reference prefix').fill('LEASE');
 await field('Booking reference prefix').fill('HIRE');await save('Booking reference prefix');
 await page.getByText('Booking reference prefix saved.',{exact:true}).waitFor();
 assert.equal(await field('Rental agreement reference prefix').inputValue(),'LEASE','saving one field preserves other unsaved edits');
 await save('Rental agreement reference prefix');
 await page.getByText('Rental agreement reference prefix saved.',{exact:true}).waitFor();
 for(const [label,value] of [['Late return fee multiplier','1.5'],['VAT rate (%)','15'],['Account lockout duration (minutes)','10'],['Failed password attempts before lockout','3'],['Record retention review age (years)','10']]) {
  await numeric(label).fill(value);await save(label);await page.getByText(label+' saved.',{exact:true}).waitFor();
 }
 await page.getByText(/10-year policy/).waitFor();
 await page.getByRole('button',{name:'Next',exact:true}).click();await page.getByText('Page 2',{exact:true}).waitFor();await page.getByText(/OLD-2/).waitFor();
 await field('Booking reference prefix').fill('1BAD');await save('Booking reference prefix');
 await page.getByText('Use 1–12 letters, numbers or hyphens, starting with a letter.',{exact:true}).waitFor();
 assert.equal(values['rentals.bookingPrefix'],'HIRE');
 await field('Booking reference prefix').fill('');
 assert.equal(await page.locator('.MuiCard-root').filter({has:field('Booking reference prefix')}).getByRole('button',{name:'Save',exact:true}).isDisabled(),true);
 await page.reload();await page.getByRole('tab',{name:'Rental defaults',exact:true}).click();
 await field('Booking reference prefix').waitFor();assert.equal(await field('Booking reference prefix').inputValue(),'HIRE');
 assert.equal(writes.length,7);assert.deepEqual(errors,[]);
 await page.screenshot({path:'.test-output/rental-defaults.png',fullPage:true});
 console.log('PASS: all seven settings save/reload, independent drafts, validation errors, empty-field guard, retention policy refresh and pagination. API mocked; no live settings changed.');
 }finally{await browser.close()}
})().catch(error=>{console.error(error);process.exit(1)});
