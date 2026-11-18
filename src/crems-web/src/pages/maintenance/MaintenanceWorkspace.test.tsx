// @vitest-environment jsdom
import React from 'react'
import {afterEach,beforeEach,expect,it,vi} from 'vitest'
import {cleanup,fireEvent,render,screen,waitFor,within} from '@testing-library/react'
import {MaintenanceWorkspace} from './MaintenanceWorkspace'
import type {Asset,Job,Detail} from './types'
const api=vi.hoisted(()=>({get:vi.fn(),post:vi.fn(),put:vi.fn()}));vi.mock('../../api/client',()=>({api}))
const asset:Asset={id:'a',assetNumber:'EQ-1',name:'Training equipment',branchId:'b',divisionId:'d',branchName:'Suva',status:'Maintenance',meterUnit:'hours',currentMeterReading:1000,nextServiceDate:null}
const options={branches:[{branchId:'b',name:'Suva'}],technicians:[],suppliers:[],technicianNames:[]};const permissions={canComplete:true,canInspect:true,canFinancial:true}
const job=(status='Open'):Job=>({id:'j',version:'v1',jobNumber:'JOB-1',assetId:'a',assetNumber:'EQ-1',assetName:'Training equipment',branchId:'b',branchName:'Suva',assetStatus:'Maintenance',meterUnit:'hours',currentMeterReading:1000,status,priority:'Normal',serviceType:'Corrective repair',faultDescription:'Repair leaking hydraulic hose',completionNotes:status==='Completed'?'Hose replaced and pressure tested.':null,assignedTo:null,assignedPersonnelId:null,supplier:null,supplierId:null,sourceType:'Manual',sourceInspectionId:null,sourceReference:null,reportedByName:'Training Staff',isPreventive:false,nextServiceDate:null,nextServiceMeter:null,serviceIntervalMonths:null,warrantyCovered:false,warrantyClaimNumber:null,parentFailureJobId:null,useDetailedCosts:true,hasEstimate:true,estimatedCost:100,actualCost:null,partsCost:0,labourCost:0,transportCost:0,externalServiceCost:0,taxMode:'Exclusive',taxRate:0,taxableCosts:9,taxOverrideReason:null,taxCost:0,otherCost:0,fuelCost:0,labourHours:null,labourRate:null,invoiceNumber:null,partsUsed:null,meterReading:status==='Completed'?1001:null,downtimeHours:0,reportedAt:'2026-11-01T00:00:00Z',startedAt:null,completedAt:status==='Completed'?'2026-11-02T00:00:00Z':null,expectedReleaseAt:null,releasedAt:null,releasedByName:null})
beforeEach(()=>{vi.clearAllMocks();api.post.mockResolvedValue({data:{id:'j',jobNumber:'JOB-1',message:'Returned to service.'}});api.put.mockResolvedValue({data:{}})});afterEach(()=>cleanup())
async function view(status:string|null=null){const saved=vi.fn(),close=vi.fn();const current=job(status??'Open');const detail:Detail={job:current,history:[current],audits:[{action:'Job created',userName:'Training Staff',occurredAt:'2026-11-01T00:00:00Z',summary:'Training job recorded'}],meters:[{reading:1000,unit:'hours',recordedAt:'2026-11-01T00:00:00Z',source:'Manual'}],inspections:[],metrics:{completedJobs:1,downtimeHours:2,repeatFailures:0,totalCost:100},safetyChecks:['Hydraulics tested','Guards secure'],permissions,blockingJobs:[]};api.get.mockImplementation(async(url:string)=>({data:url==='/maintenance-jobs/j/workspace'?detail:url==='/maintenance-jobs/workspace'?{items:[]}:url==='/maintenance-jobs/assets'?[asset]:[]}));render(<MaintenanceWorkspace jobId={status?'j':null} initialAsset={asset} options={options} permissions={permissions} onClose={close} onSaved={saved} onOpenJob={vi.fn()}/>);await screen.findByLabelText(status==='Completed'?/Verified meter reading/:/Work required/);return{saved,close}}
const click=(name:string|RegExp)=>fireEvent.click(screen.getByRole('button',{name}));const change=(label:RegExp,value:string)=>fireEvent.change(screen.getByLabelText(label),{target:{value}})
it('logs a preventive job with its selected asset and explicit open status',async()=>{const{saved}=await view();change(/Work required/,'Scheduled oil and filter service');click('Create job');await waitFor(()=>expect(api.post).toHaveBeenCalledWith('/maintenance-jobs',expect.objectContaining({assetId:'a',faultDescription:'Scheduled oil and filter service',isPreventive:true,status:'Open',sourceType:'Preventive',calculateDowntime:true})));await waitFor(()=>expect(saved).toHaveBeenCalledWith(expect.stringContaining('JOB-1 logged.'),'j'))})
it('keeps entered work when creating the job fails',async()=>{await view();api.post.mockRejectedValue(Error('offline'));change(/Work required/,'Draft repair request');click('Create job');expect(await screen.findByText('The request could not be completed. Your entered details have been kept.')).toBeTruthy();expect((screen.getByLabelText(/Work required/) as HTMLInputElement).value).toBe('Draft repair request')})
it('requires an assigned technician or supplier before starting repair',async()=>{await view('Open');click('Save & start work');await screen.findByText('Start the work?');click('Confirm');expect(await screen.findByText('Assign a technician or an external supplier before starting work.')).toBeTruthy();expect(api.put).not.toHaveBeenCalled()})
it('records a cancellation reason and uses the concurrency version',async()=>{await view('Open');fireEvent.click(screen.getByText('Job reference & actions'));click('Cancel job');await screen.findByText('Cancel this job?');expect((screen.getByRole('button',{name:'Confirm'}) as HTMLButtonElement).disabled).toBe(true);change(/Reason for status change/,'Duplicate job entered in training.');click('Confirm');await waitFor(()=>expect(api.put).toHaveBeenCalledWith('/maintenance-jobs/j',expect.objectContaining({status:'Cancelled',expectedVersion:'v1',transitionReason:'Duplicate job entered in training.'})))})
it('requires all safety checks before releasing a completed repair',async()=>{const{saved}=await view('Completed');const release=screen.getByRole('button',{name:'Confirm safety and return to service'});expect((release as HTMLButtonElement).disabled).toBe(true);fireEvent.click(screen.getByLabelText('Hydraulics tested'));fireEvent.click(screen.getByLabelText('Guards secure'));change(/Verified meter reading/,'1001');change(/Safety check \/ test run notes/,'Pressure test passed; no leaks.');fireEvent.click(release);await waitFor(()=>expect(api.post).toHaveBeenCalledWith('/maintenance-jobs/j/release',{expectedVersion:'v1',notes:'Pressure test passed; no leaks.',meterReading:1001,passedChecks:['Hydraulics tested','Guards secure']}));await waitFor(()=>expect(saved).toHaveBeenCalledWith('Returned to service.'))})
it('keeps unsaved changes until the user explicitly discards them',async()=>{const{close}=await view();change(/Work required/,'Unsaved work details');click('Cancel');await screen.findByText('Discard unsaved changes?');expect(close).not.toHaveBeenCalled();click('Keep editing');await waitFor(()=>expect(screen.queryByText('Discard unsaved changes?')).toBeNull());expect((screen.getByLabelText(/Work required/) as HTMLInputElement).value).toBe('Unsaved work details');click('Cancel');click('Discard changes');expect(close).toHaveBeenCalledOnce()})
it('shows the service timeline and audit detail for a saved job',async()=>{await view('Open');click('View service record');await screen.findByText('Service timeline');fireEvent.click(screen.getByText('Job audit trail'));click('Job created · Training Staff');const dialogs=screen.getAllByRole('dialog');expect(within(dialogs.at(-1)!).getByText('Training job recorded')).toBeTruthy()})

