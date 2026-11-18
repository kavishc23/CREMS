// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { AssetProfileDialog, MaintenanceHistory } from './AssetProfileDialog'
import { api } from '../api/client'
vi.mock('../api/client', () => ({ api: { get: vi.fn(), post: vi.fn() } }))
const profile = () => ({ asset: { id: 'a', assetNumber: 'CAR-1', name: 'Training vehicle', status: 'Available', type: 'PassengerVehicle', registrationNumber: 'TRAIN1', manufacturer: 'Training make', model: 'Model A', modelYear: 2026, currentMeterReading: 12000, meterUnit: 'km', acquisitionCost: 1000, currentBookValue: 800, dailyRate: 100, passingExpiry: '2020-01-01', insuranceExpiry: '2099-01-01', branch: { name: 'Suva' }, division: { name: 'Motors' }, attributeValues: [{ name: 'Seats', value: 5, confirmed: true, electric: false }] }, availability: { isAvailable: true, unavailableReason: null, expectedAvailableAt: null as string | null, bookings: [{ bookingNumber: 'BK-1', startAt: '2026-11-16T00:00:00Z' }], maintenance: [], transfers: [] }, alternatives: [{ assetNumber: 'CAR-2', name: 'Alternative vehicle' }], inspections: [{ id: 'i', stage: 'Maintenance', completedAt: '2026-11-16', completedByName: 'Inspector' }], rentalInspections: [], meters: [{ reading: 12000, unit: 'km', recordedAt: '2026-11-16T00:00:00Z' }], maintenance: [{ jobNumber: 'JOB-1', serviceType: 'Repair', status: 'Completed', actualCost: 100, estimatedCost: 90, hasEstimate: true, faultDescription: 'Leak', completionNotes: 'Repaired', assignedTo: 'Mechanic', reportedAt: '2026-11-01', completedAt: '2026-11-02' }], documents: [{ fileName: 'Insurance.pdf', type: 'Insurance' }], lifecycle: [{ id: 'l', type: 'Commissioning', occurredAt: '2026-11-01', recordedByName: 'Staff', notes: 'Commissioned', fromStatus: 'Inspection', toStatus: 'Available', evidenceJson: JSON.stringify({ photos: ['data:image/png;base64,AA==', 'https://unsafe.example/image'] }) }, { action: 'Updated', createdAt: '2026-11-02', evidenceJson: 'invalid' }], audits: [{ action: 'Asset updated', userName: 'Admin' }] })
const finance = { rentalRevenue: 200, maintenanceExpense: 100, operatingExpense: 150, transferExpense: 0, totalExpense: 250, operatingProfit: -50, lifetimeNetAfterAcquisition: -1050, rentalCount: 2, rentalDays: 4, inspectionCount: 2, ledger: [{ id: 'l', date: '2026-11-16', reference: 'BK-1', category: 'BaseHire', description: 'Hire charge', revenue: 200, expense: 0, quantity: 2, rate: 100 }], rentals: [], payments: [], maintenance: [] }
beforeEach(() => { vi.resetAllMocks(); vi.mocked(api.post).mockResolvedValue({}); vi.mocked(api.get).mockImplementation(async url => ({ data: url.endsWith('/profile') ? profile() : url.endsWith('/performance') ? finance : url.includes('/inspections/') ? { stage: 'Maintenance', notes: 'Detailed inspection', responsesJson: JSON.stringify([{ label: 'Brakes', passed: true }, { section: 'Lights', passed: false }, { result: 'Checked' }]), evidenceJson: '[]' } : { reference: 'CAR-1', items: [{ title: 'Asset created', detail: 'Created for training', actor: 'Admin', kind: 'Audit', occurredAt: '2026-11-16' }] } }) as never) })
afterEach(cleanup)
const tab = (name: string) => fireEvent.click(screen.getByRole('tab', { name }))
async function view() { const close = vi.fn(), edit = vi.fn(), preview = vi.fn(); const result = render(<AssetProfileDialog assetId="a" onClose={close} onEdit={edit} onPreview={preview} />); await screen.findByText('Training vehicle'); return { ...result, close, edit, preview } }
async function choose(label: string, option: string) { fireEvent.mouseDown(screen.getByRole('combobox', { name: label })); fireEvent.click(await screen.findByRole('option', { name: option })) }
it('shows identity, specification fields, usage period and header actions', async () => {
 const { edit, preview, close } = await view()
 expect(screen.getByText('Ready for hire')).toBeTruthy()
 fireEvent.click(screen.getByRole('button', { name: 'Edit asset' })); expect(edit).toHaveBeenCalledWith('a')
 fireEvent.click(screen.getByRole('button', { name: 'Customer preview' })); expect(preview).toHaveBeenCalledWith('a')
 tab('Specifications'); expect(screen.getByText('Seats')).toBeTruthy(); expect(screen.getByText('Yes')).toBeTruthy(); expect(screen.getByText('No')).toBeTruthy()
 await choose('Usage period', 'Last 30 days'); await screen.findByText('Vehicle and equipment specifications')
 expect(api.get).toHaveBeenCalledWith('/assets/a/profile', { params: { usageDays: 30 } })
 fireEvent.click(screen.getByRole('button', { name: 'Back to asset register' })); expect(close).toHaveBeenCalledOnce()
})
it('shows operational availability, meter history, maintenance cost and documents', async () => {
 await view(); tab('Availability'); expect(screen.getByText('BK-1')).toBeTruthy(); expect(screen.getByText('Alternative vehicle')).toBeTruthy()
 tab('Meters'); expect(screen.getByText('12000')).toBeTruthy()
 tab('Maintenance'); expect(screen.getByText('Actual maintenance cost')).toBeTruthy(); expect(screen.getByText('Estimate: FJD 90.00')).toBeTruthy()
 tab('Documents'); expect(screen.getByText('Insurance.pdf')).toBeTruthy()
 tab('Lifecycle'); expect(screen.getByText('Commissioned')).toBeTruthy(); expect(screen.getAllByAltText(/Saved inspection photo/)).toHaveLength(1)
 tab('Audit'); expect(screen.getByText('Asset updated')).toBeTruthy()
 fireEvent.click(screen.getByRole('button', { name: 'Open complete timeline' })); await screen.findByText('Created for training'); fireEvent.click(within(screen.getAllByRole('dialog').at(-1)!).getByRole('button', { name: 'Close' }))
})
it('loads financials lazily and shows loss, asset revenue and break-even calculations', async () => {
 await view(); expect(api.get).not.toHaveBeenCalledWith('/assets/a/performance')
 tab('Financials'); await screen.findByText('This asset is currently operating at a loss and should be reviewed.')
 expect(screen.getByText('-25.0%')).toBeTruthy(); expect(screen.getByText('0.0% recovered')).toBeTruthy()
 tab('Overview'); expect(screen.getByText('Operating profit from 2 recorded rentals')).toBeTruthy()
 tab('Financials'); expect(vi.mocked(api.get).mock.calls.filter(([url]) => url === '/assets/a/performance')).toHaveLength(1)
})
it('loads, searches and filters inspection details with retry', async () => {
 await view(); tab('Inspections')
 vi.mocked(api.get).mockRejectedValueOnce(new Error('Offline'))
 fireEvent.click(screen.getByRole('button', { name: /Asset inspection · Maintenance/ }))
 await screen.findByText('Inspection details could not be loaded.'); fireEvent.click(screen.getByRole('button', { name: 'Retry' }))
 await screen.findByText('Detailed inspection'); expect(screen.getByText('Brakes')).toBeTruthy(); expect(screen.getByText('Passed')).toBeTruthy(); expect(screen.getByText('Failed')).toBeTruthy()
 fireEvent.click(screen.getByRole('button', { name: /Asset inspection · Maintenance/ })); fireEvent.click(screen.getByRole('button', { name: /Asset inspection · Maintenance/ }))
 await screen.findByText('Detailed inspection')
 fireEvent.change(screen.getByLabelText('Search booking or inspecting staff'), { target: { value: 'missing' } }); expect(screen.getByText('No inspections match your search.')).toBeTruthy()
 fireEvent.change(screen.getByLabelText('Search booking or inspecting staff'), { target: { value: '' } }); await choose('Inspection type', 'Maintenance')
 expect(screen.getByText('1 of 1 inspections')).toBeTruthy()
})
it('records meter type, reading, unit and fuel and refreshes the profile', async () => {
 await view(); fireEvent.click(screen.getByRole('button', { name: 'Record meter' })); await choose('Meter type', 'Engine Hours')
 fireEvent.change(screen.getByLabelText('Reading'), { target: { value: '12500' } }); fireEvent.change(screen.getByLabelText('Unit'), { target: { value: 'hours' } }); fireEvent.change(screen.getByLabelText('Fuel percentage'), { target: { value: '80' } })
 fireEvent.click(screen.getByRole('button', { name: 'Save reading' })); await waitFor(() => expect(api.post).toHaveBeenCalledWith('/assets/a/meters', { type: 'EngineHours', unit: 'hours', reading: 12500, fuelPercent: 80, source: 'Manual', bookingId: null }))
})
it.each([[new Error('Offline'), 'The reading was not saved.'], [{ isAxiosError: true }, 'The reading was not saved. Check the values and try again.'], [{ isAxiosError: true, response: { data: { message: 'Odometer cannot decrease.' } } }, 'Odometer cannot decrease.']])('retains a failed meter reading %s', async (failure, error) => {
 await view(); vi.mocked(api.post).mockRejectedValue(failure); fireEvent.click(screen.getByRole('button', { name: 'Record meter' })); fireEvent.change(screen.getByLabelText('Reading'), { target: { value: '1' } }); fireEvent.click(screen.getByRole('button', { name: 'Save reading' })); await screen.findAllByText(error); expect((screen.getByLabelText('Reading') as HTMLInputElement).value).toBe('1')
 fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))
})
it('records a maintenance inspection and preserves entered notes after failure', async () => {
 await view(); fireEvent.click(screen.getByRole('button', { name: 'Complete inspection' })); await choose('Inspection stage', 'Maintenance'); await choose('Outcome', 'Passed With Notes')
 fireEvent.change(screen.getByLabelText('Meter reading'), { target: { value: '12500' } }); fireEvent.change(screen.getByLabelText('Fuel percentage'), { target: { value: '50' } }); fireEvent.change(screen.getByLabelText('Condition, defects and damage notes'), { target: { value: 'Safe with minor scratches' } }); fireEvent.change(screen.getByLabelText('Inspecting staff member'), { target: { value: 'Inspector' } })
 vi.mocked(api.post).mockRejectedValueOnce(new Error('Offline'))
 const dialog = screen.getAllByRole('dialog').at(-1)!
 fireEvent.click(within(dialog).getByRole('button', { name: 'Complete inspection' })); await screen.findAllByText('The inspection was not saved. Pre- and post-hire inspections must be completed from the related rental.')
 fireEvent.click(within(dialog).getByRole('button', { name: 'Complete inspection' }))
 await waitFor(() => expect(api.post).toHaveBeenLastCalledWith('/assets/a/inspections', expect.objectContaining({ stage: 'Maintenance', outcome: 'PassedWithNotes', meterReading: 12500, fuelPercent: 50, staffSignatureName: 'Inspector', notes: 'Safe with minor scratches', responsesJson: '[]', evidenceJson: '{"photos":[]}' }), { timeout: 60000 }))
})
it('retries the profile and shows lack of financial access after a failed financial request', async () => {
 vi.mocked(api.get).mockRejectedValueOnce(new Error('Offline'))
 render(<AssetProfileDialog assetId="a" onClose={vi.fn()} />); await screen.findByText('Unable to load this asset profile.'); fireEvent.click(screen.getByRole('button', { name: 'Try again' })); await screen.findByText('Training vehicle')
 vi.mocked(api.get).mockRejectedValueOnce(new Error('Forbidden')); tab('Financials'); await screen.findByText('You do not have permission to view asset financial information.')
})
it('shows empty tab states and fallback identity for an incomplete asset', async () => {
 const data = profile(); data.asset = { id: 'a', assetNumber: 'EMPTY', name: 'Empty asset', status: 'Maintenance', attributeValues: [] } as unknown as typeof data.asset
 data.availability = { isAvailable: false, unavailableReason: null, expectedAvailableAt: '2027-01-01', bookings: [], maintenance: [], transfers: [] }; data.alternatives = []; data.inspections = []; data.meters = []; data.maintenance = []; data.documents = []; data.lifecycle = []; data.audits = []
 vi.mocked(api.get).mockResolvedValue({ data }); render(<AssetProfileDialog assetId="a" onClose={vi.fn()} />); await screen.findByText('Empty asset')
 expect(screen.getByText('Asset identification details are incomplete')).toBeTruthy()
 for (const [name, message] of [['Specifications','Assign a configured category and enter its specifications to make this profile complete.'], ['Availability','No suitable alternatives are currently available.'], ['Inspections','No inspections are available for this asset.'], ['Meters','No meter readings have been recorded.'], ['Maintenance','No maintenance jobs have been recorded.'], ['Documents','No documents are attached to this asset.'], ['Lifecycle','No lifecycle events have been recorded.'], ['Audit','No audit events are available.']]) { tab(name); expect(screen.getByText(message)).toBeTruthy() }
})
it('distinguishes maintenance estimates, missing financial permission and recorded zero costs', () => {
 render(<MaintenanceHistory rows={[{ jobNumber: 'EST', estimatedCost: 20 }, { jobNumber: 'NO-EST', estimatedCost: 0, hasEstimate: false }, { jobNumber: 'NO-FIN', supplier: 'Workshop' }, { jobNumber: 'ZERO', actualCost: 0, estimatedCost: 0, hasEstimate: true, description: 'Repaired free' }]} />)
 expect(screen.getByText('Estimated maintenance cost')).toBeTruthy(); expect(screen.getByText('Work performed: Repaired free')).toBeTruthy(); expect(screen.getByText('Maintenance cost')).toBeTruthy()
})
