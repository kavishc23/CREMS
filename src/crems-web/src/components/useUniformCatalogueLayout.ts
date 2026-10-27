import { useLayoutEffect, useRef } from 'react'

/** All visible cards share section sizes, including when new category names wrap. */
export function useUniformCatalogueLayout() {
  const ref = useRef<HTMLDivElement>(null)
  useLayoutEffect(() => {
    const root = ref.current
    if (!root) return
    let frame = 0
    const measure = () => {
      const height = (selector: string, minimum: number, extra: number) => Math.ceil(Math.max(minimum, ...Array.from(root.querySelectorAll<HTMLElement>(selector), element => element.offsetHeight + extra)))
      const heading = `${height('.catalogue-heading-content', 122, 32)}px`
      const footer = `${height('.catalogue-footer-content', 190, 84)}px`
      if (root.style.getPropertyValue('--catalogue-heading-height') !== heading) root.style.setProperty('--catalogue-heading-height', heading)
      if (root.style.getPropertyValue('--catalogue-footer-height') !== footer) root.style.setProperty('--catalogue-footer-height', footer)
    }
    const schedule = () => { cancelAnimationFrame(frame); frame = requestAnimationFrame(measure) }
    const resize = new ResizeObserver(schedule)
    const observe = () => {
      resize.disconnect(); resize.observe(root)
      root.querySelectorAll('.catalogue-heading-content, .catalogue-footer-content').forEach(element => resize.observe(element))
      measure()
    }
    const mutation = new MutationObserver(observe)
    mutation.observe(root, { childList: true, subtree: true, characterData: true })
    observe()
    return () => { cancelAnimationFrame(frame); resize.disconnect(); mutation.disconnect() }
  }, [])
  return ref
}