async function richView(status='InProgress', patch:Partial<Job>={}, detailPatch:Partial<Detail>={}) {
 const current={...job(status),assignedPersonnelId:'tech',assignedTo:'Training Technician',labourRate:25,...patch}
 const detail:Detail={job:current,history:[current,{...job('Completed'),id:'old',jobNumber:'JOB-OLD'}],audits:[],meters:[{reading:1000,unit:'hours',recordedAt:'2026-11-01',source:'Manual'}],inspections:[{id:'inspection',stage:'Maintenance',outcome:'Passed',completedAt:'2026-11-01',completedByName:'Inspector',notes:'Checked guards'}],metrics:{completedJobs:2,downtimeHours:4,repeatFailures:1,totalRecordedCost:150,totalCost:100,bookValue:1000,costToBookValuePercent:10},safetyChecks:['Hydraulics tested','Guards secure'],permissions,blockingJobs:[],...detailPatch}
 const technicians=[{id:'tech',fullName:'Training Technician',employeeNumber:'EMP-1',branchId:'b',divisionId:'d',availability:'Available',standardCostRate:25},{id:'leave',fullName:'On leave',employeeNumber:'EMP-2',branchId:'b',divisionId:'d',availability:'Leave'}]
 api.get.mockImplementation(async(url:string)=>({data:url==='/maintenance-jobs/j/workspace'?detail:url==='/maintenance-jobs/workspace'?{items:[{...job('Completed'),id:'old',jobNumber:'JOB-OLD'}]}:url==='/maintenance-jobs/assets'?[asset]:url==='/maintenance-jobs/inspections'?[{id:'inspection',stage:'Maintenance',outcome:'Passed',completedAt:'2026-11-01',notes:'Found leak'}]:url==='/maintenance-jobs/inspections/inspection'?{id:'inspection',assetNumber:'EQ-1',stage:'Maintenance',outcome:'Passed',completedAt:'2026-11-01',completedByName:'Inspector',responsesJson:'[]',notes:null}:url.endsWith('/documents')?[{id:'doc',fileName:'repair.pdf',type:'Invoice'}]:[]}))
 const close=vi.fn(),saved=vi.fn(),openJob=vi.fn()
 render(<MaintenanceWorkspace jobId="j" initialAsset={asset} options={{...options,technicians,suppliers:[{id:'supplier',name:'Workshop supplier',supplierNumber:'SUP-1'}]}} permissions={permissions} onClose={close} onSaved={saved} onOpenJob={openJob}/> )
 if(status==='Completed')await screen.findByLabelText(/Safety check \/ test run notes/);else await screen.findByText('Record the repair')
 return {close,saved,openJob,detail}
}
async function choose(name:string,option:string|RegExp){fireEvent.mouseDown(screen.getByRole('combobox',{name}));fireEvent.click(await screen.findByRole('option',{name:option}))}
it('records expense breakdown, calculated labour, tax, warranty and next-service targets',async()=>{
 await richView();fireEvent.click(screen.getByText('Costs & files (optional)'))
 for(const [name,value] of [['Estimated cost (FJD)','300'],['Parts (FJD)','100'],['Transport (FJD)','20'],['Contractor / external service (FJD)','30'],['Fuel (FJD)','10'],['Other (FJD)','5'],['Labour hours','2'],['Supplier invoice / reference','INV-TRAIN']])fireEvent.change(screen.getByLabelText(name),{target:{value}})
 fireEvent.change(screen.getByLabelText(/Tax rate/) ,{target:{value:'15'}})
 fireEvent.click(screen.getByRole('button',{name:'Use division rate (0%)'}))
 click('Add warranty details');fireEvent.click(screen.getByLabelText('Covered by warranty'));fireEvent.change(screen.getByLabelText('Warranty claim reference'),{target:{value:'WARRANTY-1'}})
 fireEvent.click(screen.getByText('Service schedule'));await choose('Service / repair type','Vehicle passing')
 change(/Next passing date/,'2027-01-01');change(/Next service threshold/,'2000');fireEvent.click(screen.getByLabelText('Preventive service'));change(/Repeat service every/,'6')
 change(/Completion notes/,'Replaced seal and tested');change(/Final meter reading/,'1002');change(/Downtime hours/,'3');click('Save for later')
 await waitFor(()=>expect(api.put).toHaveBeenCalledWith('/maintenance-jobs/j',expect.objectContaining({estimatedCost:300,partsCost:100,labourCost:50,transportCost:20,externalServiceCost:30,fuelCost:10,otherCost:5,invoiceNumber:'INV-TRAIN',warrantyCovered:true,warrantyClaimNumber:'WARRANTY-1',nextServiceDate:'2027-01-01',nextServiceMeter:2000,serviceIntervalMonths:6,serviceType:'Vehicle passing',meterReading:1002,downtimeHours:3})))
})
it('records a supplier total and clears detailed costs in the submitted payload',async()=>{
 await richView();fireEvent.click(screen.getByText('Costs & files (optional)'));fireEvent.click(screen.getByLabelText('Calculate total from expense breakdown'));change(/Total cost without breakdown/,'450');click('Save for later')
 await waitFor(()=>expect(api.put).toHaveBeenCalledWith('/maintenance-jobs/j',expect.objectContaining({useDetailedCosts:false,actualCost:450,partsCost:0,labourCost:0,labourHours:null,labourRate:null,taxCost:0})))
})
it('requires completed work and a final meter reading before finishing repair',async()=>{
 await richView();click('Finish repair');click('Confirm');await screen.findByText('Record the work performed and final meter reading before completing the work.')
 await waitFor(()=>expect(screen.queryByText('Complete the work?')).toBeNull());change(/Completion notes/,'Repaired hose');change(/Final meter reading/,'1001');fireEvent.click(await screen.findByRole('button',{name:'Finish repair'}));click('Confirm')
 await waitFor(()=>expect(api.put).toHaveBeenCalledWith('/maintenance-jobs/j',expect.objectContaining({status:'Completed',completionNotes:'Repaired hose',meterReading:1001})))
})
it('requires a wait reason and resumes an assigned waiting job',async()=>{
 await richView();click('Wait for parts');change(/Reason for status change/,'Seal kit ordered');click('Confirm')
 await waitFor(()=>expect(api.put).toHaveBeenCalledWith('/maintenance-jobs/j',expect.objectContaining({status:'WaitingForParts',transitionReason:'Seal kit ordered'})))
 cleanup();await richView('WaitingForParts');click('Resume work');click('Confirm')
 await waitFor(()=>expect(api.put).toHaveBeenCalledWith('/maintenance-jobs/j',expect.objectContaining({status:'InProgress'})))
})
it.each([
 [{isAxiosError:true,response:{status:403}},'You do not have permission for this action.'],
 [{isAxiosError:true,response:{data:{message:'Concurrent edit detected.'}}},'Concurrent edit detected.'],
 [{isAxiosError:true,response:{data:{errors:{cost:['Cost is invalid.']}}}},'Cost is invalid.'],
 [{isAxiosError:true},'The request could not be completed. Retry when the connection is available.'],
])('keeps repair changes after a failed save: %s',async(error,message)=>{
 await richView();api.put.mockRejectedValue(error);change(/Completion notes/,'Preserved repair notes');click('Save for later');await screen.findByText(message)
 expect((screen.getByLabelText(/Completion notes/) as HTMLInputElement).value).toBe('Preserved repair notes')
})
it('refreshes blocking work after release rejection and retains safety observations',async()=>{
 const blocker={...job('Open'),id:'block',jobNumber:'JOB-BLOCK',supplier:'External workshop',expectedReleaseAt:'2026-12-01'}
 const{detail,openJob}=await richView('Completed',{}, {blockingJobs:[blocker],restrictedBlockingJobCount:1})
 expect(screen.getByText(/outside your access/)).toBeTruthy();click('Open JOB-BLOCK');expect(openJob).toHaveBeenCalledWith('block')
 fireEvent.click(screen.getByLabelText('Hydraulics tested'));fireEvent.click(screen.getByLabelText('Guards secure'));fireEvent.click(screen.getByLabelText('Guards secure'));fireEvent.click(screen.getByLabelText('Guards secure'))
 change(/Safety check \/ test run notes/,'No leaks under load');api.post.mockRejectedValue({isAxiosError:true,response:{data:{message:'Another repair is still open.'}}});click('Confirm safety and return to service')
 await screen.findByText('Another repair is still open.');expect((screen.getByLabelText(/Safety check \/ test run notes/) as HTMLInputElement).value).toBe('No leaks under load')
 detail.blockingJobs=[];detail.restrictedBlockingJobCount=0;click('Refresh open work');await screen.findByText('No other open maintenance jobs. The safety check and other release requirements still apply.')
})
it('guards safety navigation and resets discarded safety edits',async()=>{
 await richView('Completed');change(/Safety check \/ test run notes/,'Unsaved safety notes')
 fireEvent.click(screen.getByRole('button',{name:/Plan$/}));await screen.findByText('Discard unsaved changes?');click('Discard changes')
 await screen.findByText('Plan the work');fireEvent.click(await screen.findByRole('button',{name:'View safety check'}));expect((await screen.findByLabelText(/Safety check \/ test run notes/) as HTMLInputElement).value).toBe('')
})
it('opens historical jobs and inspection records from the service timeline',async()=>{
 const{openJob}=await richView();click('View service record');click('JOB-OLD · Completed');expect(openJob).toHaveBeenCalledWith('old')
 fireEvent.click(screen.getByText('Inspection history'));click(/Maintenance · Passed/);await screen.findByText('Inspection reference: inspection');click('Close inspection record')
 fireEvent.click(screen.getByText('Meter history'));expect(screen.getByText(/1000 hours · Manual/)).toBeTruthy()
})
it('uploads evidence and rejects oversized files before sending',async()=>{
 await richView();fireEvent.click(screen.getByText('Costs & files (optional)'))
 const input=document.querySelector<HTMLInputElement>('input[type="file"]')!
 const oversized=new File(['x'],'large.pdf',{type:'application/pdf'});Object.defineProperty(oversized,'size',{value:6*1024*1024})
 fireEvent.change(input,{target:{files:[oversized]}});await screen.findByText('Choose a PDF, JPEG or PNG up to 5 MB.');expect(api.post).not.toHaveBeenCalled()
 await choose('Attachment category','Completion evidence');fireEvent.change(input,{target:{files:[new File(['evidence'],'repair.png',{type:'image/png'})]}})
 await waitFor(()=>expect(api.post).toHaveBeenCalledWith('/maintenance-jobs/j/documents',expect.any(FormData)))
 expect(api.post.mock.calls[0][1].get('type')).toBe('CompletionEvidence')
})
it('changes planning assignments, timing and links to the originating inspection and prior failure',async()=>{
 const{openJob}=await richView('InProgress',{sourceInspectionId:'inspection',parentFailureJobId:'old',sourceReference:'Damage report',expectedReleaseAt:'2026-12-01T10:00:00Z'})
 fireEvent.click(screen.getByRole('button',{name:/Plan$/}))
 await choose('Technician','Training Technician (EMP-1)')
 await choose('Supplier (optional)','Workshop supplier (SUP-1)')
 await choose('Priority','High')
 fireEvent.click(screen.getByText('Expected timing (optional)'));change(/Expected release/,'2026-12-02T12:00')
 fireEvent.click(screen.getByText('Where was the fault reported?'));click('Open related failure');click('Keep editing');await waitFor(()=>expect(screen.queryByText('Discard unsaved changes?')).toBeNull());expect(openJob).not.toHaveBeenCalled()
 click('View originating inspection');await screen.findByText('Inspection reference: inspection');click('Close inspection record')
 await waitFor(()=>expect(screen.queryByText('Inspection record')).toBeNull())
 await choose('Previous job for the same fault','JOB-OLD · Corrective repair');click('Save for later')
 await waitFor(()=>expect(api.put).toHaveBeenCalledWith('/maintenance-jobs/j',expect.objectContaining({assignedPersonnelId:'tech',supplierId:'supplier',priority:'High',parentFailureJobId:'old',expectedReleaseAt:new Date('2026-12-02T12:00').toISOString()})))
})
it('downloads a saved attachment and preserves the file list after a failed download or upload',async()=>{
 const create=vi.fn().mockReturnValue('blob:maintenance'),revoke=vi.fn();Object.defineProperty(URL,'createObjectURL',{configurable:true,value:create});Object.defineProperty(URL,'revokeObjectURL',{configurable:true,value:revoke});const anchor=vi.spyOn(HTMLAnchorElement.prototype,'click').mockImplementation(()=>{})
 await richView();fireEvent.click(screen.getByText('Costs & files (optional)'))
 api.get.mockResolvedValueOnce({data:new Blob(['pdf'])});click('repair.pdf · Invoice');await waitFor(()=>expect(anchor).toHaveBeenCalledOnce());expect(create).toHaveBeenCalledOnce()
 api.get.mockRejectedValueOnce(Error('Offline'));click('repair.pdf · Invoice');await screen.findByText('The request could not be completed. Your entered details have been kept.')
 api.post.mockRejectedValueOnce(Error('Offline'));const input=document.querySelector<HTMLInputElement>('input[type="file"]')!;fireEvent.change(input,{target:{files:[new File(['x'],'invoice.pdf',{type:'application/pdf'})]}});await waitFor(()=>expect(api.post).toHaveBeenCalled())
 anchor.mockRestore()
})
it('creates corrective work from a selected asset, inspection report and prior repair',async()=>{
 const saved=vi.fn()
 api.get.mockImplementation(async(url:string)=>({data:url==='/maintenance-jobs/assets'?[asset]:url==='/maintenance-jobs/workspace'?{items:[{...job('Completed'),id:'old',jobNumber:'JOB-OLD'}]}:url==='/maintenance-jobs/inspections'?[{id:'origin',stage:'Maintenance',outcome:'Failed',completedAt:'2026-11-01',notes:'Leak'}]:[]}))
 render(<MaintenanceWorkspace jobId={null} initialAsset={null} options={options} permissions={permissions} onClose={vi.fn()} onSaved={saved} onOpenJob={vi.fn()}/> )
 fireEvent.focus(screen.getByRole('combobox',{name:/Search asset number/}));fireEvent.change(screen.getByRole('combobox',{name:/Search asset number/}),{target:{value:'EQ'}})
 fireEvent.click(await screen.findByRole('option',{name:/EQ-1 · Training equipment/}))
 change(/Work required/,'Repair inspection fault');fireEvent.click(screen.getByText('Where was the fault reported?'));await choose('Reported from','Asset inspection');await choose('Link an inspection (optional)',/Maintenance · Failed/);await choose('Previous job for the same fault','JOB-OLD · Corrective repair')
 fireEvent.click(screen.getByText('Estimate & warranty'));change(/Estimated cost/,'50');click('Add warranty details');fireEvent.click(screen.getByLabelText('Covered by warranty'));change(/Warranty claim reference/,'CLAIM-NEW')
 click('Create job');await waitFor(()=>expect(api.post).toHaveBeenCalledWith('/maintenance-jobs',expect.objectContaining({assetId:'a',sourceType:'Inspection',sourceInspectionId:'origin',sourceReference:'Maintenance inspection origin',parentFailureJobId:'old',estimatedCost:50,warrantyClaimNumber:'CLAIM-NEW'})))
})
