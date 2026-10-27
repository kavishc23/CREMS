const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const assert = require('node:assert/strict');
const fs = require('fs');
const path = require('path');
const base=process.env.CREMS_TEST_URL || 'http://localhost:5173';
const output=process.env.CREMS_TEST_ARTIFACTS || 'tmp/customer-preferences-uat';
const divisions=[{id:'carptrac',name:'Carptrac',categories:[{id:'forklift',name:'Forklift'},{id:'generator',name:'Generator set'},{id:'heavy',name:'Heavy machine'},{id:'general',name:'General hire equipment'}]},{id:'shipping',name:'Carpenters Shipping',categories:[{id:'bin',name:'Big bin'},{id:'toilet',name:'Portable toilet'}]}];
const assets=divisions.flatMap(d=>d.categories.flatMap(c=>Array.from({length:c.id==='forklift'?5:1},(_,i)=>({id:`${c.id}-${i}`,assetNumber:`${c.id}-${i}`,name:`${c.name} ${i+1}`,type:'LightEquipment',status:'Available',branchId:'suva',branchName:'Suva',dailyRate:100+i,hireUnit:'Day',isAvailable:true,divisionId:d.id,divisionName:d.name,assetCategoryId:c.id,category:c.name,categoryCode:c.id,bondAmount:0,personnelRequirement:'None',serviceName:'Hire',requiresQuote:true,quoteMode:'Always',requiresDelivery:false,photoUrlsJson:'[]',attributes:[]}))));
(async()=>{
 fs.mkdirSync(output,{recursive:true});const browser=await chromium.launch({channel:'chrome',headless:true});
 try{
  const page=await browser.newPage({viewport:{width:1440,height:1000}});const errors=[];page.on('requestfailed',r=>console.log('FAILED',r.url(),r.failure()));page.on('response',r=>{if(r.status()>=400)console.log('HTTP',r.status(),r.url())});page.on('pageerror',e=>{errors.push(e.message);console.error('PAGE ERROR',e.message)});let selected=[],saves=0;
  await page.route('**/api/**',async route=>{
   const url=new URL(route.request().url()),p=url.pathname; if(!p.startsWith('/api/'))return route.continue();
   if(p==='/api/customer-account/session')return route.fulfill({json:{fullName:'UAT Customer',email:'uat@example.test',phone:'1234567',hirePreferences:[],preferredAssetCategoryIds:selected,emailConfirmed:true}});
   if(p==='/api/customer-account/rental-preferences'){
    if(route.request().method()==='PUT'){selected=route.request().postDataJSON().assetCategoryIds;saves++;return route.fulfill({json:{preferredAssetCategoryIds:selected}})}
    return route.fulfill({json:{divisions,preferredAssetCategoryIds:selected}})
   }
   if(p==='/api/public/catalogue-bootstrap')return route.fulfill({json:{divisions:divisions.map(d=>({...d,services:[]})),branches:[{id:'suva',name:'Suva'}],assets}});
   if(p==='/api/public/assets')return route.fulfill({json:assets});
   if(p==='/api/customer-account/licence')return route.fulfill({json:{status:'Verified',classes:[2],hasImage:false,licenceNumber:'12345'}});
   if(p==='/api/customer-account/documents')return route.fulfill({json:{registered:[],agreements:[],invoices:[]}});
   if(p.includes('notifications'))return route.fulfill({json:{items:[],unreadCount:0}});
   return route.fulfill({json:[]});
  });
  await page.route('**/__preferences_uat',r=>r.fulfill({contentType:'text/html',body:`<div id="root"></div><script type="module">
import RefreshRuntime from '/@react-refresh';RefreshRuntime.injectIntoGlobalHook(window);window.$RefreshReg$=()=>{};window.$RefreshSig$=()=>type=>type;window.__vite_plugin_react_preamble_installed__=true;
import '/node_modules/@fontsource/inter/latin-400.css';
const {default:React}=await import('/node_modules/.vite/deps/react.js');const {default:{createRoot}}=await import('/node_modules/.vite/deps/react-dom_client.js');const {ThemeProvider,createTheme,CssBaseline}=await import('/node_modules/.vite/deps/@mui_material.js');
const {CustomerPortalPage}=await import('/src/pages/CustomerPortalPage.tsx');const {PublicRentalPage}=await import('/src/pages/PublicRentalPage.tsx');
sessionStorage.setItem('crems.customerSection','preferences');
function Test(){const [portal,setPortal]=React.useState(true);return React.createElement(ThemeProvider,{theme:createTheme({palette:{primary:{main:'#111111'},secondary:{main:'#ffed00'},background:{default:'#f6f6f3'}},typography:{fontFamily:'Inter, sans-serif'},shape:{borderRadius:12},components:{MuiButton:{styleOverrides:{root:{textTransform:'none',fontWeight:700}}}}})},React.createElement(CssBaseline),portal?React.createElement(CustomerPortalPage,{onBack:()=>setPortal(false)}):React.createElement(PublicRentalPage,{customerAuthenticated:true,customerName:'UAT Customer',onCustomerAccount:section=>{sessionStorage.setItem('crems.customerSection',section||'overview');setPortal(true)}}))}createRoot(document.getElementById('root')).render(React.createElement(Test));</script>`}));
  await page.goto(base+'/__preferences_uat'); 
  await page.getByRole('checkbox',{name:'Select all categories in Carptrac'}).check();
  await page.getByRole('button',{name:'Save preferences',exact:true}).click();await page.getByText('Preferences saved.',{exact:false}).waitFor();assert.equal(selected.length,4);
  await page.evaluate(()=>window.scrollTo(0,0)); await page.screenshot({path:path.join(output,'preferences-desktop.png'),fullPage:true});
  await page.getByRole('button',{name:'View homepage',exact:true}).click();
  await page.getByRole('heading',{name:'Carptrac',exact:true}).waitFor();assert.equal(await page.getByRole('heading',{name:'Carpenters Shipping',exact:true}).count(),0);
  const next=page.getByRole('button',{name:'Next Carptrac rentals'});assert(await next.isEnabled());assert.equal(await page.locator('#rentals').getByRole('button',{name:'View details',exact:true}).count(),4);await next.click();
  await page.getByRole('button',{name:'UAT Customer',exact:true}).click();const menu=page.getByRole('menu');assert.deepEqual(await menu.getByRole('menuitem').allTextContents(),['Customer ProfileManage your account','Rental PreferencesPersonalise your homepage']);await menu.getByRole('menuitem',{name:'Rental Preferences'}).click();
  await page.getByRole('button',{name:'Clear selections'}).click();await page.getByRole('checkbox',{name:'Big bin',exact:true}).check();await page.getByRole('button',{name:'Save preferences',exact:true}).click();await page.getByText('Preferences saved.',{exact:false}).waitFor();assert.deepEqual(selected,['bin']);
  await page.getByRole('button',{name:'View homepage',exact:true}).click();await page.getByRole('heading',{name:'Carpenters Shipping',exact:true}).waitFor();assert.equal(await page.getByRole('heading',{name:'Carptrac',exact:true}).count(),0);assert.equal(await page.locator('#rentals').getByRole('button',{name:'View details',exact:true}).count(),1);assert.equal(await page.getByText('Portable toilet 1',{exact:true}).count(),0);
  const card=page.locator('#rentals .MuiCard-root').first();const bounds=await card.boundingBox();assert(bounds.width<360,`Single card stretched to ${bounds.width}`);
  await page.evaluate(()=>window.scrollTo(0,0)); await page.screenshot({path:path.join(output,'homepage-big-bin.png'),fullPage:true});
  await page.getByRole('button',{name:'Show available rentals',exact:true}).click();await page.getByRole('heading',{name:'Carptrac',exact:true}).waitFor();assert(await page.getByText('Portable toilet 1',{exact:true}).count());
  divisions.push({id:'new',name:'New presentation division',categories:[{id:'projector',name:'Projector'}]});
  await page.getByRole('button',{name:'UAT Customer',exact:true}).click();await page.getByRole('menuitem',{name:'Rental Preferences'}).click();await page.getByRole('checkbox',{name:'Projector',exact:true}).waitFor();assert(await page.getByRole('checkbox',{name:'Big bin',exact:true}).isChecked());
  divisions[2].categories.push({id:'screen',name:'Display screen'}); await page.evaluate(()=>window.dispatchEvent(new Event('focus'))); await page.getByRole('checkbox',{name:'Display screen',exact:true}).waitFor(); assert(await page.getByRole('checkbox',{name:'Big bin',exact:true}).isChecked()); await page.setViewportSize({width:390,height:844});assert(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth));await page.screenshot({path:path.join(output,'preferences-mobile.png'),fullPage:true});
  assert.deepEqual(errors,[]);assert.equal(saves,2);console.log('PASS: division selection, category persistence payloads, four-card arrows, single-card width, homepage-only filtering, search unrestricted, later configured division/category, responsive layout. Browser API is intercepted; persistence is covered by backend regression tests.');
 }finally{await browser.close()}
})().catch(e=>{console.error(e);process.exit(1)});
