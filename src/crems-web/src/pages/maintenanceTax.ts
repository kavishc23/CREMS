export type TaxMode = 'Exclusive' | 'Inclusive' | 'None' | 'Manual'
export type TaxSettings = { mode: TaxMode; rate: string; categories: number; reason: string }
export type ExpenseAmounts = { partsCost: number; labourCost: number; transportCost: number; externalServiceCost: number; otherCost: number; fuelCost?: number }
export const taxCategories = [
  { key: 'partsCost', flag: 1, name: 'Parts', label: 'Purchased parts (excluding issued stock)' },
  { key: 'labourCost', flag: 2, name: 'Labour', label: 'Supplier labour' },
  { key: 'transportCost', flag: 4, name: 'Transport', label: 'Supplier transport' },
  { key: 'externalServiceCost', flag: 8, name: 'ExternalService', label: 'External service' },
  { key: 'otherCost', flag: 16, name: 'Other', label: 'Other taxable supplier charges' },
  { key: 'fuelCost', flag: 32, name: 'Fuel', label: 'Purchased fuel' },
] as const
export function parseTaxCategories(value: string | number | undefined): number {
  if (typeof value === 'number') return value
  if (value == null) return 9
  return taxCategories.reduce((flags, category) => value.split(',').map(part => part.trim()).includes(category.name) ? flags | category.flag : flags, 0)
}
export function calculateMaintenanceTax(costs: ExpenseAmounts, settings: TaxSettings, manualTax: number, issuedStockCost: number) {
  // Integer cents and millionths of a percent match the API's decimal arithmetic,
  // including half-cent boundaries and amounts whose products exceed safe JS integers.
  const cents = (value: number) => BigInt(Number.isFinite(value) ? Math.max(0, Math.round(value * 100)) : 0)
  const subtotal = Object.values(costs).reduce((sum, value) => sum + cents(value), 0n)
  const stock = cents(issuedStockCost)
  const parts = cents(costs.partsCost)
  const taxable = taxCategories.reduce((sum, category) => sum + ((settings.categories & category.flag)
    ? category.key === 'partsCost' ? (parts > stock ? parts - stock : 0n) : cents(costs[category.key] ?? 0) : 0n), 0n)
  const rateValue = Number(settings.rate || 0)
  const rate = BigInt(Number.isFinite(rateValue) ? Math.max(0, Math.round(rateValue * 1000000)) : 0)
  const denominator = 100000000n + (settings.mode === 'Inclusive' ? rate : 0n)
  const tax = settings.mode === 'None' ? 0n : settings.mode === 'Manual' ? cents(manualTax)
    : (taxable * rate + denominator / 2n) / denominator
  return { subtotal: Number(subtotal) / 100, taxable: Number(taxable) / 100, tax: Number(tax) / 100,
    total: Number(subtotal + (settings.mode === 'Inclusive' ? 0n : tax)) / 100 }
}
