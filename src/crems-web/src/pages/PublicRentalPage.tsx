import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import axios from 'axios'
import ArrowForwardOutlined from '@mui/icons-material/ArrowForwardOutlined'
import CalendarMonthOutlined from '@mui/icons-material/CalendarMonthOutlined'
import CheckCircleOutlined from '@mui/icons-material/CheckCircleOutlined'
import ConstructionOutlined from '@mui/icons-material/ConstructionOutlined'
import DirectionsCarOutlined from '@mui/icons-material/DirectionsCarOutlined'
import LocationOnOutlined from '@mui/icons-material/LocationOnOutlined'
import MenuOutlined from '@mui/icons-material/MenuOutlined'
import PhoneOutlined from '@mui/icons-material/PhoneOutlined'
import SearchOutlined from '@mui/icons-material/SearchOutlined'
import VerifiedOutlined from '@mui/icons-material/VerifiedOutlined'
import SupportAgentOutlined from '@mui/icons-material/SupportAgentOutlined'
import LocalShippingOutlined from '@mui/icons-material/LocalShippingOutlined'
import ExpandMoreOutlined from '@mui/icons-material/ExpandMoreOutlined'
import {
  Accordion, AccordionDetails, AccordionSummary, Alert, AppBar, Avatar, Box, Button, Card, CardContent, Chip, CircularProgress,
  Checkbox, Container, Dialog, DialogActions, DialogContent, DialogTitle, Divider,
  Drawer, FormControl, FormControlLabel, Grid, IconButton, InputLabel, Link,
  MenuItem, Select, Stack, Step, StepLabel, Stepper, TextField, Toolbar, Typography,
} from '@mui/material'
import { api } from '../api/client'

type AssetType = 'Vehicle' | 'Equipment'
type PublicAsset = {
  id: string; assetNumber: string; name: string; type: AssetType; status: string
  branchId: string; branchName: string; dailyRate: number; isAvailable: boolean
  divisionId: string | null; divisionName: string | null; category: string | null
  personnelRequirement: 'None' | 'Optional' | 'Required'
}
type Branch = { id: string; name: string; address: string | null; phone: string | null }
type PublicDivision = { id: string; code: string; name: string; description: string | null; capabilities: number }
type BookingForm = {
  fullName: string; customerType: 'Individual' | 'Business'; companyName: string
  email: string; phone: string; address: string; identificationNumber: string
  purpose: string; message: string
}
const emptyBooking: BookingForm = {
  fullName: '', customerType: 'Individual', companyName: '', email: '', phone: '',
  address: '', identificationNumber: '', purpose: '', message: '',
}

function dateInputValue(offsetDays: number) {
  const date = new Date(); date.setDate(date.getDate() + offsetDays)
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
}

function catalogueImage(asset: PublicAsset) {
  const searchableName = `${asset.assetNumber} ${asset.name}`.toLowerCase()

  if (asset.type === 'Vehicle') {
    if (/navara|d-max|pickup/.test(searchableName)) return '/catalog/pickup.jpg'
    if (/nv350|urvan|staria|seater|coach|minibus|bus/.test(searchableName)) return '/catalog/minibus.jpg'
    if (/npr|cargo|truck/.test(searchableName)) return '/catalog/truck.jpg'
    if (/i10|sedan/.test(searchableName)) return '/catalog/sedan.jpg'
    return '/catalog/suv.jpg'
  }

  if (/forklift/.test(searchableName)) return '/catalog/forklift.jpg'
  if (/scissor/.test(searchableName)) return '/catalog/scissor-lift.jpg'
  if (/generator|compressor/.test(searchableName)) return '/catalog/generator.jpg'
  return '/catalog/excavator.jpg'
}

