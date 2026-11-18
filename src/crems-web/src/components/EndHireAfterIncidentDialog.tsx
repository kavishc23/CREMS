import { InlineIssueNavigation } from './InlineValidation'
import { FormError } from './FormError'
import { IncidentAttachments, incidentEvidence } from './IncidentAttachments'
import { SignaturePad } from './SignaturePad'
import { IncidentInspectionSummary } from './IncidentInspectionSummary'
import { useEffect, useState } from 'react'
import axios from 'axios'
import {
  Alert, Box, Button, Checkbox, CircularProgress, Dialog, DialogActions, DialogContent, DialogTitle,
  Divider, FormControlLabel, MenuItem, Stack, TextField, Typography,
} from '@mui/material'
import { api } from '../api/client'

export type IncidentOutcome = 'ContinueRental' | 'ReplaceAsset' | 'EndHire'
type Incident = {
  id: string; incidentNumber: string; type: string; occurredAt: string; description: string;
  location?: string | null; policeReference?: string | null; insuranceClaimNumber?: string | null;
  estimatedCost: number; evidenceJson?: string | null; reviewedAt?: string | null; status: string;
  outcome?: IncidentOutcome | null; inspectionJson?: string | null;
}
type Check = { code: string; label: string; safetyCritical: boolean }
type Context = { id: string; assetNumber: string; name: string; currentMeterReading: number | null;
  tracksMeter: boolean; tracksFuel: boolean; meterUnit: string | null; customerName: string; checks: Check[] }
type Replacement = { id: string; assetNumber: string; name: string; currentMeterReading: number | null;
  tracksMeter: boolean; tracksFuel: boolean; meterUnit: string | null; checklist: string[] }
type Props = { bookingId: string; bookingNumber: string; assetName: string | null; initialIncidentId?: string;
  onClose: () => void; onSaved: (outcome: IncidentOutcome) => void }
const localTime = () => { const now = new Date(); return new Date(now.getTime() - now.getTimezoneOffset() * 60000).toISOString().slice(0, 16) }
const message = (failure: unknown, fallback: string) => {
  const data = axios.isAxiosError(failure) ? failure.response?.data : undefined
  return data?.errors ? Object.values(data.errors as Record<string, string[]>).flat().join(' ') : data?.message ?? fallback
}

