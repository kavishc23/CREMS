import { describe, expect, it } from 'vitest'
import { calculateMaintenanceTax, parseTaxCategories, type TaxMode } from './maintenanceTax'

describe('maintenance tax preview', () => {
  const costs = { partsCost: 500, labourCost: 50, transportCost: 0, externalServiceCost: 0, otherCost: 0 }
  it.each<[TaxMode, number, number]>([['Exclusive', 68.75, 618.75], ['Inclusive', 61.11, 550], ['None', 0, 550], ['Manual', 70, 620]])('%s tax and total', (mode, tax, total) => {
    expect(calculateMaintenanceTax(costs, { mode, rate: '12.5', categories: 3, reason: '' }, 70, 0)).toMatchObject({ tax, total })
  })
  it('excludes issued stock and unselected internal labour', () => {
    expect(calculateMaintenanceTax(costs, { mode: 'Exclusive', rate: '12.5', categories: 1, reason: '' }, 0, 100)).toMatchObject({ taxable: 400, tax: 50, total: 600 })
  })
  it('includes fuel in totals and taxes it only when selected', () => {
    const expenses = { ...costs, fuelCost: 50 }
    expect(calculateMaintenanceTax(expenses, { mode: 'Exclusive', rate: '12.5', categories: 32, reason: '' }, 0, 0)).toMatchObject({ subtotal: 600, taxable: 50, tax: 6.25, total: 606.25 })
    expect(calculateMaintenanceTax(expenses, { mode: 'Exclusive', rate: '12.5', categories: 0, reason: '' }, 0, 0)).toMatchObject({ tax: 0, total: 600 })
    expect(parseTaxCategories('Parts, Fuel')).toBe(33)
  })
  it('reads flags serialized by the API', () => {
    expect(parseTaxCategories('Parts, ExternalService')).toBe(9)
    expect(parseTaxCategories('None')).toBe(0)
    expect(parseTaxCategories(31)).toBe(31)
  })
  it('rounds half cents away from zero', () => {
    expect(calculateMaintenanceTax({ ...costs, partsCost: 0.04, labourCost: 0 }, { mode: 'Exclusive', rate: '12.5', categories: 1, reason: '' }, 0, 0).tax).toBe(0.01)
  })
  it.each([[17.08, 2.14], [17.40, 2.18], [19.08, 2.39], [19.40, 2.43], [32.12, 4.02]])('matches decimal tax rounding for %s', (amount, expected) => {
    expect(calculateMaintenanceTax({ ...costs, partsCost: amount, labourCost: 0 }, { mode: 'Exclusive', rate: '12.5', categories: 1, reason: '' }, 0, 0).tax).toBe(expected)
  })
  it('rounds included tax without changing the invoice total', () => {
    expect(calculateMaintenanceTax({ ...costs, partsCost: 17.07, labourCost: 0 }, { mode: 'Inclusive', rate: '20', categories: 1, reason: '' }, 0, 0)).toMatchObject({ tax: 2.85, total: 17.07 })
  })
})
