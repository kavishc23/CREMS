const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const base = process.env.CREMS_TEST_URL || 'http://127.0.0.1:5173';
const preamble = `import RefreshRuntime from '/@react-refresh';RefreshRuntime.injectIntoGlobalHook(window);window.$RefreshReg$=()=>{};window.$RefreshSig$=()=>type=>type;window.__vite_plugin_react_preamble_installed__=true;
const {default:React}=await import('/node_modules/.vite/deps/react.js');const {default:{createRoot}}=await import('/node_modules/.vite/deps/react-dom_client.js');`;
const assets = [
  {id:'car',assetNumber:'CAR-1',name:'Sedan',branchId:'suva',divisionId:'motors',assetCategoryId:'sedan',categoryName:'Sedan'},
  {id:'suv',assetNumber:'SUV-1',name:'SUV',branchId:'suva',divisionId:'motors',assetCategoryId:'suv',categoryName:'SUV'},
  {id:'west',assetNumber:'WEST-1',name:'Western sedan',branchId:'nadi',divisionId:'motors',assetCategoryId:'sedan',categoryName:'Sedan'},
  {id:'ship',assetNumber:'SHIP-1',name:'Shipping truck',branchId:'suva',divisionId:'shipping',assetCategoryId:'truck',categoryName:'Truck'},
].map(asset => ({...asset,status:'Available',isUnreturned:false}));
function profile(days) {
  const hours=days*24;
  return { asset:{id:'car',assetNumber:'DEMO-UTIL-001',name:'Demo rental vehicle',status:'Available',attributeValues:[]},
    usage:{from:'2026-08-13T00:00:00Z',to:'2026-11-11T00:00:00Z',requestedDays:days,totalHours:hours,inServiceHours:hours-100,
      rentedHours:200,availableHours:hours-300,maintenanceHours:100,reservedHours:0,otherUnavailableHours:0,
      utilisationPercent:200/hours*100,downtimePercent:100/hours*100,rentalCount:6,maintenanceCount:3,estimatedRentalCount:0,conflictingHours:0,
      basis:'Calendar hours, not engine-running hours. In service = rented + available.',
      breakdown:[{activity:'Rented',hours:200,days:200/24,percent:200/hours*100},{activity:'Available',hours:hours-300,days:(hours-300)/24,percent:(hours-300)/hours*100},{activity:'Maintenance',hours:100,days:100/24,percent:100/hours*100}]},
    availability:{isAvailable:true,bookings:[],maintenance:[],transfers:[]},alternatives:[],inspections:[],meters:[],maintenance:[],documents:[],lifecycle:[],audits:[]};
}
(async()=>{
  const browser=await chromium.launch({channel:'chrome',headless:true});
  try {
    const page=await browser.newPage({viewport:{width:1440,height:1050}});
    const errors=[];page.on('pageerror',error=>{errors.push(error.message);console.error('PAGE ERROR:',error.message)});
    page.on('console',message=>{if(message.type()==='error'&&!message.text().includes('WebSocket')&&!message.text().includes('[vite]'))console.error('BROWSER:',message.text())});
    await page.route(/^https?:\/\/[^/]+\/api\//,async route=>{
      const u=new URL(route.request().url());let data;
      if(u.pathname.endsWith('/planning/calendar')) data={assets,events:[],maintenance:[{assetId:'west',startAt:new Date(Date.now()-86400000).toISOString(),endAt:new Date(Date.now()+30*86400000).toISOString(),type:'Maintenance',reference:'TEST-MNT'}],transfers:[],personnel:[],closures:[]};
      else if(u.pathname.endsWith('/branches')) data=[{id:'suva',name:'Suva'},{id:'nadi',name:'Nadi'}];
      else if(u.pathname.endsWith('/divisions')) data=[{id:'motors',name:'Motors'},{id:'shipping',name:'Shipping'}];
      else if(u.pathname.endsWith('/profile')) data=profile(Number(u.searchParams.get('usageDays')||90));
      else if(u.pathname.endsWith('/performance')) return route.fulfill({status:403,json:{}});
      else data=[];
      await route.fulfill({json:data});
    });
    await page.route('**/__availability_usage_test',r=>r.fulfill({contentType:'text/html',body:`<div id="root"></div><script type="module">${preamble}const {PlanningCalendarPage}=await import('/src/pages/PlanningCalendarPage.tsx');createRoot(document.getElementById('root')).render(React.createElement(PlanningCalendarPage));</script>`}));
    await page.goto(base+'/__availability_usage_test'); console.log('Availability fixture loaded');
    async function choose(label,option){await page.getByRole('combobox',{name:label==='Usage period'?/^Usage period/:label,exact:label!=='Usage period'}).click();await page.getByRole('option',{name:option,exact:true}).click();await page.getByRole('listbox').waitFor({state:'hidden'});}
    await page.getByText('4 of 4 assets shown',{exact:true}).waitFor(); console.log('Availability controls ready');
    await choose('Asset category','Sedan');await page.getByText('2 of 2 assets shown',{exact:true}).waitFor();
    await page.getByRole('button',{name:'Show available only',exact:true}).click();await page.getByText('1 of 1 assets shown',{exact:true}).waitFor();
    await page.getByRole('button',{name:'Showing available only',exact:true}).click();await page.getByText('2 of 2 assets shown',{exact:true}).waitFor();
    await choose('Branch','Suva');await page.getByText('1 of 1 assets shown',{exact:true}).waitFor();
    await choose('Asset','CAR-1');
    await choose('Asset category','SUV');await page.getByText('1 of 1 assets shown',{exact:true}).waitFor();
    assert((await page.getByLabel('Asset',{exact:true}).innerText()).includes('All assets'));
    await choose('Division','Shipping');await page.getByText('0 of 0 assets shown',{exact:true}).waitFor();
    await choose('Asset category','Truck');await page.getByText('1 of 1 assets shown',{exact:true}).waitFor();
    await page.getByLabel('Search vehicle or asset').fill('no-match');await page.getByText('0 of 0 assets shown',{exact:true}).waitFor();
    await page.getByLabel('Search vehicle or asset').fill('SHIP');await page.getByText('1 of 1 assets shown',{exact:true}).waitFor();
    fs.mkdirSync('tmp/asset-usage-qa',{recursive:true});
    await page.screenshot({path:'tmp/asset-usage-qa/availability-desktop.png'});
    for(const width of [1440,900,390]){
      await page.setViewportSize({width,height:1000});
      const labels=['Search vehicle or asset','Asset','Asset category','Branch','Division','Range','Start','End'];
      const boxes=[];
      for(const label of labels){const box=await (['Asset','Asset category','Branch','Division','Range'].includes(label)?page.getByRole('combobox',{name:label,exact:true}):page.getByLabel(label,{exact:true})).boundingBox();assert(box && box.x>=0 && box.x+box.width<=width+1,`${label} outside viewport at ${width}`);boxes.push(box);}
      for(let i=0;i<boxes.length;i++)for(let j=i+1;j<boxes.length;j++){const a=boxes[i],b=boxes[j];assert(!(a.x<b.x+b.width-1&&b.x<a.x+a.width-1&&a.y<b.y+b.height-1&&b.y<a.y+a.height-1),'Filter controls overlap');}
    }
    await page.screenshot({path:'tmp/asset-usage-qa/availability-mobile.png'});
    await page.setViewportSize({width:1440,height:1050});
    await page.route('**/__asset_usage_test',r=>r.fulfill({contentType:'text/html',body:`<div id="root"></div><script type="module">${preamble}const {AssetProfileDialog}=await import('/src/components/AssetProfileDialog.tsx');createRoot(document.getElementById('root')).render(React.createElement(AssetProfileDialog,{assetId:'car',onClose:()=>{}}));</script>`}));
    await page.goto(base+'/__asset_usage_test');
    await page.getByText('Rental use and maintenance downtime',{exact:true}).waitFor();await page.screenshot({path:'tmp/asset-usage-qa/profile-desktop.png'});
    await choose('Usage period','Last 30 days');await page.getByText('620 hrs',{exact:true}).waitFor();
    await page.getByRole('button',{name:'Detailed asset report',exact:true}).click();
    await page.getByText('Asset time breakdown (24-hour days)',{exact:true}).waitFor();
    const report=await page.evaluate(async()=> (await (await import('/src/utils/moduleReports.ts')).detailedAssetReports('car',30))[0]);
    const usage=report.sections.find(s=>s.title==='Rental use and maintenance downtime');
    assert.equal(usage.rows.find(r=>r.metric==='Total observed hours').value,720);
    assert.equal(usage.rows.find(r=>r.metric==='In service (rented + available) hours').value,620);
    assert(!report.sections.some(s=>s.title.includes('financial')));
    const download=page.waitForEvent('download');await page.getByRole('button',{name:'Download PDF',exact:true}).click();
    await(await download).saveAs('tmp/asset-usage-qa/detailed-asset-report.pdf');
    await page.screenshot({path:'tmp/asset-usage-qa/report-desktop.png'});
    await page.getByRole('button',{name:'Close',exact:true}).click();await page.getByRole('button',{name:'Download PDF',exact:true}).waitFor({state:'hidden'});
    await page.setViewportSize({width:390,height:844});await page.getByText('Rental use and maintenance downtime',{exact:true}).scrollIntoViewIfNeeded();await page.screenshot({path:'tmp/asset-usage-qa/profile-mobile.png'});
    assert.deepEqual(errors,[]);
    console.log('PASS: independent category/branch/division/search filters, compatible asset selection, no filter overlap at desktop/tablet/mobile, usage period sync, non-financial report access and PDF export. API fixtures only.');
  } finally { await browser.close(); }
})().catch(error=>{console.error(error);process.exit(1)});
