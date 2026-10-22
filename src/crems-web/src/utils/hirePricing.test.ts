import { describe, it, expect } from 'vitest'
import { hireQuantity } from './hirePricing'
describe('hire billing quantities',()=>{
  it('uses calendar months including leap years',()=>{
    expect(hireQuantity('Month','2028-01-31','2028-02-29')).toBe(1)
    expect(hireQuantity('Month','2028-01-31','2028-03-01')).toBe(2)
  })
  it('preserves measured quantities and rounds started weeks',()=>{
    expect(hireQuantity('SquareMetre','2026-01-01','2026-01-09',12.5)).toBe(12.5)
    expect(hireQuantity('Week','2026-01-01','2026-01-09')).toBe(2)
  })
})
