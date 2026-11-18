// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { NotificationBell } from './NotificationBell'
import { api } from '../api/client'
import { toast } from './ToastHost'
vi.mock('../api/client', () => ({ api: { get: vi.fn(), post: vi.fn() } }))
vi.mock('./ToastHost', () => ({ toast: vi.fn() }))
vi.mock('./ComposeNotification', () => ({ default: ({ onClose }: { onClose: () => void }) => <button onClick={onClose}>Close message editor</button> }))
vi.mock('./ComposeStaffNotification', () => ({ default: ({ onClose }: { onClose: () => void }) => <button onClick={onClose}>Close staff editor</button> }))
vi.mock('./StaffNotificationSettings', () => ({ default: ({ onClose, onSaved }: { onClose: () => void; onSaved: () => void }) => <button onClick={() => { onSaved(); onClose() }}>Save notification settings</button> }))
const notice = (id: string, patch = {}) => ({ id, title: `Notice ${id}`, message: `Message ${id}`, url: null, createdAt: '2026-11-16', isRead: false, kind: 'Booking', severity: 'Info', actionLabel: null, actionType: null, isExpired: false, systemGenerated: true, toastEnabled: true, ...patch })
let items: ReturnType<typeof notice>[]
let total: number
beforeEach(() => {
 vi.resetAllMocks(); items = [notice('a')]; total = 1
 vi.mocked(api.get).mockImplementation(async (_url, config) => {
  // Hold long-poll requests until unmount; explicit refresh remains independently testable.
  const params = config?.params as { since?: string; page?: number } | undefined
  if (params?.since) return new Promise(() => {})
  return { data: { items, unreadCount: items.filter(n => !n.isRead).length, total, page: params?.page ?? 1, pageSize: 20 }, headers: { 'x-notification-version': 'version-1' } } as never
 })
 vi.mocked(api.post).mockResolvedValue({})
})
afterEach(cleanup)
async function open() {
 await screen.findByRole('button', { name: /Notifications, .* unread/ })
 await waitFor(() => expect(api.get).toHaveBeenCalled())
 fireEvent.click(screen.getByRole('button', { name: /Notifications,/ }))
 await screen.findByText('Notification centre')
}
it('loads unread messages, marks all read, and closes the centre', async () => {
 render(<NotificationBell />); await open()
 await screen.findByText('Message a')
 fireEvent.click(screen.getByRole('button', { name: 'Mark all as read' }))
 await waitFor(() => expect(api.post).toHaveBeenCalledWith('/notifications/read', { ids: [], all: true }))
 fireEvent.click(screen.getByRole('button', { name: 'Close' }))
 await waitFor(() => expect(screen.queryByText('Notification centre')).toBeNull())
})
it('opens permitted editors and saves preferences', async () => {
 render(<NotificationBell canSend />); await open()
 fireEvent.click(screen.getByRole('button', { name: 'Send a message' }))
 fireEvent.click(await screen.findByText('Close message editor'))
 fireEvent.click(screen.getByRole('button', { name: 'Notify staff' }))
 fireEvent.click(await screen.findByText('Close staff editor'))
 fireEvent.click(screen.getByRole('button', { name: 'Preferences' }))
 fireEvent.click(await screen.findByText('Save notification settings'))
 await waitFor(() => expect(vi.mocked(api.get).mock.calls.length).toBeGreaterThanOrEqual(3))
})
it('applies category, priority, history, unread and pagination filters', async () => {
 total = 45
 render(<NotificationBell />); await open()
 fireEvent.click(screen.getByRole('button', { name: 'Filters' }))
 fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Category' }))
 fireEvent.click(screen.getByRole('option', { name: 'Rentals' }))
 fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Priority' }))
 fireEvent.click(screen.getByRole('option', { name: 'Urgent' }))
 fireEvent.click(screen.getByLabelText('Unread only'))
 fireEvent.click(screen.getByLabelText('Include history'))
 fireEvent.click(screen.getByRole('button', { name: 'Go to page 2' }))
 await waitFor(() => expect(api.get).toHaveBeenCalledWith('/notifications', expect.objectContaining({ params: { category: 'Booking', severity: 'Urgent', unread: true, history: true, page: 2 } })))
 fireEvent.click(screen.getByRole('button', { name: /Hide filters/ }))
 expect(screen.queryByLabelText('Unread only')).toBeNull()
})
it('shows customer inbox without staff controls and supports an empty view', async () => {
 items = []; total = 0
 render(<NotificationBell customer />); await open()
 await screen.findByText('No notifications match this view.')
 expect(api.get).toHaveBeenCalledWith('/customer-account/notifications', expect.anything())
 expect(screen.queryByRole('button', { name: 'Preferences' })).toBeNull()
 expect((screen.getByRole('button', { name: 'Mark all as read' }) as HTMLButtonElement).disabled).toBe(true)
 fireEvent.click(screen.getByRole('button', { name: 'Filters' }))
 expect(screen.queryByRole('combobox', { name: 'Category' })).toBeNull()
})
it('reports loading and read errors and permits an explicit refresh', async () => {
 vi.mocked(api.get).mockRejectedValueOnce(new Error('Offline')).mockRejectedValueOnce(new Error('Offline'))
 render(<NotificationBell />); await open()
 await screen.findByText('Notifications unavailable. Please refresh.')
 fireEvent.click(screen.getByRole('button', { name: 'Refresh' }))
 await screen.findByText('Message a')
 vi.mocked(api.post).mockRejectedValue(new Error('Offline'))
 fireEvent.click(screen.getByRole('button', { name: 'Mark all as read' }))
 await screen.findByText('Could not mark notifications as read.')
})
it('marks a history entry read without following expired or unsafe URLs', async () => {
 items = [notice('expired', { isRead: true, isExpired: true, url: '/rentals', severity: 'Warning', systemGenerated: false }), notice('unsafe', { url: '//other.example', severity: 'Urgent' }), notice('slash', { url: '/bad\\path', actionLabel: 'Review' })]
 render(<NotificationBell />); await open()
 await screen.findByText('Expired / superseded · history only')
 expect(screen.getByText(/Staff message/)).toBeTruthy()
 expect(screen.getByText(/Open related record/)).toBeTruthy()
 fireEvent.click(screen.getByText('Message expired'))
 fireEvent.click(screen.getByText('Message unsafe'))
 fireEvent.click(screen.getByText('Message slash'))
 await waitFor(() => expect(api.post).toHaveBeenCalledTimes(3))
 expect(screen.getByText('Notification centre')).toBeTruthy()
})
it('announces fresh urgent notifications once and suppresses read, expired and muted notices', async () => {
 render(<NotificationBell />); await open()
 expect(toast).not.toHaveBeenCalled()
 items = [...items, notice('b', { severity: 'Urgent' }), notice('read', { isRead: true }), notice('expired', { isExpired: true }), notice('muted', { toastEnabled: false })]
 await act(async () => { window.dispatchEvent(new Event('crems:data-changed')) })
 expect(toast).toHaveBeenCalledWith('Message b', 'error', 'Notice b')
 await act(async () => { window.dispatchEvent(new Event('crems:data-changed')) })
 expect(toast).toHaveBeenCalledTimes(1)
 items = [...items, notice('c'), notice('d')]
 fireEvent.click(screen.getByRole('button', { name: 'Refresh' }))
 await waitFor(() => expect(toast).toHaveBeenCalledWith('Open the notification centre to read your latest messages.', 'info', '2 new notifications'))
})
