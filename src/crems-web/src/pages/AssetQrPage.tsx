import { useEffect, useState } from 'react'
import axios from 'axios'
import QrCodeScannerOutlined from '@mui/icons-material/QrCodeScannerOutlined'
import CheckCircleOutlined from '@mui/icons-material/CheckCircleOutlined'
import { Alert, Box, Button, Card, CardContent, Chip, CircularProgress, Stack, TextField, Typography } from '@mui/material'
import { RentalsPage } from './RentalsPage'
import { PageHeader } from '../components/PageHeader'
import { QrCameraScanner } from '../components/QrCameraScanner'
import { api } from '../api/client'

type Context = {
  asset: { id: string; assetNumber: string; name: string; type: string; status: string; registrationNumber: string | null; serialNumber: string | null; branchName: string }
  action: 'CheckOut' | 'CheckIn' | 'ViewOnly'
  booking: null | { id: string; bookingNumber: string; status: string; customerName: string; depositRequired: number; amountPaid: number; agreementReady: boolean; driverVerified: boolean; startAt: string; endAt: string; assetCount: number }
  message: string | null
}

export function AssetQrPage() {
  const initialCode = new URLSearchParams(window.location.search).get('code') ?? ''
  const [code, setCode] = useState(initialCode); const [context, setContext] = useState<Context | null>(null); const [loading, setLoading] = useState(false); const [error, setError] = useState(''); const [success, setSuccess] = useState('')
  useEffect(() => { if (initialCode) void resolve(initialCode) }, []) // eslint-disable-line react-hooks/exhaustive-deps

  async function resolve(value = code) {
    if (!value.trim()) return; setLoading(true); setError(''); setSuccess(''); setContext(null)
    try { const response = await api.post<Context>('/asset-qr/resolve', { code: value.trim() }); setContext(response.data); setCode(value.trim()) }
    catch (requestError: unknown) { const message = axios.isAxiosError(requestError) ? requestError.response?.data?.message : null; setError(message ?? 'The QR code could not be read or you do not have access to this asset.') }
    finally { setLoading(false) }
  }


  return <Box sx={{ p: { xs: 2, sm: 3, lg: 4 }, maxWidth: 1000, mx: 'auto' }}><PageHeader icon={<QrCodeScannerOutlined />} title="Scan vehicle or equipment" subtitle="Scan the label to start the correct check-out or check-in workflow." />
    {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}{success && <Alert icon={<CheckCircleOutlined />} severity="success" sx={{ mb: 2 }}>{success}</Alert>}
    <Card variant="outlined"><CardContent><Stack spacing={2}><Stack direction={{ xs: 'column', sm: 'row' }} gap={1}><TextField fullWidth label="QR value or asset number" value={code} onChange={(e) => setCode(e.target.value)} placeholder="Example: VEH-SUV-1001" /><Button variant="contained" disabled={loading || !code.trim()} onClick={() => void resolve()}>{loading ? <CircularProgress size={22} /> : 'Find asset'}</Button></Stack><QrCameraScanner onScan={value=>{setCode(value);return resolve(value)}}/><Typography variant="caption" color="text.secondary">If a label is damaged, enter the asset number printed beneath the QR code.</Typography></Stack></CardContent></Card>
    {context && <Stack spacing={2} mt={2}><Card variant="outlined"><CardContent><Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" gap={2}><Box><Typography variant="overline" color="text.secondary">{context.asset.assetNumber}</Typography><Typography variant="h5" fontWeight={800}>{context.asset.name}</Typography><Typography color="text.secondary">{context.asset.branchName} · {context.asset.registrationNumber || context.asset.serialNumber || context.asset.type}</Typography></Box><Stack alignItems={{ sm: 'flex-end' }} gap={1}><Chip label={context.asset.status.replace(/([a-z])([A-Z])/g, '$1 $2')} /><Chip color={context.action === 'CheckOut' ? 'info' : context.action === 'CheckIn' ? 'warning' : 'default'} label={context.action === 'CheckOut' ? 'Ready for check-out' : context.action === 'CheckIn' ? 'Ready for check-in' : 'No transaction available'} /></Stack></Stack></CardContent></Card>
      {context.booking ? <Card variant="outlined"><CardContent><Typography variant="h6" fontWeight={800}>{context.booking.bookingNumber}</Typography><Typography>{context.booking.customerName} · {context.booking.assetCount} asset{context.booking.assetCount === 1 ? '' : 's'}</Typography><Typography variant="body2" color="text.secondary">{new Date(context.booking.startAt).toLocaleString('en-FJ')} – {new Date(context.booking.endAt).toLocaleString('en-FJ')}</Typography></CardContent></Card> : <Alert severity="info">{context.message}</Alert>}
      {context.booking && context.action !== 'ViewOnly' && <RentalsPage key={context.booking.id} initialSearch={context.booking.bookingNumber} initialQueue={context.action === 'CheckIn' ? (new Date(context.booking.endAt).getTime()<new Date(new Date().toLocaleDateString('en-CA',{timeZone:'Pacific/Fiji'})+'T00:00:00+12:00').getTime()?'Overdue':new Date(context.booking.endAt).toLocaleDateString('en-CA',{timeZone:'Pacific/Fiji'})===new Date().toLocaleDateString('en-CA',{timeZone:'Pacific/Fiji'})?'DueToday':'OnHire') : 'PickupToday'} />}
    </Stack>}
  </Box>
}
