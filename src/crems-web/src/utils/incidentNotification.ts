type IncidentNotice = { kind: string; systemGenerated: boolean; message: string; url: string | null }

// Customer incident notices already carry the booking ID in the link and the report number in the subject.
export function incidentNotificationUrl(notice: IncidentNotice) {
  if (!notice.systemGenerated || notice.kind !== 'Booking') return notice.url
  const booking = notice.url?.match(/^\/staff\/bookings\?bookingId=([\da-f]{8}(?:-[\da-f]{4}){3}-[\da-f]{12})$/i)
  const incident = notice.message.match(/^Customer incident — (INC-[A-Z0-9-]+) — /i)
  if (!booking || !incident) return notice.url
  return `/staff/bookings?bookingId=${booking[1]}&incidentNumber=${encodeURIComponent(incident[1])}`
}
