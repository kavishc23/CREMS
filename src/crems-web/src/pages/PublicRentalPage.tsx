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
import { PublicAssetDetailDialog } from '../components/PublicAssetDetailDialog'

type AssetType = 'Vehicle' | 'Equipment'
type PublicAsset = {
  id: string; assetNumber: string; name: string; type: AssetType; status: string
  branchId: string; branchName: string; dailyRate: number; isAvailable: boolean
  divisionId: string | null; divisionName: string | null; category: string | null
  personnelRequirement: 'None' | 'Optional' | 'Required'
  serviceName: string | null; requiresQuote: boolean; requiresDelivery: boolean
}
type CustomerPreference = 'NoPreference' | 'Vehicles' | 'Equipment' | 'WasteAndSiteHire'
type CheckoutDetail = {
  service: { requiresQuote: boolean; requiresDelivery: boolean; defaultDepositAmount: number; requiredDocuments: string[] } | null
  charges: { id: string; name: string; category: string; unit: string; defaultSellingRate: number; isRequired: boolean }[]
}
type Branch = { id: string; name: string; address: string | null; phone: string | null }
type PublicDivision = { id: string; code: string; name: string; description: string | null; capabilities: number }
type BookingForm = {
  fullName: string; customerType: 'Individual' | 'Business'; companyName: string
  email: string; phone: string; address: string; identificationNumber: string
  purpose: string; message: string; fulfilment: 'Pickup' | 'Delivery'; deliveryAddress: string
  siteContact: string; purchaseOrderNumber: string; personnelRequested: boolean
  personnelHours: number; driverName: string; driverLicence: string
}
const emptyBooking: BookingForm = {
  fullName: '', customerType: 'Individual', companyName: '', email: '', phone: '',
  address: '', identificationNumber: '', purpose: '', message: '', fulfilment: 'Pickup',
  deliveryAddress: '', siteContact: '', purchaseOrderNumber: '', personnelRequested: false,
  personnelHours: 8, driverName: '', driverLicence: '',
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
  if (/scissor|scaffold/.test(searchableName)) return '/catalog/scissor-lift.jpg'
  if (/generator|compressor/.test(searchableName)) return '/catalog/generator.jpg'
  if (/bin|portable toilet|portaloo/.test(searchableName)) return '/catalog/truck.jpg'
  return '/catalog/excavator.jpg'
}

function isEquipmentDivision(division: PublicDivision) {
  return /carptrac|shipping/i.test(`${division.code} ${division.name}`)
}

