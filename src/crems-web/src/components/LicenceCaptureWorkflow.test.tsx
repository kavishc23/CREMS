// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { LicenceWorkflowDialog } from './LicenceWorkflowDialog'
import { api } from '../api/client'
vi.mock('../api/client',()=>({api:{post:vi.fn()}}));vi.mock('./ToastHost',()=>({toast:vi.fn()}))
const scan={submissionId:'scan',fullName:'Training Customer',licenceNumber:'DL-1',classes:[2,3],nameMatches:true}
const licence={status:'Verified',licenceNumber:'DL-1',classes:[2,3],hasImage:true}
beforeEach(()=>{vi.resetAllMocks();vi.mocked(api.post).mockImplementation(async url=>({data:url.endsWith('/scan')?scan:licence}) as never)});afterEach(cleanup)
function chooseFile(){fireEvent.change(document.querySelector<HTMLInputElement>('input[type="file"]')!,{target:{files:[new File(['licence'],'licence.png',{type:'image/png'})]}})}
it.each([undefined,'c'])('scans and confirms a licence for customer endpoint %s',async customerId=>{
 const saved=vi.fn(),close=vi.fn();render(<LicenceWorkflowDialog open customerId={customerId} onSaved={saved} onClose={close}/>);chooseFile();await screen.findByText('✓ Matches account');expect((screen.getByLabelText('Full licence classes') as HTMLInputElement).value).toBe('2, 3');fireEvent.click(screen.getByRole('button',{name:'Confirm and save licence'}));await waitFor(()=>expect(saved).toHaveBeenCalledWith(licence));expect(api.post).toHaveBeenLastCalledWith(`${customerId?'/customers/c/licence':'/customer-account/licence'}/scan/confirm`,{licenceNumber:'DL-1',classes:[2,3]});expect(close).toHaveBeenCalledOnce()
})
it.each([[false,[2],'The licence details could not be matched. Check the document and try another scan.'],[true,[],'The licence class was not detected. Try another scan or ask staff to enter the class from the document.']])('blocks confirmation after unmatched or incomplete scans %s',async(nameMatches,classes,message)=>{
 vi.mocked(api.post).mockResolvedValue({data:{...scan,nameMatches,classes}});render(<LicenceWorkflowDialog open onSaved={vi.fn()} onClose={vi.fn()}/>);chooseFile();await screen.findByText(message);expect((screen.getByRole('button',{name:'Confirm and save licence'}) as HTMLButtonElement).disabled).toBe(true);fireEvent.click(screen.getByRole('button',{name:'Reupload licence'}));expect(screen.getByText('Before you upload')).toBeTruthy()
})
it('allows staff to correct a scan manually and saves the file with sorted licence classes',async()=>{
 const saved=vi.fn();render(<LicenceWorkflowDialog open customerId="c" onSaved={saved} onClose={vi.fn()}/>);chooseFile();await screen.findByText('✓ Matches account');fireEvent.click(screen.getByRole('button',{name:'Enter details manually'}));fireEvent.change(screen.getByLabelText(/Licence number/),{target:{value:'DL-CORRECTED'}});const select=screen.getByRole('combobox');fireEvent.mouseDown(select);fireEvent.click(screen.getByRole('option',{name:'1'}));fireEvent.keyDown(screen.getByRole('listbox'),{key:'Escape'});fireEvent.click(screen.getByRole('button',{name:'Save licence details'}));await waitFor(()=>expect(saved).toHaveBeenCalled());const call=vi.mocked(api.post).mock.calls.at(-1)!;expect(call[0]).toBe('/customers/c/licence/manual');expect((call[1] as FormData).get('classes')).toBe('1,2,3');expect((call[1] as FormData).get('licenceNumber')).toBe('DL-CORRECTED')
})
it('requires an uploaded image, number and at least one class in manual staff entry',async()=>{
 render(<LicenceWorkflowDialog open manual customerId="c" onSaved={vi.fn()} onClose={vi.fn()}/>);expect((screen.getByRole('button',{name:'Save licence details'}) as HTMLButtonElement).disabled).toBe(true);chooseFile();fireEvent.change(screen.getByLabelText(/Licence number/),{target:{value:'DL-MANUAL'}});fireEvent.mouseDown(screen.getByRole('combobox'));fireEvent.click(screen.getByRole('option',{name:'2'}));fireEvent.keyDown(screen.getByRole('listbox'),{key:'Escape'});fireEvent.click(screen.getByRole('button',{name:'Save licence details'}));await waitFor(()=>expect(api.post).toHaveBeenCalledWith('/customers/c/licence/manual',expect.any(FormData)))
})
it.each([[new Error('Offline'),'The licence could not be saved.'],[{isAxiosError:true,code:'ECONNABORTED'},'The licence scan took too long. Please try again.'],[{isAxiosError:true,response:{data:{detail:'Unreadable file.'}}},'Unreadable file.'],[{isAxiosError:true,response:{data:{message:'Licence expired.'}}},'Licence expired.']])('keeps failed scans available for staff manual entry %s',async(error,message)=>{
 vi.mocked(api.post).mockRejectedValue(error);render(<LicenceWorkflowDialog open customerId="c" onSaved={vi.fn()} onClose={vi.fn()}/>);chooseFile();await screen.findByText(message);fireEvent.click(screen.getByRole('button',{name:'Enter manually'}));expect(screen.getByLabelText(/Licence number/)).toBeTruthy()
})
it('retains a failed confirmation and permits closing without saving',async()=>{
 const close=vi.fn();render(<LicenceWorkflowDialog open onSaved={vi.fn()} onClose={close}/>);chooseFile();await screen.findByText('✓ Matches account');vi.mocked(api.post).mockRejectedValue(Error('Offline'));fireEvent.click(screen.getByRole('button',{name:'Confirm and save licence'}));await screen.findByText('The licence could not be saved.');fireEvent.click(screen.getByRole('button',{name:'Close'}));expect(close).toHaveBeenCalledOnce()
})