export function EndHireAfterIncidentDialog({ bookingId, bookingNumber, assetName, initialIncidentId, onClose, onSaved }: Props) {
  const [incidents, setIncidents] = useState<Incident[]>([])
  const [context, setContext] = useState<Context | null>(null)
  const [incidentId, setIncidentId] = useState('new')
  const [outcome, setOutcome] = useState<IncidentOutcome>('ContinueRental')
  const [incidentType, setIncidentType] = useState('Damage')
  const [occurredAt, setOccurredAt] = useState(localTime)
  const [description, setDescription] = useState('')
  const [location, setLocation] = useState('')
  const [policeReference, setPoliceReference] = useState('')
  const [insuranceClaimNumber, setInsuranceClaimNumber] = useState('')
  const [estimatedCost, setEstimatedCost] = useState('0')
  const [attachments, setAttachments] = useState<string[]>([])
  const [readingAttachments, setReadingAttachments] = useState(false)
  const [readingReplacement, setReadingReplacement] = useState(false)
  const reading = readingAttachments || readingReplacement
  const [checks, setChecks] = useState<Record<string, string>>({})
  const [meter, setMeter] = useState('')
  const [fuel, setFuel] = useState('')
  const [safe, setSafe] = useState(false)
  const [assetStatus, setAssetStatus] = useState('Maintenance')
  const [assessment, setAssessment] = useState('')
  const [custody, setCustody] = useState('')
  const [confirmed, setConfirmed] = useState(false)
  const [replacements, setReplacements] = useState<Replacement[]>([])
  const [finding, setFinding] = useState(false)
  const [replacementError, setReplacementError] = useState('')
  const [replacementId, setReplacementId] = useState('')
  const [replacementChecks, setReplacementChecks] = useState<string[]>([])
  const [replacementMeter, setReplacementMeter] = useState('')
  const [replacementFuel, setReplacementFuel] = useState('')
  const [replacementPhotos, setReplacementPhotos] = useState<string[]>([])
  const [signatureName, setSignatureName] = useState('')
  const [signature, setSignature] = useState('')
  const [replacementSafe, setReplacementSafe] = useState(false)
  const [accepted, setAccepted] = useState(false)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [attempted, setAttempted] = useState(false)
  const selectedIncident = incidents.find(i => i.id === incidentId)
  const originalAttachments = incidentEvidence(selectedIncident?.evidenceJson)
  const replacement = replacements.find(a => a.id === replacementId)

  useEffect(() => {
    let active = true
    void Promise.all([
      api.get<{ incidents: Incident[] }>(`/rentals/${bookingId}/lifecycle`),
      api.get<Context>(`/rentals/${bookingId}/lifecycle/incidents/context`),
    ]).then(([reports, current]) => {
      if (!active) return
      setIncidents(reports.data.incidents); setContext(current.data); setSignatureName(current.data.customerName)
      setMeter(current.data.currentMeterReading == null ? '' : String(current.data.currentMeterReading))
      setIncidentId(reports.data.incidents.find(i => i.id === initialIncidentId)?.id ??
        reports.data.incidents.find(i => !i.outcome && !['Resolved', 'Closed'].includes(i.status))?.id ?? 'new')
    }).catch(failure => { if (active) setError(message(failure, 'The shared incident records could not be loaded.')) })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [bookingId, initialIncidentId])

  useEffect(() => {
    const selected = incidents.find(i => i.id === incidentId)
    setIncidentType(selected?.type ?? 'Damage'); setOccurredAt(selected ? new Date(new Date(selected.occurredAt).getTime() - new Date(selected.occurredAt).getTimezoneOffset() * 60000).toISOString().slice(0, 16) : localTime())
    setDescription(selected?.description ?? ''); setLocation(selected?.location ?? '')
    setPoliceReference(selected?.policeReference ?? ''); setInsuranceClaimNumber(selected?.insuranceClaimNumber ?? '')
    setEstimatedCost(String(selected?.estimatedCost ?? 0)); setAttachments([]); setAssessment(''); setConfirmed(false); setSafe(false)
    setChecks({})
  }, [incidentId, incidents])

  useEffect(() => {
    if (outcome !== 'ReplaceAsset') return
    let active = true; setFinding(true); setReplacementError('')
    api.get<Replacement[]>(`/rentals/${bookingId}/lifecycle/incidents/replacement-assets`).then(({ data }) => {
      if (active) setReplacements(data)
    }).catch(failure => { if (active) setReplacementError(message(failure, 'Available replacements could not be loaded.')) })
      .finally(() => { if (active) setFinding(false) })
    return () => { active = false }
  }, [outcome, bookingId])

  function chooseReplacement(id: string) {
    const asset = replacements.find(a => a.id === id)
    setReplacementId(id); setReplacementChecks([]); setReplacementMeter(asset?.currentMeterReading == null ? '' : String(asset.currentMeterReading))
    setReplacementFuel(''); setReplacementPhotos([]); setSignature(''); setAccepted(false); setReplacementSafe(false)
  }
  const amountValid = estimatedCost.trim() !== '' && Number.isFinite(Number(estimatedCost)) && Number(estimatedCost) >= 0 &&
    Number(estimatedCost) <= 1000000 && Number(estimatedCost) === Number(Number(estimatedCost).toFixed(2))
  const numericIssue = (value: string, required: boolean, max: number, min = 0) =>
    value.trim() === '' ? required : !Number.isFinite(Number(value)) || Number(value) < min || Number(value) > max
  const safeChecks = context?.checks.filter(c => c.safetyCritical).every(c => checks[c.code] === 'Passed') ?? false
  const issues = [
    { target: 'incident-time', message: Date.parse(occurredAt) <= Date.now() ? '' : 'Enter an incident time no later than now.' },
    { target: 'incident-description', message: selectedIncident || description.trim().length >= 10 ? '' : 'Describe what happened in at least 10 characters.' },
    { target: 'incident-location', message: location.trim().length >= 3 ? '' : 'Enter the incident location in at least 3 characters.' },
    { target: 'incident-cost', message: amountValid ? '' : 'Enter an estimated cost in dollars and cents, from zero to FJD 1,000,000.' },
    { target: 'incident-checks', message: context?.checks.every(c => checks[c.code]) ? '' : 'Record every quick inspection result; use Not inspected if the asset cannot be inspected.' },
    { target: 'incident-meter', message: numericIssue(meter, Boolean(context?.tracksMeter && outcome !== 'EndHire'), 10000000, context?.currentMeterReading ?? 0) ? 'Enter a meter reading at or above the recorded reading.' : '' },
    { target: 'incident-fuel', message: numericIssue(fuel, Boolean(context?.tracksFuel && outcome !== 'EndHire'), 100) || fuel !== '' && !Number.isInteger(Number(fuel)) ? 'Enter a whole fuel percentage from zero to 100.' : '' },
    { target: 'incident-assessment', message: assessment.trim().length >= 10 ? '' : 'Record the staff assessment and customer arrangement in at least 10 characters.' },
    { target: 'incident-safe', message: outcome === 'ContinueRental' && (!safe || !safeChecks || incidentType === 'Theft') ? 'To continue, every safety check must pass and staff must confirm safe operation; otherwise choose replacement or closure.' : '' },
    { target: 'incident-custody', message: outcome !== 'ContinueRental' && custody.trim().length < 3 ? 'Record the damaged asset’s location or custody.' : '' },
    { target: 'incident-replacement', message: outcome === 'ReplaceAsset' && !replacement ? 'Select a suitable available replacement.' : '' },
    { target: 'replacement-checklist', message: outcome === 'ReplaceAsset' && replacement?.checklist.some(c => !replacementChecks.includes(c)) ? 'Complete every replacement inspection check.' : '' },
    { target: 'replacement-meter', message: outcome === 'ReplaceAsset' && replacement && numericIssue(replacementMeter, replacement.tracksMeter, 10000000, replacement.currentMeterReading ?? 0) ? 'Enter the replacement’s current meter reading.' : '' },
    { target: 'replacement-fuel', message: outcome === 'ReplaceAsset' && replacement && (numericIssue(replacementFuel, replacement.tracksFuel, 100) || replacementFuel !== '' && !Number.isInteger(Number(replacementFuel))) ? 'Enter the replacement’s whole fuel percentage from zero to 100.' : '' },
    { target: 'replacement-photos', message: outcome === 'ReplaceAsset' && !replacementPhotos.length ? 'Attach at least one replacement handover photo.' : '' },
    { target: 'replacement-signature', message: outcome === 'ReplaceAsset' && (!signature || signatureName.trim().length < 2 || !accepted || !replacementSafe) ? 'Record the customer’s name, signature and acceptance, and confirm the replacement is safe.' : '' },
    { target: 'incident-confirmed', message: confirmed ? '' : 'Confirm the selected rental outcome and customer arrangement.' },
  ]
  const finalized = selectedIncident?.outcome === 'ReplaceAsset' || selectedIncident?.outcome === 'EndHire' || ['Resolved', 'Closed'].includes(selectedIncident?.status ?? '')
  async function submit() {
    setAttempted(true)
    if (!context || saving || reading || finalized || issues.some(i => i.message)) return
    setSaving(true); setError('')
    try {
      const path = incidentId === 'new' ? '' : `/${incidentId}`
      await api.post(`/rentals/${bookingId}/lifecycle/incidents${path}/report`, {
        expectedAssetId: context.id, expectedReviewedAt: selectedIncident?.reviewedAt ?? null, outcome,
        type: incidentType, occurredAt: selectedIncident?.occurredAt ?? new Date(occurredAt).toISOString(),
        description: description.trim(), location: location.trim(), policeReference: policeReference.trim() || null,
        insuranceClaimNumber: insuranceClaimNumber.trim() || null, estimatedCost: Number(estimatedCost),
        evidenceDataUrls: [...originalAttachments, ...attachments], checks,
        meterReading: meter === '' ? null : Number(meter), fuelPercent: fuel === '' ? null : Number(fuel),
        assessment: assessment.trim(), safeToContinue: safe, confirmed,
        assetStatus: outcome === 'ContinueRental' ? null : assetStatus, custody: outcome === 'ContinueRental' ? null : custody.trim(),
        replacementAssetId: outcome === 'ReplaceAsset' ? replacementId : null,
        replacementHandover: outcome !== 'ReplaceAsset' ? null : {
          passedChecks: replacementChecks, meterReading: replacementMeter === '' ? null : Number(replacementMeter),
          fuelPercent: replacementFuel === '' ? null : Number(replacementFuel), photos: replacementPhotos,
          safeToOperate: replacementSafe, customerAccepted: accepted, customerName: signatureName.trim(), signatureDataUrl: signature,
        },
      })
      onSaved(outcome)
    } catch (failure) { setError(message(failure, 'The incident action could not be saved. Reload its current status and try again.')) }
    finally { setSaving(false) }
  }
  const fieldError = (target: string) => attempted && Boolean(issues.find(i => i.target === target)?.message)
  return <Dialog open onClose={saving ? undefined : onClose} fullWidth maxWidth="md" aria-labelledby="incident-report-title">
    <DialogTitle id="incident-report-title">Incident and damage report</DialogTitle>
    <DialogContent><Stack spacing={2.5}>
      <Typography fontWeight={750}>{bookingNumber} · {context?.assetNumber}{assetName ? ` · ${assetName}` : ''}</Typography>
      <Alert severity="info">Customer and staff reports share the same incident history, evidence and branch response. Choose the outcome after recording the inspection.</Alert>
      {loading && <CircularProgress size={24} aria-label="Loading incident report" />}
      {context && <>
        <TextField select label="Incident" value={incidentId} onChange={e => setIncidentId(e.target.value)}>
          <MenuItem value="new">Record a new incident</MenuItem>
          {incidents.map(i => <MenuItem key={i.id} value={i.id}>{i.incidentNumber} · {i.type} · {i.status}</MenuItem>)}
        </TextField>
        {finalized && <Alert severity="warning">This report has been finalized or refers to an earlier asset. Review it in the booking history, or record a new incident for the current asset.</Alert>}
        {selectedIncident && <IncidentInspectionSummary json={selectedIncident.inspectionJson} outcome={selectedIncident.outcome}/>}
        {selectedIncident && <Alert severity="info">The original report is retained. Add your assessment and evidence below; this updates the same incident rather than creating a duplicate.</Alert>}
        <Stack direction={{ xs: 'column', sm: 'row' }} gap={2}>
          <TextField select fullWidth label="Incident type" value={incidentType} disabled={Boolean(selectedIncident)} onChange={e => setIncidentType(e.target.value)}>
            {['Accident', 'Breakdown', 'Damage', 'Theft', 'TrafficOffence', 'Other'].map(type => <MenuItem key={type} value={type}>{type.replace(/([a-z])([A-Z])/g, '$1 $2')}</MenuItem>)}
          </TextField>
          <TextField fullWidth required type="datetime-local" id="incident-time" label="When the incident occurred" value={occurredAt}
            disabled={Boolean(selectedIncident)} error={fieldError('incident-time')} onChange={e => setOccurredAt(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} helperText="Use this device’s local time." />
        </Stack>
        <TextField required multiline minRows={2} id="incident-description" label={selectedIncident ? 'Original incident description' : 'What happened? Is everyone safe?'}
          value={description} error={fieldError('incident-description')} onChange={e => setDescription(e.target.value)} slotProps={{ input: { readOnly: Boolean(selectedIncident) }, htmlInput: { maxLength: 2000 } }} />
        <TextField required id="incident-location" label="Incident location" value={location} error={fieldError('incident-location')} onChange={e => setLocation(e.target.value)} slotProps={{ htmlInput: { maxLength: 500 } }} />
        <Stack direction={{ xs: 'column', sm: 'row' }} gap={2}>
          <TextField fullWidth label="Police reference (if applicable)" value={policeReference} onChange={e => setPoliceReference(e.target.value)} slotProps={{ htmlInput: { maxLength: 100 } }} />
          <TextField fullWidth label="Insurance claim reference (if applicable)" value={insuranceClaimNumber} onChange={e => setInsuranceClaimNumber(e.target.value)} slotProps={{ htmlInput: { maxLength: 100 } }} />
        </Stack>
        <TextField type="number" id="incident-cost" label="Estimated damage cost (FJD)" value={estimatedCost} error={fieldError('incident-cost')} onChange={e => setEstimatedCost(e.target.value)} helperText="An estimate only. No charge or bond deduction is posted by this report." />
        {originalAttachments.length > 0 && <Box><Typography fontWeight={700}>Existing incident evidence</Typography><IncidentAttachments value={originalAttachments} /></Box>}
        <Box><Typography fontWeight={700}>Photos, police report or supporting documents</Typography><IncidentAttachments value={attachments} onBusyChange={setReadingAttachments} onChange={files => {
          if (files.length + originalAttachments.length > 5) { setError('Keep the combined existing and new evidence to 5 files. Original evidence must remain attached.'); return }
          setAttachments(files)
        }} /></Box>
        <Divider />
        <Typography variant="h6">Quick condition inspection</Typography>
        <Box id="incident-checks" tabIndex={-1}><Stack spacing={1.5}>
          {context.checks.map(check => <TextField key={check.code} select required label={check.label} value={checks[check.code] ?? ''}
            error={attempted && !checks[check.code]} onChange={e => { setChecks(current => ({ ...current, [check.code]: e.target.value })); setSafe(false) }}>
            <MenuItem value="Passed">Checked — satisfactory</MenuItem><MenuItem value="Issue">Issue found</MenuItem><MenuItem value="NotInspected">Not inspected / cannot access</MenuItem>
          </TextField>)}
        </Stack></Box>
        <Stack direction={{ xs: 'column', sm: 'row' }} gap={2}>
          {context.tracksMeter && <TextField fullWidth type="number" id="incident-meter" label={`Current meter (${context.meterUnit ?? 'units'})`} value={meter} error={fieldError('incident-meter')} onChange={e => setMeter(e.target.value)} />}
          {context.tracksFuel && <TextField fullWidth type="number" id="incident-fuel" label="Current fuel (%)" value={fuel} error={fieldError('incident-fuel')} onChange={e => setFuel(e.target.value)} />}
        </Stack>
        <TextField required multiline minRows={3} id="incident-assessment" label="Staff damage assessment and customer arrangement" value={assessment} error={fieldError('incident-assessment')}
          onChange={e => setAssessment(e.target.value)} slotProps={{ htmlInput: { maxLength: 1000 } }} helperText="Record findings, any minor damage and the agreed next step. At least 10 characters." />
        <Divider />
        <TextField select label="Rental outcome" value={outcome} onChange={e => { setOutcome(e.target.value as IncidentOutcome); setConfirmed(false) }}>
          <MenuItem value="ContinueRental">Minor issue — keep rental active, normal return</MenuItem>
          <MenuItem value="ReplaceAsset">Provide a replacement — keep booking active</MenuItem>
          <MenuItem value="EndHire">Serious incident — end hire and proceed to settlement</MenuItem>
        </TextField>
        {outcome === 'ContinueRental' ? <>
          <Alert severity="info">The hire and bond stay active. Complete the usual return inspection and financial settlement when the rental finishes.</Alert>
          <FormControlLabel id="incident-safe" control={<Checkbox checked={safe} onChange={e => setSafe(e.target.checked)} />} label="All safety checks passed; I confirm this minor issue does not make the asset unsafe or unsuitable for continued hire" />
        </> : <>
          <Alert severity={outcome === 'EndHire' ? 'warning' : 'info'}>{outcome === 'EndHire'
            ? 'The hire ends now. The asset is blocked from further hire, and the existing incident continues to financial settlement and investigation.'
            : 'The same booking, agreed price, dates and bond continue. Inspect and sign for the replacement below; the damaged asset is blocked from further hire.'}</Alert>
          <TextField select required label="Damaged asset status" value={assetStatus} onChange={e => setAssetStatus(e.target.value)}>
            <MenuItem value="Maintenance">Needs repair</MenuItem><MenuItem value="OutOfService">Out of service</MenuItem>
          </TextField>
          <TextField required id="incident-custody" label="Damaged asset location or custody" value={custody} error={fieldError('incident-custody')} onChange={e => setCustody(e.target.value)} slotProps={{ htmlInput: { maxLength: 300 } }} />
        </>}
        {outcome === 'ReplaceAsset' && <>
          <Typography variant="h6">Replacement handover</Typography>
          {finding && <CircularProgress size={24} aria-label="Finding available replacements" />}
          {replacementError && <Alert severity="warning">{replacementError}</Alert>}
          {!finding && !replacementError && replacements.length === 0 && <Alert severity="warning">No compatible replacement is currently available for the remaining hire. Keep the report open and arrange recovery or incident closure.</Alert>}
          <TextField select required id="incident-replacement" label="Available replacement asset" value={replacementId} error={fieldError('incident-replacement')} onChange={e => chooseReplacement(e.target.value)}>
            <MenuItem value="">Choose an asset</MenuItem>{replacements.map(a => <MenuItem key={a.id} value={a.id}>{a.assetNumber} · {a.name}</MenuItem>)}
          </TextField>
          {replacement && <>
            <Box id="replacement-checklist" tabIndex={-1}><Stack>{replacement.checklist.map(check => <FormControlLabel key={check} label={check} control={<Checkbox checked={replacementChecks.includes(check)} onChange={e => setReplacementChecks(current => e.target.checked ? [...current, check] : current.filter(c => c !== check))} />} />)}</Stack></Box>
            <Stack direction={{ xs: 'column', sm: 'row' }} gap={2}>
              {replacement.tracksMeter && <TextField fullWidth required type="number" id="replacement-meter" label={`Replacement meter (${replacement.meterUnit ?? 'units'})`} value={replacementMeter} error={fieldError('replacement-meter')} onChange={e => setReplacementMeter(e.target.value)} />}
              {replacement.tracksFuel && <TextField fullWidth required type="number" id="replacement-fuel" label="Replacement fuel (%)" value={replacementFuel} error={fieldError('replacement-fuel')} onChange={e => setReplacementFuel(e.target.value)} />}
            </Stack>
            <Box id="replacement-photos" tabIndex={-1}><Typography fontWeight={700}>Replacement inspection photos</Typography><IncidentAttachments value={replacementPhotos} onBusyChange={setReadingReplacement} onChange={files => {
              if (files.some(f => !f.startsWith('data:image/'))) { setError('Use photographs for replacement handover evidence; attach police PDFs in the incident section.'); return }
              setReplacementPhotos(files)
            }} /></Box>
            <FormControlLabel control={<Checkbox checked={replacementSafe} onChange={e => setReplacementSafe(e.target.checked)} />} label="The replacement passed inspection and is safe and suitable for this hire" />
            <Box id="replacement-signature" tabIndex={-1}><Stack spacing={1.5}>
              <TextField required label="Customer acknowledgement name" value={signatureName} onChange={e => { setSignatureName(e.target.value); setAccepted(false) }} slotProps={{ htmlInput: { maxLength: 150 } }} />
              <SignaturePad value={signature} onChange={value => { setSignature(value); setAccepted(false) }} />
              <FormControlLabel control={<Checkbox checked={accepted} onChange={e => setAccepted(e.target.checked)} />} label="The customer accepts the replacement and recorded starting condition, with the agreed charges and bond unchanged" />
            </Stack></Box>
          </>}
        </>}
        <FormControlLabel id="incident-confirmed" control={<Checkbox checked={confirmed} onChange={e => setConfirmed(e.target.checked)} />}
          label={outcome === 'EndHire' ? 'I confirm this hire must end without the normal return and custody is recorded' : 'I confirm the inspection and selected customer arrangement are complete'} />
      </>}
    </Stack></DialogContent>
    {error && <FormError placement="footer">{error}</FormError>}
    <DialogActions sx={{ flexWrap: 'wrap', gap: 1 }}>
      <InlineIssueNavigation issues={issues} attempted={attempted} />
      <Button onClick={onClose} disabled={saving || reading}>Back</Button>
      <Button color={outcome === 'EndHire' ? 'error' : 'primary'} variant="contained" onClick={() => void submit()} disabled={!context || saving || reading || finalized || outcome === 'ReplaceAsset' && finding}>
        {saving ? 'Saving incident…' : outcome === 'EndHire' ? 'End hire & open settlement' : outcome === 'ReplaceAsset' ? 'Save report & hand over replacement' : 'Save report & keep rental active'}
      </Button>
    </DialogActions>
  </Dialog>
}
