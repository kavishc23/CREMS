import { api } from './client'

export type PublicBookingSubmission = {
  reference: string
  requestType: 'Booking' | 'Quotation'
}

type BookingClient = {
  post<T>(url: string, payload: unknown, config?: { timeout: number }): Promise<{ data: T }>
}

export async function submitPublicBookingRequest(payload: unknown, client: BookingClient = api) {
  const response = await client.post<PublicBookingSubmission>('/public/booking-requests', payload, { timeout: 30_000 })
  return response.data
}
