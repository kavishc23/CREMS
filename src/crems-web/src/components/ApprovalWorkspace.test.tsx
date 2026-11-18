// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { ApprovalWorkspace } from './ApprovalWorkspace'
import { RetentionReview } from './RetentionReview'
import { api } from '../api/client'
vi.mock('../api/client', () => ({ api: { get: vi.fn(), post: vi.fn() } }))
const approval = (id = 'a', status = 'Pending', canDecide = true) => ({ id, requestNumber: `APR-${id}`, status, canDecide, type: 'Discount', entityType: 'Booking', amount: 100, reason: 'Training discount', createdAt: '2026-11-16', currentStage: 1, totalStages: 1, decisionNote: null, decidedAt: null, decidedByName: null, stageDecisions: [{ stageNumber: 1, stageName: 'Branch review', assignedRole: 'BranchManager', status, decisionNote: status === 'Pending' ? null : 'Reviewed', decidedAt: status === 'Pending' ? null : '2026-11-16', decidedByName: null }] })
beforeEach(() => { vi.resetAllMocks(); vi.mocked(api.post).mockResolvedValue({}) })
afterEach(cleanup)
it.each([true, false])('confirms an approval decision %s with an explicit trimmed note', async approved => {
 const reload = vi.fn().mockResolvedValue(undefined)
 render(<ApprovalWorkspace approvals={[approval()]} reload={reload} />)
 fireEvent.click(screen.getByRole('button', { name: approved ? 'Approve' : 'Reject' }))
 const dialog = screen.getByRole('dialog')
 fireEvent.change(within(dialog).getByLabelText(approved ? 'Approval note' : 'Rejection reason'), { target: { value: '  Checked policy and scope.  ' } })
 fireEvent.click(within(dialog).getByRole('button', { name: approved ? 'Approve' : 'Reject' }))
 await waitFor(() => expect(reload).toHaveBeenCalledOnce())
 expect(api.post).toHaveBeenCalledWith('/corporate-operations/approvals/a/decision', { approved, note: 'Checked policy and scope.' })
})
it.each([true, false])('uses a manager-review default for an empty note %s', async approved => {
 render(<ApprovalWorkspace approvals={[approval()]} reload={vi.fn().mockResolvedValue(undefined)} />)
 fireEvent.click(screen.getByRole('button', { name: approved ? 'Approve' : 'Reject' }))
 fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: approved ? 'Approve' : 'Reject' }))
 await waitFor(() => expect(api.post).toHaveBeenCalledWith('/corporate-operations/approvals/a/decision', { approved, note: approved ? 'Approved after manager review.' : 'Rejected after manager review.' }))
})
it.each([[new Error('Offline'), 'Only an authorised manager can decide this request.'], [{ isAxiosError: true, response: { data: { message: 'Request already decided.' } } }, 'Request already decided.'], [{ isAxiosError: true }, 'Only an authorised manager can decide this request.']])('retains a decision after failure %s', async (failure, error) => {
 vi.mocked(api.post).mockRejectedValue(failure)
 const reload = vi.fn(); render(<ApprovalWorkspace approvals={[approval()]} reload={reload} />)
 fireEvent.click(screen.getByRole('button', { name: 'Approve' })); fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Approve' }))
 await screen.findByText(error, {}, { timeout: 3000 }); expect(reload).not.toHaveBeenCalled()
 fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Cancel' }))
})
it('filters the queue, renders audit stages and hides decisions for unauthorized or completed records', () => {
 const done = { ...approval('b', 'Approved'), decidedAt: '2026-11-16', decidedByName: 'Manager' }
 const rejected = { ...approval('c', 'Rejected'), stageDecisions: [{ ...approval().stageDecisions[0], assignedRole: null, status: 'Rejected', decidedAt: '2026-11-16', decidedByName: 'Manager', decisionNote: 'Declined' }] }
 render(<ApprovalWorkspace approvals={[approval('a', 'Pending', false), done, rejected, { ...approval('d'), currentStage: 0, totalStages: 0, stageDecisions: [] }]} reload={vi.fn()} />)
 expect(screen.getAllByRole('button', { name: 'Approve' })).toHaveLength(1)
 expect(screen.getByText('No stage history has been recorded.')).toBeTruthy()
 expect(screen.getByText(/Final decision by Manager/)).toBeTruthy()
 fireEvent.click(screen.getByRole('tab', { name: 'Approved (1)' }))
 expect(screen.getByText('APR-b')).toBeTruthy(); expect(screen.queryByText('APR-a')).toBeNull()
 fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Show status' })); fireEvent.click(screen.getByRole('option', { name: 'Rejected' }))
 expect(screen.getByText('APR-c')).toBeTruthy()
})
it('explains empty queues for pending and other statuses', () => {
 render(<ApprovalWorkspace approvals={[]} reload={vi.fn()} />)
 expect(screen.getByText('No all approval requests to show.')).toBeTruthy()
 fireEvent.click(screen.getByRole('tab', { name: 'Pending (0)' }))
 expect(screen.getByText('No approval decisions are waiting for action.')).toBeTruthy()
})
it('pages through retention candidates and recovers an unavailable review', async () => {
 vi.mocked(api.get).mockRejectedValueOnce(new Error('Offline')).mockResolvedValue({ data: { years: 7, cutoff: '2019-01-01', pageSize: 1, bookingCount: 2, auditCount: 1, bookings: [{ id: 'b', reference: 'BK-OLD', status: 'Completed', lastActivity: '2018-01-01' }], audits: [{ id: 'a', action: 'Created', entityType: 'Booking', occurredAt: '2018-01-01' }] } })
 render(<RetentionReview />)
 await screen.findByText('Retention review could not be loaded.'); fireEvent.click(screen.getByRole('button', { name: 'Retry' }))
 await screen.findByText(/BK-OLD/)
 fireEvent.click(screen.getByRole('button', { name: 'Next' })); await screen.findByText('Page 2')
 await waitFor(() => expect(api.get).toHaveBeenCalledWith('/administration/retention-review', { params: { page: 2 } }))
 fireEvent.click(screen.getByRole('button', { name: 'Previous' })); await screen.findByText('Page 1')
})
it('shows no retention candidates and aborts state changes after unmount', async () => {
 vi.mocked(api.get).mockResolvedValue({ data: { years: 7, cutoff: '2019-01-01', pageSize: 20, bookingCount: 0, auditCount: 0, bookings: [], audits: [] } })
 render(<RetentionReview />); await screen.findByText('No records currently require review.')
 expect((screen.getByRole('button', { name: 'Next' }) as HTMLButtonElement).disabled).toBe(true)
})
