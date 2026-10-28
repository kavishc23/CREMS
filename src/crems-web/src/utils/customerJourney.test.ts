import { describe, expect, it } from 'vitest'
import { customerJourneyStage, customerJourneyActions, customerStageColor, hasQuotationOffer } from './customerJourney'

const now = new Date('2026-10-29T00:00:00Z')
const booking = (status = 'Draft') => ({ status, items: [{ startAt: '2026-11-01T00:00:00Z' }] })
const quote = (status = 'Draft') => ({ status, validUntil: '2026-11-10T00:00:00Z' })
describe('customer rental stages', () => {
  it.each([
    ['Draft', undefined, 'AwaitingConfirmation'], ['Draft', 'Draft', 'AwaitingQuotation'],
    ['Draft', 'Sent', 'ReviewQuotation'], ['Draft', 'Accepted', 'AwaitingConfirmation'],
    ['Confirmed', 'Accepted', 'Confirmed'], ['ConvertedToRental', 'Converted', 'OnHire'],
    ['Completed', 'Converted', 'Completed'], ['Draft', 'Rejected', 'Declined'], ['Expired', undefined, 'Expired'],
  ])('%s booking with %s quote is %s', (status, offer, expected) => {
    expect(customerJourneyStage(booking(status), offer ? quote(offer) : undefined, now)).toBe(expected)
  })
  it('only exposes an offer after it has been sent', () => {
    expect(hasQuotationOffer(quote())).toBe(false)
    expect(hasQuotationOffer(quote('Sent'))).toBe(true)
    expect(customerJourneyActions('AwaitingQuotation', true)).toEqual(['cancelRequest'])
    expect(customerJourneyActions('ReviewQuotation', true)).toEqual(['reviewQuote'])
  })
  it('keeps accepted and confirmed hires valid beyond quotation expiry', () => {
    const expired = { ...quote('Accepted'), validUntil: '2026-10-28T00:00:00Z' }
    expect(customerJourneyStage(booking(), expired, now)).toBe('AwaitingConfirmation')
    expect(customerJourneyStage(booking('Confirmed'), expired, now)).toBe('Confirmed')
    expect(customerJourneyStage(booking(), { ...expired, status: 'Sent' }, now)).toBe('Expired')
  })
  it('respects the authoritative cancelled or staff declined state', () => {
    expect(customerJourneyStage({ ...booking(), customerStatus: 'Declined' }, quote('Sent'), now)).toBe('Declined')
    expect(customerJourneyActions('Completed', true)).toEqual(['summary'])
  })
  it('uses the same milestone colours for booking and quotation journeys', () => {
    expect(['AwaitingQuotation', 'ReviewQuotation', 'AwaitingConfirmation', 'Confirmed', 'OnHire', 'Completed', 'Expired', 'Declined'].map(customerStageColor))
      .toEqual(['default', 'default', 'default', 'success', 'warning', 'info', 'error', 'error'])
  })
})
