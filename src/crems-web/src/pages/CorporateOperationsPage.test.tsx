// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { CorporateOperationsPage } from './CorporateOperationsPage'
import { api } from '../api/client'
vi.mock('../api/client',()=>({api:{get:vi.fn(),patch:vi.fn(),post:vi.fn()}}))
const session=vi.hoisted(()=>({user:{id:'staff',roles:['Administrator']}}));vi.mock('../auth/AuthContext',()=>({useAuth:()=>session}))
const workspace=()=>({tasks:[{id:'task',title:'Prepare vehicle',priority:'Critical',category:'Rental',dueAt:'2026-11-16T00:00:00Z',description:'Inspect before pickup',assignedUserId:'staff'}],dispatches:[{id:'dispatch',dispatchNumber:'DSP-1',type:'Delivery',status:'Arrived',scheduledAt:'2026-11-16T00:00:00Z',address:'Suva',assignedDriver:'Driver'}],cases:[{id:'case',caseNumber:'CASE-1',subject:'Customer question',status:'Open',priority:'High',dueAt:null,assignedUserId:'staff'}],quotes:[],approvals:[],personnel:[{id:'person',fullName:'Training Driver',standardCostRate:10,available:true,blocked:false,date:'2026-11-16T00:00:00Z',nothing:''}],parts:[],suppliers:[],transfers:[]})
beforeEach(()=>{vi.resetAllMocks();session.user={id:'staff',roles:['Administrator']};vi.mocked(api.patch).mockResolvedValue({});vi.mocked(api.post).mockResolvedValue({});vi.mocked(api.get).mockImplementation(async url=>({data:url==='/corporate-operations/overview'?{activeRentals:4,overdueRentals:1,todayDispatches:1,pendingApprovals:2,outstandingReceivables:150,utilizationPercent:60}:url==='/corporate-operations/workspace'?workspace():url==='/business-operations/workspace'?{}:[]}) as never)});afterEach(cleanup)
const click=(name:string|RegExp)=>fireEvent.click(screen.getByRole('button',{name}))
async function view(embedded=false){render(<CorporateOperationsPage embedded={embedded}/>);await screen.findByText('Prepare vehicle')}
it('completes assigned tasks only after a confirmation action',async()=>{
 await view();expect(screen.getByText('FJD 150')).toBeTruthy();expect(screen.getByText('60%')).toBeTruthy();click('Complete');expect(api.patch).not.toHaveBeenCalled();fireEvent.click(within(screen.getByRole('dialog')).getByRole('button',{name:'Complete task'}));await waitFor(()=>expect(api.patch).toHaveBeenCalledWith('/corporate-operations/tasks/task/complete'))
})
it('requires a delivery receiver before recording completed dispatch',async()=>{
 await view();fireEvent.click(screen.getByRole('tab',{name:'Deliveries (1)'}));click('Update');expect((screen.getByRole('button',{name:'Save status'}) as HTMLButtonElement).disabled).toBe(true);fireEvent.change(screen.getByLabelText(/Received by/),{target:{value:'Training Receiver'}});fireEvent.change(screen.getByLabelText('Dispatch note'),{target:{value:'Delivered safely'}});click('Save status');await waitFor(()=>expect(api.patch).toHaveBeenCalledWith('/corporate-operations/dispatches/dispatch/status',{status:'Completed',signatureName:'Training Receiver',notes:'Delivered safely',evidenceDataUrls:[]}))
})
it('requires a resolution note for a resolved case',async()=>{
 await view();fireEvent.click(screen.getByRole('tab',{name:'Customer help (1)'}));click('Update');fireEvent.mouseDown(screen.getByRole('combobox',{name:'Next status'}));fireEvent.click(screen.getByRole('option',{name:'Resolved'}));expect((screen.getByRole('button',{name:'Save status'}) as HTMLButtonElement).disabled).toBe(true);fireEvent.change(screen.getByLabelText(/Resolution or update note/),{target:{value:'Answered rental query'}});click('Save status');await waitFor(()=>expect(api.patch).toHaveBeenCalledWith('/corporate-operations/cases/case/status',{status:'Resolved',resolution:'Answered rental query'}))
})
it('searches tool records, opens detail and creation dialogs, then returns to daily work',async()=>{
 await view(true);expect(screen.queryByText('Operations centre')).toBeNull();fireEvent.click(screen.getByRole('tab',{name:'Business tools'}));expect(screen.getByText('Training Driver')).toBeTruthy();expect(screen.getByText('FJD 10.00')).toBeTruthy();click('View details');await screen.findByText('Operator / driver · Training Driver');click('Close');await waitFor(()=>expect(screen.queryByRole('dialog')).toBeNull());click('Add operators & drivers');await screen.findByText('Add operator or driver');click('Cancel');await waitFor(()=>expect(screen.queryByRole('dialog')).toBeNull());fireEvent.change(screen.getByLabelText('Search operators & drivers'),{target:{value:'missing'}});expect(screen.getByText('No operators & drivers to show')).toBeTruthy();click("Back to today's work");expect(screen.getByText('Prepare vehicle')).toBeTruthy()
})
it('shows quotation, approval and readiness panels',async()=>{
 await view();fireEvent.click(screen.getByRole('tab',{name:'Quotes (0)'}));expect(screen.getByText('Customer quotations')).toBeTruthy();fireEvent.click(screen.getByRole('tab',{name:'Approvals (0)'}));expect(screen.getByText('Approval queue')).toBeTruthy();fireEvent.click(screen.getByRole('tab',{name:'Hire readiness (0)'}));expect(screen.getByText('No pickups or returns are due today.')).toBeTruthy()
})
it.each([[new Error('Offline'),'The action could not be saved. Check the required details and try again.'],[{isAxiosError:true,response:{data:{message:'Access denied.'}}},'Access denied.'],[{isAxiosError:true,response:{data:{errors:{status:['Invalid transition.']}}}},'Invalid transition.']])('retains failed operational decisions %s',async(error,message)=>{
 await view();vi.mocked(api.patch).mockRejectedValue(error);click('Complete');fireEvent.click(within(screen.getByRole('dialog')).getByRole('button',{name:'Complete task'}));await screen.findByText(message);expect(screen.getByRole('dialog')).toBeTruthy();click('Cancel')
})
it('keeps available sections usable after partial loading failure and retries on refresh',async()=>{
 const normal=vi.mocked(api.get).getMockImplementation()!;vi.mocked(api.get).mockImplementation(async(url,config)=>{if(url==='/hire-readiness')throw Error('Offline');return normal(url,config)});await view();await screen.findByText('Hire readiness could not be loaded. Use Refresh to retry. Available sections are shown below.');vi.mocked(api.get).mockImplementation(normal);click('Refresh');await screen.findByText('Prepare vehicle');expect(screen.queryByText(/could not be loaded/)).toBeNull()
})
