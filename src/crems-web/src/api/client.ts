import axios from 'axios'
import { classifyApiFailure } from './errors'

function getWindowSessionId() {
  const key = 'crems.window-session-id'
  const existing = sessionStorage.getItem(key)
  if (existing) return existing
  const created = crypto.randomUUID()
  sessionStorage.setItem(key, created)
  return created
}

export const api = axios.create({
  baseURL: '/api',
  headers: { 'Content-Type': 'application/json' },
  withCredentials: true,
  timeout: 8000,
})

api.interceptors.request.use((config) => {
  if (config.data instanceof FormData) delete config.headers['Content-Type']
  config.headers['X-CREMS-Window-Id'] = getWindowSessionId()
  config.headers.Accept = 'application/json'
  return config
})

api.interceptors.response.use(
  (response) => {
    const method = response.config.method?.toLowerCase()
    const url = response.config.url ?? ''
    if (method && !['get', 'head', 'options'].includes(method) && !url.includes('/notifications/read') && !url.includes('/auth/') && !url.includes('/session')) {
      window.dispatchEvent(new Event('crems:data-changed'))
      if (!url.includes('/notifications/send')) window.dispatchEvent(new CustomEvent('crems:toast', { detail: { severity: 'success', message: url.includes('/public/booking-requests') ? response.data?.requestType === 'Quotation' ? 'Quotation Request Submitted.' : 'Booking Submitted.' : response.status === 202 ? 'Submitted for review.' : 'Changes saved.' } }))
    }
    return response
  },
  (error) => {
    const status = error?.response?.status
    const requestUrl = String(error?.config?.url ?? '')
    const isAuthenticationAttempt = requestUrl.includes('/auth/login') || requestUrl.includes('/auth/mfa/verify')
    if (status === 401 && !isAuthenticationAttempt) {
      const eventName = requestUrl.includes('/customer-account/')
        ? 'crems:customer-session-expired'
        : 'crems:staff-session-expired'
      window.dispatchEvent(new CustomEvent(eventName))
    }
    if (!axios.isCancel(error) && !['get', 'head'].includes(error?.config?.method ?? 'get') && status !== 401 && !requestUrl.includes('/notifications/read') && !requestUrl.includes('/public/booking-requests')) {
      const message = classifyApiFailure(error, 'The action could not be completed. Check the details and try again.').message
      window.dispatchEvent(new CustomEvent('crems:toast', { detail: { severity: 'error', message } }))
    }
    return Promise.reject(error)
  },
)
