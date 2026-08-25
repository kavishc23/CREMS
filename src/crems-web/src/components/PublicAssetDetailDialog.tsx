import { useEffect, useState } from 'react'
import CalendarMonthOutlined from '@mui/icons-material/CalendarMonthOutlined'
import CheckCircleOutlined from '@mui/icons-material/CheckCircleOutlined'
import CloseOutlined from '@mui/icons-material/CloseOutlined'
import DescriptionOutlined from '@mui/icons-material/DescriptionOutlined'
import LocalShippingOutlined from '@mui/icons-material/LocalShippingOutlined'
import LocationOnOutlined from '@mui/icons-material/LocationOnOutlined'
import VerifiedOutlined from '@mui/icons-material/VerifiedOutlined'
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

  return <Dialog open={open} onClose={onClose} fullWidth maxWidth="lg" fullScreen={false}>
    <Box sx={{ position: 'relative', height: { xs: 260, md: 430 }, bgcolor: '#f1f1ed', overflow: 'hidden' }}>
      {asset && imageUrls.length > 0 ? <Box component="img" src={imageUrls[visibleImageIndex]} alt={`${asset.name} photo ${visibleImageIndex + 1}`} sx={{ width: '100%', height: '100%', objectFit: 'contain', display: 'block' }} /> : <Stack alignItems="center" justifyContent="center" sx={{height:'100%',bgcolor:'#e8e8e2'}}><ImageOutlined sx={{fontSize:64,color:'grey.500'}}/><Typography color="text.secondary">Photo coming soon</Typography></Stack>}
      {imageUrls.length > 1 && <><IconButton aria-label="Previous photo" onClick={() => setImageIndex(current => (current - 1 + imageUrls.length) % imageUrls.length)} sx={{position:'absolute',left:16,top:'50%',transform:'translateY(-50%)',bgcolor:'rgba(255,255,255,.92)','&:hover':{bgcolor:'white'}}}><ChevronLeftOutlined/></IconButton><IconButton aria-label="Next photo" onClick={() => setImageIndex(current => (current + 1) % imageUrls.length)} sx={{position:'absolute',right:16,top:'50%',transform:'translateY(-50%)',bgcolor:'rgba(255,255,255,.92)','&:hover':{bgcolor:'white'}}}><ChevronRightOutlined/></IconButton><Chip label={`${visibleImageIndex + 1} / ${imageUrls.length}`} size="small" sx={{position:'absolute',right:18,bottom:18,bgcolor:'rgba(255,255,255,.92)'}}/></>}
      <IconButton aria-label="Close rental details" onClick={onClose} sx={{ position: 'absolute', top: 16, right: 16, bgcolor: 'white', '&:hover': { bgcolor: '#f5f5f5' } }}><CloseOutlined /></IconButton>
    </Box>
    <Box sx={{ bgcolor: '#111', color: 'white', px: { xs: 2.5, md: 4 }, py: { xs: 2, md: 2.5 } }}>
      <Typography variant="overline">{details?.division?.name ?? asset?.type} · {details?.category ?? 'Rental service'}</Typography>
      <Typography variant="h3" fontWeight={800} sx={{ fontSize: { xs: '1.8rem', md: '2.6rem' } }}>{asset?.name}</Typography>
    </Box>
    <DialogContent sx={{ p: { xs: 2.5, md: 4 } }}>
      {loading && <Box sx={{ minHeight: 260, display: 'grid', placeItems: 'center' }}><CircularProgress /></Box>}
      {error && <Alert severity="error">{error}</Alert>}
      {!loading && details && <Stack spacing={4}>
        <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" gap={2}>
          <Box><Stack direction="row" gap={1} flexWrap="wrap"><Chip color={available ? 'success' : 'default'} icon={available ? <CheckCircleOutlined /> : undefined} label={available ? 'Available for your dates' : 'Unavailable for your dates'} /><Chip icon={<CalendarMonthOutlined />} label={`${startDate} to ${endDate}`} /></Stack><Typography color="text.secondary" mt={1.5}>{details.service?.description ?? 'A maintained Carpenters rental asset supplied through the operating branch.'}</Typography></Box>
          <Box sx={{ minWidth: 210, textAlign: { md: 'right' } }}><Typography variant="caption" color="text.secondary">Estimated base hire</Typography><Typography variant="h4" fontWeight={800}>FJD {(details.dailyRate * days).toFixed(2)}</Typography><Typography variant="body2" color="text.secondary">{days} {days === 1 ? 'day' : 'days'} × FJD {details.dailyRate.toFixed(2)}</Typography></Box>
        </Stack>
        <Divider />
        <Grid container spacing={4}>
          <Grid size={{ xs: 12, md: 7 }}><Typography variant="h6" fontWeight={750}>Rental information</Typography><Grid container spacing={2} mt={.5}>
            {[['Category', details.category], ['Manufacturer', details.manufacturer], ['Model', details.model], ['Year', details.modelYear?.toString()], ['Hire unit', details.service?.defaultHireUnit?.toLowerCase()], ['Personnel', details.personnelRequirement === 'None' ? 'Not required' : `${details.personnelRequirement} trained personnel`]].filter(([, value]) => value).map(([label, value]) => <Grid key={label} size={{ xs: 6 }}><Typography variant="caption" color="text.secondary">{label}</Typography><Typography fontWeight={650}>{value}</Typography></Grid>)}
            {details.specifications.map(spec => <Grid key={spec.name} size={{ xs: 6 }}><Typography variant="caption" color="text.secondary">{spec.name}</Typography><Typography fontWeight={650}>{spec.value}{spec.unit ? ` ${spec.unit}` : ''}</Typography></Grid>)}
          </Grid></Grid>
          <Grid size={{ xs: 12, md: 5 }}><Box sx={{ bgcolor: '#f4f4ef', borderRadius: 3, p: 3 }}><Stack direction="row" gap={1}><LocationOnOutlined /><Typography variant="h6" fontWeight={750}>{details.branch.name}</Typography></Stack><Typography color="text.secondary" mt={1}>{details.branch.address ?? 'Contact the branch for pickup directions.'}</Typography>{details.branch.pickupInstructions && <Typography mt={2}>{details.branch.pickupInstructions}</Typography>}<Stack direction="row" gap={1} mt={2} flexWrap="wrap">{details.branch.phone && <Button size="small" href={`tel:${details.branch.phone}`}>Call branch</Button>}{details.branch.email && <Button size="small" href={`mailto:${details.branch.email}`}>Email branch</Button>}</Stack></Box></Grid>
        </Grid>
        <Grid container spacing={3}>
          <Grid size={{ xs: 12, md: 6 }}><Stack direction="row" gap={1} alignItems="center"><DescriptionOutlined color="action" /><Typography variant="h6" fontWeight={750}>What you may need</Typography></Stack>{details.service?.requiredDocuments?.length ? <Stack component="ul" sx={{ pl: 3, mb: 0 }}>{details.service.requiredDocuments.map(document => <Typography component="li" key={document} color="text.secondary">{document}</Typography>)}</Stack> : <Typography color="text.secondary" mt={1}>Identification and any service-specific documents will be confirmed by the branch.</Typography>}</Grid>
          <Grid size={{ xs: 12, md: 6 }}><Stack direction="row" gap={1} alignItems="center"><LocalShippingOutlined color="action" /><Typography variant="h6" fontWeight={750}>Additional services</Typography></Stack>{details.charges.length ? <Stack spacing={1} mt={1}>{details.charges.slice(0, 5).map(charge => <Stack key={`${charge.category}-${charge.name}`} direction="row" justifyContent="space-between"><Typography color="text.secondary">{charge.name}{charge.isRequired ? ' (required)' : ''}</Typography><Typography fontWeight={650}>{charge.defaultSellingRate > 0 ? `FJD ${charge.defaultSellingRate.toFixed(2)} / ${charge.unit.toLowerCase()}` : 'Quoted'}</Typography></Stack>)}</Stack> : <Typography color="text.secondary" mt={1}>Delivery, operator and other requirements are confirmed during quotation.</Typography>}</Grid>
        </Grid>
        <Alert severity="info" icon={<VerifiedOutlined />}>The displayed amount is an estimate. VAT, deposit, delivery, fuel, operator and excess-usage charges are confirmed before you accept the quotation or booking.</Alert>
      </Stack>}
    </DialogContent>
    <DialogActions sx={{ p: { xs: 2.5, md: 3 }, pt: 0 }}><Button onClick={onClose}>Close</Button><Box sx={{ flex: 1 }} /><Button variant="contained" disabled={!details || !available} onClick={() => asset && onContinue(asset)}>{details?.service?.requiresQuote || asset?.type === 'Equipment' ? 'Continue to request quote' : 'Continue to booking'}</Button></DialogActions>
  </Dialog>
}
