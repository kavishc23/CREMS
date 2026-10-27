import { useEffect, useRef } from 'react'

const cleanUrl = () => { const url = new URL(window.location.href); url.searchParams.delete('rentalResults'); return url.pathname + url.search + url.hash }
const clearEntry = () => window.history.replaceState({ ...window.history.state, cremsSearchActive: false }, '', cleanUrl())

/** Search results are an ephemeral history entry, never a saved account page. */
export function useSearchNavigationGuard(active: boolean, discard: () => void) {
  const restoring = useRef(false)
  useEffect(() => {
    if (!active && new URLSearchParams(window.location.search).has('rentalResults')) clearEntry()
    const beforeUnload = (event: BeforeUnloadEvent) => { if (active) { event.preventDefault(); event.returnValue = '' } }
    const pop = (event: PopStateEvent) => {
      if (restoring.current) { restoring.current = false; event.stopImmediatePropagation(); return }
      if (!active) { if (event.state?.cremsSearchActive) { clearEntry(); discard() }; return }
      if (event.state?.cremsSearchActive && new URLSearchParams(window.location.search).has('rentalResults')) return
      event.stopImmediatePropagation()
      if (!window.confirm('Leave these rental results? Your search and any unfinished request will be discarded.')) { restoring.current = true; window.history.forward(); return }
      discard(); clearEntry()
    }
    const restored = (event: PageTransitionEvent) => { if (event.persisted) { discard(); clearEntry() } }
    window.addEventListener('beforeunload', beforeUnload)
    window.addEventListener('popstate', pop, true)
    window.addEventListener('pageshow', restored)
    return () => { window.removeEventListener('beforeunload', beforeUnload); window.removeEventListener('popstate', pop, true); window.removeEventListener('pageshow', restored) }
  }, [active, discard])
  return {
    markResults() {
      if (window.history.state?.cremsSearchActive) return
      const url = new URL(window.location.href); url.searchParams.set('rentalResults', '1')
      window.history.pushState({ ...window.history.state, cremsSearchActive: true }, '', url.pathname + url.search)
    },
    leave(action: () => void) {
      if (active && !window.confirm('Leave these rental results? Your search and any unfinished request will be discarded.')) return
      if (active) { clearEntry(); discard() }
      action()
    },
  }
}
