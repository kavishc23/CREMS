import RefreshOutlined from '@mui/icons-material/RefreshOutlined'
import { useActionNotice } from '../components/useActionNotice'
import { downloadRentalFinancialPdf } from '../utils/rentalFinancialPdf'
import { rentalReference } from '../utils/rentalReference'
import { customerJourneyStage, customerStageColor, customerStageLabel, customerJourneyActions, hasQuotationOffer } from '../utils/customerJourney'
import { useEffect, useRef, useState, type FormEvent, type ReactNode } from 'react'
import { toast } from '../components/ToastHost'
import axios from 'axios'
import CloseOutlined from '@mui/icons-material/CloseOutlined'
import DashboardOutlined from '@mui/icons-material/DashboardOutlined'
import DescriptionOutlined from '@mui/icons-material/DescriptionOutlined'
import EventAvailableOutlined from '@mui/icons-material/EventAvailableOutlined'
import SearchOutlined from '@mui/icons-material/SearchOutlined'
import VisibilityOutlined from '@mui/icons-material/VisibilityOutlined'
import VisibilityOffOutlined from '@mui/icons-material/VisibilityOffOutlined'
import {
  Alert, Box, Button, Card, CardContent, Chip, CircularProgress, Container,
  Dialog, DialogActions, DialogContent, DialogTitle, Divider, FormControl, Grid,
  IconButton, InputAdornment, InputLabel, MenuItem, Paper, Select, Stack, TextField, Typography,
} from '@mui/material'
import { PageHeader } from '../components/PageHeader'
import AccountCircleOutlined from '@mui/icons-material/AccountCircleOutlined'
import TuneOutlined from '@mui/icons-material/TuneOutlined'
import { RentalPreferencesPage } from './RentalPreferencesPage'
import { api } from '../api/client'
import { monitorInactivity } from '../auth/inactivity'
import { CustomerSiteHeader, type CustomerSiteSection } from '../components/CustomerSiteHeader'
import { LicenceWorkflowDialog, type LicenceDetails } from '../components/LicenceWorkflowDialog'
import { openProtectedFile } from '../utils/openProtectedFile'
import { downloadRentalAgreementPdf, openRentalAgreementPdf, type RentalAgreementData } from '../utils/rentalAgreementPdf'

type HirePreference = 'Vehicles' | 'Equipment' | 'WasteAndSiteHire'
type LegacyHirePreference = HirePreference | 'NoPreference'
type CustomerSession = { preferredServiceIds?: string[]; email: string; fullName: string; customerNumber: string; customerName: string; phone: string | null; address: string | null; identificationNumber: string | null; hirePreferences?: HirePreference[]; hirePreference?: LegacyHirePreference; emailConfirmed: boolean }
type BookingItem = { name: string; startAt: string; endAt: string; dailyRate: number; divisionName: string | null }
type Booking = { id: string; reference: string; status: string; customerStatus?: string; total?: number; createdAt: string; branchName: string; items: BookingItem[] }
type QuoteLine = { description: string; quantity: number; rate: number; unit: string }
type Quote = { id: string; quoteNumber: string; status: string; customerStatus?: string; hasOffer?: boolean; createdAt?: string; validUntil: string; jobSite: string | null; purchaseOrderNumber: string | null; subtotal: number; discount: number; tax: number; total: number; depositRequired: number; version: number; branchName: string; divisionName: string | null; lines: QuoteLine[] | null; convertedBookingId: string | null }
type PortalDocuments = {
  registered: { id: string; documentNumber: string; type: string; fileName: string; expiresOn: string | null; createdAt: string; bookingReference: string | null }[]
  agreements: { id: string; number: string; status: string; createdAt: string; bookingReference: string; bookingId: string }[]
  invoices: { id: string; number: string; status: string; total: number; balanceDue: number; createdAt: string; bookingReference: string; bookingId: string }[]
}
type BookingDetails = {
  id: string; reference: string; status: string; customerStatus?: string; createdAt: string
  branch: { name: string; address: string | null; phone: string | null; email: string | null; pickupInstructions: string | null; returnInstructions: string | null; operatingHours: { dayOfWeek: string; opensAt: string; closesAt: string; isClosed: boolean; pickupCutoff: string | null }[] }
  items: (BookingItem & { assetId: string; type: string; category: string | null; personnelRequirement: string })[]
  pricing: { baseSubtotal: number; discountAmount: number; charges: { description: string; quantity: number; unitRate: number; amount: number }[]; chargeTotal: number; taxRate: number; taxAmount: number; total: number; depositRequired: number; additionalCharges?: number; additionalChargesDescription?: string }
  bond: { required: number; held: number; deduction: number; bondDeductionReason: string | null; refund: number; status: string; bondSettledAt: string | null }
  agreement: { agreementNumber: string; status: string; termsVersion: string; terms: { title: string; content: string }[] | null; customerSignatureName: string; customerSignedAt: string; approvedByName: string; approvedAt: string } | null
  invoice: { invoiceNumber: string; status: string; issuedAt: string; subtotal: number; taxAmount: number; total: number; amountPaid: number; balanceDue: number; lines: { description: string; quantity: number; unitPrice: number }[] } | null
  payments: { receiptNumber: string; type: string; method: string; amount: number; status: string; createdAt: string }[]
  inspections: { type: string; meterReading: number | null; fuelLevelPercent: number | null; conditionNotes: string | null; damageNotes: string | null; completedAt: string }[]
  incidents: { incidentNumber: string; type: string; status: string; occurredAt: string; description: string; location: string | null }[]
  dispatches: { dispatchNumber: string; type: string; status: string; scheduledAt: string; address: string | null; assignedDriver: string | null }[]
  documents: { id: string; documentNumber: string; type: string; fileName: string; expiresOn: string | null; createdAt: string }[]
  cases: { caseNumber: string; type: string; status: string; subject: string; description?: string; resolution?: string; createdAt: string; updatedAt?: string }[]
  history?: { title: string; message: string; createdAt: string }[]
  readiness: Record<string, { complete: boolean; required?: boolean; requiredAmount?: number; label: string }>
}

const preferenceOptions: { value: HirePreference; label: string }[] = [
  { value: 'Vehicles', label: 'Vehicles' },
  { value: 'Equipment', label: 'Construction and industrial equipment' },
  { value: 'WasteAndSiteHire', label: 'Waste and site hire' },
]
function sessionPreferences(session: CustomerSession): HirePreference[] {
  if (Array.isArray(session.hirePreferences)) return session.hirePreferences.filter(value => preferenceOptions.some(option => option.value === value))
  return session.hirePreference && session.hirePreference !== 'NoPreference' ? [session.hirePreference] : []
}
const emptyRegistration = { fullName: '', email: '', phone: '', address: '', password: '', confirmPassword: '' }
type PortalSection = 'overview' | 'bookings' | 'quotes' | 'documents' | 'receipts' | 'account' | 'preferences'
type AuthMode = 'login' | 'register' | 'activate' | 'reset'
const money = (value: number) => `FJD ${value.toLocaleString('en-FJ', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`
const date = (value: string) => new Date(value).toLocaleDateString('en-FJ', { day: '2-digit', month: 'short', year: 'numeric' })
const statusLabel = customerStageLabel
const statusColor = customerStageColor
function statusAccent(status: string) { return statusColor(status) === 'success' ? 'success.main' : statusColor(status) === 'warning' ? 'warning.main' : statusColor(status) === 'error' ? 'error.main' : statusColor(status) === 'info' ? 'info.main' : 'grey.400' }
function apiMessage(reason: unknown, fallback: string) { const data = axios.isAxiosError(reason) ? reason.response?.data : undefined; const errors = data?.errors as Record<string, string[]> | undefined; return errors ? Object.values(errors).flat().join(' ') : data?.message ?? data?.detail ?? fallback }

const accountSections: { value: PortalSection; label: string; icon: ReactNode }[] = [
  { value: 'preferences', label: 'Rental preferences', icon: <DashboardOutlined /> },
  { value: 'overview', label: 'My account', icon: <DashboardOutlined /> },
  { value: 'bookings', label: 'Manage bookings', icon: <EventAvailableOutlined /> },
]

