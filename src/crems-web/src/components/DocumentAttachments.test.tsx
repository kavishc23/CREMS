// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { BookingDocumentUploads } from './BookingDocumentUploads'
import { IncidentAttachments, incidentEvidence } from './IncidentAttachments'
import { api } from '../api/client'
vi.mock('../api/client',()=>({api:{post:vi.fn(),get:vi.fn()}}))
beforeEach(()=>{vi.resetAllMocks();vi.mocked(api.post).mockResolvedValue({data:{id:'doc',type:'ID',fileName:'id.pdf'}});vi.mocked(api.get).mockResolvedValue({data:new Blob(['document'])})});afterEach(()=>{cleanup();vi.restoreAllMocks()})
it.each([false,true])('uploads a required document using the correct staff/customer route %s',async customer=>{
 const uploaded=vi.fn(),busy=vi.fn();render(<BookingDocumentUploads customer={customer} assetId="a" bookingId="b" required={['ID']} documents={[]} onUploaded={uploaded} onBusyChange={busy}/>);fireEvent.change(screen.getByLabelText('Upload ID'),{target:{files:[new File(['pdf'],'id.pdf',{type:'application/pdf'})]}});await waitFor(()=>expect(uploaded).toHaveBeenCalledWith({id:'doc',type:'ID',fileName:'id.pdf'}));const call=vi.mocked(api.post).mock.calls[0];expect(call[0]).toBe(customer?'/customer-account/documents/upload':'/bookings/b/documents');expect((call[1] as FormData).get('type')).toBe('ID');expect((call[1] as FormData).get('assetId')).toBe(customer?'a':null);expect(busy.mock.calls).toEqual([[true],[false]])
})
it.each(['bad.exe','empty.pdf','large.pdf'])('rejects unsupported, empty and oversized documents %s',async name=>{
 render(<BookingDocumentUploads required={['ID']} documents={[]} onUploaded={vi.fn()}/>);const file=new File(name==='empty.pdf'?[]:['x'],name);if(name==='large.pdf')Object.defineProperty(file,'size',{value:6*1024*1024});fireEvent.change(screen.getByLabelText('Upload ID'),{target:{files:[file]}});await screen.findByText('Choose a PDF, JPEG or PNG up to 5 MB.');expect(api.post).not.toHaveBeenCalled()
})
it.each([false,true])('downloads a securely stored document using the permitted route %s',async customer=>{
 Object.defineProperty(URL,'createObjectURL',{configurable:true,value:vi.fn().mockReturnValue('blob:document')});Object.defineProperty(URL,'revokeObjectURL',{configurable:true,value:vi.fn()});const click=vi.spyOn(HTMLAnchorElement.prototype,'click').mockImplementation(()=>{});render(<BookingDocumentUploads customer={customer} bookingId="b" required={['ID']} documents={[{id:'doc',type:'id',fileName:'id.pdf',createdAt:'2026-11-16'}]} onUploaded={vi.fn()}/>);expect(screen.getByText('ID · Attached')).toBeTruthy();fireEvent.click(screen.getByRole('button',{name:'Download'}));await waitFor(()=>expect(click).toHaveBeenCalled());expect(api.get).toHaveBeenCalledWith(customer?'/customer-account/documents/doc/download':'/bookings/b/documents/doc/download',{responseType:'blob'})
})
it('shows upload and download errors without losing the document requirement',async()=>{
 vi.mocked(api.post).mockRejectedValue(Error('Offline'));vi.mocked(api.get).mockRejectedValue(Error('Offline'));render(<BookingDocumentUploads bookingId="b" required={['ID']} documents={[{id:'doc',type:'ID',fileName:'id.pdf'}]} onUploaded={vi.fn()}/>);fireEvent.change(screen.getByLabelText('Upload ID'),{target:{files:[new File(['x'],'id.png',{type:'image/png'})]}});await screen.findByText('The document could not be uploaded. Try again.');fireEvent.click(screen.getByRole('button',{name:'Download'}));await screen.findByText('This document could not be downloaded.')
})
it('reads incident files, provides safe download links and removes attachments',async()=>{
 const change=vi.fn(),busy=vi.fn();render(<IncidentAttachments value={['data:image/png;base64,AA==','data:application/pdf;base64,AA==','javascript:alert(1)']} onChange={change} onBusyChange={busy}/>);expect(screen.getAllByRole('link')).toHaveLength(2);fireEvent.click(screen.getAllByRole('button',{name:'Remove'})[0]);expect(change).toHaveBeenCalledWith(['data:application/pdf;base64,AA==','javascript:alert(1)']);fireEvent.change(document.querySelector('input[type="file"]')!,{target:{files:[new File(['image'],'evidence.png',{type:'image/png'})]}});await waitFor(()=>expect(change).toHaveBeenCalledWith(expect.arrayContaining([expect.stringContaining('data:image/png;base64,')])));expect(busy.mock.calls).toEqual([[true],[false]])
})
it.each(['too-many','bad-type','large'])('rejects invalid incident attachment batches %s',async kind=>{
 const files=kind==='too-many'?Array.from({length:6},()=>new File(['x'],'photo.png',{type:'image/png'})):[new File(['x'],'file',{type:kind==='bad-type'?'text/plain':'image/png'})];if(kind==='large')Object.defineProperty(files[0],'size',{value:3*1024*1024});const change=vi.fn();render(<IncidentAttachments value={[]} onChange={change}/>);fireEvent.change(document.querySelector('input[type="file"]')!,{target:{files}});await screen.findByText(kind==='too-many'?'Attach up to 5 files.':'Use JPG, PNG, WebP or PDF files up to 2 MB each.');expect(change).not.toHaveBeenCalled()
})
it('filters unsafe stored evidence and supports read-only attachments',()=>{
 expect(incidentEvidence('broken')).toEqual([]);expect(incidentEvidence('{}')).toEqual([]);expect(incidentEvidence('["data:image/webp;base64,AA==","javascript:alert(1)",1]')).toEqual(['data:image/webp;base64,AA==']);render(<IncidentAttachments value={['data:image/jpeg;base64,AA==','data:image/webp;base64,AA==']}/>);expect(screen.queryByRole('button',{name:'Remove'})).toBeNull();expect(screen.getAllByRole('link')).toHaveLength(2)
})
