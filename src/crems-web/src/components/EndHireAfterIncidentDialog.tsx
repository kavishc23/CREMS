import { InlineIssueNavigation } from './InlineValidation'
import { FormError } from './FormError'
import { useEffect, useState } from 'react'
import axios from 'axios'
import {
  Alert, Button, CircularProgress, Dialog, DialogActions, DialogContent, DialogTitle,
  FormControlLabel, Checkbox, MenuItem, Stack, TextField, Typography,
} from '@mui/material'
import { api } from '../api/client'

type Incident = { id: string; incidentNumber: string; type: string; occurredAt: string; description: string }
type Props = {
  bookingId: string
  bookingNumber: string
  assetName: string | null
  initialIncidentId?: string
  onClose: () => void
  onSaved: () => void
}

export function EndHireAfterIncidentDialog({ bookingId, bookingNumber, assetName, initialIncidentId, onClose, onSaved }: Props) {
  const [incidents, setIncidents] = useState<Incident[]>([])
  const [incidentId, setIncidentId] = useState('')
  const [incidentType, setIncidentType] = useState('Accident')
  const [occurredAt, setOccurredAt] = useState('')
  const [validationTime, setValidationTime] = useState(() => Date.now())
  const [assetStatus, setAssetStatus] = useState('')
  const [reason, setReason] = useState('')
  const [custody, setCustody] = useState('')
  const [confirmed, setConfirmed] = useState(false)
  const [loaded, setLoaded] = useState(false)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [attempted, setAttempted] = useState(false)

  useEffect(() => {
    let active = true
    api.get<{ incidents: Incident[] }>(`/rentals/${bookingId}/lifecycle`).then(({ data }) => {
      if (!active) return
      setIncidents(data.incidents)
      setIncidentId(data.incidents.find(incident => incident.id === initialIncidentId)?.id ?? data.incidents[0]?.id ?? 'new')
      setLoaded(true)
    }).catch(() => {
      if (active) setError('The incident records could not be loaded. Close this window and try again.')
    }).finally(() => {
      if (active) setLoading(false)
    })
    return () => { active = false }
  }, [bookingId, initialIncidentId])

  const selectedIncident = incidents.find(incident => incident.id === incidentId)
  const validTime = incidentId !== 'new' || Date.parse(occurredAt) <= validationTime
  const issues = [
    { target: 'end-hire-time', message: validTime ? '' : 'Enter when the incident occurred, using a time no later than now.' },
    { target: 'end-hire-asset-status', message: assetStatus ? '' : 'Select the asset’s status.' },
    { target: 'end-hire-reason', message: reason.trim().length >= 10 ? '' : 'Describe why the hire must end in at least 10 characters.' },
    { target: 'end-hire-custody', message: custody.trim().length >= 3 ? '' : 'Enter the asset location or custody in at least 3 characters.' },
    { target: 'end-hire-confirmed', message: confirmed ? '' : 'Confirm that the hire should end without a normal return.' },
  ]

  async function submit() {
    const now = Date.now()
    setValidationTime(now)
    setAttempted(true)
    if (!loaded || !incidentId || saving || issues.some(issue => issue.target !== 'end-hire-time' && issue.message) ||
      (incidentId === 'new' && !(Date.parse(occurredAt) <= now))) return
    setSaving(true)
    setError('')
    try {
      const incidentPath = incidentId === 'new' ? '' : `/${incidentId}`
      await api.post(`/rentals/${bookingId}/lifecycle/incidents${incidentPath}/end-hire`, {
        assetStatus, reason: reason.trim(), custody: custody.trim(),
        incidentType: incidentId === 'new' ? incidentType : null,
        occurredAt: incidentId === 'new' ? new Date(occurredAt).toISOString() : null,
      })
      onSaved()
    } catch (failure) {
      const data = axios.isAxiosError(failure) ? failure.response?.data : undefined
      const errors = data?.errors as Record<string, string[]> | undefined
      setError(errors ? Object.values(errors).flat().join(' ') : data?.message ?? 'The hire could not be ended. Refresh its status before trying again.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <Dialog open onClose={saving ? undefined : onClose} fullWidth maxWidth="sm" aria-labelledby="end-hire-after-incident-title">
      <DialogTitle id="end-hire-after-incident-title">End hire after serious incident</DialogTitle>
      <DialogContent>
        <Stack spacing={2}>
          <Typography fontWeight={700}>{bookingNumber}{assetName ? ` · ${assetName}` : ''}</Typography>
          <Alert severity="warning">This closes the hire immediately and blocks the asset from further hire. An operations follow-up task will track recovery, investigation and any outstanding finances.</Alert>

          {loading && <CircularProgress size={24} aria-label="Loading incidents" />}
          {loaded && <>
            <TextField select label="Incident" value={incidentId} onChange={event => { setIncidentId(event.target.value); setConfirmed(false) }}>
              {incidents.map(incident => <MenuItem key={incident.id} value={incident.id}>{incident.incidentNumber} · {incident.type}</MenuItem>)}
              <MenuItem value="new">Record a new incident</MenuItem>
            </TextField>
            {selectedIncident && <Stack spacing={0.5}>
              <Typography variant="caption" color="text.secondary">
                {new Date(selectedIncident.occurredAt).toLocaleString('en-FJ', { timeZone: 'Pacific/Fiji' })} Fiji time
              </Typography>
              <Typography variant="body2" sx={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{selectedIncident.description}</Typography>
            </Stack>}
            {incidentId === 'new' && <>
              <TextField select label="Incident type" value={incidentType} onChange={event => setIncidentType(event.target.value)}>
                {['Accident', 'Breakdown', 'Damage', 'Theft', 'Other'].map(type => <MenuItem key={type} value={type}>{type}</MenuItem>)}
              </TextField>
              <TextField type="datetime-local" required id="end-hire-time" label="When the incident occurred" value={occurredAt}
                onChange={event => { setOccurredAt(event.target.value); setValidationTime(Date.now()) }} slotProps={{ inputLabel: { shrink: true } }}
                helperText="Use the local time shown on this device." />
            </>}
            <TextField select required id="end-hire-asset-status" label="Asset status" value={assetStatus} onChange={event => { setAssetStatus(event.target.value); setConfirmed(false) }}>
              <MenuItem value="OutOfService">Out of service</MenuItem>
              <MenuItem value="Maintenance">Needs repair</MenuItem>
            </TextField>
            <TextField required multiline minRows={2} id="end-hire-reason" label="Why the hire must end" value={reason}
              onChange={event => setReason(event.target.value)} slotProps={{ htmlInput: { maxLength: 1000 } }}
              helperText="Staff only. Include why the normal return cannot be completed." />
            <TextField required id="end-hire-custody" label="Asset location or custody" value={custody}
              onChange={event => setCustody(event.target.value)} slotProps={{ htmlInput: { maxLength: 300 } }}
              helperText="For example, with the recovery provider, held by police, or location unknown." />
            <FormControlLabel control={<Checkbox checked={confirmed} onChange={event => setConfirmed(event.target.checked)} />}
              id="end-hire-confirmed" label="I confirm that this hire should end without the normal return." />
          </>}
        </Stack>
      </DialogContent>
      {error && <FormError placement="footer">{error}</FormError>}
      <DialogActions>
        <InlineIssueNavigation issues={issues} attempted={attempted}/>
        <Button onClick={onClose} disabled={saving}>Back</Button>
        <Button color="error" variant="contained" onClick={() => void submit()} disabled={!loaded || saving}>
          {saving ? 'Ending hire…' : 'End hire'}
        </Button>
      </DialogActions>
    </Dialog>
  )
}