export function CustomerPortalPage({ onBack, onSessionChange }: { onBack: () => void; onSessionChange?: (signedIn: boolean) => void }) {
  const [session, setSession] = useState<CustomerSession | null>(null)
  const [bookings, setBookings] = useState<Booking[]>([])
  const [quotes, setQuotes] = useState<Quote[]>([])
  const [documents, setDocuments] = useState<PortalDocuments>({ registered: [], agreements: [], invoices: [] })
  const [section, setSection] = useState<PortalSection>(() => new URLSearchParams(window.location.search).has('reference') || sessionStorage.getItem('crems.customerSection') === 'bookings' ? 'bookings' : sessionStorage.getItem('crems.customerSection') === 'preferences' ? 'preferences' : 'overview')
  const [authMode, setAuthMode] = useState<AuthMode>('login')
  const [checking, setChecking] = useState(true)
  const [submitting, setSubmitting] = useState(false)
  const quoteDecisionPending = useRef(false)
  const [cancelQuote, setCancelQuote] = useState<Quote | null>(null)
  const [error, setError] = useState('')
  const setNotice = useActionNotice()
  const [login, setLogin] = useState({ email: '', password: '' })
  const [showPassword, setShowPassword] = useState(false)
  const [registration, setRegistration] = useState(emptyRegistration)
  const [activation, setActivation] = useState({ email: '', code: '', password: '', confirmPassword: '' })
  const [reset, setReset] = useState({ email: '', code: '', password: '', confirmPassword: '', codeSent: false })
  const [verificationCode, setVerificationCode] = useState('')
  const [verificationOpen, setVerificationOpen] = useState(false)
  const [maskedEmail, setMaskedEmail] = useState('your registered email')
  const [resendSeconds, setResendSeconds] = useState(0)
  const [profile, setProfile] = useState({ fullName: '', phone: '', address: '', identificationNumber: '', hirePreferences: [] as HirePreference[], preferredServiceIds: [] as string[] })
  const [licence, setLicence] = useState<LicenceDetails>({ status: 'Required', licenceNumber: null, classes: [], hasImage: false })
  const [licenceOpen, setLicenceOpen] = useState(false)
  const [selectedBooking, setSelectedBooking] = useState<BookingDetails | null>(null)
  const [agreementOnly, setAgreementOnly] = useState(false)
  const [loadingBooking, setLoadingBooking] = useState(false)
  const [changeBooking, setChangeBooking] = useState<Booking | null>(null)
  const [changeType, setChangeType] = useState<'dates' | 'cancel' | 'extend' | 'incident'>('cancel')
  const [requestedStart, setRequestedStart] = useState('')
  const [changeReason, setChangeReason] = useState('')
  const [requestedEnd, setRequestedEnd] = useState('')
  const [incident, setIncident] = useState({ type: 'Breakdown', location: '', policeReference: '' })
  const [declineQuote, setDeclineQuote] = useState<Quote | null>(null)
  const [declineReason, setDeclineReason] = useState('')
  const [selectedQuote, setSelectedQuote] = useState<Quote | null>(null)
  const [referenceSearch, setReferenceSearch] = useState(() => new URLSearchParams(window.location.search).get('reference') ?? sessionStorage.getItem('crems.customerReferenceSearch') ?? '')
  const [refreshingBookings, setRefreshingBookings] = useState(false)
  const [activityFilter, setActivityFilter] = useState<ActivityFilter>('all')
  const [journeySort, setJourneySort] = useState<JourneySort>('recent')
  const [journeyPage, setJourneyPage] = useState(0)
  const [expandedJourney, setExpandedJourney] = useState<string | null>(null)

  async function loadPortalData() {
    const [bookingResponse, quoteResponse, documentResponse] = await Promise.all([
      api.get<Booking[]>('/customer-account/bookings'), api.get<Quote[]>('/customer-account/quotes'), api.get<PortalDocuments>('/customer-account/documents'),
    ])
    setBookings(bookingResponse.data); setQuotes(quoteResponse.data); setDocuments(documentResponse.data)
  }
  async function refreshBookings() {
    setRefreshingBookings(true)
    try { await loadPortalData() } catch (reason) { toast(apiMessage(reason, 'Could not refresh your bookings.'), 'error') }
    finally { setRefreshingBookings(false) }
  }
  async function loadSession() {
    try {
      const current = (await api.get<CustomerSession>('/customer-account/session')).data
      setSession(current); sessionStorage.setItem('crems.customerName', current.fullName); setProfile({ fullName: current.fullName, phone: current.phone ?? '', address: current.address ?? '', identificationNumber: current.identificationNumber ?? '', hirePreferences: sessionPreferences(current), preferredServiceIds: current.preferredServiceIds ?? [] }); onSessionChange?.(true)
      try { await loadPortalData(); setLicence((await api.get<LicenceDetails>('/customer-account/licence')).data) } catch { setError('Your account is signed in, but some account information could not be loaded. Please try again.') }
      return true
    } catch { setSession(null); onSessionChange?.(false); return false }
    finally { setChecking(false) }
  }
  // Hydrate the browser-window customer session once when the portal opens.
  // eslint-disable-next-line react-hooks/set-state-in-effect, react-hooks/exhaustive-deps
  useEffect(() => { void loadSession() }, [])

  useEffect(() => {
    if (!session || section !== 'bookings') return
    const refresh = () => { if (document.visibilityState === 'visible') void loadPortalData().catch(() => undefined) }
    const timer = window.setInterval(refresh, 30_000)
    window.addEventListener('focus', refresh)
    return () => { window.clearInterval(timer); window.removeEventListener('focus', refresh) }
  }, [session, section])

  useEffect(() => {
    if (!session) return

    const expirePortalSession = () => {
      sessionStorage.removeItem('crems.customerName')
      sessionStorage.removeItem('crems.customerSection')
      setSession(null)
      setBookings([])
      setQuotes([])
      onSessionChange?.(false)
    }
    const stopMonitoring = monitorInactivity(() => {
      void api.post('/customer-account/logout').catch(() => undefined).finally(expirePortalSession)
    })
    window.addEventListener('crems:customer-session-expired', expirePortalSession)

    return () => {
      stopMonitoring()
      window.removeEventListener('crems:customer-session-expired', expirePortalSession)
    }
  }, [session, onSessionChange])
  useEffect(() => { if (resendSeconds <= 0) return; const timer = window.setInterval(() => setResendSeconds(value => Math.max(0, value - 1)), 1000); return () => window.clearInterval(timer) }, [resendSeconds])

  async function finishAuthentication() { const signedIn = await loadSession(); if (signedIn) { sessionStorage.removeItem('crems.customerSection'); onBack() } }
  async function signIn(event: FormEvent) { event.preventDefault(); setShowPassword(false); setSubmitting(true); setError(''); try { await api.post('/auth/login?useCookies=true', login); await finishAuthentication() } catch { setError('The email address or password is incorrect, or this is not a customer account.') } finally { setSubmitting(false) } }
  async function register(event: FormEvent) { event.preventDefault(); setError(''); if (registration.password !== registration.confirmPassword) return setError('The passwords do not match.'); if (registration.password.length < 10 || !/[A-Z]/.test(registration.password) || !/\d/.test(registration.password)) return setError('Use at least 10 characters with an uppercase letter and a number.'); setSubmitting(true); try { const { confirmPassword: _, ...details } = registration; void _; await api.post('/customer-account/register', details, { timeout: 30_000 }); await finishAuthentication() } catch (reason) { setError(apiMessage(reason, axios.isAxiosError(reason) && reason.code === 'ECONNABORTED' ? 'Account creation timed out. Confirm that the API is running and try again.' : 'Unable to create your account.')) } finally { setSubmitting(false) } }
  async function activate(event: FormEvent) { event.preventDefault(); setError(''); if (activation.password !== activation.confirmPassword) return setError('The passwords do not match.'); setSubmitting(true); try { await api.post('/customer-account/activate', { email: activation.email, code: activation.code, password: activation.password }); await finishAuthentication() } catch (reason) { setError(apiMessage(reason, 'Unable to activate the account.')) } finally { setSubmitting(false) } }
  async function requestReset() { setSubmitting(true); setError(''); try { const response = await api.post<{ message: string }>('/auth/password-reset/request', { email: reset.email }); setReset({ ...reset, codeSent: true }); setNotice(response.data.message) } catch { setError('Unable to request a password reset right now.') } finally { setSubmitting(false) } }
  async function completeReset(event: FormEvent) { event.preventDefault(); setError(''); if (reset.password !== reset.confirmPassword) return setError('The passwords do not match.'); setSubmitting(true); try { await api.post('/auth/password-reset/complete', { email: reset.email, code: reset.code, newPassword: reset.password }); setNotice('Password changed. You can now sign in.'); setLogin({ email: reset.email, password: '' }); setAuthMode('login') } catch (reason) { setError(apiMessage(reason, 'Unable to reset the password.')) } finally { setSubmitting(false) } }
  async function logout() { await api.post('/customer-account/logout'); sessionStorage.removeItem('crems.customerName'); sessionStorage.removeItem('crems.customerSection'); setSession(null); setBookings([]); setQuotes([]); onSessionChange?.(false) }
  async function requestVerification() { setSubmitting(true); setError(''); setNotice(''); try { const response = await api.post<{ message: string; maskedDestination?: string; resendAfterSeconds?: number }>('/customer-account/verification/request'); setMaskedEmail(response.data.maskedDestination ?? 'your registered email'); setResendSeconds(response.data.resendAfterSeconds ?? 60); setVerificationOpen(true) } catch { setError('Unable to send a verification code right now.') } finally { setSubmitting(false) } }
  async function confirmVerification(event: FormEvent) { event.preventDefault(); setSubmitting(true); setError(''); try { await api.post('/customer-account/verification/confirm', { code: verificationCode }); setNotice('Email verified successfully.'); setVerificationCode(''); setVerificationOpen(false); await loadSession() } catch (reason) { setError(apiMessage(reason, 'The verification code is invalid or expired.')) } finally { setSubmitting(false) } }
  async function updateProfile() {
    const response = await api.put<{ hirePreferences?: HirePreference[] }>('/customer-account/profile', { phone: profile.phone, address: profile.address, hirePreferences: [] })
    if (!Array.isArray(response.data.hirePreferences)) throw new Error('The customer API must be restarted to enable multiple rental preferences.')
  }
  function profileError(reason: unknown, fallback: string) { return reason instanceof Error && reason.message.startsWith('The customer API') ? reason.message : apiMessage(reason, fallback) }
  async function saveProfile(event: FormEvent) { event.preventDefault(); setSubmitting(true); setError(''); try { await updateProfile(); setNotice('Your contact details have been updated.'); await loadSession() } catch (reason) { setError(profileError(reason, 'Unable to update your profile.')) } finally { setSubmitting(false) } }
  async function openBooking(id: string, openAgreementOnly = false) { setLoadingBooking(true); setError(''); try { setAgreementOnly(openAgreementOnly); setSelectedBooking((await api.get<BookingDetails>(`/customer-account/bookings/${id}`)).data) } catch { setError('Unable to load this booking.') } finally { setLoadingBooking(false) } }
  function openChange(booking: Booking, type: 'dates' | 'cancel' | 'extend' | 'incident') { setChangeBooking(booking); setChangeType(type); setChangeReason(''); setRequestedStart(booking.items[0]?.startAt.slice(0, 10) ?? ''); setRequestedEnd(booking.items[0]?.endAt.slice(0, 10) ?? ''); setIncident({ type: 'Breakdown', location: '', policeReference: '' }); setError('') }
  async function submitChange() {
    if (!changeBooking) return
    const awaitingReview = changeBooking.status === 'Draft'
    setSubmitting(true); setError('')
    try {
      if (changeType === 'dates') await api.put(`/customer-account/bookings/${changeBooking.id}/dates`, { startAt: `${requestedStart}T09:00:00+12:00`, endAt: `${requestedEnd}T09:00:00+12:00` })
      else if (changeType === 'cancel') await api.post(`/customer-account/bookings/${changeBooking.id}/cancellation-request`, { reason: changeReason })
      else if (changeType === 'extend') await api.post(`/customer-account/bookings/${changeBooking.id}/extension-request`, { requestedEndAt: `${requestedEnd}T09:00:00+12:00`, reason: changeReason })
      else await api.post(`/customer-account/bookings/${changeBooking.id}/incident`, { type: incident.type, occurredAt: new Date().toISOString(), description: changeReason, location: incident.location || null, policeReference: incident.policeReference || null })
      setChangeBooking(null)
      setNotice(awaitingReview && changeType === 'dates' ? 'Your booking dates have been updated.' : awaitingReview && changeType === 'cancel' ? 'Your booking has been cancelled.' : 'Your request has been sent to the operating branch.')
      await loadPortalData()
    } catch (reason) { setError(apiMessage(reason, 'Your request could not be submitted.')) }
    finally { setSubmitting(false) }
  }
  async function resendConfirmation(bookingId: string) { setSubmitting(true); try { await api.post(`/customer-account/bookings/${bookingId}/resend-confirmation`); setNotice('A fresh confirmation email has been queued.') } catch (reason) { setError(apiMessage(reason, 'Unable to resend the confirmation.')) } finally { setSubmitting(false) } }
  async function uploadCustomerDocument(file?: File) { if (!file) return; setSubmitting(true); setError(''); try { const form = new FormData(); form.append('file', file); form.append('type', 'DriverLicence'); await api.post('/customer-account/documents/upload', form); setNotice('Driver licence uploaded securely.'); await loadPortalData() } catch (reason) { setError(apiMessage(reason, 'The driver licence could not be uploaded.')) } finally { setSubmitting(false) } }
  async function viewCustomerDocument(documentId: string) { setError(''); try { const response = await api.get(`/customer-account/documents/${documentId}/download`, { responseType: 'blob' }); const url = URL.createObjectURL(response.data); window.open(url, '_blank', 'noopener,noreferrer'); window.setTimeout(() => URL.revokeObjectURL(url), 60_000) } catch (reason) { setError(apiMessage(reason, 'The document could not be opened.')) } }
  async function decideQuote(quote: Quote, accepted: boolean, note?: string) {
    if (quoteDecisionPending.current) return
    quoteDecisionPending.current = true
    setSubmitting(true); setError('')
    try {
      await api.post(`/customer-account/quotes/${quote.id}/decision`, { accepted, note, expectedVersion: quote.version })
      setSelectedQuote(null); setDeclineQuote(null)
      setQuotes(current => current.map(item => item.id === quote.id ? { ...item, status: accepted ? 'Accepted' : 'Rejected', customerStatus: accepted ? 'AwaitingConfirmation' : 'Declined' } : item))
      if (accepted) setBookings(current => current.map(item => item.id === quote.convertedBookingId ? { ...item, customerStatus: 'AwaitingConfirmation' } : item))
      setNotice(accepted ? 'Quotation accepted. Awaiting branch confirmation.' : 'Quotation declined. The branch has received your reason.')
      try { await loadPortalData() } catch { toast('Your decision was saved. Refresh to load the latest rental details.', 'info') }
    } catch (reason) { toast(apiMessage(reason, 'Your quotation decision could not be saved.'), 'error') }
    finally { quoteDecisionPending.current = false; setSubmitting(false) }
  }

  async function cancelQuotationRequest() {
    if (!cancelQuote || quoteDecisionPending.current) return
    quoteDecisionPending.current = true; setSubmitting(true)
    try {
      await api.post(`/customer-account/quotes/${cancelQuote.id}/cancel`)
      setCancelQuote(null); setNotice('Your quotation request has been cancelled.')
      await loadPortalData()
    } catch (reason) { toast(apiMessage(reason, 'The quotation request could not be cancelled.'), 'error') }
    finally { quoteDecisionPending.current = false; setSubmitting(false) }
  }


  function returnToWebsite(section: CustomerSiteSection = 'services') {
    sessionStorage.setItem('crems.publicTarget', section)
    onBack()
  }

  function changeSection(next: PortalSection) {
    sessionStorage.setItem('crems.customerSection', next)
    setNotice(''); setError('')
    setSection(next)
  }

  if (checking) return <Box sx={{ minHeight: '100vh', display: 'grid', placeItems: 'center' }}><CircularProgress /></Box>
  return <Box sx={{ minHeight: '100dvh', bgcolor: 'background.default', overflowX: 'hidden' }}>
    <CustomerSiteHeader accountActive authenticated={Boolean(session)} accountName={session?.fullName} onAccount={() => undefined} onAccountSection={changeSection} onNavigate={returnToWebsite} onSignOut={logout} />
    {!session ? <Container maxWidth="sm" sx={{
      py: authMode === 'register' ? { xs: 2, sm: 3 } : 6,
      minHeight: authMode === 'register' ? { sm: 'calc(100dvh - 86px)' } : undefined,
      display: authMode === 'register' ? { sm: 'flex' } : undefined,
      alignItems: authMode === 'register' ? { sm: 'center' } : undefined,
    }}><Card variant="outlined" sx={{ width: '100%' }}><CardContent sx={{ p: authMode === 'register' ? { xs: 2.5, sm: 4 } : { xs: 3, sm: 4 } }}>
      <Typography variant="h4" fontWeight={800}>{authMode === 'register' ? 'Create customer account' : authMode === 'activate' ? 'Activate existing account' : authMode === 'reset' ? 'Reset your password' : 'Customer sign in'}</Typography>
      <Typography color="text.secondary" mt={1} mb={authMode === 'register' ? 2.5 : 3}>{authMode === 'login' ? 'Manage requests, quotations, documents and active rentals securely.' : authMode === 'register' ? 'Create one account for participating Carpenters rental services.' : authMode === 'activate' ? 'Use the code provided by Carpenters staff and choose your password.' : 'We will send a six-digit reset code to your account email.'}</Typography>
      {error && <Alert severity="error" sx={{ mb: authMode === 'register' ? 1.25 : 2, py: authMode === 'register' ? .25 : undefined }}>{error}</Alert>}
      {authMode === 'login' && <Box component="form" onSubmit={signIn}><Stack spacing={2}><TextField required type="email" label="Email" autoComplete="email" value={login.email} onChange={e => setLogin({ ...login, email: e.target.value })} /><TextField
          required
          type={showPassword ? 'text' : 'password'}
          label="Password"
          autoComplete="current-password"
          value={login.password}
          onChange={e => setLogin({ ...login, password: e.target.value })}
          slotProps={{
            input: {
              endAdornment: (
                <InputAdornment position="end">
                  <IconButton
                    type="button"
                    aria-label={showPassword ? 'Hide password' : 'Show password'}
                    onClick={() => setShowPassword(value => !value)}
                    onMouseDown={event => event.preventDefault()}
                    edge="end"
                  >
                    {showPassword ? <VisibilityOffOutlined /> : <VisibilityOutlined />}
                  </IconButton>
                </InputAdornment>
              ),
            },
          }}
        /><Button type="submit" size="large" variant="contained" disabled={submitting}>Sign in</Button><Button onClick={() => { setAuthMode('reset'); setError(''); setNotice('') }}>Forgot password?</Button><Divider>New customer?</Divider><Button variant="outlined" onClick={() => { setRegistration({ ...emptyRegistration }); setAuthMode('register'); setError('') }}>Create customer account</Button><Button onClick={() => { setAuthMode('activate'); setError('') }}>I received an activation code</Button></Stack></Box>}
      {authMode === 'register' && <Box component="form" onSubmit={register} autoComplete="off"><Stack spacing={{ xs: 1.4, sm: 1.75 }}>
        <TextField size="small" required label="Your full name" autoComplete="name" value={registration.fullName} onChange={e => setRegistration({ ...registration, fullName: e.target.value })} />
        <TextField size="small" required type="email" label="Email" autoComplete="email" value={registration.email} onChange={e => setRegistration({ ...registration, email: e.target.value })} />
        <TextField size="small" required label="Phone" autoComplete="tel" value={registration.phone} onChange={e => setRegistration({ ...registration, phone: e.target.value })} />
        <TextField size="small" label="Address" autoComplete="street-address" value={registration.address} onChange={e => setRegistration({ ...registration, address: e.target.value })} />
        <TextField size="small" required type="password" label="Password" autoComplete="new-password" helperText="At least 10 characters with an uppercase letter and number" value={registration.password} onChange={e => setRegistration({ ...registration, password: e.target.value })} />
        <TextField size="small" required type="password" label="Confirm password" autoComplete="new-password" value={registration.confirmPassword} onChange={e => setRegistration({ ...registration, confirmPassword: e.target.value })} />
        <Button type="submit" size="large" variant="contained" disabled={submitting}>{submitting ? 'Creating account…' : 'Create account'}</Button><Button onClick={() => setAuthMode('login')}>Back to sign in</Button>
      </Stack></Box>}
      {authMode === 'activate' && <Box component="form" onSubmit={activate}><Stack spacing={2}><TextField required type="email" label="Invited email address" value={activation.email} onChange={e => setActivation({ ...activation, email: e.target.value })} /><TextField required label="Six-digit activation code" value={activation.code} onChange={e => setActivation({ ...activation, code: e.target.value.replace(/\D/g, '').slice(0, 6) })} /><TextField required type="password" label="Create password" value={activation.password} onChange={e => setActivation({ ...activation, password: e.target.value })} /><TextField required type="password" label="Confirm password" value={activation.confirmPassword} onChange={e => setActivation({ ...activation, confirmPassword: e.target.value })} /><Button type="submit" variant="contained" size="large" disabled={submitting || activation.code.length !== 6}>Activate account</Button><Button onClick={() => setAuthMode('login')}>Back to sign in</Button></Stack></Box>}
      {authMode === 'reset' && <Box component="form" onSubmit={completeReset}><Stack spacing={2}><TextField required type="email" label="Account email" value={reset.email} onChange={e => setReset({ ...reset, email: e.target.value })} disabled={reset.codeSent} />{!reset.codeSent ? <Button variant="contained" size="large" disabled={submitting || !reset.email} onClick={() => void requestReset()}>Send reset code</Button> : <><TextField required label="Six-digit reset code" value={reset.code} onChange={e => setReset({ ...reset, code: e.target.value.replace(/\D/g, '').slice(0, 6) })} /><TextField required type="password" label="New password" value={reset.password} onChange={e => setReset({ ...reset, password: e.target.value })} /><TextField required type="password" label="Confirm password" value={reset.confirmPassword} onChange={e => setReset({ ...reset, confirmPassword: e.target.value })} /><Button type="submit" variant="contained" size="large" disabled={submitting || reset.code.length !== 6}>Change password</Button></>}<Button onClick={() => setAuthMode('login')}>Back to sign in</Button></Stack></Box>}
    </CardContent></Card></Container> : <Box sx={{ width: '100%', minHeight: section === 'overview' ? undefined : 'calc(100dvh - 76px)' }}>
      <Paper square elevation={0} sx={{ minHeight: section === 'overview' ? undefined : 'calc(100dvh - 76px)', border: 0, bgcolor: 'background.default' }}>
        <Box sx={{ py: { xs: 2.5, md: 2.75 }, px: { xs: 2, md: 4, lg: 5 }, minWidth: 0, maxWidth: 1660, mx: 'auto', ...(section === 'overview' ? { '@media (min-width: 1200px) and (max-height: 900px)': { py: 2, '& [data-page-header]': { py: 2, mb: 2 }, '& .MuiCardContent-root': { p: 2.5, '&:last-child': { pb: 2.5 } }, '& form': { mt: 2 }, '& form > .MuiGrid-container': { rowGap: 1.5 }, '& form > .MuiButton-root': { mt: 2 } } } : {}) }}>
      <PageHeader icon={section === 'preferences' ? <TuneOutlined /> : section === 'bookings' ? <EventAvailableOutlined /> : <AccountCircleOutlined />} title={section === 'overview' ? 'Customer profile' : accountSections.find(item => item.value === section)?.label ?? 'My account'} subtitle={section === 'preferences' ? 'Make your homepage reflect the rentals you use.' : section === 'bookings' ? 'Track your requests, quotations and active rentals.' : 'Manage your contact details, security and driver licence.'} dense actions={section === 'bookings' ? <Button size="small" variant="outlined" startIcon={<RefreshOutlined />} disabled={refreshingBookings} onClick={() => void refreshBookings()}>{refreshingBookings ? 'Refreshing…' : 'Refresh'}</Button> : undefined} />
      {error && <Alert severity="error" sx={{ mb: 3 }} onClose={() => setError('')}>{error}</Alert>}


      {section === 'preferences' && <RentalPreferencesPage onBrowse={onBack} />}
      {section === 'bookings' && <RentalJourneys bookings={bookings} quotes={quotes} search={referenceSearch} filter={activityFilter} sort={journeySort} page={journeyPage} expanded={expandedJourney} loading={loadingBooking} onSearch={value => { setReferenceSearch(value); sessionStorage.setItem('crems.customerReferenceSearch', value); setJourneyPage(0) }} onFilter={value => { setActivityFilter(value); setJourneyPage(0) }} onSort={value => { setJourneySort(value); setJourneyPage(0) }} onPage={setJourneyPage} onExpand={setExpandedJourney} onOpenBooking={openBooking} onChangeBooking={openChange} onViewQuote={setSelectedQuote} onCancelQuote={setCancelQuote} onBrowse={onBack} />}

      {section === 'quotes' && <Box><Typography color="text.secondary" mb={3}>Review prices, validity dates and customer charges before accepting or declining an offer.</Typography><CustomerQuoteCards quotes={quotes} onView={setSelectedQuote} onCancel={setCancelQuote} /></Box>}

      {section === 'documents' && <Box><Typography color="text.secondary" mb={3}>Find your rental agreements and securely stored customer documents in one place.</Typography><Grid container spacing={3}><Grid size={{ xs: 12, md: 6 }}><DocumentGroup title="Rental agreements" empty="Signed agreements will appear after pickup." items={documents.agreements.map(item => ({ key: item.id, title: item.number, subtitle: `${item.bookingReference} · ${date(item.createdAt)}`, status: item.status, action: () => void openBooking(item.bookingId) }))} /></Grid><Grid size={{ xs: 12, md: 6 }}><DocumentGroup title="Driver licence" empty="No driver licence has been uploaded." items={documents.registered.filter(item => item.type === 'DriverLicence').map(item => ({ key: item.id, title: item.fileName, subtitle: item.bookingReference ? `Linked to ${item.bookingReference}` : 'Securely stored on your account', status: item.expiresOn ? `Expires ${date(item.expiresOn)}` : 'On file', action: () => void viewCustomerDocument(item.id) }))} /></Grid></Grid></Box>}
      {section === 'receipts' && <Box><Typography color="text.secondary" mb={3}>View invoices, payment status and rental receipts.</Typography><DocumentGroup title="Invoices & receipts" empty="Final invoices will appear after billing." items={documents.invoices.map(item => ({ key: item.id, title: item.number, subtitle: `${item.bookingReference} · ${money(item.total)}`, status: item.balanceDue > 0 ? `${money(item.balanceDue)} due` : 'Paid', action: () => void openBooking(item.bookingId) }))} /></Box>}

      {section === 'overview' && <Grid container spacing={2.5} alignItems="stretch">
        <Grid size={{ xs: 12, lg: 8 }}>
          <Card variant="outlined" sx={{ height: '100%', borderRadius: 3 }}><CardContent sx={{ p: { xs: 2.5, md: 3.5 }, '&:last-child': { pb: { xs: 2.5, md: 3.5 } } }}>
            <Typography variant="h6" fontWeight={800}>Personal details</Typography>
            <Typography color="text.secondary" variant="body2" mt={.75}>Keep your contact information current for quotes, bookings and collection updates.</Typography>
            <Box component="form" onSubmit={saveProfile} mt={3}>
              <Grid container spacing={2.5}>
                <Grid size={{ xs: 12, sm: 6 }}><Typography component="label" htmlFor="profile-full-name" variant="caption" fontWeight={750} display="block" mb={.75}>Full name</Typography><TextField id="profile-full-name" fullWidth value={session.fullName} InputProps={{ readOnly: true }} sx={{ '& .MuiInputBase-root': { bgcolor: '#fafaf8' } }} helperText="Contact Carpenters to change your name." /></Grid>
                <Grid size={{ xs: 12, sm: 6 }}><Typography component="label" htmlFor="profile-email" variant="caption" fontWeight={750} display="block" mb={.75}>Account email</Typography><TextField id="profile-email" fullWidth value={session.email} InputProps={{ readOnly: true }} sx={{ '& .MuiInputBase-root': { bgcolor: '#fafaf8' } }} /></Grid>
                <Grid size={{ xs: 12, sm: 6 }}><Typography component="label" htmlFor="profile-phone" variant="caption" fontWeight={750} display="block" mb={.75}>Phone</Typography><TextField id="profile-phone" fullWidth required value={profile.phone} onChange={event => setProfile({ ...profile, phone: event.target.value })} inputProps={{ 'aria-label': 'Phone' }} /></Grid>
                <Grid size={{ xs: 12, sm: 6 }}><Typography component="label" htmlFor="profile-licence" variant="caption" fontWeight={750} display="block" mb={.75}>Driver licence number</Typography><TextField id="profile-licence" fullWidth value={licence.licenceNumber ?? 'Not verified'} InputProps={{ readOnly: true }} sx={{ '& .MuiInputBase-root': { bgcolor: '#fafaf8' } }} /></Grid>
                <Grid size={12}><Typography component="label" htmlFor="profile-address" variant="caption" fontWeight={750} display="block" mb={.75}>Address</Typography><TextField id="profile-address" fullWidth multiline minRows={1} value={profile.address} onChange={event => setProfile({ ...profile, address: event.target.value })} /></Grid>
              </Grid>
              <Button type="submit" variant="contained" disabled={submitting} sx={{ mt: 3, px: 2.5, minHeight: 44 }}>Save details</Button>
            </Box>
          </CardContent></Card>
        </Grid>
        <Grid size={{ xs: 12, lg: 4 }}><Stack spacing={2.5} sx={{ height: '100%' }}>
          <Card variant="outlined" sx={{ borderRadius: 3 }}><CardContent sx={{ p: { xs: 2.5, md: 3 }, '&:last-child': { pb: { xs: 2.5, md: 3 } } }}>
            <Typography variant="h6" fontWeight={800}>Account security</Typography><Typography color="text.secondary" variant="body2" mt={.75}>Keep your account secure and verify your email address.</Typography>
            <Stack direction="row" flexWrap="wrap" gap={1} mt={2}><Chip label={session.emailConfirmed ? 'Email verified' : 'Email not verified'} size="small" sx={{ bgcolor: session.emailConfirmed ? '#e5f4e9' : '#fbe7e5', color: session.emailConfirmed ? '#217b3f' : '#b3241d', fontWeight: 750 }} /><Chip label={licence.status === 'Verified' ? 'Licence verified' : 'Licence required'} size="small" sx={{ bgcolor: licence.status === 'Verified' ? '#e5f4e9' : '#fff1db', color: licence.status === 'Verified' ? '#217b3f' : '#855214', fontWeight: 750 }} /></Stack>
            {!session.emailConfirmed && <Button variant="contained" disabled={submitting} sx={{ mt: 2, minHeight: 44 }} onClick={() => void requestVerification()}>Verify email address</Button>}
          </CardContent></Card>
          <Card variant="outlined" sx={{ borderRadius: 3, flex: 1 }}><CardContent sx={{ p: { xs: 2.5, md: 3 }, '&:last-child': { pb: { xs: 2.5, md: 3 } } }}>
            <Typography variant="h6" fontWeight={800}>Driver licence</Typography>
            <Typography color="text.secondary" variant="body2" mt={.75}>{licence.status === 'Verified' ? 'Your licence document is securely stored and verified for vehicle rentals.' : 'Upload and verify your driver licence before driving a rental vehicle.'}</Typography>
            {licence.classes.length > 0 && <Typography variant="body2" mt={1.25}>Licence {licence.classes.length === 1 ? 'class' : 'classes'}: <b>{licence.classes.join(', ')}</b></Typography>}
            <Stack direction="row" gap={1} flexWrap="wrap" mt={2}>{licence.hasImage && <Button variant="outlined" onClick={() => void openProtectedFile('/customer-account/licence/image')}>View licence</Button>}<Button variant={licence.hasImage ? 'text' : 'contained'} onClick={() => { setNotice(''); setLicenceOpen(true) }}>{licence.hasImage ? 'Replace licence' : 'Verify driver licence'}</Button></Stack>
          </CardContent></Card>
        </Stack></Grid>
      </Grid>}

        </Box>
      </Paper>
      </Box>}

    <BookingDetailDialog booking={selectedBooking} quotation={quotes.find(quote => quote.convertedBookingId === selectedBooking?.id)} agreementOnly={agreementOnly} onClose={() => { setSelectedBooking(null); setAgreementOnly(false) }} onResend={resendConfirmation} />
    <LicenceWorkflowDialog open={licenceOpen} onClose={() => setLicenceOpen(false)} onSaved={value => { setLicence(value); setNotice('Your driver licence has been verified and saved.') }} />
    <Dialog open={verificationOpen} onClose={() => !submitting && setVerificationOpen(false)} fullWidth maxWidth="xs" PaperProps={{ sx: { borderRadius: 2.5 } }}><Box component="form" onSubmit={confirmVerification}><DialogTitle sx={{ px: 3.5, py: 2.5, display: 'flex', justifyContent: 'space-between', alignItems: 'center', fontSize: 26, fontWeight: 850 }}>Verify your email<IconButton aria-label="Close" onClick={() => setVerificationOpen(false)}><CloseOutlined /></IconButton></DialogTitle><DialogContent dividers sx={{ p: 3.5 }}><Typography color="text.secondary" mb={2}>Enter the six-digit code sent to <b>{maskedEmail}</b>. It expires after 10 minutes.</Typography><Typography variant="caption" fontWeight={800}>Verification code</Typography><TextField fullWidth autoFocus required placeholder="000000" value={verificationCode} onChange={event => setVerificationCode(event.target.value.replace(/\D/g, '').slice(0, 6))} inputProps={{ inputMode: 'numeric', maxLength: 6, style: { textAlign: 'center', fontSize: 27, fontWeight: 850, letterSpacing: '.28em' } }} sx={{ mt: .75 }} /><Stack spacing={1.25} mt={2}><Button type="submit" variant="contained" size="large" disabled={submitting || verificationCode.length !== 6}>Verify email</Button><Button variant="outlined" size="large" disabled={submitting || resendSeconds > 0} onClick={() => void requestVerification()}>Send a new code</Button></Stack><Typography variant="caption" color="text.secondary" display="block" textAlign="center" mt={2}>{resendSeconds > 0 ? `Code sent. You can request another in ${resendSeconds} seconds.` : 'You can request another code now.'}</Typography></DialogContent></Box></Dialog>
    <QuoteDetailDialog quote={selectedQuote} submitting={submitting} onClose={() => setSelectedQuote(null)} onAccept={quote => void decideQuote(quote, true)} onDecline={quote => { setSelectedQuote(null); setDeclineQuote(quote); setDeclineReason('') }} />
    <Dialog open={Boolean(changeBooking)} onClose={() => !submitting && setChangeBooking(null)} fullWidth maxWidth="sm">
      <DialogTitle>{changeType === 'dates' ? changeBooking?.status === 'Draft' ? 'Change booking dates' : 'Request a date change' : changeType === 'cancel' ? changeBooking?.status === 'Draft' ? 'Cancel booking' : 'Request cancellation' : changeType === 'extend' ? 'Request an extension' : 'Report an incident'}</DialogTitle>
      <DialogContent><Stack spacing={2} mt={1}>
        <Alert severity={changeType === 'incident' ? 'warning' : 'info'}>{changeBooking?.reference} · {changeBooking?.items[0]?.name}</Alert>
        {changeType === 'dates' && <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}><TextField fullWidth required type="date" label="Pickup date" InputLabelProps={{ shrink: true }} value={requestedStart} onChange={e => setRequestedStart(e.target.value)} /><TextField fullWidth required type="date" label="Return date" InputLabelProps={{ shrink: true }} value={requestedEnd} onChange={e => setRequestedEnd(e.target.value)} inputProps={{ min: requestedStart }} /></Stack>}
        {changeType === 'extend' && <TextField required type="date" label="Requested return date" InputLabelProps={{ shrink: true }} value={requestedEnd} onChange={e => setRequestedEnd(e.target.value)} inputProps={{ min: changeBooking?.items[0]?.endAt.slice(0, 10) }} />}
        {changeType === 'incident' && <><FormControl><InputLabel>Incident type</InputLabel><Select label="Incident type" value={incident.type} onChange={e => setIncident({ ...incident, type: e.target.value })}>{['Breakdown', 'Accident', 'Damage', 'Theft', 'TrafficOffence', 'Other'].map(item => <MenuItem key={item} value={item}>{item.replace('TrafficOffence', 'Traffic offence')}</MenuItem>)}</Select></FormControl><TextField label="Current location" value={incident.location} onChange={e => setIncident({ ...incident, location: e.target.value })} /><TextField label="Police reference (if applicable)" value={incident.policeReference} onChange={e => setIncident({ ...incident, policeReference: e.target.value })} /></>}
        {changeType !== 'dates' && !(changeType === 'cancel' && changeBooking?.status === 'Draft') && <TextField required={changeBooking?.status !== 'Draft'} multiline minRows={4} label={changeType === 'incident' ? 'What happened? Is everyone safe?' : changeBooking?.status === 'Draft' ? 'Reason (optional)' : 'Reason or additional information'} value={changeReason} onChange={e => setChangeReason(e.target.value)} />}
        <Typography variant="body2" color="text.secondary">{changeBooking?.status === 'Draft' ? changeType === 'cancel' ? 'Cancel this booking? Cancellation takes effect immediately.' : 'Changes take effect immediately.' : 'Confirmed booking changes are sent to the branch for approval.'}</Typography>
      </Stack></DialogContent>
      <DialogActions sx={{ p: 3 }}><Button onClick={() => setChangeBooking(null)}>Close</Button><Button variant="contained" color={changeType === 'incident' ? 'warning' : 'primary'} disabled={submitting || (changeBooking?.status !== 'Draft' && changeType !== 'dates' && !changeReason.trim()) || (changeType === 'dates' && (!requestedStart || !requestedEnd)) || (changeType === 'extend' && !requestedEnd)} onClick={() => void submitChange()}>{submitting ? 'Sending…' : changeBooking?.status === 'Draft' && changeType === 'dates' ? 'Save dates' : changeBooking?.status === 'Draft' && changeType === 'cancel' ? 'Cancel booking' : 'Submit request'}</Button></DialogActions>
    </Dialog>
    <Dialog open={Boolean(cancelQuote)} onClose={() => !submitting && setCancelQuote(null)} fullWidth maxWidth="xs"><DialogTitle>Cancel quotation request?</DialogTitle><DialogContent><Typography>{cancelQuote?.quoteNumber}</Typography><Typography mt={1}>This will cancel your request immediately.</Typography></DialogContent><DialogActions sx={{p:2}}><Button disabled={submitting} onClick={() => setCancelQuote(null)}>Keep request</Button><Button disabled={submitting} color="error" variant="contained" onClick={() => void cancelQuotationRequest()}>Cancel request</Button></DialogActions></Dialog>
    <Dialog open={Boolean(declineQuote)} onClose={() => !submitting && setDeclineQuote(null)} fullWidth maxWidth="sm"><DialogTitle>Decline quotation {declineQuote?.quoteNumber}</DialogTitle><DialogContent><TextField autoFocus fullWidth multiline minRows={4} label="Why are you declining?" value={declineReason} onChange={e => setDeclineReason(e.target.value)} sx={{ mt: 1 }} /><Typography variant="body2" color="text.secondary" mt={2}>Your reason helps the branch offer a more suitable option or revised quotation.</Typography></DialogContent><DialogActions sx={{ p: 3 }}><Button onClick={() => setDeclineQuote(null)}>Keep quotation</Button><Button variant="contained" color="error" disabled={submitting || !declineReason.trim()} onClick={() => declineQuote && void decideQuote(declineQuote, false, declineReason)}>Decline quotation</Button></DialogActions></Dialog>
  </Box>
}

