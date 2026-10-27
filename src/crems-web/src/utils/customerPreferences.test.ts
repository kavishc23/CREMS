import { describe, expect, it } from 'vitest'
import { matchesHomepagePreferences } from './customerPreferences'

const assets = [
  { id: 'forklift', division: 'carptrac', assetCategoryId: 'forklift' },
  { id: 'generator', division: 'carptrac', assetCategoryId: 'generator' },
  { id: 'heavy', division: 'carptrac', assetCategoryId: 'heavy' },
  { id: 'general', division: 'carptrac', assetCategoryId: 'general' },
  { id: 'bin', division: 'shipping', assetCategoryId: 'bin' },
  { id: 'toilet', division: 'shipping', assetCategoryId: 'toilet' },
  { id: 'other-bin', division: 'new-division', assetCategoryId: 'other-bin' },
]
describe('homepage category preferences', () => {
  it('shows only the selected Carptrac categories', () => {
    expect(assets.filter(asset => matchesHomepagePreferences(asset, ['forklift', 'generator', 'heavy', 'general'])).map(asset => asset.id)).toEqual(['forklift', 'generator', 'heavy', 'general'])
  })
  it('shows big bins without other Shipping categories or similarly named categories in another division', () => {
    expect(assets.filter(asset => matchesHomepagePreferences(asset, ['bin'])).map(asset => asset.id)).toEqual(['bin'])
  })
  it('includes all rentals when cleared, and supports categories configured later', () => {
    expect(assets.filter(asset => matchesHomepagePreferences(asset, []))).toEqual(assets)
    expect(matchesHomepagePreferences({ assetCategoryId: 'new-projector' }, ['new-projector'])).toBe(true)
    expect(matchesHomepagePreferences({}, ['bin'])).toBe(false)
  })
})
