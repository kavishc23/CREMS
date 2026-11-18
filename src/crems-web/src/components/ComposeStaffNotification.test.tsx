// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import Compose from './ComposeStaffNotification'
import { api } from '../api/client'
import { toast } from './ToastHost'
vi.mock('../api/client', () => ({ api: { get: vi.fn(), post: vi.fn() } }))
vi.mock('./ToastHost', () => ({ toast: vi.fn() }))
const targets = { staff: [{ id: 'staff-a', fullName: 'Jane Officer', branchId: 'suva', divisionId: 'motors' }, { id: 'staff-b', fullName: 'John Officer', branchId: 'suva', divisionId: 'motors' }], branches: [{ id: 'suva', name: 'Suva' }], divisions: [{ id: 'motors', name: 'Motors' }], roles: ['RentalOfficer'], isAdministrator: true }
beforeEach(() => { vi.resetAllMocks(); vi.mocked(api.get).mockResolvedValue({ data: targets }); vi.mocked(api.post).mockResolvedValue({ data: { message: 'Notification queued.' } }) })
afterEach(cleanup)
async function select(label: string, option: string) {
 fireEvent.mouseDown(screen.getByRole('combobox', { name: label }))
 fireEvent.click(await screen.findByRole('option', { name: option }))
}
function message() {
 fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Collection reminder' } })
 fireEvent.change(screen.getByLabelText('Message'), { target: { value: 'Prepare the approved vehicle.' } })
}
it('sends to an administrator-scoped role, branch and division with expiry and priority', async () => {
 const close = vi.fn(); render(<Compose onClose={close} />)
 await select('Role', 'Rental Officer'); await select('Branch', 'Suva'); await select('Division', 'Motors')
 await select('Priority', 'Urgent'); message()
 fireEvent.change(screen.getByLabelText('Expiry (optional)'), { target: { value: '2026-12-01T10:00' } })
 fireEvent.click(screen.getByRole('button', { name: 'Send staff notification' }))
 await waitFor(() => expect(close).toHaveBeenCalledOnce())
 expect(api.post).toHaveBeenCalledWith('/notifications/staff', expect.objectContaining({ requestId: expect.any(String), recipientUserIds: [], requiredRole: 'RentalOfficer', branchId: 'suva', divisionId: 'motors', title: 'Collection reminder', message: 'Prepare the approved vehicle.', severity: 'Urgent', expiresAt: new Date('2026-12-01T10:00').toISOString() }))
 expect(toast).toHaveBeenCalledWith('Notification queued.', 'success')
})
it('selects specific staff, clears audience filters and disables their controls', async () => {
 const close = vi.fn(); render(<Compose onClose={close} />)
 await select('Branch', 'Suva')
 await select('Select staff members (optional)', 'Jane Officer')
 expect((screen.getByRole('combobox', { name: 'Branch' }) as HTMLInputElement).getAttribute('aria-disabled')).toBe('true')
 message()
 fireEvent.click(screen.getByRole('button', { name: 'Send to 1 staff member' }))
 await waitFor(() => expect(close).toHaveBeenCalledOnce())
 expect(api.post).toHaveBeenCalledWith('/notifications/staff', expect.objectContaining({ recipientUserIds: ['staff-a'], branchId: null, divisionId: null, requiredRole: null, expiresAt: null }))
})
it('requires both branch and division for a non-administrator scoped audience', async () => {
 vi.mocked(api.get).mockResolvedValue({ data: { ...targets, isAdministrator: false } })
 render(<Compose onClose={vi.fn()} />); message()
 await select('Branch', 'Suva')
 expect((screen.getByRole('button', { name: 'Send staff notification' }) as HTMLButtonElement).disabled).toBe(true)
 await select('Division', 'Motors')
 expect((screen.getByRole('button', { name: 'Send staff notification' }) as HTMLButtonElement).disabled).toBe(false)
})
it.each([
 [new Error('Offline'), 'Notification could not be sent.'],
 [{ isAxiosError: true, response: { data: { message: 'Recipient outside scope.' } } }, 'Recipient outside scope.'],
 [{ isAxiosError: true, response: { data: { errors: { title: ['Title is invalid.'], message: ['Message is invalid.'] } } } }, 'Title is invalid. Message is invalid.'],
 [{ isAxiosError: true }, 'Notification could not be sent.'],
])('retains a failed draft and the same idempotency key for retry: %s', async (failure, error) => {
 vi.mocked(api.post).mockRejectedValueOnce(failure)
 const close = vi.fn(); render(<Compose onClose={close} />)
 await select('Role', 'Rental Officer'); message()
 fireEvent.click(screen.getByRole('button', { name: 'Send staff notification' }))
 await screen.findByText(error)
 expect(close).not.toHaveBeenCalled()
 fireEvent.click(screen.getByRole('button', { name: 'Send staff notification' }))
 await waitFor(() => expect(close).toHaveBeenCalledOnce())
 expect(vi.mocked(api.post).mock.calls[0][1]).toEqual(vi.mocked(api.post).mock.calls[1][1])
})
it('disables sending when recipients cannot load and supports cancellation', async () => {
 vi.mocked(api.get).mockRejectedValue(new Error('Offline'))
 const close = vi.fn(); render(<Compose onClose={close} />)
 await screen.findByText('Could not load staff recipients.')
 expect((screen.getByRole('button', { name: 'Send staff notification' }) as HTMLButtonElement).disabled).toBe(true)
 fireEvent.click(screen.getByRole('button', { name: 'Cancel' })); expect(close).toHaveBeenCalledOnce()
})