async function downloadQuotePdf(quote: Quote) {
  await downloadRentalFinancialPdf({ kind: 'Quotation', reference: quote.quoteNumber,
    details: [['Division', quote.divisionName ?? 'Carpenters Fiji'], ['Branch', quote.branchName], ['Valid until', date(quote.validUntil)], ...(quote.jobSite ? [['Job site', quote.jobSite] as [string, string]] : []), ...(quote.purchaseOrderNumber ? [['Purchase order', quote.purchaseOrderNumber] as [string, string]] : [])],
    lines: (quote.lines ?? []).map(line => ({ description: line.description, quantity: line.quantity, rate: line.rate, unit: line.unit })),
    totals: [{ label: 'Subtotal', amount: quote.subtotal }, ...(quote.discount > 0 ? [{ label: 'Discount', amount: -quote.discount }] : []), { label: 'VAT', amount: quote.tax }, { label: 'Quotation total', amount: quote.total, emphasis: true }, ...(quote.depositRequired > 0 ? [{ label: 'Refundable bond (held separately)', amount: quote.depositRequired }] : [])],
    note: 'This quotation is valid until the date shown above and remains subject to the stated hire terms. A refundable bond, where required, is held separately.' })
}

function QuotePrintButton({quote}:{quote:Quote}){return <Button size="small" variant="outlined" startIcon={<DescriptionOutlined/>} onClick={()=>void downloadQuotePdf(quote)}>Download quotation</Button>}

