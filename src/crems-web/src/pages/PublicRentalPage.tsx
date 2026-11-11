import { assetPhotoUrls } from '../utils/assetPhotoUrls'
import Popover from '@mui/material/Popover'
import TuneOutlined from '@mui/icons-material/TuneOutlined'
import { useSearchNavigationGuard } from '../components/useSearchNavigationGuard'
import { matchesHomepagePreferences } from '../utils/customerPreferences'
import { hireQuantity, measuredUnit } from '../utils/hirePricing'
import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import CheckCircleOutlined from '@mui/icons-material/CheckCircleOutlined'
import LocationOnOutlined from '@mui/icons-material/LocationOnOutlined'
import PhoneOutlined from '@mui/icons-material/PhoneOutlined'
import SearchOutlined from '@mui/icons-material/SearchOutlined'
import VerifiedOutlined from '@mui/icons-material/VerifiedOutlined'
import SupportAgentOutlined from '@mui/icons-material/SupportAgentOutlined'
import LocalShippingOutlined from '@mui/icons-material/LocalShippingOutlined'
import { RentalCatalogueCard } from '../components/RentalCatalogueCard'
import { useUniformCatalogueLayout } from '../components/useUniformCatalogueLayout'
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
import { ConfiguredCustomerFields } from '../components/ConfiguredCustomerFields'
import type { CustomerQuestion } from '../config/organizationSetup'
import { api } from '../api/client'
import { apiErrorMessage } from '../api/errors'
import { submitPublicBookingRequest } from '../api/publicBooking'
import { CustomerSiteHeader, type CustomerSiteSection } from '../components/CustomerSiteHeader'
import { PublicAssetDetailDialog } from '../components/PublicAssetDetailDialog'

type AssetType = 'Vehicle'|'Equipment'|'PassengerVehicle'|'CommercialVehicle'|'HeavyEquipment'|'MaterialHandlingEquipment'|'PowerEquipment'|'LightEquipment'|'Scaffolding'|'PortableSanitation'|'WasteContainer'|'PropertyUnit'
type PublicAsset = {
  id: string; assetNumber: string; name: string; type: AssetType; status: string
  branchId: string; branchName: string; dailyRate: number; hireUnit?:string; useServiceHireRate?:boolean; isAvailable: boolean
  fulfilmentMode?: 'PickupOnly' | 'DeliveryOnly' | 'PickupOrDelivery'; assetCategoryId?: string | null; divisionId: string | null; divisionName: string | null; category: string | null; categoryCode: string | null
  bondAmount: number; turnaroundMinutes?: number; nextAvailableAt?: string | null
  personnelRequirement: 'None' | 'Optional' | 'Required'
  serviceName: string | null; serviceOfferingId?: string | null; isBookableOnline?: boolean; allowsQuoteRequests?: boolean; requiresQuote: boolean; quoteMode: 'Automatic'|'Always'|'DirectWhenPriced'; requiresDelivery: boolean
  photoUrlsJson: string
  attributes: { code: string; name: string; value: string; unit: string | null }[]
}
type CustomerPreferenceSession = { preferredAssetCategoryIds?: string[] }

type CatalogueSort = 'recommended' | 'price-low' | 'price-high'
type CheckoutDetail = {
  customerQuestions?: CustomerQuestion[]
  hireRate?:{rate:number;unit:string;isTaxable:boolean;source:string}
  branch?: { name: string; address: string | null };
  bondAmount: number; taxRate: number; isAvailable: boolean
  bookingFlow: {quoteMode:'Automatic'|'Always'|'DirectWhenPriced';businessCustomersRequireQuote:boolean;unpricedPersonnelRequiresQuote:boolean;unpricedDeliveryRequiresQuote:boolean;requireVerifiedLicenceForVehicles:boolean;minimumHireDays:number;maximumHireDays:number}
  service: { requiresQuote: boolean; requiresDelivery: boolean; defaultDepositAmount: number; requiredDocuments: string[] } | null
  charges: { id: string; name: string; category: string; unit: string; defaultSellingRate: number; isRequired: boolean; isTaxable: boolean }[]
}
type Branch = { id: string; name: string; address: string | null; phone: string | null }
type PublicDivision = { id: string; code: string; name: string; description: string | null; capabilities: number; branding?: { displayName: string | null; logoUrl: string | null; accentColor: string | null } }
type BookingForm = {
  secondaryContact: string; fullName: string; customerType: 'Individual' | 'Business'; companyName: string
  email: string; phone: string; address: string; identificationNumber: string
  purpose: string; message: string; fulfilment: 'Pickup' | 'Delivery'; deliveryAddress: string
  siteContact: string; purchaseOrderNumber: string; personnelRequested: boolean
  hireQuantity:number; personnelHours: number | null; driverName: string; driverLicence: string; driverLicenceClasses: number[]
}
const emptyBooking: BookingForm = {
  secondaryContact: '', fullName: '', customerType: 'Individual', companyName: '', email: '', phone: '',
  address: '', identificationNumber: '', purpose: '', message: '', fulfilment: 'Pickup',
  deliveryAddress: '', siteContact: '', purchaseOrderNumber: '', personnelRequested: false,
  hireQuantity:1, personnelHours: null, driverName: '', driverLicence: '', driverLicenceClasses: [],
}

function dateInputValue(offsetDays: number) {
  const date = new Date(); date.setDate(date.getDate() + offsetDays)
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
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
  return Boolean(asset && asset.personnelRequirement !== 'None')
}

type PublicCatalogueBootstrap = { branches: Branch[]; divisions: PublicDivision[]; assets: PublicAsset[] }
let publicCatalogueCache: { expiresAt: number; promise: Promise<PublicCatalogueBootstrap> } | null = null

async function loadPublicResource<T>(url: string) {
  try {
    return (await api.get<T>(url)).data
  } catch {
    // Public catalogue reads are idempotent. Retrying once covers the brief
    // startup interval while the local API or database is becoming ready.
    return (await api.get<T>(url)).data
  }
}

function loadPublicCatalogueBootstrap() {
  const now = Date.now()
  if (publicCatalogueCache && publicCatalogueCache.expiresAt > now) return publicCatalogueCache.promise
  const promise = loadPublicResource<PublicCatalogueBootstrap>('/public/catalogue-bootstrap')
  publicCatalogueCache = { expiresAt: now + 30_000, promise }
  promise.catch(() => { publicCatalogueCache = null })
  return promise
}

