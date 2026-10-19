const {chromium,request}=require(process.env.PLAYWRIGHT_MODULE||'playwright');
const assert=require('node:assert/strict');
const crypto=require('node:crypto');
const fs=require('node:fs/promises');

(async()=>{
 const baseURL=process.env.CREMS_E2E_URL, password=process.env.CREMS_E2E_PASSWORD;
 assert.ok(baseURL&&password,'Run through the disposable CREMS.E2E host');
 const browser=await chromium.launch({channel:'chrome',headless:true});
 const page=await browser.newPage({viewport:{width:1440,height:1000}});
 page.setDefaultTimeout(15000);
 const failures=[],passed=[],contexts=[];
 const check=(condition,message)=>{if(!condition)failures.push(message);else passed.push(message)};
 const errors=[];page.on('pageerror',e=>errors.push(e.message));
 const artifacts='tmp/workspace-e2e'; await fs.mkdir(artifacts,{recursive:true});
 async function api(method,path,body) {
  const result=await page.evaluate(async({method,path,body})=>{
   const response=await fetch('/api'+path,{method,headers:{'Content-Type':'application/json','X-CREMS-Window-Id':sessionStorage.getItem('crems.window-session-id')},body:body===undefined?undefined:JSON.stringify(body)});
   return {status:response.status,text:await response.text()};
  },{method,path,body});
  assert.ok(result.status<300,`${method} ${path}: ${result.status} ${result.text.slice(0,500)}`);
  return result.text?JSON.parse(result.text):null;
 }
 async function loginRole(name) {
  const context=await request.newContext({baseURL,extraHTTPHeaders:{'X-CREMS-Window-Id':crypto.randomUUID()}}); contexts.push(context);
  const response=await context.post('/api/auth/login',{data:{email:name+'@example.test',password}});
  assert.equal(response.status(),204,`${name} login`);return context;
 }
 async function select(label,option){await page.getByRole('combobox',{name:label}).click();await page.getByRole('option',{name:option,exact:true}).click();}
 try {
  const anonymous=await request.newContext({baseURL});contexts.push(anonymous);
  check((await anonymous.get('/api/divisions')).status()===401,'Anonymous organization access denied');
  check((await anonymous.get('/api/corporate-operations/workspace')).status()===401,'Anonymous operations access denied');
  await page.goto(baseURL+'/staff/organization');
  await page.getByLabel('Email address').fill('admin@example.test');
  await page.getByLabel(/^Password/).fill(password);
  await page.getByRole('button',{name:'Sign in',exact:true}).click();
  await page.getByRole('heading',{name:'Organization',exact:true}).waitFor();
  passed.push('Real browser password login and protected organization navigation');
  await page.getByRole('button',{name:'Add business / division',exact:true}).click();
  await page.getByLabel('Division code').fill('E2E');
  await page.getByLabel('Division name').fill('E2E Division');
  await page.getByRole('dialog').getByRole('button').last().click();
  await page.getByRole('dialog').waitFor({state:'hidden'});
  await page.reload();
  await page.getByRole('heading',{name:'E2E Division',exact:true}).waitFor();
  const divisions=await api('GET','/divisions'),division=divisions.find(x=>x.code==='E2E');
  check(!!division,'Division created through browser persists after reload');
  await api('POST',`/divisions/${division.id}/services`,{code:'HIRE',name:'E2E Hire',type:0,personnelRequirement:'None',isBookableOnline:true,requiresQuote:false});
  const service=(await api('GET','/divisions')).find(x=>x.id===division.id).services[0];
  const branch=await api('POST','/branches',{code:'E2E',name:'E2E Branch',divisionIds:[division.id]});
  await page.reload();
  await page.getByRole('tab',{name:'Branch operations',exact:true}).click();
  await select('Branch to configure','E2E — E2E Branch');
  await page.getByLabel('E2E Hire · online').check();
  await page.getByLabel('Pickup cutoff').nth(1).fill('');
  await page.getByRole('button',{name:'Save branch configuration',exact:true}).click();
  await page.getByText('Branch services and calendar saved.',{exact:true}).waitFor();
  let config=await api('GET',`/branches/${branch.id}/configuration`);
  check(config.services.some(x=>x.serviceOfferingId===service.id&&x.isActive),'Browser service assignment persisted to SQL');
  check(config.periods.find(x=>x.dayOfWeek==='Monday').pickupCutoff===null,'Cleared cutoff persisted as SQL null');
  // Reproduce the inactive-row case with actual storage and real endpoint validation.
  await api('PUT',`/branches/${branch.id}/services`,[]);
  await page.reload();await page.getByRole('tab',{name:'Branch operations',exact:true}).click();
  await select('Branch to configure','E2E — E2E Branch');
  await page.getByLabel('E2E Hire · online').check();
  await page.getByRole('button',{name:'Save branch configuration',exact:true}).click();
  await page.getByText('Branch services and calendar saved.',{exact:true}).waitFor();
  config=await api('GET',`/branches/${branch.id}/configuration`);
  check(config.services.filter(x=>x.serviceOfferingId===service.id).length===1&&config.services[0].isActive,'Inactive service reactivation persists without duplicate rows');
  await page.getByRole('tab',{name:'Asset categories',exact:true}).click();
  await page.getByRole('button',{name:'Add category',exact:true}).click();
  await select('Division','E2E Division');await select('Service','E2E Hire');
  await page.getByLabel('Code').fill('E2ECAT');await page.getByLabel('Name').fill('E2E Category');
  await page.getByRole('button',{name:'Create',exact:true}).click();
  await page.getByRole('dialog').waitFor({state:'hidden'});
  await page.getByRole('button',{name:'Configure fields',exact:true}).click();
  await page.getByRole('button',{name:'Add field',exact:true}).click();
  await page.getByLabel('Code').fill('CAPACITY');await page.getByLabel('Field name').fill('Capacity');
  await page.getByRole('button',{name:'Save fields',exact:true}).click();
  await page.getByRole('dialog').waitFor({state:'hidden'});
  const category=(await api('GET','/asset-categories')).find(x=>x.code==='E2ECAT');
  check(category.attributeDefinitions[0].name==='Capacity','Browser category and custom field persisted to SQL');
  await page.reload();await page.getByRole('tab',{name:'Asset categories',exact:true}).click();
  await page.getByText('Capacity',{exact:true}).waitFor();
  await page.screenshot({path:artifacts+'/organization.png',fullPage:true});
  const admin=await api('GET','/auth/session');
  const task=await api('POST','/corporate-operations/tasks',{branchId:branch.id,category:'MaintenanceDue',priority:'Normal',title:'E2E task',dueAt:new Date().toISOString(),assignedUserId:admin.id});
  await page.goto(baseURL+'/staff/operations');
  await page.getByRole('cell',{name:'E2E task',exact:true}).waitFor();
  await page.getByRole('row').filter({hasText:'E2E task'}).getByRole('button',{name:'Complete',exact:true}).click();
  await page.getByRole('button',{name:'Complete task',exact:true}).click();
  await page.getByRole('dialog').waitFor({state:'hidden'});
  check(!(await api('GET','/corporate-operations/workspace')).tasks.some(x=>x.id===task.id),'Browser task completion persisted and removed from active queue');
  await page.getByRole('button').filter({has:page.locator('.MuiListItemText-primary',{hasText:'Deliveries'})}).click();
  await page.getByRole('row').filter({hasText:'E2EDEL'}).getByRole('button',{name:'Update',exact:true}).click();
  check(await page.getByRole('button',{name:'Save status',exact:true}).isDisabled(),'Dispatch completion requires recipient in browser');
  await page.getByLabel('Received by').fill('E2E Receiver');
  await page.getByRole('button',{name:'Save status',exact:true}).click();await page.getByRole('dialog').waitFor({state:'hidden'});
  const dispatch=(await api('GET','/corporate-operations/workspace')).dispatches.find(x=>x.dispatchNumber==='E2EDEL');
  check(dispatch.status==='Completed'&&dispatch.proofJson.includes('E2E Receiver'),'Completed delivery and recipient evidence persist to SQL');
  await page.getByRole('tab',{name:'Business tools',exact:true}).click();
  await page.getByRole('button').filter({has:page.locator('.MuiListItemText-primary',{hasText:'Suppliers'})}).click();
  await page.getByRole('button',{name:/Add supplier/i}).click();
  await page.getByLabel('Supplier number').fill('E2ESUP');await page.getByLabel('Supplier name').fill('E2E Supplier');
  await page.getByRole('button',{name:'Create',exact:true}).click();await page.getByRole('dialog').waitFor({state:'hidden'});
  check((await api('GET','/business-operations/workspace')).suppliers.some(x=>x.name==='E2E Supplier'),'Browser supplier creation persisted to SQL');
  await page.reload();await page.getByRole('tab',{name:'Business tools',exact:true}).click();
  for(const label of ['Operators & drivers','Personnel schedule','Delivery zones','Rate cards','Business alerts','Parts stock','Suppliers','Purchase orders','Business accounts','Branch transfers','Asset lifecycle','Meter history']){
   await page.getByRole('button').filter({has:page.locator('.MuiListItemText-primary',{hasText:label})}).click();
   await page.getByRole('heading',{name:label,exact:true}).waitFor();
  }
  check(!await page.getByRole('alert').filter({hasText:'could not be loaded'}).count(),'All business-tools sections load against SQL');
  await page.setViewportSize({width:390,height:844});await page.getByRole('tab',{name:'Meter history',exact:true}).waitFor();
  await page.screenshot({path:artifacts+'/operations-mobile.png',fullPage:true});
  const manager=await loginRole('manager'),officer=await loginRole('officer');
  const officerDivisions=await (await officer.get('/api/divisions')).json();
  check(Array.isArray(officerDivisions)&&officerDivisions.every(x=>x.id!==division.id),'Rental officer cannot list another division');
  check((await officer.post('/api/divisions',{data:{code:'DENIED',name:'Denied',capabilities:0,isPublic:true}})).status()===403,'Rental officer cannot create divisions');
  check((await officer.post('/api/business-operations/suppliers',{data:{supplierNumber:'DENIED',name:'Denied',paymentTermsDays:0}})).status()===403,'Rental officer cannot create suppliers');
  check((await manager.get(`/api/branches/${branch.id}/configuration`)).status()===403,'Manager cannot read another branch configuration');
  check((await manager.patch(`/api/branches/${branch.id}/status`,{data:{isActive:false}})).status()===403,'Manager cannot change another branch status');
  check((await api('GET','/branches')).find(x=>x.id===branch.id).isActive,'Denied branch-status write leaves SQL data unchanged');
  check((await manager.put(`/api/asset-categories/${category.id}/attributes`,{data:[]})).status()===403,'Manager cannot change another division category fields');
  check((await api('GET','/asset-categories')).find(x=>x.id===category.id).attributeDefinitions.length===1,'Denied category-field write leaves SQL data unchanged');
  const managerSession=await (await manager.get('/api/auth/session')).json();
  check((await manager.get(`/api/branches/${managerSession.branchId}/configuration`)).status()===200,'Manager can read their own branch configuration');
  const ownCategory=await api('POST','/asset-categories',{divisionId:managerSession.divisionId,code:'OWN',name:'Own category',personnelRequirement:'None',isActive:true});
  const ownFields=[{code:'OWNFIELD',name:'Own field',dataType:'Text',isRequired:false,isSearchable:true,isCustomerVisible:true,isReportable:true,displayOrder:0,optionsJson:'[]'}];
  check((await manager.put(`/api/asset-categories/${ownCategory.id}/attributes`,{data:ownFields})).status()===204,'Manager can edit fields within their assigned division');
  check((await api('GET','/asset-categories')).find(x=>x.id===ownCategory.id).attributeDefinitions[0]?.code==='OWNFIELD','Authorized manager field edit persists to SQL');
  check(errors.length===0,'No browser JavaScript crashes');
 } catch(e) {failures.push(e.message);await page.screenshot({path:artifacts+'/failure.png',fullPage:true}).catch(()=>{});}
 finally {
  await fs.writeFile(artifacts+'/results.json',JSON.stringify({passed,failures,errors},null,2));
  for(const item of passed)console.log('PASS '+item);
  for(const item of failures)console.error('FAIL '+item);
  await Promise.all(contexts.map(c=>c.dispose()));await browser.close();
 }
 assert.equal(failures.length,0,failures.join('\n'));
})().catch(e=>{console.error(e.message);process.exitCode=1});
