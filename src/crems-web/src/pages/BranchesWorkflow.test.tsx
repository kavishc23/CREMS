// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { BranchesPage } from './BranchesPage'
import { api } from '../api/client'
vi.mock('../api/client', () => ({ api: { get: vi.fn(), post: vi.fn(), put: vi.fn(), patch: vi.fn() } }))
const branch = { id: 'b', code: 'SUV', name: 'Suva', address: null, phone: null, isActive: true, divisionIds: ['d'], postalAddress: null, email: null, latitude: null, longitude: null, branchManagerUserId: null, pickupInstructions: null, returnInstructions: null, deliveryCoverage: null, isPublic: true }
beforeEach(() => {
 vi.resetAllMocks(); vi.mocked(api.post).mockResolvedValue({}); vi.mocked(api.put).mockResolvedValue({}); vi.mocked(api.patch).mockResolvedValue({})
 vi.mocked(api.get).mockImplementation(async url => ({ data: url === '/branches' ? [branch] : url === '/divisions' ? [{ id: 'd', name: 'Motors', isActive: true, services: [{ id: 's', name: 'Vehicle hire', isActive: true }] }] : url === '/asset-categories' ? [{ name: 'Cars', divisionId: 'd', serviceOfferingId: 's' }] : { services: [{ serviceOfferingId: 's', isActive: true }] } }) as never)
})
afterEach(cleanup)
const click = (name: string | RegExp) => fireEvent.click(screen.getByRole('button', { name }))
const change = (label: string | RegExp, value: string) => fireEvent.change(screen.getByLabelText(label), { target: { value } })
async function view(roles = ['SuperAdministrator']) { const saved = vi.fn(), configure = vi.fn(); render(<BranchesPage userRoles={roles} onSaved={saved} onConfigure={configure} />); await screen.findByText('Suva'); return { saved, configure } }
it('creates a branch with contact, coordinates, customer instructions and eligible services', async () => {
 const { saved } = await view(); click('Add branch')
 change(/Branch code/, 'NAD'); change(/Branch name/, 'Nadi'); change('Address','Airport road'); change('Phone','12345'); change('Public contact email','nadi@example.test'); click('Location details & customer instructions')
 for (const [label,value] of [['Postal address','PO Box 1'],['Latitude','-17.75'],['Longitude','177.45'],['Pickup instructions','Bring ID'],['Return instructions','Refuel'],['Delivery coverage','Nadi region']]) change(label,value)
 fireEvent.click(screen.getByLabelText('Show this branch to customers'))
 fireEvent.click(within(screen.getByRole('dialog')).getByRole('button',{name:'Motors'})); fireEvent.click(screen.getByLabelText(/Vehicle hire/)); click('Save branch')
 await waitFor(()=>expect(saved).toHaveBeenCalledOnce())
 expect(api.post).toHaveBeenCalledWith('/branches',expect.objectContaining({code:'NAD',name:'Nadi',address:'Airport road',postalAddress:'PO Box 1',latitude:-17.75,longitude:177.45,pickupInstructions:'Bring ID',returnInstructions:'Refuel',deliveryCoverage:'Nadi region',serviceOfferingIds:['s'],divisionIds:['d'],isPublic:false,branchManagerUserId:null}))
})
it('requires a division, edits existing branch service assignments, and opens its calendar',async()=>{
 const{configure}=await view();click('Services & hours');expect(configure).toHaveBeenCalledWith('b');click('Edit branch details')
 await waitFor(()=>expect((screen.getByLabelText(/Vehicle hire/) as HTMLInputElement).checked).toBe(true))
 fireEvent.click(screen.getByLabelText(/Vehicle hire/));change(/Branch name/,'Suva Central');click('Save branch')
 await waitFor(()=>expect(api.put).toHaveBeenCalledWith('/branches/b',expect.objectContaining({name:'Suva Central',serviceOfferingIds:[],latitude:null,longitude:null})))
 await waitFor(()=>expect(screen.queryByRole('dialog')).toBeNull());click('Add branch');change(/Branch code/,'NEW');change(/Branch name/,'New branch');click('Save branch');await screen.findByText('Assign at least one business/division to this branch.')
})
it('limits non-administrators to existing branch details',async()=>{
 await view(['BranchManager']);expect(screen.queryByRole('button',{name:'Add branch'})).toBeNull();click('Edit branch details');expect((screen.getByLabelText(/Branch code/) as HTMLInputElement).disabled).toBe(true);change(/Branch name/,'Branch office');click('Save branch')
 await waitFor(()=>expect(api.put).toHaveBeenCalledWith('/branches/b',expect.objectContaining({name:'Branch office',serviceOfferingIds:undefined})))
})
it.each([[new Error('Offline'),'Unable to save the branch.'],[{isAxiosError:true,response:{data:{message:'Duplicate code.'}}},'Duplicate code.'],[{isAxiosError:true,response:{data:{errors:{code:['Code unavailable.']}}}},'Code unavailable.']])('preserves failed branch edits %s',async(error,message)=>{
 await view();click('Edit branch details');await waitFor(()=>expect((screen.getByRole('button',{name:'Save branch'}) as HTMLButtonElement).disabled).toBe(false));vi.mocked(api.put).mockRejectedValue(error);change(/Branch name/,'Preserved branch');click('Save branch');await screen.findByText(message);expect((screen.getByLabelText(/Branch name/) as HTMLInputElement).value).toBe('Preserved branch')
})
it('toggles branch availability and reports update errors',async()=>{
 await view();fireEvent.click(screen.getByRole('switch'));await waitFor(()=>expect(api.patch).toHaveBeenCalledWith('/branches/b/status',{isActive:false}));vi.mocked(api.patch).mockRejectedValue(new Error('Offline'));fireEvent.click(screen.getByRole('switch'));await screen.findByText('Unable to update the branch status.')
})
it('prevents saving when existing operating service choices cannot load',async()=>{
 const normal=vi.mocked(api.get).getMockImplementation()!;vi.mocked(api.get).mockImplementation(async(url,config)=>{if(url.endsWith('/configuration'))throw new Error('Offline');return normal(url,config)})
 await view();click('Edit branch details');await screen.findByText('Operating services could not be loaded. Reopen the branch before saving.');expect((screen.getByRole('button',{name:'Save branch'}) as HTMLButtonElement).disabled).toBe(true);click('Cancel')
})
