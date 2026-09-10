import axios from 'axios'

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
  (response) => response,
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
    return Promise.reject(error)
  },
)