type ActivityFilter = 'all' | 'bookings' | 'quotations' | 'confirmed' | 'on-hire' | 'completed' | 'cancelled'
type JourneySort = 'recent' | 'oldest' | 'needs-action'
type Journey = { id: string; kind: 'booking' | 'quote'; createdAt: string; attention: boolean; active: boolean; title: string; meta: string; reference: string; status: string; dateText: string; total?: number; booking?: Booking; quote?: Quote }
const needsBookingAction = (booking: Booking) => booking.status === 'Draft'
const needsQuoteAction = (quote: Quote) => ['Sent', 'Negotiating'].includes(quote.status)
const activeBooking = (booking: Booking) => ['Confirmed', 'ConvertedToRental'].includes(booking.status)

function RentalJourneys({ bookings, quotes, search, filter, sort, page, expanded, loading, onSearch, onFilter, onSort, onPage, onExpand, onOpenBooking, onChangeBooking, onViewQuote, onCancelQuote, onBrowse }: {
  bookings: Booking[]; quotes: Quote[]; search: string; filter: ActivityFilter; sort: JourneySort; page: number; expanded: string | null; loading: boolean
  onSearch: (value: string) => void; onFilter: (value: ActivityFilter) => void; onSort: (value: JourneySort) => void; onPage: (value: number) => void; onExpand: (value: string | null) => void
  onOpenBooking: (id: string, agreementOnly?: boolean) => void; onChangeBooking: (booking: Booking, type: 'dates' | 'cancel' | 'extend' | 'incident') => void; onViewQuote: (quote: Quote) => void; onCancelQuote: (quote: Quote) => void; onBrowse: () => void
}) {
  const linkedIds = new Set(quotes.map(quote => quote.convertedBookingId).filter(Boolean))
  const journeyFor = (booking?: Booking, quote?: Quote): Journey => {
    const item = booking?.items[0]
    const stage = customerJourneyStage(booking, quote)
    return {
      id: quote ? `quote-${quote.id}` : `booking-${booking!.id}`, kind: quote ? 'quote' : 'booking',
      createdAt: quote?.createdAt ?? booking?.createdAt ?? new Date(0).toISOString(),
      attention: stage === 'ReviewQuotation', active: ['Confirmed', 'OnHire'].includes(stage),
      title: item?.name ?? `${quote?.divisionName ?? 'Rental request'} · ${quote?.branchName ?? booking?.branchName ?? ''}`,
      meta: `${item?.divisionName ?? quote?.divisionName ?? 'Carpenters Fiji'} · ${booking?.branchName ?? quote?.branchName ?? ''}`,
      reference: quote && ['AwaitingQuotation', 'ReviewQuotation'].includes(stage) ? quote.quoteNumber : booking?.reference ?? quote!.quoteNumber,
      status: stage, dateText: item ? `${date(item.startAt)} – ${date(item.endAt)}` : quote && hasQuotationOffer(quote) ? `Valid until ${date(quote.validUntil)}` : 'The branch is preparing your quotation.',
      total: quote ? hasQuotationOffer(quote) ? quote.total : undefined : booking?.total, booking, quote,
    }
  }
  const journeys: Journey[] = [
    ...quotes.map(quote => journeyFor(bookings.find(booking => booking.id === quote.convertedBookingId), quote)),
    ...bookings.filter(booking => !linkedIds.has(booking.id)).map(booking => journeyFor(booking)),
  ]
  const filtered = journeys.filter(journey => `${journey.title} ${journey.meta} ${journey.reference} ${journey.quote?.quoteNumber ?? ""} ${journey.booking?.reference ?? ""}`.toLowerCase().includes(search.trim().toLowerCase())).filter(journey => filter === 'all' ? true : filter === 'bookings' ? journey.kind === 'booking' : filter === 'quotations' ? journey.kind === 'quote' : journey.status === ({ confirmed: 'Confirmed', 'on-hire': 'OnHire', completed: 'Completed', cancelled: 'Cancelled' } as Record<string, string>)[filter]).sort((a, b) => sort === 'needs-action' ? Number(b.attention) - Number(a.attention) || new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime() : (new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime()) * (sort === 'recent' ? 1 : -1))
  const attention = journeys.filter(journey => journey.attention).sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime())
  const pageSize = 10; const totalPages = Math.max(1, Math.ceil(filtered.length / pageSize)); const currentPage = Math.min(page, totalPages - 1); const pageJourneys = filtered.slice(currentPage * pageSize, currentPage * pageSize + pageSize)
  const filters: { value: ActivityFilter; label: string }[] = [{ value: 'all', label: 'All' }, { value: 'bookings', label: 'Bookings' }, { value: 'quotations', label: 'Quotations' }, { value: 'confirmed', label: 'Confirmed' }, { value: 'on-hire', label: 'On Hire' }, { value: 'completed', label: 'Completed' }, { value: 'cancelled', label: 'Cancelled' }]
  return <Stack spacing={2.5}>
    <Card variant="outlined"><CardContent sx={{ p: 1.5 }}><TextField fullWidth size="small" placeholder="Search reference, rental or supplier" value={search} onChange={event => onSearch(event.target.value)} InputProps={{ startAdornment: <InputAdornment position="start"><SearchOutlined /></InputAdornment> }} /><Stack direction="row" gap={.75} flexWrap="wrap" mt={1.25}>{filters.map(item => <Button key={item.value} size="small" variant={filter === item.value ? 'contained' : 'outlined'} color={filter === item.value ? 'primary' : 'inherit'} onClick={() => onFilter(item.value)} sx={{ borderRadius: 2 }}>{item.label}</Button>)}<Button size="small" color="inherit" onClick={() => { onSearch(''); onFilter('all') }}>Clear filters</Button></Stack></CardContent></Card>
    <Box><Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" alignItems={{ sm: 'center' }} gap={1} mb={1.25}><Typography variant="h6" fontWeight={800}>{filter === 'quotations' ? 'Quotations' : filter === 'bookings' ? 'Bookings' : 'All rental journeys'}</Typography><Stack direction="row" alignItems="center" gap={1}><Typography variant="caption" color="text.secondary">{filtered.length} result{filtered.length === 1 ? '' : 's'} · showing {filtered.length ? currentPage * pageSize + 1 : 0}–{Math.min((currentPage + 1) * pageSize, filtered.length)}</Typography><TextField select size="small" value={sort} onChange={event => onSort(event.target.value as JourneySort)} sx={{ minWidth: 130 }}><MenuItem value="recent">Recent</MenuItem><MenuItem value="oldest">Oldest</MenuItem><MenuItem value="needs-action">Needs action</MenuItem></TextField></Stack></Stack>{pageJourneys.length ? <Stack spacing={1.25}>{pageJourneys.map(journey => <Card key={journey.id} variant="outlined"><JourneyCard journey={journey} loading={loading} onExpand={onExpand} expanded={expanded === journey.id} onOpenBooking={onOpenBooking} onChangeBooking={onChangeBooking} onViewQuote={onViewQuote} onCancelQuote={onCancelQuote} /></Card>)}</Stack> : <EmptyState title="No matching rental journeys" text="Try another filter or browse rentals to start a new request." action="Browse rentals" onAction={onBrowse} />}</Box>
    {filtered.length > pageSize && <Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" alignItems={{ sm: 'center' }} gap={1}><Typography variant="caption" color="text.secondary">Showing {currentPage * pageSize + 1}–{Math.min((currentPage + 1) * pageSize, filtered.length)} of {filtered.length}</Typography><Stack direction="row" gap={.5}><Button size="small" disabled={currentPage === 0} onClick={() => onPage(currentPage - 1)}>Previous</Button>{Array.from({ length: totalPages }, (_, index) => <Button key={index} size="small" variant={currentPage === index ? 'contained' : 'outlined'} onClick={() => onPage(index)}>{index + 1}</Button>)}<Button size="small" disabled={currentPage === totalPages - 1} onClick={() => onPage(currentPage + 1)}>Next</Button></Stack></Stack>}
  </Stack>
}

