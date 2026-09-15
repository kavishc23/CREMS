import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import axios from 'axios'
import ArrowForwardOutlined from '@mui/icons-material/ArrowForwardOutlined'
import CheckCircleOutlined from '@mui/icons-material/CheckCircleOutlined'
import LocationOnOutlined from '@mui/icons-material/LocationOnOutlined'
import PhoneOutlined from '@mui/icons-material/PhoneOutlined'
import SearchOutlined from '@mui/icons-material/SearchOutlined'
import VerifiedOutlined from '@mui/icons-material/VerifiedOutlined'
import SupportAgentOutlined from '@mui/icons-material/SupportAgentOutlined'
import LocalShippingOutlined from '@mui/icons-material/LocalShippingOutlined'
import ImageOutlined from '@mui/icons-material/ImageOutlined'
import ExpandMoreOutlined from '@mui/icons-material/ExpandMoreOutlined'
import ChevronLeftOutlined from '@mui/icons-material/ChevronLeftOutlined'
import ChevronRightOutlined from '@mui/icons-material/ChevronRightOutlined'
import CloseOutlined from '@mui/icons-material/CloseOutlined'
import EditOutlined from '@mui/icons-material/EditOutlined'
import {
  Accordion, AccordionDetails, AccordionSummary, Alert, Avatar, Box, Button, Card, CardContent, Chip, CircularProgress,
  Checkbox, Container, Dialog, DialogActions, DialogContent, DialogTitle, Divider,
  FormControl, FormControlLabel, Grid, IconButton, InputLabel, Link,
  MenuItem, Select, Stack, Step, StepLabel, Stepper, Switch, TextField, Typography,
} from '@mui/material'
import { api } from '../api/client'
import { CustomerSiteHeader, type CustomerSiteSection } from '../components/CustomerSiteHeader'
import { PublicAssetDetailDialog } from '../components/PublicAssetDetailDialog'

type AssetType = 'Vehicle'|'Equipment'|'PassengerVehicle'|'CommercialVehicle'|'HeavyEquipment'|'MaterialHandlingEquipment'|'PowerEquipment'|'LightEquipment'|'Scaffolding'|'PortableSanitation'|'WasteContainer'
type PublicAsset = {
  id: string; assetNumber: string; name: string; type: AssetType; status: string
  branchId: string; branchName: string; dailyRate: number; isAvailable: boolean
  divisionId: string | null; divisionName: string | null; category: string | null; categoryCode: string | null
  personnelRequirement: 'None' | 'Optional' | 'Required'
  serviceName: string | null; requiresQuote: boolean; requiresDelivery: boolean
  photoUrlsJson: string
  attributes: { code: string; name: string; value: string; unit: string | null }[]
}
type CustomerPreference = 'Vehicles' | 'Equipment' | 'WasteAndSiteHire'
type CustomerPreferenceSession = { hirePreferences?: CustomerPreference[]; hirePreference?: CustomerPreference | 'NoPreference' }
type CatalogueSort = 'recommended' | 'price-low' | 'price-high'
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

function assetPhotoUrls(asset: PublicAsset) {
  try {
    const uploaded = JSON.parse(asset.photoUrlsJson || '[]') as string[]
    return uploaded.filter(url => typeof url === 'string' && url.startsWith('/api/public/assets/'))
      // Invalidate missing-image responses cached before catalogue files were restored.
      .map(url => `${url}${url.includes('?') ? '&' : '?'}v=2`)
  } catch { return [] }
}

function catalogueFeatures(asset: PublicAsset) {
  const features = (asset.attributes ?? []).map(attribute => attribute.code === 'SEATS'
    ? `${attribute.value} seats`
    : `${attribute.value}${attribute.unit ? ` ${attribute.unit}` : ''}`)
  if (asset.category && features.length < 3) features.push(asset.category)
  if (asset.requiresDelivery) features.push('Delivery arranged')
  if (asset.personnelRequirement === 'Required') features.push('Operator included in quote')
  else if (asset.personnelRequirement === 'Optional') features.push('Operator available')
  return [...new Set(features)].slice(0, 3)
}

function assetAttribute(asset: PublicAsset, code: string) {
  return (asset.attributes ?? []).find(attribute => attribute.code === code)?.value ?? ''
}

function requiresProfessionalPersonnel(asset: PublicAsset | null) {
  if (!asset) return false
  return asset.personnelRequirement === 'Required'
}

function isVehicleAsset(asset:PublicAsset|null){return Boolean(asset&&['Vehicle','PassengerVehicle','CommercialVehicle'].includes(asset.type))}

function allowsProfessionalPersonnel(asset: PublicAsset | null) {
  return Boolean(asset && (isVehicleAsset(asset) || asset.personnelRequirement !== 'None'))
}

type CatalogueState = {
  divisionId: string; branchId: string; type: AssetType | ''; category: string; startDate: string; endDate: string
}

function readCatalogueState(): CatalogueState | null {
  try {
    const value = sessionStorage.getItem('crems.catalogueState')
    if (!value) return null
    const saved = JSON.parse(value) as CatalogueState
    const earliestPickup = dateInputValue(1)
    if (!saved.startDate || !saved.endDate || saved.startDate < earliestPickup || saved.endDate <= saved.startDate) {
      return { ...saved, startDate: earliestPickup, endDate: dateInputValue(3) }
    }
    return saved
  } catch { return null }
}

type PublicCatalogueBootstrap = { branches: Branch[]; divisions: PublicDivision[]; assets: PublicAsset[] }
let publicCatalogueCache: { expiresAt: number; promise: Promise<PublicCatalogueBootstrap> } | null = null

function loadPublicCatalogueBootstrap() {
  const now = Date.now()
  if (publicCatalogueCache && publicCatalogueCache.expiresAt > now) return publicCatalogueCache.promise
  const promise = Promise.all([
    api.get<Branch[]>('/public/branches'),
    api.get<PublicDivision[]>('/public/divisions'),
    api.get<PublicAsset[]>('/public/assets'),
  ]).then(([branchResponse, divisionResponse, assetResponse]) => ({
    branches: branchResponse.data,
    divisions: divisionResponse.data,
    assets: assetResponse.data,
  }))
  publicCatalogueCache = { expiresAt: now + 30_000, promise }
  promise.catch(() => { publicCatalogueCache = null })
  return promise
}

