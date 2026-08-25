import type { AppPage } from '../auth/access'

export type DemoStage = 'week2' | 'week3' | 'full'

const configuredStage = String(import.meta.env.VITE_CREMS_DEMO_STAGE ?? 'full').toLowerCase()

export const demoStage: DemoStage = configuredStage === 'week2' || configuredStage === 'week3'
  ? configuredStage
  : 'full'
export const isWeek2Demo = demoStage === 'week2'
export const isWeek3Demo = demoStage === 'week3'

const week2Pages = new Set<AppPage>([
  'dashboard',
  'assets',
  'users',
  'branches',
])

const week3Pages = new Set<AppPage>([
  'dashboard',
  'assets',
  'scan',
  'users',
  'customerAccounts',
])

export function isPageEnabledForDemo(page: AppPage) {
  if (isWeek2Demo) return week2Pages.has(page)
  if (isWeek3Demo) return week3Pages.has(page)
  return true
}
