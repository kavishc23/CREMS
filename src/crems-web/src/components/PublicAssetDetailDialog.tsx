import { useEffect, useState } from 'react'
import CalendarMonthOutlined from '@mui/icons-material/CalendarMonthOutlined'
import CheckCircleOutlined from '@mui/icons-material/CheckCircleOutlined'
import CloseOutlined from '@mui/icons-material/CloseOutlined'
import ChevronLeftOutlined from '@mui/icons-material/ChevronLeftOutlined'
import ChevronRightOutlined from '@mui/icons-material/ChevronRightOutlined'
import ImageOutlined from '@mui/icons-material/ImageOutlined'
import {
  Alert, Box, Button, Chip, CircularProgress, Dialog, DialogActions, DialogContent,
  Divider, Grid, IconButton, Stack, Typography,
} from '@mui/material'
import { api } from '../api/client'
import axios from 'axios'

type AssetSummary = {
  id: string
  name: string
  type: 'Vehicle' | 'Equipment'
  branchName: string
  dailyRate: number
  isAvailable: boolean
}

type AssetDetails = {
  id: string
  name: string
  type: string
  category: string | null
  dailyRate: number
  personnelRequirement: 'None' | 'Optional' | 'Required'
  manufacturer: string | null
  model: string | null
  modelYear: number | null
  division: { id: string; code: string; name: string } | null
  branch: { id: string; name: string; address: string | null; phone: string | null; email: string | null; pickupInstructions: string | null; deliveryCoverage: string | null }
  service: { name: string; description: string | null; requiresQuote: boolean; defaultHireUnit: string; requiresDelivery: boolean; defaultDepositAmount: number; requiredDocuments: string[] } | null
  specifications: { name: string; value: string; unit: string | null }[]
  charges: { name: string; category: string; unit: string; defaultSellingRate: number; isRequired: boolean }[]
  isAvailable: boolean
  availabilityChecked: boolean
}

