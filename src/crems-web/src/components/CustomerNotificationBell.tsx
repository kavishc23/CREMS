import { useCallback, useEffect, useState } from 'react'
import NotificationsOutlined from '@mui/icons-material/NotificationsOutlined'
import CloseOutlined from '@mui/icons-material/CloseOutlined'
import { Alert, Badge, Box, Button, CircularProgress, Divider, IconButton, List, ListItem, Popover, Stack, Typography } from '@mui/material'
import { api } from '../api/client'

type Notice = { id: string; title: string; message: string; createdAt: string; isRead: boolean; isExpired?: boolean; url?: string | null }
type Inbox = { items: Notice[]; unreadCount: number }

/** Customer-only recent updates. Staff filters, settings and composition stay in NotificationBell. */
export function CustomerNotificationBell(_props: { onOpenBookings: () => void }) {
  void _props
  const [anchor, setAnchor] = useState<HTMLElement | null>(null)
  const [inbox, setInbox] = useState<Inbox>({ items: [], unreadCount: 0 })
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const load = useCallback(async (signal?: AbortSignal) => {
    try {
      const { data } = await api.get<Inbox>('/customer-account/notifications', { signal })
      if (!signal?.aborted) { setInbox({ items: data.items ?? [], unreadCount: data.unreadCount ?? 0 }); setError('') }
    } catch { if (!signal?.aborted) setError('Updates are unavailable. Please try again.') }
  }, [])
  useEffect(() => {
    const controller = new AbortController()
    const refresh = () => { if (document.visibilityState !== 'hidden') void load(controller.signal) }
    refresh()
    const interval = window.setInterval(refresh, 60_000)
    window.addEventListener('focus', refresh)
    window.addEventListener('crems:data-changed', refresh)
    return () => { controller.abort(); clearInterval(interval); window.removeEventListener('focus', refresh); window.removeEventListener('crems:data-changed', refresh) }
  }, [load])
  async function read(notice?: Notice) {
    setLoading(true)
    try {
      await api.post('/customer-account/notifications/read', { ids: notice ? [notice.id] : [], all: !notice })
      setInbox(current => ({ items: current.items.map(item => !notice || item.id === notice.id ? { ...item, isRead: true } : item), unreadCount: notice ? Math.max(0, current.unreadCount - (notice.isRead ? 0 : 1)) : 0 }))
      setError('')
    } catch { setError('Could not mark this update as read. Please try again.') }
    finally { setLoading(false) }
  }
  const recent = inbox.items.filter(item => !item.isExpired).slice(0, 10)
  return <>
    <IconButton color="inherit" aria-label={`Notifications, ${inbox.unreadCount} unread`} aria-haspopup="dialog" aria-expanded={Boolean(anchor)} onClick={event => { setAnchor(event.currentTarget); void load() }}>
      <Badge color="error" badgeContent={inbox.unreadCount} max={99}><NotificationsOutlined /></Badge>
    </IconButton>
    <Popover open={Boolean(anchor)} anchorEl={anchor} onClose={() => setAnchor(null)} anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }} transformOrigin={{ vertical: 'top', horizontal: 'right' }} disableScrollLock slotProps={{ paper: { role: 'dialog', 'aria-label': 'Your notifications', sx: { width: 420, display: 'flex', flexDirection: 'column', overflow: 'hidden', maxWidth: 'calc(100vw - 24px)', mt: 1.25, borderRadius: 2, border: '1px solid #deded7', maxHeight: 'min(640px, calc(100dvh - 100px))' } } }}>
      <Stack direction="row" alignItems="center" justifyContent="space-between" sx={{ px: 2, py: 1.5, flexShrink: 0, borderTop: '4px solid #ffed00' }}><Box><Typography fontWeight={800}>Your notifications</Typography><Typography variant="caption" color="text.secondary">Updates about your rentals</Typography></Box><IconButton size="small" aria-label="Close notifications" onClick={() => setAnchor(null)}><CloseOutlined fontSize="small" /></IconButton></Stack>
      <Divider />
      <Stack direction="row" alignItems="center" justifyContent="space-between" sx={{ px: 1.5, py: .75, borderBottom: 1, borderColor: 'divider', flexShrink: 0 }}><Button size="small" disabled={!inbox.unreadCount || loading} onClick={() => void read()}>Mark all read</Button><Typography variant="caption" color="text.secondary">Latest 10 updates</Typography>{loading && <CircularProgress size={16} />}</Stack>
      {error && <Alert severity="info" action={<Button size="small" onClick={() => void load()}>Retry</Button>}>{error}</Alert>}
      <List disablePadding sx={{overflowY: 'auto', minHeight: 0}}>{recent.map(notice => <ListItem key={notice.id} sx={{ alignItems: 'flex-start', gap: 1.25, px: 2, py: 1.25, borderBottom: '1px solid #eeeeea', bgcolor: 'white', '&:hover': { bgcolor: '#f7f7f4' } }}><Box sx={{ mt: .8, width: 7, height: 7, flexShrink: 0, borderRadius: '50%', bgcolor: notice.isRead ? 'transparent' : '#b2a600' }} /><Box sx={{ minWidth: 0 }}><Typography variant="body2" fontWeight={notice.isRead ? 600 : 800}>{notice.title}</Typography><Typography variant="body2" color="text.secondary" sx={{ mt: .5, overflowWrap: 'anywhere', lineHeight: 1.4 }}>{notice.message.replace(/((?:BK|QUO|RA)-\d{4}-)([a-f0-9]{32})\b/gi, (_, prefix: string, id: string) => prefix + id.slice(0, 8) + '…')}</Typography><Typography variant="caption" color="text.secondary" display="block" mt={.5}>{new Date(notice.createdAt).toLocaleDateString(undefined, { day: 'numeric', month: 'short' })}</Typography>{!notice.isRead && <Button size="small" disabled={loading} onClick={() => void read(notice)} sx={{mt:.5,px:0,minWidth:0,fontSize:'.75rem'}}>Mark as read</Button>}</Box></ListItem>)}</List>
      {!recent.length && !error && <Box p={3} textAlign="center"><NotificationsOutlined sx={{ color: 'text.disabled', fontSize: 32 }} /><Typography variant="body2" color="text.secondary" mt={1}>You're all caught up.</Typography></Box>}

    </Popover>
  </>
}
