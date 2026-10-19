const {chromium}=require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const assert=require('node:assert/strict');
(async()=>{
 const browser=await chromium.launch({channel:'chrome',headless:true});
 try {
  const page=await browser.newPage({viewport:{width:1440,height:1000}});
  const errors=[];page.on('pageerror',error=>errors.push(error.message));
  await page.route('**/__hire_test',r=>r.fulfill({contentType:'text/html',body:`<div id="root"></div><script type="module">
  import RefreshRuntime from '/@react-refresh';RefreshRuntime.injectIntoGlobalHook(window);window.$RefreshReg$=()=>{};window.$RefreshSig$=()=>type=>type;window.__vite_plugin_react_preamble_installed__=true;
  const {default:React}=await import('/node_modules/.vite/deps/react.js');const {default:{createRoot}}=await import('/node_modules/.vite/deps/react-dom_client.js');
  const {PublicAssetDetailDialog}=await import('/src/components/PublicAssetDetailDialog.tsx');
  const asset={id:'flat',name:'Flat 1',type:'PropertyUnit',branchName:'Suva',dailyRate:100,isAvailable:true};
  const previewDetails={...asset,category:'Housing',hireRate:{rate:900,unit:'Month'},personnelRequirement:'None',branch:{name:'Suva'},division:{name:'Housing'},service:null,specifications:[],charges:[],availabilityChecked:true};
  createRoot(document.getElementById('root')).render(React.createElement(PublicAssetDetailDialog,{asset,previewDetails,imageUrls:[],startDate:'2028-01-31',endDate:'2028-02-29',open:true,onClose:()=>{},onContinue:()=>{}}));
  </script>`}));
  await page.goto((process.env.CREMS_WEB_URL||'http://localhost:5173')+'/__hire_test');
  await page.getByText('FJD 900.00',{exact:true}).waitFor();
  await page.getByText('1 month × FJD 900.00',{exact:true}).waitFor();
  assert.deepEqual(errors,[]);
  await page.screenshot({path:'.test-output/housing-pricing.png',fullPage:true});
  console.log('PASS: property detail uses configured monthly rate and calendar-month quantity. Preview fixture; no live data changed.');
 }finally{await browser.close()}
})().catch(error=>{console.error(error);process.exit(1)});
