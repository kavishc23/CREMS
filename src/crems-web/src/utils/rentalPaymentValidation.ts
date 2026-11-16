export function paymentAmountError(value: string, label: string, maximum = 1000000): string {
  if (!value.trim()) return `Enter ${label.toLowerCase()}, or enter 0 if none applies.`
  const amount = Number(value)
  if (!Number.isFinite(amount) || amount < 0) return `${label} must be a valid amount of zero or more.`
  if (amount !== Number(amount.toFixed(2))) return `${label} must have no more than two decimal places.`
  if (amount > maximum) return `${label} cannot exceed FJD ${maximum.toFixed(2)}.`
  return ''
}

export function receiptValidationError(value: string, required: boolean, maximum: number): string {
  if (required && !value.trim()) return 'Enter a receipt or transaction reference for the money received.'
  if (value.trim().length > maximum) return `Keep the receipt or transaction reference to ${maximum} characters or fewer.`
  return ''
}

export function previewAmount(value: string): number {
  const amount = Number(value)
  return Number.isFinite(amount) ? Math.max(0, amount) : 0
}

export const roundMoney = (amount: number) => Math.round((amount + Number.EPSILON) * 100) / 100
