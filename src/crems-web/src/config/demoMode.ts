import type { AppPage } from '../auth/access'

export type DemoStage = 'week2' | 'full'

const configuredStage = String(import.meta.env.VITE_CREMS_DEMO_STAGE ?? 'full').toLowerCase()

export const demoStage: DemoStage = configuredStage === 'week2' ? 'week2' : 'full'
export const isWeek2Demo = demoStage === 'week2'

const week2Pages = new Set<AppPage>([
  'dashboard',
  'assets',
  'users',
  'branches',
])

export function isPageEnabledForDemo(page: AppPage) {
  return !isWeek2Demo || week2Pages.has(page)
}
