import type { AppPage } from '../auth/access'

export type DemoStage = 'demo' | 'full'

const configuredStage = String(import.meta.env.VITE_CREMS_DEMO_STAGE ?? 'full').toLowerCase()

export const demoStage: DemoStage = configuredStage === 'demo' ? 'demo' : 'full'
export const isDemoMode = demoStage === 'demo'
export const isNotificationModuleEnabled = !isDemoMode

// Demo is the cumulative, presentation-safe environment. It now includes
// staged approvals and the guided QR-supported pickup and return lifecycle.
const demoPages = new Set<AppPage>([
  'dashboard',
  'assets',
  'users',
  'customerAccounts',
  'customers',
  'bookings',
  'rentals',
  'scan',
  'operations',
  'configuration',
  'divisions',
  'branches',
])

export function isPageEnabledForDemo(page: AppPage) {
  return !isDemoMode || demoPages.has(page)
}
