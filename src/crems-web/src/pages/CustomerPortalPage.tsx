import { useEffect, useState, type FormEvent, type ReactNode } from 'react'
import axios from 'axios'
import CheckCircleOutlined from '@mui/icons-material/CheckCircleOutlined'
import DashboardOutlined from '@mui/icons-material/DashboardOutlined'
import DescriptionOutlined from '@mui/icons-material/DescriptionOutlined'
import EventAvailableOutlined from '@mui/icons-material/EventAvailableOutlined'
import ReceiptLongOutlined from '@mui/icons-material/ReceiptLongOutlined'
import SearchOutlined from '@mui/icons-material/SearchOutlined'
import {
  Alert, Box, Button, Card, CardContent, Checkbox, Chip, CircularProgress, Container,
  Dialog, DialogActions, DialogContent, DialogTitle, Divider, FormControl, FormControlLabel, FormGroup, Grid,
  InputAdornment, InputLabel, MenuItem, Paper, Select, Stack, TextField, Typography,
} from '@mui/material'
import { api } from '../api/client'
import { CustomerSiteHeader, type CustomerSiteSection } from '../components/CustomerSiteHeader'

type HirePreference = 'Vehicles' | 'Equipment' | 'WasteAndSiteHire'
type LegacyHirePreference = HirePreference | 'NoPreference'
type CustomerSession = { email: string; fullName: string; customerNumber: string; customerName: string; phone: string | null; address: string | null; identificationNumber: string | null; hirePreferences?: HirePreference[]; hirePreference?: LegacyHirePreference; emailConfirmed: boolean }
type BookingItem = { name: string; startAt: string; endAt: string; dailyRate: number; divisionName: string | null }
type Booking = { id: string; reference: string; status: string; createdAt: string; branchName: string; items: BookingItem[] }
type QuoteLine = { description: string; quantity: number; rate: number; unit: string }
type Quote = { id: string; quoteNumber: string; status: string; validUntil: string; jobSite: string | null; purchaseOrderNumber: string | null; subtotal: number; discount: number; tax: number; total: number; depositRequired: number; version: number; branchName: string; divisionName: string | null; lines: QuoteLine[] | null; convertedBookingId: string | null }
type PortalDocuments = {
  registered: { id: string; documentNumber: string; type: string; fileName: string; expiresOn: string | null; createdAt: string; bookingReference: string | null }[]
  agreements: { id: string; number: string; status: string; createdAt: string; bookingReference: string; bookingId: string }[]
  invoices: { id: string; number: string; status: string; total: number; balanceDue: number; createdAt: string; bookingReference: string; bookingId: string }[]
}
type BookingDetails = {
  id: string; reference: string; status: string; createdAt: string
  branch: { name: string; address: string | null; phone: string | null; email: string | null; pickupInstructions: string | null; operatingHours: { dayOfWeek: string; opensAt: string; closesAt: string; isClosed: boolean; pickupCutoff: string | null }[] }
  items: (BookingItem & { assetId: string; type: string; category: string | null; personnelRequirement: string })[]
  pricing: { baseSubtotal: number; discountAmount: number; charges: { description: string; quantity: number; unitRate: number; amount: number }[]; chargeTotal: number; taxRate: number; taxAmount: number; total: number; depositRequired: number }
  bond: { required: number; held: number; deduction: number; bondDeductionReason: string | null; refund: number; status: string; bondSettledAt: string | null }
  agreement: { agreementNumber: string; status: string; termsVersion: string; terms: { title: string; content: string }[] | null; customerSignatureName: string; customerSignedAt: string; approvedByName: string; approvedAt: string } | null
  invoice: { invoiceNumber: string; status: string; issuedAt: string; subtotal: number; taxAmount: number; total: number; amountPaid: number; balanceDue: number; lines: { description: string; quantity: number; unitPrice: number }[] } | null
  payments: { receiptNumber: string; type: string; method: string; amount: number; status: string; createdAt: string }[]
  inspections: { type: string; meterReading: number | null; fuelLevelPercent: number | null; conditionNotes: string | null; damageNotes: string | null; completedAt: string }[]
  incidents: { incidentNumber: string; type: string; status: string; occurredAt: string; description: string; location: string | null }[]
  dispatches: { dispatchNumber: string; type: string; status: string; scheduledAt: string; address: string | null; assignedDriver: string | null }[]
  documents: { id: string; documentNumber: string; type: string; fileName: string; expiresOn: string | null; createdAt: string }[]
  cases: { caseNumber: string; type: string; status: string; subject: string; createdAt: string }[]
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
const emptyRegistration = { fullName: '', email: '', phone: '', address: '', identificationNumber: '', hirePreferences: [] as HirePreference[], password: '', confirmPassword: '' }
type PortalSection = 'overview' | 'bookings' | 'quotes' | 'documents' | 'receipts' | 'account'
type AuthMode = 'login' | 'register' | 'activate' | 'reset'
const bookingStages = ['Request received', 'Confirmed', 'On hire', 'Returned']
const money = (value: number) => `FJD ${value.toLocaleString('en-FJ', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`
const date = (value: string) => new Date(value).toLocaleDateString('en-FJ', { day: '2-digit', month: 'short', year: 'numeric' })
function bookingStage(status: string) { return status === 'Completed' ? 3 : status === 'ConvertedToRental' ? 2 : status === 'Confirmed' ? 1 : 0 }
function statusLabel(status: string) { return ({ Draft: 'Awaiting review', Confirmed: 'Confirmed', ConvertedToRental: 'Currently on hire', Completed: 'Completed', Cancelled: 'Cancelled', Sent: 'Ready for your decision', Negotiating: 'Revised quotation', Accepted: 'Accepted', Rejected: 'Declined', Converted: 'Booking created', Expired: 'Expired' } as Record<string, string>)[status] ?? status }
function statusColor(status: string): 'default' | 'info' | 'success' | 'warning' | 'error' { return ['Completed', 'Accepted', 'Converted'].includes(status) ? 'success' : ['ConvertedToRental', 'Negotiating'].includes(status) ? 'warning' : ['Cancelled', 'Rejected', 'Expired'].includes(status) ? 'error' : ['Confirmed', 'Sent'].includes(status) ? 'info' : 'default' }
function apiMessage(reason: unknown, fallback: string) { const data = axios.isAxiosError(reason) ? reason.response?.data : undefined; const errors = data?.errors as Record<string, string[]> | undefined; return errors ? Object.values(errors).flat().join(' ') : data?.message ?? fallback }

function PreferenceCheckboxes({ values, onChange }: { values: HirePreference[]; onChange: (values: HirePreference[]) => void }) {
  return <FormControl component="fieldset" variant="standard"><Typography component="legend" fontWeight={700} mb={.5}>Preferred rentals</Typography><FormGroup>{preferenceOptions.map(option => <FormControlLabel key={option.value} control={<Checkbox checked={values.includes(option.value)} onChange={event => onChange(event.target.checked ? [...values, option.value] : values.filter(value => value !== option.value))} />} label={option.label} />)}</FormGroup></FormControl>
}

const accountSections: { value: PortalSection; label: string; icon: ReactNode }[] = [
  { value: 'overview', label: 'My account', icon: <DashboardOutlined /> },
  { value: 'bookings', label: 'Manage bookings', icon: <EventAvailableOutlined /> },
]

export function CustomerPortalPage({ onBack, onSessionChange }: { onBack: () => void; onSessionChange?: (signedIn: boolean) => void }) {
  const [session, setSession] = useState<CustomerSession | null>(null)
  const [bookings, setBookings] = useState<Booking[]>([])
  const [quotes, setQuotes] = useState<Quote[]>([])
  const [documents, setDocuments] = useState<PortalDocuments>({ registered: [], agreements: [], invoices: [] })
  const [section, setSection] = useState<PortalSection>(() => sessionStorage.getItem('crems.customerSection') === 'bookings' ? 'bookings' : 'overview')
  const [authMode, setAuthMode] = useState<AuthMode>('login')
  const [checking, setChecking] = useState(true)
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [login, setLogin] = useState({ email: '', password: '' })
  const [registration, setRegistration] = useState(emptyRegistration)
  const [activation, setActivation] = useState({ email: '', code: '', password: '', confirmPassword: '' })
  const [reset, setReset] = useState({ email: '', code: '', password: '', confirmPassword: '', codeSent: false })
  const [verificationCode, setVerificationCode] = useState('')
  const [profile, setProfile] = useState({ fullName: '', phone: '', address: '', identificationNumber: '', hirePreferences: [] as HirePreference[] })
  const [selectedBooking, setSelectedBooking] = useState<BookingDetails | null>(null)
  const [loadingBooking, setLoadingBooking] = useState(false)
  const [changeBooking, setChangeBooking] = useState<Booking | null>(null)
  const [changeType, setChangeType] = useState<'dates' | 'cancel' | 'extend' | 'incident'>('cancel')
  const [requestedStart, setRequestedStart] = useState('')
  const [changeReason, setChangeReason] = useState('')
  const [requestedEnd, setRequestedEnd] = useState('')
  const [incident, setIncident] = useState({ type: 'Breakdown', location: '', policeReference: '' })
  const [declineQuote, setDeclineQuote] = useState<Quote | null>(null)
  const [declineReason, setDeclineReason] = useState('')
  const [referenceSearch, setReferenceSearch] = useState(() => sessionStorage.getItem('crems.customerReferenceSearch') ?? '')

  async function loadPortalData() {
    const [bookingResponse, quoteResponse, documentResponse] = await Promise.all([
      api.get<Booking[]>('/customer-account/bookings'), api.get<Quote[]>('/customer-account/quotes'), api.get<PortalDocuments>('/customer-account/documents'),
    ])
    setBookings(bookingResponse.data); setQuotes(quoteResponse.data); setDocuments(documentResponse.data)
  }
  async function loadSession() {
    try {
      const current = (await api.get<CustomerSession>('/customer-account/session')).data
      setSession(current); sessionStorage.setItem('crems.customerName', current.fullName); sessionStorage.removeItem('crems.customerSection'); setProfile({ fullName: current.fullName, phone: current.phone ?? '', address: current.address ?? '', identificationNumber: current.identificationNumber ?? '', hirePreferences: sessionPreferences(current) }); onSessionChange?.(true)
      await loadPortalData()
      return true
    } catch { setSession(null); onSessionChange?.(false); return false }
    finally { setChecking(false) }
  }
  // Hydrate the browser-window customer session once when the portal opens.
  // eslint-disable-next-line react-hooks/set-state-in-effect, react-hooks/exhaustive-deps
  useEffect(() => { void loadSession() }, [])

  useEffect(() => {
    if (!session) return

    const expirePortalSession = () => {
      setSession(null)
      setBookings([])
      setQuotes([])
      onSessionChange?.(false)
    }
    const timer = window.setTimeout(() => {
      void api.post('/customer-account/logout').catch(() => undefined).finally(expirePortalSession)
    }, 30 * 60 * 1000)
    window.addEventListener('crems:session-expired', expirePortalSession)

    return () => {
      window.clearTimeout(timer)
      window.removeEventListener('crems:session-expired', expirePortalSession)
    }
  }, [session, onSessionChange])

  async function finishAuthentication() { const signedIn = await loadSession(); if (signedIn) onBack() }
  async function signIn(event: FormEvent) { event.preventDefault(); setSubmitting(true); setError(''); try { await api.post('/auth/login?useCookies=true', login); await finishAuthentication() } catch { setError('The email address or password is incorrect, or this is not a customer account.') } finally { setSubmitting(false) } }
  async function register(event: FormEvent) { event.preventDefault(); setError(''); if (registration.password !== registration.confirmPassword) return setError('The passwords do not match.'); setSubmitting(true); try { const { confirmPassword: _, ...request } = registration; void _; await api.post('/customer-account/register', request); await finishAuthentication() } catch (reason) { setError(apiMessage(reason, 'Unable to create your account.')) } finally { setSubmitting(false) } }
  async function activate(event: FormEvent) { event.preventDefault(); setError(''); if (activation.password !== activation.confirmPassword) return setError('The passwords do not match.'); setSubmitting(true); try { await api.post('/customer-account/activate', { email: activation.email, code: activation.code, password: activation.password }); await finishAuthentication() } catch (reason) { setError(apiMessage(reason, 'Unable to activate the account.')) } finally { setSubmitting(false) } }
  async function requestReset() { setSubmitting(true); setError(''); try { const response = await api.post<{ message: string }>('/auth/password-reset/request', { email: reset.email }); setReset({ ...reset, codeSent: true }); setNotice(response.data.message) } catch { setError('Unable to request a password reset right now.') } finally { setSubmitting(false) } }
  async function completeReset(event: FormEvent) { event.preventDefault(); setError(''); if (reset.password !== reset.confirmPassword) return setError('The passwords do not match.'); setSubmitting(true); try { await api.post('/auth/password-reset/complete', { email: reset.email, code: reset.code, newPassword: reset.password }); setNotice('Password changed. You can now sign in.'); setLogin({ email: reset.email, password: '' }); setAuthMode('login') } catch (reason) { setError(apiMessage(reason, 'Unable to reset the password.')) } finally { setSubmitting(false) } }
  async function logout() { await api.post('/customer-account/logout'); sessionStorage.removeItem('crems.customerName'); setSession(null); setBookings([]); setQuotes([]); onSessionChange?.(false) }
  async function requestVerification() { setSubmitting(true); setError(''); try { const response = await api.post<{ message: string }>('/customer-account/verification/request'); setNotice(response.data.message) } catch { setError('Unable to send a verification code right now.') } finally { setSubmitting(false) } }
  async function confirmVerification(event: FormEvent) { event.preventDefault(); setSubmitting(true); setError(''); try { await api.post('/customer-account/verification/confirm', { code: verificationCode }); setNotice('Email verified successfully.'); setVerificationCode(''); await loadSession() } catch (reason) { setError(apiMessage(reason, 'The verification code is invalid or expired.')) } finally { setSubmitting(false) } }
  async function updateProfile() {
    const response = await api.put<{ hirePreferences?: HirePreference[] }>('/customer-account/profile', profile)
    if (!Array.isArray(response.data.hirePreferences)) throw new Error('The customer API must be restarted to enable multiple rental preferences.')
  }
  function profileError(reason: unknown, fallback: string) { return reason instanceof Error && reason.message.startsWith('The customer API') ? reason.message : apiMessage(reason, fallback) }
  async function saveProfile(event: FormEvent) { event.preventDefault(); setSubmitting(true); setError(''); try { await updateProfile(); setNotice('Your contact details have been updated.'); await loadSession() } catch (reason) { setError(profileError(reason, 'Unable to update your profile.')) } finally { setSubmitting(false) } }
  async function savePreference() { setSubmitting(true); setError(''); try { await updateProfile(); setNotice('Your rental preferences have been updated.'); await loadSession() } catch (reason) { setError(profileError(reason, 'Unable to update your preference.')) } finally { setSubmitting(false) } }
  async function openBooking(id: string) { setLoadingBooking(true); setError(''); try { setSelectedBooking((await api.get<BookingDetails>(`/customer-account/bookings/${id}`)).data) } catch { setError('Unable to load this booking.') } finally { setLoadingBooking(false) } }
  function openChange(booking: Booking, type: 'dates' | 'cancel' | 'extend' | 'incident') { setChangeBooking(booking); setChangeType(type); setChangeReason(''); setRequestedStart(booking.items[0]?.startAt.slice(0, 10) ?? ''); setRequestedEnd(booking.items[0]?.endAt.slice(0, 10) ?? ''); setIncident({ type: 'Breakdown', location: '', policeReference: '' }); setError('') }
  async function submitChange() { if (!changeBooking) return; setSubmitting(true); setError(''); try { if (changeType === 'dates') await api.put(`/customer-account/bookings/${changeBooking.id}/dates`, { startAt: `${requestedStart}T09:00:00+12:00`, endAt: `${requestedEnd}T09:00:00+12:00` }); else if (changeType === 'cancel') await api.post(`/customer-account/bookings/${changeBooking.id}/cancellation-request`, { reason: changeReason }); else if (changeType === 'extend') await api.post(`/customer-account/bookings/${changeBooking.id}/extension-request`, { requestedEndAt: `${requestedEnd}T09:00:00+12:00`, reason: changeReason }); else await api.post(`/customer-account/bookings/${changeBooking.id}/incident`, { type: incident.type, occurredAt: new Date().toISOString(), description: changeReason, location: incident.location || null, policeReference: incident.policeReference || null }); setChangeBooking(null); setNotice(changeType === 'dates' ? 'Your requested dates have been updated.' : 'Your request has been sent to the operating branch.'); await loadPortalData() } catch (reason) { setError(apiMessage(reason, 'Your request could not be submitted.')) } finally { setSubmitting(false) } }
  async function resendConfirmation(bookingId: string) { setSubmitting(true); try { await api.post(`/customer-account/bookings/${bookingId}/resend-confirmation`); setNotice('A fresh confirmation email has been queued.') } catch (reason) { setError(apiMessage(reason, 'Unable to resend the confirmation.')) } finally { setSubmitting(false) } }
  async function uploadCustomerDocument(file?: File) { if (!file) return; setSubmitting(true); setError(''); try { const form = new FormData(); form.append('file', file); form.append('type', 'DriverLicence'); await api.post('/customer-account/documents/upload', form); setNotice('Driver licence uploaded securely.'); await loadPortalData() } catch (reason) { setError(apiMessage(reason, 'The driver licence could not be uploaded.')) } finally { setSubmitting(false) } }
  async function viewCustomerDocument(documentId: string) { setError(''); try { const response = await api.get(`/customer-account/documents/${documentId}/download`, { responseType: 'blob' }); const url = URL.createObjectURL(response.data); window.open(url, '_blank', 'noopener,noreferrer'); window.setTimeout(() => URL.revokeObjectURL(url), 60_000) } catch (reason) { setError(apiMessage(reason, 'The document could not be opened.')) } }
  async function decideQuote(quote: Quote, accepted: boolean, note?: string) { setSubmitting(true); setError(''); try { await api.post(`/customer-account/quotes/${quote.id}/decision`, { accepted, note }); setDeclineQuote(null); setNotice(accepted ? 'Quotation accepted. The branch will complete the booking confirmation.' : 'Quotation declined. The branch has received your reason.'); await loadPortalData() } catch (reason) { setError(apiMessage(reason, 'Your quotation decision could not be saved.')) } finally { setSubmitting(false) } }


  function returnToWebsite(section: CustomerSiteSection = 'services') {
    sessionStorage.setItem('crems.publicTarget', section)
    onBack()
  }

  if (checking) return <Box sx={{ minHeight: '100vh', display: 'grid', placeItems: 'center' }}><CircularProgress /></Box>
  return <Box sx={{ minHeight: '100vh', bgcolor: '#f7f7f3' }}>
    <CustomerSiteHeader accountActive authenticated={Boolean(session)} accountName={session?.fullName} onAccount={() => undefined} onAccountSection={setSection} onNavigate={returnToWebsite} onSignOut={logout} />
    {!session ? <Container maxWidth="sm" sx={{ py: 6 }}><Card variant="outlined"><CardContent sx={{ p: { xs: 3, sm: 4 } }}>
      <Typography variant="h4" fontWeight={800}>{authMode === 'register' ? 'Create customer account' : authMode === 'activate' ? 'Activate existing account' : authMode === 'reset' ? 'Reset your password' : 'Customer sign in'}</Typography>
      <Typography color="text.secondary" mt={1} mb={3}>{authMode === 'login' ? 'Manage requests, quotations, documents and active rentals securely.' : authMode === 'register' ? 'Create one account for participating Carpenters rental services.' : authMode === 'activate' ? 'Use the code provided by Carpenters staff and choose your password.' : 'We will send a six-digit reset code to your account email.'}</Typography>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}{notice && <Alert severity="success" sx={{ mb: 2 }}>{notice}</Alert>}
      {authMode === 'login' && <Box component="form" onSubmit={signIn}><Stack spacing={2}><TextField required type="email" label="Email" autoComplete="email" value={login.email} onChange={e => setLogin({ ...login, email: e.target.value })} /><TextField required type="password" label="Password" autoComplete="current-password" value={login.password} onChange={e => setLogin({ ...login, password: e.target.value })} /><Button type="submit" size="large" variant="contained" disabled={submitting}>Sign in</Button><Button onClick={() => { setAuthMode('reset'); setError(''); setNotice('') }}>Forgot password?</Button><Divider>New customer?</Divider><Button variant="outlined" onClick={() => { setRegistration({ ...emptyRegistration }); setAuthMode('register'); setError('') }}>Create customer account</Button><Button onClick={() => { setAuthMode('activate'); setError('') }}>I received an activation code</Button></Stack></Box>}
      {authMode === 'register' && <Box component="form" onSubmit={register} autoComplete="off"><Stack spacing={2}>
        <TextField required label="Your full name" autoComplete="name" value={registration.fullName} onChange={e => setRegistration({ ...registration, fullName: e.target.value })} />
        <TextField required type="email" label="Email" autoComplete="email" value={registration.email} onChange={e => setRegistration({ ...registration, email: e.target.value })} />
        <TextField required label="Phone" autoComplete="tel" value={registration.phone} onChange={e => setRegistration({ ...registration, phone: e.target.value })} />
        <TextField label="Address" autoComplete="street-address" value={registration.address} onChange={e => setRegistration({ ...registration, address: e.target.value })} />
        <TextField label="Driver licence / identification number" autoComplete="off" value={registration.identificationNumber} onChange={e => setRegistration({ ...registration, identificationNumber: e.target.value })} />
        <PreferenceCheckboxes values={registration.hirePreferences} onChange={hirePreferences => setRegistration({ ...registration, hirePreferences })} />
        <Typography variant="caption" color="text.secondary">This personalises your starting view. You can still browse every Carpenters rental service.</Typography>
        <TextField required type="password" label="Password" autoComplete="new-password" helperText="At least 10 characters with an uppercase letter and number" value={registration.password} onChange={e => setRegistration({ ...registration, password: e.target.value })} />
        <TextField required type="password" label="Confirm password" autoComplete="new-password" value={registration.confirmPassword} onChange={e => setRegistration({ ...registration, confirmPassword: e.target.value })} />
        <Button type="submit" size="large" variant="contained" disabled={submitting}>Create account</Button><Button onClick={() => setAuthMode('login')}>Back to sign in</Button>
      </Stack></Box>}
      {authMode === 'activate' && <Box component="form" onSubmit={activate}><Stack spacing={2}><TextField required type="email" label="Invited email address" value={activation.email} onChange={e => setActivation({ ...activation, email: e.target.value })} /><TextField required label="Six-digit activation code" value={activation.code} onChange={e => setActivation({ ...activation, code: e.target.value.replace(/\D/g, '').slice(0, 6) })} /><TextField required type="password" label="Create password" value={activation.password} onChange={e => setActivation({ ...activation, password: e.target.value })} /><TextField required type="password" label="Confirm password" value={activation.confirmPassword} onChange={e => setActivation({ ...activation, confirmPassword: e.target.value })} /><Button type="submit" variant="contained" size="large" disabled={submitting || activation.code.length !== 6}>Activate account</Button><Button onClick={() => setAuthMode('login')}>Back to sign in</Button></Stack></Box>}
      {authMode === 'reset' && <Box component="form" onSubmit={completeReset}><Stack spacing={2}><TextField required type="email" label="Account email" value={reset.email} onChange={e => setReset({ ...reset, email: e.target.value })} disabled={reset.codeSent} />{!reset.codeSent ? <Button variant="contained" size="large" disabled={submitting || !reset.email} onClick={() => void requestReset()}>Send reset code</Button> : <><TextField required label="Six-digit reset code" value={reset.code} onChange={e => setReset({ ...reset, code: e.target.value.replace(/\D/g, '').slice(0, 6) })} /><TextField required type="password" label="New password" value={reset.password} onChange={e => setReset({ ...reset, password: e.target.value })} /><TextField required type="password" label="Confirm password" value={reset.confirmPassword} onChange={e => setReset({ ...reset, confirmPassword: e.target.value })} /><Button type="submit" variant="contained" size="large" disabled={submitting || reset.code.length !== 6}>Change password</Button></>}<Button onClick={() => setAuthMode('login')}>Back to sign in</Button></Stack></Box>}
    </CardContent></Card></Container> : <Box sx={{ width: '100%', minHeight: 'calc(100vh - 76px)' }}>
      <Paper square elevation={0} sx={{ minHeight: 'calc(100vh - 76px)', border: 0 }}>
        <Box sx={{ p: { xs: 2.5, sm: 3.5, lg: 4.5 }, minWidth: 0, maxWidth: 1600, mx: 'auto' }}>
      <Stack direction={{ xs: 'column', sm: 'row' }} alignItems={{ sm: 'center' }} justifyContent="space-between" gap={2} mb={4}><Box><Typography variant="overline" color="text.secondary">Customer portal</Typography><Typography variant="h4" fontWeight={800}>{accountSections.find(item => item.value === section)?.label}</Typography><Typography color="text.secondary">{session.customerName} · {session.customerNumber}</Typography></Box></Stack>
      {error && <Alert severity="error" sx={{ mb: 3 }} onClose={() => setError('')}>{error}</Alert>}{notice && <Alert severity="success" sx={{ mb: 3 }} onClose={() => setNotice('')}>{notice}</Alert>}
      {!session.emailConfirmed && <Card variant="outlined" sx={{ mb: 3, borderColor: 'warning.main' }}><CardContent><Typography variant="h6" fontWeight={700}>Verify your email address</Typography><Typography color="text.secondary">Enter the six-digit code sent to {session.email}. Codes expire after 10 minutes.</Typography><Box component="form" onSubmit={confirmVerification} sx={{ mt: 2 }}><Stack direction={{ xs: 'column', sm: 'row' }} gap={1.5}><TextField required label="Six-digit code" value={verificationCode} onChange={e => setVerificationCode(e.target.value.replace(/\D/g, '').slice(0, 6))} /><Button type="submit" variant="contained" disabled={submitting || verificationCode.length !== 6}>Verify</Button><Button type="button" disabled={submitting} onClick={() => void requestVerification()}>Send new code</Button></Stack></Box></CardContent></Card>}

      {section === 'bookings' && <Stack spacing={4}><TextField fullWidth label="Search booking or quotation reference" placeholder="BK-2026-0001 or QUO-20260903-XXXXXX" value={referenceSearch} onChange={event=>{setReferenceSearch(event.target.value);sessionStorage.setItem('crems.customerReferenceSearch',event.target.value)}} InputProps={{startAdornment:<InputAdornment position="start"><SearchOutlined/></InputAdornment>}}/><Box><Typography color="text.secondary" mb={3}>View every current and previous rental. Open a booking for its agreement, invoice, receipts, inspections and support history.</Typography><BookingCards bookings={bookings.filter(item=>item.reference.toLowerCase().includes(referenceSearch.trim().toLowerCase()))} onOpen={openBooking} onChange={openChange} loading={loadingBooking} onBrowse={onBack} /></Box><Box><Typography variant="h5" fontWeight={750}>Quotations</Typography><Typography color="text.secondary" mb={2}>Review and respond to quotations linked to your rental requests.</Typography><CustomerQuoteCards quotes={quotes.filter(item=>item.quoteNumber.toLowerCase().includes(referenceSearch.trim().toLowerCase()))} submitting={submitting} onAccept={quote=>void decideQuote(quote,true)} onDecline={quote=>{setDeclineQuote(quote);setDeclineReason('')}} /></Box></Stack>}

      {section === 'quotes' && <Box><Typography color="text.secondary" mb={3}>Review prices, validity dates and customer charges before accepting or declining an offer.</Typography><CustomerQuoteCards quotes={quotes} submitting={submitting} onAccept={quote=>void decideQuote(quote,true)} onDecline={quote=>{setDeclineQuote(quote);setDeclineReason('')}} /></Box>}

      {section === 'documents' && <Box><Typography color="text.secondary" mb={3}>Find your rental agreements and securely stored customer documents in one place.</Typography><Grid container spacing={3}><Grid size={{ xs: 12, md: 6 }}><DocumentGroup title="Rental agreements" empty="Signed agreements will appear after pickup." items={documents.agreements.map(item => ({ key: item.id, title: item.number, subtitle: `${item.bookingReference} · ${date(item.createdAt)}`, status: item.status, action: () => void openBooking(item.bookingId) }))} /></Grid><Grid size={{ xs: 12, md: 6 }}><DocumentGroup title="Driver licence" empty="No driver licence has been uploaded." items={documents.registered.filter(item => item.type === 'DriverLicence').map(item => ({ key: item.id, title: item.fileName, subtitle: item.bookingReference ? `Linked to ${item.bookingReference}` : 'Securely stored on your account', status: item.expiresOn ? `Expires ${date(item.expiresOn)}` : 'On file', action: () => void viewCustomerDocument(item.id) }))} /></Grid></Grid></Box>}
      {section === 'receipts' && <Box><Typography color="text.secondary" mb={3}>View invoices, payment status and rental receipts.</Typography><DocumentGroup title="Invoices & receipts" empty="Final invoices will appear after billing." items={documents.invoices.map(item => ({ key: item.id, title: item.number, subtitle: `${item.bookingReference} · ${money(item.total)}`, status: item.balanceDue > 0 ? `${money(item.balanceDue)} due` : 'Paid', action: () => void openBooking(item.bookingId) }))} /></Box>}

      {section === 'overview' && <Grid container spacing={3} alignItems="flex-start">
        <Grid size={{ xs: 12, lg: 7 }}><Card variant="outlined" sx={{ borderRadius: 3 }}><CardContent sx={{ p: { xs: 2.5, sm: 3.5 } }}><Typography variant="h6" fontWeight={750}>Contact and identification details</Typography><Typography color="text.secondary" mb={3}>Keep these details current so the branch can contact and identify you at pickup.</Typography><Box component="form" onSubmit={saveProfile}><Stack spacing={2.25}><TextField required label="Full name" value={profile.fullName} onChange={e => setProfile({ ...profile, fullName: e.target.value })} /><TextField label="Email" value={session.email} disabled helperText="Contact Carpenters to change the account email." /><TextField required label="Phone" value={profile.phone} onChange={e => setProfile({ ...profile, phone: e.target.value })} /><TextField label="Driver licence / identification number" value={profile.identificationNumber} onChange={e => setProfile({ ...profile, identificationNumber: e.target.value })} /><TextField multiline minRows={3} label="Address" value={profile.address} onChange={e => setProfile({ ...profile, address: e.target.value })} /><Box><Button type="submit" variant="contained" disabled={submitting}>Save details</Button></Box></Stack></Box></CardContent></Card></Grid>
        <Grid size={{ xs: 12, lg: 5 }}><Stack spacing={3}>
          <Card variant="outlined" sx={{ borderRadius: 3 }}><CardContent sx={{ p: 3 }}><Typography variant="h6" fontWeight={750}>Account security</Typography><Typography color="text.secondary" mt={1}>This account is protected by a browser-window session. Sign out on shared devices.</Typography><Stack direction="row" flexWrap="wrap" gap={1} mt={2.5}><Chip label={session.emailConfirmed ? 'Email verified' : 'Verification required'} color={session.emailConfirmed ? 'success' : 'warning'} /><Chip label={documents.registered.some(item => item.type === 'DriverLicence') ? 'Licence on file' : 'Licence required'} /></Stack></CardContent></Card>
          <Card variant="outlined" sx={{ borderRadius: 3 }}><CardContent sx={{ p: 3 }}><Typography variant="h6" fontWeight={750}>Rental preferences</Typography><Typography color="text.secondary" mt={.5} mb={2.5}>Choose everything you usually hire. Leave all unchecked to show everything.</Typography><Stack spacing={2}><PreferenceCheckboxes values={profile.hirePreferences} onChange={hirePreferences => setProfile({ ...profile, hirePreferences })} /><Box><Button variant="contained" disabled={submitting} onClick={() => void savePreference()}>Save preferences</Button></Box></Stack></CardContent></Card>
        </Stack></Grid>
      </Grid>}
      {section === 'overview' && <Card variant="outlined" sx={{ mt: 3, borderRadius: 3 }}><CardContent sx={{ p: 3 }}><Typography variant="h6" fontWeight={750}>Driver licence document</Typography><Typography color="text.secondary" mt={.5}>Upload a clear PDF, JPEG or PNG copy up to 5 MB. This is only required when you or another nominated driver will drive a rental vehicle.</Typography><Stack direction={{ xs: 'column', sm: 'row' }} gap={1} mt={2}>{documents.registered.filter(item => item.type === 'DriverLicence').map(item => <Button key={item.id} variant="outlined" onClick={() => void viewCustomerDocument(item.id)}>View {item.fileName}</Button>)}<Button component="label" variant="contained" disabled={submitting}>{submitting ? 'Uploading…' : documents.registered.some(item => item.type === 'DriverLicence') ? 'Replace driver licence' : 'Upload driver licence'}<input hidden type="file" accept="application/pdf,image/jpeg,image/png" onChange={event => { const file = event.target.files?.[0]; event.target.value = ''; void uploadCustomerDocument(file) }} /></Button></Stack></CardContent></Card>}
        </Box>
      </Paper>
      </Box>}

    <BookingDetailDialog booking={selectedBooking} onClose={() => setSelectedBooking(null)} onResend={resendConfirmation} />
    <Dialog open={Boolean(changeBooking)} onClose={() => !submitting && setChangeBooking(null)} fullWidth maxWidth="sm"><DialogTitle>{changeType === 'dates' ? 'Change requested dates' : changeType === 'cancel' ? 'Request cancellation' : changeType === 'extend' ? 'Request an extension' : 'Report an incident'}</DialogTitle><DialogContent><Stack spacing={2} mt={1}><Alert severity={changeType === 'incident' ? 'warning' : 'info'}>{changeBooking?.reference} · {changeBooking?.items[0]?.name}</Alert>{changeType === 'dates' && <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}><TextField fullWidth required type="date" label="Pickup date" InputLabelProps={{ shrink: true }} value={requestedStart} onChange={e => setRequestedStart(e.target.value)} /><TextField fullWidth required type="date" label="Return date" InputLabelProps={{ shrink: true }} value={requestedEnd} onChange={e => setRequestedEnd(e.target.value)} inputProps={{ min: requestedStart }} /></Stack>}{changeType === 'extend' && <TextField required type="date" label="Requested return date" InputLabelProps={{ shrink: true }} value={requestedEnd} onChange={e => setRequestedEnd(e.target.value)} inputProps={{ min: changeBooking?.items[0]?.endAt.slice(0, 10) }} />}{changeType === 'incident' && <><FormControl><InputLabel>Incident type</InputLabel><Select label="Incident type" value={incident.type} onChange={e => setIncident({ ...incident, type: e.target.value })}>{['Breakdown', 'Accident', 'Damage', 'Theft', 'TrafficOffence', 'Other'].map(item => <MenuItem key={item} value={item}>{item.replace('TrafficOffence', 'Traffic offence')}</MenuItem>)}</Select></FormControl><TextField label="Current location" value={incident.location} onChange={e => setIncident({ ...incident, location: e.target.value })} /><TextField label="Police reference (if applicable)" value={incident.policeReference} onChange={e => setIncident({ ...incident, policeReference: e.target.value })} /></>}{changeType !== 'dates' && <TextField required multiline minRows={4} label={changeType === 'incident' ? 'What happened? Is everyone safe?' : 'Reason or additional information'} value={changeReason} onChange={e => setChangeReason(e.target.value)} />}<Typography variant="body2" color="text.secondary">Date changes are rechecked for availability. Confirmed-rental changes require branch approval.</Typography></Stack></DialogContent><DialogActions sx={{ p: 3 }}><Button onClick={() => setChangeBooking(null)}>Close</Button><Button variant="contained" color={changeType === 'incident' ? 'warning' : 'primary'} disabled={submitting || (changeType !== 'dates' && !changeReason.trim()) || (changeType === 'dates' && (!requestedStart || !requestedEnd)) || (changeType === 'extend' && !requestedEnd)} onClick={() => void submitChange()}>{submitting ? 'Sending…' : changeType === 'dates' ? 'Save dates' : 'Submit request'}</Button></DialogActions></Dialog>
    <Dialog open={Boolean(declineQuote)} onClose={() => !submitting && setDeclineQuote(null)} fullWidth maxWidth="sm"><DialogTitle>Decline quotation {declineQuote?.quoteNumber}</DialogTitle><DialogContent><TextField autoFocus fullWidth multiline minRows={4} label="Why are you declining?" value={declineReason} onChange={e => setDeclineReason(e.target.value)} sx={{ mt: 1 }} /><Typography variant="body2" color="text.secondary" mt={2}>Your reason helps the branch offer a more suitable option or revised quotation.</Typography></DialogContent><DialogActions sx={{ p: 3 }}><Button onClick={() => setDeclineQuote(null)}>Keep quotation</Button><Button variant="contained" color="error" disabled={submitting || !declineReason.trim()} onClick={() => declineQuote && void decideQuote(declineQuote, false, declineReason)}>Decline quotation</Button></DialogActions></Dialog>
  </Box>
}

