import { describe, expect, it } from 'vitest'
import { sessionFailureEvent } from './sessionFailure'
import { classifyApiFailure } from './errors'

describe('session failures', () => {
  it('public reads and health failures do not sign out a customer or staff member', () => {
    for (const url of ['/health', '/api/health', '/public/assets', '/public/branches', '/public/divisions']) {
      expect(sessionFailureEvent(url)).toBeNull()
      const error = { isAxiosError: true, message: '', config: { url }, response: { status: 401 } }
      expect(classifyApiFailure(error, 'Catalogue unavailable.').message).toBe('Catalogue unavailable.')
    }
  })
  it('routes booking expiry to the customer and staff expiry to staff', () => {
    expect(sessionFailureEvent('/public/booking-requests', 'post')).toBe('crems:customer-session-expired')
    expect(sessionFailureEvent('/customer-account/session')).toBe('crems:customer-session-expired')
    expect(sessionFailureEvent('/bookings')).toBe('crems:staff-session-expired')
  })
  it('does not expire sessions for failed login or MFA attempts', () => {
    expect(sessionFailureEvent('/auth/login', 'post')).toBeNull()
    expect(sessionFailureEvent('/auth/mfa/verify', 'post')).toBeNull()
  })
})
