export type StaffRole = { name: string; isBuiltIn: boolean; modules: string[]; permissions: string[] }
export type StaffRoleCatalog = { builtInPermissions?: string[]; roles: StaffRole[]; modules: { key: string; label: string; pages: string[] }[]; permissions: string[] }
export function permissionLabel(value: string) {
  const labels: Record<string, string> = {
    'users.create': 'Create staff accounts',
    'users.reset_password': 'Reset staff passwords',
    'users.manage_access': 'Manage staff access',
    'customers.manage_access': 'Manage customer portal access',
    'rentals.approve': 'Approve rentals',
    'maintenance.complete': 'Complete maintenance jobs',
    'payments.refund': 'Refund payments',
    'reports.financial': 'View financial reports',
    'divisions.configure': 'Configure divisions',
    'branches.configure': 'Configure branches',
    'services.configure': 'Configure services',
    'asset_categories.configure': 'Configure asset categories',
    'branch_calendar.manage': 'Manage branch calendars',
    'pricing.configure': 'Configure pricing',
    'assets.view': 'View assets',
    'assets.create': 'Create assets',
    'assets.edit': 'Edit assets',
    'assets.transfer': 'Transfer assets',
    'assets.inspect': 'Inspect assets',
    'assets.record_meter': 'Record meter readings',
    'assets.retire': 'Retire assets',
    'assets.view_financials': 'View asset financials',
  }
  if (labels[value]) return labels[value]
  const words = value.replaceAll('.', ' · ').replaceAll('_', ' ')
  return words.charAt(0).toUpperCase() + words.slice(1)
}
