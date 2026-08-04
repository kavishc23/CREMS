import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import axios from 'axios'
import ArrowForwardOutlined from '@mui/icons-material/ArrowForwardOutlined'
import BuildOutlined from '@mui/icons-material/BuildOutlined'
import CalendarMonthOutlined from '@mui/icons-material/CalendarMonthOutlined'
import CheckCircleOutlined from '@mui/icons-material/CheckCircleOutlined'
import DirectionsCarOutlined from '@mui/icons-material/DirectionsCarOutlined'
import LocationOnOutlined from '@mui/icons-material/LocationOnOutlined'
import MenuOutlined from '@mui/icons-material/MenuOutlined'
import PhoneOutlined from '@mui/icons-material/PhoneOutlined'
import SearchOutlined from '@mui/icons-material/SearchOutlined'
import {
  Alert, AppBar, Avatar, Box, Button, Card, CardContent, Chip, CircularProgress,
  Checkbox, Container, Dialog, DialogActions, DialogContent, DialogTitle, Divider,
  Drawer, FormControl, FormControlLabel, Grid, IconButton, InputLabel, Link,
  MenuItem, Select, Stack, Step, StepLabel, Stepper, TextField, Toolbar, Typography,
} from '@mui/material'
import { api } from '../api/client'

type AssetType = 'Vehicle' | 'Equipment'
type PublicAsset = {
  id: string; assetNumber: string; name: string; type: AssetType; status: string
  branchId: string; branchName: string; dailyRate: number; isAvailable: boolean
}
type Branch = { id: string; name: string; address: string | null; phone: string | null }
type BookingForm = {
  fullName: string; customerType: 'Individual' | 'Business'; companyName: string
  email: string; phone: string; address: string; identificationNumber: string
  purpose: string; message: string
}
type BookingTracking = {
  reference: string; status: string; message: string; assetName: string | null
  branchName: string; startAt: string | null; endAt: string | null; submittedAt: string
}
const emptyBooking: BookingForm = {
  fullName: '', customerType: 'Individual', companyName: '', email: '', phone: '',
  address: '', identificationNumber: '', purpose: '', message: '',
}

function dateInputValue(offsetDays: number) {
  const date = new Date(); date.setDate(date.getDate() + offsetDays)
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
}