async function downloadQuotePdf(quote:Quote){
    const {jsPDF}=await import('jspdf'); const pdf=new jsPDF(); let y=18
    pdf.setFontSize(18);pdf.text('Carpenters Fiji — Rental Quotation',18,y);y+=10
    pdf.setFontSize(11);pdf.text(`Quotation: ${quote.quoteNumber}  Version ${quote.version}`,18,y);y+=7
    pdf.text(`${quote.divisionName??'Carpenters Fiji'} — ${quote.branchName}`,18,y);y+=7
    pdf.text(`Valid until: ${date(quote.validUntil)}`,18,y);y+=12
    pdf.setFontSize(12);pdf.text('Customer charges',18,y);y+=8;pdf.setFontSize(10)
    ;(quote.lines??[]).forEach(line=>{pdf.text(`${line.description}  ${line.quantity} x ${money(line.rate)}`,18,y);pdf.text(money(line.quantity*line.rate),190,y,{align:'right'});y+=7})
    y+=3;pdf.line(18,y,190,y);y+=7
    pdf.text(`Subtotal: ${money(quote.subtotal)}`,190,y,{align:'right'});y+=7
    pdf.text(`Discount: -${money(quote.discount)}`,190,y,{align:'right'});y+=7
    pdf.text(`VAT: ${money(quote.tax)}`,190,y,{align:'right'});y+=7
    pdf.setFontSize(13);pdf.text(`Total: ${money(quote.total)}`,190,y,{align:'right'});y+=9
    pdf.setFontSize(10);pdf.text(`Refundable bond required: ${money(quote.depositRequired??0)}`,18,y);y+=7
    pdf.text(`Job site: ${quote.jobSite??'Not specified'}`,18,y);y+=7
    pdf.text(`Purchase order: ${quote.purchaseOrderNumber??'Not supplied'}`,18,y)
    pdf.save(`${quote.quoteNumber}.pdf`)
}

