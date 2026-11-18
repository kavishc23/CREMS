// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { MaintenancePage } from './MaintenancePage'
import { api } from '../api/client'
import type { Job, ScheduledAsset } from './maintenance/types'
vi.mock('../api/client', () => ({ api: { get: vi.fn() } }))
vi.mock('./maintenance/MaintenanceWorkspace', () => ({ MaintenanceWorkspace: ({ jobId, initialAsset, onClose, onSaved, onOpenJob }: { jobId: string | null; initialAsset: { id: string } | null; onClose: () => void; onSaved: (message: string, id?: string) => void; onOpenJob: (id: string) => void }) => <div role="dialog"><span>Workspace {jobId ?? initialAsset?.id ?? 'new'}</span><button onClick={onClose}>Close workspace</button><button onClick={() => onSaved('Job saved.', 'new-job')}>Save workspace</button><button onClick={() => onOpenJob('linked')}>Open linked job</button><button onClick={() => onSaved('Job updated.')}>Update workspace</button></div> }))
const job = (id: string, status: string, patch: Partial<Job> = {}): Job => ({ id, jobNumber: `JOB-${id}`, assetNumber: `EQ-${id}`, assetName: 'Training equipment', branchName: 'Suva', status, serviceType: 'Repair', priority: 'Normal', assignedTo: null, supplier: null, expectedReleaseAt: null, releasedAt: null, actualCost: null, hasEstimate: false, ...patch } as Job)
const asset = (id: string, patch: Partial<ScheduledAsset> = {}): ScheduledAsset => ({ id, assetNumber: `ASSET-${id}`, name: 'Training vehicle', branchId: 'b', divisionId: 'd', branchName: 'Suva', status: 'Available', meterUnit: 'km', currentMeterReading: 1000, nextServiceDate: '2026-11-15', nextServiceMeter: 1200, activeJobId: null, ...patch })
let jobs: Job[], assets: ScheduledAsset[], total: number
const options = { branches: [{ branchId: 'b', name: 'Suva' }], technicians: [], suppliers: [], technicianNames: ['Jane', ''] }
beforeEach(() => {
 vi.resetAllMocks(); sessionStorage.clear(); jobs = [job('1', 'Open')]; assets = [asset('1')]; total = 1
 vi.mocked(api.get).mockImplementation(async url => ({ data: url.endsWith('/options') ? options : url.endsWith('/schedule') ? { items: assets, total, today: '2026-11-16', dueCount: 1, overdueCount: 1 } : { items: jobs, total, counts: { active: 4, unassigned: 1, inProgress: 1, waiting: 1, awaitingRelease: 2, preventive: 1, overdueRepairs: 1 }, permissions: { canFinancial: true, canComplete: true, canInspect: true } } }) as never)
})
afterEach(cleanup)
const click = (name: string | RegExp) => fireEvent.click(screen.getByRole('button', { name }))
async function loaded() { await waitFor(() => expect(screen.queryByLabelText('Loading maintenance')).toBeNull()) }
async function select(label: string, option: string) { fireEvent.mouseDown(screen.getByRole('combobox', { name: label })); fireEvent.click(await screen.findByRole('option', { name: option })) }
it('opens a job, follows linked work, refreshes after save, and creates a new job', async () => {
 render(<MaintenancePage />); await loaded()
 click('Plan work JOB-1'); await screen.findByText('Workspace 1')
 click('Open linked job'); await screen.findByText('Workspace linked')
 click('Save workspace'); await screen.findByText('Workspace new-job')
 click('Update workspace'); click('Close workspace')
 click('Log maintenance job'); await screen.findByText('Workspace new')
 click('Close workspace'); expect(screen.queryByRole('dialog')).toBeNull()
})
it('renders job stages, costs, assignments and overdue repair states', async () => {
 jobs = [job('1', 'InProgress', { assignedTo: 'Jane', priority: 'Critical', actualCost: 250, expectedReleaseAt: '2020-01-01' }), job('2', 'WaitingForParts', { supplier: 'Parts supplier', priority: 'High', estimatedCost: 100, hasEstimate: true }), job('3', 'Completed'), job('4', 'Cancelled', { releasedAt: '2026-11-15' })]
 render(<MaintenancePage />); await loaded()
 expect(screen.getByRole('button', { name: 'Continue work JOB-1' })).toBeTruthy()
 expect(screen.getByRole('button', { name: 'Safety check JOB-3' })).toBeTruthy()
 expect(screen.getByRole('button', { name: 'View history JOB-4' })).toBeTruthy()
 expect(screen.getByText('Actual')).toBeTruthy(); expect(screen.getByText('Estimated')).toBeTruthy()
 expect(screen.getAllByText('Repair deadline missed')).toHaveLength(2)
 expect(screen.getAllByText('Parts supplier')).toHaveLength(2)
})
it.each([
 ['Overdue service', { queue: 'Preventive', overdueOnly: true }],
 ['Missed repair deadlines', { queue: 'History', overdueRepairs: true, sort: 'release', ascending: true }],
 ['Unassigned jobs', { queue: 'Active', unassigned: true }],
 ['In progress', { queue: 'Active', status: 'InProgress' }],
 ['Waiting for parts', { queue: 'Active', status: 'WaitingForParts' }],
 ['Jobs awaiting safety check', { queue: 'AwaitingRelease' }],
])('applies the %s summary filter', async (name, params) => {
 render(<MaintenancePage />); await loaded(); click(new RegExp(name))
 await waitFor(() => expect(api.get).toHaveBeenCalledWith('/maintenance-jobs/workspace', expect.objectContaining({ params: expect.objectContaining(params) })))
})
it('edits advanced filters, sorting, branch and search, then clears them', async () => {
 render(<MaintenancePage />); await loaded()
 fireEvent.change(screen.getByLabelText('Search maintenance'), { target: { value: 'EQ-1' } })
 await select('Branch', 'Suva'); click('Filters')
 await select('Priority', 'Critical'); await select('Status', 'In Progress'); await select('Sort by', 'Expected release')
 fireEvent.change(screen.getByRole('combobox', { name: 'Technician' }), { target: { value: 'Jane' } }); fireEvent.keyDown(screen.getByRole('combobox', { name: 'Technician' }), { key: 'ArrowDown' }); fireEvent.keyDown(screen.getByRole('combobox', { name: 'Technician' }), { key: 'Enter' })
 fireEvent.click(screen.getByLabelText('Unassigned only')); fireEvent.click(screen.getByLabelText('Missed repair deadlines only')); click('Descending')
 click('Done')
 await waitFor(() => expect(api.get).toHaveBeenCalledWith('/maintenance-jobs/workspace', expect.objectContaining({ params: expect.objectContaining({ search: 'EQ-1', branchId: 'b', status: 'InProgress', priority: 'Critical', unassigned: true, overdueRepairs: true, sort: 'release', ascending: true }) })))
 click('Clear filters'); await loaded()
 expect((screen.getByLabelText('Search maintenance') as HTMLInputElement).value).toBe('')
})
it('shows preventive rules and opens existing work or creates service with the selected asset', async () => {
 assets = [asset('1', { isOverdue: true, meterInterval: 500, serviceIntervalMonths: 6, ruleWarning: 'Check service history', meterTargetSource: 'Completed service' }), asset('2', { activeJobId: 'active', nextServiceDate: '2026-11-16', nextServiceMeter: null }), asset('3', { status: 'Rented', nextServiceDate: null, currentMeterReading: null })]
 render(<MaintenancePage />); await loaded(); click('Review due assets'); await loaded()
 expect(screen.getByText('Due today')).toBeTruthy(); expect(screen.getByText('Overdue')).toBeTruthy()
 fireEvent.click(screen.getAllByRole('button', { name: 'View service rules' })[0]); await screen.findByText('Check service history'); click('Close service rules'); await waitFor(() => expect(screen.queryByText('Preventive service rules')).toBeNull())
 fireEvent.click(screen.getAllByRole('button', { name: 'Log service' })[0]); await screen.findByText('Workspace 1'); click('Close workspace')
 fireEvent.click(screen.getAllByRole('button', { name: 'Open work' })[0]); await screen.findByText('Workspace active'); click('Close workspace')
 click(/Filters/); fireEvent.click(screen.getByLabelText('Overdue service only')); fireEvent.click(screen.getByLabelText('Due assets only')); click('Clear all'); click('Done')
 await loaded()
})
it('shows empty job and schedule views and persists filters', async () => {
 jobs = []; assets = []; total = 0
 render(<MaintenancePage />); await loaded()
 await screen.findByText('No jobs in this queue. Log maintenance work or choose another view.')
 fireEvent.change(screen.getByLabelText('Search maintenance'), { target: { value: 'missing' } }); await loaded()
 await screen.findByText('No jobs match these filters. Clear filters to see this queue.')
 fireEvent.click(screen.getByRole('tab', { name: 'Preventive schedule' })); await loaded()
 await screen.findByText(/No assets match this schedule/)
 expect(JSON.parse(sessionStorage.getItem('crems.maintenance.filters.v2')!).queue).toBe('Schedule')
})
it('recovers request failures without losing the saved search', async () => {
 sessionStorage.setItem('crems.maintenance.filters.v2', JSON.stringify({ search: 'saved-search' }))
 const normal = vi.mocked(api.get).getMockImplementation()!
 vi.mocked(api.get).mockRejectedValue(new Error('Offline'))
 render(<MaintenancePage />)
 await screen.findByText('Maintenance records could not be loaded. Retry without losing your filters.')
 expect((screen.getByLabelText('Search maintenance') as HTMLInputElement).value).toBe('saved-search')
 vi.mocked(api.get).mockImplementation(normal); click('Retry'); await loaded()
 await screen.findByRole('button', { name: 'Plan work JOB-1' })
})
it('tolerates corrupt stored filters and changes pagination', async () => {
 sessionStorage.setItem('crems.maintenance.filters.v2', '{broken'); total = 60
 render(<MaintenancePage />); await loaded()
 click('Go to next page')
 await waitFor(() => expect(api.get).toHaveBeenCalledWith('/maintenance-jobs/workspace', expect.objectContaining({ params: expect.objectContaining({ page: 2 }) })))
 await select('Rows per page:', '50')
 await waitFor(() => expect(api.get).toHaveBeenCalledWith('/maintenance-jobs/workspace', expect.objectContaining({ params: expect.objectContaining({ page: 1, pageSize: 50 }) })))
})
