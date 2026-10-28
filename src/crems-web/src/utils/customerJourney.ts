type BookingStageSource = { status: string; customerStatus?: string; items: { startAt: string }[] }
type QuoteStageSource = { status: string; customerStatus?: string; validUntil: string; hasOffer?: boolean }
export function hasQuotationOffer(quote: QuoteStageSource) {
  return quote.hasOffer ?? ['Sent', 'Negotiating', 'Accepted', 'Converted'].includes(quote.status)
}
export function customerJourneyStage(booking?: BookingStageSource, quote?: QuoteStageSource, now = new Date()): string {
  if (booking?.customerStatus) return booking.customerStatus
  if (booking && ['Confirmed', 'Completed', 'Expired', 'Cancelled'].includes(booking.status)) return booking.status
  if (booking?.status === 'ConvertedToRental') return 'OnHire'
  if (quote?.customerStatus) return quote.customerStatus
  if (quote?.status === 'Rejected') return 'Declined'
  if (quote?.status === 'Expired' || quote && ['Sent', 'Negotiating'].includes(quote.status) && new Date(quote.validUntil) <= now) return 'Expired'
  const localDate = (date: Date) => new Intl.DateTimeFormat('en-CA', { timeZone: 'Pacific/Fiji', year: 'numeric', month: '2-digit', day: '2-digit' }).format(date)
  const pickup = booking?.items.map(item => item.startAt).sort()[0]
  if (pickup && localDate(new Date(pickup)) < localDate(now)) return 'Expired'
  if (!quote || ['Accepted', 'Converted'].includes(quote.status)) return 'AwaitingConfirmation'
  return ['Sent', 'Negotiating'].includes(quote.status) ? 'ReviewQuotation' : 'AwaitingQuotation'
}
export function customerStageLabel(status: string) {
  return ({ Draft: 'Awaiting Confirmation', AwaitingConfirmation: 'Awaiting Confirmation', AwaitingQuotation: 'Awaiting Quotation',
    ReviewQuotation: 'Review Quotation', Sent: 'Review Quotation', Negotiating: 'Review Quotation', Accepted: 'Awaiting Confirmation',
    Converted: 'Awaiting Confirmation', ConvertedToRental: 'On Hire', OnHire: 'On Hire', Confirmed: 'Confirmed', Completed: 'Completed',
    Rejected: 'Declined', Declined: 'Declined', Expired: 'Expired', Cancelled: 'Cancelled' } as Record<string, string>)[status] ?? status
}
export function customerStageColor(status: string): 'default' | 'success' | 'warning' | 'info' | 'error' {
  if (status === 'Confirmed') return 'success'
  if (['OnHire', 'ConvertedToRental'].includes(status)) return 'warning'
  if (status === 'Completed') return 'info'
  if (['Declined', 'Rejected', 'Expired', 'Cancelled'].includes(status)) return 'error'
  return 'default'
}
export type JourneyAction = 'summary' | 'dates' | 'cancelRequest' | 'reviewQuote' | 'cancelBooking' | 'requestCancellation' | 'extension' | 'incident'
export function customerJourneyActions(stage: string, quotation: boolean): JourneyAction[] {
  switch (stage) {
    case 'AwaitingQuotation': return ['cancelRequest']
    case 'ReviewQuotation': return ['reviewQuote']
    case 'AwaitingConfirmation': return quotation ? ['summary', 'cancelBooking'] : ['summary', 'dates', 'cancelBooking']
    case 'Confirmed': return ['summary', 'requestCancellation', 'extension']
    case 'OnHire': return ['summary', 'extension', 'incident']
    default: return ['summary']
  }
}
