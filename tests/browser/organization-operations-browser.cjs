const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const assert = require('node:assert/strict');
(async () => {
 const browser = await chromium.launch({channel:'chrome',headless:true});
 const page = await browser.newPage({viewport:{width:1440,height:1000}});
 const errors=[], writes=[];
 page.on('pageerror', e=>errors.push(e.message));
 const divisions=[{id:'d1',name:'Motors',code:'MOT',defaultTaxRate:12.5,defaultBondAmount:0,defaultCurrency:'FJD',capabilities:1,isActive:true,services:[{id:'s1',name:'Vehicle rental',code:'RENT',type:'Rental',defaultDepositAmount:0,personnelRequirement:'None',defaultHireUnit:'Daily',isActive:true,isBookableOnline:true}]}];
 const branches=[{id:'b1',name:'Suva',code:'SUV',isActive:true,divisionIds:['d1']}];
 let tasks=[{id:'t1',title:'Check vehicle',priority:'Normal',assignedUserId:'u1'}];
 let failBusiness=false;
 await page.route('**/api/**', async route=>{
  const req=route.request(); if(!new URL(req.url()).pathname.startsWith('/api/'))return route.continue(); const path=new URL(req.url()).pathname.replace('/api','');
  if(req.method()!=='GET') { writes.push({path,body:req.postData()?req.postDataJSON():null}); if(path.endsWith('/complete'))tasks=[]; return route.fulfill({json:{}}); }
  if(path==='/business-operations/workspace'&&failBusiness)return route.fulfill({status:500,json:{}});
  const data={
   '/auth/session':{id:'u1',roles:['SuperAdministrator'],fullName:'Test administrator'},
   '/divisions':divisions,'/branches':branches,'/asset-categories':[], '/assets':[],
   '/branches/b1/configuration':{periods:[],exceptions:[],services:[{divisionId:'d1',serviceOfferingId:'s1',isActive:false,isBookable:false}]},
   '/corporate-operations/overview':{activeRentals:2},
   '/corporate-operations/workspace':{tasks,dispatches:[{id:'dispatch1',dispatchNumber:'DEL-1',status:'Arrived'}],cases:[{id:'case1',subject:'Test help',status:'Open',assignedUserId:'u1'}]},
   '/business-operations/workspace':{personnel:[{id:'p1',fullName:'Test Driver'}],alerts:[{id:'alert1',title:'Test alert'}]},'/hire-readiness':[]
  };
  if(!(path in data))throw new Error('Unexpected API read '+path);
  return route.fulfill({json:data[path]});
 });
 await page.route('**/__workspace_test*', route=>route.fulfill({contentType:'text/html',body:`<html><body><div id="root"></div><script type="module">
 import RefreshRuntime from '/@react-refresh'; RefreshRuntime.injectIntoGlobalHook(window); window.$RefreshReg$=()=>{}; window.$RefreshSig$=()=>type=>type; window.__vite_plugin_react_preamble_installed__=true;
 const {default:React}=await import('/node_modules/.vite/deps/react.js'); const {default:ReactDOM}=await import('/node_modules/.vite/deps/react-dom_client.js');
 const {ThemeProvider,CssBaseline}=await import('/node_modules/.vite/deps/@mui_material.js'); const {staffTheme}=await import('/src/layout/staffTheme.ts');
 const {AuthProvider}=await import('/src/auth/AuthContext.tsx');
 const name=location.search.includes('operations')?'CorporateOperationsPage':'OrganizationWorkspacePage';
 const module=await import('/src/pages/'+name+'.tsx');
 ReactDOM.createRoot(document.getElementById('root')).render(React.createElement(AuthProvider,null,React.createElement(ThemeProvider,{theme:staffTheme},React.createElement(CssBaseline),React.createElement(module[name],{userRoles:['SuperAdministrator']}))));
 </script></body></html>`}));
 const url=process.env.CREMS_TEST_URL||'http://127.0.0.1:5175';
 try {
  await page.goto(url+'/__workspace_test?organization');
  await page.getByRole('tab',{name:'Branch operations',exact:true}).click();
  await page.getByLabel('Vehicle rental · online').check();
  await page.getByLabel('Pickup cutoff',{exact:true}).nth(1).fill('');
  await page.getByRole('button',{name:'Save branch configuration'}).click();
  await page.getByText('Branch services and calendar saved.',{exact:true}).waitFor();
  const services=writes.find(x=>x.path.endsWith('/services')).body;
  assert.equal(services.length,1,'Re-enabling a disabled service must not submit duplicate IDs');
  assert.equal(services[0].isActive,true);
  assert.equal(writes.find(x=>x.path.endsWith('/calendar')).body.periods[1].pickupCutoff,null,'Cleared optional cutoff must be null');
  await page.getByRole('tab',{name:'Asset categories',exact:true}).click();
  await page.getByRole('button',{name:'Add category',exact:true}).click();
  assert.equal(await page.getByRole('button',{name:'Create',exact:true}).isDisabled(),true);
  await page.getByRole('button',{name:'Cancel',exact:true}).click();
  await page.getByRole('tab',{name:'Branches',exact:true}).click();
  await page.getByRole('cell',{name:'Suva',exact:true}).waitFor();
  console.log('PASS organization branch service reactivation, nullable cutoff, category validation and branch list');
  await page.goto(url+'/__workspace_test?operations');
  await page.getByRole('button',{name:'Complete',exact:true}).click();
  await page.getByRole('button',{name:'Complete task',exact:true}).click();
  await page.getByText('No my work to show',{exact:true}).waitFor();
  assert.ok(writes.some(x=>x.path==='/corporate-operations/tasks/t1/complete'));
  await page.getByRole('button',{name:/Deliveries Collections/}).click();
  await page.getByRole('button',{name:'Update',exact:true}).click();
  assert.equal(await page.getByRole('button',{name:'Save status',exact:true}).isDisabled(),true);
  await page.getByLabel('Received by').fill('Test Receiver');
  await page.getByRole('button',{name:'Save status',exact:true}).click();
  await page.getByRole('dialog').waitFor({state:'hidden'});
  assert.equal(writes.find(x=>x.path.includes('/dispatches/')).body.signatureName,'Test Receiver');
  await page.getByRole('tab',{name:'Business tools',exact:true}).click();
  await page.getByRole('cell',{name:'Test Driver',exact:true}).waitFor();
  for(const label of ['Personnel schedule','Delivery zones','Rate cards','Business alerts','Parts stock','Suppliers','Purchase orders','Business accounts','Branch transfers','Asset lifecycle','Meter history']) {
   await page.getByRole('button').filter({has:page.locator('.MuiListItemText-primary',{hasText:label})}).click();
   await page.getByRole('heading',{name:label,exact:true}).waitFor();
  }
  await page.getByRole('button').filter({has:page.locator('.MuiListItemText-primary',{hasText:'Operators & drivers'})}).click();
  await page.getByLabel('Search operators & drivers').fill('no match');
  await page.getByText('Showing 0 of 1',{exact:true}).waitFor();
  failBusiness=true;
  await page.getByRole('button',{name:'Refresh',exact:true}).click();
  await page.getByRole('alert').filter({hasText:'Business tools could not be loaded'}).waitFor();
  failBusiness=false;
  await page.getByRole('button',{name:'Refresh',exact:true}).click();
  await page.getByLabel('Search operators & drivers').fill('');
  await page.getByRole('cell',{name:'Test Driver',exact:true}).waitFor();
  await page.setViewportSize({width:390,height:844});
  await page.getByRole('tab',{name:'Operators & drivers',exact:true}).waitFor();
  assert.deepEqual(errors,[]);
  console.log('PASS operations task completion, delivery signature validation and payload, search, partial failure/retry and mobile navigation');
 } catch(e) { console.error(errors); console.error(await page.locator("body").innerText()); throw e; } finally {await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1});

