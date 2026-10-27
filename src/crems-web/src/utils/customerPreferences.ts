/** Category IDs belong to a specific division; matching by ID prevents name collisions. */
export function matchesHomepagePreferences(asset: { assetCategoryId?: string | null }, categoryIds: readonly string[]): boolean {
  return categoryIds.length === 0 || !!asset.assetCategoryId && categoryIds.includes(asset.assetCategoryId)
}