export function PublicRentalPage({ onCustomerAccount, customerAuthenticated = false }: { onCustomerAccount: () => void; customerAuthenticated?: boolean }) {
  const [assets, setAssets] = useState<PublicAsset[]>([])
  const [branches, setBranches] = useState<Branch[]>([])
  const [divisions, setDivisions] = useState<PublicDivision[]>([])
  const [divisionId, setDivisionId] = useState('')
  const [branchId, setBranchId] = useState('')
  const [type, setType] = useState<AssetType | ''>('')
  const [category, setCategory] = useState('All')
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

  const loadBranches = useCallback(async () => {
    try { setBranches((await api.get<Branch[]>('/public/branches')).data) } catch { /* Contacts remain optional. */ }
  }, [])
  const loadDivisions = useCallback(async () => {
    try { setDivisions((await api.get<PublicDivision[]>('/public/divisions')).data) } catch { /* Catalogue still works without the selector. */ }
  }, [])
  const searchAssets = useCallback(async () => {
    setLoading(true); setError('')
    try {
      const response = await api.get<PublicAsset[]>('/public/assets', { params: {
        branchId: branchId || undefined, divisionId: divisionId || undefined, type: type || undefined, startDate, endDate,
      } })
      setAssets(response.data); setSearched(true)
    } catch { setError('We could not check availability. Please try again or contact a branch.') }
    finally { setLoading(false) }
  }, [branchId, divisionId, endDate, startDate, type])

  useEffect(() => {
    // Load public catalogue data on first visit.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void loadBranches(); void loadDivisions(); void searchAssets()
  }, [loadBranches, loadDivisions, searchAssets])

  const availableCount = useMemo(() => assets.filter((asset) => asset.isAvailable).length, [assets])
  const displayedAssets = useMemo(() => assets.filter((asset) => {
    if (category === 'All') return true
    const value = `${asset.name} ${asset.category}`.toLowerCase()
    if (category === 'Cars & SUVs') return asset.type === 'Vehicle' && !/truck|van|urvan|staria|coach|seater/.test(value)
    if (category === 'Vans & trucks') return asset.type === 'Vehicle' && /truck|van|urvan|staria|coach|seater|cargo/.test(value)
    if (category === 'Earthmoving') return /excavator|backhoe|loader/.test(value)
    if (category === 'Lifting') return /crane|forklift|telehandler|scissor/.test(value)
    if (category === 'Power & site') return /generator|compressor|compactor|mixer/.test(value)
    return true
  }), [assets, category])
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

  function chooseDivision(division: PublicDivision) {
    setDivisionId(division.id)
    setType(/carptrac/i.test(`${division.code} ${division.name}`) ? 'Equipment' : 'Vehicle')
    setTimeout(() => scrollTo('search'), 0)
  }
  function requestBooking(asset: PublicAsset) {
    if (!customerAuthenticated) {
      sessionStorage.setItem('crems.pendingBooking', JSON.stringify({ assetId: asset.id, startDate, endDate }))
      onCustomerAccount()
      return
    }
    openRequest(asset)
  }

  const nav = <Stack direction={{ xs: 'column', md: 'row' }} gap={{ xs: 1, md: 3 }} alignItems={{ md: 'center' }}>
    <Button color="inherit" onClick={() => scrollTo('services')}>Services</Button><Button color="inherit" onClick={() => scrollTo('rentals')}>Browse fleet</Button><Button color="inherit" onClick={() => scrollTo('how-it-works')}>How it works</Button><Button color="inherit" onClick={() => scrollTo('faq')}>Help</Button>
    <Button color="inherit" onClick={() => scrollTo('contact')}>Contact</Button><Button variant="contained" color="secondary" onClick={onCustomerAccount}>{customerAuthenticated ? 'My bookings' : 'Customer login'}</Button>
  </Stack>

  return <Box sx={{ minHeight: '100vh', bgcolor: '#f7f7f3' }}>
    <AppBar position="sticky" elevation={0} sx={{ bgcolor: '#0b0b0b', color: 'white' }}><Toolbar sx={{ minHeight: 76 }}>
      <Box component="img" src="/brand/carpenters-logo.png" alt="Carpenters Fiji" sx={{ width: 48, height: 48, mr: 1.5 }} />
      <Box><Typography fontWeight={800} lineHeight={1}>Carpenters Services</Typography><Typography variant="caption" color="secondary.main">Simple rentals, group-wide capability</Typography></Box>
      <Box sx={{ flexGrow: 1 }} /><Box sx={{ display: { xs: 'none', md: 'block' } }}>{nav}</Box>
      <IconButton color="inherit" sx={{ display: { md: 'none' } }} onClick={() => setMobileMenu(true)}><MenuOutlined /></IconButton>
    </Toolbar></AppBar>
    <Drawer anchor="right" open={mobileMenu} onClose={() => setMobileMenu(false)}><Box sx={{ width: 280, p: 3 }}>{nav}</Box></Drawer>

    <Box sx={{ bgcolor: '#111', color: 'white', position: 'relative', overflow: 'hidden', py: { xs: 7, md: 11 } }}>
      <Box sx={{ position: 'absolute', width: 600, height: 600, borderRadius: '50%', bgcolor: 'secondary.main', opacity: .08, right: -120, top: -260 }} />
      <Container maxWidth="lg" sx={{ position: 'relative' }}><Typography color="secondary.main" fontWeight={750} letterSpacing={1.5}>FIJI-WIDE RENTAL SERVICE</Typography>
        <Typography variant="h2" fontWeight={800} maxWidth={760} mt={1} sx={{ fontSize: { xs: '2.5rem', md: '4rem' } }}>The right vehicle or equipment, when you need it.</Typography>
        <Typography variant="h6" color="rgba(255,255,255,.7)" maxWidth={650} mt={2}>Choose Carpenters Rentals for vehicles or Carptrac for equipment. Browse prices and live availability without creating an account.</Typography>
        <Stack direction={{ xs: 'column', sm: 'row' }} gap={1.5} mt={3} alignItems={{ sm: 'center' }}><Button size="large" variant="contained" color="secondary" endIcon={<SearchOutlined />} onClick={() => scrollTo('search')}>Check availability</Button><Button size="large" variant="outlined" color="inherit" onClick={onCustomerAccount}>{customerAuthenticated ? 'Manage my bookings' : 'Sign in to book'}</Button><Typography variant="body2" color="rgba(255,255,255,.6)">No account needed to browse</Typography></Stack>
      </Container>
    </Box>

    <Container id="services" maxWidth="lg" sx={{ py: { xs: 5, md: 7 } }}>
      <Typography variant="h3" fontWeight={800} sx={{ fontSize: { xs: '2rem', md: '3rem' } }}>What would you like to hire?</Typography>
      <Typography color="text.secondary" mt={1}>Start with the division that matches your need. You can return and change it at any time.</Typography>
      <Grid container spacing={3} mt={1}>{divisions.filter((division) => /rental|carptrac/i.test(`${division.code} ${division.name}`)).map((division) => {
        const equipment = /carptrac/i.test(`${division.code} ${division.name}`)
        const active = divisionId === division.id
        return <Grid key={division.id} size={{ xs: 12, md: 6 }}><Card variant="outlined" onClick={() => chooseDivision(division)} sx={{ cursor: 'pointer', height: '100%', borderColor: active ? 'secondary.main' : undefined, borderWidth: active ? 2 : 1, transition: 'transform .2s ease, border-color .2s ease', '&:hover': { transform: 'translateY(-3px)', borderColor: 'secondary.main' } }}><CardContent sx={{ p: 3.5 }}><Avatar sx={{ bgcolor: 'secondary.main', color: '#111', width: 58, height: 58 }}>{equipment ? <ConstructionOutlined /> : <DirectionsCarOutlined />}</Avatar><Typography variant="h5" fontWeight={800} mt={2}>{division.name}</Typography><Typography color="text.secondary" mt={1}>{division.description || (equipment ? 'Generators, earthmoving and other equipment for commercial and project work.' : 'Cars, SUVs, pickups, vans and other vehicles for personal or business travel.')}</Typography><Button endIcon={<ArrowForwardOutlined />} sx={{ mt: 2 }}>View {equipment ? 'equipment' : 'vehicles'}</Button></CardContent></Card></Grid>
      })}</Grid>
    </Container>

    <Container id="search" maxWidth="lg" sx={{ position: 'relative' }}>
      <Card elevation={5}><CardContent sx={{ p: { xs: 2.5, md: 3 } }}><Grid container spacing={2} alignItems="end">
        <Grid size={{ xs: 12, md: 2.5 }}><FormControl fullWidth><InputLabel>Hire from</InputLabel><Select label="Hire from" value={divisionId} onChange={(e) => { const value = e.target.value; setDivisionId(value); const division = divisions.find(x => x.id === value); if (division) setType(/carptrac/i.test(`${division.code} ${division.name}`) ? 'Equipment' : 'Vehicle') }}><MenuItem value="">Rentals and Carptrac</MenuItem>{divisions.filter(x => /rental|carptrac/i.test(`${x.code} ${x.name}`)).map((division) => <MenuItem key={division.id} value={division.id}>{division.name}</MenuItem>)}</Select></FormControl></Grid>
        <Grid size={{ xs: 12, md: 2.5 }}><FormControl fullWidth><InputLabel>Pickup branch</InputLabel><Select label="Pickup branch" value={branchId} onChange={(e) => setBranchId(e.target.value)}><MenuItem value="">All branches</MenuItem>{branches.map((branch) => <MenuItem key={branch.id} value={branch.id}>{branch.name}</MenuItem>)}</Select></FormControl></Grid>
        <Grid size={{ xs: 12, sm: 6, md: 2 }}><FormControl fullWidth><InputLabel>Rental type</InputLabel><Select label="Rental type" value={type} onChange={(e) => setType(e.target.value as AssetType | '')}><MenuItem value="">All</MenuItem><MenuItem value="Vehicle">Vehicles</MenuItem><MenuItem value="Equipment">Equipment</MenuItem></Select></FormControl></Grid>
        <Grid size={{ xs: 12, sm: 6, md: 2 }}><TextField fullWidth type="date" label="Pickup date" InputLabelProps={{ shrink: true }} inputProps={{ min: dateInputValue(0) }} value={startDate} onChange={(e) => setStartDate(e.target.value)} /></Grid>
        <Grid size={{ xs: 12, sm: 6, md: 2 }}><TextField fullWidth type="date" label="Return date" InputLabelProps={{ shrink: true }} inputProps={{ min: startDate }} value={endDate} onChange={(e) => setEndDate(e.target.value)} /></Grid>
        <Grid size={{ xs: 12, sm: 6, md: 1 }}><Button fullWidth size="large" variant="contained" aria-label="Search availability" onClick={() => void searchAssets()} sx={{ minHeight: 56 }}><SearchOutlined /></Button></Grid>
      </Grid></CardContent></Card>
    </Container>

    <Container id="rentals" maxWidth="lg" sx={{ py: 9 }}>
      <Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" gap={2} mb={4}><Box><Typography variant="h3" fontWeight={800} sx={{ fontSize: { xs: '2rem', md: '3rem' } }}>Available rentals</Typography><Typography color="text.secondary" mt={1}>{searched ? `${availableCount} options available for your dates` : 'Our rental fleet'}</Typography></Box>
        <Chip label="Live availability" color="success" variant="outlined" icon={<CheckCircleOutlined />} sx={{ alignSelf: 'flex-start' }} /></Stack>
      <Stack direction="row" gap={1} flexWrap="wrap" mb={3}>{['All', 'Cars & SUVs', 'Vans & trucks', 'Earthmoving', 'Lifting', 'Power & site'].map(item => <Chip key={item} clickable label={item} color={category === item ? 'primary' : 'default'} variant={category === item ? 'filled' : 'outlined'} onClick={() => setCategory(item)} />)}</Stack>
      {error && !selected && <Alert severity="error" sx={{ mb: 3 }}>{error}</Alert>}
      {loading ? <Box sx={{ py: 10, display: 'grid', placeItems: 'center' }}><CircularProgress /></Box> : <Grid container spacing={3}>
        {displayedAssets.length === 0 && <Grid size={12}><Card variant="outlined"><CardContent sx={{ textAlign: 'center', py: 8 }}><Typography variant="h6">No rentals match this search</Typography><Typography color="text.secondary">Try another category, branch or date range.</Typography><Button sx={{ mt: 2 }} onClick={() => setCategory('All')}>Clear category</Button></CardContent></Card></Grid>}
        {displayedAssets.map((asset) => <Grid key={asset.id} size={{ xs: 12, sm: 6, lg: 4 }}><Card variant="outlined" sx={{ height: '100%', overflow: 'hidden' }}>
          <Box sx={{ height: 210, bgcolor: '#e8e8e2', position: 'relative', overflow: 'hidden' }}>
            <Box component="img" src={catalogueImage(asset)} alt={`${asset.name} available from ${asset.branchName}`} loading="lazy" sx={{ width: '100%', height: '100%', display: 'block', objectFit: 'cover', transition: 'transform .3s ease', '.MuiCard-root:hover &': { transform: 'scale(1.035)' } }} />
            <Box sx={{ position: 'absolute', inset: 0, background: 'linear-gradient(180deg, rgba(0,0,0,.04) 45%, rgba(0,0,0,.36) 100%)', pointerEvents: 'none' }} />
            <Chip label={asset.isAvailable ? 'Available' : 'Unavailable'} color={asset.isAvailable ? 'success' : 'default'} size="small" sx={{ position: 'absolute', top: 14, right: 14, bgcolor: asset.isAvailable ? undefined : 'white' }} />
          </Box><CardContent sx={{ p: 2.5 }}><Typography variant="overline" color="text.secondary">{asset.divisionName || asset.type} · {asset.assetNumber}</Typography><Typography variant="h6" fontWeight={750}>{asset.name}</Typography>
            {asset.personnelRequirement !== 'None' && <Chip size="small" sx={{ mt: 1 }} label={`Trained personnel ${asset.personnelRequirement.toLowerCase()}`} color={asset.personnelRequirement === 'Required' ? 'warning' : 'default'} />}
            <Stack direction="row" alignItems="center" gap={.5} mt={1}><LocationOnOutlined fontSize="small" color="action" /><Typography variant="body2" color="text.secondary">{asset.branchName}</Typography></Stack>
            <Divider sx={{ my: 2 }} /><Stack direction="row" justifyContent="space-between" alignItems="end"><Box><Typography variant="caption" color="text.secondary">Estimated for {rentalDays} {rentalDays === 1 ? 'day' : 'days'}</Typography><Typography variant="h5" fontWeight={800}>${(asset.dailyRate * rentalDays).toFixed(2)}<Typography component="span" variant="body2" color="text.secondary"> FJD</Typography></Typography><Typography variant="caption" color="text.secondary">${asset.dailyRate.toFixed(2)} per day</Typography></Box>
              <Button variant="contained" disabled={!asset.isAvailable} endIcon={<ArrowForwardOutlined />} onClick={() => requestBooking(asset)}>{customerAuthenticated ? (asset.type === 'Equipment' ? 'Request quote' : 'Book vehicle') : 'Sign in to continue'}</Button></Stack>
            <Typography variant="caption" color="text.secondary" display="block" mt={1.5}>Estimate excludes VAT, deposit, delivery, fuel and optional operator charges. Final price is confirmed before approval.</Typography>
          </CardContent></Card></Grid>)}
      </Grid>}
    </Container>

    <Box sx={{ bgcolor: 'secondary.main', py: { xs: 6, md: 7 } }}><Container maxWidth="md"><Stack direction={{ xs: 'column', md: 'row' }} alignItems={{ md: 'center' }} justifyContent="space-between" gap={3}><Box><Typography variant="h4" fontWeight={800}>Ready to book or track a booking?</Typography><Typography mt={1} sx={{ opacity: .72 }}>Sign in to submit bookings securely and see every status update in one place.</Typography></Box><Button size="large" variant="contained" onClick={onCustomerAccount} sx={{ bgcolor: '#111', color: 'white', minWidth: 190, '&:hover': { bgcolor: '#292929' } }}>{customerAuthenticated ? 'Open my bookings' : 'Customer login'}</Button></Stack></Container></Box>

    <Box id="how-it-works" sx={{ bgcolor: '#111', color: 'white', py: 9 }}><Container maxWidth="lg"><Typography variant="h3" fontWeight={800} textAlign="center" sx={{ fontSize: { xs: '2rem', md: '3rem' } }}>Simple from search to pickup</Typography><Grid container spacing={3} mt={3}>
      {[['1', 'Choose a division', 'Select Carpenters Rentals for vehicles or Carptrac for equipment.'], ['2', 'Compare options', 'Check prices, branches and live availability without signing in.'], ['3', 'Sign in and book', 'Use your customer account to submit and track bookings securely.']].map(([number, title, text]) => <Grid key={number} size={{ xs: 12, md: 4 }}><Stack alignItems="center" textAlign="center"><Avatar sx={{ bgcolor: 'secondary.main', color: '#111', fontWeight: 800, width: 52, height: 52 }}>{number}</Avatar><Typography variant="h6" fontWeight={700} mt={2}>{title}</Typography><Typography color="rgba(255,255,255,.65)" mt={1}>{text}</Typography></Stack></Grid>)}
    </Grid></Container></Box>

    <Container maxWidth="lg" sx={{ py: 8 }}><Grid container spacing={3}>{[[<VerifiedOutlined />, 'Maintained and inspected', 'Availability and maintenance status are managed by the operating branch.'], [<SupportAgentOutlined />, 'Local support', 'A Carpenters team member reviews every request and confirms collection requirements.'], [<LocalShippingOutlined />, 'Pickup or delivery planning', 'Equipment transport and trained personnel can be included in the final quote.']].map(([icon, title, text]) => <Grid key={String(title)} size={{ xs: 12, md: 4 }}><Card variant="outlined" sx={{ height: '100%' }}><CardContent sx={{ p: 3 }}><Avatar sx={{ bgcolor: 'secondary.main', color: '#111' }}>{icon}</Avatar><Typography variant="h6" fontWeight={750} mt={2}>{title}</Typography><Typography color="text.secondary" mt={1}>{text}</Typography></CardContent></Card></Grid>)}</Grid></Container>

    <Box id="faq" sx={{ bgcolor: 'white', py: 8 }}><Container maxWidth="md"><Typography variant="h3" fontWeight={800} textAlign="center" sx={{ fontSize: { xs: '2rem', md: '2.7rem' } }}>Before you make a request</Typography><Typography color="text.secondary" textAlign="center" mt={1} mb={4}>Clear answers to common rental questions.</Typography>{[['Is the displayed price final?', 'No. It is an estimated base hire charge. VAT, deposit, delivery, fuel, damage waiver, excess usage and operator charges may apply. Staff confirm the complete quote before approval.'], ['What do I need at pickup?', 'Individual vehicle customers normally need valid identification, an appropriate driver licence, their booking reference and an accepted payment method. Business and equipment hires may require a purchase order, site details or approved operator.'], ['Does an online request reserve the asset?', 'The request is held for staff review. It becomes confirmed only after eligibility, availability, pricing and any deposit or documentation requirements are approved.'], ['Can Carptrac equipment include an operator?', 'Yes, where the service supports it. Some assets require trained personnel, while others offer personnel as an option. This is shown on the asset card and confirmed in the quote.']].map(([question, answer]) => <Accordion key={question} disableGutters elevation={0} sx={{ borderBottom: 1, borderColor: 'divider' }}><AccordionSummary expandIcon={<ExpandMoreOutlined />}><Typography fontWeight={700}>{question}</Typography></AccordionSummary><AccordionDetails><Typography color="text.secondary">{answer}</Typography></AccordionDetails></Accordion>)}</Container></Box>

    <Container id="contact" maxWidth="lg" sx={{ py: 9 }}><Typography variant="h3" fontWeight={800} sx={{ fontSize: { xs: '2rem', md: '3rem' } }}>Contact our branches</Typography><Typography color="text.secondary" mt={1} mb={4}>Need advice before requesting? Speak with a local rental team.</Typography><Grid container spacing={3}>
      {branches.map((branch) => <Grid key={branch.id} size={{ xs: 12, md: 6 }}><Card variant="outlined"><CardContent sx={{ p: 3 }}><Typography variant="h6" fontWeight={750}>{branch.name}</Typography><Stack gap={1.25} mt={2}>{branch.address && <Stack direction="row" gap={1}><LocationOnOutlined color="action" /><Typography color="text.secondary">{branch.address}</Typography></Stack>}{branch.phone && <Stack direction="row" gap={1}><PhoneOutlined color="action" /><Link href={`tel:${branch.phone}`} color="inherit">{branch.phone}</Link></Stack>}</Stack></CardContent></Card></Grid>)}
    </Grid></Container>
    <Box sx={{ bgcolor: '#080808', color: 'rgba(255,255,255,.65)', py: 3 }}><Container maxWidth="lg"><Typography variant="body2">© {new Date().getFullYear()} Carpenters Fiji — Vehicle & Equipment Rentals</Typography></Container></Box>

    <Dialog open={Boolean(selected)} onClose={() => !submitting && setSelected(null)} fullWidth maxWidth="md"><DialogTitle>{reference ? 'Request received' : `Request ${selected?.name ?? 'rental'}`}</DialogTitle><DialogContent>
      {reference ? <Stack alignItems="center" textAlign="center" py={4}><CheckCircleOutlined color="success" sx={{ fontSize: 70 }} /><Typography variant="h5" fontWeight={750} mt={2}>Booking request submitted</Typography><Typography color="text.secondary" mt={1}>The request is now available in your customer account.</Typography><Chip label={`Reference: ${reference}`} sx={{ mt: 3, fontWeight: 700, fontSize: '1rem', py: 2.25 }} /><Button sx={{ mt: 2 }} onClick={onCustomerAccount}>View my bookings</Button></Stack> :
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
