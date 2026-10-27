import { hireQuantity, measuredUnit } from '../utils/hirePricing'
import { useEffect, useState } from 'react'
import CalendarMonthOutlined from '@mui/icons-material/CalendarMonthOutlined'
import CheckCircleOutlined from '@mui/icons-material/CheckCircleOutlined'
import ChevronLeftOutlined from '@mui/icons-material/ChevronLeftOutlined'
import ChevronRightOutlined from '@mui/icons-material/ChevronRightOutlined'
import {
  Alert, Box, Button, Chip, CircularProgress, Dialog, DialogActions, DialogContent,
  Divider, Grid, IconButton, Stack, Typography,
} from '@mui/material'
import { api } from '../api/client'

type AssetType='Vehicle'|'Equipment'|'PassengerVehicle'|'CommercialVehicle'|'HeavyEquipment'|'MaterialHandlingEquipment'|'PowerEquipment'|'LightEquipment'|'Scaffolding'|'PortableSanitation'|'WasteContainer'|'PropertyUnit'
import axios from 'axios'

type AssetSummary = {
  id: string
  name: string
  type: AssetType
  branchName: string
  hireRate?: { rate:number; unit:string }; hireUnit?:string
  dailyRate: number
  isAvailable: boolean
  requiresQuote?: boolean; isBookableOnline?: boolean; allowsQuoteRequests?: boolean
}

type AssetDetails = {
  id: string
  name: string
  type: string
  category: string | null
  hireRate?: { rate:number; unit:string }; hireUnit?:string
  dailyRate: number
  personnelRequirement: 'None' | 'Optional' | 'Required'
  manufacturer: string | null
  model: string | null
  modelYear: number | null
  division: { id: string; code: string; name: string } | null
  branch: { id: string; name: string; address: string | null; phone: string | null; email: string | null; pickupInstructions: string | null; deliveryCoverage: string | null }
  service: { name: string; description: string | null; requiresQuote: boolean; isBookableOnline: boolean; allowsQuoteRequests: boolean; defaultHireUnit: string; requiresDelivery: boolean; defaultDepositAmount: number; requiredDocuments: string[] } | null
  specifications: { name: string; value: string; unit: string | null }[]
  charges: { name: string; category: string; unit: string; defaultSellingRate: number; isRequired: boolean }[]
  isAvailable: boolean
  availabilityChecked: boolean
}

