import { useEffect, useRef, useState } from 'react'
import { Alert, Box, Button, type AlertProps } from '@mui/material'

/** Keeps the field or form error in context and gives staff a way back to it from elsewhere in the form. */
export function FormError({ children, placement = 'inline', ...props }: Omit<AlertProps, 'severity'> & { placement?: 'inline' | 'footer' }) {
  const alert = useRef<HTMLDivElement>(null)
  const [offscreen, setOffscreen] = useState(false)

  useEffect(() => {
    const element = alert.current
    if (placement === 'footer' || !element || typeof IntersectionObserver === 'undefined') return
    const observer = new IntersectionObserver(([entry]) => {
      setOffscreen(!entry.isIntersecting && element.getClientRects().length > 0)
    })
    observer.observe(element)
    return () => observer.disconnect()
  }, [placement])

  const message = <Alert {...props} severity="error" tabIndex={-1} ref={alert}>
    <div data-form-error>{children}</div>
  </Alert>
  if (placement === 'footer') return <Box sx={{ px: { xs: 2, md: 3 }, py: 1, borderTop: 1, borderColor: 'divider', maxHeight: '25dvh', overflowY: 'auto', flexShrink: 0 }}>
    {message}
  </Box>

  return <>
    {message}
    {offscreen && <Button color="error" variant="contained" onClick={() => {
      alert.current?.scrollIntoView({ block: 'center', inline: 'nearest' })
      alert.current?.focus({ preventScroll: true })
    }} sx={{
      position: 'fixed', right: { xs: 12, sm: 24 }, bottom: 'calc(84px + env(safe-area-inset-bottom, 0px))',
      zIndex: theme => theme.zIndex.modal + 1, boxShadow: 2,
    }}>Review form error</Button>}
  </>
}