function QuotePrintButton({quote}:{quote:Quote}){return <Button size="small" variant="outlined" startIcon={<DescriptionOutlined/>} onClick={()=>void downloadQuotePdf(quote)}>Print / PDF</Button>}

function CustomerQuoteCards({quotes,submitting,onAccept,onDecline}:{quotes:Quote[];submitting:boolean;onAccept:(quote:Quote)=>void;onDecline:(quote:Quote)=>void}){
  if(quotes.length===0)return <Alert severity="info">No quotations are linked to this account.</Alert>
  return <Stack spacing={2}>{quotes.map(quote=><Card key={quote.id} variant="outlined"><CardContent sx={{p:3}}><Stack direction={{xs:'column',sm:'row'}} justifyContent="space-between" gap={2}><Box><Typography variant="overline">{quote.quoteNumber} · Version {quote.version}</Typography><Typography variant="h6" fontWeight={750}>{quote.divisionName??'Carpenters Fiji'} · {quote.branchName}</Typography><Typography color="text.secondary">Valid until {date(quote.validUntil)}</Typography></Box><Stack alignItems={{sm:'flex-end'}} gap={1}><Chip label={statusLabel(quote.status)} color={statusColor(quote.status)}/><Typography variant="h5" fontWeight={800}>{money(quote.total)}</Typography><QuotePrintButton quote={quote}/></Stack></Stack>{quote.lines&&quote.lines.length>0&&<Box sx={{bgcolor:'#f5f5f1',borderRadius:2,p:2,mt:2}}>{quote.lines.map((line,index)=><Stack key={`${line.description}-${index}`} direction="row" justifyContent="space-between" gap={2} py={.5}><Typography>{line.description} <Typography component="span" variant="caption" color="text.secondary">× {line.quantity} {String(line.unit??'unit').toLowerCase()}</Typography></Typography><Typography fontWeight={650}>{money(line.quantity*line.rate)}</Typography></Stack>)}<Divider sx={{my:1}}/><Stack direction="row" justifyContent="space-between"><Typography>Subtotal</Typography><Typography>{money(quote.subtotal)}</Typography></Stack><Stack direction="row" justifyContent="space-between"><Typography>Discount</Typography><Typography>−{money(quote.discount)}</Typography></Stack><Stack direction="row" justifyContent="space-between"><Typography>VAT</Typography><Typography>{money(quote.tax)}</Typography></Stack>{quote.depositRequired>0&&<Stack direction="row" justifyContent="space-between"><Typography>Refundable bond required</Typography><Typography>{money(quote.depositRequired)}</Typography></Stack>}</Box>}{['Sent','Negotiating'].includes(quote.status)&&<Stack direction={{xs:'column',sm:'row'}} gap={1} mt={2}><Button variant="contained" color="success" disabled={submitting} onClick={()=>onAccept(quote)}>Accept quote and request hire</Button><Button variant="outlined" color="error" onClick={()=>onDecline(quote)}>Decline</Button></Stack>}</CardContent></Card>)}</Stack>
}

