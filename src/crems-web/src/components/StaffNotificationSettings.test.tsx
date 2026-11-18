// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import Settings from './StaffNotificationSettings'
import { api } from '../api/client'
vi.mock('../api/client', () => ({ api: { get: vi.fn(), put: vi.fn() } }))
const preferences = () => [{ category: 'Booking', inAppEnabled: true, toastEnabled: true, emailEnabled: false, emailFrequency: 'Immediate' }, { category: 'Finance', inAppEnabled: true, toastEnabled: false, emailEnabled: true, emailFrequency: 'Immediate' }]
beforeEach(() => { vi.resetAllMocks(); vi.mocked(api.get).mockResolvedValue({ data: preferences() }); vi.mocked(api.put).mockResolvedValue({}) })
afterEach(cleanup)
it('edits channel preferences and daily delivery without changing another category', async () => {
 const close = vi.fn(), saved = vi.fn()
 render(<Settings onClose={close} onSaved={saved} />)
 await screen.findByText('Rentals')
 fireEvent.click(screen.getAllByLabelText('Toast alerts')[0])
 fireEvent.click(screen.getAllByLabelText('In-app')[0])
 expect((screen.getAllByLabelText('Toast alerts')[0] as HTMLInputElement).disabled).toBe(true)
 fireEvent.click(screen.getAllByLabelText('Email')[0])
 fireEvent.mouseDown(screen.getAllByRole('combobox', { name: 'Email frequency' })[0])
 fireEvent.click(screen.getByRole('option', { name: 'Daily summary' }))
 fireEvent.click(screen.getByRole('button', { name: 'Save preferences' }))
 await waitFor(() => expect(saved).toHaveBeenCalledOnce())
 expect(close).toHaveBeenCalledOnce()
 expect(api.put).toHaveBeenCalledWith('/notifications/preferences', [{ ...preferences()[0], inAppEnabled: false, toastEnabled: false, emailEnabled: true, emailFrequency: 'Daily' }, preferences()[1]])
})
it('disables save after load failure and allows cancellation', async () => {
 vi.mocked(api.get).mockRejectedValue(new Error('Offline'))
 const close = vi.fn()
 render(<Settings onClose={close} onSaved={vi.fn()} />)
 await screen.findByText('Could not load your notification preferences.')
 expect((screen.getByRole('button', { name: 'Save preferences' }) as HTMLButtonElement).disabled).toBe(true)
 fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))
 expect(close).toHaveBeenCalledOnce()
})
it.each([
 [new Error('Offline'), 'Preferences could not be saved.'],
 [{ isAxiosError: true, response: { data: { message: 'Verify staff email first.' } } }, 'Verify staff email first.'],
 [{ isAxiosError: true }, 'Preferences could not be saved.'],
])('retains preferences after a rejected save: %s', async (failure, message) => {
 vi.mocked(api.put).mockRejectedValue(failure)
 const saved = vi.fn()
 render(<Settings onClose={vi.fn()} onSaved={saved} />)
 await screen.findByText('Rentals')
 fireEvent.click(screen.getByRole('button', { name: 'Save preferences' }))
 await screen.findByText(message)
 expect(saved).not.toHaveBeenCalled()
 expect((screen.getByRole('button', { name: 'Save preferences' }) as HTMLButtonElement).disabled).toBe(false)
})
it('prevents dismissal while a save is in progress', async () => {
 let finish!: (value: object) => void
 vi.mocked(api.put).mockImplementation(() => new Promise(resolve => { finish = resolve }))
 const close = vi.fn()
 render(<Settings onClose={close} onSaved={vi.fn()} />)
 await screen.findByText('Rentals')
 fireEvent.click(screen.getByRole('button', { name: 'Save preferences' }))
 expect((screen.getByRole('button', { name: 'Cancel' }) as HTMLButtonElement).disabled).toBe(true)
 fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Escape', code: 'Escape' })
 expect(close).not.toHaveBeenCalled()
 finish({})
 await waitFor(() => expect(close).toHaveBeenCalledOnce())
})
it('allows dismissal before saving', async () => {
 const close = vi.fn()
 render(<Settings onClose={close} onSaved={vi.fn()} />)
 await screen.findByText('Rentals')
 fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Escape', code: 'Escape' })
 expect(close).toHaveBeenCalledOnce()
})
