import { describe, expect, it } from 'vitest'
import { newDocument, normalizeSetupDivision, parseDivisionCapabilities } from './organizationSetup'

describe('division setup optional contact email', () => {
  it.each(['', '   ', null])('omits blank email %s from API validation', email => {
    const division = { ...newDocument().division, contactEmail: email }
    const normalized = normalizeSetupDivision(division)
    expect(JSON.parse(JSON.stringify(normalized)).contactEmail).toBeNull()
    expect(division.contactEmail).toBe(email)
    expect(normalized).toEqual({ ...division, contactEmail: null })
  })
  it('trims a supplied email without hiding invalid input', () => {
    const division = newDocument().division
    expect(normalizeSetupDivision({ ...division, contactEmail: ' test@example.com ' }).contactEmail).toBe('test@example.com')
    expect(normalizeSetupDivision({ ...division, contactEmail: 'invalid-email' }).contactEmail).toBe('invalid-email')
  })
})

describe('division capabilities returned by the API', () => {
  it.each([[3, 3], ['3', 3], ['None', 0], ['Rental, Maintenance', 3], ['PropertyLeasing, Logistics, Retail, PersonnelSupportedHire', 60]])('decodes %s as %s', (value, expected) => {
    expect(parseDivisionCapabilities(value)).toBe(expected)
  })
  it('preserves existing capabilities when toggling another checkbox', () => {
    const saved = parseDivisionCapabilities('Rental, Maintenance')
    expect(saved | 8).toBe(11)
    expect(saved & ~1).toBe(2)
  })
})
