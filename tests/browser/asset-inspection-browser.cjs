const {chromium}=require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const assert=require('node:assert/strict');
(async()=>{
const browser=await chromium.launch({channel:'chrome',headless:true});const page=await browser.newPage({viewport:{width:1400,height:1000}});let submitted;
await page.route('**/api/assets/**',async r=>{
if(r.request().method()==='POST'){submitted=r.request().postDataJSON();await r.fulfill({json:{}});return}
await r.fulfill({json:{asset:{id:'test',assetNumber:'TEST',name:'Test asset',status:'Available',attributeValues:[]},availability:{isAvailable:true,bookings:[],maintenance:[],transfers:[]},alternatives:[],inspections:submitted?[{id:"saved",stage:submitted.stage,evidenceJson:submitted.evidenceJson}]:[],meters:[],maintenance:[],documents:[],lifecycle:[],audits:[]}});
});
await page.route('**/__asset_inspection_test',r=>r.fulfill({contentType:'text/html',body:`<div id="root"></div><script type="module">
import RefreshRuntime from '/@react-refresh';RefreshRuntime.injectIntoGlobalHook(window);window.$RefreshReg$=()=>{};window.$RefreshSig$=()=>type=>type;window.__vite_plugin_react_preamble_installed__=true;
const {default:React}=await import('/node_modules/.vite/deps/react.js');const {default:{createRoot}}=await import('/node_modules/.vite/deps/react-dom_client.js');const {AssetProfileDialog}=await import('/src/components/AssetProfileDialog.tsx');createRoot(document.getElementById('root')).render(React.createElement(AssetProfileDialog,{assetId:'test',onClose:()=>{}}));
</script>`}));
await page.goto('http://localhost:5173/__asset_inspection_test');
await page.getByRole('button',{name:'Complete inspection',exact:true}).click();
assert.equal(await page.getByRole('link',{name:'Open Hire operations'}).getAttribute('href'),'/staff/hire-operations');
await page.getByLabel('Inspection stage').click();
assert.equal(await page.getByRole('option',{name:/Pre Hire|Post Hire/}).count(),0);
await page.getByRole('option',{name:'Commissioning',exact:true}).click();
await page.getByLabel('Inspection photo files').setInputFiles('src/crems-web/public/catalog/suv.jpg');
await page.getByAltText('Inspection photo 1',{exact:true}).waitFor();
await page.getByLabel('Inspecting staff member').fill('Test Inspector');
await page.getByLabel('Condition, defects and damage notes').fill('Test condition');
await page.getByRole('button',{name:'Complete inspection',exact:true}).last().click();
await page.waitForTimeout(300);
assert(submitted);assert(JSON.parse(submitted.evidenceJson).photos[0].startsWith('data:image/jpeg'));
assert.equal(submitted.notes,'Test condition');assert.equal(submitted.bookingId,null);
await page.getByRole('tab',{name:'Inspections',exact:true}).click();await page.getByAltText('Saved inspection photo 1',{exact:true}).waitFor();
console.log('PASS: Asset register general inspection photo selection, saved evidence and rental-workflow link; rental stages excluded. API writes mocked.');
await browser.close();
})().catch(e=>{console.error(e);process.exit(1)});
