import { describe, expect, it } from 'vitest'
import { categoryKey, matchesAvailabilityFilters, type AvailabilityAsset } from './availabilityFilters'

const asset: AvailabilityAsset = { id: 'car', assetNumber: 'CAR-1', name: 'Car', branchId: 'suva', divisionId: 'motors', assetCategoryId: 'sedan', categoryName: 'Sedan' }
describe('availability filters', () => {
  it('combines category, branch and division independently', () => {
    expect(matchesAvailabilityFilters(asset, { branch: 'suva', division: 'motors', category: 'sedan' })).toBe(true)
    for (const filters of [
      { branch: 'other', division: 'motors', category: 'sedan' },
      { branch: 'suva', division: 'shipping', category: 'sedan' },
      { branch: 'suva', division: 'motors', category: 'suv' },
    ]) expect(matchesAvailabilityFilters(asset, filters)).toBe(false)
  })
  it('supports all selections, legacy categories and assets without categories', () => {
    expect(matchesAvailabilityFilters(asset, { branch: 'all', division: 'all', category: 'all' })).toBe(true)
    expect(categoryKey({ ...asset, assetCategoryId: null })).toBe('legacy:Sedan')
    expect(categoryKey({ ...asset, assetCategoryId: null, categoryName: null })).toBe('uncategorised')
    expect(matchesAvailabilityFilters({ ...asset, assetCategoryId: null, categoryName: null }, { branch: 'suva', division: 'all', category: 'uncategorised' })).toBe(true)
  })
})
