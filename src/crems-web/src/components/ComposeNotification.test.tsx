// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import Compose from './ComposeNotification'
import { api } from '../api/client'
import { toast } from './ToastHost'
vi.mock('../api/client', () => ({ api: { get: vi.fn(), post: vi.fn() } }))
vi.mock('./ToastHost', () => ({ toast: vi.fn() }))
const templates = [{ id: 'email', name: 'Collection email', channel: 'Email', subject: 'Collect your vehicle', body: 'Please bring your licence.' }, { id: 'app', name: 'App reminder', channel: 'InApp', subject: 'Rental reminder', body: 'Review your booking.' }]
beforeEach(() => {
 vi.resetAllMocks()
 vi.mocked(api.get).mockImplementation(async url => ({ data: url.endsWith('/targets') ? { staff: [{ id: 'staff-1', fullName: 'Jane Staff' }], emailEnabled: false } : url.endsWith('/message-templates') ? templates : [{ id: 'customer-1', title: 'Training Customer', detail: 'CUST-001', kind: 'Customer account' }, { id: 'asset-1', title: 'Vehicle', detail: 'CAR-001', kind: 'Asset' }] }) as never)
 vi.mocked(api.post).mockResolvedValue({ data: { message: 'Message queued.' } })
})
afterEach(cleanup)
async function select(label: string, option: string) { fireEvent.mouseDown(await screen.findByRole('combobox', { name: label })); fireEvent.click(await screen.findByRole('option', { name: option })) }
function draft() { fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Rental update' } }); fireEvent.change(screen.getByLabelText('Message'), { target: { value: 'Your rental is ready.' } }) }
it('renders nothing when closed', () => { const { container } = render(<Compose open={false} onClose={vi.fn()} />); expect(container.textContent).toBe(''); expect(api.get).not.toHaveBeenCalled() })
it('searches customers, excludes asset hits, combines staff and sends the exact recipients', async () => {
 const close = vi.fn(); render(<Compose open onClose={close} />)
 fireEvent.focus(screen.getByRole('combobox', { name: 'Select customers' })); fireEvent.change(screen.getByRole('combobox', { name: 'Select customers' }), { target: { value: 'Training' } })
 fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Select customers' }))
 fireEvent.click(await screen.findByRole('option', { name: 'Training Customer · CUST-001' }))
 expect(screen.queryByRole('option', { name: 'Vehicle · CAR-001' })).toBeNull()
 await select('Select staff members', 'Jane Staff'); draft()
 fireEvent.click(screen.getByLabelText('Also send by email'))
 fireEvent.click(screen.getByRole('button', { name: 'Send to 2 recipients' }))
 await waitFor(() => expect(close).toHaveBeenCalledOnce())
 expect(api.post).toHaveBeenCalledWith('/notifications/send', expect.objectContaining({ requestId: expect.any(String), customerIds: ['customer-1'], allCustomers: false, recipientUserIds: ['staff-1'], title: 'Rental update', message: 'Your rental is ready.', sendEmail: false }))
 expect(toast).toHaveBeenCalledWith('Message queued.', 'success')
})
it('requires confirmation for all customers, supports templates and combined staff audiences', async () => {
 const close = vi.fn(); render(<Compose open onClose={close} />)
 await select('Use a saved message template', 'Collection email')
 expect((screen.getByLabelText('Title') as HTMLInputElement).value).toBe('Collect your vehicle')
 await screen.findByText('Email delivery is disabled on this server. Messages will still arrive in the notification centre.')
 fireEvent.click(screen.getByLabelText('All accessible customers'))
 expect((screen.getByRole('button', { name: 'Send to all customers' }) as HTMLButtonElement).disabled).toBe(true)
 await select('Select staff members', 'Jane Staff')
 fireEvent.click(screen.getByLabelText('I confirm this message should be sent to every accessible customer.'))
 fireEvent.click(screen.getByRole('button', { name: 'Send to all customers + 1 staff' }))
 await waitFor(() => expect(close).toHaveBeenCalledOnce())
 expect(api.post).toHaveBeenCalledWith('/notifications/send', expect.objectContaining({ customerIds: [], allCustomers: true, recipientUserIds: ['staff-1'], sendEmail: true }))
})
it('uses an in-app template and cancels without posting', async () => {
 const close = vi.fn(); render(<Compose open onClose={close} />)
 await select('Use a saved message template', 'App reminder')
 expect((screen.getByLabelText('Also send by email') as HTMLInputElement).checked).toBe(false)
 expect((screen.getByLabelText('Message') as HTMLInputElement).value).toBe('Review your booking.')
 await select('Use a saved message template', 'Write a new message')
 fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))
 expect(close).toHaveBeenCalledOnce(); expect(api.post).not.toHaveBeenCalled()
})
it.each([
 [new Error('Offline'), 'Notification could not be sent. Try again.'],
 [{ isAxiosError: true, response: { data: { message: 'No eligible recipients.' } } }, 'No eligible recipients.'],
 [{ isAxiosError: true, response: { data: { errors: { title: ['Invalid title.'] } } } }, 'Invalid title.'],
 [{ isAxiosError: true }, 'Notification could not be sent. Try again.'],
])('keeps a failed draft and request ID for safe retries: %s', async (failure, error) => {
 vi.mocked(api.post).mockRejectedValueOnce(failure)
 const close = vi.fn(); render(<Compose open onClose={close} />)
 await select('Select staff members', 'Jane Staff'); draft()
 fireEvent.click(screen.getByRole('button', { name: 'Send to 1 recipient' }))
 await screen.findByText(error)
 expect((screen.getByLabelText('Title') as HTMLInputElement).value).toBe('Rental update')
 expect(close).not.toHaveBeenCalled()
 fireEvent.click(screen.getByRole('button', { name: 'Send to 1 recipient' }))
 await waitFor(() => expect(close).toHaveBeenCalledOnce())
 expect(vi.mocked(api.post).mock.calls[0][1]).toEqual(vi.mocked(api.post).mock.calls[1][1])
})
it('handles unavailable recipients and templates without losing the message editor', async () => {
 vi.mocked(api.get).mockRejectedValue(new Error('Offline'))
 render(<Compose open onClose={vi.fn()} />)
 await screen.findByText('Could not load staff recipients. Reopen this window to try again.')
 expect(screen.queryByRole('combobox', { name: 'Use a saved message template' })).toBeNull()
 fireEvent.focus(screen.getByRole('combobox', { name: 'Select customers' })); fireEvent.change(screen.getByRole('combobox', { name: 'Select customers' }), { target: { value: 'Jane' } })
 await screen.findByText('Customer search could not be loaded.')
})
