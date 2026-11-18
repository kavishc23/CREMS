// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { DashboardPage } from './DashboardPage'
import { api } from '../api/client'
vi.mock('../api/client', () => ({ api: { get: vi.fn() } }))
const summary = { availableAssets: 9, activeRentals: 6, underMaintenance: 2, overdueRentals: 1, pendingRequests: 3, upcomingBookings: 4, servicesDueSoon: 5, vehicleUtilization: 60, equipmentUtilization: 30, totalAssets: 20 }
const operations = () => ({ currentRevenue: 1500, previousRevenue: 1000, currentCosts: 50, previousCosts: 100, updatedAt: '2026-11-16T00:00:00Z', fleet: ['Available', 'Rented', 'Reserved', 'Maintenance', 'Inspection', 'OutOfService'].map(status => ({ division: 'Motors', status, count: 2 })), alerts: [{ type: 'Overdue hire', detail: 'Contact customer', priority: 'High', page: 'rentals' }, { type: 'Service due', detail: 'Book workshop', priority: 'Normal', page: 'maintenance' }], activity: [{ id: '1', action: 'Collected', summary: 'Vehicle handed over', userName: 'Staff A', occurredAt: '2026-11-16', branchName: 'Suva' }, { id: '2', action: 'Returned', summary: 'Inspection complete', userName: 'Staff B', occurredAt: '2026-11-16', branchName: null }] })
const service = { id: 'hire', name: 'Vehicle hire', categories: [{ code: 'CAR', name: 'Passenger cars' }] }
const network = [{ branchId: 'a', divisionId: 'm', branchName: 'Suva', divisionName: 'Motors', services: [service] }, { branchId: 'b', divisionId: 'm', branchName: 'Nadi', divisionName: 'Motors', services: [service] }]
function setup(role = 'Administrator', data: unknown = operations(), rows: unknown = network, name = 'Jane Smith') {
  vi.mocked(api.get).mockImplementation(async url => ({ data: url === '/dashboard/bootstrap' ? { summary, operations: data } : rows }) as never)
  const navigate = vi.fn()
  render(<DashboardPage userName={name} userRoles={[role]} onNavigate={navigate} />)
  return navigate
}
beforeEach(() => vi.resetAllMocks())
afterEach(cleanup)
describe('Dashboard live summaries', () => {
  it.each([
    ['SuperAdministrator', 'System and group overview', 'Assets in scope', true],
    ['Administrator', 'Operations and access overview', 'Requests to review', true],
    ['BranchManager', 'Your branch at a glance', 'In maintenance', true],
    ['RentalOfficer', 'Today’s rental work', 'Upcoming collections', false],
    ['MaintenanceOfficer', 'Maintenance work today', 'Services due soon', false],
    ['UnknownRole', 'Today’s rental work', 'Requests to review', false],
  ])('loads the scoped summary for %s', async (role, title, metric, includeOperations) => {
    setup(role, null)
    await screen.findByText('Passenger cars')
    expect(screen.getByText(title)).toBeTruthy()
    expect(screen.getByText(metric)).toBeTruthy()
    expect(screen.getByText(/Welcome back, Jane/)).toBeTruthy()
    expect(api.get).toHaveBeenCalledWith('/dashboard/bootstrap', { params: { includeOperations } })
    expect(screen.queryByText('Operational performance')).toBeNull()
  })
  it('shows fleet, financial trends and actionable links', async () => {
    const navigate = setup()
    await screen.findByText('Operational performance')
    expect(screen.getByText('FJD 1,500')).toBeTruthy()
    expect(screen.getByText('▲ 50% versus last month')).toBeTruthy()
    expect(screen.getByText('▼ 50% versus last month')).toBeTruthy()
    expect(screen.getByText('30%')).toBeTruthy()
    expect(screen.getByTitle('Out of service: 2 (17%)')).toBeTruthy()
    expect(screen.getByText('Vehicle handed over')).toBeTruthy()
    expect(screen.getByText(/Staff A.*Suva/)).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Open management reports' }))
    fireEvent.click(screen.getByRole('button', { name: /Overdue hire/ }))
    fireEvent.click(screen.getByRole('button', { name: /Service due/ }))
    fireEvent.click(screen.getByRole('button', { name: 'Open asset register' }))
    expect(navigate.mock.calls).toEqual([['reports'], ['rentals'], ['maintenance'], ['assets']])
    expect(screen.getAllByText('2 locations', { selector: '.MuiChip-label' })).toHaveLength(2)
    expect(screen.getByText('1 service')).toBeTruthy()
    expect(screen.getAllByText('Passenger cars')).toHaveLength(1)
  })
  it('handles no previous period, an empty fleet and no overdue hires', async () => {
    const data = { ...operations(), previousRevenue: 0, previousCosts: 0, fleet: [], alerts: [], activity: [] }
    setup('Administrator', data, [], '')
    vi.mocked(api.get).mockResolvedValue({ data: { summary: { ...summary, totalAssets: 0, overdueRentals: 0 }, operations: data } } as never)
    // The initial request has already received the configured summary; reload by remounting.
    cleanup()
    render(<DashboardPage userName="" userRoles={['Administrator']} onNavigate={vi.fn()} />)
    await screen.findByText('No overdue returns')
    expect(screen.getByText(/Welcome back, there/)).toBeTruthy()
    expect(screen.getAllByText('No previous-period comparison')).toHaveLength(2)
    expect(screen.getByText('No urgent operational alerts.')).toBeTruthy()
    expect(screen.getAllByText('0%')).toHaveLength(2)
  })
  it('renders negative revenue and increased maintenance cost comparisons', async () => {
    setup('Administrator', { ...operations(), currentRevenue: 500, currentCosts: 200 }, [])
    await screen.findByText('▼ 50% versus last month')
    expect(screen.getByText('▲ 100% versus last month')).toBeTruthy()
  })
  it('retries a failed summary and independently recovers operating services on focus', async () => {
    let failed = true
    vi.mocked(api.get).mockImplementation(async url => {
      if (failed) throw new Error('Offline')
      return { data: url === '/dashboard/bootstrap' ? { summary, operations: null } : [network[0], { ...network[1], divisionId: 'e', divisionName: 'Equipment', services: [{ id: 'tools', name: 'Tool hire', categories: [] }] }] } as never
    })
    render(<DashboardPage userName="Jane" userRoles={['RentalOfficer']} onNavigate={vi.fn()} />)
    await screen.findByText('Live dashboard figures are temporarily unavailable.')
    expect(screen.getByText(/Operating services are temporarily unavailable/)).toBeTruthy()
    failed = false
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }))
    await waitFor(() => expect(screen.queryByText('Live dashboard figures are temporarily unavailable.')).toBeNull())
    await act(async () => { window.dispatchEvent(new Event('focus')) })
    await screen.findByText('Tool hire')
    expect(screen.getByText('2 services')).toBeTruthy()
    expect(screen.getAllByText('1 location')).toHaveLength(2)
    await act(async () => { window.dispatchEvent(new Event('crems:configuration-changed')) })
    expect(api.get).toHaveBeenCalledTimes(5)
  })
})