function JourneyCard({ journey, compact = false, loading, onOpenBooking, onChangeBooking, onViewQuote, onCancelQuote }: {
  journey: Journey; compact?: boolean; loading: boolean; expanded?: boolean; onExpand?: (id: string | null) => void
  onOpenBooking: (id: string) => void; onChangeBooking: (booking: Booking, type: 'dates' | 'cancel' | 'extend' | 'incident') => void
  onViewQuote: (quote: Quote) => void; onCancelQuote: (quote: Quote) => void
}) {
  const { booking, quote } = journey
  const actions = customerJourneyActions(journey.status, Boolean(quote))
  return <Box data-testid="customer-rental-journey" data-stage={journey.status} data-journey-id={journey.id}
    sx={{ p: 2, borderLeft: 4, borderLeftColor: statusAccent(journey.status), borderBottom: compact ? 0 : 1, borderBottomColor: 'divider', '&:last-child': { borderBottom: 0 } }}>
    <Stack spacing={2}>
      <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" gap={2}>
        <Box><Typography variant="overline">{quote ? 'Quotation' : 'Booking'}</Typography><Typography fontWeight={800}>{journey.title}</Typography>
          <Typography variant="body2" color="text.secondary">{journey.meta}</Typography><Typography variant="caption" color="text.secondary" display="block" mt={.5}>Reference: {quote?.quoteNumber ?? journey.reference}</Typography>
        </Box>
        <Stack alignItems={{ md: 'flex-end' }} gap={.75}><Chip size="small" label={statusLabel(journey.status)} color={statusColor(journey.status)} />
          {journey.total != null && <Box sx={{ bgcolor: '#15140f', color: '#fff', borderRadius: 1.5, px: 1.75, py: 1, borderLeft: '4px solid #ffed00' }}><Typography variant="h6" fontWeight={850} sx={{lineHeight:1.2,fontVariantNumeric:'tabular-nums'}}>{money(journey.total)}</Typography><Typography variant="caption" sx={{color:'#ffed00'}}>Including VAT</Typography></Box>}
        </Stack>
      </Stack>
      <Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" alignItems={{ sm: 'center' }} gap={1.5}>
        <Typography variant="body2">{journey.dateText}</Typography>
        <Stack direction="row" gap={.75} flexWrap="wrap" justifyContent={{ md: 'flex-end' }}>
          {actions.includes('summary') && booking && <Button size="small" variant="contained" disabled={loading} onClick={() => onOpenBooking(booking.id)}>View summary</Button>}
          {actions.includes('summary') && !booking && quote && hasQuotationOffer(quote) && <Button size="small" variant="contained" onClick={() => onViewQuote(quote)}>View summary</Button>}
          {actions.includes('cancelRequest') && quote && <Button size="small" color="error" variant="outlined" onClick={() => onCancelQuote(quote)}>Cancel request</Button>}
          {actions.includes('reviewQuote') && quote && hasQuotationOffer(quote) && <Button size="small" variant="contained" onClick={() => onViewQuote(quote)}>Review quotation</Button>}
          {booking && actions.includes('dates') && <Button size="small" variant="outlined" onClick={() => onChangeBooking(booking, 'dates')}>Change dates</Button>}
          {booking && actions.includes('cancelBooking') && <Button size="small" color="error" variant="outlined" onClick={() => onChangeBooking(booking, 'cancel')}>Cancel booking</Button>}
          {booking && actions.includes('requestCancellation') && <Button size="small" color="error" variant="outlined" onClick={() => onChangeBooking(booking, 'cancel')}>Request cancellation</Button>}
          {booking && actions.includes('extension') && <Button size="small" variant="outlined" onClick={() => onChangeBooking(booking, 'extend')}>Request extension</Button>}
          {booking && actions.includes('incident') && <Button size="small" color="warning" variant="contained" onClick={() => onChangeBooking(booking, 'incident')}>Emergency / incident</Button>}
        </Stack>
      </Stack>
    </Stack>
  </Box>
}

