const timeoutMs = 15 * 60 * 1000
const activityEvents = ['pointerdown', 'keydown', 'scroll', 'touchstart'] as const

export function monitorInactivity(onTimeout: () => void) {
  let lastActivity = Date.now()
  let timer = 0

  const schedule = () => {
    window.clearTimeout(timer)
    const remaining = timeoutMs - (Date.now() - lastActivity)
    if (remaining <= 0) onTimeout()
    else timer = window.setTimeout(onTimeout, remaining)
  }
  const active = () => { lastActivity = Date.now(); schedule() }
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
