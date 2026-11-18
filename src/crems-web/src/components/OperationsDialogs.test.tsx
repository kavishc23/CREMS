// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { OperationsCreateDialog } from './OperationsCreateDialog'
import { OperationsRecordDialog } from './OperationsRecordDialog'
import { api } from '../api/client'
vi.mock('../api/client',()=>({api:{get:vi.fn(),post:vi.fn(),patch:vi.fn()}}))
beforeEach(()=>{vi.resetAllMocks();vi.mocked(api.post).mockResolvedValue({});vi.mocked(api.patch).mockResolvedValue({});vi.mocked(api.get).mockImplementation(async url=>({data:url==='/branches'?[{id:'b',name:'Suva'},{id:'b2',name:'Nadi'}]:url==='/divisions'?[{id:'d',name:'Motors'}]:[{id:'a',assetNumber:'CAR-1',name:'Training car',branchId:'b'}]}) as never)});afterEach(cleanup)
async function select(label:string,name:string|RegExp){fireEvent.mouseDown(await screen.findByRole('combobox',{name:new RegExp('^'+label)}));fireEvent.click(await screen.findByRole('option',{name}))}
it.each([
 ['personnel','/business-operations/personnel',{employeeNumber:'EMP-1',fullName:'Training Driver',standardCostRate:10},['Employee number','Full name','Standard cost rate'],['EMP-1','Training Driver','10']],
 ['parts','/corporate-operations/parts',{partNumber:'PART-1',name:'Seal',unitCost:20},['Part number','Part name','Unit cost'],['PART-1','Seal','20']],
 ['suppliers','/business-operations/suppliers',{supplierNumber:'SUP-1',name:'Workshop',paymentTermsDays:30},['Supplier number','Supplier name','Payment terms (days)'],['SUP-1','Workshop','30']],
 ['deliveryZones','/business-operations/delivery-zones',{name:'Suva zone',baseCharge:50},['Zone name','Base charge'],['Suva zone','50']],
 ['pricing','/corporate-operations/pricing',{name:'Daily rate',rate:100,weekendMultiplier:1},['Rate card name','Rate'],['Daily rate','100']],
])('creates a %s record with numeric fields and scoped lookups',async(sectionKey,route,payload,labels,values)=>{
 const close=vi.fn(),reload=vi.fn().mockResolvedValue(undefined);render(<OperationsCreateDialog open sectionKey={sectionKey} onClose={close} reload={reload}/> )
 for(let i=0;i<labels.length;i++)fireEvent.change(screen.getByLabelText(new RegExp('^'+labels[i].replace(/[()]/g,'\\$&')+(labels[i]==='Rate'?'$':''))),{target:{value:values[i]}})
 if(sectionKey!=='suppliers')await select('Branch','Suva');if(['personnel','deliveryZones','pricing'].includes(sectionKey))await select('Division','Motors');if(sectionKey==='personnel')await select('Role','Driver');if(sectionKey==='pricing')await select('Rate period','Daily')
 fireEvent.click(screen.getByRole('button',{name:'Create'}));await waitFor(()=>expect(reload).toHaveBeenCalledOnce());expect(api.post).toHaveBeenCalledWith(route,expect.objectContaining(payload));expect(close).toHaveBeenCalledOnce()
})
it('fills transfer origin from asset and prevents transferring to the same branch',async()=>{
 const reload=vi.fn().mockResolvedValue(undefined);render(<OperationsCreateDialog open sectionKey="transfers" onClose={vi.fn()} reload={reload}/>);await select('Asset','CAR-1 Training car');await select('To branch','Suva');fireEvent.change(screen.getByLabelText(/Reason/),{target:{value:'Move fleet'}});fireEvent.click(screen.getByRole('button',{name:'Create'}));await screen.findByText('Select a different destination branch.');expect(api.post).not.toHaveBeenCalled();await select('To branch','Nadi');fireEvent.change(screen.getByLabelText('Transfer cost'),{target:{value:'25'}});fireEvent.click(screen.getByRole('button',{name:'Create'}));await waitFor(()=>expect(api.post).toHaveBeenCalledWith('/corporate-operations/transfers',expect.objectContaining({assetId:'a',fromBranchId:'b',toBranchId:'b2',transferCost:25})))
})
it.each([[new Error('Offline'),'The record could not be created.'],[{isAxiosError:true,response:{data:{message:'Duplicate record.'}}},'Duplicate record.'],[{isAxiosError:true,response:{data:{errors:{name:['Invalid name.']}}}},'Invalid name.']])('shows create errors without dropping the form %s',async(error,message)=>{
 vi.mocked(api.post).mockRejectedValue(error);render(<OperationsCreateDialog open sectionKey="suppliers" onClose={vi.fn()} reload={vi.fn()}/>);fireEvent.change(screen.getByLabelText(/Supplier number/),{target:{value:'SUP'}});fireEvent.change(screen.getByLabelText(/Supplier name/),{target:{value:'Workshop'}});fireEvent.click(screen.getByRole('button',{name:'Create'}));await screen.findByText(message)
})
it('reports unavailable lookup lists and closes a clean form',async()=>{
 vi.mocked(api.get).mockRejectedValue(Error('Offline'));const close=vi.fn();render(<OperationsCreateDialog open sectionKey="parts" onClose={close} reload={vi.fn()}/>);await screen.findByText('Some selection lists could not be loaded.');fireEvent.click(screen.getByRole('button',{name:'Cancel'}));expect(close).toHaveBeenCalledOnce()
})
it.each(['Requested','Approved','InTransit','Received'])('advances a transfer from %s with recorded condition and meter',async status=>{
 const close=vi.fn(),reload=vi.fn().mockResolvedValue(undefined);render(<OperationsRecordDialog sectionKey="transfers" row={{id:'t',transferNumber:'TR-1',status,priority:'Critical',isActive:true,blocked:false,empty:'',date:'2026-11-16T00:00:00Z'}} onClose={close} reload={reload}/>);fireEvent.change(screen.getByLabelText('Meter reading (if applicable)'),{target:{value:'12000'}});fireEvent.change(screen.getByLabelText(/Condition notes/),{target:{value:'Good condition'}});fireEvent.click(screen.getByRole('button',{name:'Save transfer status'}));await waitFor(()=>expect(reload).toHaveBeenCalledOnce());expect(api.patch).toHaveBeenCalledWith('/corporate-operations/transfers/t/status',expect.objectContaining({status:{Requested:'Approved',Approved:'InTransit',InTransit:'Received',Received:'Inspected'}[status],meterReading:12000,conditionNotes:'Good condition',evidenceDataUrls:[]}))
})
it('supports cancellation without optional readings and acknowledges alerts',async()=>{
 render(<OperationsRecordDialog sectionKey="transfers" row={{id:'t',status:'Requested',transferNumber:'TR-1'}} onClose={vi.fn()} reload={vi.fn()}/>);await select('Next status','Cancelled');fireEvent.click(screen.getByRole('button',{name:'Save transfer status'}));await waitFor(()=>expect(api.patch).toHaveBeenCalledWith('/corporate-operations/transfers/t/status',expect.objectContaining({status:'Cancelled',meterReading:null,conditionNotes:null})));cleanup();render(<OperationsRecordDialog sectionKey="alerts" row={{id:'alert',message:'Overdue vehicle'}} onClose={vi.fn()} reload={vi.fn()}/>);fireEvent.click(screen.getByRole('button',{name:'Acknowledge'}));await waitFor(()=>expect(api.post).toHaveBeenCalledWith('/business-operations/alerts/alert/acknowledge'))
})
it.each([[new Error('Offline'),'This action could not be saved.'],[{isAxiosError:true,response:{data:{message:'Access denied.'}}},'Access denied.'],[{isAxiosError:true,response:{data:{errors:{status:['Invalid status.']}}}},'Invalid status.']])('keeps a failed operational action available to retry %s',async(error,message)=>{
 vi.mocked(api.post).mockRejectedValue(error);render(<OperationsRecordDialog sectionKey="alerts" row={{id:'a',name:'Alert'}} onClose={vi.fn()} reload={vi.fn()}/>);fireEvent.click(screen.getByRole('button',{name:'Acknowledge'}));await screen.findByText(message)
})