export function PublicRentalPage({ onStaffLogin }: { onStaffLogin: () => void }) {
  const [assets, setAssets] = useState<PublicAsset[]>([])
  const [branches, setBranches] = useState<Branch[]>([])
  const [branchId, setBranchId] = useState('')
  const [type, setType] = useState<AssetType | ''>('')
  const [startDate, setStartDate] = useState(dateInputValue(1))
  const [endDate, setEndDate] = useState(dateInputValue(3))
  const [loading, setLoading] = useState(true)
  const [searched, setSearched] = useState(false)
  const [mobileMenu, setMobileMenu] = useState(false)
  const [selected, setSelected] = useState<PublicAsset | null>(null)
  const [booking, setBooking] = useState<BookingForm>(emptyBooking)
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState('')
  const [reference, setReference] = useState('')
  const [bookingStep, setBookingStep] = useState(0)
  const [termsAccepted, setTermsAccepted] = useState(false)
  const [trackingReference, setTrackingReference] = useState('')
  const [tracking, setTracking] = useState<BookingTracking | null>(null)
  const [trackingError, setTrackingError] = useState('')
  const [trackingLoading, setTrackingLoading] = useState(false)

  const loadBranches = useCallback(async () => {
    try { setBranches((await api.get<Branch[]>('/public/branches')).data) } catch { /* Contacts remain optional. */ }
  }, [])
  const searchAssets = useCallback(async () => {
    setLoading(true); setError('')
    try {
      const response = await api.get<PublicAsset[]>('/public/assets', { params: {
        branchId: branchId || undefined, type: type || undefined, startDate, endDate,
      } })
      setAssets(response.data); setSearched(true)
    } catch { setError('We could not check availability. Please try again or contact a branch.') }
    finally { setLoading(false) }
  }, [branchId, endDate, startDate, type])

  useEffect(() => {
    // Load public catalogue data on first visit.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void loadBranches(); void searchAssets()
  }, [loadBranches, searchAssets])

  const availableCount = useMemo(() => assets.filter((asset) => asset.isAvailable).length, [assets])
  const rentalDays = Math.max(1, Math.ceil((new Date(`${endDate}T00:00:00`).getTime() - new Date(`${startDate}T00:00:00`).getTime()) / 86400000))
  function scrollTo(id: string) { document.getElementById(id)?.scrollIntoView({ behavior: 'smooth' }); setMobileMenu(false) }
  function openRequest(asset: PublicAsset) { setSelected(asset); setBooking(emptyBooking); setBookingStep(0); setTermsAccepted(false); setReference(''); setError('') }
  async function submitRequest(event: FormEvent) {
    event.preventDefault(); if (!selected) return
    setSubmitting(true); setError('')
    try {
      const response = await api.post<{ reference: string }>('/public/booking-requests', {
        assetId: selected.id, startDate, endDate, ...booking,
      })
      setReference(response.data.reference)
    } catch (requestError: unknown) {
      const data = axios.isAxiosError(requestError) ? requestError.response?.data : undefined
      const errors = data?.errors as Record<string, string[]> | undefined
      setError(errors ? Object.values(errors).flat().join(' ') : 'Your request could not be submitted. Please contact a branch.')
    } finally { setSubmitting(false) }
  }

  async function trackBooking(event: FormEvent) {
    event.preventDefault(); setTrackingLoading(true); setTrackingError(''); setTracking(null)
    try {
      const response = await api.get<BookingTracking>(`/public/booking-status/${encodeURIComponent(trackingReference.trim())}`)
      setTracking(response.data)
    } catch (requestError: unknown) {
      setTrackingError(axios.isAxiosError(requestError) && requestError.response?.status === 404
        ? 'We could not find that booking reference. Check the number and try again.'
        : 'Booking tracking is temporarily unavailable. Please try again.')
    } finally { setTrackingLoading(false) }
  }

  const nav = <Stack direction={{ xs: 'column', md: 'row' }} gap={{ xs: 1, md: 3 }} alignItems={{ md: 'center' }}>
    <Button color="inherit" onClick={() => scrollTo('rentals')}>Rentals</Button><Button color="inherit" onClick={() => scrollTo('tracking')}>Track booking</Button><Button color="inherit" onClick={() => scrollTo('how-it-works')}>How it works</Button>
    <Button color="inherit" onClick={() => scrollTo('contact')}>Contact</Button><Button variant="outlined" color="inherit" onClick={onStaffLogin}>Staff login</Button>
  </Stack>

  return <Box sx={{ minHeight: '100vh', bgcolor: '#f7f7f3' }}>
    <AppBar position="sticky" elevation={0} sx={{ bgcolor: '#0b0b0b', color: 'white' }}><Toolbar sx={{ minHeight: 76 }}>
      <Box component="img" src="/brand/carpenters-logo.png" alt="Carpenters Fiji" sx={{ width: 48, height: 48, mr: 1.5 }} />
      <Box><Typography fontWeight={800} lineHeight={1}>Carpenters Rentals</Typography><Typography variant="caption" color="secondary.main">Vehicles & equipment</Typography></Box>
      <Box sx={{ flexGrow: 1 }} /><Box sx={{ display: { xs: 'none', md: 'block' } }}>{nav}</Box>
      <IconButton color="inherit" sx={{ display: { md: 'none' } }} onClick={() => setMobileMenu(true)}><MenuOutlined /></IconButton>
    </Toolbar></AppBar>
    <Drawer anchor="right" open={mobileMenu} onClose={() => setMobileMenu(false)}><Box sx={{ width: 280, p: 3 }}>{nav}</Box></Drawer>

    <Box sx={{ bgcolor: '#111', color: 'white', position: 'relative', overflow: 'hidden', py: { xs: 7, md: 11 } }}>
      <Box sx={{ position: 'absolute', width: 600, height: 600, borderRadius: '50%', bgcolor: 'secondary.main', opacity: .08, right: -120, top: -260 }} />
      <Container maxWidth="lg" sx={{ position: 'relative' }}><Typography color="secondary.main" fontWeight={750} letterSpacing={1.5}>FIJI-WIDE RENTAL SERVICE</Typography>
        <Typography variant="h2" fontWeight={800} maxWidth={760} mt={1} sx={{ fontSize: { xs: '2.5rem', md: '4rem' } }}>The right vehicle or equipment, when you need it.</Typography>
        <Typography variant="h6" color="rgba(255,255,255,.7)" maxWidth={650} mt={2}>Browse real availability, compare daily rates and send a booking request directly to our rental team.</Typography>
      </Container>
    </Box>

    <Container maxWidth="lg" sx={{ mt: { xs: -3, md: -4 }, position: 'relative' }}>
      <Card elevation={5}><CardContent sx={{ p: { xs: 2.5, md: 3 } }}><Grid container spacing={2} alignItems="end">
        <Grid size={{ xs: 12, md: 3 }}><FormControl fullWidth><InputLabel>Pickup branch</InputLabel><Select label="Pickup branch" value={branchId} onChange={(e) => setBranchId(e.target.value)}><MenuItem value="">All branches</MenuItem>{branches.map((branch) => <MenuItem key={branch.id} value={branch.id}>{branch.name}</MenuItem>)}</Select></FormControl></Grid>
        <Grid size={{ xs: 12, sm: 6, md: 2 }}><FormControl fullWidth><InputLabel>Rental type</InputLabel><Select label="Rental type" value={type} onChange={(e) => setType(e.target.value as AssetType | '')}><MenuItem value="">All</MenuItem><MenuItem value="Vehicle">Vehicles</MenuItem><MenuItem value="Equipment">Equipment</MenuItem></Select></FormControl></Grid>
        <Grid size={{ xs: 12, sm: 6, md: 2.5 }}><TextField fullWidth type="date" label="Pickup date" InputLabelProps={{ shrink: true }} inputProps={{ min: dateInputValue(0) }} value={startDate} onChange={(e) => setStartDate(e.target.value)} /></Grid>
        <Grid size={{ xs: 12, sm: 6, md: 2.5 }}><TextField fullWidth type="date" label="Return date" InputLabelProps={{ shrink: true }} inputProps={{ min: startDate }} value={endDate} onChange={(e) => setEndDate(e.target.value)} /></Grid>
        <Grid size={{ xs: 12, sm: 6, md: 2 }}><Button fullWidth size="large" variant="contained" startIcon={<SearchOutlined />} onClick={() => void searchAssets()} sx={{ minHeight: 56 }}>Search</Button></Grid>
      </Grid></CardContent></Card>
    </Container>

    <Container id="rentals" maxWidth="lg" sx={{ py: 9 }}>
      <Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" gap={2} mb={4}><Box><Typography variant="h3" fontWeight={800} sx={{ fontSize: { xs: '2rem', md: '3rem' } }}>Available rentals</Typography><Typography color="text.secondary" mt={1}>{searched ? `${availableCount} options available for your dates` : 'Our rental fleet'}</Typography></Box>
        <Chip label="Live availability" color="success" variant="outlined" icon={<CheckCircleOutlined />} sx={{ alignSelf: 'flex-start' }} /></Stack>
      {error && !selected && <Alert severity="error" sx={{ mb: 3 }}>{error}</Alert>}
      {loading ? <Box sx={{ py: 10, display: 'grid', placeItems: 'center' }}><CircularProgress /></Box> : <Grid container spacing={3}>
        {assets.length === 0 && <Grid size={12}><Card variant="outlined"><CardContent sx={{ textAlign: 'center', py: 8 }}><Typography variant="h6">No rentals match this search</Typography><Typography color="text.secondary">Try another branch, type, or date range.</Typography></CardContent></Card></Grid>}
        {assets.map((asset) => <Grid key={asset.id} size={{ xs: 12, sm: 6, lg: 4 }}><Card variant="outlined" sx={{ height: '100%', overflow: 'hidden' }}>
          <Box sx={{ height: 190, bgcolor: asset.type === 'Vehicle' ? '#e8e8e2' : '#ffed00', display: 'grid', placeItems: 'center', position: 'relative' }}>
            {asset.type === 'Vehicle' ? <DirectionsCarOutlined sx={{ fontSize: 92, color: '#292929' }} /> : <BuildOutlined sx={{ fontSize: 82, color: '#292929' }} />}
            <Chip label={asset.isAvailable ? 'Available' : 'Unavailable'} color={asset.isAvailable ? 'success' : 'default'} size="small" sx={{ position: 'absolute', top: 14, right: 14, bgcolor: asset.isAvailable ? undefined : 'white' }} />
          </Box><CardContent sx={{ p: 2.5 }}><Typography variant="overline" color="text.secondary">{asset.type} · {asset.assetNumber}</Typography><Typography variant="h6" fontWeight={750}>{asset.name}</Typography>
            <Stack direction="row" alignItems="center" gap={.5} mt={1}><LocationOnOutlined fontSize="small" color="action" /><Typography variant="body2" color="text.secondary">{asset.branchName}</Typography></Stack>
            <Divider sx={{ my: 2 }} /><Stack direction="row" justifyContent="space-between" alignItems="end"><Box><Typography variant="caption" color="text.secondary">Estimated for {rentalDays} {rentalDays === 1 ? 'day' : 'days'}</Typography><Typography variant="h5" fontWeight={800}>${(asset.dailyRate * rentalDays).toFixed(2)}<Typography component="span" variant="body2" color="text.secondary"> FJD</Typography></Typography><Typography variant="caption" color="text.secondary">${asset.dailyRate.toFixed(2)} per day</Typography></Box>
              <Button variant="contained" disabled={!asset.isAvailable} endIcon={<ArrowForwardOutlined />} onClick={() => openRequest(asset)}>Request</Button></Stack>
          </CardContent></Card></Grid>)}
      </Grid>}
    </Container>

    <Box id="tracking" sx={{ bgcolor: 'secondary.main', py: { xs: 7, md: 9 } }}><Container maxWidth="md">
      <Stack alignItems="center" textAlign="center"><Typography variant="h3" fontWeight={800} sx={{ fontSize: { xs: '2rem', md: '3rem' } }}>Track your booking</Typography><Typography mt={1} sx={{ opacity: .72 }}>Enter the reference supplied when you submitted your rental request.</Typography></Stack>
      <Card sx={{ mt: 4 }}><CardContent sx={{ p: { xs: 2.5, md: 4 } }}>
        <Box component="form" onSubmit={trackBooking}><Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5}>
          <TextField fullWidth required label="Booking reference" placeholder="REQ-XXXXXXXXXXXX" value={trackingReference} onChange={(e) => setTrackingReference(e.target.value.toUpperCase())} inputProps={{ maxLength: 50 }} />
          <Button type="submit" variant="contained" size="large" disabled={trackingLoading || !trackingReference.trim()} sx={{ minWidth: 160 }}>{trackingLoading ? <CircularProgress size={22} color="inherit" /> : 'Check status'}</Button>
        </Stack></Box>
        {trackingError && <Alert severity="error" sx={{ mt: 3 }}>{trackingError}</Alert>}
        {tracking && <Box sx={{ mt: 3, p: { xs: 2, sm: 3 }, bgcolor: '#f7f7f3', borderRadius: 2 }}>
          <Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" gap={2}><Box><Typography variant="overline" color="text.secondary">Reference</Typography><Typography variant="h6" fontWeight={750}>{tracking.reference}</Typography></Box><Chip label={tracking.status} color={tracking.status === 'Confirmed' || tracking.status === 'Rental active' ? 'success' : tracking.status === 'Not proceeding' ? 'error' : 'warning'} sx={{ alignSelf: 'flex-start', fontWeight: 700 }} /></Stack>
          <Typography mt={2}>{tracking.message}</Typography><Divider sx={{ my: 2 }} />
          <Grid container spacing={2}><Grid size={{ xs: 12, sm: 4 }}><Typography variant="caption" color="text.secondary">Rental item</Typography><Typography fontWeight={650}>{tracking.assetName || 'To be confirmed'}</Typography></Grid><Grid size={{ xs: 12, sm: 4 }}><Typography variant="caption" color="text.secondary">Branch</Typography><Typography fontWeight={650}>{tracking.branchName}</Typography></Grid><Grid size={{ xs: 12, sm: 4 }}><Typography variant="caption" color="text.secondary">Rental dates</Typography><Typography fontWeight={650}>{tracking.startAt && tracking.endAt ? `${new Date(tracking.startAt).toLocaleDateString('en-FJ')} – ${new Date(tracking.endAt).toLocaleDateString('en-FJ')}` : 'To be confirmed'}</Typography></Grid></Grid>
        </Box>}
      </CardContent></Card>
    </Container></Box>

    <Box id="how-it-works" sx={{ bgcolor: '#111', color: 'white', py: 9 }}><Container maxWidth="lg"><Typography variant="h3" fontWeight={800} textAlign="center" sx={{ fontSize: { xs: '2rem', md: '3rem' } }}>Simple from search to pickup</Typography><Grid container spacing={3} mt={3}>
      {[['1', 'Search availability', 'Choose your branch, rental dates and the type of asset you need.'], ['2', 'Send your request', 'Provide your contact details—no account or password is required.'], ['3', 'We confirm', 'A rental officer checks your request and contacts you to finalize the booking.']].map(([number, title, text]) => <Grid key={number} size={{ xs: 12, md: 4 }}><Stack alignItems="center" textAlign="center"><Avatar sx={{ bgcolor: 'secondary.main', color: '#111', fontWeight: 800, width: 52, height: 52 }}>{number}</Avatar><Typography variant="h6" fontWeight={700} mt={2}>{title}</Typography><Typography color="rgba(255,255,255,.65)" mt={1}>{text}</Typography></Stack></Grid>)}
    </Grid></Container></Box>

    <Container id="contact" maxWidth="lg" sx={{ py: 9 }}><Typography variant="h3" fontWeight={800} sx={{ fontSize: { xs: '2rem', md: '3rem' } }}>Contact our branches</Typography><Typography color="text.secondary" mt={1} mb={4}>Need advice before requesting? Speak with a local rental team.</Typography><Grid container spacing={3}>
      {branches.map((branch) => <Grid key={branch.id} size={{ xs: 12, md: 6 }}><Card variant="outlined"><CardContent sx={{ p: 3 }}><Typography variant="h6" fontWeight={750}>{branch.name}</Typography><Stack gap={1.25} mt={2}>{branch.address && <Stack direction="row" gap={1}><LocationOnOutlined color="action" /><Typography color="text.secondary">{branch.address}</Typography></Stack>}{branch.phone && <Stack direction="row" gap={1}><PhoneOutlined color="action" /><Link href={`tel:${branch.phone}`} color="inherit">{branch.phone}</Link></Stack>}</Stack></CardContent></Card></Grid>)}
    </Grid></Container>
    <Box sx={{ bgcolor: '#080808', color: 'rgba(255,255,255,.65)', py: 3 }}><Container maxWidth="lg"><Typography variant="body2">© {new Date().getFullYear()} Carpenters Fiji — Vehicle & Equipment Rentals</Typography></Container></Box>

    <Dialog open={Boolean(selected)} onClose={() => !submitting && setSelected(null)} fullWidth maxWidth="md"><DialogTitle>{reference ? 'Request received' : `Request ${selected?.name ?? 'rental'}`}</DialogTitle><DialogContent>
      {reference ? <Stack alignItems="center" textAlign="center" py={4}><CheckCircleOutlined color="success" sx={{ fontSize: 70 }} /><Typography variant="h5" fontWeight={750} mt={2}>Request successfully submitted</Typography><Typography color="text.secondary" mt={1}>Save this reference to track your request. Our rental team will contact you after review.</Typography><Chip label={`Reference: ${reference}`} sx={{ mt: 3, fontWeight: 700, fontSize: '1rem', py: 2.25 }} /><Button sx={{ mt: 2 }} onClick={() => { setTrackingReference(reference); setSelected(null); setTimeout(() => scrollTo('tracking'), 0) }}>Track this request</Button></Stack> :
      <Box component="form" id="public-booking-form" onSubmit={bookingStep === 2 ? submitRequest : (event) => { event.preventDefault(); setBookingStep((step) => step + 1) }}>
        <Stepper activeStep={bookingStep} sx={{ py: 2.5 }}><Step><StepLabel>Rental</StepLabel></Step><Step><StepLabel>Your details</StepLabel></Step><Step><StepLabel>Review</StepLabel></Step></Stepper>
        <Stack spacing={2.25} mt={1}>{error && <Alert severity="error">{error}</Alert>}
          {bookingStep === 0 && <><Alert severity="info" icon={<CalendarMonthOutlined />}>{startDate} to {endDate} · {selected?.branchName}</Alert>
            <Card variant="outlined"><CardContent><Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" gap={2}><Box><Typography variant="overline" color="text.secondary">Selected rental</Typography><Typography variant="h6" fontWeight={750}>{selected?.name}</Typography><Typography color="text.secondary">{selected?.type} · {selected?.assetNumber}</Typography></Box><Box textAlign={{ sm: 'right' }}><Typography variant="overline" color="text.secondary">Estimated rental charge</Typography><Typography variant="h5" fontWeight={800}>${((selected?.dailyRate ?? 0) * rentalDays).toFixed(2)} FJD</Typography><Typography variant="caption" color="text.secondary">{rentalDays} days × ${selected?.dailyRate.toFixed(2)}</Typography></Box></Stack></CardContent></Card>
            <Alert severity="warning"><Typography fontWeight={700}>Bring when collecting</Typography><Typography variant="body2">Valid driver licence or approved identification, your booking reference, and an accepted payment method. A security deposit and additional requirements may apply after staff review.</Typography></Alert></>}
          {bookingStep === 1 && <><Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}><FormControl fullWidth><InputLabel>Customer type</InputLabel><Select label="Customer type" value={booking.customerType} onChange={(e) => setBooking({ ...booking, customerType: e.target.value as 'Individual' | 'Business' })}><MenuItem value="Individual">Individual</MenuItem><MenuItem value="Business">Business</MenuItem></Select></FormControl><TextField fullWidth required label="Contact person" value={booking.fullName} onChange={(e) => setBooking({ ...booking, fullName: e.target.value })} /></Stack>
            {booking.customerType === 'Business' && <TextField required label="Registered business name" value={booking.companyName} onChange={(e) => setBooking({ ...booking, companyName: e.target.value })} />}
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}><TextField fullWidth required type="email" label="Email" value={booking.email} onChange={(e) => setBooking({ ...booking, email: e.target.value })} /><TextField fullWidth required label="Phone" value={booking.phone} onChange={(e) => setBooking({ ...booking, phone: e.target.value })} /></Stack>
            <TextField label="Address" value={booking.address} onChange={(e) => setBooking({ ...booking, address: e.target.value })} /><TextField label={booking.customerType === 'Business' ? 'TIN / registration number' : 'Driver licence / ID number'} value={booking.identificationNumber} onChange={(e) => setBooking({ ...booking, identificationNumber: e.target.value })} />
            <TextField label="Rental purpose" placeholder="For example: site work, airport transfer, event logistics" value={booking.purpose} onChange={(e) => setBooking({ ...booking, purpose: e.target.value })} /><TextField label="Additional requirements" multiline minRows={2} value={booking.message} onChange={(e) => setBooking({ ...booking, message: e.target.value })} /></>}
          {bookingStep === 2 && <><Card variant="outlined"><CardContent><Typography variant="h6" fontWeight={750}>Review your request</Typography><Divider sx={{ my: 2 }} /><Grid container spacing={2}><Grid size={{ xs: 12, sm: 6 }}><Typography variant="caption" color="text.secondary">Rental</Typography><Typography fontWeight={650}>{selected?.name}</Typography><Typography variant="body2">{startDate} to {endDate}</Typography></Grid><Grid size={{ xs: 12, sm: 6 }}><Typography variant="caption" color="text.secondary">Estimated charge</Typography><Typography fontWeight={750}>${((selected?.dailyRate ?? 0) * rentalDays).toFixed(2)} FJD</Typography><Typography variant="body2" color="text.secondary">Final charges confirmed by staff</Typography></Grid><Grid size={{ xs: 12, sm: 6 }}><Typography variant="caption" color="text.secondary">Customer</Typography><Typography fontWeight={650}>{booking.customerType === 'Business' ? booking.companyName : booking.fullName}</Typography><Typography variant="body2">Contact: {booking.fullName}</Typography></Grid><Grid size={{ xs: 12, sm: 6 }}><Typography variant="caption" color="text.secondary">Contact</Typography><Typography>{booking.email}</Typography><Typography>{booking.phone}</Typography></Grid></Grid></CardContent></Card>
            <FormControlLabel control={<Checkbox checked={termsAccepted} onChange={(e) => setTermsAccepted(e.target.checked)} />} label="I understand this is a request, not a confirmed reservation, and final rates, deposits, eligibility and documents will be confirmed by Carpenters Rentals." /></>}
        </Stack>
      </Box>}
    </DialogContent><DialogActions sx={{ p: 3, pt: 1 }}>{reference ? <Button onClick={() => setSelected(null)}>Close</Button> : <><Button onClick={() => bookingStep === 0 ? setSelected(null) : setBookingStep((step) => step - 1)} disabled={submitting}>{bookingStep === 0 ? 'Cancel' : 'Back'}</Button><Box sx={{ flex: 1 }} /><Button form="public-booking-form" type="submit" variant="contained" disabled={submitting || (bookingStep === 2 && !termsAccepted)}>{submitting ? 'Sending…' : bookingStep === 2 ? 'Submit request' : 'Continue'}</Button></>}</DialogActions></Dialog>
  </Box>
}
