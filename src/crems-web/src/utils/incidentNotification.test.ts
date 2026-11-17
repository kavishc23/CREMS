import { describe, expect, it } from 'vitest'
import { incidentNotificationUrl } from './incidentNotification'

describe('customer incident notification links', () => {
  const url = '/staff/bookings?bookingId=9d2fd55b-e7cc-41df-b8b7-8db13f5097bd'
  const notice = { kind: 'Booking', systemGenerated: true, message: 'Customer incident — INC-20261116-A91935 — BK-2611-JK-05', url }

  it('opens the exact report from an existing notification', () => {
    expect(incidentNotificationUrl(notice)).toBe(url + '&incidentNumber=INC-20261116-A91935')
  })

  it('preserves booking, approval and manually sent notification links', () => {
    expect(incidentNotificationUrl({ ...notice, message: 'Booking status updated' })).toBe(url)
    expect(incidentNotificationUrl({ ...notice, systemGenerated: false })).toBe(url)
    const approvalUrl = url + '&section=approval'
    expect(incidentNotificationUrl({ ...notice, kind: 'Approval', url: approvalUrl })).toBe(approvalUrl)
  })
})
