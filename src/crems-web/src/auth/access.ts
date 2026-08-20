export const roles = {
  superAdministrator: 'SuperAdministrator',
  administrator: 'Administrator',
  branchManager: 'BranchManager',
  rentalOfficer: 'RentalOfficer',
  maintenanceOfficer: 'MaintenanceOfficer',
  financeOfficer: 'FinanceOfficer',
  driver: 'Driver',
} as const

export type AppPage =
  | 'dashboard'
  | 'assets'
  | 'customers'
  | 'bookings'
  | 'rentals'
  | 'scan'
  | 'maintenance'
  | 'operations'
  | 'reports'
  | 'users'
  | 'branches'
  | 'divisions'
  | 'configuration'

const pageRoles: Record<AppPage, readonly string[]> = {
  dashboard: Object.values(roles),
  assets: [roles.superAdministrator, roles.administrator, roles.branchManager, roles.rentalOfficer, roles.maintenanceOfficer, roles.driver],
  customers: [roles.superAdministrator, roles.administrator, roles.branchManager, roles.rentalOfficer],
  bookings: [roles.superAdministrator, roles.administrator, roles.branchManager, roles.rentalOfficer],
  rentals: [roles.superAdministrator, roles.administrator, roles.branchManager, roles.rentalOfficer],
  scan: [roles.superAdministrator, roles.administrator, roles.branchManager, roles.rentalOfficer, roles.maintenanceOfficer, roles.driver],
  maintenance: [roles.superAdministrator, roles.administrator, roles.branchManager, roles.maintenanceOfficer],
  operations: [roles.superAdministrator, roles.administrator, roles.branchManager],
  reports: [roles.superAdministrator, roles.administrator, roles.branchManager, roles.financeOfficer],
  users: [roles.superAdministrator, roles.administrator],
  branches: [roles.superAdministrator, roles.administrator],
  divisions: [roles.superAdministrator],
  configuration: [roles.superAdministrator],
}

export function canAccessPage(userRoles: string[], page: AppPage) {
  return pageRoles[page].some((role) => userRoles.includes(role))
}

export function getPrimaryRole(userRoles: string[]) {
  return Object.values(roles).find((role) => userRoles.includes(role)) ?? 'User'
}

export function formatRole(role: string) {
  return role.replace(/([a-z])([A-Z])/g, '$1 $2')
}
