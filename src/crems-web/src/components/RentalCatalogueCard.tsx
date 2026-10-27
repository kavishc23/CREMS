import ImageOutlined from '@mui/icons-material/ImageOutlined'
import ChevronLeftOutlined from '@mui/icons-material/ChevronLeftOutlined'
import ChevronRightOutlined from '@mui/icons-material/ChevronRightOutlined'
import { Box, Button, Card, CardContent, Chip, IconButton, Stack, Typography } from '@mui/material'
import { hireQuantity, measuredUnit } from '../utils/hirePricing'

type CatalogueAsset = {
  id: string; assetNumber: string; name: string; category: string | null; serviceName: string | null; type: string
  status: string; isAvailable: boolean; branchName: string; dailyRate: number; hireUnit?: string; useServiceHireRate?: boolean
  isBookableOnline?: boolean; requiresQuote: boolean; personnelRequirement: string; allowsQuoteRequests?: boolean
}

export function RentalCatalogueCard({ asset, photos, photoIndex, searched, startDate, endDate, onPhoto, onDetails, onChooseDates, onBook }: {
  asset: CatalogueAsset; photos: string[]; photoIndex: number; searched: boolean; startDate: string; endDate: string
  onPhoto: (direction: number) => void; onDetails: () => void; onChooseDates: () => void; onBook: (quote: boolean) => void
}) {
  const unit = asset.hireUnit ?? 'Day'
  const vehicle = ['Vehicle', 'PassengerVehicle', 'CommercialVehicle'].includes(asset.type)
  const days = hireQuantity('Day', startDate, endDate)
  return <Card data-testid="rental-catalogue-card" variant="outlined" sx={{ width: '100%', height: '100%', minWidth: 0, display: 'flex', flexDirection: 'column', borderRadius: 3, bgcolor: '#fff', borderTop: '4px solid', borderTopColor: 'secondary.main', overflowWrap: 'anywhere' }}>
    <CardContent sx={{ p: 2, minHeight: 'var(--catalogue-heading-height, 122px)', flexShrink: 0 }}>
      <Stack className="catalogue-heading-content" gap={.75} alignItems="flex-start">
        <Typography fontWeight={800} lineHeight={1.35}>{asset.name}</Typography>
        <Typography variant="caption" color="text.secondary">{asset.category ?? asset.serviceName ?? asset.type} · {asset.assetNumber}</Typography>
        {searched && <Chip label={asset.isAvailable ? 'Available for dates' : 'Unavailable for dates'} color={asset.isAvailable ? 'success' : 'default'} size="small" sx={{ maxWidth: '100%', height: 'auto', minHeight: 24, '& .MuiChip-label': { whiteSpace: 'normal', py: .25 } }} />}
      </Stack>
    </CardContent>
    <Box sx={{ height: 180, flexShrink: 0, position: 'relative', mx: 1.5, borderRadius: 2, bgcolor: '#f7f6ef' }}>
      {photos[photoIndex] ? <Box component="img" src={photos[photoIndex]} alt={`${asset.name}, photo ${photoIndex + 1} of ${photos.length}`} loading="lazy" sx={{ width: '100%', height: '100%', display: 'block', objectFit: 'contain', p: 1 }} /> : <Stack sx={{ height: '100%' }} alignItems="center" justifyContent="center"><ImageOutlined sx={{ fontSize: 40, color: 'grey.400' }} /><Typography variant="caption" color="text.secondary">Photo coming soon</Typography></Stack>}
      {photos.length > 1 && <><IconButton size="small" aria-label="Previous photo" onClick={() => onPhoto(-1)} sx={{ position: 'absolute', left: 4, top: '50%', transform: 'translateY(-50%)', bgcolor: 'rgba(255,255,255,.95)' }}><ChevronLeftOutlined /></IconButton><IconButton size="small" aria-label="Next photo" onClick={() => onPhoto(1)} sx={{ position: 'absolute', right: 4, top: '50%', transform: 'translateY(-50%)', bgcolor: 'rgba(255,255,255,.95)' }}><ChevronRightOutlined /></IconButton></>}
    </Box>
    <CardContent sx={{ p: 2, '&:last-child': { pb: 2 }, minHeight: 'var(--catalogue-footer-height, 190px)', flex: 1, display: 'flex', flexDirection: 'column', gap: 1 }}>
      <Stack className="catalogue-footer-content" spacing={1}>
        <Typography variant="caption" color="text.secondary">{asset.branchName}</Typography>
        <Box><Typography fontWeight={850}>{asset.useServiceHireRate ? 'See configured rate' : <>FJD {asset.dailyRate.toFixed(2)} <Typography component="span" variant="caption" color="text.secondary">/ {unit.toLowerCase()}</Typography></>}</Typography>
          {searched && !asset.useServiceHireRate && !measuredUnit(unit) && unit !== 'Hour' && <Typography variant="caption" color="text.secondary">FJD {(asset.dailyRate * hireQuantity(unit, startDate, endDate)).toFixed(2)} for {days} {days === 1 ? 'day' : 'days'}</Typography>}
        </Box>
        <Stack direction="row" gap={1} flexWrap="wrap" sx={{ '& .MuiButton-root': { minHeight: 40, whiteSpace: 'normal' } }}>
          {!searched ? <Button size="small" variant="contained" onClick={onChooseDates}>Choose dates</Button> : <>
            {vehicle && asset.isBookableOnline !== false && !asset.requiresQuote && asset.personnelRequirement !== 'Required' && <Button size="small" variant="contained" disabled={!asset.isAvailable} onClick={() => onBook(false)}>Book now</Button>}
            {asset.allowsQuoteRequests !== false && <Button size="small" variant={vehicle ? 'outlined' : 'contained'} disabled={!asset.isAvailable} onClick={() => onBook(true)}>Get quote</Button>}
          </>}
        </Stack>
      </Stack>
      <Button size="small" sx={{ mt: 'auto', alignSelf: 'flex-start', px: 0, minHeight: 36 }} onClick={onDetails}>View details</Button>
    </CardContent>
  </Card>
}