export function PublicRentalPage({ onCustomerAccount, onCustomerSignOut, customerAuthenticated = false, customerName }: { onCustomerAccount: (section?: 'overview' | 'bookings') => void; onCustomerSignOut?: () => void | Promise<void>; customerAuthenticated?: boolean; customerName?: string }) {
  const [trackingReference, setTrackingReference] = useState('')
  const [savedCatalogue] = useState(readCatalogueState)
  const [assets, setAssets] = useState<PublicAsset[]>([])
  const [catalogueAssets, setCatalogueAssets] = useState<PublicAsset[]>([])
  const [branches, setBranches] = useState<Branch[]>([])
  const [divisions, setDivisions] = useState<PublicDivision[]>([])
  const [divisionId, setDivisionId] = useState(savedCatalogue?.divisionId ?? '')
  const [branchId, setBranchId] = useState(savedCatalogue?.branchId ?? '')
  const [differentReturnLocation, setDifferentReturnLocation] = useState(false)
  const [returnBranchId, setReturnBranchId] = useState('')
  const [type, setType] = useState<AssetType | ''>(savedCatalogue?.type ?? '')
  const [category, setCategory] = useState(savedCatalogue?.category ?? 'All')
  const [sort, setSort] = useState<CatalogueSort>('recommended')
  const [seatFilter, setSeatFilter] = useState('')
  const [transmissionFilter, setTransmissionFilter] = useState('')
  const [priceFilter, setPriceFilter] = useState('')
  const [availableOnly, setAvailableOnly] = useState(false)
  const [startDate, setStartDate] = useState(savedCatalogue?.startDate ?? dateInputValue(1))
  const [endDate, setEndDate] = useState(savedCatalogue?.endDate ?? dateInputValue(3))
  const [loading, setLoading] = useState(true)
  const [searched, setSearched] = useState(false)
  const [selected, setSelected] = useState<PublicAsset | null>(null)
  const [detailAsset, setDetailAsset] = useState<PublicAsset | null>(null)
  const [checkoutDetail, setCheckoutDetail] = useState<CheckoutDetail | null>(null)
  const [selectedExtras, setSelectedExtras] = useState<Record<string, number>>({})
  const [booking, setBooking] = useState<BookingForm>(emptyBooking)
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState('')
  const [reference, setReference] = useState('')
  const [responseKind, setResponseKind] = useState<'Booking' | 'Quotation'>('Booking')
  const [requestQuotation, setRequestQuotation] = useState(false)
  const [bookingStep, setBookingStep] = useState(0)
  const [termsAccepted, setTermsAccepted] = useState(false)
  const [photoIndexes, setPhotoIndexes] = useState<Record<string, number>>({})
  const [galleryOffsets, setGalleryOffsets] = useState<Record<string, number>>({})
  const [hirePreferences, setHirePreferences] = useState<CustomerPreference[]>([])
  const [showModifySearch, setShowModifySearch] = useState(false)
  const [cancelConfirmOpen, setCancelConfirmOpen] = useState(false)

  function changeCataloguePhoto(assetId: string, photoCount: number, direction: number) {
    setPhotoIndexes(current => ({
      ...current,
      [assetId]: ((current[assetId] ?? 0) + direction + photoCount) % photoCount,
    }))
  }

  const checkAvailability = useCallback(async () => {
    if (!startDate || !endDate || endDate <= startDate) {
      setError('Choose a return date after the pickup date.')
      return
    }
    if (differentReturnLocation && !returnBranchId) {
      setError('Choose a return location or turn off the different return location option.')
      return
    }
    setLoading(true); setError('')
    try {
      const response = await api.get<PublicAsset[]>('/public/assets', { params: {
        branchId: branchId || undefined, divisionId: divisionId || undefined, type: type || undefined, startDate, endDate,
      } })
      setAssets(response.data); setSearched(true); setShowModifySearch(false)
    } catch { setError('We could not check availability. Please try again or contact a branch.') }
    finally { setLoading(false) }
  }, [branchId, differentReturnLocation, divisionId, endDate, returnBranchId, startDate, type])

  useEffect(() => {
    let active = true
    void loadPublicCatalogueBootstrap()
      .then(data => {
        if (!active) return
        setBranches(data.branches)
        setDivisions(data.divisions)
        setCatalogueAssets(data.assets)
        setAssets(data.assets)
      })
      .catch(() => { if (active) setError('We could not load the rental catalogue. Please try again or contact a branch.') })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [])

  const displayedAssets = useMemo(() => (searched ? assets : catalogueAssets).filter((asset) => {
    if (!searched && hirePreferences.length > 0 && hirePreferences.length < 3) {
      const preferred = (hirePreferences.includes('Vehicles') && /carpenters motors/i.test(asset.divisionName ?? '')) ||
        (hirePreferences.includes('Equipment') && /carptrac/i.test(asset.divisionName ?? '')) ||
        (hirePreferences.includes('WasteAndSiteHire') && /carpenters shipping/i.test(asset.divisionName ?? ''))
      if (!preferred) return false
    }
    if (divisionId && asset.divisionId !== divisionId) return false
    if (branchId && asset.branchId !== branchId) return false
    if (type && asset.type !== type) return false
    if (availableOnly && searched && !asset.isAvailable) return false
    if (seatFilter && Number(assetAttribute(asset, 'SEATS')) < Number(seatFilter)) return false
    if (transmissionFilter && assetAttribute(asset, 'TRANSMISSION') !== transmissionFilter) return false
    if (priceFilter === 'under-200' && asset.dailyRate >= 200) return false
    if (priceFilter === '200-500' && (asset.dailyRate < 200 || asset.dailyRate > 500)) return false
    if (priceFilter === 'over-500' && asset.dailyRate <= 500) return false
    if (category === 'All') return true
    const value = `${asset.name} ${asset.category}`.toLowerCase()
    if (category === 'Cars & SUVs') return isVehicleAsset(asset) && !/truck|van|urvan|staria|coach|seater/.test(value)
    if (category === 'Vans & trucks') return isVehicleAsset(asset) && /truck|van|urvan|staria|coach|seater|cargo/.test(value)
    if (category === 'Earthmoving') return /excavator|backhoe|loader/.test(value)
    if (category === 'Lifting') return /crane|forklift|telehandler|scissor/.test(value)
    if (category === 'Power & site') return /generator|compressor|compactor|mixer|scaffold|toilet|portaloo|bin/.test(value)
    return true
  }), [assets, availableOnly, branchId, catalogueAssets, category, divisionId, hirePreferences, priceFilter, searched, seatFilter, transmissionFilter, type])
  const seatOptions = useMemo(() => [...new Set(catalogueAssets.map(asset => assetAttribute(asset, 'SEATS')).filter(Boolean).map(Number))].sort((a, b) => a - b), [catalogueAssets])
  const transmissionOptions = useMemo(() => [...new Set(catalogueAssets.map(asset => assetAttribute(asset, 'TRANSMISSION')).filter(Boolean))].sort(), [catalogueAssets])
  const sortedAssets = useMemo(() => [...displayedAssets].sort((left, right) => {
    if (sort === 'price-low') return left.dailyRate - right.dailyRate
    if (sort === 'price-high') return right.dailyRate - left.dailyRate
    return Number(right.isAvailable) - Number(left.isAvailable) || left.dailyRate - right.dailyRate
  }), [displayedAssets, sort])
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
  const quotationFlow = Boolean(requestQuotation || selected && (selected.requiresQuote || selected.personnelRequirement !== 'None' || booking.personnelRequested))
  function scrollTo(id: string) { document.getElementById(id)?.scrollIntoView({ behavior: 'smooth' }) }
  function openRequest(asset: PublicAsset, details: CheckoutDetail, customerDetails: BookingForm = emptyBooking, quote = false) { setRequestQuotation(quote); setSelected(asset); setCheckoutDetail(details); setSelectedExtras(Object.fromEntries(details.charges.filter(x => x.isRequired).map(x => [x.id, 1]))); setBooking({ ...customerDetails, fulfilment: details.service?.requiresDelivery ? 'Delivery' : customerDetails.fulfilment, personnelRequested: requiresProfessionalPersonnel(asset) || customerDetails.personnelRequested }); setBookingStep(0); setTermsAccepted(false); setReference(''); setError('') }
  async function submitRequest(event: FormEvent) {
    event.preventDefault(); if (!selected) return
    setSubmitting(true); setError('')
    try {
      const response = await api.post<{ reference: string; requestType: string }>('/public/booking-requests', {
        assetId: selected.id, startDate, endDate, ...booking,
        requestQuotation: quotationFlow, extras: checkoutCharges.map(charge => ({ chargeDefinitionId: charge.id, quantity: charge.quantity })),
      })
      setReference(response.data.reference); setResponseKind(response.data.requestType as 'Booking' | 'Quotation')
    } catch (requestError: unknown) {
      const data = axios.isAxiosError(requestError) ? requestError.response?.data : undefined
      const errors = data?.errors as Record<string, string[]> | undefined
      setError(errors ? Object.values(errors).flat().join(' ') : 'Your request could not be submitted. Please contact a branch.')
    } finally { setSubmitting(false) }
  }

  async function requestBooking(asset: PublicAsset, quote = false) {
    if (!customerAuthenticated) {
      sessionStorage.setItem('crems.pendingBooking', JSON.stringify({ assetId: asset.id, startDate, endDate, quote }))
      onCustomerAccount()
      return
    }
    try {
      const account = (await api.get<{ fullName: string; customerName: string; email: string; phone: string | null; address: string | null }>('/customer-account/session')).data
      const details = (await api.get<CheckoutDetail>(`/public/assets/${asset.id}`, { params: { startDate, endDate } })).data
      openRequest(asset, details, { ...emptyBooking, fullName: account.fullName, customerType: 'Individual',
        companyName: '', email: account.email,
        phone: account.phone ?? '', address: account.address ?? '' }, quote)
    } catch { setError('We could not prepare this checkout. Please refresh availability and try again.') }
  }

  useEffect(() => {
    if (!customerAuthenticated || catalogueAssets.length === 0) return
    const raw = sessionStorage.getItem('crems.pendingBooking')
    if (!raw) return
    sessionStorage.removeItem('crems.pendingBooking')
    void (async () => {
      try {
        const pending = JSON.parse(raw) as { assetId: string; startDate: string; endDate: string; quote?: boolean }
        const asset = catalogueAssets.find(item => item.id === pending.assetId)
        if (!asset) return
        const availability = (await api.get<{ isAvailable: boolean }>(`/public/assets/${asset.id}`, { params: { startDate: pending.startDate, endDate: pending.endDate } })).data
        setStartDate(pending.startDate); setEndDate(pending.endDate); setSearched(true)
        if (!availability.isAvailable) { setError('This rental is no longer available for the selected dates. Please choose another option.'); return }
        await requestBooking({ ...asset, isAvailable: true }, pending.quote)
      } catch { setError('We could not restore your rental selection. Please check availability again.') }
    })()
    // Resume exactly once from the browser-window booking hand-off.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [catalogueAssets, customerAuthenticated])

  useEffect(() => {
    sessionStorage.setItem('crems.catalogueState', JSON.stringify({ divisionId, branchId, type, category, startDate, endDate }))
  }, [branchId, category, divisionId, endDate, startDate, type])

  useEffect(() => {
    const target = sessionStorage.getItem('crems.publicTarget')
    if (!target) return
    sessionStorage.removeItem('crems.publicTarget')
    window.setTimeout(() => scrollTo(target), 0)
  }, [])

  useEffect(() => {
    if (!customerAuthenticated) return
    void api.get<CustomerPreferenceSession>('/customer-account/session').then(({ data }) => {
      const preferences = Array.isArray(data.hirePreferences) ? data.hirePreferences : data.hirePreference && data.hirePreference !== 'NoPreference' ? [data.hirePreference] : []
      setHirePreferences(preferences)
    }).catch(() => undefined)
  }, [customerAuthenticated])

  function renderSearchForm(compact = false) {
    return <Box>
      <Grid container spacing={2} alignItems="end">
        <Grid size={{ xs: 12, md: differentReturnLocation ? 2.5 : compact ? 4.5 : 6 }}><FormControl fullWidth><InputLabel>Pickup location</InputLabel><Select label="Pickup location" value={branchId} onChange={(e) => { setBranchId(e.target.value); if (returnBranchId === e.target.value) setReturnBranchId(''); if (!compact) setSearched(false) }}><MenuItem value="">All participating locations</MenuItem>{branches.map(branch => <MenuItem key={branch.id} value={branch.id}>{branch.name}</MenuItem>)}</Select></FormControl></Grid>
        {differentReturnLocation && <Grid size={{ xs: 12, md: 2.5 }}><FormControl fullWidth><InputLabel>Return location</InputLabel><Select label="Return location" value={returnBranchId} onChange={(e) => { setReturnBranchId(e.target.value); if (!compact) setSearched(false) }}><MenuItem value="">Select location</MenuItem>{branches.filter(branch => branch.id !== branchId).map(branch => <MenuItem key={branch.id} value={branch.id}>{branch.name}</MenuItem>)}</Select></FormControl></Grid>}
        <Grid size={{ xs: 12, sm: 6, md: differentReturnLocation ? 2 : compact ? 2.25 : 3 }}><TextField fullWidth type="date" label="Pickup date" InputLabelProps={{ shrink: true }} inputProps={{ min: dateInputValue(0) }} value={startDate} onChange={(e) => { setStartDate(e.target.value); if (!compact) setSearched(false) }} /></Grid>
        <Grid size={{ xs: 12, sm: 6, md: differentReturnLocation ? 2 : compact ? 2.25 : 3 }}><TextField fullWidth type="date" label="Return date" InputLabelProps={{ shrink: true }} inputProps={{ min: startDate }} value={endDate} onChange={(e) => { setEndDate(e.target.value); if (!compact) setSearched(false) }} /></Grid>
        {compact && <Grid size={{ xs: 12, md: 3 }}><Button fullWidth size="large" variant="contained" startIcon={<SearchOutlined />} onClick={() => void checkAvailability()} sx={{ minHeight: 56 }}>Update results</Button></Grid>}
        <Grid size={12}><FormControlLabel sx={{ m: 0 }} control={<Switch checked={differentReturnLocation} onChange={event => { setDifferentReturnLocation(event.target.checked); if (!event.target.checked) setReturnBranchId(''); if (!compact) setSearched(false) }} />} label={<Box><Typography variant="body2" fontWeight={700}>Return to a different location</Typography><Typography variant="caption" color="text.secondary">A one-way or transfer fee may apply.</Typography></Box>} /></Grid>
        {!compact && <Grid size={12}><Button fullWidth size="large" variant="contained" startIcon={<SearchOutlined />} onClick={() => void checkAvailability()} sx={{ minHeight: 58 }}>Show available rentals</Button></Grid>}
      </Grid>
    </Box>
  }

  const divisionGalleries = useMemo(() => [
    { key: 'motors', title: 'Carpenters Motors', matches: (asset: PublicAsset) => /carpenters motors/i.test(asset.divisionName ?? '') },
    { key: 'carptrac', title: 'Carptrac CAT', matches: (asset: PublicAsset) => /carptrac/i.test(asset.divisionName ?? '') },
    { key: 'shipping', title: 'Carpenters Shipping', matches: (asset: PublicAsset) => /carpenters shipping/i.test(asset.divisionName ?? '') },
  ], [])

  function moveGallery(key: string, total: number, direction: number) {
    setGalleryOffsets(current => ({ ...current, [key]: Math.max(0, Math.min((current[key] ?? 0) + direction, Math.max(0, total - 4))) }))
  }

  function resetToSearch() {
    setSearched(false); setShowModifySearch(false); setDivisionId(''); setBranchId(''); setType(''); setCategory('All'); setSeatFilter(''); setTransmissionFilter(''); setPriceFilter(''); setAvailableOnly(false)
    window.setTimeout(() => { document.getElementById('search')?.scrollIntoView({ behavior: 'smooth' }); (document.querySelector('#search input') as HTMLInputElement | null)?.focus() }, 0)
  }

  function cancelBooking() {
    setSelected(null); setBooking(emptyBooking); setSelectedExtras({}); setBookingStep(0); setTermsAccepted(false); setReference(''); setStartDate(dateInputValue(1)); setEndDate(dateInputValue(3)); setCancelConfirmOpen(false)
  }

  function renderRentalCard(asset: PublicAsset) {
    const photos = assetPhotoUrls(asset)
    const photoIndex = Math.min(photoIndexes[asset.id] ?? 0, Math.max(photos.length - 1, 0))
    return <Card key={asset.id} variant="outlined" sx={{ width: '100%', minWidth: 0, overflow: 'hidden', borderRadius: 3, bgcolor: '#fff', borderColor: '#deddd6', borderTop: '4px solid', borderTopColor: 'secondary.main' }}>
      <CardContent sx={{ p: 2, pb: 1 }}><Stack direction="row" justifyContent="space-between" alignItems="flex-start" gap={1}><Box><Typography fontWeight={850} lineHeight={1.2}>{asset.name}</Typography><Typography variant="caption" fontWeight={700} color="text.secondary">{asset.category ?? asset.serviceName ?? asset.type} · {asset.assetNumber}</Typography></Box><Chip label={!searched ? 'Preview' : asset.isAvailable ? 'Available' : 'Unavailable'} color={searched && asset.isAvailable ? 'success' : 'default'} size="small" /></Stack></CardContent>
      <Box sx={{ height: 150, position: 'relative', overflow: 'hidden', mx: 1.25, borderRadius: 2, bgcolor: '#f7f6ef' }}>{photos[photoIndex] ? <Box component="img" src={photos[photoIndex]} alt={`${asset.name}, photo ${photoIndex + 1} of ${photos.length}`} loading="lazy" sx={{ width: '100%', height: '100%', display: 'block', objectFit: 'contain', p: 1 }} /> : <Stack sx={{ height: '100%' }} alignItems="center" justifyContent="center"><ImageOutlined sx={{ fontSize: 40, color: 'grey.400' }} /><Typography variant="caption" color="text.secondary">Photo coming soon</Typography></Stack>}{photos.length > 1 && <><IconButton aria-label="Previous photo" onClick={() => changeCataloguePhoto(asset.id, photos.length, -1)} sx={{ position: 'absolute', left: 4, top: '50%', transform: 'translateY(-50%)', bgcolor: 'rgba(255,255,255,.92)' }}><ChevronLeftOutlined /></IconButton><IconButton aria-label="Next photo" onClick={() => changeCataloguePhoto(asset.id, photos.length, 1)} sx={{ position: 'absolute', right: 4, top: '50%', transform: 'translateY(-50%)', bgcolor: 'rgba(255,255,255,.92)' }}><ChevronRightOutlined /></IconButton></>}</Box>
      <CardContent sx={{ p: 2 }}><Typography variant="caption" color="text.secondary">{asset.branchName}</Typography><Stack direction="row" justifyContent="space-between" alignItems="center" gap={1} mt={.5}><Typography fontWeight={900}>FJD {asset.dailyRate.toFixed(2)} <Typography component="span" variant="caption" color="text.secondary">/ day</Typography></Typography><Stack direction="row" gap={.75}>{isVehicleAsset(asset)&&!asset.requiresQuote&&asset.personnelRequirement==='None'&&<Button size="small" variant="contained" disabled={!searched||!asset.isAvailable} onClick={()=>void requestBooking(asset,false)}>Book now</Button>}<Button size="small" variant={isVehicleAsset(asset)?'outlined':'contained'} disabled={!searched||!asset.isAvailable} onClick={()=>void requestBooking(asset,true)}>Get quote</Button></Stack></Stack><Button size="small" sx={{ mt: .75, px: 0 }} onClick={() => setDetailAsset(asset)}>View details</Button></CardContent>
    </Card>
  }

  return <Box sx={{ minHeight: '100vh', bgcolor: '#f7f7f3' }}>
    <CustomerSiteHeader authenticated={customerAuthenticated} accountName={customerName} onAccount={onCustomerAccount} onAccountSection={onCustomerAccount} onNavigate={(section: CustomerSiteSection) => section === 'top' || section === 'search' ? resetToSearch() : scrollTo(section)} onSignOut={onCustomerSignOut} />

    {!searched ? <Box id="top" sx={{ bgcolor: '#111', color: 'white', position: 'relative', overflow: 'hidden', py: { xs: 6, md: 9 } }}>
      <Box sx={{ position: 'absolute', width: 650, height: 650, borderRadius: '50%', bgcolor: 'secondary.main', opacity: .09, right: -160, top: -250 }} />
      <Container id="search" maxWidth="xl" sx={{ position: 'relative', px: { md: 5 } }}><Grid container spacing={{ xs: 4, md: 5 }} alignItems="center">
        <Grid size={{ xs: 12, md: 8 }}><Card elevation={10} sx={{ borderRadius: 3 }}><CardContent sx={{ p: { xs: 3, md: 5 } }}><Typography variant="h4" fontWeight={850} mb={1}>Find your rental</Typography><Typography variant="body1" color="text.secondary" mb={3.5}>Choose a location and hire dates to see live availability.</Typography>{renderSearchForm()}</CardContent></Card></Grid>
        <Grid size={{ xs: 12, md: 4 }}><Typography color="secondary.main" fontWeight={800} letterSpacing={1.5}>CARPENTERS FIJI RENTALS</Typography><Typography variant="h2" fontWeight={900} mt={1} sx={{ fontSize: { xs: '2.6rem', md: '3.7rem' } }}>Hire with confidence.</Typography><Typography variant="h6" color="rgba(255,255,255,.72)" mt={2}>Vehicles, heavy equipment and site-hire services with local branch support across Fiji.</Typography><Button sx={{ mt: 3 }} size="large" variant="outlined" color="inherit" onClick={() => onCustomerAccount(customerAuthenticated ? 'bookings' : undefined)}>{customerAuthenticated ? 'Manage my bookings' : 'Sign in to book'}</Button></Grid>
      </Grid></Container>
    </Box> : <Box id="top" sx={{ bgcolor: 'white', borderBottom: 1, borderColor: 'divider', py: 2 }}><Container maxWidth="lg">
      <Stack direction={{ xs: 'column', md: 'row' }} alignItems={{ md: 'center' }} justifyContent="space-between" gap={2}><Box><Typography variant="caption" color="text.secondary">YOUR RENTAL SEARCH</Typography><Typography fontWeight={800}>{branches.find(branch => branch.id === branchId)?.name ?? 'All participating locations'}{differentReturnLocation && returnBranchId ? ` → ${branches.find(branch => branch.id === returnBranchId)?.name}` : ''}</Typography><Typography variant="body2" color="text.secondary">{startDate} to {endDate} · {rentalDays} {rentalDays === 1 ? 'day' : 'days'}</Typography></Box><Button variant="outlined" startIcon={<EditOutlined />} onClick={() => setShowModifySearch(true)}>Modify search</Button></Stack>
      {showModifySearch && <Card elevation={8} sx={{ mt: 2, position: 'relative' }}><IconButton aria-label="Close modify search" onClick={() => setShowModifySearch(false)} sx={{ position: 'absolute', top: 8, right: 8, zIndex: 1 }}><CloseOutlined /></IconButton><CardContent sx={{ p: { xs: 2.5, md: 3 }, pt: { xs: 6, md: 3 } }}><Typography variant="h6" fontWeight={850} mb={2}>Modify location or dates</Typography>{renderSearchForm(true)}</CardContent></Card>}
    </Container></Box>}

    <Container id="rentals" maxWidth={false} sx={{ py: { xs: 5, md: 6 }, px: { xs: 2, md: 5, lg: 7 } }}>
      {error && !selected && <Alert severity="error" sx={{ mb: 3 }}>{error}</Alert>}
      {searched && <Card variant="outlined" sx={{ mb: 3, bgcolor: 'white', borderRadius: 3 }}><CardContent sx={{ p: 2.5 }}><Stack direction="row" gap={1.25} alignItems="center" flexWrap="wrap" sx={{ '& > .MuiFormControl-root': { flex: '1 1 145px' } }}>
        <FormControl size="small" sx={{ minWidth: 175 }}><InputLabel>Division</InputLabel><Select MenuProps={{ disableScrollLock: true }} label="Division" value={divisionId} onChange={event => { const value = event.target.value; setDivisionId(value); setType('') }}><MenuItem value="">All divisions</MenuItem>{divisions.map(division => <MenuItem key={division.id} value={division.id}>{division.name}</MenuItem>)}</Select></FormControl>
        <FormControl size="small" sx={{ minWidth: 170 }}><InputLabel>Rental category</InputLabel><Select MenuProps={{ disableScrollLock: true }} label="Rental category" value={category} onChange={event => setCategory(event.target.value)}>{['All', 'Cars & SUVs', 'Vans & trucks', 'Earthmoving', 'Lifting', 'Power & site'].map(item => <MenuItem key={item} value={item}>{item === 'All' ? 'All categories' : item}</MenuItem>)}</Select></FormControl>
        {seatOptions.length > 0 && <FormControl size="small" sx={{ minWidth: 125 }}><InputLabel>Seats</InputLabel><Select MenuProps={{ disableScrollLock: true }} label="Seats" value={seatFilter} onChange={event => setSeatFilter(event.target.value)}><MenuItem value="">Any seats</MenuItem>{seatOptions.map(seats => <MenuItem key={seats} value={String(seats)}>{seats}+ seats</MenuItem>)}</Select></FormControl>}
        {transmissionOptions.length > 0 && <FormControl size="small" sx={{ minWidth: 150 }}><InputLabel>Transmission</InputLabel><Select MenuProps={{ disableScrollLock: true }} label="Transmission" value={transmissionFilter} onChange={event => setTransmissionFilter(event.target.value)}><MenuItem value="">Any</MenuItem>{transmissionOptions.map(option => <MenuItem key={option} value={option}>{option}</MenuItem>)}</Select></FormControl>}
        <FormControl size="small" sx={{ minWidth: 145 }}><InputLabel>Daily price</InputLabel><Select MenuProps={{ disableScrollLock: true }} label="Daily price" value={priceFilter} onChange={event => setPriceFilter(event.target.value)}><MenuItem value="">Any price</MenuItem><MenuItem value="under-200">Under FJD 200</MenuItem><MenuItem value="200-500">FJD 200–500</MenuItem><MenuItem value="over-500">Over FJD 500</MenuItem></Select></FormControl>
        <FormControl size="small" sx={{ minWidth: 180 }}><InputLabel>Sort by</InputLabel><Select MenuProps={{ disableScrollLock: true }} label="Sort by" value={sort} onChange={event => setSort(event.target.value as CatalogueSort)}><MenuItem value="recommended">Recommended</MenuItem><MenuItem value="price-low">Price: low to high</MenuItem><MenuItem value="price-high">Price: high to low</MenuItem></Select></FormControl>
        {(divisionId || category !== 'All' || seatFilter || transmissionFilter || priceFilter) && <Button size="small" onClick={() => { setDivisionId(''); setType(''); setCategory('All'); setSeatFilter(''); setTransmissionFilter(''); setPriceFilter(''); setAvailableOnly(false) }}>Clear</Button>}
      </Stack></CardContent></Card>}
      {searched && <Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" alignItems={{ sm: 'center' }} gap={1.5} mb={3}><Box><Typography fontWeight={800}>{sortedAssets.length} {sortedAssets.length === 1 ? 'rental' : 'rentals'} found</Typography><Typography variant="body2" color="text.secondary">Live availability for your selected dates · Prices in FJD</Typography></Box><FormControlLabel control={<Switch checked={availableOnly} onChange={event => setAvailableOnly(event.target.checked)} />} label="Available only" /></Stack>}
      {false && <Grid container spacing={3}>
        <Grid size={12}>
          <Stack direction={{xs:'column',sm:'row'}} justifyContent="space-between" alignItems={{sm:'center'}} gap={1.5} mb={2}>
            <Box><Typography fontWeight={800}>{sortedAssets.length} {sortedAssets.length === 1 ? 'rental' : 'rentals'} found</Typography><Typography variant="body2" color="text.secondary">{branches.find(item => item.id === branchId)?.name ?? 'All participating locations'} · Prices in FJD</Typography></Box>
            <FormControlLabel control={<Switch checked={availableOnly} onChange={event => setAvailableOnly(event.target.checked)} />} label="Available only" />
          </Stack>
          {loading ? <Box sx={{ py: 10, display: 'grid', placeItems: 'center' }}><CircularProgress /></Box> : <Grid container spacing={3}>
        {displayedAssets.length === 0 && <Grid size={12}><Card variant="outlined"><CardContent sx={{ textAlign: 'center', py: 8 }}><Typography variant="h6">No rentals match this search</Typography><Typography color="text.secondary">Try another category, branch or date range.</Typography><Button sx={{ mt: 2 }} onClick={() => { setCategory('All'); setSeatFilter(''); setTransmissionFilter(''); setPriceFilter('') }}>Clear filters</Button></CardContent></Card></Grid>}
        {sortedAssets.map((asset) => {
          const photos = assetPhotoUrls(asset)
          const photoIndex = Math.min(photoIndexes[asset.id] ?? 0, Math.max(photos.length - 1, 0))
          return <Grid key={asset.id} size={{ xs: 12, sm: 6, lg: 4 }}><Card variant="outlined" sx={{ height: '100%', overflow: 'hidden', borderRadius: 3, bgcolor: '#fff', borderColor: '#deddd6', borderTop: '4px solid', borderTopColor: 'secondary.main', transition: 'transform .2s ease, box-shadow .2s ease', '&:hover': { transform: 'translateY(-3px)', boxShadow: '0 14px 30px rgba(0,0,0,.09)' } }}>
            <CardContent sx={{ p: 2.5, pb: 1 }}>
              <Stack direction="row" justifyContent="space-between" alignItems="flex-start" gap={1}>
                <Box><Typography variant="h5" fontWeight={850} lineHeight={1.15}>{asset.name}</Typography><Typography variant="body2" fontWeight={700} color="text.secondary" mt={.75}>{asset.category ?? asset.serviceName ?? asset.type} · {asset.assetNumber}</Typography></Box>
                <Chip label={!searched ? 'Check dates' : asset.isAvailable ? 'Available' : 'Unavailable'} color={searched && asset.isAvailable ? 'success' : 'default'} size="small" sx={{ bgcolor: searched && asset.isAvailable ? undefined : 'white' }} />
              </Stack>
              <Stack direction="row" gap={.75} flexWrap="wrap" mt={1.5}>{catalogueFeatures(asset).map(feature => <Chip key={feature} size="small" label={feature} sx={{ bgcolor: 'rgba(255,255,255,.7)' }} />)}</Stack>
            </CardContent>
            <Box sx={{ height: { xs: 250, md: 285 }, position: 'relative', overflow: 'hidden', mx: 1.5, borderRadius: 2, bgcolor: '#f7f6ef' }}>
              {photos[photoIndex] ? <Box component="img" src={photos[photoIndex]} alt={`${asset.name}, photo ${photoIndex + 1} of ${photos.length}`} loading="lazy" sx={{ width: '100%', height: '100%', display: 'block', objectFit: 'contain', p: 1.5 }} /> : <Stack sx={{height:'100%'}} alignItems="center" justifyContent="center"><ImageOutlined sx={{fontSize:52,color:'grey.400'}}/><Typography variant="body2" color="text.secondary">Photo coming soon</Typography></Stack>}
              {photos.length > 1 && <>
                <IconButton aria-label="Previous photo" onClick={() => changeCataloguePhoto(asset.id, photos.length, -1)} sx={{ position: 'absolute', left: 8, top: '50%', transform: 'translateY(-50%)', bgcolor: 'rgba(255,255,255,.92)', '&:hover': { bgcolor: 'white' } }}><ChevronLeftOutlined /></IconButton>
                <IconButton aria-label="Next photo" onClick={() => changeCataloguePhoto(asset.id, photos.length, 1)} sx={{ position: 'absolute', right: 8, top: '50%', transform: 'translateY(-50%)', bgcolor: 'rgba(255,255,255,.92)', '&:hover': { bgcolor: 'white' } }}><ChevronRightOutlined /></IconButton>
                <Stack direction="row" gap={.7} sx={{ position: 'absolute', bottom: 10, left: '50%', transform: 'translateX(-50%)', bgcolor: 'rgba(255,255,255,.88)', borderRadius: 5, px: 1, py: .65 }}>{photos.map((_, index) => <Box key={index} component="button" aria-label={`Show photo ${index + 1}`} onClick={() => setPhotoIndexes(current => ({ ...current, [asset.id]: index }))} sx={{ border: 0, p: 0, width: 7, height: 7, borderRadius: '50%', cursor: 'pointer', bgcolor: index === photoIndex ? '#111' : 'grey.400' }} />)}</Stack>
              </>}
            </Box>
            <CardContent sx={{ p: 2.5, pt: 2 }}>
              <Stack direction="row" alignItems="center" gap={.5}><LocationOnOutlined fontSize="small" color="action" /><Typography variant="body2" color="text.secondary">{asset.branchName} · {asset.divisionName || asset.type}</Typography></Stack>
              <Divider sx={{ my: 1.75 }} /><Stack direction="row" justifyContent="space-between" alignItems="flex-end" gap={1}><Box><Typography variant="h5" fontWeight={900}>FJD {asset.dailyRate.toFixed(2)}<Typography component="span" variant="body2" color="text.secondary"> / day</Typography></Typography><Typography variant="caption" color="text.secondary">FJD {(asset.dailyRate * rentalDays).toFixed(2)} estimated for {rentalDays} {rentalDays === 1 ? 'day' : 'days'}</Typography></Box><Stack direction="row" gap={1}>{isVehicleAsset(asset)&&!asset.requiresQuote&&asset.personnelRequirement==='None'&&<Button variant="contained" disabled={!searched||!asset.isAvailable} onClick={()=>void requestBooking(asset,false)}>Book now</Button>}<Button variant={isVehicleAsset(asset)?'outlined':'contained'} disabled={!searched||!asset.isAvailable} endIcon={<ArrowForwardOutlined/>} onClick={()=>void requestBooking(asset,true)}>Get quote</Button></Stack></Stack>
              <Button size="small" sx={{ mt: 1.25, px: 0 }} onClick={() => setDetailAsset(asset)}>View full details and photos</Button>
            </CardContent>
          </Card></Grid>
        })}
          </Grid>}
        </Grid>
      </Grid>}
      {!loading && divisionGalleries.map(gallery => {
        const galleryAssets = sortedAssets.filter(gallery.matches)
        const offset = Math.min(galleryOffsets[gallery.key] ?? 0, Math.max(0, galleryAssets.length - 4))
        const visibleAssets = galleryAssets.slice(offset, offset + 4)
        if (galleryAssets.length === 0) return null
        if (searched && divisionId && !gallery.matches({ divisionName: divisions.find(division => division.id === divisionId)?.name ?? '' } as PublicAsset)) return null
        if ((searched && divisionId) || (!searched && hirePreferences.length === 1)) return <Box key={gallery.key} sx={{ mb: 5 }}><Stack mb={2}><Typography variant="h5" fontWeight={850}>{gallery.title}</Typography><Typography variant="body2" color="text.secondary">{galleryAssets.length} rental{galleryAssets.length === 1 ? '' : 's'} to explore</Typography></Stack><Grid container spacing={2}>{galleryAssets.map(asset => <Grid key={asset.id} size={{ xs: 12, sm: 6, lg: 3 }}>{renderRentalCard(asset)}</Grid>)}</Grid></Box>
        return <Box key={gallery.key} sx={{ mb: 5 }}><Stack direction="row" alignItems="center" justifyContent="space-between" mb={1.5}><Box><Typography variant="h5" fontWeight={850}>{gallery.title}</Typography><Typography variant="body2" color="text.secondary">{galleryAssets.length ? `${galleryAssets.length} rental${galleryAssets.length === 1 ? '' : 's'} to explore` : 'Rental preview'}</Typography></Box></Stack>{visibleAssets.length ? <Stack direction="row" alignItems="center" gap={1.5}><IconButton aria-label={`Previous ${gallery.title} rentals`} disabled={offset === 0} onClick={() => moveGallery(gallery.key, galleryAssets.length, -1)} sx={{ width: 54, height: 54, flex: '0 0 auto', color: 'white', bgcolor: '#111', '&:hover': { bgcolor: '#333' } }}><ChevronLeftOutlined fontSize="large" /></IconButton><Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} sx={{ flex: 1, overflow: 'hidden', '& > *': { width: { sm: 'calc((100% - 48px) / 4)' }, minWidth: { sm: 0 } } }}>{visibleAssets.map(renderRentalCard)}</Stack><IconButton aria-label={`Next ${gallery.title} rentals`} disabled={offset >= Math.max(0, galleryAssets.length - 4)} onClick={() => moveGallery(gallery.key, galleryAssets.length, 1)} sx={{ width: 54, height: 54, flex: '0 0 auto', color: 'white', bgcolor: '#111', '&:hover': { bgcolor: '#333' } }}><ChevronRightOutlined fontSize="large" /></IconButton></Stack> : <Card variant="outlined"><CardContent><Typography color="text.secondary">No rentals are currently listed for this division.</Typography></CardContent></Card>}</Box>
      })}
    </Container>

    {!searched && <Box sx={{ bgcolor: 'secondary.main', py: { xs: 6, md: 7 } }}><Container maxWidth="lg"><Stack direction={{ xs: 'column', lg: 'row' }} alignItems={{ lg: 'center' }} justifyContent="space-between" gap={3}><Box><Typography variant="h4" fontWeight={800}>Ready to book or track a booking?</Typography><Typography mt={1} sx={{ opacity: .72 }}>Enter a booking or quotation reference to find it securely in your account.</Typography></Box><Box component="form" onSubmit={event=>{event.preventDefault();sessionStorage.setItem('crems.customerReferenceSearch',trackingReference.trim());onCustomerAccount('bookings')}} sx={{display:'flex',flexDirection:{xs:'column',sm:'row'},gap:1,width:{xs:'100%',lg:520}}}><TextField fullWidth placeholder="BK-2026-0001 or QUO-20260903-XXXXXX" value={trackingReference} onChange={event=>setTrackingReference(event.target.value)} sx={{bgcolor:'white',borderRadius:1}} InputProps={{startAdornment:<SearchOutlined sx={{mr:1,color:'text.secondary'}}/>}}/><Button type="submit" size="large" variant="contained" sx={{ bgcolor: '#111', color: 'white', minWidth: 150, '&:hover': { bgcolor: '#292929' } }}>{customerAuthenticated ? 'Find reference' : 'Sign in & find'}</Button></Box></Stack></Container></Box>}

    {!searched && <Box id="how-it-works" sx={{ bgcolor: '#111', color: 'white', py: 9 }}><Container maxWidth="lg"><Typography variant="h3" fontWeight={800} textAlign="center" sx={{ fontSize: { xs: '2rem', md: '3rem' } }}>Simple from search to pickup</Typography><Grid container spacing={3} mt={3}>
      {[['1', 'Choose a division', 'Select the Carpenters division that provides the vehicle, equipment or site service you need.'], ['2', 'Compare options', 'Check prices, branches and live availability without signing in.'], ['3', 'Sign in and book', 'Use your customer account to submit and track bookings securely.']].map(([number, title, text]) => <Grid key={number} size={{ xs: 12, md: 4 }}><Stack alignItems="center" textAlign="center"><Avatar sx={{ bgcolor: 'secondary.main', color: '#111', fontWeight: 800, width: 52, height: 52 }}>{number}</Avatar><Typography variant="h6" fontWeight={700} mt={2}>{title}</Typography><Typography color="rgba(255,255,255,.65)" mt={1}>{text}</Typography></Stack></Grid>)}
    </Grid></Container></Box>}

    {!searched && <Container maxWidth="lg" sx={{ py: 8 }}><Grid container spacing={3}>{[[<VerifiedOutlined />, 'Maintained and inspected', 'Availability and maintenance status are managed by the operating branch.'], [<SupportAgentOutlined />, 'Local support', 'A Carpenters team member reviews every request and confirms collection requirements.'], [<LocalShippingOutlined />, 'Pickup or delivery planning', 'Equipment transport and trained personnel can be included in the final quote.']].map(([icon, title, text]) => <Grid key={String(title)} size={{ xs: 12, md: 4 }}><Card variant="outlined" sx={{ height: '100%' }}><CardContent sx={{ p: 3 }}><Avatar sx={{ bgcolor: 'secondary.main', color: '#111' }}>{icon}</Avatar><Typography variant="h6" fontWeight={750} mt={2}>{title}</Typography><Typography color="text.secondary" mt={1}>{text}</Typography></CardContent></Card></Grid>)}</Grid></Container>}

    {!searched && <Box id="faq" sx={{ bgcolor: 'white', py: 8 }}><Container maxWidth="md"><Typography variant="h3" fontWeight={800} textAlign="center" sx={{ fontSize: { xs: '2rem', md: '2.7rem' } }}>Before you make a request</Typography><Typography color="text.secondary" textAlign="center" mt={1} mb={4}>Clear answers to common rental questions.</Typography>{[['Is the displayed price final?', 'No. It is an estimated base hire charge. VAT, refundable bond, delivery, fuel, damage waiver, excess usage and operator charges may apply. Staff confirm the complete quote before approval.'], ['What do I need at pickup?', 'Individual vehicle customers normally need valid identification, an appropriate driver licence, their booking reference and an accepted payment method. Business and equipment hires may require a purchase order, site details or approved operator.'], ['Does an online request reserve the asset?', 'The request is held for staff review. It becomes confirmed only after eligibility, availability, pricing and any refundable bond or documentation requirements are approved.'], ['Can Carptrac equipment include an operator?', 'Yes, where the service supports it. Some assets require trained personnel, while others offer personnel as an option. This is shown on the asset card and confirmed in the quote.']].map(([question, answer]) => <Accordion key={question} disableGutters elevation={0} sx={{ borderBottom: 1, borderColor: 'divider' }}><AccordionSummary expandIcon={<ExpandMoreOutlined />}><Typography fontWeight={700}>{question}</Typography></AccordionSummary><AccordionDetails><Typography color="text.secondary">{answer}</Typography></AccordionDetails></Accordion>)}</Container></Box>}

    <Box sx={{ borderTop: '4px solid', borderColor: 'secondary.main', bgcolor: '#f4f2ed' }}><Container id="contact" maxWidth="xl" sx={{ py: 9 }}><Typography variant="h3" fontWeight={800} sx={{ fontSize: { xs: '2rem', md: '3rem' } }}>Contact our branches</Typography><Typography color="text.secondary" mt={1} mb={4}>Need advice before requesting? Speak with a local rental team.</Typography><Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(min(280px, 100%), 1fr))', gap: { xs: 2.5, md: 4 } }}>
      {branches.map((branch) => <Card key={branch.id} variant="outlined" sx={{ height: '100%', borderRadius: 2 }}><CardContent sx={{ p: 3.5 }}><Typography variant="h6" fontWeight={750}>{branch.name}</Typography><Stack gap={1.5} mt={2}>{branch.address && <Stack direction="row" gap={1.25} alignItems="flex-start"><LocationOnOutlined color="action" /><Typography color="text.secondary">{branch.address}</Typography></Stack>}{branch.phone && <Stack direction="row" gap={1.25}><PhoneOutlined color="action" /><Link href={`tel:${branch.phone}`} color="inherit">{branch.phone}</Link></Stack>}</Stack></CardContent></Card>)}
    </Box></Container></Box>
    <Box sx={{ bgcolor: '#080808', color: 'rgba(255,255,255,.65)', py: 3 }}><Container maxWidth="lg"><Typography variant="body2">© {new Date().getFullYear()} Carpenters Fiji — Vehicle & Equipment Rentals</Typography></Container></Box>

    <PublicAssetDetailDialog asset={detailAsset} imageUrls={detailAsset ? assetPhotoUrls(detailAsset) : []} startDate={startDate} endDate={endDate} open={Boolean(detailAsset)} onClose={() => setDetailAsset(null)} onContinue={(asset) => { setDetailAsset(null); void requestBooking(asset as PublicAsset) }} />

    <Dialog disableScrollLock open={Boolean(selected)} onClose={() => !submitting && setCancelConfirmOpen(true)} fullWidth maxWidth="xl" slotProps={{ paper: { sx: { height: 'min(820px, calc(100dvh - 32px))', m: { xs: 1, md: 2 }, overflow: 'hidden' } } }}><DialogTitle sx={{ pr: 7, position: 'relative', py: 1.25 }}>{reference ? 'Request received' : quotationFlow ? 'Request a quotation' : 'Book your vehicle'}{!reference && <IconButton aria-label="Cancel booking" onClick={() => setCancelConfirmOpen(true)} sx={{ position: 'absolute', top: 5, right: 12 }}><CloseOutlined /></IconButton>}</DialogTitle><DialogContent dividers sx={{ p: { xs: 1.5, md: 2.5 }, overflowY: 'auto', overflowX: 'hidden' }}>
      {reference ? <Stack alignItems="center" textAlign="center" py={4}><CheckCircleOutlined color="success" sx={{ fontSize: 70 }} /><Typography variant="h5" fontWeight={750} mt={2}>{responseKind === 'Quotation' ? 'Quotation request submitted' : 'Booking request submitted'}</Typography><Typography color="text.secondary" mt={1}>{responseKind === 'Quotation' ? 'A rental specialist will review transport, personnel and final pricing, normally within one business day.' : 'The branch will verify your details and confirm pickup requirements.'}</Typography><Chip label={`Reference: ${reference}`} sx={{ mt: 3, fontWeight: 700, fontSize: '1rem', py: 2.25 }} /><Button sx={{ mt: 2 }} onClick={() => onCustomerAccount('bookings')}>{responseKind === 'Quotation' ? 'View my quotations' : 'View my bookings'}</Button></Stack> :
      <Box component="form" id="public-booking-form" onSubmit={bookingStep === 3 ? submitRequest : (event) => { event.preventDefault(); setBookingStep(step => step + 1) }}>
        <Stepper activeStep={bookingStep} alternativeLabel sx={{ py: { xs: .5, md: 1 } }}>{['Dates & branch', 'Rental & extras', 'Your details', 'Price & submit'].map(label => <Step key={label}><StepLabel>{label}</StepLabel></Step>)}</Stepper>
        <Grid container spacing={{ xs: 2, md: 2.5 }}>{error && <Grid size={12}><Alert severity="error">{error}</Alert></Grid>}<Grid size={{ xs: 12, md: 8 }}><Stack spacing={{ xs: 1.5, md: 2 }}>
          {bookingStep === 0 && <><Typography variant="h6" fontWeight={750}>When and where?</Typography><Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}><TextField fullWidth required type="date" label="Pickup date" InputLabelProps={{ shrink: true }} inputProps={{ min: dateInputValue(0) }} value={startDate} onChange={e => setStartDate(e.target.value)} /><TextField fullWidth required type="date" label="Return date" InputLabelProps={{ shrink: true }} inputProps={{ min: startDate }} value={endDate} onChange={e => setEndDate(e.target.value)} /></Stack><Card variant="outlined"><CardContent><Stack direction="row" gap={1.5}><LocationOnOutlined color="action" /><Box><Typography fontWeight={700}>{selected?.branchName}</Typography><Typography variant="body2" color="text.secondary">This item is supplied by this branch. Search again to choose another location.</Typography></Box></Stack></CardContent></Card><Alert severity="info">Availability and pricing are revalidated when you submit.</Alert></>}
          {bookingStep === 1 && <>
            <Typography variant="h6" fontWeight={750}>Rental and optional services</Typography>
            <Card variant="outlined"><CardContent><Typography variant="overline" color="text.secondary">Selected rental</Typography><Typography variant="h6" fontWeight={750}>{selected?.name}</Typography><Typography color="text.secondary">{selected?.serviceName ?? selected?.category ?? selected?.type} · {selected?.assetNumber}</Typography></CardContent></Card>
            <FormControl fullWidth><InputLabel>Pickup or delivery</InputLabel><Select label="Pickup or delivery" value={booking.fulfilment} onChange={e => setBooking({ ...booking, fulfilment: e.target.value as 'Pickup' | 'Delivery' })}><MenuItem value="Pickup">Pickup from {selected?.branchName}</MenuItem><MenuItem value="Delivery">Deliver to my address or worksite</MenuItem></Select></FormControl>
            {requiresProfessionalPersonnel(selected) && <Alert severity="info"><Typography fontWeight={750}>Professional operator included</Typography>This asset must be supplied with a qualified Carpenters operator. The operator charge is compulsory and is included in the estimate below.</Alert>}
            {!requiresProfessionalPersonnel(selected) && allowsProfessionalPersonnel(selected) && <FormControlLabel control={<Checkbox checked={booking.personnelRequested} onChange={e => setBooking({ ...booking, personnelRequested: e.target.checked })} />} label={isVehicleAsset(selected) ? 'Add a professional driver' : 'Add a trained operator'} />}
            {checkoutDetail?.charges.filter(x => !x.isRequired && !['Operator', 'Driver'].includes(x.category)).map(charge => <Card variant="outlined" key={charge.id}><CardContent sx={{ py: 1.5 }}><Stack direction="row" justifyContent="space-between" alignItems="center" gap={2}><FormControlLabel control={<Checkbox checked={(selectedExtras[charge.id] ?? 0) > 0} onChange={e => setSelectedExtras({ ...selectedExtras, [charge.id]: e.target.checked ? 1 : 0 })} />} label={charge.name} /><Typography fontWeight={700}>${charge.defaultSellingRate.toFixed(2)} / {charge.unit.toLowerCase()}</Typography></Stack></CardContent></Card>)}
          </>}
          {bookingStep === 2 && <><Typography variant="h6" fontWeight={750}>Customer and fulfilment details</Typography><Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, minmax(0, 1fr))' }, gap: { xs: 1.5, md: 2 } }}><TextField fullWidth required label="Contact person" value={booking.fullName} onChange={e => setBooking({ ...booking, fullName: e.target.value })} /><TextField fullWidth required type="email" label="Email" value={booking.email} disabled /><TextField fullWidth required label="Phone" value={booking.phone} onChange={e => setBooking({ ...booking, phone: e.target.value })} />{booking.personnelRequested ? <TextField fullWidth required label="Valid identification number" helperText="No licence is required when Carpenters supplies the operator." value={booking.identificationNumber} onChange={e => setBooking({ ...booking, identificationNumber: e.target.value })} /> : isVehicleAsset(selected) && <><TextField fullWidth required label="Driver name" value={booking.driverName} onChange={e => setBooking({ ...booking, driverName: e.target.value })} /><TextField fullWidth required label="Driver licence number" value={booking.driverLicence} onChange={e => setBooking({ ...booking, driverLicence: e.target.value })} /></>}{booking.fulfilment === 'Delivery' && <><TextField fullWidth required label="Delivery / worksite address" value={booking.deliveryAddress} onChange={e => setBooking({ ...booking, deliveryAddress: e.target.value })} /><TextField fullWidth label="Site contact and access instructions" value={booking.siteContact} onChange={e => setBooking({ ...booking, siteContact: e.target.value })} /></>}{booking.personnelRequested && <TextField fullWidth required type="number" label="Estimated driver/operator hours" inputProps={{ min: 1, max: 1000 }} value={booking.personnelHours} onChange={e => setBooking({ ...booking, personnelHours: Number(e.target.value) })} />}{booking.customerType === 'Business' && <TextField fullWidth label="Purchase order number" value={booking.purchaseOrderNumber} onChange={e => setBooking({ ...booking, purchaseOrderNumber: e.target.value })} />}<TextField fullWidth label="Rental purpose" value={booking.purpose} onChange={e => setBooking({ ...booking, purpose: e.target.value })} /><TextField fullWidth label="Additional requirements" value={booking.message} onChange={e => setBooking({ ...booking, message: e.target.value })} /></Box></>}
          {bookingStep === 3 && <><Typography variant="h6" fontWeight={750}>Review and submit</Typography><Alert severity={quotationFlow ? 'info' : 'success'}><Typography fontWeight={700}>{quotationFlow ? 'Quotation workflow' : 'Simple vehicle booking'}</Typography><Typography variant="body2">{quotationFlow ? 'A specialist will confirm availability, delivery, operator requirements and negotiated rates before you accept anything.' : 'The branch will verify your licence, refundable bond and pickup requirements before final confirmation.'}</Typography></Alert><Card variant="outlined"><CardContent><Typography fontWeight={750}>{booking.fullName}</Typography><Typography color="text.secondary">{booking.phone} · {booking.email}</Typography><Divider sx={{ my: 2 }} /><Typography>{booking.fulfilment === 'Delivery' ? `Delivery to ${booking.deliveryAddress}` : `Pickup from ${selected?.branchName}`}</Typography>{booking.personnelRequested && <Typography>Trained personnel · approximately {booking.personnelHours} hours</Typography>}{booking.purchaseOrderNumber && <Typography>Purchase order {booking.purchaseOrderNumber}</Typography>}</CardContent></Card><FormControlLabel control={<Checkbox checked={termsAccepted} onChange={e => setTermsAccepted(e.target.checked)} />} label={quotationFlow ? 'I understand this submits a quotation request and I can review the final quotation before accepting it.' : 'I understand the booking is confirmed only after Carpenters verifies availability, eligibility, documents and any required refundable bond.'} /></>}
        </Stack></Grid><Grid size={{ xs: 12, md: 4 }}><Card variant="outlined" sx={{ position: { md: 'sticky' }, top: 16 }}><CardContent><Typography variant="overline" color="text.secondary">Booking summary</Typography><Typography variant="h6" fontWeight={800}>{selected?.name}</Typography><Typography variant="body2" color="text.secondary">{rentalDays} days · {selected?.branchName}</Typography><Divider sx={{ my: 2 }} /><Stack spacing={1}><Stack direction="row" justifyContent="space-between"><Typography>Base hire</Typography><Typography>{baseHire.toFixed(2)}</Typography></Stack>{checkoutCharges.map(charge => <Stack key={charge.id} direction="row" justifyContent="space-between" gap={1}><Typography variant="body2">{charge.name} × {charge.quantity}</Typography><Typography variant="body2">{(charge.defaultSellingRate * charge.quantity).toFixed(2)}</Typography></Stack>)}<Stack direction="row" justifyContent="space-between"><Typography>VAT (15%)</Typography><Typography>{estimatedTax.toFixed(2)}</Typography></Stack><Divider /><Stack direction="row" justifyContent="space-between"><Typography fontWeight={800}>Estimated total</Typography><Typography fontWeight={800}>FJD {estimatedTotal.toFixed(2)}</Typography></Stack>{(checkoutDetail?.service?.defaultDepositAmount ?? 0) > 0 && <Typography variant="caption" color="text.secondary">Refundable bond: FJD {checkoutDetail?.service?.defaultDepositAmount.toFixed(2)}</Typography>}</Stack><Typography variant="caption" color="text.secondary" display="block" mt={2}>Late return, excess usage, fuel, cleaning or damage charges apply only when relevant and are assessed after return.</Typography></CardContent></Card></Grid></Grid>
      </Box>}
    </DialogContent><DialogActions sx={{ px: { xs: 2, md: 3 }, py: 1.5 }}>{reference ? <Button onClick={() => setSelected(null)}>Close</Button> : <><Button onClick={() => bookingStep === 0 ? setSelected(null) : setBookingStep(step => step - 1)} disabled={submitting}>{bookingStep === 0 ? 'Cancel' : 'Back'}</Button><Box sx={{ flex: 1 }} /><Button form="public-booking-form" type="submit" variant="contained" disabled={submitting || (bookingStep === 3 && !termsAccepted)}>{submitting ? 'Sending…' : bookingStep === 3 ? quotationFlow ? 'Request quotation' : 'Submit booking' : 'Continue'}</Button></>}</DialogActions></Dialog>
    <Dialog disableScrollLock open={cancelConfirmOpen} onClose={() => setCancelConfirmOpen(false)} maxWidth="xs" fullWidth><DialogTitle>Cancel this booking?</DialogTitle><DialogContent><Typography color="text.secondary">Your entered booking details will be deleted and cannot be restored.</Typography></DialogContent><DialogActions><Button onClick={() => setCancelConfirmOpen(false)}>Keep booking</Button><Button color="error" variant="contained" onClick={cancelBooking}>Cancel booking</Button></DialogActions></Dialog>
  </Box>
}
