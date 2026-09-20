import axios from 'axios'

export type ApiFailureKind = 'validation' | 'session' | 'network' | 'timeout' | 'server' | 'unexpected'
export type ApiFailure = { kind: ApiFailureKind; message: string }

function validationMessage(data: unknown) {
  if (!data || typeof data !== 'object') return ''
  const body = data as { message?: unknown; detail?: unknown; errors?: Record<string, unknown> }
  if (typeof body.message === 'string' && body.message.trim()) return body.message.trim()
  if (body.errors) {
    const messages = Object.values(body.errors).flatMap(value => Array.isArray(value) ? value : [value])
      .filter((value): value is string => typeof value === 'string' && Boolean(value.trim()))
    if (messages.length) return messages.join(' ')
  }
  if (typeof body.detail === 'string' && body.detail.trim()) return body.detail.trim()
  return ''
}

export function classifyApiFailure(error: unknown, fallback = 'The request could not be completed.'): ApiFailure {
  if (!axios.isAxiosError(error)) return { kind: 'unexpected', message: fallback }
  if (error.code === 'ECONNABORTED' || /timeout/i.test(error.message))
    return { kind: 'timeout', message: 'The request timed out. Please try again.' }
  if (!error.response)
    return { kind: 'network', message: 'CREMS could not connect to the API. Start the backend on http://localhost:5080 and try again.' }
  if (error.response.status === 401)
    return { kind: 'session', message: 'Your customer session has expired. Sign in again, then resubmit your request.' }
  const safeDetail = validationMessage(error.response.data)
  if (error.response.status === 400 || error.response.status === 404 || error.response.status === 409 || error.response.status === 422)
    return { kind: 'validation', message: safeDetail || fallback }
  if (error.response.status >= 500)
    return { kind: 'server', message: 'The server could not complete this request. Please try again or contact the branch.' }
  return { kind: 'unexpected', message: safeDetail || fallback }
}

export function apiErrorMessage(error: unknown, fallback?: string) {
  return classifyApiFailure(error, fallback).message
}