export function PublicAssetDetailDialog({ asset, imageUrls, startDate, endDate, open, onClose, onContinue, previewDetails, availabilityRequested = false }: {
  asset: AssetSummary | null
  imageUrls: string[]
  startDate: string
  endDate: string
  open: boolean
  onClose: () => void
  onContinue: (asset: AssetSummary) => void
  previewDetails?: AssetDetails
  availabilityRequested?: boolean
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
    setDetails(null)
    setError('')
    if (previewDetails) {
      setDetails(previewDetails)
      setLoading(false)
      return () => { active = false }
    }
    void api.get<AssetDetails>(`/public/assets/${asset.id}`, { params: availabilityRequested && startDate && endDate ? { startDate, endDate } : undefined })
      .then(response => { if (active) setDetails(response.data) })
      .catch((reason) => {
        if (!active) return
        const validationErrors = axios.isAxiosError(reason) ? reason.response?.data?.errors as Record<string, string[]> | undefined : undefined
        setError(validationErrors ? Object.values(validationErrors).flat().join(' ') : 'We could not load this rental. Please try again.')
      })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [asset, availabilityRequested, endDate, open, previewDetails, startDate])

  const unit=details?.hireRate?.unit??details?.hireUnit??'Day'
  const rate=details?.hireRate?.rate??details?.dailyRate??0
  const quantity=hireQuantity(unit,startDate,endDate)
  const available = details?.isAvailable ?? asset?.isAvailable ?? false
  const datesChecked = availabilityRequested && !!startDate && !!endDate && details?.availabilityChecked === true
  const visibleImageIndex = imageUrls.length ? imageIndex % imageUrls.length : 0

  return <Dialog disableScrollLock open={open} onClose={onClose} fullWidth maxWidth="lg" slotProps={{ paper: { sx: { m: 2, borderRadius: 3, overflow: 'hidden', maxHeight: 'calc(100dvh - 32px)' } } }}>
    <Box sx={{ bgcolor: '#111', color: 'white', px: 3.5, py: 2.25, position: 'relative', overflowWrap: 'anywhere' }}>
      <Typography variant="overline">{details?.division?.name ?? asset?.type} · {details?.category ?? 'Rental service'}</Typography>
      <Typography variant="h5" fontWeight={800}>{asset?.name}</Typography>
    </Box>
    <DialogContent sx={{ p: 0, overflowY: 'auto', overflowX: 'hidden' }}>
      {loading && <Box sx={{ minHeight: 260, display: 'grid', placeItems: 'center' }}><CircularProgress /></Box>}
      {error && <Alert severity="error">{error}</Alert>}
      {!loading && details && <Grid container>
        <Grid size={{ xs: 12, md: 6 }} sx={{ p: 3.5 }}><Stack spacing={2.5} width="100%">
          <Box sx={{ display: 'grid', gridTemplateColumns: { sm: 'minmax(220px, 1fr) auto' }, gap: 2, alignItems: 'start' }}>
            {datesChecked ? <Stack gap={1} alignItems="flex-start"><Chip color={available ? 'success' : 'default'} icon={available ? <CheckCircleOutlined /> : undefined} label={available ? 'Available for your dates' : 'Unavailable for your dates'} /><Chip icon={<CalendarMonthOutlined />} label={`${startDate} to ${endDate}`} /></Stack> : <Typography variant="body2" color="text.secondary">Choose your hire dates in the rental search to check availability.</Typography>}
            <Box sx={{ textAlign: { xs: 'left', sm: 'right' }, overflowWrap: 'anywhere' }}><Typography variant="caption" color="text.secondary">{datesChecked ? 'Estimated base hire' : 'Hire rate'}</Typography><Typography variant="h5" fontWeight={800}>FJD {(datesChecked ? rate * quantity : rate).toFixed(2)}</Typography><Typography variant="body2" color="text.secondary">{datesChecked ? `${quantity} ${unit.toLowerCase()} × FJD ${rate.toFixed(2)}${measuredUnit(unit) ? ' · Confirm quantity at checkout' : ''}` : `per ${unit.toLowerCase()}`}</Typography></Box>
          </Box>
          <Typography variant="h6" fontWeight={750} fontSize="1.15rem">Rental information</Typography><Divider /><Grid container columnSpacing={1.35} rowSpacing={.75}>
            {[['Category', details.category], ['Manufacturer', details.manufacturer], ['Model', details.model], ['Year', details.modelYear?.toString()], ['Hire unit', details.service?.defaultHireUnit?.toLowerCase()], ['Personnel', details.personnelRequirement === 'None' ? 'Not required' : `${details.personnelRequirement} trained personnel`]].filter(([, value]) => value).map(([label, value]) => <Grid key={label} size={{ xs: 6 }}><Typography variant="caption" color="text.secondary">{label}</Typography><Typography fontSize=".95rem" fontWeight={650}>{value}</Typography></Grid>)}
            {details.specifications.map(spec => <Grid key={spec.name} size={{ xs: 6 }}><Typography variant="caption" color="text.secondary">{spec.name}</Typography><Typography fontSize=".95rem" fontWeight={650}>{spec.value}{spec.unit ? ` ${spec.unit}` : ''}</Typography></Grid>)}
          </Grid></Stack></Grid>
        <Grid size={{ xs: 12, md: 6 }} sx={{ p: 3, display: 'grid', placeItems: 'center' }}>
          <Box sx={{ width: 'min(100%, calc(54vh * 1.3))', aspectRatio: '1.3', position: 'relative', overflow: 'hidden', borderRadius: 3, bgcolor: 'grey.200' }}>{asset && imageUrls.length ? <Box component="img" src={imageUrls[visibleImageIndex]} alt={`${asset.name} photo ${visibleImageIndex + 1}`} sx={{ width: '100%', height: '100%', objectFit: 'contain', display: 'block' }} /> : <Box sx={{ width: '100%', height: '100%', display: 'grid', placeItems: 'center' }}>Photo coming soon</Box>}{imageUrls.length > 1 && <><IconButton aria-label="Previous photo" onClick={() => setImageIndex(current => (current - 1 + imageUrls.length) % imageUrls.length)} sx={{ position: 'absolute', left: 8, top: '50%', transform: 'translateY(-50%)', bgcolor: 'white' }}><ChevronLeftOutlined /></IconButton><IconButton aria-label="Next photo" onClick={() => setImageIndex(current => (current + 1) % imageUrls.length)} sx={{ position: 'absolute', right: 8, top: '50%', transform: 'translateY(-50%)', bgcolor: 'white' }}><ChevronRightOutlined /></IconButton></>}</Box>
        </Grid>
      </Grid>}
    </DialogContent>
    <DialogActions sx={{ p: { xs: 2.5, md: 3 }, pt: 0, flexShrink: 0, bgcolor: 'background.paper' }}><Button onClick={onClose}>Close</Button><Box sx={{ flex: 1 }} />{datesChecked && <Button variant="contained" disabled={!available} onClick={() => asset && onContinue(asset)}>{!['Vehicle','PassengerVehicle','CommercialVehicle'].includes(details?.type ?? '') || details?.service?.requiresQuote || details?.service?.isBookableOnline === false ? 'Continue to quotation' : 'Continue to booking'}</Button>}</DialogActions>
  </Dialog>
}
