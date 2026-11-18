// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { MaintenancePartsDialog } from './MaintenancePartsDialog'
import { api } from '../api/client'
vi.mock('../api/client',()=>({api:{get:vi.fn(),post:vi.fn()}}))
const data=()=>({stock:[{id:'p',partNumber:'PART-1',name:'Seal kit',available:10,unitCost:20}],ledger:[{id:'i',inventoryPartId:'p',partNumber:'PART-1',quantity:3,unitCost:20,batchKey:'batch',createdAt:'2026-11-16'},{id:'r',inventoryPartId:'p',partNumber:'PART-1',quantity:-1,unitCost:20,batchKey:'batch',createdAt:'2026-11-16'}],canManage:true,canIssue:true,canFinancial:true})
beforeEach(()=>{vi.resetAllMocks();vi.mocked(api.get).mockResolvedValue({data:data()});vi.mocked(api.post).mockResolvedValue({})});afterEach(cleanup)
async function select(label:string,name:RegExp){fireEvent.mouseDown(await screen.findByRole('combobox',{name:label}));fireEvent.click(await screen.findByRole('option',{name}))}
it('issues stock, returns unused batch quantities and refreshes parent costs',async()=>{
 const changed=vi.fn().mockResolvedValue(undefined),close=vi.fn();render(<MaintenancePartsDialog job={{id:'j',jobNumber:'JOB-1'}} onClose={close} onChanged={changed}/>);await select('Issue stock',/PART-1 · Seal kit/)
 fireEvent.change(screen.getByLabelText('Quantity'),{target:{value:'2'}});fireEvent.click(screen.getByRole('button',{name:'Issue parts'}));await waitFor(()=>expect(changed).toHaveBeenCalledOnce());expect(api.post).toHaveBeenCalledWith('/business-operations/maintenance/j/parts',{inventoryPartId:'p',quantity:2})
 await select('Return unused stock',/PART-1 · 2 outstanding/);fireEvent.change(screen.getByLabelText('Return reason'),{target:{value:'Unused kit'}});fireEvent.click(screen.getByRole('button',{name:'Return parts to stock'}));await waitFor(()=>expect(changed).toHaveBeenCalledTimes(2));expect(api.post).toHaveBeenCalledWith('/maintenance-jobs/j/parts/i/return',{quantity:1,reason:'Unused kit'});fireEvent.click(screen.getByRole('button',{name:'Close'}));expect(close).toHaveBeenCalledOnce()
})
it.each([[new Error('Offline'),'Unable to record stock movement. Refresh before retrying.'],[{isAxiosError:true,response:{data:{message:'Insufficient stock.'}}},'Insufficient stock.'],[{isAxiosError:true,response:{data:{errors:{quantity:['Invalid quantity.']}}}},'Invalid quantity.']])('preserves stock selections after failure %s',async(error,message)=>{
 vi.mocked(api.post).mockRejectedValue(error);render(<MaintenancePartsDialog job={{id:'j',jobNumber:'JOB-1'}} onClose={vi.fn()} onChanged={vi.fn()}/>);await select('Issue stock',/PART-1 · Seal kit/);fireEvent.click(screen.getByRole('button',{name:'Issue parts'}));await screen.findByText(message)
})
it('rejects fractional quantities and hides financial fields without permission',async()=>{
 vi.mocked(api.get).mockResolvedValue({data:{...data(),canFinancial:false}});render(<MaintenancePartsDialog job={{id:'j',jobNumber:'JOB-1'}} onClose={vi.fn()} onChanged={vi.fn()}/>);await select('Issue stock',/PART-1 · Seal kit/);fireEvent.change(screen.getByLabelText('Quantity'),{target:{value:'1.5'}});expect((screen.getByRole('button',{name:'Issue parts'}) as HTMLButtonElement).disabled).toBe(true);expect(screen.queryByText(/at FJD/)).toBeNull()
})
it('reports inventory loading failures and supports a read-only empty ledger',async()=>{
 vi.mocked(api.get).mockRejectedValueOnce(Error('Offline'));render(<MaintenancePartsDialog job={{id:'j',jobNumber:'JOB-1'}} onClose={vi.fn()} onChanged={vi.fn()}/>);await screen.findByText('Unable to load stock and issue history.');cleanup();vi.mocked(api.get).mockResolvedValue({data:{...data(),canManage:false,ledger:[]}});render(<MaintenancePartsDialog job={{id:'j',jobNumber:'JOB-1'}} onClose={vi.fn()} onChanged={vi.fn()}/>);await screen.findByText('No stock movements recorded.');expect(screen.queryByLabelText('Quantity')).toBeNull()
})
