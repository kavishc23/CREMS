import { useEffect, useState } from 'react'
import { Alert, Box, Button, Card, CardContent, Chip, CircularProgress, Dialog, DialogActions, DialogContent, DialogTitle, Stack, Typography } from '@mui/material'
import { api } from '../api/client'

export type Inspection = {
  id: string; type: 'Handover' | 'Return'; completedAt: string; completedByName: string | null
  conditionNotes: string | null; damageNotes: string | null; meterReading: number | null
  fuelLevelPercent: number | null; evidenceJson: string | null; signatureName: string | null; signatureDataUrl: string | null
  identificationVerified?: boolean; driverLicenceVerified?: boolean; paymentVerified?: boolean
}
const safeImage = (value: unknown): value is string => typeof value === 'string' && /^(data:image\/(jpeg|jpg|png|webp);base64,|https?:\/\/|\/)/i.test(value)
const strings = (value: unknown): string[] => Array.isArray(value) ? value.filter((item): item is string => typeof item === 'string') : []

export function RentalInspectionHistory({ bookingId, bookingNumber, assetName, onClose }: {
  bookingId: string; bookingNumber: string; assetName: string | null; onClose: () => void
}) {
  const [records, setRecords] = useState<Inspection[] | null>(null)
  const [error, setError] = useState('')
  const [attempt, setAttempt] = useState(0)
  const [photo, setPhoto] = useState<string | null>(null)
  useEffect(() => {
    let active = true
    api.get<{ inspections: Inspection[] }>(`/rentals/${bookingId}`).then(response => {
      if (active) setRecords(response.data.inspections)
    }).catch(() => { if (active) setError('Inspection records could not be loaded. Check your access or try again.') })
    return () => { active = false }
  }, [bookingId, attempt])
  return <Dialog open onClose={onClose} fullWidth maxWidth="lg" aria-labelledby="inspection-history-title">
    <DialogTitle id="inspection-history-title">Inspection history — {bookingNumber}</DialogTitle>
    <DialogContent dividers>
      <Typography color="text.secondary" mb={2}>{assetName}</Typography>
      {error ? <Alert severity="error" action={<Button color="inherit" onClick={() => { setError(''); setAttempt(value => value + 1) }}>Retry</Button>}>{error}</Alert> : records === null ? <Box textAlign="center" p={4}><CircularProgress aria-label="Loading inspections" /></Box> :
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: 2, alignItems: 'start' }}>
          {(['Handover', 'Return'] as const).map(type => <Stack spacing={2} key={type}>
            <Typography variant="h6">{type === 'Handover' ? 'Pre-hire' : 'Post-hire'}</Typography>
            {!records.some(record => record.type === type) && <Alert severity="info">No {type === 'Handover' ? 'pre-hire' : 'post-hire'} inspection has been saved.</Alert>}
            {records.filter(record => record.type === type).map(record => <InspectionRecord key={record.id} record={record} onPhoto={setPhoto} />)}
          </Stack>)}
        </Box>}
      <Dialog open={Boolean(photo)} onClose={() => setPhoto(null)} fullWidth maxWidth="lg" aria-label="Inspection photo preview">
        <DialogTitle>Inspection photo</DialogTitle><DialogContent>{photo && <Box component="img" src={photo} alt="Inspection evidence enlarged" sx={{ width: '100%', maxHeight: '75vh', objectFit: 'contain' }} />}</DialogContent>
        <DialogActions><Button onClick={() => setPhoto(null)}>Close photo</Button></DialogActions>
      </Dialog>
    </DialogContent>
    <DialogActions><Button onClick={onClose}>Close</Button></DialogActions>
  </Dialog>
}

export function InspectionRecord({ record, onPhoto }: { record: Inspection; onPhoto: (photo: string) => void }) {
  let evidence: Record<string, unknown> = {}
  let unreadable = false
  try {
    const parsed: unknown = JSON.parse(record.evidenceJson || '{}')
    if (Array.isArray(parsed)) evidence = { photos: parsed }
    else if (parsed && typeof parsed === 'object') evidence = parsed as Record<string, unknown>
  } catch { unreadable = true }
  const photos = strings(evidence.photos).filter(safeImage)
  const checklist = strings(evidence.checklist)
  const checklistResults = Array.isArray(evidence.checklistResults)
    ? evidence.checklistResults.filter((item): item is {label:string;passed:boolean|null} => Boolean(item) && typeof item === 'object' && typeof item.label === 'string' && (typeof item.passed === 'boolean' || item.passed === null))
    : checklist.map(label => ({label,passed:true}))
  const fields = [
    ['Recorded by', record.completedByName || 'Not recorded'],
    ['Recorded at', new Date(record.completedAt).toLocaleString('en-FJ')],
    ['Condition / result', record.conditionNotes || 'Not recorded'],
    ['Damage / faults', record.damageNotes || 'None recorded'],
    ['Meter reading', record.meterReading == null ? 'Not recorded' : String(record.meterReading)],
    ['Fuel / battery', record.fuelLevelPercent == null ? 'Not recorded' : `${record.fuelLevelPercent}%`],
    ['Accessories / quantities', typeof evidence.accessories === 'string' && evidence.accessories || 'Not recorded'],
    ['Customer acknowledgement', record.signatureName || 'Not recorded'],
    ...(record.identificationVerified === undefined ? [] : [['Identity verified', record.identificationVerified ? 'Yes' : 'No']]),
    ...(record.driverLicenceVerified === undefined ? [] : [['Driver licence verified', record.driverLicenceVerified ? 'Yes' : 'No']]),
    ...(record.paymentVerified === undefined ? [] : [['Payment verified', record.paymentVerified ? 'Yes' : 'No']]),
  ]
  return <Card variant="outlined"><CardContent><Stack spacing={2}>
    {fields.map(([label, value]) => <Box key={label}><Typography variant="caption" color="text.secondary">{label}</Typography><Typography sx={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{value}</Typography></Box>)}
    <Box><Typography fontWeight={700}>Checklist ({checklistResults.filter(item=>item.passed).length}/{checklistResults.length} passed)</Typography>{checklistResults.length ? <Stack spacing={1} mt={1}>{checklistResults.map((item,index)=><Stack key={index} direction="row" justifyContent="space-between" alignItems="center" gap={2}><Typography>{item.label}</Typography><Chip size="small" color={item.passed===true?'success':item.passed===false?'error':'default'} label={item.passed===true?'Passed':item.passed===false?'Failed':'Pending'}/></Stack>)}</Stack> : <Typography color="text.secondary">No checklist recorded.</Typography>}</Box>
    {safeImage(record.signatureDataUrl) && <Box component="img" src={record.signatureDataUrl} alt="Customer acknowledgement signature" sx={{ maxWidth: '100%', height: 100, objectFit: 'contain' }} />}
    <Typography fontWeight={700}>Photos ({photos.length})</Typography>
    {unreadable && <Alert severity="warning">The saved evidence could not be read. Other inspection details are shown above.</Alert>}
    {photos.length ? <Stack direction="row" flexWrap="wrap" gap={1}>{photos.map((src, index) => <Button key={index} onClick={() => onPhoto(src)} aria-label={`Enlarge inspection photo ${index + 1}`} sx={{ p: 0.5, border: 1, borderColor: 'divider' }}><Box component="img" src={src} alt={`Inspection photo ${index + 1}`} sx={{ width: 180, height: 135, objectFit: 'contain' }} /></Button>)}</Stack> : <Typography color="text.secondary">No photos recorded in this inspection.</Typography>}
  </Stack></CardContent></Card>
}
