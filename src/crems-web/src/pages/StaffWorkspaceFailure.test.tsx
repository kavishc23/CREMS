// @vitest-environment jsdom
import React from 'react'
import { afterEach,beforeEach,describe,expect,it,vi } from 'vitest'
import { cleanup,render,screen,waitFor } from '@testing-library/react'
import { QueryClient,QueryClientProvider } from '@tanstack/react-query'
import { AssetsPage } from './AssetsPage'
import { CustomersPage } from './CustomersPage'
import { BranchesPage } from './BranchesPage'
import { DivisionsPage } from './DivisionsPage'
import { ReportsPage } from './ReportsPage'
import { FinancePage } from './FinancePage'
import { BookingsPage } from './BookingsPage'
import { RentalsPage } from './RentalsPage'
import { MaintenanceDirectoryPage } from './MaintenanceDirectoryPage'
import { StaffRolesPage } from './StaffRolesPage'
import { ChargeDefinitionsPage } from './ChargeDefinitionsPage'
import { DivisionBookingFlowPage } from './DivisionBookingFlowPage'
import { OperatingConfigurationPage } from './OperatingConfigurationPage'
import { PlanningCalendarPage } from './PlanningCalendarPage'
const m=vi.hoisted(()=>({get:vi.fn(),post:vi.fn(),put:vi.fn(),patch:vi.fn(),delete:vi.fn()}))
vi.mock('../api/client',()=>({api:m}));vi.mock('../auth/AuthContext',()=>({useAuth:()=>({user:{id:'u',fullName:'Test Administrator',roles:['SuperAdministrator'],permissions:[]}})}))
const clients:QueryClient[]=[];afterEach(()=>{cleanup();clients.splice(0).forEach(c=>c.clear());vi.restoreAllMocks()});beforeEach(()=>{vi.clearAllMocks();m.get.mockRejectedValue(new Error('Network unavailable'))})
const roles=['SuperAdministrator'];
const cases:[string,React.ReactNode,string][]=[
 ['asset register',<AssetsPage userRoles={roles}/>, '/assets'],['customer directory',<CustomersPage/>, '/customers'],['branches',<BranchesPage userRoles={roles}/>, '/branches'],['divisions',<DivisionsPage/>, '/divisions'],['reports',<ReportsPage/>, '/reporting/workspace'],['finance',<FinancePage/>, '/finance/workspace'],['bookings',<BookingsPage/>, '/bookings/work-queue'],['pickups and returns',<RentalsPage/>, '/rentals/work-queue'],['maintenance directory',<MaintenanceDirectoryPage/>, '/administration/maintenance-directory'],['staff roles',<StaffRolesPage onChanged={()=>{}}/>, '/staff-roles'],['charge configuration',<ChargeDefinitionsPage/>, '/charge-definitions'],['division booking policy',<DivisionBookingFlowPage/>, '/divisions'],['operating context',<OperatingConfigurationPage/>, '/organization/operating-context'],['planning calendar',<PlanningCalendarPage/>, '/planning/calendar']]
describe('staff workspace failures do not appear as successful empty data',()=>{
 it.each(cases)('%s displays recoverable API failure feedback',async(_name,node,endpoint)=>{const client=new QueryClient({defaultOptions:{queries:{retry:false},mutations:{retry:false}}});clients.push(client);render(<QueryClientProvider client={client}>{node}</QueryClientProvider>);await waitFor(()=>expect(m.get.mock.calls.some(call=>call[0]===endpoint)).toBe(true));await waitFor(()=>expect(screen.getAllByRole('alert').some(e=>/unable|could not|unavailable|failed|refresh/i.test(e.textContent??''))).toBe(true));expect(m.post).not.toHaveBeenCalled();expect(m.put).not.toHaveBeenCalled();expect(m.delete).not.toHaveBeenCalled()})
})
