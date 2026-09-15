const timeoutMs = 15 * 60 * 1000
const activityEvents = ['pointerdown', 'keydown', 'scroll', 'touchstart'] as const

export function monitorInactivity(onTimeout: () => void, onActive?: () => void) {
  let lastActivity = Date.now()
  let timer = 0
  let lastRenewal = Date.now()
  let expired = false

  const expire = () => { if (!expired) { expired = true; onTimeout() } }
  const schedule = () => {
    window.clearTimeout(timer)
    const remaining = timeoutMs - (Date.now() - lastActivity)
    if (remaining <= 0) expire()
    else timer = window.setTimeout(expire, remaining)
  }
  const active = () => {
    const now = Date.now()
    if (expired) return
    if (now - lastActivity >= timeoutMs) { expire(); return }
    lastActivity = now
    schedule()
    // Renew only on user activity, at most once a minute. Idle tabs stay idle.
    if (onActive && now - lastRenewal >= 60_000) {
      lastRenewal = now
      onActive()
    }
  }
  const visible = () => { if (document.visibilityState === 'visible') schedule() }

  activityEvents.forEach(event => window.addEventListener(event, active, { passive: true }))
  document.addEventListener('visibilitychange', visible)
  schedule()

  return () => {
    window.clearTimeout(timer)
    activityEvents.forEach(event => window.removeEventListener(event, active))
    document.removeEventListener('visibilitychange', visible)
  }
}