export function PublicRentalPage({ onCustomerAccount, onCustomerSignOut, customerAuthenticated = false, customerName }: { onCustomerAccount: (section?: 'overview' | 'bookings' | 'preferences') => void; onCustomerSignOut?: () => void | Promise<void>; customerAuthenticated?: boolean; customerName?: string }) {
  const [trackingReference, setTrackingReference] = useState('')
  const catalogueRef = useUniformCatalogueLayout()
  const [assets, setAssets] = useState<PublicAsset[]>([])
  const [catalogueAssets, setCatalogueAssets] = useState<PublicAsset[]>([])
  const [branches, setBranches] = useState<Branch[]>([])
  const [divisions, setDivisions] = useState<PublicDivision[]>([])
  const [divisionId, setDivisionId] = useState('')
  const [branchId, setBranchId] = useState('')
  const [differentReturnLocation, setDifferentReturnLocation] = useState(false)
  const [returnBranchId, setReturnBranchId] = useState('')
  const [type, setType] = useState<AssetType | ''>('')
  const [category, setCategory] = useState('All')
  const [sort, setSort] = useState<CatalogueSort>('recommended')
  const [seatFilter, setSeatFilter] = useState('')
  const [transmissionFilter, setTransmissionFilter] = useState('')
  const [priceFilter, setPriceFilter] = useState('')
  const [availableOnly, setAvailableOnly] = useState(false)
  const [startDate, setStartDate] = useState(dateInputValue(0))
  const [endDate, setEndDate] = useState('')
  const [pickupTime, setPickupTime] = useState('09:00')
  const [returnTime, setReturnTime] = useState('09:00')
  const [loading, setLoading] = useState(true)
  const [searched, setSearched] = useState(false)
  const [searchDates, setSearchDates] = useState<{ startDate: string; endDate: string } | null>(null)
  const [selected, setSelected] = useState<PublicAsset | null>(null)
  const [detailAsset, setDetailAsset] = useState<PublicAsset | null>(null)
  const [checkoutDetail, setCheckoutDetail] = useState<CheckoutDetail | null>(null)
  const [selectedExtras, setSelectedExtras] = useState<Record<string, number>>({})
  const [booking, setBooking] = useState<BookingForm>(emptyBooking)
  const [configurationAnswers, setConfigurationAnswers] = useState<Record<string, string>>({})
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState('')
  const [catalogueRetry, setCatalogueRetry] = useState(0)
  const [reference, setReference] = useState('')
  const [responseKind, setResponseKind] = useState<'Booking' | 'Quotation'>('Booking')
  const [requestQuotation, setRequestQuotation] = useState(false)
  const [bookingStep, setBookingStep] = useState(0)
  const [termsAccepted, setTermsAccepted] = useState(false)
  const [photoIndexes, setPhotoIndexes] = useState<Record<string, number>>({})
  const [galleryOffsets, setGalleryOffsets] = useState<Record<string, number>>({})
  const [advancedFilterAnchor, setAdvancedFilterAnchor] = useState<HTMLElement | null>(null)
  const advancedFiltersOpen = Boolean(advancedFilterAnchor)
  const [resultPreferenceIds, setResultPreferenceIds] = useState<string[]>([])
  const [preferredCategoryIds, setPreferredCategoryIds] = useState<string[]>([])
  const [showModifySearch, setShowModifySearch] = useState(false)
  const [cancelConfirmOpen, setCancelConfirmOpen] = useState(false)
  const [deliveryConfirmOpen, setDeliveryConfirmOpen] = useState(false)
  const [requestBaseline, setRequestBaseline] = useState('')
  const searchNavigation = useSearchNavigationGuard(searched && !reference, () => { setResultPreferenceIds([]); setAttributeFilters({}); setSearched(false); setSearchDates(null); setAssets([]); setSelected(null); setDetailAsset(null); setStartDate(dateInputValue(0)); setEndDate(''); setError(''); setDivisionId(''); setBranchId(''); setCategory('All') })

  useEffect(() => {
    if (!selected) return
    let cancelled = false
    const refreshPricing = () => {
      if (document.visibilityState === 'hidden') return
      void api.get<CheckoutDetail>(`/public/assets/${selected.id}`, { params: { startDate, endDate, pickupTime, returnTime } })
        .then(({data}) => { if (!cancelled) setCheckoutDetail(data) })
        .catch(() => { if (!cancelled) setError('Unable to refresh pricing. Close and reopen this request before submitting.') })
    }
    window.addEventListener('focus', refreshPricing)
    window.addEventListener('crems:data-changed', refreshPricing)
    document.addEventListener('visibilitychange', refreshPricing)
    return () => { cancelled = true; window.removeEventListener('focus', refreshPricing); window.removeEventListener('crems:data-changed', refreshPricing); document.removeEventListener('visibilitychange', refreshPricing) }
  }, [selected, startDate, endDate, pickupTime, returnTime])

  function changeStartDate(value: string) {
    setStartDate(value)
    if (endDate && endDate < value) setEndDate('')
  }

  function changeCataloguePhoto(assetId: string, photoCount: number, direction: number) {
    setPhotoIndexes(current => ({
      ...current,
      [assetId]: ((current[assetId] ?? 0) + direction + photoCount) % photoCount,
    }))
  }

  const checkAvailability = useCallback(async () => {
    if (!startDate || startDate < dateInputValue(0) || !endDate || `${endDate}T${returnTime}` <= `${startDate}T${pickupTime}`) {
      setError('Choose a return time after pickup.')
      return
    }
    if (differentReturnLocation && !returnBranchId) {
      setError('Choose a return location or turn off the different return location option.')
      return
    }
    setLoading(true); setError('')
    try {
      const response = await api.get<PublicAsset[]>('/public/assets', { params: {
        branchId: branchId || undefined, startDate, endDate, pickupTime, returnTime,
      } })
      if (!window.history.state?.cremsSearchActive) { const url = new URL(window.location.href); url.searchParams.set('rentalResults', '1'); window.history.pushState({ ...window.history.state, cremsSearchActive: true }, '', url.pathname + url.search) }
      if (!searched) { setResultPreferenceIds(preferredCategoryIds); const ids = [...new Set(response.data.filter(a => preferredCategoryIds.includes(a.assetCategoryId ?? '')).map(a => a.divisionId))]; if (ids.length === 1) setDivisionId(ids[0] ?? ''); }
      setAssets(response.data); setSearchDates({ startDate, endDate }); setGalleryOffsets({}); setSearched(true); setShowModifySearch(false)
    } catch (reason) { setError(apiErrorMessage(reason, 'We could not check availability. Please try again or contact a branch.')) }
    finally { setLoading(false) }
  }, [branchId, differentReturnLocation, endDate, returnBranchId, startDate, pickupTime, returnTime, searched, preferredCategoryIds])

  useEffect(() => {
    let active = true
    void Promise.resolve().then(() => { if (active) { setError(''); setLoading(true) } })
    void loadPublicCatalogueBootstrap()
      .then(data => {
        if (!active) return
        setBranches(data.branches)
        setDivisions(data.divisions)
        setCatalogueAssets(data.assets)
        setAssets(data.assets)
      })
      .catch(reason => { if (active) setError(apiErrorMessage(reason, 'We could not load the rental catalogue. Please try again or contact a branch.')) })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [catalogueRetry])

  useEffect(() => {
    function refresh() {
      if (document.visibilityState === 'hidden' || selected) return
      publicCatalogueCache = null
      if (searched) void checkAvailability()
      else setCatalogueRetry(value => value + 1)
    }
    window.addEventListener('focus', refresh)
    window.addEventListener('crems:configuration-changed', refresh)
    return () => { window.removeEventListener('focus', refresh); window.removeEventListener('crems:configuration-changed', refresh) }
  }, [searched, selected, checkAvailability])

  const seatOptions = useMemo(() => [...new Set(catalogueAssets.filter(asset => (!divisionId || asset.divisionId === divisionId) && (category === 'All' || category === (asset.category || asset.serviceName || asset.type))).map(asset => assetAttribute(asset, 'SEATS')).filter(Boolean).map(Number))].sort((a, b) => a - b), [catalogueAssets, divisionId, category])
  const transmissionOptions = useMemo(() => [...new Set(catalogueAssets.filter(asset => (!divisionId || asset.divisionId === divisionId) && (category === 'All' || category === (asset.category || asset.serviceName || asset.type))).map(asset => assetAttribute(asset, 'TRANSMISSION')).filter(Boolean))].sort(), [catalogueAssets, divisionId, category])
  const [attributeFilters, setAttributeFilters] = useState<Record<string, string>>({})
  const divisionFilterAssets = catalogueAssets.filter(asset => (!divisionId || asset.divisionId === divisionId) && (category === 'All' || category === (asset.category || asset.serviceName || asset.type)))
  const attributeOptions = [...new Map(divisionFilterAssets.flatMap(asset => asset.attributes ?? []).filter(a => !['SEATS', 'TRANSMISSION'].includes(a.code)).map(a => [a.code, { code: a.code, name: a.name }])).values()].map(a => ({ ...a, values: [...new Set(divisionFilterAssets.flatMap(asset => (asset.attributes ?? []).filter(v => v.code === a.code).map(v => v.value)).filter(Boolean))].sort() })).filter(a => a.values.length > 1)
  useEffect(() => { setAttributeFilters({}); setSeatFilter(''); setTransmissionFilter('') }, [divisionId, category])
  const displayedAssets = useMemo(() => (searched ? assets : catalogueAssets).filter((asset) => {
    if (resultPreferenceIds.length && !resultPreferenceIds.includes(asset.assetCategoryId ?? '')) return false
    if (Object.entries(attributeFilters).some(([code, value]) => value && assetAttribute(asset, code) !== value)) return false
    if (divisionId && asset.divisionId !== divisionId) return false
    if (branchId && asset.branchId !== branchId) return false
    if (type && asset.type !== type) return false
    if (availableOnly && searched && !asset.isAvailable) return false
    if (seatOptions.length > 0 && seatFilter && Number(assetAttribute(asset, 'SEATS')) < Number(seatFilter)) return false
    if (transmissionOptions.length > 0 && transmissionFilter && assetAttribute(asset, 'TRANSMISSION') !== transmissionFilter) return false
    if (priceFilter === 'under-200' && asset.dailyRate >= 200) return false
    if (priceFilter === '200-500' && (asset.dailyRate < 200 || asset.dailyRate > 500)) return false
    if (priceFilter === 'over-500' && asset.dailyRate <= 500) return false
    if (category === 'All') return true
    return category === (asset.category || asset.serviceName || asset.type)
  }), [assets, availableOnly, branchId, catalogueAssets, category, divisionId, priceFilter, searched, seatFilter, transmissionFilter, type, seatOptions, transmissionOptions, attributeFilters, resultPreferenceIds])
  const categoryOptions = useMemo(() => [...new Set(catalogueAssets.filter(asset => !divisionId || asset.divisionId === divisionId).map(asset => asset.category || asset.serviceName || asset.type))].sort(), [catalogueAssets, divisionId])
  const sortedAssets = useMemo(() => [...displayedAssets].sort((left, right) => {
    if (sort === 'price-low') return left.dailyRate - right.dailyRate
    if (sort === 'price-high') return right.dailyRate - left.dailyRate
    return Number(right.isAvailable) - Number(left.isAvailable) || left.dailyRate - right.dailyRate
  }), [displayedAssets, sort])
  const rentalHours = Math.max(0, (new Date(`${endDate}T${returnTime}:00+12:00`).getTime() - new Date(`${startDate}T${pickupTime}:00+12:00`).getTime()) / 3600000) || 0
  const rentalDays = Math.max(1, Math.ceil(rentalHours / 24))
  const timedQuantity = (unit: string, measured = 1) => unit === 'Day' ? rentalDays : unit === 'Week' ? Math.max(1, Math.ceil(rentalHours / 168)) : unit === 'Hour' ? Math.max(1, Math.ceil(rentalHours)) : hireQuantity(unit, startDate, endDate, measured)
  const checkoutCharges = useMemo(() => (checkoutDetail?.charges ?? []).filter(charge => !['Operator', 'Driver', 'Transport'].includes(charge.category)).filter(charge => charge.isRequired ||
    (selectedExtras[charge.id] ?? 0) > 0 || booking.fulfilment === 'Delivery' && charge.category === 'Transport' ||
    booking.personnelRequested && ['Operator', 'Driver'].includes(charge.category)).map(charge => ({ ...charge,
      quantity: timedQuantity(charge.unit,selectedExtras[charge.id]??1) })),
  [booking.fulfilment, booking.personnelRequested, checkoutDetail, selectedExtras,startDate,endDate])
  const baseUnit=checkoutDetail?.hireRate?.unit??selected?.hireUnit??'Day'
  const baseRate=checkoutDetail?.hireRate?.rate??selected?.dailyRate??0
  const baseQuantity=baseUnit==='Hour' ? booking.hireQuantity : timedQuantity(baseUnit,booking.hireQuantity)
  const baseHire = baseRate*baseQuantity
  const extrasTotal = checkoutCharges.reduce((sum, charge) => sum + charge.defaultSellingRate * charge.quantity, 0)
  const taxRate = checkoutDetail?.taxRate ?? 12.5
  const bondAmount = checkoutDetail?.bondAmount ?? selected?.bondAmount ?? 0
  const estimatedTax = Math.round(((checkoutDetail?.hireRate?.isTaxable===false?0:baseHire) + checkoutCharges.filter(x => x.isTaxable).reduce((sum,x) => sum + x.defaultSellingRate*x.quantity,0)) * taxRate) / 100
  const estimatedTotal = baseHire + extrasTotal + estimatedTax
  const flow=checkoutDetail?.bookingFlow
  const licenceRequired = Boolean(flow?.requireVerifiedLicenceForVehicles && isVehicleAsset(selected) && !booking.personnelRequested)
  const pendingServiceCharges = booking.fulfilment === 'Delivery' || booking.personnelRequested
  const quotationFlow = Boolean(selected && (pendingServiceCharges || selected.requiresQuote || requestQuotation || baseRate <= 0 || selected.allowsQuoteRequests !== false && (flow?.quoteMode === 'Always' || flow?.businessCustomersRequireQuote && booking.customerType === 'Business')))
  async function advanceBooking(event: FormEvent) {
    event.preventDefault()
    if (bookingStep === 0 && flow && (rentalDays < flow.minimumHireDays || rentalDays > flow.maximumHireDays)) {
      setError(`Choose a hire between ${flow.minimumHireDays} and ${flow.maximumHireDays} days for this division.`)
      return
    }
    setError('')
    if (bookingStep === 0 && selected) {
      if (!startDate || !endDate || `${endDate}T${returnTime}` <= `${startDate}T${pickupTime}` || startDate < dateInputValue(0)) { setError('Choose valid pickup and return times.'); return }
      setSubmitting(true)
      try { const { data } = await api.get<CheckoutDetail>('/public/assets/' + selected.id, { params: { startDate, endDate, pickupTime, returnTime } }); if (!data.isAvailable) { setError('This asset is not available for those dates and times.'); return }; setCheckoutDetail(data) }
      catch (reason) { setError(apiErrorMessage(reason, 'Unable to verify the selected dates.')); return }
      finally { setSubmitting(false) }
    }
    if (bookingStep === 1 && !booking.phone.trim()) { setError('Add a phone contact in your customer profile before submitting.'); return }
    setBookingStep(step => step + 1)
  }
  function scrollTo(id: string) { document.getElementById(id)?.scrollIntoView({ behavior: 'smooth' }) }
  function requestState(value = booking, extras = selectedExtras, terms = termsAccepted) {
    return JSON.stringify({ startDate, endDate, pickupTime, returnTime, booking: value, extras, terms, configurationAnswers })
  }
  function openRequest(asset: PublicAsset, details: CheckoutDetail, customerDetails: BookingForm = emptyBooking, quote = false, dates = { startDate, endDate }) {
    const extras: Record<string, number> = {}
    const initial = { ...customerDetails, hireQuantity: details.hireRate?.unit === 'Hour' ? Math.max(1, Math.ceil(rentalHours)) : customerDetails.hireQuantity, fulfilment: asset.requiresDelivery || asset.fulfilmentMode === 'DeliveryOnly' ? 'Delivery' as const : 'Pickup' as const, personnelRequested: requiresProfessionalPersonnel(asset) || customerDetails.personnelRequested }
    const hoursQuestion = details.customerQuestions?.find(question => ['Number','Integer'].includes(question.type) && /^enter hours?$/i.test(question.label.trim()))
    setConfigurationAnswers(hoursQuestion && details.hireRate?.unit === 'Hour' ? { [hoursQuestion.code]: String(initial.hireQuantity) } : {}); setRequestQuotation(quote); setSelected(asset); setCheckoutDetail(details); setSelectedExtras(extras); setBooking(initial)
    setRequestBaseline(JSON.stringify({ ...dates, pickupTime, returnTime, booking: initial, extras, terms: false, configurationAnswers: hoursQuestion && details.hireRate?.unit === 'Hour' ? { [hoursQuestion.code]: String(initial.hireQuantity) } : {} }))
    setBookingStep(0); setTermsAccepted(false); setReference(''); setError(''); setCancelConfirmOpen(false); setDeliveryConfirmOpen(false)
  }
  async function submitRequest(event: FormEvent) {
    event.preventDefault(); if (!selected) return
    setSubmitting(true); setError('')
    try {
      const response = await submitPublicBookingRequest({
        assetId: selected.id, startDate, endDate, pickupTime, returnTime, ...booking, driverLicenceClasses: undefined,
        driverName: licenceRequired ? booking.driverName : '', driverLicence: licenceRequired ? booking.driverLicence : '',
        personnelHours: null, requestQuotation: quotationFlow, configurationAnswers, extras: checkoutCharges.map(charge => ({ chargeDefinitionId: charge.id, quantity: charge.quantity })),
      })
      setReference(response.reference); setResponseKind(response.requestType)
    } catch (requestError: unknown) { setError(apiErrorMessage(requestError, 'Your request could not be submitted. Please contact a branch.')) }
    finally { setSubmitting(false) }
  }

  async function requestBooking(asset: PublicAsset, quote = false, dates = searchDates ?? { startDate, endDate }, times = { pickupTime, returnTime }) {
    if (!dates.startDate || !dates.endDate || dates.endDate <= dates.startDate) {
      setError('Choose a return date after the pickup date.')
      return
    }
    if (!customerAuthenticated) {
      sessionStorage.setItem('crems.pendingBooking', JSON.stringify({ assetId: asset.id, ...dates, pickupTime, returnTime, quote }))
      searchNavigation.leave(() => onCustomerAccount())
      return
    }
    try {
      const account = (await api.get<{ fullName: string; customerName: string; email: string; phone: string | null; address: string | null; driverLicenceNumber: string | null; driverLicenceClasses?: number[]; type: 'Individual' | 'Business' }>('/customer-account/session')).data
      const details = (await api.get<CheckoutDetail>(`/public/assets/${asset.id}`, { params: { ...dates, ...times } })).data
      if (!details.isAvailable) {
        setError('This rental is no longer available for the selected dates. Please choose another option.')
        return
      }
      setStartDate(dates.startDate); setEndDate(dates.endDate); setPickupTime(times.pickupTime); setReturnTime(times.returnTime)
      openRequest(asset, details, { ...emptyBooking, fullName: account.fullName || account.customerName, customerType: account.type,
        companyName: account.type === 'Business' ? account.customerName : '', email: account.email,
        phone: account.phone ?? '', address: account.address ?? '', driverName: account.customerName,
        driverLicence: account.driverLicenceNumber ?? '', driverLicenceClasses: account.driverLicenceClasses ?? [] }, quote, dates)
    } catch { setError('We could not prepare this checkout. Please refresh availability and try again.') }
  }

  useEffect(() => {
    if (!customerAuthenticated || catalogueAssets.length === 0) return
    const raw = sessionStorage.getItem('crems.pendingBooking')
    if (!raw) return
    sessionStorage.removeItem('crems.pendingBooking')
    void (async () => {
      try {
        const pending = JSON.parse(raw) as { assetId: string; startDate: string; endDate: string; pickupTime?: string; returnTime?: string; quote?: boolean }
        const asset = catalogueAssets.find(item => item.id === pending.assetId)
        if (!asset) return
        const availability = (await api.get<{ isAvailable: boolean }>(`/public/assets/${asset.id}`, { params: { startDate: pending.startDate, endDate: pending.endDate, pickupTime: pending.pickupTime ?? '09:00', returnTime: pending.returnTime ?? '09:00' } })).data
        searchNavigation.markResults()
        setStartDate(pending.startDate); setEndDate(pending.endDate); setPickupTime(pending.pickupTime ?? '09:00'); setReturnTime(pending.returnTime ?? '09:00'); setSearchDates({ startDate: pending.startDate, endDate: pending.endDate }); setSearched(true)
        if (!availability.isAvailable) { setError('This rental is no longer available for the selected dates. Please choose another option.'); return }
        await requestBooking({ ...asset, isAvailable: true }, pending.quote, { startDate: pending.startDate, endDate: pending.endDate }, { pickupTime: pending.pickupTime ?? '09:00', returnTime: pending.returnTime ?? '09:00' })
      } catch { setError('We could not restore your rental selection. Please check availability again.') }
    })()
    // Resume exactly once from the browser-window booking hand-off.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [catalogueAssets, customerAuthenticated])

  useEffect(() => {
    const target = sessionStorage.getItem('crems.publicTarget')
    if (!target) return
    sessionStorage.removeItem('crems.publicTarget')
    window.setTimeout(() => scrollTo(target), 0)
  }, [])

  useEffect(() => {
    let active = true
    const request = customerAuthenticated ? api.get<CustomerPreferenceSession>('/customer-account/session').then(({data}) => data) : Promise.resolve<CustomerPreferenceSession>({})
    void request.then(data => {
      if (active) setPreferredCategoryIds(data.preferredAssetCategoryIds ?? [])
    }).catch(() => { if (active) setPreferredCategoryIds([]) })
    return () => { active = false }
  }, [customerAuthenticated])

  function renderSearchForm(compact = false) {
    return <Stack spacing={2} sx={{ containerType: 'inline-size' }}>
      <Box data-testid="rental-search-fields" sx={{ display: 'grid', gridTemplateColumns: '1fr', '@container (min-width: 480px)': { gridTemplateColumns: 'repeat(2, minmax(0, 1fr))', '& > :first-of-type': { gridColumn: differentReturnLocation ? 'auto' : '1 / -1' } }, '@container (min-width: 860px)': { gridTemplateColumns: differentReturnLocation ? 'repeat(2, minmax(0, 1fr))' : '2fr 1fr 1fr', '& > :first-of-type': { gridColumn: 'auto' } }, gap: 2, alignItems: 'start', '& .MuiFormControl-root': { minWidth: 0 }, '& .MuiSelect-select': { whiteSpace: 'normal', overflowWrap: 'anywhere' } }}>
        <FormControl fullWidth><InputLabel id="rental-pickup-location-label">Pickup location</InputLabel><Select labelId="rental-pickup-location-label" label="Pickup location" value={branchId} onChange={event => { setBranchId(event.target.value); if (returnBranchId === event.target.value) setReturnBranchId('') }}><MenuItem value="">All participating locations</MenuItem>{branches.map(branch => <MenuItem key={branch.id} value={branch.id}>{branch.name}</MenuItem>)}</Select></FormControl>
        {differentReturnLocation && <FormControl fullWidth><InputLabel id="rental-return-location-label">Return location</InputLabel><Select labelId="rental-return-location-label" label="Return location" value={returnBranchId} onChange={event => setReturnBranchId(event.target.value)}><MenuItem value="">Select location</MenuItem>{branches.filter(branch => branch.id !== branchId).map(branch => <MenuItem key={branch.id} value={branch.id}>{branch.name}</MenuItem>)}</Select></FormControl>}
        <TextField fullWidth type="date" label="Pickup date" InputLabelProps={{ shrink: true }} inputProps={{ min: dateInputValue(0) }} value={startDate} onChange={event => changeStartDate(event.target.value)} />
        <TextField fullWidth type="date" label="Return date" InputLabelProps={{ shrink: true }} inputProps={{ min: startDate || dateInputValue(0) }} value={endDate} onChange={event => setEndDate(event.target.value)} />
        <TextField fullWidth type="time" label="Pickup time" InputLabelProps={{ shrink: true }} value={pickupTime} onChange={event => setPickupTime(event.target.value)} />
        <TextField fullWidth type="time" label="Return time" InputLabelProps={{ shrink: true }} value={returnTime} onChange={event => setReturnTime(event.target.value)} />
      </Box>
      <FormControlLabel sx={{ m: 0 }} control={<Switch checked={differentReturnLocation} onChange={event => { setDifferentReturnLocation(event.target.checked); if (!event.target.checked) setReturnBranchId('') }} />} label={<Box><Typography variant="body2" fontWeight={700}>Return to a different location</Typography><Typography variant="caption" color="text.secondary">A one-way or transfer fee may apply.</Typography></Box>} />
      <Button fullWidth size="large" variant="contained" startIcon={<SearchOutlined />} disabled={loading} onClick={() => void checkAvailability()} sx={{ minHeight: 58 }}>{compact ? 'Update results' : 'Search rentals'}</Button>
    </Stack>
  }

  const allDivisionGalleries = useMemo(() => divisions.map(division => ({
    key: division.id,
    title: division.branding?.displayName || division.name,
    branding: division.branding,
    matches: (asset: PublicAsset) => asset.divisionId === division.id,
  })), [divisions])
  const homepageAssets = useMemo(() => sortedAssets.filter(asset => matchesHomepagePreferences(asset, preferredCategoryIds)), [sortedAssets, preferredCategoryIds])
  const preferredDivisionGalleries = allDivisionGalleries.filter(gallery => homepageAssets.some(gallery.matches))

  function moveGallery(key: string, total: number, direction: number) {
    setGalleryOffsets(current => ({ ...current, [key]: Math.max(0, Math.min((current[key] ?? 0) + direction, Math.max(0, total - 4))) }))
  }

  function resetToSearch() {
    setResultPreferenceIds([]); setAttributeFilters({}); setSearched(false); setSearchDates(null); setStartDate(dateInputValue(0)); setEndDate(''); setShowModifySearch(false); setDivisionId(''); setBranchId(''); setType(''); setCategory('All'); setSeatFilter(''); setTransmissionFilter(''); setPriceFilter(''); setAvailableOnly(false)
    window.setTimeout(() => window.scrollTo({ top: 0, behavior: 'smooth' }), 0)
  }

  function cancelBooking() {
    setSelected(null); setBooking(emptyBooking); setSelectedExtras({}); setRequestBaseline(''); setBookingStep(0); setTermsAccepted(false); setReference(''); setCancelConfirmOpen(false)
  }

  function closeRequest() {
    if (requestBaseline && requestState() !== requestBaseline) setCancelConfirmOpen(true)
    else cancelBooking()
  }

  function renderRentalCard(asset: PublicAsset) {
    const photos = assetPhotoUrls(asset, true)
    const photoIndex = Math.min(photoIndexes[asset.id] ?? 0, Math.max(photos.length - 1, 0))
    return <RentalCatalogueCard asset={asset} photos={photos} photoIndex={photoIndex} searched={searched} startDate={searchDates?.startDate ?? ''} endDate={searchDates?.endDate ?? ''} onPhoto={direction => changeCataloguePhoto(asset.id, photos.length, direction)} onDetails={() => setDetailAsset(asset)} onChooseDates={() => scrollTo('search')} onChooseNext={next => { const local = new Date(next).toLocaleString('sv-SE', { timeZone: 'Pacific/Fiji', hour12: false }); setStartDate(local.slice(0, 10)); setPickupTime(local.slice(11, 16)); setEndDate(''); setShowModifySearch(true); scrollTo('search') }} onBook={quote => void requestBooking(asset, quote)} />
  }

  return <Box sx={{ minHeight: '100vh', bgcolor: '#f7f7f3' }}>
    <CustomerSiteHeader authenticated={customerAuthenticated} accountName={customerName} onAccount={() => searchNavigation.leave(() => onCustomerAccount())} onAccountSection={section => searchNavigation.leave(() => onCustomerAccount(section))} onNavigate={(section: CustomerSiteSection) => section === 'top' || section === 'search' ? searchNavigation.leave(resetToSearch) : scrollTo(section)} onSignOut={onCustomerSignOut ? () => searchNavigation.leave(() => { void onCustomerSignOut() }) : undefined} />

    {!searched ? <Box id="top" sx={{ bgcolor: '#111', color: 'white', position: 'relative', overflow: 'hidden', py: { xs: 6, md: 9 } }}>
      <Box sx={{ position: 'absolute', width: 650, height: 650, borderRadius: '50%', bgcolor: 'secondary.main', opacity: .09, right: -160, top: -250 }} />
      <Container id="search" maxWidth="xl" sx={{ position: 'relative', px: { md: 5 } }}><Grid container spacing={{ xs: 4, md: 5 }} alignItems="center">
        <Grid size={{ xs: 12, md: 8 }}><Card elevation={10} sx={{ borderRadius: 3 }}><CardContent sx={{ p: { xs: 3, md: 5 } }}><Typography variant="h4" fontWeight={850} mb={1}>Find your rental</Typography><Typography variant="body1" color="text.secondary" mb={3.5}>Choose a location and hire dates to see live availability.</Typography>{renderSearchForm()}</CardContent></Card></Grid>
        <Grid size={{ xs: 12, md: 4 }}><Typography color="secondary.main" fontWeight={800} letterSpacing={1.5}>CARPENTERS FIJI RENTALS</Typography><Typography variant="h2" fontWeight={900} mt={1} sx={{ fontSize: { xs: '2.6rem', md: '3.7rem' } }}>Hire with confidence.</Typography><Typography variant="h6" color="rgba(255,255,255,.72)" mt={2}>Explore assets, spaces and hire services from participating Carpenters divisions across Fiji.</Typography><Button sx={{ mt: 3 }} size="large" variant="outlined" color="inherit" onClick={() => onCustomerAccount(customerAuthenticated ? 'bookings' : undefined)}>{customerAuthenticated ? 'Manage my bookings' : 'Sign in to book'}</Button></Grid>
      </Grid></Container>
    </Box> : <Box id="top" sx={{ bgcolor: 'white', borderBottom: 1, borderColor: 'divider', py: 2 }}><Container maxWidth="lg">
      <Stack direction={{ xs: 'column', md: 'row' }} alignItems={{ md: 'center' }} justifyContent="space-between" gap={2}><Box><Typography variant="caption" color="text.secondary">YOUR RENTAL SEARCH</Typography><Typography fontWeight={800}>{branches.find(branch => branch.id === branchId)?.name ?? 'All participating locations'}{differentReturnLocation && returnBranchId ? ` → ${branches.find(branch => branch.id === returnBranchId)?.name}` : ''}</Typography><Typography variant="body2" color="text.secondary">{searchDates?.startDate} to {searchDates?.endDate} · {rentalDays} {rentalDays === 1 ? 'day' : 'days'}</Typography></Box><Button variant="outlined" startIcon={<EditOutlined />} onClick={() => setShowModifySearch(true)}>Modify search</Button></Stack>
      {showModifySearch && <Card elevation={8} sx={{ mt: 2, position: 'relative' }}><IconButton aria-label="Close modify search" onClick={() => setShowModifySearch(false)} sx={{ position: 'absolute', top: 8, right: 8, zIndex: 1 }}><CloseOutlined /></IconButton><CardContent sx={{ p: { xs: 2.5, md: 3 }, pt: { xs: 6, md: 3 } }}><Typography variant="h6" fontWeight={850} mb={2}>Modify location or dates</Typography>{renderSearchForm(true)}</CardContent></Card>}
    </Container></Box>}

    <Container ref={catalogueRef} id="rentals" maxWidth={false} sx={{ py: { xs: 5, md: 6 }, px: { xs: 2, md: 5, lg: 7 } }}>
      {error && !selected && <Alert severity="error" sx={{ mb: 3 }} action={<Button color="inherit" onClick={() => { publicCatalogueCache = null; setCatalogueRetry(value => value + 1) }}>Try again</Button>}>{error}</Alert>}
      {!searched && loading && <Box sx={{py:5,display:'grid',placeItems:'center'}}><CircularProgress aria-label="Loading available assets" /></Box>}
      {!searched && !loading && !error && sortedAssets.length === 0 && <Alert severity="info">There are no published assets available to browse at the moment. Contact a branch for assistance or check again shortly.</Alert>}
      {searched && <Card variant="outlined" sx={{ mb: 3, bgcolor: 'white', borderRadius: 1.5 }}><CardContent sx={{ p: {xs:2,md:2.5} }}><Stack direction="row" gap={1.25} alignItems="center" flexWrap="wrap" sx={{ '& > .MuiFormControl-root': { flex: '1 1 145px' } }}>
        <FormControl size="small" sx={{ minWidth: 175 }}><InputLabel id="rental-division-label">Division</InputLabel><Select labelId="rental-division-label" MenuProps={{ disableScrollLock: true }} label="Division" value={divisionId} onChange={event => { setResultPreferenceIds([]); setDivisionId(event.target.value); setType(''); setCategory('All'); setSeatFilter(''); setTransmissionFilter(''); setPriceFilter(''); setGalleryOffsets({}) }}><MenuItem value="">All divisions</MenuItem>{divisions.map(division => <MenuItem key={division.id} value={division.id}>{division.name}</MenuItem>)}</Select></FormControl>
        <FormControl size="small" sx={{ minWidth: 170 }}><InputLabel id="rental-rental-category-label">Rental category</InputLabel><Select labelId="rental-rental-category-label" MenuProps={{ disableScrollLock: true }} label="Rental category" value={category} onChange={event => { setResultPreferenceIds([]); setCategory(event.target.value) }}>{['All', ...categoryOptions].map(item => <MenuItem key={item} value={item}>{item === 'All' ? 'All categories' : item}</MenuItem>)}</Select></FormControl>
        <FormControl size="small" sx={{ minWidth: 180 }}><InputLabel id="rental-sort-by-label">Sort by</InputLabel><Select labelId="rental-sort-by-label" MenuProps={{ disableScrollLock: true }} label="Sort by" value={sort} onChange={event => setSort(event.target.value as CatalogueSort)}><MenuItem value="recommended">Recommended</MenuItem><MenuItem value="price-low">Price: low to high</MenuItem><MenuItem value="price-high">Price: high to low</MenuItem></Select></FormControl>
        {(divisionId || category !== 'All' || seatFilter || transmissionFilter || priceFilter) && <Button size="small" onClick={() => { setResultPreferenceIds([]); setAttributeFilters({}); setDivisionId(''); setType(''); setCategory('All'); setSeatFilter(''); setTransmissionFilter(''); setPriceFilter(''); setAvailableOnly(false) }}>Clear</Button>}
        <Button startIcon={<TuneOutlined />} aria-expanded={advancedFiltersOpen} onClick={event=>setAdvancedFilterAnchor(event.currentTarget)} sx={{whiteSpace:'nowrap'}}>Advanced filters</Button>
      </Stack><Popover open={advancedFiltersOpen} anchorEl={advancedFilterAnchor} onClose={()=>setAdvancedFilterAnchor(null)} anchorOrigin={{vertical:'bottom',horizontal:'right'}} transformOrigin={{vertical:'top',horizontal:'right'}} slotProps={{paper:{sx:{p:3,mt:1,width:{xs:'calc(100vw - 32px)',sm:620},maxHeight:'75vh',borderRadius:2}}}}><Box><Typography fontWeight={750} mb={2}>Refine your results</Typography><Stack direction="row" flexWrap="wrap" gap={2} sx={{'& > .MuiFormControl-root':{flex:'1 1 200px',maxWidth:{sm:320}}}}>        {resultPreferenceIds.length > 0 && <TextField select size="small" label="Preferred categories" SelectProps={{ multiple: true }} value={resultPreferenceIds} onChange={e=>setResultPreferenceIds(typeof e.target.value === 'string' ? e.target.value.split(',') : e.target.value)} sx={{minWidth:220}}>{[...new Map(catalogueAssets.filter(a=>a.assetCategoryId).map(a=>[a.assetCategoryId!,a.category || a.type])).entries()].map(([id,name])=><MenuItem key={id} value={id}>{name}</MenuItem>)}</TextField>}
        {attributeOptions.map(option => <TextField key={option.code} select size="small" label={option.name} value={attributeFilters[option.code] ?? ''} sx={{ minWidth: 150 }} onChange={event => setAttributeFilters(current => ({ ...current, [option.code]: event.target.value }))}><MenuItem value="">Any {option.name.toLowerCase()}</MenuItem>{option.values.map(value => <MenuItem key={value} value={value}>{value}</MenuItem>)}</TextField>)}
        {seatOptions.length > 0 && <FormControl size="small" sx={{ minWidth: 125 }}><InputLabel id="rental-seats-label">Seats</InputLabel><Select labelId="rental-seats-label" MenuProps={{ disableScrollLock: true }} label="Seats" value={seatFilter} onChange={event => setSeatFilter(event.target.value)}><MenuItem value="">Any seats</MenuItem>{seatOptions.map(seats => <MenuItem key={seats} value={String(seats)}>{seats}+ seats</MenuItem>)}</Select></FormControl>}
        {transmissionOptions.length > 0 && <FormControl size="small" sx={{ minWidth: 150 }}><InputLabel id="rental-transmission-label">Transmission</InputLabel><Select labelId="rental-transmission-label" MenuProps={{ disableScrollLock: true }} label="Transmission" value={transmissionFilter} onChange={event => setTransmissionFilter(event.target.value)}><MenuItem value="">Any</MenuItem>{transmissionOptions.map(option => <MenuItem key={option} value={option}>{option}</MenuItem>)}</Select></FormControl>}
        <FormControl size="small" sx={{ minWidth: 145 }}><InputLabel id="rental-daily-price-label">Hire rate</InputLabel><Select labelId="rental-daily-price-label" MenuProps={{ disableScrollLock: true }} label="Hire rate" value={priceFilter} onChange={event => setPriceFilter(event.target.value)}><MenuItem value="">Any price</MenuItem><MenuItem value="under-200">Under FJD 200</MenuItem><MenuItem value="200-500">FJD 200–500</MenuItem><MenuItem value="over-500">Over FJD 500</MenuItem></Select></FormControl>
</Stack><Button sx={{mt:2}} onClick={()=>setAdvancedFilterAnchor(null)}>Show results</Button></Box></Popover></CardContent></Card>}
      {searched && <Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" alignItems={{ sm: 'center' }} gap={1.5} mb={3}><Box><Typography fontWeight={800}>{sortedAssets.length} {sortedAssets.length === 1 ? 'rental' : 'rentals'} found</Typography><Typography variant="body2" color="text.secondary">Live availability for your selected dates · Prices in FJD</Typography></Box><FormControlLabel control={<Switch checked={availableOnly} onChange={event => setAvailableOnly(event.target.checked)} />} label="Available only" /></Stack>}
      {searched && (loading ? <Box sx={{ py: 10, display: 'grid', placeItems: 'center' }}><CircularProgress /></Box> : <>
        {sortedAssets.length === 0 && <Card variant="outlined"><CardContent sx={{ textAlign: 'center', py: 8 }}><Typography variant="h6">No rentals match this search</Typography><Typography color="text.secondary">Try another category, branch or date range.</Typography><Button sx={{ mt: 2 }} onClick={() => { setResultPreferenceIds([]); setAttributeFilters({}); setDivisionId(''); setCategory('All'); setSeatFilter(''); setTransmissionFilter(''); setPriceFilter('') }}>Clear filters</Button></CardContent></Card>}
        {allDivisionGalleries.map(gallery => {
          const divisionAssets = sortedAssets.filter(gallery.matches)
          if (divisionAssets.length === 0) return null
        const offset = Math.min(galleryOffsets[gallery.key] ?? 0, Math.max(0, divisionAssets.length - 4))
        const visibleAssets = divisionAssets.slice(offset, offset + 4)
        if (divisionAssets.length === 0) return null
        return <Box key={gallery.key} sx={{ mb: 5 }}><Stack direction="row" alignItems="center" justifyContent="space-between" mb={1.5}><Box><Typography variant="h5" fontWeight={850} sx={{borderLeft:gallery.branding?.accentColor?'4px solid':undefined,borderColor:gallery.branding?.accentColor,pl:gallery.branding?.accentColor?1.5:undefined}}>{gallery.branding?.logoUrl && <Box component="img" src={gallery.branding.logoUrl} alt="" sx={{width:32,height:32,objectFit:'contain',mr:1,verticalAlign:'middle'}} />}{gallery.title}</Typography><Typography variant="body2" color="text.secondary">{divisionAssets.length ? `${divisionAssets.length} rental${divisionAssets.length === 1 ? '' : 's'} to explore` : 'Rental preview'}</Typography></Box></Stack>{visibleAssets.length ? <Stack direction="row" alignItems="center" gap={1.5} sx={{ minWidth: 0 }}><IconButton aria-label={`Previous ${gallery.title} results`} disabled={offset === 0} onClick={() => moveGallery(gallery.key, divisionAssets.length, -1)} sx={{ width: 54, height: 54, flex: '0 0 auto', color: 'white', bgcolor: '#111', '&:hover': { bgcolor: '#333' } }}><ChevronLeftOutlined fontSize="large" /></IconButton><Box sx={{ flex: 1, minWidth: 0, display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, minmax(0, 1fr))', lg: 'repeat(4, minmax(0, 1fr))' }, gap: 2 }}>{visibleAssets.map(asset => <Box key={asset.id} sx={{ minWidth: 0 }}>{renderRentalCard(asset)}</Box>)}</Box><IconButton aria-label={`Next ${gallery.title} results`} disabled={offset >= Math.max(0, divisionAssets.length - 4)} onClick={() => moveGallery(gallery.key, divisionAssets.length, 1)} sx={{ width: 54, height: 54, flex: '0 0 auto', color: 'white', bgcolor: '#111', '&:hover': { bgcolor: '#333' } }}><ChevronRightOutlined fontSize="large" /></IconButton></Stack> : <Card variant="outlined"><CardContent><Typography color="text.secondary">No rentals are currently listed for this division.</Typography></CardContent></Card>}</Box>
        })}
      </>)}
      {!searched && !loading && preferredCategoryIds.length > 0 && homepageAssets.length === 0 && <Alert severity="info" action={<Button onClick={() => searchNavigation.leave(() => onCustomerAccount('preferences'))}>Edit preferences</Button>}>No rentals are currently listed for your selected categories. You can still search all rentals above.</Alert>}
      {!searched && !loading && preferredDivisionGalleries.map(gallery => {
        const galleryAssets = homepageAssets.filter(gallery.matches)
        const offset = Math.min(galleryOffsets[gallery.key] ?? 0, Math.max(0, galleryAssets.length - 4))
        const visibleAssets = galleryAssets.slice(offset, offset + 4)
        if (galleryAssets.length === 0) return null
        return <Box key={gallery.key} sx={{ mb: 5 }}><Stack direction="row" alignItems="center" justifyContent="space-between" mb={1.5}><Box><Typography variant="h5" fontWeight={850} sx={{borderLeft:gallery.branding?.accentColor?'4px solid':undefined,borderColor:gallery.branding?.accentColor,pl:gallery.branding?.accentColor?1.5:undefined}}>{gallery.branding?.logoUrl && <Box component="img" src={gallery.branding.logoUrl} alt="" sx={{width:32,height:32,objectFit:'contain',mr:1,verticalAlign:'middle'}} />}{gallery.title}</Typography><Typography variant="body2" color="text.secondary">{galleryAssets.length ? `${galleryAssets.length} rental${galleryAssets.length === 1 ? '' : 's'} to explore` : 'Rental preview'}</Typography></Box></Stack>{visibleAssets.length ? <Stack direction="row" alignItems="center" gap={1.5} sx={{ minWidth: 0 }}><IconButton aria-label={`Previous ${gallery.title} rentals`} disabled={offset === 0} onClick={() => moveGallery(gallery.key, galleryAssets.length, -1)} sx={{ width: 54, height: 54, flex: '0 0 auto', color: 'white', bgcolor: '#111', '&:hover': { bgcolor: '#333' } }}><ChevronLeftOutlined fontSize="large" /></IconButton><Box sx={{ flex: 1, minWidth: 0, display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, minmax(0, 1fr))', lg: 'repeat(4, minmax(0, 1fr))' }, gap: 2 }}>{visibleAssets.map(asset => <Box key={asset.id} sx={{ minWidth: 0 }}>{renderRentalCard(asset)}</Box>)}</Box><IconButton aria-label={`Next ${gallery.title} rentals`} disabled={offset >= Math.max(0, galleryAssets.length - 4)} onClick={() => moveGallery(gallery.key, galleryAssets.length, 1)} sx={{ width: 54, height: 54, flex: '0 0 auto', color: 'white', bgcolor: '#111', '&:hover': { bgcolor: '#333' } }}><ChevronRightOutlined fontSize="large" /></IconButton></Stack> : <Card variant="outlined"><CardContent><Typography color="text.secondary">No rentals are currently listed for this division.</Typography></CardContent></Card>}</Box>
      })}
    </Container>

    {!searched && <Box sx={{ bgcolor: 'secondary.main', py: { xs: 6, md: 7 } }}><Container maxWidth="lg"><Stack direction={{ xs: 'column', lg: 'row' }} alignItems={{ lg: 'center' }} justifyContent="space-between" gap={3}><Box><Typography variant="h4" fontWeight={800}>Ready to book or track a booking?</Typography><Typography mt={1} sx={{ opacity: .72 }}>Enter a booking or quotation reference to find it securely in your account.</Typography></Box><Box component="form" onSubmit={event=>{event.preventDefault();sessionStorage.setItem('crems.customerReferenceSearch',trackingReference.trim());onCustomerAccount('bookings')}} sx={{display:'flex',flexDirection:{xs:'column',sm:'row'},gap:1,width:{xs:'100%',lg:520}}}><TextField fullWidth placeholder="BK-2026-0001 or QUO-20260903-XXXXXX" value={trackingReference} onChange={event=>setTrackingReference(event.target.value)} sx={{bgcolor:'white',borderRadius:1}} InputProps={{startAdornment:<SearchOutlined sx={{mr:1,color:'text.secondary'}}/>}}/><Button type="submit" size="large" variant="contained" sx={{ bgcolor: '#111', color: 'white', minWidth: 150, '&:hover': { bgcolor: '#292929' } }}>{customerAuthenticated ? 'Find reference' : 'Sign in & find'}</Button></Box></Stack></Container></Box>}

    {!searched && <Box id="how-it-works" sx={{ bgcolor: '#111', color: 'white', py: 9 }}><Container maxWidth="lg"><Typography variant="h3" fontWeight={800} textAlign="center" sx={{ fontSize: { xs: '2rem', md: '3rem' } }}>Simple from search to pickup</Typography><Grid container spacing={3} mt={3}>
      {[['1', 'Choose a division', 'Select the Carpenters division that provides the vehicle, equipment or site service you need.'], ['2', 'Compare options', 'Check prices, branches and live availability without signing in.'], ['3', 'Sign in and book', 'Use your customer account to submit and track bookings securely.']].map(([number, title, text]) => <Grid key={number} size={{ xs: 12, md: 4 }}><Stack alignItems="center" textAlign="center"><Avatar sx={{ bgcolor: 'secondary.main', color: '#111', fontWeight: 800, width: 52, height: 52 }}>{number}</Avatar><Typography variant="h6" fontWeight={700} mt={2}>{title}</Typography><Typography color="rgba(255,255,255,.65)" mt={1}>{text}</Typography></Stack></Grid>)}
    </Grid></Container></Box>}

    {!searched && <Container maxWidth="lg" sx={{ py: 8 }}><Grid container spacing={3}>{[[<VerifiedOutlined />, 'Maintained and inspected', 'Availability and maintenance status are managed by the operating branch.'], [<SupportAgentOutlined />, 'Local support', 'A Carpenters team member reviews every request and confirms collection requirements.'], [<LocalShippingOutlined />, 'Pickup or delivery planning', 'Delivery and a driver/operator can be included in the final quotation.']].map(([icon, title, text]) => <Grid key={String(title)} size={{ xs: 12, md: 4 }}><Card variant="outlined" sx={{ height: '100%' }}><CardContent sx={{ p: 3 }}><Avatar sx={{ bgcolor: 'secondary.main', color: '#111' }}>{icon}</Avatar><Typography variant="h6" fontWeight={750} mt={2}>{title}</Typography><Typography color="text.secondary" mt={1}>{text}</Typography></CardContent></Card></Grid>)}</Grid></Container>}

    {!searched && <Box id="faq" sx={{ bgcolor: 'white', py: 8 }}><Container maxWidth="md"><Typography variant="h3" fontWeight={800} textAlign="center" sx={{ fontSize: { xs: '2rem', md: '2.7rem' } }}>Before you make a request</Typography><Typography color="text.secondary" textAlign="center" mt={1} mb={4}>Clear answers to common rental questions.</Typography>{[['Is the displayed price final?', 'No. It is an estimated base hire charge. VAT, refundable bond, delivery, fuel, damage waiver, excess usage and operator charges may apply. Staff confirm the complete quote before approval.'], ['What do I need at pickup?', 'Individual vehicle customers normally need valid identification, an appropriate driver licence, their booking reference and an accepted payment method. Business and equipment hires may require a purchase order, site details or approved operator.'], ['Does an online request reserve the asset?', 'The request is held for staff review. It becomes confirmed only after eligibility, availability, pricing and any refundable bond or documentation requirements are approved.'], ['Can Carptrac equipment include an operator?', 'Yes, where the service supports it. Some assets require driver/operator, while others offer personnel as an option. This is shown on the asset card and confirmed in the quote.']].map(([question, answer]) => <Accordion key={question} disableGutters elevation={0} sx={{ borderBottom: 1, borderColor: 'divider' }}><AccordionSummary expandIcon={<ExpandMoreOutlined />}><Typography fontWeight={700}>{question}</Typography></AccordionSummary><AccordionDetails><Typography color="text.secondary">{answer}</Typography></AccordionDetails></Accordion>)}</Container></Box>}

    <Box sx={{ borderTop: '4px solid', borderColor: 'secondary.main', bgcolor: '#f4f2ed' }}><Container id="contact" maxWidth="xl" sx={{ py: 9 }}><Typography variant="h3" fontWeight={800} sx={{ fontSize: { xs: '2rem', md: '3rem' } }}>Contact our branches</Typography><Typography color="text.secondary" mt={1} mb={4}>Need advice before requesting? Speak with a local rental team.</Typography><Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(min(280px, 100%), 1fr))', gap: { xs: 2.5, md: 4 } }}>
      {branches.map((branch) => <Card key={branch.id} variant="outlined" sx={{ height: '100%', borderRadius: 2 }}><CardContent sx={{ p: 3.5 }}><Typography variant="h6" fontWeight={750}>{branch.name}</Typography><Stack gap={1.5} mt={2}>{branch.address && <Stack direction="row" gap={1.25} alignItems="flex-start"><LocationOnOutlined color="action" /><Typography color="text.secondary">{branch.address}</Typography></Stack>}{branch.phone && <Stack direction="row" gap={1.25}><PhoneOutlined color="action" /><Link href={`tel:${branch.phone}`} color="inherit">{branch.phone}</Link></Stack>}</Stack></CardContent></Card>)}
    </Box></Container></Box>
    <Box sx={{ bgcolor: '#080808', color: 'rgba(255,255,255,.65)', py: 3 }}><Container maxWidth="lg"><Typography variant="body2">© {new Date().getFullYear()} Carpenters Fiji — Vehicle & Equipment Rentals</Typography></Container></Box>

    <PublicAssetDetailDialog asset={detailAsset} imageUrls={detailAsset ? assetPhotoUrls(detailAsset) : []} startDate={searchDates?.startDate ?? ''} endDate={searchDates?.endDate ?? ''} pickupTime={pickupTime} returnTime={returnTime} availabilityRequested={searched && !!searchDates} open={Boolean(detailAsset)} onClose={() => setDetailAsset(null)} onContinue={(asset) => { setDetailAsset(null); void requestBooking(asset as PublicAsset, (asset as PublicAsset).requiresQuote || (asset as PublicAsset).isBookableOnline === false) }} />

    <Dialog disableScrollLock open={Boolean(selected)} onClose={() => !submitting && closeRequest()} fullWidth maxWidth="lg" slotProps={{ paper: { sx: { maxHeight: 'calc(100dvh - 24px)', height: 'auto', m: { xs: 1, md: 2 }, overflow: 'hidden' } } }}><DialogTitle sx={{ pr: 7, position: 'relative', py: 1.25 }}>{reference ? 'Request received' : quotationFlow ? 'Request a quotation' : 'Request a booking'}{!reference && <IconButton aria-label="Cancel request" onClick={closeRequest} sx={{ position: 'absolute', top: 5, right: 12 }}><CloseOutlined /></IconButton>}</DialogTitle><DialogContent dividers sx={{ p: 'clamp(16px, 2.2vw, 32px)', '@media (min-width: 900px) and (max-height: 900px)': { py: 2, '& .MuiCardContent-root': { p: 2, '&:last-child': { pb: 2 } }, '& .MuiAlert-root': { py: .5 }, '& .MuiTypography-overline': { lineHeight: 2 } }, overflowY: 'auto', overflowX: 'hidden', '& .MuiInputBase-input:not(textarea)': { paddingBlock: 'clamp(13px, 1.55vh, 18px)' }, '& .MuiInputLabel-root:not(.MuiInputLabel-shrink)': { transform: 'translate(14px, 14px) scale(1)' }, '& .MuiCardContent-root': { p: 'clamp(16px, 1.7vw, 24px)', '&:last-child': { pb: 'clamp(16px, 1.7vw, 24px)' } }, '& .MuiTypography-h6': { fontSize: 'clamp(1.05rem, 1.25vw, 1.3rem)' }, '& .MuiStepLabel-label': { fontSize: '.875rem' } }}>
      {reference ? <Stack alignItems="center" textAlign="center" py={4}><CheckCircleOutlined color="success" sx={{ fontSize: 70 }} /><Typography variant="h5" fontWeight={750} mt={2}>{responseKind === 'Quotation' ? 'Quotation request submitted' : 'Booking request submitted'}</Typography><Typography color="text.secondary" mt={1}>{responseKind === 'Quotation' ? 'A rental specialist will review delivery, driver/operator arrangements and final pricing, normally within one business day.' : 'The branch will verify your details and confirm pickup requirements.'}</Typography><Chip label={`Reference: ${reference}`} sx={{ mt: 3, fontWeight: 700, fontSize: '1rem', py: 2.25 }} /><Button sx={{ mt: 2 }} onClick={() => onCustomerAccount('bookings')}>{responseKind === 'Quotation' ? 'View my quotations' : 'View my bookings'}</Button></Stack> :
      <Box component="form" id="public-booking-form" onSubmit={bookingStep === 2 ? submitRequest : advanceBooking}>
        <Stepper activeStep={bookingStep} sx={{ py: { xs: .5, md: 1 } }}>{['Your rental', 'Your details', 'Price & submit'].map(label => <Step key={label}><StepLabel>{label}</StepLabel></Step>)}</Stepper>
        <Typography variant="h6" fontWeight={800} sx={{ mt: 1, mb: 2 }}>{['Your rental', 'Your details', 'Price and submit'][bookingStep]}</Typography>
        <Grid container spacing={2}>{error && <Grid size={12}><Alert severity="error">{error}</Alert></Grid>}<Grid size={{ xs: 12, md: 8 }}><Stack spacing={2}>
          {bookingStep === 0 && <>
            <Card variant="outlined"><CardContent><Typography variant="overline" color="text.secondary">Selected asset</Typography><Typography variant="h6" fontWeight={800}>{selected?.name}</Typography><Typography variant="body2" color="text.secondary">{selected?.category ?? selected?.serviceName} · {selected?.assetNumber}</Typography><Divider sx={{ my: 1.5 }} /><Stack direction="row" gap={1} alignItems="flex-start"><LocationOnOutlined color="action" /><Box><Typography fontWeight={750}>{selected?.branchName}</Typography><Typography variant="body2" color="text.secondary">{checkoutDetail?.branch?.address ?? 'Supplied by this branch. Search again to choose another location.'}</Typography></Box></Stack></CardContent></Card>
            <Stack direction={{ xs: 'column', sm: 'row' }} gap={2}><TextField fullWidth required type="date" label="Pickup date" InputLabelProps={{ shrink: true }} inputProps={{ min: dateInputValue(0) }} value={startDate} onChange={event => changeStartDate(event.target.value)} /><TextField fullWidth required type="date" label="Return date" InputLabelProps={{ shrink: true }} inputProps={{ min: startDate }} value={endDate} onChange={event => setEndDate(event.target.value)} /></Stack>
            <Stack direction={{ xs: 'column', sm: 'row' }} gap={2}><TextField fullWidth type="time" label="Pickup time" InputLabelProps={{ shrink: true }} value={pickupTime} onChange={event => setPickupTime(event.target.value)} /><TextField fullWidth type="time" label="Return time" InputLabelProps={{ shrink: true }} value={returnTime} onChange={event => setReturnTime(event.target.value)} /></Stack>
            {selected?.type === 'PropertyUnit' ? <Alert severity="info">Handover takes place at the property. Delivery and operator services do not apply.</Alert> : selected?.fulfilmentMode === 'PickupOnly' ? <Alert severity="info">Branch pickup only · Collect from {selected.branchName}.</Alert> : selected?.requiresDelivery || selected?.fulfilmentMode === 'DeliveryOnly' ? <Alert severity="info"><Typography fontWeight={750}>Delivery by Carpenters</Typography>We will deliver this rental to your address.</Alert> : <FormControl fullWidth><InputLabel id="request-fulfilment-label">Pickup or delivery</InputLabel><Select labelId="request-fulfilment-label" label="Pickup or delivery" value={booking.fulfilment} onChange={event => { if (event.target.value === 'Delivery' && !quotationFlow) setDeliveryConfirmOpen(true); else setBooking({ ...booking, fulfilment: event.target.value as 'Pickup' | 'Delivery' }) }}><MenuItem value="Pickup">Pickup from {selected?.branchName}</MenuItem><MenuItem value="Delivery">Deliver to my address or worksite</MenuItem></Select></FormControl>}
            {requiresProfessionalPersonnel(selected) && <Alert severity="info"><Typography fontWeight={750}>Driver/operator included</Typography>The branch will confirm arrangements and charges after reviewing your request.</Alert>}
            {!requiresProfessionalPersonnel(selected) && allowsProfessionalPersonnel(selected) && <FormControlLabel control={<Checkbox checked={booking.personnelRequested} onChange={event => setBooking({ ...booking, personnelRequested: event.target.checked })} />} label="Driver/operator included" />}
          </>}
          {bookingStep === 1 && <>

            <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, minmax(0, 1fr))' }, gap: 1.25 }}>
              <TextField fullWidth label="Full name" value={booking.fullName} InputProps={{ readOnly: true }} />
              <TextField fullWidth type="email" label="Email" value={booking.email} InputProps={{ readOnly: true }} />
              <TextField fullWidth label="Phone contact" value={booking.phone} InputProps={{ readOnly: true }} />
              <TextField fullWidth label="Emergency / secondary contact (optional)" value={booking.secondaryContact} inputProps={{ maxLength: 100 }} onChange={event => setBooking({ ...booking, secondaryContact: event.target.value })} />
              {booking.fulfilment === 'Delivery' && <TextField sx={{ gridColumn: '1 / -1' }} fullWidth required label="Delivery address" value={booking.deliveryAddress} inputProps={{ maxLength: 500 }} onChange={event => setBooking({ ...booking, deliveryAddress: event.target.value })} />}
              <TextField fullWidth multiline minRows={2} maxRows={4} label="Purpose (optional)" value={booking.purpose} inputProps={{ maxLength: 250 }} onChange={event => setBooking({ ...booking, purpose: event.target.value })} />
              <TextField fullWidth multiline minRows={2} maxRows={4} label="Additional message (optional)" value={booking.message} inputProps={{ maxLength: 1000 }} onChange={event => setBooking({ ...booking, message: event.target.value })} />
            </Box>
            {licenceRequired && <TextField fullWidth label="Driver licence number" value={booking.driverLicence} InputProps={{ readOnly: true }} error={!booking.driverLicence} helperText={booking.driverLicence ? `Verified from your profile${booking.driverLicenceClasses.length ? ' · Classes: ' + booking.driverLicenceClasses.join(', ') : ''}. Bring your licence for the handover check.` : 'Verify your driver licence in your customer profile before submitting this request.'} />}
            {(baseUnit === 'Hour' || measuredUnit(baseUnit) || !!checkoutDetail?.customerQuestions?.length || !!checkoutDetail?.charges.some(charge => !charge.isRequired && !['Operator', 'Driver', 'Transport'].includes(charge.category))) && <Box sx={{ borderTop: '1px solid', borderColor: 'divider', pt: 2 }}>
              <Typography fontWeight={800} mb={1}>Service requirements</Typography>
              <Stack spacing={2}>
                {(baseUnit === 'Hour' || measuredUnit(baseUnit)) && !checkoutDetail?.customerQuestions?.some(question => ['Number','Integer'].includes(question.type) && /^enter hours?$/i.test(question.label.trim())) && <TextField required type="number" label={baseUnit === 'Hour' ? 'Estimated hire hours' : 'Hire quantity (' + baseUnit + ')'} inputProps={{ min: baseUnit === 'Hour' ? 1 : .001, max: baseUnit === 'Hour' ? Math.floor(rentalHours) : 1000000, step: baseUnit === 'Hour' ? 1 : 'any' }} value={booking.hireQuantity} onChange={event => setBooking({ ...booking, hireQuantity: Number(event.target.value) })} />}
                {!!checkoutDetail?.customerQuestions?.length && <ConfiguredCustomerFields questions={checkoutDetail.customerQuestions} answers={configurationAnswers} onChange={answers => { setConfigurationAnswers(answers); const question = checkoutDetail.customerQuestions?.find(q => ['Number','Integer'].includes(q.type) && /^enter hours?$/i.test(q.label.trim())); if (baseUnit === 'Hour' && question && answers[question.code] !== undefined) setBooking(current => ({ ...current, hireQuantity: Number(answers[question.code]) })) }} />}
                {checkoutDetail?.charges.filter(charge => !charge.isRequired && !['Operator', 'Driver', 'Transport'].includes(charge.category)).map(charge => <FormControlLabel key={charge.id} control={<Checkbox checked={(selectedExtras[charge.id] ?? 0) > 0} onChange={event => setSelectedExtras({ ...selectedExtras, [charge.id]: event.target.checked ? 1 : 0 })} />} label={charge.name} />)}
                {checkoutCharges.filter(charge => measuredUnit(charge.unit)).map(charge => <TextField key={charge.id} type="number" required label={charge.name + ' quantity (' + charge.unit + ')'} inputProps={{ min: .001, max: 1000, step: 'any' }} value={selectedExtras[charge.id] ?? 1} onChange={event => setSelectedExtras({ ...selectedExtras, [charge.id]: Number(event.target.value) })} />)}
              </Stack>
            </Box>}
          </>}
          {bookingStep === 2 && <>
            <Alert severity={quotationFlow ? 'info' : 'success'}><Typography fontWeight={750}>{quotationFlow ? 'Quotation request' : 'Booking request'}</Typography>{quotationFlow ? 'The rental team will confirm the complete price before you accept the quotation.' : 'The branch will confirm availability, eligibility and collection requirements.'}</Alert>
            <Card variant="outlined"><CardContent><Typography fontWeight={750}>{booking.fullName}</Typography><Typography variant="body2" color="text.secondary">{booking.email} · {booking.phone}</Typography><Divider sx={{ my: 1.5 }} /><Typography variant="body2">{startDate} to {endDate}</Typography><Typography variant="body2" mt={.5}>{booking.fulfilment === 'Delivery' ? 'Delivery to ' + booking.deliveryAddress : 'Pickup from ' + selected?.branchName}</Typography>{booking.personnelRequested && <Typography variant="body2" mt={.5}>Driver/operator included · price to be confirmed</Typography>}{licenceRequired && booking.driverLicence && <Typography variant="body2" mt={.5}>Driver licence: {booking.driverLicence}{booking.driverLicenceClasses.length ? ` · Classes: ${booking.driverLicenceClasses.join(", ")}` : ""}</Typography>}{booking.secondaryContact && <Typography variant="body2" mt={.5}>Secondary contact: {booking.secondaryContact}</Typography>}</CardContent></Card>
            <FormControlLabel control={<Checkbox checked={termsAccepted} onChange={event => setTermsAccepted(event.target.checked)} />} label={quotationFlow ? 'I understand that the complete price will be confirmed in a quotation for me to review and accept.' : 'I understand this request is subject to branch confirmation and any required refundable bond.'} />
          </>}
        </Stack></Grid><Grid size={{ xs: 12, md: 4 }}><Card variant="outlined" sx={{ position: { md: 'sticky' }, top: 16 }}><CardContent><Typography variant="overline" color="text.secondary">{quotationFlow ? 'Quotation estimate' : 'Booking summary'}</Typography><Typography variant="h6" fontWeight={800}>{selected?.name}</Typography><Typography variant="body2" color="text.secondary">{baseQuantity} × {baseUnit.toLowerCase()} · {selected?.branchName}</Typography><Divider sx={{ my: 2 }} /><Stack spacing={1}><Stack direction="row" justifyContent="space-between"><Typography>Base hire</Typography><Typography>{baseHire.toFixed(2)}</Typography></Stack>{checkoutCharges.map(charge => <Stack key={charge.id} direction="row" justifyContent="space-between" gap={1}><Typography variant="body2">{charge.name} × {charge.quantity}</Typography><Typography variant="body2">{(charge.defaultSellingRate * charge.quantity).toFixed(2)}</Typography></Stack>)}<Stack direction="row" justifyContent="space-between"><Typography>VAT ({taxRate}%)</Typography><Typography>{estimatedTax.toFixed(2)}</Typography></Stack><Divider />{booking.fulfilment === 'Delivery' && <Stack direction="row" justifyContent="space-between" gap={1}><Typography variant="body2">Delivery</Typography><Typography variant="body2">To be confirmed</Typography></Stack>}{booking.personnelRequested && <Stack direction="row" justifyContent="space-between" gap={1}><Typography variant="body2">Driver/operator included</Typography><Typography variant="body2">To be confirmed</Typography></Stack>}<Stack direction="row" justifyContent="space-between"><Typography fontWeight={800}>{pendingServiceCharges ? 'Hire estimate incl. VAT' : 'Estimated total incl. VAT'}</Typography><Typography fontWeight={800}>FJD {estimatedTotal.toFixed(2)}</Typography></Stack>{bondAmount > 0 && <Stack direction="row" justifyContent="space-between"><Typography>Refundable bond</Typography><Typography>FJD {bondAmount.toFixed(2)}</Typography></Stack>}<Divider />{!pendingServiceCharges && bondAmount > 0 && <Stack direction="row" justifyContent="space-between"><Typography fontWeight={800}>Total including bond</Typography><Typography fontWeight={800}>FJD {(estimatedTotal + bondAmount).toFixed(2)}</Typography></Stack>}{bondAmount > 0 && <Typography variant="caption" color="text.secondary">Refundable bond is held separately, subject to return inspection.</Typography>}</Stack>{pendingServiceCharges && <Alert severity="info" sx={{ mt: 2 }}>Our team will confirm the complete quotation, including {[booking.fulfilment === 'Delivery' ? 'delivery' : '', booking.personnelRequested ? 'driver/operator' : ''].filter(Boolean).join(' and ')}.</Alert>}</CardContent></Card></Grid></Grid>
      </Box>}
    </DialogContent><DialogActions sx={{ px: { xs: 2, md: 3 }, py: 1.5 }}>{reference ? <Button onClick={() => setSelected(null)}>Close</Button> : <><Button onClick={() => bookingStep === 0 ? closeRequest() : setBookingStep(step => step - 1)} disabled={submitting}>{bookingStep === 0 ? 'Cancel' : 'Back'}</Button><Box sx={{ flex: 1 }} /><Button form="public-booking-form" type="submit" variant="contained" disabled={submitting || (bookingStep === 2 && !termsAccepted)}>{submitting ? 'Sending…' : bookingStep === 2 ? quotationFlow ? 'Request quotation' : 'Submit booking' : 'Continue'}</Button></>}</DialogActions></Dialog>
    <Dialog disableScrollLock open={deliveryConfirmOpen} onClose={() => setDeliveryConfirmOpen(false)} maxWidth="xs" fullWidth><DialogTitle>Switch to a quotation?</DialogTitle><DialogContent><Typography>Delivery needs a price from our team. Request a quotation instead?</Typography></DialogContent><DialogActions><Button onClick={() => setDeliveryConfirmOpen(false)}>Keep pickup</Button><Button variant="contained" onClick={() => { setBooking(current => ({ ...current, fulfilment: 'Delivery' })); setDeliveryConfirmOpen(false) }}>Get quotation</Button></DialogActions></Dialog>
    <Dialog disableScrollLock open={cancelConfirmOpen} onClose={() => setCancelConfirmOpen(false)} maxWidth="xs" fullWidth><DialogTitle>Cancel this request?</DialogTitle><DialogContent><Typography color="text.secondary">Your entered details will be deleted.</Typography></DialogContent><DialogActions><Button onClick={() => setCancelConfirmOpen(false)}>Keep editing</Button><Button color="error" variant="contained" onClick={cancelBooking}>Cancel request</Button></DialogActions></Dialog>
  </Box>
}
