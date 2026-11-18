// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { AssetQrPage } from './AssetQrPage'
import { api } from '../api/client'
vi.mock('../api/client', () => ({ api: { post: vi.fn() } }))
vi.mock('../components/QrCameraScanner', () => ({ QrCameraScanner: ({ onScan, onScanningChange }: { onScan: (value: string) => void; onScanningChange: (value: boolean) => void }) => <><button onClick={() => onScanningChange(true)}>Start training camera</button><button onClick={() => { onScanningChange(false); onScan('QR-1') }}>Read training QR</button></> }))
vi.mock('../components/AssetProfileDialog', () => ({ AssetProfileDialog: ({ assetId, onClose }: { assetId: string | null; onClose: () => void }) => assetId ? <div role="dialog">Profile {assetId}<button onClick={onClose}>Close profile</button></div> : null }))
const context = (action = 'CheckOut') => ({ asset: { id: 'asset-1', assetNumber: 'CAR-001', name: 'Training car', status: 'Available', isActive: true, branchName: 'Suva', divisionName: 'Motors', registrationNumber: 'TRAIN1', serialNumber: null }, action, booking: { id: 'booking 1', bookingNumber: 'BK-001', customerName: 'Training Customer', startAt: '2026-11-16', endAt: '2026-11-18' }, availability: { availableNow: true, reason: 'Ready for collection', checkedAt: '2026-11-16', maintenance: [] as { jobNumber: string; serviceType: string; status: string }[], upcoming: [{ bookingNumber: 'BK-001', startAt: '2026-11-16', endAt: '2026-11-18' }, { bookingNumber: 'BK-002', startAt: '2026-11-20', endAt: '2026-11-22' }] }, message: null })
beforeEach(() => { vi.resetAllMocks(); window.history.replaceState({}, '', '/'); vi.mocked(api.post).mockResolvedValue({ data: context() }) })
afterEach(() => { cleanup(); window.history.replaceState({}, '', '/') })
function find() { fireEvent.change(screen.getByLabelText('Asset number or QR value'), { target: { value: ' CAR-001 ' } }); fireEvent.click(screen.getByRole('button', { name: 'Find asset' })) }
it.each([['CheckOut', 'Begin pickup', '&workflow=pickup'], ['CheckIn', 'Begin return', '&workflow=return'], ['ViewOnly', 'View booking', '']])('resolves %s to the correct booking workflow', async (action, label, suffix) => {
 vi.mocked(api.post).mockResolvedValue({ data: context(action) })
 render(<AssetQrPage />); find(); await screen.findByText('Training car')
 expect(api.post).toHaveBeenCalledWith('/asset-qr/resolve', { code: 'CAR-001' })
 expect(screen.getByRole('link', { name: label }).getAttribute('href')).toBe(`/staff/hire-operations?bookingId=booking%201${suffix}`)
 expect(screen.getByText('No maintenance hold')).toBeTruthy()
 expect(screen.getByText(/Registration TRAIN1/)).toBeTruthy()
 expect(screen.getByText(/BK-002 ·/)).toBeTruthy()
 expect(screen.getByRole('link', { name: 'Availability calendar' }).getAttribute('href')).toBe('/staff/availability?assetId=asset-1')
 fireEvent.click(screen.getByRole('button', { name: 'Full asset profile' })); await screen.findByText('Profile asset-1')
 fireEvent.click(screen.getByRole('button', { name: 'Close profile' }))
 fireEvent.click(screen.getByRole('button', { name: 'Check another asset' })); expect((screen.getByLabelText('Asset number or QR value') as HTMLInputElement).value).toBe('')
})
it('resolves a printed QR URL automatically and displays a maintenance hold without a booking', async () => {
 const data = context('ViewOnly'); data.booking = null as unknown as typeof data.booking
 data.asset.registrationNumber = null as unknown as string; data.asset.serialNumber = 'SERIAL-1' as unknown as null
 data.availability.availableNow = false; data.availability.reason = 'Safety release required'; data.availability.maintenance = [{ jobNumber: 'JOB-1', serviceType: 'Repair', status: 'WaitingForParts' }]
 vi.mocked(api.post).mockResolvedValue({ data }); window.history.replaceState({}, '', '/?code=printed-code')
 render(<AssetQrPage />)
 await screen.findByText('Maintenance hold recorded')
 expect(api.post).toHaveBeenCalledWith('/asset-qr/resolve', { code: 'printed-code' })
 expect(screen.getByText(/Serial SERIAL-1/)).toBeTruthy(); expect(screen.getByText(/Waiting For Parts/)).toBeTruthy()
 expect(screen.getByText('No active hire or pickup is linked to this asset.')).toBeTruthy()
})
it('uses camera results and suppresses the manual form during scanning', async () => {
 render(<AssetQrPage />)
 fireEvent.click(screen.getByRole('button', { name: 'Start training camera' }))
 expect(screen.queryByLabelText('Asset number or QR value')).toBeNull()
 fireEvent.click(screen.getByRole('button', { name: 'Read training QR' }))
 await screen.findByText('Training car')
 expect(api.post).toHaveBeenCalledWith('/asset-qr/resolve', { code: 'QR-1' })
})
it.each([
 [new Error('Offline'), 'The asset could not be loaded. Please try again.'],
 [{ isAxiosError: true, response: { status: 403 } }, 'This asset is outside your assigned operating scope.'],
 [{ isAxiosError: true, response: { status: 404, data: { message: 'Asset not found.' } } }, 'Asset not found.'],
 [{ isAxiosError: true }, 'The asset could not be loaded. Please try again.'],
])('shows resolution errors and allows retry: %s', async (failure, error) => {
 vi.mocked(api.post).mockRejectedValueOnce(failure)
 render(<AssetQrPage />); find(); await screen.findByText(error)
 fireEvent.click(screen.getByRole('button', { name: 'Close' })); expect(screen.queryByText(error)).toBeNull()
 fireEvent.click(screen.getByRole('button', { name: 'Find asset' })); await screen.findByText('Training car')
})
it('prevents empty lookup and duplicate requests while loading', async () => {
 let finish!: (data: unknown) => void
 vi.mocked(api.post).mockImplementation(() => new Promise(resolve => { finish = resolve }))
 render(<AssetQrPage />)
 expect((screen.getByRole('button', { name: 'Find asset' }) as HTMLButtonElement).disabled).toBe(true)
 find(); expect(api.post).toHaveBeenCalledOnce()
 fireEvent.submit(screen.getByLabelText('Asset number or QR value').closest('form')!)
 expect(api.post).toHaveBeenCalledOnce()
 finish({ data: context() }); await waitFor(() => expect(screen.getByText('Training car')).toBeTruthy())
})