function CustomerQuoteCards({quotes,onView,onCancel}:{quotes:Quote[];onView:(quote:Quote)=>void;onCancel:(quote:Quote)=>void}){
  if(!quotes.length)return <Typography color="text.secondary">No quotations are linked to this account.</Typography>
  return <Stack spacing={2}>{quotes.map(quote => { const stage=customerJourneyStage(undefined,quote); return <Card key={quote.id} variant="outlined"><CardContent><Stack direction={{xs:'column',sm:'row'}} justifyContent="space-between" gap={2}><Box><Typography variant="overline">{quote.quoteNumber}</Typography><Typography fontWeight={750}>{quote.divisionName} · {quote.branchName}</Typography></Box><Chip label={statusLabel(stage)} color={statusColor(stage)}/></Stack>{stage==='AwaitingQuotation'?<Button color="error" onClick={()=>onCancel(quote)}>Cancel request</Button>:hasQuotationOffer(quote)&&<Button onClick={()=>onView(quote)}>{stage==='ReviewQuotation'?'Review quotation':'View summary'}</Button>}</CardContent></Card>})}</Stack>
}

function QuoteDetailDialog({quote,submitting,onClose,onAccept,onDecline}:{quote:Quote|null;submitting:boolean;onClose:()=>void;onAccept:(quote:Quote)=>void;onDecline:(quote:Quote)=>void}){
  if(!quote || !hasQuotationOffer(quote))return null
  return <Dialog open fullWidth maxWidth="md" onClose={onClose}><DialogTitle><Stack direction="row" justifyContent="space-between" alignItems="center" gap={2}><Box><Typography variant="overline">{quote.quoteNumber}</Typography><Typography variant="h5" fontWeight={800}>{quote.divisionName??'Carpenters Fiji'} · {quote.branchName}</Typography></Box><Chip label={statusLabel(customerJourneyStage(undefined,quote))} color={statusColor(customerJourneyStage(undefined,quote))}/></Stack></DialogTitle><DialogContent dividers><Typography color="text.secondary">Valid until {date(quote.validUntil)}</Typography><Box sx={{bgcolor:'#f5f5f1',borderRadius:2,p:2.5,mt:2}}><Stack spacing={1}>{(quote.lines??[]).map((line,index)=><Stack key={`${line.description}-${index}`} direction="row" justifyContent="space-between" gap={2}><Typography>{line.description} <Typography component="span" variant="caption" color="text.secondary">× {line.quantity} {String(line.unit??'unit').toLowerCase()}</Typography></Typography><Typography fontWeight={650}>{money(line.quantity*line.rate)}</Typography></Stack>)}<Divider sx={{my:1}}/><Stack direction="row" justifyContent="space-between"><Typography>Subtotal</Typography><Typography>{money(quote.subtotal)}</Typography></Stack><Stack direction="row" justifyContent="space-between"><Typography>Discount</Typography><Typography>−{money(quote.discount)}</Typography></Stack><Stack direction="row" justifyContent="space-between"><Typography>VAT</Typography><Typography>{money(quote.tax)}</Typography></Stack><Divider sx={{my:1}}/><Stack direction="row" justifyContent="space-between"><Typography variant="h6" fontWeight={750}>Total</Typography><Typography variant="h6" fontWeight={800}>{money(quote.total)}</Typography></Stack><Typography color="text.secondary">Refundable bond required: {money(quote.depositRequired ?? 0)}</Typography><Stack direction="row" justifyContent="space-between"><Typography fontWeight={750}>Total including refundable bond</Typography><Typography fontWeight={800}>{money(quote.total + (quote.depositRequired ?? 0))}</Typography></Stack></Stack></Box></DialogContent><DialogActions sx={{p:3}}><QuotePrintButton quote={quote}/><Box sx={{flex:1}}/>{customerJourneyStage(undefined, quote) === 'ReviewQuotation'&&<><Button variant="outlined" color="error" disabled={submitting} onClick={()=>onDecline(quote)}>Decline</Button><Button variant="contained" color="success" disabled={submitting} onClick={()=>onAccept(quote)}>Accept quote</Button></>}<Button onClick={onClose}>Close</Button></DialogActions></Dialog>
}