export function PublicRentalPage({ onCustomerAccount, customerAuthenticated = false }: { onCustomerAccount: () => void; customerAuthenticated?: boolean }) {
  const [assets, setAssets] = useState<PublicAsset[]>([])
  const [catalogueAssets, setCatalogueAssets] = useState<PublicAsset[]>([])
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
  const [detailAsset, setDetailAsset] = useState<PublicAsset | null>(null)
  const [checkoutDetail, setCheckoutDetail] = useState<CheckoutDetail | null>(null)
  const [selectedExtras, setSelectedExtras] = useState<Record<string, number>>({})
  const [customerPreference, setCustomerPreference] = useState<CustomerPreference>('NoPreference')
  const [booking, setBooking] = useState<BookingForm>(emptyBooking)
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState('')
  const [reference, setReference] = useState('')
  const [responseKind, setResponseKind] = useState<'Booking' | 'Quotation'>('Booking')
  const [bookingStep, setBookingStep] = useState(0)
  const [termsAccepted, setTermsAccepted] = useState(false)

  const loadBranches = useCallback(async () => {
    try { setBranches((await api.get<Branch[]>('/public/branches')).data) } catch { /* Contacts remain optional. */ }
  }, [])
  const loadDivisions = useCallback(async () => {
    try { setDivisions((await api.get<PublicDivision[]>('/public/divisions')).data) } catch { /* Catalogue still works without the selector. */ }
  }, [])
  const loadCatalogue = useCallback(async () => {
    setLoading(true)
    try {
      const catalogue = (await api.get<PublicAsset[]>('/public/assets')).data
      setCatalogueAssets(catalogue)
      setAssets(catalogue)
    }
    catch { setError('We could not load the rental catalogue. Please try again or contact a branch.') }
    finally { setLoading(false) }
  }, [])
  const checkAvailability = useCallback(async () => {
    if (!startDate || !endDate || endDate <= startDate) {
      setError('Choose a return date after the pickup date.')
      setSearched(false)
      return
    }
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
    // Public catalogue and contact data are intentionally available without authentication.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void loadBranches(); void loadDivisions(); void loadCatalogue()
  }, [loadBranches, loadCatalogue, loadDivisions])

  const displayedAssets = useMemo(() => (searched ? assets : catalogueAssets).filter((asset) => {
    if (divisionId && asset.divisionId !== divisionId) return false
    if (branchId && asset.branchId !== branchId) return false
    if (type && asset.type !== type) return false
    if (category === 'All') return true
    const value = `${asset.name} ${asset.category}`.toLowerCase()
    if (category === 'Cars & SUVs') return asset.type === 'Vehicle' && !/truck|van|urvan|staria|coach|seater/.test(value)
    if (category === 'Vans & trucks') return asset.type === 'Vehicle' && /truck|van|urvan|staria|coach|seater|cargo/.test(value)
    if (category === 'Earthmoving') return /excavator|backhoe|loader/.test(value)
    if (category === 'Lifting') return /crane|forklift|telehandler|scissor/.test(value)
    if (category === 'Power & site') return /generator|compressor|compactor|mixer|scaffold|toilet|portaloo|bin/.test(value)
    return true
  }), [assets, branchId, catalogueAssets, category, divisionId, searched, type])
  const availableCount = useMemo(() => displayedAssets.filter((asset) => asset.isAvailable).length, [displayedAssets])
  const rentalDays = Math.max(1, Math.ceil((new Date(`${endDate}T00:00:00`).getTime() - new Date(`${startDate}T00:00:00`).getTime()) / 86400000))
  const checkoutCharges = useMemo(() => (checkoutDetail?.charges ?? []).filter(charge => charge.isRequired ||
    (selectedExtras[charge.id] ?? 0) > 0 || booking.fulfilment === 'Delivery' && charge.category === 'Transport' ||
    booking.personnelRequested && ['Operator', 'Driver'].includes(charge.category)).map(charge => ({ ...charge,
      quantity: charge.unit === 'Day' ? rentalDays : charge.unit === 'Hour' ? booking.personnelHours : 1 })),
  [booking.fulfilment, booking.personnelHours, booking.personnelRequested, checkoutDetail, rentalDays, selectedExtras])
  const baseHire = (selected?.dailyRate ?? 0) * rentalDays
  const extrasTotal = checkoutCharges.reduce((sum, charge) => sum + charge.defaultSellingRate * charge.quantity, 0)
  const estimatedTax = (baseHire + extrasTotal) * .15
  const estimatedTotal = baseHire + extrasTotal + estimatedTax
  const quotationFlow = Boolean(selected && (selected.type === 'Equipment' || selected.requiresQuote || selected.personnelRequirement !== 'None' || booking.customerType === 'Business'))
  function scrollTo(id: string) { document.getElementById(id)?.scrollIntoView({ behavior: 'smooth' }); setMobileMenu(false) }
  function openRequest(asset: PublicAsset, details: CheckoutDetail, customerDetails: BookingForm = emptyBooking) { setSelected(asset); setCheckoutDetail(details); setSelectedExtras(Object.fromEntries(details.charges.filter(x => x.isRequired).map(x => [x.id, 1]))); setBooking({ ...customerDetails, fulfilment: details.service?.requiresDelivery ? 'Delivery' : customerDetails.fulfilment, personnelRequested: asset.personnelRequirement === 'Required' || customerDetails.personnelRequested }); setBookingStep(0); setTermsAccepted(false); setReference(''); setError('') }
  async function submitRequest(event: FormEvent) {
    event.preventDefault(); if (!selected) return
    setSubmitting(true); setError('')
    try {
      const response = await api.post<{ reference: string; requestType: string }>('/public/booking-requests', {
        assetId: selected.id, startDate, endDate, ...booking,
        extras: checkoutCharges.map(charge => ({ chargeDefinitionId: charge.id, quantity: charge.quantity })),
      })
      setReference(response.data.reference); setResponseKind(response.data.requestType as 'Booking' | 'Quotation')
    } catch (requestError: unknown) {
      const data = axios.isAxiosError(requestError) ? requestError.response?.data : undefined
      const errors = data?.errors as Record<string, string[]> | undefined
      setError(errors ? Object.values(errors).flat().join(' ') : 'Your request could not be submitted. Please contact a branch.')
    } finally { setSubmitting(false) }
  }

  function chooseDivision(division: PublicDivision) {
    setDivisionId(division.id)
    setType(isEquipmentDivision(division) ? 'Equipment' : 'Vehicle')
    setSearched(false)
    setTimeout(() => scrollTo('search'), 0)
  }
  async function requestBooking(asset: PublicAsset) {
    if (!customerAuthenticated) {
      sessionStorage.setItem('crems.pendingBooking', JSON.stringify({ assetId: asset.id, startDate, endDate }))
      onCustomerAccount()
      return
    }
    try {
      const account = (await api.get<{ fullName: string; type: 'Individual' | 'Business'; customerName: string; email: string; phone: string | null; address: string | null }>('/customer-account/session')).data
      const details = (await api.get<CheckoutDetail>(`/public/assets/${asset.id}`, { params: { startDate, endDate } })).data
      openRequest(asset, details, { ...emptyBooking, fullName: account.fullName, customerType: account.type,
        companyName: account.type === 'Business' ? account.customerName : '', email: account.email,
        phone: account.phone ?? '', address: account.address ?? '' })
    } catch { setError('We could not prepare this checkout. Please refresh availability and try again.') }
  }

  useEffect(() => {
    if (!customerAuthenticated || catalogueAssets.length === 0) return
    const raw = sessionStorage.getItem('crems.pendingBooking')
    if (!raw) return
    sessionStorage.removeItem('crems.pendingBooking')
    void (async () => {
      try {
        const pending = JSON.parse(raw) as { assetId: string; startDate: string; endDate: string }
        const asset = catalogueAssets.find(item => item.id === pending.assetId)
        if (!asset) return
        const availability = (await api.get<{ isAvailable: boolean }>(`/public/assets/${asset.id}`, { params: { startDate: pending.startDate, endDate: pending.endDate } })).data
        setStartDate(pending.startDate); setEndDate(pending.endDate); setSearched(true)
        if (!availability.isAvailable) { setError('This rental is no longer available for the selected dates. Please choose another option.'); return }
        await requestBooking({ ...asset, isAvailable: true })
      } catch { setError('We could not restore your rental selection. Please check availability again.') }
    })()
    // Resume exactly once from the browser-window booking hand-off.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [catalogueAssets, customerAuthenticated])

  useEffect(() => {
    if (!customerAuthenticated) return
    void api.get<{ hirePreference: CustomerPreference }>('/customer-account/session').then(({ data }) => {
      setCustomerPreference(data.hirePreference)
      if (data.hirePreference === 'Vehicles') { setType('Vehicle'); setCategory('All') }
      else if (data.hirePreference === 'Equipment') { setType('Equipment'); setCategory('All') }
      else if (data.hirePreference === 'WasteAndSiteHire') { setType('Equipment'); setCategory('Power & site') }
    }).catch(() => undefined)
  }, [customerAuthenticated])

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
        <Typography variant="h6" color="rgba(255,255,255,.7)" maxWidth={650} mt={2}>Browse vehicles, heavy equipment and selected site-hire services across Carpenters divisions. Check prices and live availability without creating an account.</Typography>
        <Stack direction={{ xs: 'column', sm: 'row' }} gap={1.5} mt={3} alignItems={{ sm: 'center' }}><Button size="large" variant="contained" color="secondary" endIcon={<SearchOutlined />} onClick={() => scrollTo('search')}>Check availability</Button><Button size="large" variant="outlined" color="inherit" onClick={onCustomerAccount}>{customerAuthenticated ? 'Manage my bookings' : 'Sign in to book'}</Button><Typography variant="body2" color="rgba(255,255,255,.6)">No account needed to browse</Typography></Stack>
      </Container>
    </Box>

    <Container id="services" maxWidth="lg" sx={{ py: { xs: 5, md: 7 } }}>
      <Typography variant="h3" fontWeight={800} sx={{ fontSize: { xs: '2rem', md: '3rem' } }}>What would you like to hire?</Typography>
      <Typography color="text.secondary" mt={1}>Start with the division that matches your need. You can return and change it at any time.</Typography>
      <Grid container spacing={3} mt={1}>{divisions.map((division) => {
        const equipment = isEquipmentDivision(division)
        const active = divisionId === division.id
        return <Grid key={division.id} size={{ xs: 12, md: divisions.length > 2 ? 4 : 6 }}><Card variant="outlined" onClick={() => chooseDivision(division)} sx={{ cursor: 'pointer', height: '100%', borderColor: active ? 'secondary.main' : undefined, borderWidth: active ? 2 : 1, transition: 'transform .2s ease, border-color .2s ease', '&:hover': { transform: 'translateY(-3px)', borderColor: 'secondary.main' } }}><CardContent sx={{ p: 3.5 }}><Avatar sx={{ bgcolor: 'secondary.main', color: '#111', width: 58, height: 58 }}>{equipment ? <ConstructionOutlined /> : <DirectionsCarOutlined />}</Avatar><Typography variant="h5" fontWeight={800} mt={2}>{division.name}</Typography><Typography color="text.secondary" mt={1}>{division.description || (equipment ? 'Equipment and site services for commercial, industrial and project work.' : 'Cars, SUVs, pickups, vans and other vehicles for personal or business travel.')}</Typography><Button endIcon={<ArrowForwardOutlined />} sx={{ mt: 2 }}>View rentals</Button></CardContent></Card></Grid>
      })}</Grid>
    </Container>

    <Container id="search" maxWidth="lg" sx={{ position: 'relative' }}>
      <Card elevation={5}><CardContent sx={{ p: { xs: 2.5, md: 3 } }}><Grid container spacing={2} alignItems="end">
        <Grid size={{ xs: 12, md: 2 }}><FormControl fullWidth><InputLabel>Hire from</InputLabel><Select label="Hire from" value={divisionId} onChange={(e) => { const value = e.target.value; setDivisionId(value); setSearched(false); const division = divisions.find(x => x.id === value); setType(division ? (isEquipmentDivision(division) ? 'Equipment' : 'Vehicle') : '') }}><MenuItem value="">All divisions</MenuItem>{divisions.map((division) => <MenuItem key={division.id} value={division.id}>{division.name}</MenuItem>)}</Select></FormControl></Grid>
        <Grid size={{ xs: 12, md: 2 }}><FormControl fullWidth><InputLabel>Pickup branch</InputLabel><Select label="Pickup branch" value={branchId} onChange={(e) => { setBranchId(e.target.value); setSearched(false) }}><MenuItem value="">All branches</MenuItem>{branches.map((branch) => <MenuItem key={branch.id} value={branch.id}>{branch.name}</MenuItem>)}</Select></FormControl></Grid>
        <Grid size={{ xs: 12, sm: 6, md: 1.5 }}><FormControl fullWidth><InputLabel>Rental type</InputLabel><Select label="Rental type" value={type} onChange={(e) => { setType(e.target.value as AssetType | ''); setSearched(false) }}><MenuItem value="">All</MenuItem><MenuItem value="Vehicle">Vehicles</MenuItem><MenuItem value="Equipment">Equipment</MenuItem></Select></FormControl></Grid>
        <Grid size={{ xs: 12, sm: 6, md: 2 }}><TextField fullWidth type="date" label="Pickup date" InputLabelProps={{ shrink: true }} inputProps={{ min: dateInputValue(0) }} value={startDate} onChange={(e) => { setStartDate(e.target.value); setSearched(false) }} /></Grid>
        <Grid size={{ xs: 12, sm: 6, md: 2 }}><TextField fullWidth type="date" label="Return date" InputLabelProps={{ shrink: true }} inputProps={{ min: startDate }} value={endDate} onChange={(e) => { setEndDate(e.target.value); setSearched(false) }} /></Grid>
        <Grid size={{ xs: 12, sm: 6, md: 2.5 }}><Button fullWidth size="large" variant="contained" startIcon={<SearchOutlined />} onClick={() => void checkAvailability()} sx={{ minHeight: 56 }}>Check availability</Button></Grid>
      </Grid></CardContent></Card>
    </Container>

    <Container id="rentals" maxWidth="lg" sx={{ py: 9 }}>
      {customerPreference !== 'NoPreference' && <Alert severity="info" sx={{ mb: 3 }} action={<Button color="inherit" onClick={() => { setCustomerPreference('NoPreference'); setType(''); setCategory('All') }}>Show everything</Button>}>Your catalogue starts with {customerPreference === 'Vehicles' ? 'vehicles' : customerPreference === 'Equipment' ? 'construction and industrial equipment' : 'waste and site-hire equipment'} based on your account preference.</Alert>}
      <Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" gap={2} mb={3}><Box><Typography variant="h3" fontWeight={800} sx={{ fontSize: { xs: '2rem', md: '3rem' } }}>Rental availability</Typography><Typography color="text.secondary" mt={1}>{searched ? `${availableCount} of ${displayedAssets.length} options available from ${startDate} to ${endDate}` : 'Choose your dates above to check live availability. No login is required.'}</Typography></Box>
        <Chip label={searched ? 'Availability checked' : 'Public availability search'} color={searched ? 'success' : 'default'} variant="outlined" icon={searched ? <CheckCircleOutlined /> : <SearchOutlined />} sx={{ alignSelf: 'flex-start' }} /></Stack>
      <Alert severity={searched ? (availableCount > 0 ? 'success' : 'warning') : 'info'} sx={{ mb: 3 }}>
        {searched ? (availableCount > 0 ? 'These results are for your selected dates. Sign in is only required when you continue with a booking.' : 'No items are available for this search. Try another branch, division or date range.') : 'Browse the catalogue below, then use Check availability to confirm an item for your hire period.'}
      </Alert>
      <Stack direction="row" gap={1} flexWrap="wrap" mb={3}>{['All', 'Cars & SUVs', 'Vans & trucks', 'Earthmoving', 'Lifting', 'Power & site'].map(item => <Chip key={item} clickable label={item} color={category === item ? 'primary' : 'default'} variant={category === item ? 'filled' : 'outlined'} onClick={() => setCategory(item)} />)}</Stack>
      {error && !selected && <Alert severity="error" sx={{ mb: 3 }}>{error}</Alert>}
      {loading ? <Box sx={{ py: 10, display: 'grid', placeItems: 'center' }}><CircularProgress /></Box> : <Grid container spacing={3}>
        {displayedAssets.length === 0 && <Grid size={12}><Card variant="outlined"><CardContent sx={{ textAlign: 'center', py: 8 }}><Typography variant="h6">No rentals match this search</Typography><Typography color="text.secondary">Try another category, branch or date range.</Typography><Button sx={{ mt: 2 }} onClick={() => setCategory('All')}>Clear category</Button></CardContent></Card></Grid>}
        {displayedAssets.map((asset) => <Grid key={asset.id} size={{ xs: 12, sm: 6, lg: 4 }}><Card variant="outlined" sx={{ height: '100%', overflow: 'hidden' }}>
          <Box sx={{ height: 210, bgcolor: '#e8e8e2', position: 'relative', overflow: 'hidden' }}>
            <Box component="img" src={catalogueImage(asset)} alt={`${asset.name} available from ${asset.branchName}`} loading="lazy" sx={{ width: '100%', height: '100%', display: 'block', objectFit: 'cover', transition: 'transform .3s ease', '.MuiCard-root:hover &': { transform: 'scale(1.035)' } }} />
            <Box sx={{ position: 'absolute', inset: 0, background: 'linear-gradient(180deg, rgba(0,0,0,.04) 45%, rgba(0,0,0,.36) 100%)', pointerEvents: 'none' }} />
            <Chip label={!searched ? 'Check your dates' : asset.isAvailable ? 'Available for your dates' : 'Unavailable for your dates'} color={searched && asset.isAvailable ? 'success' : 'default'} size="small" sx={{ position: 'absolute', top: 14, right: 14, bgcolor: searched && asset.isAvailable ? undefined : 'white' }} />
          </Box><CardContent sx={{ p: 2.5 }}><Typography variant="overline" color="text.secondary">{asset.divisionName || asset.type} · {asset.assetNumber}</Typography><Typography variant="h6" fontWeight={750}>{asset.name}</Typography>
            {asset.personnelRequirement !== 'None' && <Chip size="small" sx={{ mt: 1 }} label={`Trained personnel ${asset.personnelRequirement.toLowerCase()}`} color={asset.personnelRequirement === 'Required' ? 'warning' : 'default'} />}
            <Stack direction="row" alignItems="center" gap={.5} mt={1}><LocationOnOutlined fontSize="small" color="action" /><Typography variant="body2" color="text.secondary">{asset.branchName}</Typography></Stack>
            <Divider sx={{ my: 2 }} /><Stack direction="row" justifyContent="space-between" alignItems="end" gap={2}><Box><Typography variant="caption" color="text.secondary">Estimated for {rentalDays} {rentalDays === 1 ? 'day' : 'days'}</Typography><Typography variant="h5" fontWeight={800}>${(asset.dailyRate * rentalDays).toFixed(2)}<Typography component="span" variant="body2" color="text.secondary"> FJD</Typography></Typography><Typography variant="caption" color="text.secondary">${asset.dailyRate.toFixed(2)} per day</Typography></Box>
              <Stack gap={1} alignItems="flex-end"><Button size="small" onClick={() => setDetailAsset(asset)}>View details</Button><Button variant="contained" disabled={!searched || !asset.isAvailable} endIcon={<ArrowForwardOutlined />} onClick={() => void requestBooking(asset)}>{!searched ? 'Check dates first' : !customerAuthenticated ? 'Sign in to continue' : asset.type === 'Equipment' || asset.requiresQuote || asset.personnelRequirement !== 'None' ? 'Request quotation' : 'Book now'}</Button></Stack></Stack>
            <Typography variant="caption" color="text.secondary" display="block" mt={1.5}>Estimate excludes VAT, deposit, delivery, fuel and optional operator charges. Final price is confirmed before approval.</Typography>
          </CardContent></Card></Grid>)}
      </Grid>}
    </Container>

    <Box sx={{ bgcolor: 'secondary.main', py: { xs: 6, md: 7 } }}><Container maxWidth="md"><Stack direction={{ xs: 'column', md: 'row' }} alignItems={{ md: 'center' }} justifyContent="space-between" gap={3}><Box><Typography variant="h4" fontWeight={800}>Ready to book or track a booking?</Typography><Typography mt={1} sx={{ opacity: .72 }}>Sign in to submit bookings securely and see every status update in one place.</Typography></Box><Button size="large" variant="contained" onClick={onCustomerAccount} sx={{ bgcolor: '#111', color: 'white', minWidth: 190, '&:hover': { bgcolor: '#292929' } }}>{customerAuthenticated ? 'Open my bookings' : 'Customer login'}</Button></Stack></Container></Box>

    <Box id="how-it-works" sx={{ bgcolor: '#111', color: 'white', py: 9 }}><Container maxWidth="lg"><Typography variant="h3" fontWeight={800} textAlign="center" sx={{ fontSize: { xs: '2rem', md: '3rem' } }}>Simple from search to pickup</Typography><Grid container spacing={3} mt={3}>
      {[['1', 'Choose a division', 'Select the Carpenters division that provides the vehicle, equipment or site service you need.'], ['2', 'Compare options', 'Check prices, branches and live availability without signing in.'], ['3', 'Sign in and book', 'Use your customer account to submit and track bookings securely.']].map(([number, title, text]) => <Grid key={number} size={{ xs: 12, md: 4 }}><Stack alignItems="center" textAlign="center"><Avatar sx={{ bgcolor: 'secondary.main', color: '#111', fontWeight: 800, width: 52, height: 52 }}>{number}</Avatar><Typography variant="h6" fontWeight={700} mt={2}>{title}</Typography><Typography color="rgba(255,255,255,.65)" mt={1}>{text}</Typography></Stack></Grid>)}
    </Grid></Container></Box>

    <Container maxWidth="lg" sx={{ py: 8 }}><Grid container spacing={3}>{[[<VerifiedOutlined />, 'Maintained and inspected', 'Availability and maintenance status are managed by the operating branch.'], [<SupportAgentOutlined />, 'Local support', 'A Carpenters team member reviews every request and confirms collection requirements.'], [<LocalShippingOutlined />, 'Pickup or delivery planning', 'Equipment transport and trained personnel can be included in the final quote.']].map(([icon, title, text]) => <Grid key={String(title)} size={{ xs: 12, md: 4 }}><Card variant="outlined" sx={{ height: '100%' }}><CardContent sx={{ p: 3 }}><Avatar sx={{ bgcolor: 'secondary.main', color: '#111' }}>{icon}</Avatar><Typography variant="h6" fontWeight={750} mt={2}>{title}</Typography><Typography color="text.secondary" mt={1}>{text}</Typography></CardContent></Card></Grid>)}</Grid></Container>

    <Box id="faq" sx={{ bgcolor: 'white', py: 8 }}><Container maxWidth="md"><Typography variant="h3" fontWeight={800} textAlign="center" sx={{ fontSize: { xs: '2rem', md: '2.7rem' } }}>Before you make a request</Typography><Typography color="text.secondary" textAlign="center" mt={1} mb={4}>Clear answers to common rental questions.</Typography>{[['Is the displayed price final?', 'No. It is an estimated base hire charge. VAT, deposit, delivery, fuel, damage waiver, excess usage and operator charges may apply. Staff confirm the complete quote before approval.'], ['What do I need at pickup?', 'Individual vehicle customers normally need valid identification, an appropriate driver licence, their booking reference and an accepted payment method. Business and equipment hires may require a purchase order, site details or approved operator.'], ['Does an online request reserve the asset?', 'The request is held for staff review. It becomes confirmed only after eligibility, availability, pricing and any deposit or documentation requirements are approved.'], ['Can Carptrac equipment include an operator?', 'Yes, where the service supports it. Some assets require trained personnel, while others offer personnel as an option. This is shown on the asset card and confirmed in the quote.']].map(([question, answer]) => <Accordion key={question} disableGutters elevation={0} sx={{ borderBottom: 1, borderColor: 'divider' }}><AccordionSummary expandIcon={<ExpandMoreOutlined />}><Typography fontWeight={700}>{question}</Typography></AccordionSummary><AccordionDetails><Typography color="text.secondary">{answer}</Typography></AccordionDetails></Accordion>)}</Container></Box>

    <Container id="contact" maxWidth="lg" sx={{ py: 9 }}><Typography variant="h3" fontWeight={800} sx={{ fontSize: { xs: '2rem', md: '3rem' } }}>Contact our branches</Typography><Typography color="text.secondary" mt={1} mb={4}>Need advice before requesting? Speak with a local rental team.</Typography><Grid container spacing={3}>
      {branches.map((branch) => <Grid key={branch.id} size={{ xs: 12, md: 6 }}><Card variant="outlined"><CardContent sx={{ p: 3 }}><Typography variant="h6" fontWeight={750}>{branch.name}</Typography><Stack gap={1.25} mt={2}>{branch.address && <Stack direction="row" gap={1}><LocationOnOutlined color="action" /><Typography color="text.secondary">{branch.address}</Typography></Stack>}{branch.phone && <Stack direction="row" gap={1}><PhoneOutlined color="action" /><Link href={`tel:${branch.phone}`} color="inherit">{branch.phone}</Link></Stack>}</Stack></CardContent></Card></Grid>)}
    </Grid></Container>
    <Box sx={{ bgcolor: '#080808', color: 'rgba(255,255,255,.65)', py: 3 }}><Container maxWidth="lg"><Typography variant="body2">© {new Date().getFullYear()} Carpenters Fiji — Vehicle & Equipment Rentals</Typography></Container></Box>

    <PublicAssetDetailDialog asset={detailAsset} imageUrl={detailAsset ? catalogueImage(detailAsset) : ''} startDate={startDate} endDate={endDate} open={Boolean(detailAsset)} onClose={() => setDetailAsset(null)} onContinue={(asset) => { setDetailAsset(null); void requestBooking(asset as PublicAsset) }} />

    <Dialog open={Boolean(selected)} onClose={() => !submitting && setSelected(null)} fullWidth maxWidth="lg"><DialogTitle>{reference ? 'Request received' : quotationFlow ? 'Request a quotation' : 'Book your vehicle'}</DialogTitle><DialogContent dividers>
      {reference ? <Stack alignItems="center" textAlign="center" py={4}><CheckCircleOutlined color="success" sx={{ fontSize: 70 }} /><Typography variant="h5" fontWeight={750} mt={2}>{responseKind === 'Quotation' ? 'Quotation request submitted' : 'Booking request submitted'}</Typography><Typography color="text.secondary" mt={1}>{responseKind === 'Quotation' ? 'A rental specialist will review transport, personnel and final pricing, normally within one business day.' : 'The branch will verify your details and confirm pickup requirements.'}</Typography><Chip label={`Reference: ${reference}`} sx={{ mt: 3, fontWeight: 700, fontSize: '1rem', py: 2.25 }} /><Button sx={{ mt: 2 }} onClick={onCustomerAccount}>{responseKind === 'Quotation' ? 'View my quotations' : 'View my bookings'}</Button></Stack> :
      <Box component="form" id="public-booking-form" onSubmit={bookingStep === 3 ? submitRequest : (event) => { event.preventDefault(); setBookingStep(step => step + 1) }}>
        <Stepper activeStep={bookingStep} alternativeLabel sx={{ py: 2.5 }}>{['Dates & branch', 'Rental & extras', 'Your details', 'Price & submit'].map(label => <Step key={label}><StepLabel>{label}</StepLabel></Step>)}</Stepper>
        <Grid container spacing={3}>{error && <Grid size={12}><Alert severity="error">{error}</Alert></Grid>}<Grid size={{ xs: 12, md: 8 }}><Stack spacing={2.25}>
          {bookingStep === 0 && <><Typography variant="h6" fontWeight={750}>When and where?</Typography><Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}><TextField fullWidth required type="date" label="Pickup date" InputLabelProps={{ shrink: true }} inputProps={{ min: dateInputValue(0) }} value={startDate} onChange={e => setStartDate(e.target.value)} /><TextField fullWidth required type="date" label="Return date" InputLabelProps={{ shrink: true }} inputProps={{ min: startDate }} value={endDate} onChange={e => setEndDate(e.target.value)} /></Stack><Card variant="outlined"><CardContent><Stack direction="row" gap={1.5}><LocationOnOutlined color="action" /><Box><Typography fontWeight={700}>{selected?.branchName}</Typography><Typography variant="body2" color="text.secondary">This item is supplied by this branch. Search again to choose another location.</Typography></Box></Stack></CardContent></Card><Alert severity="info">Availability and pricing are revalidated when you submit.</Alert></>}
          {bookingStep === 1 && <>
            <Typography variant="h6" fontWeight={750}>Rental and optional services</Typography>
            <Card variant="outlined"><CardContent><Typography variant="overline" color="text.secondary">Selected rental</Typography><Typography variant="h6" fontWeight={750}>{selected?.name}</Typography><Typography color="text.secondary">{selected?.serviceName ?? selected?.category ?? selected?.type} · {selected?.assetNumber}</Typography></CardContent></Card>
            <FormControl fullWidth><InputLabel>Pickup or delivery</InputLabel><Select label="Pickup or delivery" value={booking.fulfilment} onChange={e => setBooking({ ...booking, fulfilment: e.target.value as 'Pickup' | 'Delivery' })}><MenuItem value="Pickup">Pickup from {selected?.branchName}</MenuItem><MenuItem value="Delivery">Deliver to my address or worksite</MenuItem></Select></FormControl>
            {selected?.personnelRequirement !== 'None' && <FormControlLabel control={<Checkbox disabled={selected?.personnelRequirement === 'Required'} checked={booking.personnelRequested} onChange={e => setBooking({ ...booking, personnelRequested: e.target.checked })} />} label={selected?.personnelRequirement === 'Required' ? 'Trained operator is required for this equipment' : 'Add a trained operator'} />}
            {checkoutDetail?.charges.filter(x => !x.isRequired).map(charge => <Card variant="outlined" key={charge.id}><CardContent sx={{ py: 1.5 }}><Stack direction="row" justifyContent="space-between" alignItems="center" gap={2}><FormControlLabel control={<Checkbox checked={(selectedExtras[charge.id] ?? 0) > 0} onChange={e => setSelectedExtras({ ...selectedExtras, [charge.id]: e.target.checked ? 1 : 0 })} />} label={charge.name} /><Typography fontWeight={700}>${charge.defaultSellingRate.toFixed(2)} / {charge.unit.toLowerCase()}</Typography></Stack></CardContent></Card>)}
          </>}
          {bookingStep === 2 && <><Typography variant="h6" fontWeight={750}>Customer and fulfilment details</Typography><TextField fullWidth required label="Contact person" value={booking.fullName} onChange={e => setBooking({ ...booking, fullName: e.target.value })} /><Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}><TextField fullWidth required type="email" label="Email" value={booking.email} disabled /><TextField fullWidth required label="Phone" value={booking.phone} onChange={e => setBooking({ ...booking, phone: e.target.value })} /></Stack>{selected?.type === 'Vehicle' && <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}><TextField fullWidth required label="Driver name" value={booking.driverName} onChange={e => setBooking({ ...booking, driverName: e.target.value })} /><TextField fullWidth required label="Driver licence number" value={booking.driverLicence} onChange={e => setBooking({ ...booking, driverLicence: e.target.value })} /></Stack>}{booking.fulfilment === 'Delivery' && <><TextField required label="Delivery / worksite address" value={booking.deliveryAddress} onChange={e => setBooking({ ...booking, deliveryAddress: e.target.value })} /><TextField label="Site contact and access instructions" value={booking.siteContact} onChange={e => setBooking({ ...booking, siteContact: e.target.value })} /></>}{booking.personnelRequested && <TextField required type="number" label="Estimated operator/driver hours" inputProps={{ min: 1, max: 1000 }} value={booking.personnelHours} onChange={e => setBooking({ ...booking, personnelHours: Number(e.target.value) })} />}{booking.customerType === 'Business' && <TextField label="Purchase order number" value={booking.purchaseOrderNumber} onChange={e => setBooking({ ...booking, purchaseOrderNumber: e.target.value })} />}<TextField label="Rental purpose" value={booking.purpose} onChange={e => setBooking({ ...booking, purpose: e.target.value })} /><TextField label="Additional requirements" multiline minRows={2} value={booking.message} onChange={e => setBooking({ ...booking, message: e.target.value })} /></>}
          {bookingStep === 3 && <><Typography variant="h6" fontWeight={750}>Review and submit</Typography><Alert severity={quotationFlow ? 'info' : 'success'}><Typography fontWeight={700}>{quotationFlow ? 'Quotation workflow' : 'Simple vehicle booking'}</Typography><Typography variant="body2">{quotationFlow ? 'A specialist will confirm availability, delivery, operator requirements and negotiated rates before you accept anything.' : 'The branch will verify your licence, deposit and pickup requirements before final confirmation.'}</Typography></Alert><Card variant="outlined"><CardContent><Typography fontWeight={750}>{booking.fullName}</Typography><Typography color="text.secondary">{booking.phone} · {booking.email}</Typography><Divider sx={{ my: 2 }} /><Typography>{booking.fulfilment === 'Delivery' ? `Delivery to ${booking.deliveryAddress}` : `Pickup from ${selected?.branchName}`}</Typography>{booking.personnelRequested && <Typography>Trained personnel · approximately {booking.personnelHours} hours</Typography>}{booking.purchaseOrderNumber && <Typography>Purchase order {booking.purchaseOrderNumber}</Typography>}</CardContent></Card><FormControlLabel control={<Checkbox checked={termsAccepted} onChange={e => setTermsAccepted(e.target.checked)} />} label={quotationFlow ? 'I understand this submits a quotation request and I can review the final quotation before accepting it.' : 'I understand the booking is confirmed only after Carpenters verifies availability, eligibility, documents and any required deposit.'} /></>}
        </Stack></Grid><Grid size={{ xs: 12, md: 4 }}><Card variant="outlined" sx={{ position: { md: 'sticky' }, top: 16 }}><CardContent><Typography variant="overline" color="text.secondary">Booking summary</Typography><Typography variant="h6" fontWeight={800}>{selected?.name}</Typography><Typography variant="body2" color="text.secondary">{rentalDays} days · {selected?.branchName}</Typography><Divider sx={{ my: 2 }} /><Stack spacing={1}><Stack direction="row" justifyContent="space-between"><Typography>Base hire</Typography><Typography>{baseHire.toFixed(2)}</Typography></Stack>{checkoutCharges.map(charge => <Stack key={charge.id} direction="row" justifyContent="space-between" gap={1}><Typography variant="body2">{charge.name} × {charge.quantity}</Typography><Typography variant="body2">{(charge.defaultSellingRate * charge.quantity).toFixed(2)}</Typography></Stack>)}<Stack direction="row" justifyContent="space-between"><Typography>VAT (15%)</Typography><Typography>{estimatedTax.toFixed(2)}</Typography></Stack><Divider /><Stack direction="row" justifyContent="space-between"><Typography fontWeight={800}>Estimated total</Typography><Typography fontWeight={800}>FJD {estimatedTotal.toFixed(2)}</Typography></Stack>{(checkoutDetail?.service?.defaultDepositAmount ?? 0) > 0 && <Typography variant="caption" color="text.secondary">Deposit: FJD {checkoutDetail?.service?.defaultDepositAmount.toFixed(2)}</Typography>}</Stack><Typography variant="caption" color="text.secondary" display="block" mt={2}>Late return, excess usage, fuel, cleaning or damage charges apply only when relevant and are assessed after return.</Typography></CardContent></Card></Grid></Grid>
      </Box>}
    </DialogContent><DialogActions sx={{ p: 3 }}>{reference ? <Button onClick={() => setSelected(null)}>Close</Button> : <><Button onClick={() => bookingStep === 0 ? setSelected(null) : setBookingStep(step => step - 1)} disabled={submitting}>{bookingStep === 0 ? 'Cancel' : 'Back'}</Button><Box sx={{ flex: 1 }} /><Button form="public-booking-form" type="submit" variant="contained" disabled={submitting || (bookingStep === 3 && !termsAccepted)}>{submitting ? 'Sending…' : bookingStep === 3 ? quotationFlow ? 'Request quotation' : 'Submit booking' : 'Continue'}</Button></>}</DialogActions></Dialog>
  </Box>
}
