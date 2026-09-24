export function sessionFailureEvent(url: string, method = 'get') {
  const path = new URL(url, 'http://crems.local').pathname.replace(/^\/api(?=\/)/, '')
  if (path === '/auth/login' || path.startsWith('/auth/mfa/')) return null
  if (path === '/health' || (path.startsWith('/public/') && ['get', 'head', 'options'].includes(method.toLowerCase()))) return null
  return path.startsWith('/customer-account/') || path === '/public/booking-requests'
    ? 'crems:customer-session-expired'
    : 'crems:staff-session-expired'
}
