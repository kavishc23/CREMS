export const roles = {
  administrator: 'Administrator',
  branchManager: 'BranchManager',
  rentalOfficer: 'RentalOfficer',
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

const pageRoles: Record<AppPage, readonly string[]> = {
  dashboard: Object.values(roles),
  assets: [roles.administrator, roles.branchManager, roles.rentalOfficer],
  customers: [roles.administrator, roles.branchManager, roles.rentalOfficer],
  bookings: [roles.administrator, roles.branchManager, roles.rentalOfficer],
  rentals: [roles.administrator, roles.branchManager, roles.rentalOfficer],
  scan: [roles.administrator, roles.branchManager, roles.rentalOfficer],
  maintenance: [roles.administrator, roles.branchManager],
  operations: [roles.administrator, roles.branchManager],
  reports: [roles.administrator, roles.branchManager],
  users: [roles.administrator],
  branches: [roles.administrator, roles.branchManager],
  divisions: [roles.administrator],
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
