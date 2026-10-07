import { useEffect, useState } from 'react'
import { Alert, Button, CircularProgress, Dialog, DialogActions, DialogContent, DialogTitle, Stack, Typography } from '@mui/material'
import { api } from '../../api/client'
import { date, label } from './types'

type InspectionRecord = { id: string; assetNumber: string; stage: string; outcome: string; completedAt: string; completedByName: string; notes: string | null; meterReading: number | null; meterUnit: string | null; fuelPercent: number | null; responsesJson: string }
function responses(json: string): { name: string; result: string }[] {
  try {
    const items: { name: string; result: string }[] = []
    const walk = (value: unknown) => {
      if (Array.isArray(value)) { value.forEach(walk); return }
      if (!value || typeof value !== 'object') return
      const record = value as Record<string, unknown>
      const name = record.label ?? record.name ?? record.text ?? record.question
      const result = record.passed ?? record.result ?? record.value ?? record.answer ?? record.status
      if (typeof name === 'string' && result != null && typeof result !== 'object') items.push({ name, result: typeof result === 'boolean' ? result ? 'Passed' : 'Failed' : String(result) })
      Object.values(record).filter(x => x && typeof x === 'object').forEach(walk)
    }
    walk(JSON.parse(json)); return items
  } catch { return [] }
}
export function MaintenanceInspectionRecord({ id, onClose }: { id: string; onClose: () => void }) {
  const [record, setRecord] = useState<InspectionRecord | null>(null)
  const [error, setError] = useState(false)
  const [revision, setRevision] = useState(0)
  useEffect(() => {
    const controller = new AbortController()
    api.get<InspectionRecord>(`/maintenance-jobs/inspections/${id}`, { signal: controller.signal }).then(result => setRecord(result.data)).catch(() => { if (!controller.signal.aborted) setError(true) })
    return () => controller.abort()
  }, [id, revision])
  return <Dialog open fullWidth maxWidth="sm" onClose={onClose}><DialogTitle>Inspection record</DialogTitle><DialogContent><Stack spacing={2}>
    {error ? <Alert severity="error" action={<Button onClick={() => { setError(false); setRevision(x => x + 1) }}>Retry</Button>}>This inspection could not be loaded, or you no longer have access to it.</Alert> : !record ? <CircularProgress aria-label="Loading inspection record" /> : <>
      <Typography fontWeight={700}>{record.assetNumber} · {label(record.stage)} · {label(record.outcome)}</Typography><Typography>{date(record.completedAt, true)} · {record.completedByName}</Typography>
      <Typography>Meter: {record.meterReading ?? 'Not recorded'} {record.meterUnit} · Fuel: {record.fuelPercent == null ? 'Not recorded' : `${record.fuelPercent}%`}</Typography>
      <Typography sx={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{record.notes || 'No notes recorded.'}</Typography>
      {responses(record.responsesJson).map((item, index) => <Typography key={index}>{item.name}: {item.result}</Typography>)}
      {responses(record.responsesJson).length === 0 && <Typography color="text.secondary">No named checklist responses recorded.</Typography>}
      <Typography variant="caption" color="text.secondary" sx={{ overflowWrap: 'anywhere' }}>Inspection reference: {record.id}</Typography>
    </>}
  </Stack></DialogContent><DialogActions><Button onClick={onClose}>Close inspection record</Button></DialogActions></Dialog>
}