function BookingCards({ bookings, onOpen, onChange, loading, onBrowse }: { bookings: Booking[]; onOpen: (id: string) => void; onChange: (booking: Booking, type: 'dates' | 'cancel' | 'extend' | 'incident') => void; loading: boolean; onBrowse: () => void }) {
  if (bookings.length === 0) return <EmptyState title="No bookings linked to this account" text="Browse the catalogue and check availability to submit your first request." action="Browse rentals" onAction={onBrowse} />
  return <Stack spacing={2} mt={2}>{bookings.map(booking => { const item = booking.items[0]; return <Card key={booking.id} variant="outlined"><CardContent sx={{ p: 3 }}><Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" gap={2}><Box><Typography variant="overline">Reference {booking.reference}</Typography><Typography variant="h6" fontWeight={750}>{item?.name ?? 'Service request'}</Typography><Typography color="text.secondary">{item?.divisionName ?? 'Carpenters Fiji'} · {booking.branchName}</Typography></Box><Stack alignItems={{ md: 'flex-end' }} gap={1}><Chip label={statusLabel(booking.status)} color={statusColor(booking.status)} />{item && <Typography>{date(item.startAt)} – {date(item.endAt)}</Typography>}</Stack></Stack><Stack direction={{ xs: 'column', sm: 'row' }} gap={1} mt={2}><Button variant="contained" disabled={loading} onClick={() => onOpen(booking.id)}>Manage rental</Button>{booking.status === 'Draft' && <Button variant="outlined" onClick={() => onChange(booking, 'dates')}>Change dates</Button>}{['Draft', 'Confirmed'].includes(booking.status) && <Button color="error" variant="outlined" onClick={() => onChange(booking, 'cancel')}>Request cancellation</Button>}{['Confirmed', 'ConvertedToRental'].includes(booking.status) && <Button variant="outlined" onClick={() => onChange(booking, 'extend')}>Request extension</Button>}{booking.status === 'ConvertedToRental' && <Button color="warning" variant="contained" onClick={() => onChange(booking, 'incident')}>Emergency / incident</Button>}</Stack></CardContent></Card>})}</Stack>
}

function BookingDetailDialog({ booking, onClose, onResend }: { booking: BookingDetails | null; onClose: () => void; onResend: (bookingId: string) => Promise<void> }) {
  if (!booking) return null
  const item = booking.items[0]
  async function downloadReceipt() {
    if (!booking?.invoice) return
    const { jsPDF } = await import('jspdf')
    const pdf = new jsPDF(); let y = 20
    pdf.setFontSize(18); pdf.text('Carpenters Fiji — Rental Receipt', 20, y); y += 12
    pdf.setFontSize(11); pdf.text(`Invoice: ${booking.invoice.invoiceNumber}`, 20, y); y += 7
    pdf.text(`Booking: ${booking.reference}`, 20, y); y += 7
    pdf.text(`Customer rental: ${item?.name ?? 'Rental service'}`, 20, y); y += 12
    booking.invoice.lines.forEach(line => { pdf.text(`${line.description}  ${line.quantity} × ${money(line.unitPrice)}`, 20, y); y += 7 })
    y += 3; pdf.text(`Total: ${money(booking.invoice!.total)}`, 20, y); y += 7
    pdf.text(`Paid: ${money(booking.invoice!.amountPaid)}`, 20, y); y += 7
    pdf.text(`Balance: ${money(booking.invoice!.balanceDue)}`, 20, y)
    pdf.save(`${booking.invoice.invoiceNumber}.pdf`)
  }
  return <Dialog open fullWidth maxWidth="lg" onClose={onClose}><DialogTitle><Stack direction="row" justifyContent="space-between" alignItems="center"><Box><Typography variant="overline">Booking {booking.reference}</Typography><Typography variant="h5" fontWeight={800}>{item?.name ?? 'Rental booking'}</Typography></Box><Chip label={statusLabel(booking.status)} color={statusColor(booking.status)} /></Stack></DialogTitle><DialogContent dividers><Stack spacing={4}>
    <Box><Typography variant="h6" fontWeight={750}>Progress</Typography><Grid container spacing={1} mt={1}>{bookingStages.map((stage, index) => <Grid key={stage} size={{ xs: 6, sm: 3 }}><Box sx={{ borderRadius: 2, p: 1.5, bgcolor: index <= bookingStage(booking.status) ? 'secondary.main' : '#eee' }}><Typography fontWeight={700}>{index + 1}. {stage}</Typography></Box></Grid>)}</Grid></Box>
    {['Confirmed', 'ConvertedToRental'].includes(booking.status) && <Box><Stack direction="row" justifyContent="space-between" alignItems="center"><Box><Typography variant="h6" fontWeight={750}>Pickup readiness</Typography><Typography color="text.secondary">Complete these items to avoid delays at pickup.</Typography></Box><Chip label={`${Object.values(booking.readiness).filter(x => x.complete).length}/${Object.values(booking.readiness).length} ready`} color={Object.values(booking.readiness).every(x => x.complete) ? 'success' : 'warning'} /></Stack><Grid container spacing={1.5} mt={1}>{Object.entries(booking.readiness).filter(([, item]) => item.required !== false).map(([key, item]) => <Grid key={key} size={{ xs: 12, sm: 6 }}><Card variant="outlined" sx={{ borderColor: item.complete ? 'success.main' : 'warning.main' }}><CardContent sx={{ py: 1.5 }}><Stack direction="row" gap={1} alignItems="center">{item.complete ? <CheckCircleOutlined color="success" /> : <DashboardOutlined color="warning" />}<Box><Typography fontWeight={700}>{item.label}</Typography>{!item.complete && <Typography variant="caption" color="text.secondary">Pending — contact the branch if you need help.</Typography>}</Box></Stack></CardContent></Card></Grid>)}</Grid></Box>}
    <Grid container spacing={3}><Grid size={{ xs: 12, md: 7 }}><Typography variant="h6" fontWeight={750}>Hire details</Typography><Grid container spacing={2} mt={.5}><Grid size={{ xs: 6 }}><Typography variant="caption" color="text.secondary">Division</Typography><Typography>{item?.divisionName}</Typography></Grid><Grid size={{ xs: 6 }}><Typography variant="caption" color="text.secondary">Category</Typography><Typography>{item?.category ?? item?.type}</Typography></Grid><Grid size={{ xs: 6 }}><Typography variant="caption" color="text.secondary">Hire period</Typography><Typography>{item ? `${date(item.startAt)} – ${date(item.endAt)}` : 'Not assigned'}</Typography></Grid><Grid size={{ xs: 6 }}><Typography variant="caption" color="text.secondary">Personnel</Typography><Typography>{item?.personnelRequirement === 'None' ? 'Not required' : item?.personnelRequirement}</Typography></Grid></Grid></Grid><Grid size={{ xs: 12, md: 5 }}><Box sx={{ bgcolor: '#f5f5f1', borderRadius: 2, p: 2.5 }}><Typography fontWeight={750}>{booking.branch.name}</Typography><Typography color="text.secondary">{booking.branch.address}</Typography>{booking.branch.pickupInstructions && <Typography variant="body2" mt={1}>{booking.branch.pickupInstructions}</Typography>}{booking.branch.operatingHours.length > 0 && <Typography variant="caption" color="text.secondary" display="block" mt={1}>{booking.branch.operatingHours.filter(x => !x.isClosed).map(x => `${x.dayOfWeek.slice(0, 3)} ${x.opensAt.slice(0, 5)}–${x.closesAt.slice(0, 5)}`).join(' · ')}</Typography>}{booking.branch.phone && <Button href={`tel:${booking.branch.phone}`} sx={{ mt: 1 }}>Call branch</Button>}</Box></Grid></Grid>
    <Box><Typography variant="h6" fontWeight={750}>Customer price summary</Typography><Stack spacing={1} mt={1}>{[['Base hire', booking.pricing.baseSubtotal], ...booking.pricing.charges.map(x => [x.description, x.amount] as [string, number]), ['Discount', -booking.pricing.discountAmount], [`VAT (${booking.pricing.taxRate}%)`, booking.pricing.taxAmount]].map(([label, value]) => <Stack key={String(label)} direction="row" justifyContent="space-between"><Typography color="text.secondary">{label}</Typography><Typography>{money(Number(value))}</Typography></Stack>)}<Divider /><Stack direction="row" justifyContent="space-between"><Typography variant="h6" fontWeight={750}>Estimated rental total</Typography><Typography variant="h6" fontWeight={800}>{money(booking.pricing.total)}</Typography></Stack>{booking.bond.required > 0 && <Alert severity={booking.bond.status==='AwaitingPayment'?'warning':'info'}>Refundable bond: {money(booking.bond.required)} · {booking.bond.status.replace(/([A-Z])/g,' $1').trim()}{booking.bond.held>0?` · Held ${money(booking.bond.held)}`:''}{booking.bond.deduction>0?` · Deduction ${money(booking.bond.deduction)}`:''}{booking.bond.refund>0?` · Refund ${money(booking.bond.refund)}`:''}</Alert>}</Stack></Box>
    <Grid container spacing={3}><Grid size={{ xs: 12, md: 6 }}><Typography variant="h6" fontWeight={750}>Agreement</Typography>{booking.agreement ? <Card variant="outlined" sx={{ mt: 1 }}><CardContent><Typography fontWeight={700}>{booking.agreement.agreementNumber}</Typography><Typography color="text.secondary">Signed by {booking.agreement.customerSignatureName} on {date(booking.agreement.customerSignedAt)}</Typography><Stack direction="row" gap={1} mt={1}><Chip size="small" label={booking.agreement.status} color="success" /><Button size="small" startIcon={<DescriptionOutlined />} onClick={() => window.print()}>View / print agreement</Button></Stack></CardContent></Card> : <Alert severity="info" sx={{ mt: 1 }}>The agreement is generated at pickup after verification and the pre-hire inspection.</Alert>}</Grid><Grid size={{ xs: 12, md: 6 }}><Typography variant="h6" fontWeight={750}>Invoice and payments</Typography>{booking.invoice ? <Card variant="outlined" sx={{ mt: 1 }}><CardContent><Stack direction="row" justifyContent="space-between"><Typography fontWeight={700}>{booking.invoice.invoiceNumber}</Typography><Chip size="small" label={booking.invoice.status} /></Stack><Typography variant="h5" fontWeight={800} mt={1}>{money(booking.invoice.total)}</Typography><Typography color="text.secondary">Paid {money(booking.invoice.amountPaid)} · Balance {money(booking.invoice.balanceDue)}</Typography><Button size="small" startIcon={<DescriptionOutlined />} sx={{ mt: 1 }} onClick={() => void downloadReceipt()}>Download receipt PDF</Button></CardContent></Card> : <Alert severity="info" sx={{ mt: 1 }}>The final invoice will appear after billing or return.</Alert>}</Grid></Grid>
    {(booking.dispatches.length > 0 || booking.inspections.length > 0) && <Grid container spacing={3}><Grid size={{ xs: 12, md: 6 }}><Typography variant="h6" fontWeight={750}>Delivery and collection</Typography>{booking.dispatches.length ? booking.dispatches.map(x => <Card key={x.dispatchNumber} variant="outlined" sx={{ mt: 1 }}><CardContent><Typography fontWeight={700}>{x.type} · {x.dispatchNumber}</Typography><Typography>{date(x.scheduledAt)} · {x.status}</Typography><Typography color="text.secondary">{x.address}</Typography></CardContent></Card>) : <Typography color="text.secondary">No delivery or collection scheduled.</Typography>}</Grid><Grid size={{ xs: 12, md: 6 }}><Typography variant="h6" fontWeight={750}>Inspection records</Typography>{booking.inspections.map((x, index) => <Card key={`${x.type}-${index}`} variant="outlined" sx={{ mt: 1 }}><CardContent><Typography fontWeight={700}>{x.type} inspection</Typography><Typography color="text.secondary">{date(x.completedAt)}{x.meterReading != null ? ` · Meter ${x.meterReading}` : ''}{x.fuelLevelPercent != null ? ` · Fuel ${x.fuelLevelPercent}%` : ''}</Typography>{x.damageNotes && <Alert severity="warning" sx={{ mt: 1 }}>{x.damageNotes}</Alert>}</CardContent></Card>)}</Grid></Grid>}
    {(booking.incidents.length > 0 || booking.cases.length > 0) && <Box><Typography variant="h6" fontWeight={750}>Support history</Typography><Stack spacing={1} mt={1}>{booking.incidents.map(x => <Alert key={x.incidentNumber} severity="warning"><strong>{x.incidentNumber} · {x.type} · {x.status}</strong><br />{x.description}</Alert>)}{booking.cases.map(x => <Alert key={x.caseNumber} severity="info">{x.caseNumber} · {x.subject} · {x.status}</Alert>)}</Stack></Box>}
  </Stack></DialogContent><DialogActions sx={{ p: 3 }}><Button onClick={() => void onResend(booking.id)}>Resend confirmation</Button><Button onClick={() => window.print()} startIcon={<DescriptionOutlined />}>Print summary</Button><Box sx={{ flex: 1 }} /><Button variant="contained" onClick={onClose}>Close</Button></DialogActions></Dialog>
}

function EmptyState({ title, text, action, onAction }: { title: string; text: string; action: string; onAction: () => void }) { return <Card variant="outlined"><CardContent sx={{ py: 7, textAlign: 'center' }}><Typography variant="h6" fontWeight={700}>{title}</Typography><Typography color="text.secondary" mt={1}>{text}</Typography><Button variant="contained" sx={{ mt: 2 }} onClick={onAction}>{action}</Button></CardContent></Card> }
function DocumentGroup({ title, empty, items }: { title: string; empty: string; items: { key: string; title: string; subtitle: string; status: string; action?: () => void }[] }) { return <Card variant="outlined" sx={{ height: '100%' }}><CardContent sx={{ p: 3 }}><Typography variant="h6" fontWeight={750}>{title}</Typography>{items.length === 0 ? <Typography color="text.secondary" mt={2}>{empty}</Typography> : <Stack divider={<Divider flexItem />} mt={1}>{items.map(item => <Stack key={item.key} direction="row" alignItems="center" justifyContent="space-between" gap={2} py={1.5}><Box><Typography fontWeight={650}>{item.title}</Typography><Typography variant="body2" color="text.secondary">{item.subtitle}</Typography></Box><Stack alignItems="flex-end" gap={.5}><Chip size="small" label={item.status} />{item.action && <Button size="small" onClick={item.action}>View</Button>}</Stack></Stack>)}</Stack>}</CardContent></Card> }
