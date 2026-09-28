import { useEffect, useState } from 'react'
import { Alert, AlertTitle, Snackbar } from '@mui/material'

type Severity = 'success' | 'error' | 'info'
type Notice = { message: string; severity: Severity; title?: string; id: number }

export function toast(message: string, severity: Severity = 'info', title?: string) {
  window.dispatchEvent(new CustomEvent('crems:toast', { detail: { message, severity, title } }))
}
export function ToastHost() {
  const [notice, setNotice] = useState<Notice | null>(null)
  useEffect(() => {
    const listener = (event: Event) => setNotice({ ...(event as CustomEvent).detail, id: Date.now() })
    window.addEventListener('crems:toast', listener)
    return () => window.removeEventListener('crems:toast', listener)
  }, [])
  const severity = notice?.severity ?? 'info'
  const heading = notice?.title || { success: 'All done', error: 'Needs your attention', info: 'New update' }[severity]
  return <Snackbar key={notice?.id} open={Boolean(notice)} autoHideDuration={8000}
    onClose={(_, reason) => { if (reason !== 'clickaway') setNotice(null) }}
    anchorOrigin={{ vertical: 'top', horizontal: 'right' }}
    sx={{ top: { xs: 72, sm: 80 }, right: { xs: 12, sm: 24 }, left: 'auto', maxWidth: 'calc(100vw - 24px)', zIndex: theme => theme.zIndex.modal + 100 }}>
    <Alert severity={severity} role={severity === 'error' ? 'alert' : 'status'} onClose={() => setNotice(null)} variant="outlined"
      sx={{ width: { xs: 320, sm: 380 }, maxWidth: '100%', bgcolor: 'background.paper', color: 'text.primary',
        borderColor: 'divider', borderLeft: 4, borderLeftColor: `${severity}.main`, borderRadius: 2,
        boxShadow: '0 8px 32px rgba(0,0,0,0.14)', alignItems: 'flex-start', py: 1.25,
        '& .MuiAlert-icon': { color: `${severity}.main` },
        '& .MuiAlert-message': { maxHeight: 200, overflowY: 'auto', overflowWrap: 'anywhere', whiteSpace: 'pre-wrap', fontWeight: 400, lineHeight: 1.5 },
        '& .MuiAlert-action': { color: 'text.secondary' } }}>
      <AlertTitle sx={{ fontWeight: 700, mb: 0.5 }}>{heading}</AlertTitle>
      {notice?.message}
    </Alert>
  </Snackbar>
}