export function PublicAssetDetailDialog({ asset, imageUrls, startDate, endDate, open, onClose, onContinue }: {
  asset: AssetSummary | null
  imageUrls: string[]
  startDate: string
  endDate: string
  open: boolean
  onClose: () => void
  onContinue: (asset: AssetSummary) => void
}) {
  const [details, setDetails] = useState<AssetDetails | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [imageIndex, setImageIndex] = useState(0)

  useEffect(() => {
    if (!open || !asset) return
    let active = true
    // Opening a catalogue item synchronizes the dialog with its public API details.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setLoading(true)
    setError('')
    void api.get<AssetDetails>(`/public/assets/${asset.id}`, { params: { startDate, endDate } })
      .then(response => { if (active) setDetails(response.data) })
      .catch((reason) => {
        if (!active) return
        const validationErrors = axios.isAxiosError(reason) ? reason.response?.data?.errors as Record<string, string[]> | undefined : undefined
        setError(validationErrors ? Object.values(validationErrors).flat().join(' ') : 'We could not load this rental. Please try again.')
      })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [asset, endDate, open, startDate])

  const days = Math.max(1, Math.ceil((new Date(`${endDate}T00:00:00`).getTime() - new Date(`${startDate}T00:00:00`).getTime()) / 86400000))
  const available = details?.isAvailable ?? asset?.isAvailable ?? false
  const visibleImageIndex = imageUrls.length ? imageIndex % imageUrls.length : 0

  return <Dialog open={open} onClose={onClose} fullWidth maxWidth="lg" slotProps={{ paper: { sx: { m: 2, borderRadius: 3, overflow: 'hidden', maxHeight: 'calc(100dvh - 32px)' } } }}>
    <Box sx={{ display: 'none' }}>
      {asset && imageUrls.length > 0 ? <Box component="img" src={imageUrls[visibleImageIndex]} alt={`${asset.name} photo ${visibleImageIndex + 1}`} sx={{ width: '100%', height: '100%', objectFit: 'contain', display: 'block' }} /> : <Stack alignItems="center" justifyContent="center" sx={{height:'100%',bgcolor:'#e8e8e2'}}><ImageOutlined sx={{fontSize:64,color:'grey.500'}}/><Typography color="text.secondary">Photo coming soon</Typography></Stack>}
      {imageUrls.length > 1 && <><IconButton aria-label="Previous photo" onClick={() => setImageIndex(current => (current - 1 + imageUrls.length) % imageUrls.length)} sx={{position:'absolute',left:16,top:'50%',transform:'translateY(-50%)',bgcolor:'rgba(255,255,255,.92)','&:hover':{bgcolor:'white'}}}><ChevronLeftOutlined/></IconButton><IconButton aria-label="Next photo" onClick={() => setImageIndex(current => (current + 1) % imageUrls.length)} sx={{position:'absolute',right:16,top:'50%',transform:'translateY(-50%)',bgcolor:'rgba(255,255,255,.92)','&:hover':{bgcolor:'white'}}}><ChevronRightOutlined/></IconButton><Chip label={`${visibleImageIndex + 1} / ${imageUrls.length}`} size="small" sx={{position:'absolute',right:18,bottom:18,bgcolor:'rgba(255,255,255,.92)'}}/></>}
      <IconButton aria-label="Close rental details" onClick={onClose} sx={{ position: 'absolute', top: 16, right: 16, bgcolor: 'white', '&:hover': { bgcolor: '#f5f5f5' } }}><CloseOutlined /></IconButton>
    </Box>
    <Box sx={{ bgcolor: '#111', color: 'white', px: 3.5, py: 2.25, position: 'relative' }}>
      <Typography variant="overline">{details?.division?.name ?? asset?.type} · {details?.category ?? 'Rental service'}</Typography>
      <Typography variant="h5" fontWeight={800}>{asset?.name}</Typography>
    </Box>
    <DialogContent sx={{ p: 0, overflowY: { xs: 'auto', md: 'hidden' }, overflowX: 'hidden' }}>
      {loading && <Box sx={{ minHeight: 260, display: 'grid', placeItems: 'center' }}><CircularProgress /></Box>}
      {error && <Alert severity="error">{error}</Alert>}
      {!loading && details && <Grid container>
        <Grid size={{ xs: 12, md: 6 }} sx={{ p: 3.5 }}><Stack spacing={2.5} width="100%">
          <Box sx={{ display: 'grid', gridTemplateColumns: { sm: 'minmax(220px, 1fr) auto' }, gap: 2, alignItems: 'start' }}>
            <Stack gap={1} alignItems="flex-start"><Chip color={available ? 'success' : 'default'} icon={available ? <CheckCircleOutlined /> : undefined} label={available ? 'Available for your dates' : 'Unavailable for your dates'} /><Chip icon={<CalendarMonthOutlined />} label={`${startDate} to ${endDate}`} /></Stack>
            <Box sx={{ textAlign: { xs: 'left', sm: 'right' } }}><Typography variant="caption" color="text.secondary">Estimated base hire</Typography><Typography variant="h5" fontWeight={800} whiteSpace="nowrap">FJD {(details.dailyRate * days).toFixed(2)}</Typography><Typography variant="body2" color="text.secondary" whiteSpace="nowrap">{days} {days === 1 ? 'day' : 'days'} × FJD {details.dailyRate.toFixed(2)}</Typography></Box>
          </Box>
          <Typography variant="h6" fontWeight={750} fontSize="1.15rem">Rental information</Typography><Divider /><Grid container columnSpacing={1.35} rowSpacing={.75}>
            {[['Category', details.category], ['Manufacturer', details.manufacturer], ['Model', details.model], ['Year', details.modelYear?.toString()], ['Hire unit', details.service?.defaultHireUnit?.toLowerCase()], ['Personnel', details.personnelRequirement === 'None' ? 'Not required' : `${details.personnelRequirement} trained personnel`]].filter(([, value]) => value).map(([label, value]) => <Grid key={label} size={{ xs: 6 }}><Typography variant="caption" color="text.secondary">{label}</Typography><Typography fontSize=".95rem" fontWeight={650}>{value}</Typography></Grid>)}
            {details.specifications.map(spec => <Grid key={spec.name} size={{ xs: 6 }}><Typography variant="caption" color="text.secondary">{spec.name}</Typography><Typography fontSize=".95rem" fontWeight={650}>{spec.value}{spec.unit ? ` ${spec.unit}` : ''}</Typography></Grid>)}
          </Grid></Stack></Grid>
        <Grid size={{ xs: 12, md: 6 }} sx={{ p: 3, display: 'grid', placeItems: 'center' }}>
          <Box sx={{ width: 'min(100%, calc(54vh * 1.3))', aspectRatio: '1.3', position: 'relative', overflow: 'hidden', borderRadius: 3, bgcolor: 'grey.200' }}>{asset && imageUrls.length ? <Box component="img" src={imageUrls[visibleImageIndex]} alt={`${asset.name} photo ${visibleImageIndex + 1}`} sx={{ width: '100%', height: '100%', objectFit: 'cover', display: 'block' }} /> : <Box sx={{ width: '100%', height: '100%', display: 'grid', placeItems: 'center' }}>Photo coming soon</Box>}{imageUrls.length > 1 && <><IconButton aria-label="Previous photo" onClick={() => setImageIndex(current => (current - 1 + imageUrls.length) % imageUrls.length)} sx={{ position: 'absolute', left: 8, top: '50%', transform: 'translateY(-50%)', bgcolor: 'white' }}><ChevronLeftOutlined /></IconButton><IconButton aria-label="Next photo" onClick={() => setImageIndex(current => (current + 1) % imageUrls.length)} sx={{ position: 'absolute', right: 8, top: '50%', transform: 'translateY(-50%)', bgcolor: 'white' }}><ChevronRightOutlined /></IconButton></>}</Box>
        </Grid>
      </Grid>}
    </DialogContent>
    <DialogActions sx={{ p: { xs: 2.5, md: 3 }, pt: 0, flexShrink: 0, bgcolor: 'background.paper' }}><Button onClick={onClose}>Close</Button><Box sx={{ flex: 1 }} /><Button variant="contained" disabled={!details || !available} onClick={() => asset && onContinue(asset)}>{details?.service?.requiresQuote || asset?.type === 'Equipment' ? 'Continue to request quote' : 'Continue to booking'}</Button></DialogActions>
  </Dialog>
}
