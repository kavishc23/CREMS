// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { createPortal } from 'react-dom'
import { CustomersPage } from './CustomersPage'
import { api } from '../api/client'
import { openProtectedFile } from '../utils/openProtectedFile'
vi.mock('../api/client',()=>({api:{get:vi.fn(),post:vi.fn(),put:vi.fn(),patch:vi.fn(),delete:vi.fn()}}))
vi.mock('../utils/openProtectedFile',()=>({openProtectedFile:vi.fn()}))
vi.mock('../components/LicenceWorkflowDialog',()=>({LicenceWorkflowDialog:({open,onClose,onSaved}:{open:boolean;onClose:()=>void;onSaved:()=>void})=>open?createPortal(<div role="dialog"><button onClick={onSaved}>Save training licence</button><button onClick={onClose}>Close licence editor</button></div>,document.body):null}))
const customer=()=>({id:'c',customerNumber:'CUST-1',name:'Training Customer',email:'customer@example.test',phone:'1234',address:'Suva',identificationNumber:null,isBlocked:false,isActive:true,hirePreferences:['Vehicles'],hasOnlineAccount:true,emailConfirmed:false,portalActive:true,portalLockoutEnd:null,driverLicenceDocumentId:null,driverLicenceFileName:null,licenceNumber:'DL-1',licenceClasses:'2',licenceStatus:'Verified'})
const activity=()=>({customer:customer(),account:{emailConfirmed:true,isActive:true,lastLoginAt:null,lastActivityAt:'2026-11-16',lockoutEnd:null},licence:{licenceNumber:'DL-1',licenceClasses:'2',status:'Verified',updatedAt:'2026-11-16'},summary:{bookings:11,quotations:1,invoices:1,totalBilled:100,outstanding:50},bookings:[{id:'b',bookingNumber:'BK-1',status:'Confirmed',createdAt:'2026-11-16',branchName:'Suva',assetCount:1}],quotations:[{id:'q',quoteNumber:'QUO-1',status:'Sent',total:100,createdAt:'2026-11-16',validUntil:'2026-12-01',branchName:'Suva'}],invoices:[{id:'i',invoiceNumber:'INV-1',status:'Issued',total:100,balanceDue:50,issuedAt:'2026-11-16'}],pagination:{page:1,pageSize:10,bookings:11,quotations:1,invoices:1}})
beforeEach(()=>{
 vi.resetAllMocks();vi.mocked(api.post).mockResolvedValue({data:{message:'Activation queued.'}});vi.mocked(api.put).mockResolvedValue({});vi.mocked(api.patch).mockResolvedValue({});vi.mocked(api.delete).mockResolvedValue({});vi.mocked(api.get).mockImplementation(async url=>({data:url.endsWith('/activity')?activity():[customer()],headers:{'x-total-count':'25','x-customer-total':'25','x-customer-verified':'10','x-customer-pending':'15','x-customer-blocked':'0','x-customer-licence-verified':'5'}}) as never)
});afterEach(cleanup)
const click=(name:string|RegExp)=>fireEvent.click(screen.getByRole('button',{name}))
const change=(name:string|RegExp,value:string)=>fireEvent.change(screen.getByLabelText(name),{target:{value}})
async function view(){render(<CustomersPage administrationView/>);await screen.findByText('Training Customer')}
it('creates and edits customer contact details without changing the system identifier',async()=>{
 await view();click('Add customer');for(const[label,value]of [['Full name','New Customer'],['Email','new@example.test'],['Phone','5555'],['Address','Nadi']])change(new RegExp(label),value);click('Save customer');await waitFor(()=>expect(api.post).toHaveBeenCalledWith('/customers',expect.objectContaining({name:'New Customer',email:'new@example.test',phone:'5555',address:'Nadi'})));await waitFor(()=>expect(screen.queryByRole('dialog')).toBeNull())
 click('Edit customer');change(/Full name/,'Edited Customer');click('Save customer');await waitFor(()=>expect(api.put).toHaveBeenCalledWith('/customers/c',expect.objectContaining({name:'Edited Customer',customerNumber:'CUST-1'})))
})
it.each([[new Error('Offline'),'Unable to save the customer.'],[{isAxiosError:true,response:{data:{message:'Duplicate email.'}}},'Unable to save the customer.'],[{isAxiosError:true,response:{data:{errors:{email:['Invalid email.']}}}},'Invalid email.']])('retains failed customer edits %s',async(error,message)=>{
 await view();click('Edit customer');vi.mocked(api.put).mockRejectedValue(error);change(/Full name/,'Unsaved customer');click('Save customer');await screen.findByText(message);expect((screen.getByLabelText(/Full name/) as HTMLInputElement).value).toBe('Unsaved customer');click('Cancel')
})
it('searches, pages, filters and blocks an eligible customer only after confirmation',async()=>{
 await view();fireEvent.change(screen.getByPlaceholderText('Search by name, customer number, email or phone'),{target:{value:'Training'}});await waitFor(()=>expect(api.get).toHaveBeenCalledWith('/customers',expect.objectContaining({params:expect.objectContaining({search:'Training'})})))
 fireEvent.mouseDown(screen.getByRole('combobox'));fireEvent.click(screen.getByRole('option',{name:'Active'}));await screen.findByText('Training Customer');click('Go to page 2');await waitFor(()=>expect(api.get).toHaveBeenCalledWith('/customers',expect.objectContaining({params:expect.objectContaining({page:2,status:'Active'})})))
 await screen.findByText('Training Customer');click('Block from rentals');expect(api.patch).not.toHaveBeenCalled();click('Block account');await waitFor(()=>expect(api.patch).toHaveBeenCalledWith('/customers/c/status',{isActive:true,isBlocked:true}))
})
it('queues activation, removes licence and refreshes completed licence edits',async()=>{
 await view();fireEvent.click(screen.getByLabelText('Send a new activation code').querySelector('button')!);fireEvent.click(within(screen.getByRole('dialog')).getByRole('button',{name:'Send a new activation code'}));await waitFor(()=>expect(api.post).toHaveBeenCalledWith('/customers/c/online-access'));await waitFor(()=>expect(screen.queryByRole('dialog')).toBeNull())
 click('Edit customer');click('View licence');expect(openProtectedFile).toHaveBeenCalledWith('/customers/c/licence/image');click('Enter manually');click('Save training licence');await waitFor(()=>expect(screen.queryByText('Save training licence')).toBeNull());click('Upload & scan');click('Close licence editor');click('Remove driver licence');click('Remove licence');await waitFor(()=>expect(api.delete).toHaveBeenCalledWith('/customers/c/licence'))
})
it('shows booking, quote and invoice activity and applies status and reference searches',async()=>{
 await view();click('View customer activity');await screen.findByText('Customer activity · Training Customer');click('View licence');expect(openProtectedFile).toHaveBeenCalledWith('/customers/c/licence/image')
 fireEvent.click(screen.getByRole('tab',{name:'Bookings 11'}));expect(screen.getByText('BK-1')).toBeTruthy();fireEvent.change(screen.getByPlaceholderText('Search booking reference or branch'),{target:{value:'BK'}});fireEvent.keyDown(screen.getByPlaceholderText('Search booking reference or branch'),{key:'Enter'});await waitFor(()=>expect(api.get).toHaveBeenCalledWith('/customers/c/activity',{params:{page:1,pageSize:10,search:'BK'}}));click('Next');await waitFor(()=>expect(api.get).toHaveBeenCalledWith('/customers/c/activity',{params:{page:2,pageSize:10,search:'BK'}}));await screen.findByText('Page 2 of 2');click('Previous')
 fireEvent.click(await screen.findByRole('tab',{name:'Quotations 1'}));expect(screen.getByText('QUO-1')).toBeTruthy();fireEvent.click(screen.getByRole('tab',{name:'Invoice 1'}));expect(screen.getByText('INV-1')).toBeTruthy();expect(screen.getByText('Payment due')).toBeTruthy()
 fireEvent.mouseDown(screen.getAllByRole('combobox').at(-1)!);fireEvent.click(screen.getByRole('option',{name:'Issued'}));expect(screen.getByText('INV-1')).toBeTruthy()
})
it.each(['reset-password','revoke-sessions','lock','disable'])('performs the scoped customer security action %s',async path=>{
 await view();click('View customer activity');await screen.findByText('Customer activity · Training Customer');click({'reset-password':'Reset password','revoke-sessions':'Revoke sessions',lock:'Lock portal',disable:'Disable portal'}[path]!);await waitFor(()=>expect(api.post).toHaveBeenCalledWith(`/customers/c/security/${path}`))
})
it('reports activity access errors and keeps a security failure visible',async()=>{
 await view();vi.mocked(api.get).mockRejectedValueOnce({isAxiosError:true,response:{status:403}});click('View customer activity');await screen.findByText('You do not have access to this customer’s activity.');click('Close');await waitFor(()=>expect(screen.queryByRole('dialog')).toBeNull());click('View customer activity');await screen.findByText('Customer activity · Training Customer');vi.mocked(api.post).mockRejectedValue({isAxiosError:true,response:{data:{message:'Security update denied.'}}});click('Reset password');await screen.findByText('Security update denied.')
})
