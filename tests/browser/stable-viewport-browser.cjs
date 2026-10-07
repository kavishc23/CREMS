const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const assert = require('node:assert/strict');

(async () => {
  const browser = await chromium.launch({ channel: 'chrome', headless: true, args: ['--disable-features=OverlayScrollbar'] });
  try {
    const page = await browser.newPage();
    page.on('pageerror', error => console.error(error.message));
    page.on('console', message => { if (message.type() === 'error') console.error(message.text()); });
    await page.route('**/__viewport_test', route => route.fulfill({ contentType: 'text/html', body: `
      <html><body><div id="root"></div><script type="module">
      import RefreshRuntime from '/@react-refresh';
      RefreshRuntime.injectIntoGlobalHook(window);window.$RefreshReg$=()=>{};window.$RefreshSig$=()=>type=>type;window.__vite_plugin_react_preamble_installed__=true;
      const {default:React} = await import('/node_modules/.vite/deps/react.js');
      const {default:{createRoot}} = await import('/node_modules/.vite/deps/react-dom_client.js');
      const {Box,Button,CssBaseline,Dialog,DialogTitle,MenuItem,TextField,ThemeProvider,Drawer} = await import('/node_modules/.vite/deps/@mui_material.js');
      const {staffTheme} = await import('/src/layout/staffTheme.ts');
      const {StableViewport} = await import('/src/components/StableViewport.tsx');
      const h = React.createElement;
      function Harness(){
        const [dialog,setDialog]=React.useState(false),[drawer,setDrawer]=React.useState(false);
        return h(ThemeProvider,{theme:staffTheme},h(CssBaseline),h(StableViewport),
          h(Box,{'data-testid':'header',className:'mui-fixed',sx:{position:'fixed',top:0,right:0,width:'calc(100% - 80px)',height:60}},'Header'),
          h(Box,{'data-testid':'content',sx:{ml:'80px',height:2200,p:3,pt:10}},
            h(TextField,{select:true,label:'Asset',value:'all',sx:{width:200}},h(MenuItem,{value:'all'},'All assets'),h(MenuItem,{value:'one'},'Vehicle one')),
            h(Button,{onClick:()=>setDialog(true)},'Open dialog'),h(Button,{onClick:()=>setDrawer(true)},'Open drawer')),
          h(Dialog,{open:dialog,onClose:()=>setDialog(false)},h(DialogTitle,null,'Edit job'),h(TextField,{select:true,label:'Status',value:'open'},h(MenuItem,{value:'open'},'Open'))),
          h(Drawer,{open:drawer,onClose:()=>setDrawer(false)},h(Box,{sx:{width:240}},'Navigation')));
      }
      createRoot(document.getElementById('root')).render(h(Harness));
      </script></body></html>` }));
    for (const width of [1440, 768, 390]) {
      await page.setViewportSize({ width, height: 900 });
      await page.goto(`${process.env.VITE_TEST_URL || 'http://localhost:5173'}/__viewport_test`);
      await page.getByRole('button', { name: 'Open dialog' }).waitFor();
      const geometry = () => page.evaluate(() => ['header', 'content'].map(id => {
        const r = document.querySelector(`[data-testid="${id}"]`).getBoundingClientRect();
        return { x: r.x, width: r.width };
      }));
      const baseline = await geometry();
      await page.getByRole('combobox', { name: 'Asset' }).click();
      await page.getByRole('listbox').waitFor();
      assert.deepEqual(await geometry(), baseline, `Select shifted at ${width}`);
      await page.keyboard.press('Escape');
      await page.getByRole('listbox').waitFor({ state: 'hidden' });
      for (const kind of ['dialog', 'drawer']) {
        await page.getByRole('button', { name: `Open ${kind}` }).click();
        await page.waitForFunction(() => getComputedStyle(document.body).overflow === 'hidden');
        assert.deepEqual(await geometry(), baseline, `${kind} shifted at ${width}`);
        if (kind === 'dialog') {
          await page.getByRole('combobox', { name: 'Status' }).click();
          await page.getByRole('listbox').waitFor();
          assert.deepEqual(await geometry(), baseline, `Nested Select shifted at ${width}`);
          await page.keyboard.press('Escape');
          await page.getByRole('listbox').waitFor({ state: 'hidden' });
        }
        await page.keyboard.press('Escape');
        await page.waitForFunction(() => getComputedStyle(document.body).overflow !== 'hidden');
        assert.deepEqual(await geometry(), baseline, `Closing ${kind} shifted at ${width}`);
      }
      assert(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), `Horizontal document overflow at ${width}`);
    }
    console.log('PASS: stable headers/content for selects, nested selects, dialogs and temporary drawers at desktop, tablet and mobile widths; scroll locking retained.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exit(1); });
