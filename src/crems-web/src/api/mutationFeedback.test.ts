import { describe, expect, it } from 'vitest'
import { usesContextualFeedback } from './mutationFeedback'

describe('customer action feedback', () => {
  it('keeps verification, licence scans, and saved customer changes in their own workflow', () => {
    for (const url of ['/customer-account/verification/request', '/customer-account/verification/confirm', '/customer-account/licence/scan', '/customer-account/licence/submission/confirm', '/customer-account/profile', '/api/customer-account/rental-preferences', '/public/booking-requests', '/auth/password-reset/request']) {
      expect(usesContextualFeedback(url)).toBe(true)
    }
  })
  it('keeps generic saved feedback for existing staff actions but not incomplete licence scans', () => {
    expect(usesContextualFeedback('/customers/123/licence/scan')).toBe(true)
    expect(usesContextualFeedback('/customers/123/licence/456/confirm')).toBe(true)
    expect(usesContextualFeedback('/assets/123')).toBe(false)
    expect(usesContextualFeedback('/bookings/123')).toBe(false)
  })
})
