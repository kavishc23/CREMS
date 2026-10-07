import { useEffect, useState } from 'react'
import axios from 'axios'
import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, MenuItem, Stack, TextField, Typography } from '@mui/material'
import { api } from '../api/client'

type Entry = { id: string; inventoryPartId: string; partNumber: string; quantity: number; unitCost?: number; batchKey: string; createdAt: string }
type Parts = { stock: { id: string; partNumber: string; name: string; available: number; unitCost?: number }[]; ledger: Entry[]; canManage: boolean; canIssue: boolean; canFinancial: boolean }

export function MaintenancePartsDialog({ job, onClose, onChanged }: { job: { id: string; jobNumber: string }; onClose: () => void; onChanged: () => Promise<void> }) {
  const [data, setData] = useState<Parts | null>(null)
  const [part, setPart] = useState('')
  const [issue, setIssue] = useState('')
  const [quantity, setQuantity] = useState('1')
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  useEffect(() => {
    let active = true
    api.get<Parts>(`/maintenance-jobs/${job.id}/parts`).then(response => { if (active) setData(response.data) })
      .catch(() => { if (active) setError('Unable to load stock and issue history.') })
    return () => { active = false }
  }, [job.id])
  const balances = (data?.ledger ?? []).filter(entry => entry.quantity > 0).filter((entry, index, entries) =>
    entries.findIndex(other => other.batchKey === entry.batchKey) === index)
    .map(entry => ({ ...entry, outstanding: data!.ledger.filter(other => other.batchKey === entry.batchKey).reduce((sum, other) => sum + other.quantity, 0) }))
    .filter(entry => entry.outstanding > 0)
  async function submit(returning: boolean) {
    setBusy(true); setError('')
    try {
      if (returning) await api.post(`/maintenance-jobs/${job.id}/parts/${issue}/return`, { quantity: Number(quantity), reason })
      else await api.post(`/business-operations/maintenance/${job.id}/parts`, { inventoryPartId: part, quantity: Number(quantity) })
      const response = await api.get<Parts>(`/maintenance-jobs/${job.id}/parts`)
      setData(response.data); setIssue(''); setPart(''); setQuantity('1'); setReason('')
      await onChanged()
    } catch (failure) {
      const response = axios.isAxiosError(failure) ? failure.response?.data : undefined
      setError(response?.message ?? (response?.errors ? Object.values(response.errors).flat().join(' ') : 'Unable to record stock movement. Refresh before retrying.'))
    } finally { setBusy(false) }
  }
  const validQuantity = Number.isInteger(Number(quantity)) && Number(quantity) > 0 && Number(quantity) <= 100000
  return <Dialog open fullWidth maxWidth="sm" onClose={() => !busy && onClose()}>
    <DialogTitle>Parts · {job.jobNumber}</DialogTitle>
    <DialogContent><Stack spacing={2} mt={1}>
      {error && <Alert severity="error">{error}</Alert>}
      {!data && !error && <Typography>Loading inventory…</Typography>}
      {data?.canManage && <>
        <TextField label="Quantity" type="number" value={quantity} disabled={busy} inputProps={{ min: 1, max: 100000, step: 1 }} onChange={event => setQuantity(event.target.value)} />
        {data.canIssue && <><TextField select label="Issue stock" value={part} disabled={busy} onChange={event => setPart(event.target.value)}>
          <MenuItem value="">Select stock</MenuItem>{data.stock.map(stock => <MenuItem key={stock.id} value={stock.id}>{stock.partNumber} · {stock.name} · {stock.available} available{data.canFinancial ? ` · FJD ${(stock.unitCost ?? 0).toFixed(2)}` : ''}</MenuItem>)}
        </TextField><Button disabled={busy || !part || !validQuantity} onClick={() => void submit(false)}>Issue parts</Button></>}
        <TextField select label="Return unused stock" value={issue} disabled={busy} onChange={event => setIssue(event.target.value)}>
          <MenuItem value="">Select issued stock</MenuItem>{balances.map(entry => <MenuItem key={entry.id} value={entry.id}>{entry.partNumber} · {entry.outstanding} outstanding{data.canFinancial ? ` · FJD ${(entry.unitCost ?? 0).toFixed(2)}` : ''}</MenuItem>)}
        </TextField>
        <TextField label="Return reason" value={reason} disabled={busy} inputProps={{ maxLength: 1000 }} onChange={event => setReason(event.target.value)} />
        <Button disabled={busy || !issue || !reason.trim() || !validQuantity} onClick={() => void submit(true)}>Return parts to stock</Button>
      </>}
      <Typography variant="subtitle2">Stock movement history</Typography>
      {data?.ledger.length === 0 && <Typography>No stock movements recorded.</Typography>}
      {data?.ledger.map(entry => <Typography key={entry.id} variant="body2">{entry.partNumber}: {entry.quantity > 0 ? 'Issued' : 'Returned'} {Math.abs(entry.quantity)}{data.canFinancial ? ` at FJD ${(entry.unitCost ?? 0).toFixed(2)}` : ''} · {new Date(entry.createdAt).toLocaleString()}</Typography>)}
    </Stack></DialogContent><DialogActions><Button disabled={busy} onClick={onClose}>Close</Button></DialogActions>
  </Dialog>
}
