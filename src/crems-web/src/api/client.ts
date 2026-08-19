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
  config.headers['X-CREMS-Window-Id'] = getWindowSessionId()
  config.headers.Accept = 'application/json'
  return config
})
