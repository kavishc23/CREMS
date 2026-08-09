import { useEffect, useRef, useState, type FormEvent } from 'react'
import axios from 'axios'
import QrCodeScannerOutlined from '@mui/icons-material/QrCodeScannerOutlined'
import CameraAltOutlined from '@mui/icons-material/CameraAltOutlined'
import StopCircleOutlined from '@mui/icons-material/StopCircleOutlined'
import CheckCircleOutlined from '@mui/icons-material/CheckCircleOutlined'
import { Alert, Box, Button, Card, CardContent, Checkbox, Chip, CircularProgress, FormControlLabel, Grid, Stack, TextField, Typography } from '@mui/material'
import { api } from '../api/client'

type Context = {
  asset: { id: string; assetNumber: string; name: string; type: string; status: string; registrationNumber: string | null; serialNumber: string | null; branchName: string }
  action: 'CheckOut' | 'CheckIn' | 'ViewOnly'
  booking: null | { id: string; bookingNumber: string; status: string; customerName: string; depositRequired: number; amountPaid: number; agreementReady: boolean; driverVerified: boolean; startAt: string; endAt: string; assetCount: number }
  message: string | null
}
type Form = { identificationVerified: boolean; driverLicenceVerified: boolean; paymentVerified: boolean; meterReading: string; fuelLevelPercent: string; conditionNotes: string; damageNotes: string; signatureName: string; lateFee: string; excessUsageCharge: string; refuellingCharge: string; cleaningCharge: string; damageCharge: string; additionalCharges: string; additionalChargesDescription: string }
const emptyForm: Form = { identificationVerified: false, driverLicenceVerified: false, paymentVerified: false, meterReading: '', fuelLevelPercent: '', conditionNotes: '', damageNotes: '', signatureName: '', lateFee: '', excessUsageCharge: '', refuellingCharge: '', cleaningCharge: '', damageCharge: '', additionalCharges: '', additionalChargesDescription: '' }

