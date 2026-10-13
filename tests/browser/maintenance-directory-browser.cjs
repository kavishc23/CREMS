const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
(async () => {
 const browser = await chromium.launch({ channel: 'chrome', headless: true });
 const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
 page.setDefaultTimeout(15000);
 const errors = []; page.on('pageerror', error => errors.push(error.message));
 let failSave = false, sequence = 0;
 const directory = { technicians: [], suppliers: [], locations: [{branchId:'b1',divisionId:'d1',branchName:'Suva',divisionName:'Motors'},{branchId:'b2',divisionId:'d2',branchName:'Lautoka',divisionName:'Equipment'}] };
 const writes = [];
 try {
  await page.route('**/api/administration/maintenance-directory**', async route => {
   const request = route.request(), pathname = new URL(request.url()).pathname;
   if (request.method() === 'GET') return route.fulfill({json:directory});
   const body = request.postDataJSON(); writes.push(body);
   if (failSave) return route.fulfill({status:409,json:{message:'This technician changed. Refresh the directory before editing again.'}});
   const kind = pathname.includes('/technicians') ? 'technicians' : 'suppliers';
   if (request.method() === 'POST') directory[kind].push({...body,id:`id${++sequence}`,version:String(sequence), ...(kind === 'technicians' ? {employeeNumber:`EMP-${String(sequence).padStart(12,'0')}`} : {})});
   else {
    const item = directory[kind].find(item => pathname.endsWith('/'+item.id));
    assert.equal(body.expectedVersion,item.version);
    Object.assign(item,body,{version:String(++sequence)});
   }
   return route.fulfill({json:{}});
  });
  await page.route('**/__maintenance_directory', route => route.fulfill({contentType:'text/html',body:`<html><body><div id="root"></div><script type="module">
    import RefreshRuntime from '/@react-refresh'; RefreshRuntime.injectIntoGlobalHook(window); window.$RefreshReg$=()=>{}; window.$RefreshSig$=()=>type=>type; window.__vite_plugin_react_preamble_installed__=true;
    const {default:React}=await import('/node_modules/.vite/deps/react.js'); const {default:ReactDOM}=await import('/node_modules/.vite/deps/react-dom_client.js');
    const {ThemeProvider,CssBaseline}=await import('/node_modules/.vite/deps/@mui_material.js'); const {staffTheme}=await import('/src/layout/staffTheme.ts');
    const {MaintenanceDirectoryPage}=await import('/src/pages/MaintenanceDirectoryPage.tsx');
    ReactDOM.createRoot(document.getElementById('root')).render(React.createElement(ThemeProvider,{theme:staffTheme},React.createElement(CssBaseline),React.createElement(MaintenanceDirectoryPage)));
   </script></body></html>`}));
  await page.goto((process.env.CREMS_TEST_URL || 'http://localhost:5173')+'/__maintenance_directory');
  await page.getByRole('button',{name:'Add technician',exact:true}).click();
  await page.getByLabel('Technician name').fill('Ravi Singh'); assert.equal(await page.getByLabel('Employee number').inputValue(),'Generated automatically on save'); assert.equal(await page.getByLabel('Employee number').getAttribute('readonly'),'');
  await page.getByRole('combobox',{name:'Branch / division assignments'}).click(); await page.getByRole('option',{name:'Suva / Motors',exact:true}).click(); await page.getByRole('option',{name:'Lautoka / Equipment',exact:true}).click(); await page.getByRole('combobox',{name:'Branch / division assignments'}).press('Escape');
  await page.getByLabel('Hourly labour rate (FJD)').fill('30');
  await page.getByRole('button',{name:'Save',exact:true}).click();
  await page.getByRole('button',{name:'Edit technician Ravi Singh',exact:true}).click();
  assert.equal(directory.technicians[0].locations.length,2);
  await page.getByRole('combobox',{name:'Branch / division assignments'}).click(); await page.getByRole('option',{name:'Suva / Motors',exact:true}).click(); await page.getByRole('combobox',{name:'Branch / division assignments'}).press('Escape');
  assert.equal(await page.getByLabel('Hourly labour rate (FJD)').inputValue(),'30');
  await page.getByLabel('Hourly labour rate (FJD)').fill('42.50');
  await page.getByRole('button',{name:'Save',exact:true}).click();
  await page.getByRole('cell',{name:'42.50',exact:true}).waitFor(); assert.equal(directory.technicians[0].standardCostRate,42.5); assert.deepEqual(directory.technicians[0].locations,[{branchId:'b2',divisionId:'d2'}]);
  await page.getByRole('button',{name:'Add technician',exact:true}).click();
  await page.getByLabel('Technician name').fill('Ana Kumar'); assert.equal(await page.getByLabel('Employee number').inputValue(),'Generated automatically on save');
  await page.getByRole('combobox',{name:'Branch / division assignments'}).click(); await page.getByRole('option',{name:'Suva / Motors',exact:true}).click(); await page.getByRole('option',{name:'Lautoka / Equipment',exact:true}).click(); await page.getByRole('combobox',{name:'Branch / division assignments'}).press('Escape');
  await page.getByLabel('Hourly labour rate (FJD)').fill('55');
  failSave = true; await page.getByRole('button',{name:'Save',exact:true}).click();
  await page.getByRole('alert').filter({hasText:'This technician changed. Refresh the directory before editing again.'}).waitFor();
  assert.equal(await page.getByLabel('Technician name').inputValue(),'Ana Kumar');
  failSave = false; await page.getByRole('button',{name:'Save',exact:true}).click(); await page.getByRole('cell',{name:'55.00',exact:true}).waitFor();
  assert.equal(directory.technicians.length,2); assert.notEqual(directory.technicians[0].employeeNumber,directory.technicians[1].employeeNumber); assert.equal(writes.some(body => 'employeeNumber' in body),false);
  await page.getByRole('button',{name:'Edit technician Ravi Singh',exact:true}).click();
  await page.getByLabel('Active',{exact:true}).uncheck(); await page.getByRole('button',{name:'Save',exact:true}).click();
  await page.getByText('Inactive',{exact:true}).waitFor(); assert.equal(directory.technicians[0].isActive,false);
  console.log('PASS create, edit and deactivate technicians; distinct rates; conflict retains entered values');
  await page.getByRole('tab',{name:'Suppliers',exact:true}).click();
  await page.getByRole('button',{name:'Add supplier',exact:true}).click();
  await page.getByLabel('Supplier name').fill('Suva Fleet Services'); await page.getByLabel('Supplier number').fill('SUP-001');
  await page.getByLabel('Email',{exact:true}).fill('fleet@example.test'); await page.getByLabel('Phone',{exact:true}).fill('1234567');
  await page.getByRole('button',{name:'Save',exact:true}).click();
  await page.getByRole('button',{name:'Edit supplier Suva Fleet Services',exact:true}).click();
  await page.getByLabel('Supplier name').fill('Suva Fleet Repairs'); await page.getByLabel('Active',{exact:true}).uncheck();
  await page.getByRole('button',{name:'Save',exact:true}).click();
  await page.getByRole('button',{name:'Edit supplier Suva Fleet Repairs',exact:true}).waitFor();
  assert.equal(directory.suppliers[0].isActive,false); assert.equal(directory.suppliers[0].email,'fleet@example.test');
  console.log('PASS create, edit and deactivate suppliers with contact details');
  await fs.mkdir('C:/Github/CREMS/.test-output',{recursive:true});
  await page.getByRole('tab',{name:'Technicians',exact:true}).click();
  await page.screenshot({path:'C:/Github/CREMS/.test-output/maintenance-directory-desktop.png',fullPage:true});
  await page.setViewportSize({width:390,height:844});
  assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth > innerWidth),false);
  await page.getByRole('button',{name:'Edit technician Ana Kumar',exact:true}).click();
  await page.waitForFunction(() => { const dialog = document.querySelector('.MuiDialog-container'); return dialog && getComputedStyle(dialog).opacity === '1'; });
  await page.screenshot({path:'C:/Github/CREMS/.test-output/maintenance-directory-mobile.png',fullPage:true});
  assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth > innerWidth),false);
  assert.deepEqual(errors,[]); console.log('PASS responsive directory and no uncaught JavaScript errors');
 } catch(error) { await page.screenshot({path:'C:/Github/CREMS/.test-output/maintenance-directory-failure.png',fullPage:true}); throw error; }
 finally { await browser.close(); }
})().catch(error => {console.error(error);process.exit(1)});
