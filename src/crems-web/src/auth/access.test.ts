import { describe, expect, it } from 'vitest'
import { canAccessPage, roles } from './access'

describe('organization access', () => {
  it('keeps organization settings with administrators', () => {
    expect(canAccessPage([roles.superAdministrator], 'divisions')).toBe(true)
    expect(canAccessPage([roles.administrator], 'divisions')).toBe(true)
    expect(canAccessPage([roles.branchManager], 'divisions')).toBe(false)
    expect(canAccessPage([roles.rentalOfficer], 'divisions')).toBe(false)
  })

  it('keeps branch manager operations available', () => {
    expect(canAccessPage([roles.branchManager], 'assets')).toBe(true)
    expect(canAccessPage([roles.branchManager], 'rentals')).toBe(true)
  })
})
