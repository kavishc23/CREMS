import { useEffect, useState } from 'react'
import HistoryOutlined from '@mui/icons-material/HistoryOutlined'
import { Alert, Box, Button, Chip, CircularProgress, Dialog, DialogActions, DialogContent, DialogTitle, Stack, Typography } from '@mui/material'
import { api } from '../api/client'

type TimelineItem = { occurredAt: string; title: string; detail: string; actor: string; kind: string }
type Timeline = { reference: string; items: TimelineItem[] }
type RecordType = 'booking' | 'asset' | 'invoice' | 'customer' | 'maintenancejob'

export function RecordTimeline({ entityType, recordId, label = 'Activity timeline' }: { entityType: RecordType; recordId: string; label?: string }) {
  const [open, setOpen] = useState(false)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [timeline, setTimeline] = useState<Timeline | null>(null)
  useEffect(() => {
    if (!open) return
    const controller = new AbortController()
    void api.get<Timeline>(`/audit/timeline/${entityType}/${recordId}`, { signal: controller.signal })
      .then(response => setTimeline(response.data))
      .catch(() => { if (!controller.signal.aborted) setError('The activity timeline could not be loaded. Try again.') })
      .finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [open, entityType, recordId])
  return <>
    <Button size="small" startIcon={<HistoryOutlined />} onClick={() => {setLoading(true);setError('');setOpen(true)}}>{label}</Button>
    <Dialog open={open} onClose={() => setOpen(false)} fullWidth maxWidth="sm" aria-labelledby="record-timeline-title">
      <DialogTitle id="record-timeline-title">{timeline?.reference ?? 'Record'} · activity timeline</DialogTitle>
      <DialogContent dividers sx={{ maxHeight: '70vh' }}>
        {loading && <Box role="status" sx={{ p: 5, textAlign: 'center' }}><CircularProgress aria-label="Loading activity timeline" /></Box>}
        {error && <Alert severity="error">{error}</Alert>}
        {!loading && !error && timeline && <Stack component="ol" spacing={2} sx={{ listStyle: 'none', p: 0, m: 0 }}>
          {timeline.items.map((item, index) => <Box component="li" key={`${item.occurredAt}-${index}`} sx={{ pl: 2, borderLeft: '3px solid', borderColor: 'secondary.main' }}>
            <Stack direction="row" justifyContent="space-between" gap={1} alignItems="flex-start"><Typography fontWeight={750}>{item.title}</Typography><Chip size="small" label={item.kind} variant="outlined" /></Stack>
            <Typography variant="body2" sx={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{item.detail}</Typography>
            <Typography variant="caption" color="text.secondary">{new Date(item.occurredAt).toLocaleString('en-FJ')} · {item.actor}</Typography>
          </Box>)}
          {!timeline.items.length && <Typography color="text.secondary">No activity is recorded for this item.</Typography>}
        </Stack>}
      </DialogContent>
      <DialogActions><Button onClick={() => setOpen(false)}>Close</Button></DialogActions>
    </Dialog>
  </>
}
