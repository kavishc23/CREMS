const {chromium}=require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const assert=require('node:assert/strict');
(async()=>{
const browser=await chromium.launch({channel:'chrome',headless:true});
const page=await browser.newPage();
page.on('pageerror',e=>console.error('PAGE ERROR',e.message));
await page.route('**/__inspection_test',r=>r.fulfill({contentType:'text/html',body:`<html><body><div id="root"></div><script type="module">
import RefreshRuntime from '/@react-refresh';
RefreshRuntime.injectIntoGlobalHook(window);window.$RefreshReg$=()=>{};window.$RefreshSig$=()=>type=>type;window.__vite_plugin_react_preamble_installed__=true;
const {default:React}=await import('/node_modules/.vite/deps/react.js');
const {default:{createRoot}}=await import('/node_modules/.vite/deps/react-dom_client.js');
const {InspectionPhotos}=await import('/src/components/InspectionPhotos.tsx');
function Harness(){const [photos,setPhotos]=React.useState([]);return React.createElement(InspectionPhotos,{photos,onChange:setPhotos})}
createRoot(document.getElementById('root')).render(React.createElement(Harness));
</script></body></html>`}));
await page.goto('http://localhost:5173/__inspection_test');
await page.getByRole('button',{name:'Attach inspection photos'}).waitFor();
const chooser=page.waitForEvent('filechooser');
await page.getByRole('button',{name:'Attach inspection photos'}).click();
await (await chooser).setFiles('src/crems-web/public/catalog/suv.jpg');
await page.getByAltText('Inspection photo 1',{exact:true}).waitFor();
await page.getByRole('button',{name:'Attach inspection photos'}).waitFor({state:'visible'});
assert(await page.getByAltText('Inspection photo 1',{exact:true}).evaluate(i=>i.complete&&i.naturalWidth>0));
await page.getByLabel('Inspection photo files').setInputFiles('src/crems-web/public/catalog/sedan.jpg');
await page.getByAltText('Inspection photo 2',{exact:true}).waitFor();
await page.getByRole('button',{name:'Remove photo 1',exact:true}).click();
assert.equal(await page.locator('img').count(),1);
await page.getByLabel('Inspection photo files').setInputFiles({name:'broken.jpg',mimeType:'image/jpeg',buffer:Buffer.from('invalid')});
await page.getByRole('alert').filter({hasText:'could not be opened'}).waitFor();
assert.equal(await page.locator('img').count(),1);
const large=await page.evaluate(()=>{const c=document.createElement('canvas');c.width=2500;c.height=2000;const ctx=c.getContext('2d');const d=ctx.createImageData(c.width,c.height);for(let i=0;i<d.data.length;i+=4){d.data[i]=Math.random()*255;d.data[i+1]=Math.random()*255;d.data[i+2]=Math.random()*255;d.data[i+3]=255}ctx.putImageData(d,0,0);return c.toDataURL('image/png').split(',')[1]});
const bytes=Buffer.from(large,'base64');assert(bytes.length>3*1024*1024);
await page.getByLabel('Inspection photo files').setInputFiles({name:'phone.png',mimeType:'image/png',buffer:bytes});
await page.getByAltText('Inspection photo 2',{exact:true}).waitFor();
const dimensions=await page.getByAltText('Inspection photo 2',{exact:true}).evaluate(i=>({width:i.naturalWidth,length:i.src.length}));
assert(dimensions.width<=1600);assert(dimensions.length<3*1024*1024);
console.log('PASS: file chooser, preview decoding, append, removal, corrupt-file error, and >3 MB phone photo resizing');
await browser.close();
})().catch(e=>{console.error(e);process.exit(1)});
