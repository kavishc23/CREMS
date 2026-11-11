export type AvailabilityAsset = {
  id: string; assetNumber: string; name: string; branchId: string; divisionId: string | null
  assetCategoryId?: string | null; categoryName?: string | null
}
export type AvailabilityFilters = { branch: string; division: string; category: string }
export function categoryKey(asset: AvailabilityAsset) {
  return asset.assetCategoryId || (asset.categoryName ? `legacy:${asset.categoryName}` : 'uncategorised')
}
export function matchesAvailabilityFilters(asset: AvailabilityAsset, filters: AvailabilityFilters) {
  return (filters.branch === 'all' || asset.branchId === filters.branch)
    && (filters.division === 'all' || asset.divisionId === filters.division)
    && (filters.category === 'all' || categoryKey(asset) === filters.category)
}
