import { useEffect, useRef, useState } from 'react'
import axios from 'axios'
import QrCodeScannerOutlined from '@mui/icons-material/QrCodeScannerOutlined'
import CameraAltOutlined from '@mui/icons-material/CameraAltOutlined'
import StopCircleOutlined from '@mui/icons-material/StopCircleOutlined'
import CheckCircleOutlined from '@mui/icons-material/CheckCircleOutlined'
import { Alert, Box, Button, Card, CardContent, Chip, CircularProgress, Stack, TextField, Typography } from '@mui/material'
import { RentalsPage } from './RentalsPage'
import { api } from '../api/client'

type Context = {
  asset: { id: string; assetNumber: string; name: string; type: string; status: string; registrationNumber: string | null; serialNumber: string | null; branchName: string }
  action: 'CheckOut' | 'CheckIn' | 'ViewOnly'
  booking: null | { id: string; bookingNumber: string; status: string; customerName: string; depositRequired: number; amountPaid: number; agreementReady: boolean; driverVerified: boolean; startAt: string; endAt: string; assetCount: number }
  message: string | null
}

export function AssetQrPage() {
  const initialCode = new URLSearchParams(window.location.search).get('code') ?? ''
  const [code, setCode] = useState(initialCode); const [context, setContext] = useState<Context | null>(null); const [scanning, setScanning] = useState(false); const [loading, setLoading] = useState(false); const [error, setError] = useState(''); const [success, setSuccess] = useState('')
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

  return <Box sx={{ p: { xs: 2, sm: 3, lg: 4 }, maxWidth: 1000, mx: 'auto' }}><Stack direction="row" alignItems="center" gap={1.5} mb={.5}><QrCodeScannerOutlined color="secondary" fontSize="large" /><Typography variant="h4" fontWeight={800}>Scan vehicle or equipment</Typography></Stack><Typography color="text.secondary" mb={3}>Scan the label to start the correct check-out or check-in workflow.</Typography>
    {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}{success && <Alert icon={<CheckCircleOutlined />} severity="success" sx={{ mb: 2 }}>{success}</Alert>}
    <Card variant="outlined"><CardContent><Stack spacing={2}><Stack direction={{ xs: 'column', sm: 'row' }} gap={1}><TextField fullWidth label="QR value or asset number" value={code} onChange={(e) => setCode(e.target.value)} placeholder="Example: VEH-SUV-1001" /><Button variant="contained" disabled={loading || !code.trim()} onClick={() => void resolve()}>{loading ? <CircularProgress size={22} /> : 'Find asset'}</Button><Button variant="outlined" startIcon={scanning ? <StopCircleOutlined /> : <CameraAltOutlined />} onClick={() => scanning ? stopScanner() : void startScanner()}>{scanning ? 'Stop' : 'Use camera'}</Button></Stack>{scanning && <Box sx={{ bgcolor: '#111', borderRadius: 2, overflow: 'hidden', aspectRatio: '4/3', maxHeight: 440 }}><video ref={videoRef} muted playsInline style={{ width: '100%', height: '100%', objectFit: 'cover' }} /></Box>}<Typography variant="caption" color="text.secondary">If a label is damaged, enter the asset number printed beneath the QR code.</Typography></Stack></CardContent></Card>
    {context && <Stack spacing={2} mt={2}><Card variant="outlined"><CardContent><Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" gap={2}><Box><Typography variant="overline" color="text.secondary">{context.asset.assetNumber}</Typography><Typography variant="h5" fontWeight={800}>{context.asset.name}</Typography><Typography color="text.secondary">{context.asset.branchName} · {context.asset.registrationNumber || context.asset.serialNumber || context.asset.type}</Typography></Box><Stack alignItems={{ sm: 'flex-end' }} gap={1}><Chip label={context.asset.status.replace(/([a-z])([A-Z])/g, '$1 $2')} /><Chip color={context.action === 'CheckOut' ? 'info' : context.action === 'CheckIn' ? 'warning' : 'default'} label={context.action === 'CheckOut' ? 'Ready for check-out' : context.action === 'CheckIn' ? 'Ready for check-in' : 'No transaction available'} /></Stack></Stack></CardContent></Card>
      {context.booking ? <Card variant="outlined"><CardContent><Typography variant="h6" fontWeight={800}>{context.booking.bookingNumber}</Typography><Typography>{context.booking.customerName} · {context.booking.assetCount} asset{context.booking.assetCount === 1 ? '' : 's'}</Typography><Typography variant="body2" color="text.secondary">{new Date(context.booking.startAt).toLocaleString('en-FJ')} – {new Date(context.booking.endAt).toLocaleString('en-FJ')}</Typography></CardContent></Card> : <Alert severity="info">{context.message}</Alert>}
      {context.booking && context.action !== 'ViewOnly' && <RentalsPage key={context.booking.id} initialSearch={context.booking.bookingNumber} initialQueue={context.action === 'CheckIn' ? (new Date(context.booking.endAt).getTime()<new Date(new Date().toLocaleDateString('en-CA',{timeZone:'Pacific/Fiji'})+'T00:00:00+12:00').getTime()?'Overdue':new Date(context.booking.endAt).toLocaleDateString('en-CA',{timeZone:'Pacific/Fiji'})===new Date().toLocaleDateString('en-CA',{timeZone:'Pacific/Fiji'})?'DueToday':'OnHire') : 'PickupToday'} />}
    </Stack>}
  </Box>
}
