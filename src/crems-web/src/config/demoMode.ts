import type { AppPage } from '../auth/access'

export type DemoStage = 'demo' | 'full'

const configuredStage = String(import.meta.env.VITE_CREMS_DEMO_STAGE ?? 'full').toLowerCase()

export const demoStage: DemoStage = configuredStage === 'demo' ? 'demo' : 'full'
export const isDemoMode = demoStage === 'demo'

// Demo is the cumulative, presentation-safe environment. Week 4 adds the
// customer-to-staff booking flow without exposing later lifecycle modules.
const demoPages = new Set<AppPage>([
  'dashboard',
  'assets',
  'users',
  'customerAccounts',
  'customers',
  'bookings',
])

export function isPageEnabledForDemo(page: AppPage) {
  return !isDemoMode || demoPages.has(page)
}
