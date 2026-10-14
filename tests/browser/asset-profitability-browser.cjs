const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const assert = require('node:assert/strict');
(async () => {
 const browser = await chromium.launch({channel:'chrome',headless:true});
 try {
  const page = await browser.newPage({viewport:{width:1440,height:1000}});
  const errors=[]; page.on('pageerror',error=>errors.push(error.message));
  const report={totals:{revenue:330.01,expense:132.02,profit:197.99,lossMakingAssets:0},unallocated:{revenue:20,expense:8,profit:12},monthlyTrend:[{month:'2026-10',revenue:330.01,expense:132.02,profit:197.99}],assets:[{id:'a1',assetNumber:'TEST-1',name:'Test asset',revenue:310.01,maintenanceCost:110,directCost:0,componentCost:4.01,personnelCost:10.01,totalExpense:124.02,grossProfit:185.99,profitMargin:60,lossMaking:false}]};
  await page.route('**/api/**', async route=>{
   const pathname=new URL(route.request().url()).pathname;
   if(!pathname.startsWith('/api/'))return route.continue();
   if(pathname==='/api/business-operations/asset-profitability')return route.fulfill({json:report});
   if(pathname==='/api/business-operations/reports/asset-profitability.csv')return route.fulfill({contentType:'text/csv',body:'Asset number,Revenue\nTEST-1,310.01\nUnallocated,20\n'});
   if(['/api/assets','/api/bookings','/api/customers','/api/audit'].includes(pathname))return route.fulfill({json:[]});
   throw Error('Unexpected request '+pathname);
  });
  await page.route('**/__profitability',route=>route.fulfill({contentType:'text/html',body:`<html><body><div id="root"></div><script type="module">
   import RefreshRuntime from '/@react-refresh'; RefreshRuntime.injectIntoGlobalHook(window); window.$RefreshReg$=()=>{}; window.$RefreshSig$=()=>type=>type; window.__vite_plugin_react_preamble_installed__=true;
   const {default:React}=await import('/node_modules/.vite/deps/react.js'); const {default:ReactDOM}=await import('/node_modules/.vite/deps/react-dom_client.js');
   const {ThemeProvider,CssBaseline}=await import('/node_modules/.vite/deps/@mui_material.js'); const {staffTheme}=await import('/src/layout/staffTheme.ts'); const {ReportsPage}=await import('/src/pages/ReportsPage.tsx');
   ReactDOM.createRoot(document.getElementById('root')).render(React.createElement(ThemeProvider,{theme:staffTheme},React.createElement(CssBaseline),React.createElement(ReportsPage)));
   </script></body></html>`}));
  await page.goto((process.env.CREMS_TEST_URL || 'http://localhost:5173')+'/__profitability');
  await page.getByRole('heading',{name:'Asset profit and loss',exact:true}).waitFor();
  for(const text of ['Maintenance','Other asset costs','Booking charges','Personnel']) await page.getByRole('columnheader',{name:text,exact:true}).waitFor();
  await page.getByRole('alert').filter({hasText:'unallocated booking revenue FJD 20.00, expenses FJD 8.00, profit FJD 12.00'}).waitFor();
  await page.getByRole('row').filter({hasText:'2026-10'}).getByText('132.02',{exact:true}).waitFor();
  const downloadEvent=page.waitForEvent('download'); await page.getByRole('button',{name:'Export CSV',exact:true}).click(); assert.equal((await downloadEvent).suggestedFilename(),'asset-profitability.csv');
  await page.setViewportSize({width:390,height:900});
  assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>window.innerWidth),false);
  assert.deepEqual(errors,[]);
  console.log('PASS profitability cost breakdown, totals, unallocated balance, monthly activity, CSV download and mobile layout (API mocked)');
 } finally {await browser.close();}
})().catch(error=>{console.error(error);process.exit(1)});
