import { describe, expect, it } from 'vitest'
import { newDocument, normalizeSetupDivision, brandingError, capabilityValue, newService, readJson, serviceTracking, setServiceTracking, setupTemplates, templateService } from './organizationSetup'

describe('division settings compatibility', () => {
  it('reads API enum flags and legacy numbers without erasing selected scope', () => {
    expect(capabilityValue('Rental, Maintenance, PersonnelSupportedHire')).toBe(35)
    expect(capabilityValue('PropertyLeasing')).toBe(4)
    expect(capabilityValue(4)).toBe(4)
    expect(capabilityValue('4')).toBe(4)
  })
  it('starts custom resources without motor-related requirements', () => {
    expect(serviceTracking(newService().service)).toEqual({ meter: false, fuel: false })
    for (const [index, template] of setupTemplates.entries()) {
      if (!template.meter) expect(serviceTracking(templateService(index).service)).toEqual({ meter: false, fuel: false })
    }
    const property = templateService(setupTemplates.findIndex(t => t.type === 'PropertyLease'))
    expect(property.service.defaultHireUnit).toBe('Month')
    expect(property.service.meterType).toBeNull()
  })
  it('removing usage tracking removes the usage trigger but preserves calendar and custom rules', () => {
    const service = { ...newService().service, meterType: 'Cycles', maintenanceRulesJson: '{"meterInterval":250,"serviceIntervalDays":90,"customRule":"keep"}', inspectionRequirementsJson: '{"tracksMeter":true,"tracksFuel":true,"evidence":"photo"}' }
    const result = { ...service, ...setServiceTracking(service, false, false) }
    expect(readJson(result.maintenanceRulesJson, {})).toEqual({ serviceIntervalDays: 90, customRule: 'keep' })
    expect(readJson(result.inspectionRequirementsJson, {})).toEqual({ tracksMeter: false, tracksFuel: false, evidence: 'photo' })
    expect(result.meterType).toBeNull()
  })
  it('validates optional presentation without discarding existing keys', () => {
    expect(brandingError('{"legacy":"keep"}')).toBe('')
    expect(brandingError('{"accentColor":"#fff000","logoUrl":"https://example.test/logo.png"}')).toBe('')
    expect(brandingError('{"accentColor":"yellow"}')).not.toBe('')
    expect(brandingError('{"logoUrl":"javascript:alert(1)"}')).not.toBe('')
  })
})

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
    expect(capabilityValue(value)).toBe(expected)
  })
  it('preserves existing capabilities when toggling another checkbox', () => {
    const saved = capabilityValue('Rental, Maintenance')
    expect(saved | 8).toBe(11)
    expect(saved & ~1).toBe(2)
  })
})
