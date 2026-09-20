import { describe, expect, it, vi } from 'vitest'
import { classifyApiFailure } from './errors'
import { submitPublicBookingRequest } from './publicBooking'

const axiosError = (values: Record<string, unknown>) => ({ isAxiosError: true, message: '', ...values })

describe('public booking API handling', () => {
  it('returns the quotation reference supplied by the API', async () => {
    const post = vi.fn().mockResolvedValue({ data: { reference: 'QUO-20260920-ABC123', requestType: 'Quotation' } })

    const result = await submitPublicBookingRequest({ assetId: 'asset-1' }, { post })

    expect(result.reference).toBe('QUO-20260920-ABC123')
    expect(post).toHaveBeenCalledWith('/public/booking-requests', { assetId: 'asset-1' }, { timeout: 30_000 })
  })

  it('shows safe API validation details', () => {
    const result = classifyApiFailure(axiosError({
      response: { status: 409, data: { message: 'No operator rate is configured for this service.' } },
    }))
    expect(result).toEqual({ kind: 'validation', message: 'No operator rate is configured for this service.' })
  })

  it('distinguishes an unavailable API from a timed-out request', () => {
    expect(classifyApiFailure(axiosError({ code: 'ERR_NETWORK' }))).toEqual({
      kind: 'network',
      message: 'CREMS could not connect to the API. Start the backend on http://localhost:5080 and try again.',
    })
    expect(classifyApiFailure(axiosError({ code: 'ECONNABORTED', message: 'timeout of 30000ms exceeded' })).kind)
      .toBe('timeout')
  })

  it('does not expose server response details for unexpected failures', () => {
    const result = classifyApiFailure(axiosError({
      response: { status: 500, data: { detail: 'System.Exception: database password=secret' } },
    }))
    expect(result.kind).toBe('server')
    expect(result.message).not.toContain('Exception')
    expect(result.message).not.toContain('secret')
  })
})