export function AssetQrPage() {
  const initialCode = new URLSearchParams(window.location.search).get('code') ?? ''
  const [code, setCode] = useState(initialCode); const [context, setContext] = useState<Context | null>(null); const [form, setForm] = useState<Form>(emptyForm); const [photos, setPhotos] = useState<string[]>([]); const [scanning, setScanning] = useState(false); const [loading, setLoading] = useState(false); const [saving, setSaving] = useState(false); const [error, setError] = useState(''); const [success, setSuccess] = useState('')
  const videoRef = useRef<HTMLVideoElement>(null); const streamRef = useRef<MediaStream | null>(null); const timerRef = useRef<number | null>(null)

  function stopScanner() { if (timerRef.current) window.clearInterval(timerRef.current); timerRef.current = null; streamRef.current?.getTracks().forEach((track) => track.stop()); streamRef.current = null; setScanning(false) }
  useEffect(() => () => stopScanner(), [])
  useEffect(() => { if (initialCode) void resolve(initialCode) }, []) // eslint-disable-line react-hooks/exhaustive-deps

  async function resolve(value = code) {
    if (!value.trim()) return; setLoading(true); setError(''); setSuccess(''); setContext(null)
    try { const response = await api.post<Context>('/asset-qr/resolve', { code: value.trim() }); setContext(response.data); setCode(value.trim()); stopScanner() }
    catch (requestError: unknown) { const message = axios.isAxiosError(requestError) ? requestError.response?.data?.message : null; setError(message ?? 'The QR code could not be read or you do not have access to this asset.') }
    finally { setLoading(false) }
  }

  async function startScanner() {
    setError(''); const Detector = (window as unknown as { BarcodeDetector?: new (options: { formats: string[] }) => { detect(source: HTMLVideoElement): Promise<{ rawValue: string }[]> } }).BarcodeDetector
    if (!Detector) { setError('Camera QR scanning is not supported by this browser. Use your phone camera to open the QR link, or enter the asset number below.'); return }
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: { ideal: 'environment' } }, audio: false }); streamRef.current = stream; setScanning(true)
      window.setTimeout(async () => { if (!videoRef.current) return; videoRef.current.srcObject = stream; await videoRef.current.play(); const detector = new Detector({ formats: ['qr_code'] }); timerRef.current = window.setInterval(async () => { try { const results = await detector.detect(videoRef.current!); if (results[0]?.rawValue) { setCode(results[0].rawValue); await resolve(results[0].rawValue) } } catch { /* keep scanning */ } }, 600) }, 0)
    } catch { setError('Camera access was not available. Check browser permission or enter the asset number manually.') }
  }

  async function addPhotos(files: FileList | null) {
    if (!files) return; const selected = Array.from(files).slice(0, 5 - photos.length)
    if (selected.some((file) => file.size > 5_000_000)) { setError('Each photo must be smaller than 5 MB.'); return }
    const encoded = await Promise.all(selected.map((file) => new Promise<string>((done, fail) => { const reader = new FileReader(); reader.onload = () => done(String(reader.result)); reader.onerror = () => fail(reader.error); reader.readAsDataURL(file) })))
    setPhotos((current) => [...current, ...encoded].slice(0, 5))
  }

  async function submit(event: FormEvent) {
    event.preventDefault(); if (!context?.booking) return; setSaving(true); setError('')
    const shared = { identificationVerified: form.identificationVerified, driverLicenceVerified: form.driverLicenceVerified, paymentVerified: form.paymentVerified, meterReading: form.meterReading ? Number(form.meterReading) : null, fuelLevelPercent: form.fuelLevelPercent ? Number(form.fuelLevelPercent) : null, conditionNotes: form.conditionNotes || null, damageNotes: form.damageNotes || null, signatureName: form.signatureName || null, evidenceDataUrls: photos, damageZones: [] }
    try {
      if (context.action === 'CheckOut') await api.post(`/rentals/${context.booking.id}/handover`, shared)
      else await api.post(`/rentals/${context.booking.id}/return`, { ...shared, additionalCharges: Number(form.additionalCharges || 0), additionalChargesDescription: form.additionalChargesDescription || null, lateFee: Number(form.lateFee || 0), excessUsageCharge: Number(form.excessUsageCharge || 0), refuellingCharge: Number(form.refuellingCharge || 0), cleaningCharge: Number(form.cleaningCharge || 0), damageCharge: Number(form.damageCharge || 0) })
      setSuccess(context.action === 'CheckOut' ? 'Check-out completed. The asset is now rented.' : 'Check-in completed. The rental and asset statuses were updated.'); setContext(null); setForm(emptyForm); setPhotos([])
    } catch (requestError: unknown) { const data = axios.isAxiosError(requestError) ? requestError.response?.data : undefined; const errors = data?.errors as Record<string, string[]> | undefined; setError(errors ? Object.values(errors).flat().join(' ') : 'The transaction could not be completed.') }
    finally { setSaving(false) }
  }

  const ready = context?.action === 'CheckIn' || (context?.booking?.agreementReady && context.booking.driverVerified && context.booking.amountPaid >= context.booking.depositRequired)
  return <Box sx={{ p: { xs: 2, sm: 3, lg: 4 }, maxWidth: 1000, mx: 'auto' }}><Stack direction="row" alignItems="center" gap={1.5} mb={.5}><QrCodeScannerOutlined color="secondary" fontSize="large" /><Typography variant="h4" fontWeight={800}>Scan vehicle or equipment</Typography></Stack><Typography color="text.secondary" mb={3}>Scan the label to start the correct check-out or check-in workflow.</Typography>
    {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}{success && <Alert icon={<CheckCircleOutlined />} severity="success" sx={{ mb: 2 }}>{success}</Alert>}
    <Card variant="outlined"><CardContent><Stack spacing={2}><Stack direction={{ xs: 'column', sm: 'row' }} gap={1}><TextField fullWidth label="QR value or asset number" value={code} onChange={(e) => setCode(e.target.value)} placeholder="Example: VEH-SUV-1001" /><Button variant="contained" disabled={loading || !code.trim()} onClick={() => void resolve()}>{loading ? <CircularProgress size={22} /> : 'Find asset'}</Button><Button variant="outlined" startIcon={scanning ? <StopCircleOutlined /> : <CameraAltOutlined />} onClick={() => scanning ? stopScanner() : void startScanner()}>{scanning ? 'Stop' : 'Use camera'}</Button></Stack>{scanning && <Box sx={{ bgcolor: '#111', borderRadius: 2, overflow: 'hidden', aspectRatio: '4/3', maxHeight: 440 }}><video ref={videoRef} muted playsInline style={{ width: '100%', height: '100%', objectFit: 'cover' }} /></Box>}<Typography variant="caption" color="text.secondary">If a label is damaged, enter the asset number printed beneath the QR code.</Typography></Stack></CardContent></Card>
    {context && <Stack spacing={2} mt={2}><Card variant="outlined"><CardContent><Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" gap={2}><Box><Typography variant="overline" color="text.secondary">{context.asset.assetNumber}</Typography><Typography variant="h5" fontWeight={800}>{context.asset.name}</Typography><Typography color="text.secondary">{context.asset.branchName} · {context.asset.registrationNumber || context.asset.serialNumber || context.asset.type}</Typography></Box><Stack alignItems={{ sm: 'flex-end' }} gap={1}><Chip label={context.asset.status.replace(/([a-z])([A-Z])/g, '$1 $2')} /><Chip color={context.action === 'CheckOut' ? 'info' : context.action === 'CheckIn' ? 'warning' : 'default'} label={context.action === 'CheckOut' ? 'Ready for check-out' : context.action === 'CheckIn' ? 'Ready for check-in' : 'No transaction available'} /></Stack></Stack></CardContent></Card>
      {context.booking ? <Card variant="outlined"><CardContent><Typography variant="h6" fontWeight={800}>{context.booking.bookingNumber}</Typography><Typography>{context.booking.customerName} · {context.booking.assetCount} asset{context.booking.assetCount === 1 ? '' : 's'}</Typography><Typography variant="body2" color="text.secondary">{new Date(context.booking.startAt).toLocaleString('en-FJ')} – {new Date(context.booking.endAt).toLocaleString('en-FJ')}</Typography>{context.action === 'CheckOut' && <Grid container spacing={1} mt={1}><Grid size={{ xs: 12, sm: 4 }}><Alert severity={context.booking.agreementReady ? 'success' : 'error'}>Agreement {context.booking.agreementReady ? 'signed' : 'required'}</Alert></Grid><Grid size={{ xs: 12, sm: 4 }}><Alert severity={context.booking.driverVerified ? 'success' : 'error'}>Driver {context.booking.driverVerified ? 'verified' : 'not verified'}</Alert></Grid><Grid size={{ xs: 12, sm: 4 }}><Alert severity={context.booking.amountPaid >= context.booking.depositRequired ? 'success' : 'warning'}>Paid ${context.booking.amountPaid.toFixed(2)}</Alert></Grid></Grid>}</CardContent></Card> : <Alert severity="info">{context.message}</Alert>}
      {context.booking && context.action !== 'ViewOnly' && <Card variant="outlined"><CardContent><Box component="form" onSubmit={submit}><Typography variant="h6" fontWeight={800} mb={2}>{context.action === 'CheckOut' ? 'Pre-rental inspection and handover' : 'Return inspection and charges'}</Typography><Stack spacing={2}><Stack direction={{ xs: 'column', sm: 'row' }}><FormControlLabel control={<Checkbox checked={form.identificationVerified} onChange={(e) => setForm({ ...form, identificationVerified: e.target.checked })} />} label="Customer identity checked" /><FormControlLabel control={<Checkbox checked={form.driverLicenceVerified} onChange={(e) => setForm({ ...form, driverLicenceVerified: e.target.checked })} />} label="Driver licence checked" />{context.action === 'CheckOut' && <FormControlLabel control={<Checkbox checked={form.paymentVerified} onChange={(e) => setForm({ ...form, paymentVerified: e.target.checked })} />} label="Payment checked" />}</Stack><Grid container spacing={2}><Grid size={{ xs: 12, sm: 6 }}><TextField fullWidth required type="number" label="Odometer / hour meter" value={form.meterReading} onChange={(e) => setForm({ ...form, meterReading: e.target.value })} /></Grid><Grid size={{ xs: 12, sm: 6 }}><TextField fullWidth required type="number" label="Fuel / charge level (%)" inputProps={{ min: 0, max: 100 }} value={form.fuelLevelPercent} onChange={(e) => setForm({ ...form, fuelLevelPercent: e.target.value })} /></Grid></Grid><TextField required multiline minRows={2} label="Condition and accessories" value={form.conditionNotes} onChange={(e) => setForm({ ...form, conditionNotes: e.target.value })} /><TextField multiline minRows={2} label="Damage or faults" value={form.damageNotes} onChange={(e) => setForm({ ...form, damageNotes: e.target.value })} /><Button component="label" variant="outlined" startIcon={<CameraAltOutlined />}>Add condition photos ({photos.length}/5)<input hidden multiple accept="image/*" capture="environment" type="file" onChange={(e) => void addPhotos(e.target.files)} /></Button>
        {context.action === 'CheckIn' && <><Typography fontWeight={700}>Additional charges (FJD)</Typography><Grid container spacing={2}>{([['Late fee','lateFee'],['Extra kilometres / hours','excessUsageCharge'],['Fuel','refuellingCharge'],['Cleaning','cleaningCharge'],['Damage','damageCharge'],['Other','additionalCharges']] as const).map(([label, key]) => <Grid key={key} size={{ xs: 6, sm: 4 }}><TextField fullWidth type="number" label={label} value={form[key]} onChange={(e) => setForm({ ...form, [key]: e.target.value })} /></Grid>)}</Grid><TextField label="Other charge explanation" value={form.additionalChargesDescription} onChange={(e) => setForm({ ...form, additionalChargesDescription: e.target.value })} /></>}
        <TextField required label={context.action === 'CheckOut' ? 'Customer handover confirmation name' : 'Agent return confirmation name'} value={form.signatureName} onChange={(e) => setForm({ ...form, signatureName: e.target.value })} /><Alert severity="info">Completing this action updates the whole booking. It contains {context.booking.assetCount} asset{context.booking.assetCount === 1 ? '' : 's'}.</Alert><Button type="submit" size="large" variant="contained" disabled={saving || !ready || !form.identificationVerified || !form.driverLicenceVerified || (context.action === 'CheckOut' && !form.paymentVerified)}>{saving ? 'Saving…' : context.action === 'CheckOut' ? 'Confirm check-out' : 'Confirm check-in'}</Button></Stack></Box></CardContent></Card>}
    </Stack>}
  </Box>
}