function BookingDetailDialog({ booking, quotation, agreementOnly = false, onClose, onResend }: { booking: BookingDetails | null; quotation?: Quote; agreementOnly?: boolean; onClose: () => void; onResend: (bookingId: string) => Promise<void> }) {
  const [documentError, setDocumentError] = useState('')
  if (!booking) return null
  const item = booking.items[0]
  // Some older bookings do not have every optional portal field. Keep their
  // rental page usable instead of allowing one missing collection to crash it.
  const readiness = booking.readiness ?? {}
  const outstanding = Object.values(readiness).filter(x => x.required !== false && !x.complete)
  async function downloadDocument(id: string, fileName: string) {
    setDocumentError('')
    try { const { data } = await api.get<Blob>('/customer-account/documents/' + id + '/download', { responseType: 'blob' }); const url = URL.createObjectURL(data); const link = document.createElement('a'); link.href = url; link.download = fileName; link.click(); window.setTimeout(() => URL.revokeObjectURL(url), 60_000) }
    catch (reason) { setDocumentError(apiMessage(reason, 'The document could not be downloaded.')) }
  }
  async function downloadReceipt() {
    if (!booking?.invoice) return
    try {
      await downloadRentalFinancialPdf({ kind: booking.invoice.balanceDue > 0 ? 'Invoice' : 'Receipt', reference: booking.invoice.invoiceNumber,
        details: [['Booking', booking.reference], ['Rental', item?.name ?? 'Rental service'], ['Branch', booking.branch.name], ['Issued', date(booking.invoice.issuedAt)], ['Status', booking.invoice.status]],
        lines: booking.invoice.lines.map(line => ({ description: line.description, quantity: line.quantity, rate: line.unitPrice })),
        totals: [{ label: 'Subtotal', amount: booking.invoice.subtotal }, { label: 'VAT', amount: booking.invoice.taxAmount }, { label: 'Invoice total', amount: booking.invoice.total, emphasis: true }, { label: 'Payments received', amount: booking.invoice.amountPaid }, { label: 'Balance due', amount: booking.invoice.balanceDue, emphasis: true }],
        note: booking.invoice.balanceDue > 0 ? 'Please contact your branch for payment arrangements. This invoice is not proof of payment.' : 'Payment received as recorded above. Thank you for choosing Carpenters Rentals & Hire.' })
    } catch (reason) { setDocumentError(apiMessage(reason, 'The invoice could not be downloaded.')) }
  }

  async function agreementPdf(openInNewTab: boolean) {
    if (!booking?.agreement) return
    try {
      const agreement = (await api.get<RentalAgreementData>(`/customer-account/bookings/${booking.id}/agreement`)).data
      if (openInNewTab) await openRentalAgreementPdf(agreement)
      else await downloadRentalAgreementPdf(agreement)
    } catch (reason) {
      window.alert(apiMessage(reason, 'The agreement could not be downloaded.'))
    }
  }
  if (agreementOnly) return <Dialog open fullWidth maxWidth="sm" onClose={onClose}><DialogTitle>Rental agreement</DialogTitle><DialogContent><Typography color="text.secondary">{booking.agreement ? `Choose how you would like to access ${booking.agreement.agreementNumber}.` : 'A signed agreement is not available for this booking yet.'}</Typography></DialogContent><DialogActions sx={{p:3}}><Button onClick={onClose}>Close</Button><Box sx={{flex:1}}/>{booking.agreement && <><Button onClick={() => void agreementPdf(true)}>View in new tab</Button><Button variant="contained" onClick={() => void agreementPdf(false)}>Download PDF</Button></>}</DialogActions></Dialog>
  return <Dialog open fullWidth maxWidth="xl" onClose={onClose} slotProps={{ paper: { sx: { m: { xs: 1, sm: 2 }, width: 'calc(100% - 32px)', maxHeight: 'calc(100dvh - 32px)' } } }}><DialogTitle sx={{ px: 2.5, py: 1.5 }}><Stack direction="row" justifyContent="space-between" alignItems="flex-start" gap={2}><Box sx={{ minWidth: 0 }}><Typography variant="caption" color="text.secondary" sx={{ overflowWrap: 'anywhere' }}>Reference: {quotation?.quoteNumber ?? booking.reference}</Typography><Typography variant="h5" fontWeight={800}>{item?.name ?? 'Rental summary'}</Typography></Box><Chip size="small" label={statusLabel(customerJourneyStage(booking, quotation))} color={statusColor(customerJourneyStage(booking, quotation))} /></Stack></DialogTitle>
    <DialogContent dividers sx={{ p: { xs: 1.5, sm: 2 }, '& .MuiButton-root': { minHeight: 32 }, '& .MuiTypography-body2': { lineHeight: 1.4 } }}><Stack spacing={1.5}>
      {documentError && <Alert severity="error">{documentError}</Alert>}
      {outstanding.length > 0 && booking.status === 'Confirmed' && <Alert severity="warning"><Typography fontWeight={750}>Before collection</Typography>{outstanding.map((entry, index) => <Typography key={index} variant="body2">{entry.label}</Typography>)}</Alert>}
      <Grid container spacing={2}><Grid size={{ xs: 12, sm: 7 }}><Typography variant="overline" color="text.secondary">Hire details</Typography><Typography fontWeight={750}>{item ? date(item.startAt) + ' – ' + date(item.endAt) : 'Dates to be confirmed'}</Typography><Typography variant="body2" mt={.5}>{item?.divisionName} · {item?.category ?? item?.type}</Typography><Typography variant="body2" color="text.secondary" mt={.5}>{booking.branch.name}{booking.branch.address ? ' · ' + booking.branch.address : ''}</Typography>{item?.personnelRequirement === 'Required' && <Typography variant="body2" mt={.5}>Driver/operator included</Typography>}{booking.items.slice(1).map((entry, index) => <Typography key={index} variant="body2" mt={.5}>{entry.name}</Typography>)}</Grid>
      <Grid size={{ xs: 12, sm: 5 }}><Box sx={{ bgcolor: '#f5f5ef', borderRadius: 2, p: 2 }}><Typography variant="caption" color="text.secondary">{booking.invoice ? 'Invoice total' : 'Estimated hire total'}</Typography><Typography variant="h5" fontWeight={850}>{money(booking.invoice?.total ?? booking.pricing.total)}</Typography>{booking.invoice && <Typography variant="body2" mt={.5}>Paid {money(booking.invoice.amountPaid)} · Due {money(booking.invoice.balanceDue)}</Typography>}{booking.bond.required > 0 && <Typography variant="caption" display="block" mt={.75}>Refundable bond: {money(booking.bond.required)}{booking.bond.held > 0 ? ' · Held ' + money(booking.bond.held) : ''}{booking.bond.refund > 0 ? ' · Refunded ' + money(booking.bond.refund) : ''}{booking.bond.deduction > 0 ? ' · Deducted ' + money(booking.bond.deduction) : ''}</Typography>}</Box></Grid></Grid>
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'minmax(0, 1fr) minmax(0, 1fr)' }, gap: 2.5, alignItems: 'start' }}><Stack spacing={1.5}><Box><Typography fontWeight={800} mb={1}>Documents</Typography><Stack spacing={1}>
        {quotation && hasQuotationOffer(quotation) && <Stack direction={{xs: 'column', sm: 'row'}} justifyContent="space-between" alignItems={{sm: 'center'}} gap={1} sx={{p:1,border:1,borderColor:'divider',borderRadius:1.5}}><Box><Typography variant="body2" fontWeight={750}>Quotation</Typography><Typography variant="caption" color="text.secondary">{quotation.quoteNumber}</Typography></Box><QuotePrintButton quote={quotation}/></Stack>}{booking.agreement && ['Signed', 'Approved', 'Active', 'Completed'].includes(booking.agreement.status) && <Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" alignItems={{ sm: 'center' }} gap={1} sx={{ p: 1.5, border: '1px solid', borderColor: 'divider', borderRadius: 1.5 }}><Box><Typography variant="body2" fontWeight={750}>Rental agreement</Typography><Typography variant="caption" color="text.secondary">Signed {date(booking.agreement.customerSignedAt)}</Typography></Box><Stack direction="row" gap={1}><Button size="small" onClick={() => void agreementPdf(true)}>View</Button><Button size="small" variant="outlined" startIcon={<DescriptionOutlined />} onClick={() => void agreementPdf(false)}>Download PDF</Button></Stack></Stack>}
        {booking.invoice && <Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" alignItems={{ sm: 'center' }} gap={1} sx={{ p: 1.5, border: '1px solid', borderColor: 'divider', borderRadius: 1.5 }}><Box><Typography variant="body2" fontWeight={750}>{booking.invoice.balanceDue > 0 ? 'Invoice' : 'Invoice and receipt'}</Typography><Typography variant="caption" color="text.secondary">{booking.invoice.invoiceNumber}</Typography></Box><Button size="small" variant="outlined" startIcon={<DescriptionOutlined />} onClick={() => void downloadReceipt()}>Download PDF</Button></Stack>}
        {(booking.documents ?? []).filter(document => document.type !== 'DriverLicence').map(document => <Stack key={document.id} direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" alignItems={{ sm: 'center' }} gap={1} sx={{ p: 1.5, border: '1px solid', borderColor: 'divider', borderRadius: 1.5 }}><Typography variant="body2" sx={{ overflowWrap: 'anywhere' }}>{document.fileName}</Typography><Button size="small" variant="outlined" startIcon={<DescriptionOutlined />} onClick={() => void downloadDocument(document.id, document.fileName)}>Download</Button></Stack>)}
        {!(quotation && hasQuotationOffer(quotation)) && !booking.agreement && !booking.invoice && !(booking.documents ?? []).some(document => document.type !== 'DriverLicence') && <Typography variant="body2" color="text.secondary">Documents will appear when issued by your branch.</Typography>}
      </Stack></Box>
      <RentalActivity booking={booking} section="charges" /></Stack><RentalActivity booking={booking} section="history" /></Box>
      {booking.dispatches?.filter(dispatch => !['Completed', 'Cancelled'].includes(dispatch.status)).map(dispatch => <Typography key={dispatch.dispatchNumber} variant="body2">{dispatch.type}: {date(dispatch.scheduledAt)} · {dispatch.address} · {dispatch.status}</Typography>)}
    </Stack></DialogContent><DialogActions sx={{ px: 2.5, py: 1.5 }}>{booking.status === 'Confirmed' && <Button onClick={() => void onResend(booking.id)}>Resend confirmation</Button>}<Box sx={{ flex: 1 }} />{booking.branch.phone && <Button size="small" href={'tel:' + booking.branch.phone}>Call branch</Button>}{booking.branch.email && <Button size="small" href={'mailto:' + booking.branch.email}>Email branch</Button>}<Button variant="contained" onClick={onClose}>Close</Button></DialogActions>
  </Dialog>
}

function RentalActivity({ booking, section }: { booking: BookingDetails; section: 'charges' | 'history' }) {
  const charges = booking.pricing.charges ?? []
  const damage = (booking.inspections ?? []).filter(item => item.damageNotes && !/^(none|no damage|n\/a)\.?$/i.test(item.damageNotes.trim()))
  return <Stack spacing={1.5}>
    {section === 'charges' && <Box><Typography fontWeight={800} mb={1}>Charges and payments</Typography>
      {charges.map((charge, index) => <Stack key={index} direction="row" justifyContent="space-between" gap={2}><Typography variant="body2">{charge.description} · {charge.quantity} × {money(charge.unitRate)}</Typography><Typography variant="body2">{money(charge.amount)}</Typography></Stack>)}
      {Boolean(booking.pricing.additionalCharges) && charges.length === 0 && <Typography variant="body2">{booking.pricing.additionalChargesDescription?.trim() || 'Additional charges'}: {money(booking.pricing.additionalCharges!)}</Typography>}
      {!charges.length && !booking.pricing.additionalCharges && <Typography variant="body2" color="text.secondary">No additional charges recorded.</Typography>}
      {booking.invoice && <Box mt={1}><Typography variant="body2" fontWeight={750}>Final invoice breakdown</Typography>{booking.invoice.lines.map((line, index) => <Stack key={index} direction="row" justifyContent="space-between" gap={2}><Typography variant="body2">{line.description}</Typography><Typography variant="body2">{money(line.quantity * line.unitPrice)}</Typography></Stack>)}</Box>}
      {booking.bond.bondDeductionReason && <Typography variant="body2" mt={1}>Bond deduction: {booking.bond.bondDeductionReason}</Typography>}
      {(booking.payments ?? []).map((payment, index) => <Typography variant="body2" mt={.5} key={index}>{date(payment.createdAt)} · {payment.type.replace(/([a-z])([A-Z])/g, '$1 $2')} · {money(payment.amount)} · {payment.status}{payment.receiptNumber ? ` · ${payment.receiptNumber}` : ''}</Typography>)}
    </Box>}
    {section === 'history' && <><Box><Typography fontWeight={800} mb={1}>Rental history</Typography><Typography variant="body2" color="text.secondary">{date(booking.createdAt)} · Request submitted</Typography>{(booking.history ?? []).map((event, index) => <Stack key={index} direction="row" justifyContent="space-between" gap={1} sx={{py:.6,borderBottom:1,borderColor:'divider'}}><Typography variant="body2" fontWeight={650}>{event.title}</Typography><Typography variant="caption" color="text.secondary" sx={{flexShrink:0}}>{date(event.createdAt)}</Typography></Stack>)}</Box>
    <Box><Typography fontWeight={800} mb={1}>Requests and extensions</Typography>
      {(booking.cases ?? []).length ? <Stack spacing={1}>{booking.cases.map(item => <Box key={item.caseNumber} sx={{p:1,border:1,borderColor:'divider',borderRadius:1.5}}>
        <Stack direction="row" justifyContent="space-between" gap={1}><Typography variant="body2" fontWeight={750}>{item.subject}</Typography><Chip size="small" label={item.status}/></Stack>
        <Typography variant="caption" color="text.secondary">{item.caseNumber} · {date(item.createdAt)}</Typography>
        {item.description && <Typography variant="body2" sx={{whiteSpace:'pre-line',overflowWrap:'anywhere'}}>{item.description.split('\nRequestedEndAt:')[0]}</Typography>}
        {item.resolution && <Typography variant="body2" mt={.5}>Branch response: {item.resolution}</Typography>}
      </Box>)}</Stack> : <Typography variant="body2" color="text.secondary">No change or support requests recorded.</Typography>}
    </Box>
    <Box><Typography fontWeight={800} mb={1}>Incidents and damage</Typography>
      {!(booking.incidents ?? []).length && !damage.length && <Typography variant="body2" color="text.secondary">No incidents or damage recorded.</Typography>}
      {(booking.incidents ?? []).map(incident => <Box key={incident.incidentNumber} sx={{p:1,border:1,borderColor:'divider',borderRadius:1.5,mb:1}}><Typography variant="body2" fontWeight={750}>{incident.type} · {incident.status}</Typography><Typography variant="caption" color="text.secondary">{incident.incidentNumber} · {date(incident.occurredAt)}</Typography><Typography variant="body2">{incident.description}</Typography>{incident.location && <Typography variant="body2">Location: {incident.location}</Typography>}</Box>)}
      {damage.map((inspection, index) => <Typography key={index} variant="body2">{date(inspection.completedAt)} · {inspection.type}: {inspection.damageNotes}</Typography>)}
    </Box>
    </>}
  </Stack>
}

function EmptyState({ title, text, action, onAction }: { title: string; text: string; action: string; onAction: () => void }) { return <Card variant="outlined"><CardContent sx={{ py: 7, textAlign: 'center' }}><Typography variant="h6" fontWeight={700}>{title}</Typography><Typography color="text.secondary" mt={1}>{text}</Typography><Button variant="contained" sx={{ mt: 2 }} onClick={onAction}>{action}</Button></CardContent></Card> }
function DocumentGroup({ title, empty, items }: { title: string; empty: string; items: { key: string; title: string; subtitle: string; status: string; action?: () => void }[] }) { return <Card variant="outlined" sx={{ height: '100%' }}><CardContent sx={{ p: 3 }}><Typography variant="h6" fontWeight={750}>{title}</Typography>{items.length === 0 ? <Typography color="text.secondary" mt={2}>{empty}</Typography> : <Stack divider={<Divider flexItem />} mt={1}>{items.map(item => <Stack key={item.key} direction="row" alignItems="center" justifyContent="space-between" gap={2} py={1.5}><Box><Typography fontWeight={650}>{item.title}</Typography><Typography variant="body2" color="text.secondary">{item.subtitle}</Typography></Box><Stack alignItems="flex-end" gap={.5}><Chip size="small" label={item.status} />{item.action && <Button size="small" onClick={item.action}>View</Button>}</Stack></Stack>)}</Stack>}</CardContent></Card> }
