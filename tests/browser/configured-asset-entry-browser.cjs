const {chromium}=require(process.env.PLAYWRIGHT_MODULE||'playwright');
const assert=require('node:assert/strict'),fs=require('node:fs');
const base=process.env.CREMS_TEST_URL||'http://127.0.0.1:5173',out='tmp/configured-asset-entry';
const fixtures={
 property:{division:'Properties',serviceType:'PropertyLease',type:'PropertyUnit',category:'Office space',meter:null,fields:[{code:'ROOMS',name:'Number of rooms',dataType:'Integer',isRequired:true,optionsJson:'[]'},{code:'AREA',name:'Floor area',dataType:'Number',unit:'m²',isRequired:false,optionsJson:'[]'}]},
 vehicle:{division:'Carpenters Motors',serviceType:'VehicleRental',type:'PassengerVehicle',category:'Passenger vehicles',meter:'km',fields:[{code:'CHASSIS',name:'Chassis number',dataType:'Text',isRequired:true,optionsJson:'[]'},{code:'ENGINE_NUMBER',name:'Engine number',dataType:'Text',isRequired:true,optionsJson:'[]'}]},
 bin:{division:'Carpenters Shipping',serviceType:'EquipmentHire',type:'WasteContainer',category:'Big bins',meter:null,fields:[{code:'CAPACITY',name:'Bin capacity',dataType:'Number',isRequired:true,optionsJson:'[]'}]}
};
(async()=>{fs.mkdirSync(out,{recursive:true});const browser=await chromium.launch({channel:'chrome',headless:true});const page=await browser.newPage({viewport:{width:1440,height:1000}});const errors=[],writes=[];page.on('pageerror',e=>errors.push(e.message));let kind='property',editingCase=false;
 const existingAsset={id:'existing',assetNumber:'PROPERTY-01',name:'Existing office',type:'PropertyUnit',status:'Available',divisionId:'division',divisionName:'Properties',branchId:'branch',branchName:'Suva',dailyRate:2400,hireUnit:'Month',useServiceHireRate:false,defaultBondAmount:1000,inheritBond:false,isActive:true,serviceOfferingId:'service',assetCategoryId:'category',personnelRequirement:'None',requiresDelivery:false,currentLocation:'Tower Level 3',photoUrlsJson:'[]',acquisitionCost:120000,currentBookValue:100000,ownershipType:'Owned'};
 await page.route('**/api/**',async route=>{const path=new URL(route.request().url()).pathname.replace(/^\/api/,''),f=fixtures[kind];if(!new URL(route.request().url()).pathname.startsWith('/api/'))return route.continue();
 if(route.request().method()!=='GET'){writes.push(route.request().postDataJSON());return route.fulfill({status:204})}
 if(path==='/auth/session')return route.fulfill({json:{id:'staff',roles:['BranchManager'],fullName:'Test staff'}});
 if(path==='/assets/existing/profile')return route.fulfill({json:{asset:{attributeValues:[{value:'4',attributeDefinition:{code:'ROOMS'}},{value:'120',attributeDefinition:{code:'AREA'}}]}}});
 if(path==='/assets')return route.fulfill({json:editingCase?[existingAsset]:[]});
 if(path==='/assets/summary')return route.fulfill({json:{total:0,available:0,onHire:0,reserved:0,maintenance:0,inspection:0,outOfService:0,categories:[]}});
 if(path==='/branches')return route.fulfill({json:[{id:'branch',name:'Suva',isActive:true,divisionIds:['division']},{id:'other',name:'Unassigned location',isActive:true,divisionIds:['division']}]});
 if(path==='/divisions')return route.fulfill({json:[{id:'division',name:f.division,isActive:true,services:[{id:'service',name:f.category+' hire',isActive:true}]}]});
 if(path==='/asset-categories')return route.fulfill({json:[{id:'category',code:kind.toUpperCase(),name:f.category,divisionId:'division',serviceOfferingId:'service',defaultAssetType:f.type,defaultMeterType:f.meter,attributeDefinitions:f.fields,personnelRequirement:'None'}]});
 if(path==='/organization/operating-context')return route.fulfill({json:[{branchId:'branch',divisionId:'division',services:[{id:'service',type:f.serviceType,defaultHireUnit:kind==='property'?'Month':'Day',meterType:f.meter,requiresDelivery:false}]}]});
 return route.fulfill({json:[]});});
 await page.route('**/__asset_entry_test*',route=>route.fulfill({contentType:'text/html',body:`<html><body><div id="root"></div><script type="module">
 import RefreshRuntime from '/@react-refresh';RefreshRuntime.injectIntoGlobalHook(window);window.$RefreshReg$=()=>{};window.$RefreshSig$=()=>type=>type;window.__vite_plugin_react_preamble_installed__=true;
 const {default:React}=await import('/node_modules/.vite/deps/react.js'),{default:ReactDOM}=await import('/node_modules/.vite/deps/react-dom_client.js');
 const {AuthProvider}=await import('/src/auth/AuthContext.tsx');
 const {ThemeProvider,CssBaseline}=await import('/node_modules/.vite/deps/@mui_material.js'),{staffTheme}=await import('/src/layout/staffTheme.ts'),{AssetsPage}=await import('/src/pages/AssetsPage.tsx');
 ReactDOM.createRoot(document.getElementById('root')).render(React.createElement(AuthProvider,null,React.createElement(ThemeProvider,{theme:staffTheme},React.createElement(CssBaseline),React.createElement(AssetsPage,{userRoles:[new URL(location.href).searchParams.get('role')||'BranchManager']}))));
 </script></body></html>`}));
 try{
 for(kind of Object.keys(fixtures)){
  await page.goto(base+'/__asset_entry_test');await page.getByRole('button',{name:'Add asset',exact:true}).click();const dialog=page.getByRole('dialog');await dialog.waitFor();
  await dialog.getByLabel('Asset number',{exact:false}).fill('TEST-'+kind);await dialog.getByLabel('Asset name',{exact:false}).fill(fixtures[kind].category+' test');
  await dialog.getByLabel('Branch',{exact:false}).click();assert.equal(await page.getByRole('option',{name:'Unassigned location'}).count(),0);await page.getByRole('option',{name:'Suva',exact:true}).click();
  assert.match(await dialog.getByLabel('Asset kind',{exact:true}).innerText(),new RegExp(kind==='property'?'Property':kind==='vehicle'?'Passenger':'Waste'));
  await dialog.getByRole('button',{name:'Continue',exact:true}).click();
  if(kind==='vehicle'){assert.equal(await dialog.getByLabel('VIN / chassis number',{exact:true}).count(),0);await dialog.getByLabel('Chassis number',{exact:false}).fill('VIN-001');await dialog.getByLabel('Engine number',{exact:false}).fill('ENGINE-001')}
  else {assert.equal(await dialog.getByLabel('VIN / chassis number',{exact:true}).count(),0);assert.equal(await dialog.getByLabel('Engine number',{exact:true}).count(),0)}
  if(kind==='property'){await dialog.getByLabel('Number of rooms',{exact:false}).fill('3');await dialog.getByLabel('Floor area',{exact:false}).fill('90');await dialog.getByLabel('Property address / unit location').fill('Tower Level 2')}
  if(kind==='bin')await dialog.getByLabel('Bin capacity',{exact:false}).fill('12');
  await page.screenshot({path:out+'/'+kind+'-specifications.png',fullPage:true});
  await dialog.getByRole('button',{name:'Continue',exact:true}).click();
  await dialog.getByLabel(/Rate per/).fill(kind==='property'?'2400':'100');
  if(kind==='property'){assert.match(await dialog.getByLabel('Billing unit',{exact:true}).innerText(),/Month/);assert.equal(await dialog.getByLabel('Professional driver / operator').count(),0);assert.equal(await dialog.getByLabel('Current meter reading').count(),0)}
  await dialog.getByRole('button',{name:'Continue',exact:true}).click();await dialog.getByRole('button',{name:'Save asset',exact:true}).click();await dialog.waitFor({state:'hidden'});
  const saved=writes.at(-1);assert.equal(saved.type,fixtures[kind].type);assert.equal(saved.branchId,'branch');assert.equal(saved.assetCategoryId,'category');assert.equal(saved.serviceOfferingId,'service');
  if(kind==='vehicle'){assert.equal(saved.vinOrChassisNumber,'VIN-001');assert.equal(saved.engineNumber,'ENGINE-001');assert.equal(saved.categoryAttributes.CHASSIS,'VIN-001')}
  if(kind==='property'){assert.equal(saved.hireUnit,'Month');assert.equal(saved.categoryAttributes.ROOMS,'3');assert.equal(saved.currentLocation,'Tower Level 2');assert.equal(saved.requiresDelivery,false)}
 }
 kind='property';editingCase=true;await page.setViewportSize({width:390,height:844});
 await page.goto(base+'/__asset_entry_test');await page.getByRole('button',{name:'Edit',exact:true}).click();let editor=page.getByRole('dialog');
 await editor.getByRole('button',{name:'Continue',exact:true}).click();assert.equal(await editor.getByLabel('Number of rooms',{exact:false}).inputValue(),'4');
 await page.screenshot({path:out+'/property-mobile.png',fullPage:true});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth+1),false);
 await editor.getByRole('button',{name:'Continue',exact:true}).click();await editor.getByRole('button',{name:'Continue',exact:true}).click();await editor.getByRole('button',{name:'Save asset',exact:true}).click();await editor.waitFor({state:'hidden'});
 assert.equal(writes.at(-1).categoryAttributes.ROOMS,'4');assert.equal(writes.at(-1).acquisitionCost,120000);assert.equal(writes.at(-1).defaultBondAmount,1000);assert.equal(writes.at(-1).currentLocation,'Tower Level 3');
 await page.goto(base+'/__asset_entry_test?role=RentalOfficer');await page.getByRole('heading',{name:'Asset register'}).waitFor();assert.equal(await page.getByRole('button',{name:'Add asset',exact:true}).count(),0);
 assert.deepEqual(errors,[]);console.log('PASS division-specific vehicle/property/bin fields, configured values, scoped branches, save payloads and add-asset permissions');
 }catch(e){await page.screenshot({path:out+'/failure.png',fullPage:true});console.error(errors);console.error((await page.locator('body').innerText()).slice(-2500));throw e}finally{await browser.close()}
})().catch(e=>{console.error(e);process.exitCode=1});
