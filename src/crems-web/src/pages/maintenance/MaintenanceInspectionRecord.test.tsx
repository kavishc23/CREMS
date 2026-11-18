// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { MaintenanceInspectionRecord } from './MaintenanceInspectionRecord'
import { MaintenanceServicePlan } from './MaintenanceServicePlan'
import { api } from '../../api/client'
vi.mock('../../api/client', () => ({ api: { get: vi.fn() } }))
const record = { id: 'inspection-1', assetNumber: 'CAR-001', stage: 'Maintenance', outcome: 'PassedWithNotes', completedAt: '2026-11-16T00:00:00Z', completedByName: 'Training inspector', notes: 'Brakes checked', meterReading: 10000, meterUnit: 'km', fuelPercent: 70, responsesJson: JSON.stringify([{ label: 'Brakes', passed: true }, { name: 'Lights', result: false }, { text: 'Tyres', value: 'Good' }, { question: 'Damage', answer: 'None' }, { label: 'Fluid', status: 'Normal' }, { nested: [{ name: 'Horn', result: 'Working' }, null, 1] }, { name: 'Ignored', result: {} }]) }
beforeEach(() => { vi.resetAllMocks(); vi.mocked(api.get).mockResolvedValue({ data: record }) })
afterEach(cleanup)
it('renders nested checklist responses, readings, inspector and notes', async () => {
 const close = vi.fn(); render(<MaintenanceInspectionRecord id="inspection-1" onClose={close} />)
 expect(screen.getByLabelText('Loading inspection record')).toBeTruthy()
 await screen.findByText('Brakes: Passed')
 for (const text of ['Lights: Failed', 'Tyres: Good', 'Damage: None', 'Fluid: Normal', 'Horn: Working', 'Brakes checked']) expect(screen.getByText(text)).toBeTruthy()
 expect(screen.getByText(/Meter: 10000 km · Fuel: 70%/)).toBeTruthy()
 fireEvent.click(screen.getByRole('button', { name: 'Close inspection record' })); expect(close).toHaveBeenCalledOnce()
})
it.each(['broken-json', '{}', 'null', '[]'])('tolerates missing readings, notes and checklist format %s', async responsesJson => {
 vi.mocked(api.get).mockResolvedValue({ data: { ...record, notes: null, meterReading: null, fuelPercent: null, responsesJson } })
 render(<MaintenanceInspectionRecord id="inspection-1" onClose={vi.fn()} />)
 await screen.findByText('No named checklist responses recorded.')
 expect(screen.getByText('No notes recorded.')).toBeTruthy()
 expect(screen.getByText(/Meter: Not recorded km · Fuel: Not recorded/)).toBeTruthy()
})
it('retries an unavailable inspection and aborts the request on unmount', async () => {
 vi.mocked(api.get).mockRejectedValueOnce(new Error('Offline'))
 const view = render(<MaintenanceInspectionRecord id="inspection-1" onClose={vi.fn()} />)
 await screen.findByText('This inspection could not be loaded, or you no longer have access to it.')
 fireEvent.click(screen.getByRole('button', { name: 'Retry' }))
 await screen.findByText('Brakes: Passed')
 const signal = vi.mocked(api.get).mock.calls[1][1]?.signal
 view.unmount(); expect(signal?.aborted).toBe(true)
})
it.each([
 [null, null, null, false, false, 'Service targets not set', 'Not recorded', 'Not configured'],
 ['2027-01-01', 1200, 1000, false, false, 'Upcoming service', '1000', '200 km until the target'],
 ['2026-11-16', 1000, 1000, true, false, 'Service due now', '1000', 'Meter target reached'],
 ['2026-11-15', 900, 1000, true, true, 'Service overdue', '1000', '100 km beyond the target'],
])('explains service targets and the earliest due trigger %s/%s', (nextServiceDate, nextServiceMeter, currentMeterReading, isDue, isOverdue, status, current, detail) => {
 render(<MaintenanceServicePlan plan={{ id: 'a', assetNumber: 'CAR-001', name: 'Car', branchId: 'b', divisionId: 'd', branchName: 'Suva', status: 'Available', meterUnit: 'km', currentMeterReading, nextServiceDate, nextServiceMeter, activeJobId: null, isDue, isOverdue, meterInterval: isDue ? 500 : null, serviceIntervalMonths: isDue ? 6 : null, ruleWarning: isOverdue ? 'Review previous service' : null }} />)
 expect(screen.getByText(new RegExp(status))).toBeTruthy()
 expect(screen.getByText(new RegExp(`Current meter: ${current}`))).toBeTruthy()
 expect(screen.getAllByText(new RegExp(detail)).length).toBeGreaterThan(0)
 if (isOverdue) expect(screen.getByText('Review previous service')).toBeTruthy()
})
