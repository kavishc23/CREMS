import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import axios from 'axios'
import AddOutlined from '@mui/icons-material/AddOutlined'
import BlockOutlined from '@mui/icons-material/BlockOutlined'
import CheckCircleOutline from '@mui/icons-material/CheckCircleOutline'
import EditOutlined from '@mui/icons-material/EditOutlined'
import SearchOutlined from '@mui/icons-material/SearchOutlined'
import MarkEmailReadOutlined from '@mui/icons-material/MarkEmailReadOutlined'
import VisibilityOutlined from '@mui/icons-material/VisibilityOutlined'
import PeopleAltOutlined from '@mui/icons-material/PeopleAltOutlined'
import CloseOutlined from '@mui/icons-material/CloseOutlined'
import {
  Alert, Box, Button, Card, CardContent, Chip, CircularProgress, Dialog,
  DialogActions, DialogContent, DialogTitle, FormControl, IconButton,
  InputAdornment, InputLabel, MenuItem, Select, Stack, Table, TableBody,
  TableCell, TableContainer, TableHead, TableRow, TextField, Tooltip, Typography, Pagination,
  Grid, Tab, Tabs,
} from '@mui/material'
import { api } from '../api/client'
import { PageHeader } from '../components/PageHeader'
import { LicenceWorkflowDialog } from '../components/LicenceWorkflowDialog'
import { openProtectedFile } from '../utils/openProtectedFile'

type Customer = {
  id: string; customerNumber: string; name: string
  email: string | null; phone: string | null; address: string | null
  identificationNumber: string | null; isBlocked: boolean; isActive: boolean
  hirePreferences: HirePreference[]
  hirePreference?: HirePreference | 'NoPreference'
  hasOnlineAccount: boolean; emailConfirmed: boolean
  driverLicenceDocumentId: string | null; driverLicenceFileName: string | null
  licenceNumber: string | null; licenceClasses: string | null; licenceStatus: 'Required' | 'Verified'
}

function AdminActivityDialog({ activity, loading, tab, search, page, saving, onTab, onSearch, onLoad, onSecurity, onClose }: { activity: CustomerActivity | null; loading: boolean; tab: 'overview' | 'bookings' | 'quotations' | 'invoice'; search: string; page: number; saving: boolean; onTab: (value: 'overview' | 'bookings' | 'quotations' | 'invoice') => void; onSearch: (value: string) => void; onLoad: (page: number, search: string) => void; onSecurity: (path: string) => void; onClose: () => void }) {
  const total = activity ? activity.pagination[tab === 'invoice' ? 'invoices' : tab === 'overview' ? 'bookings' : tab] : 0
  const [recordStatus, setRecordStatus] = useState('All')
  useEffect(() => setRecordStatus('All'), [activity?.customer.id])
  const statusMatches = (status: string) => recordStatus === 'All' || status === recordStatus
  const pageCount = Math.max(1, Math.ceil(total / (activity?.pagination.pageSize ?? 10)))
  return <Dialog open={Boolean(activity) || loading} onClose={() => !loading && onClose()} disableScrollLock fullWidth maxWidth="lg" PaperProps={{ sx: { height: { md: 'calc(100dvh - 20px)' }, maxHeight: 'calc(100dvh - 12px)', overflow: { xs: 'auto', md: 'hidden' }, borderRadius: 2.5 } }}>
    <DialogTitle sx={{ px: 3, py: 1.4, display: 'flex', justifyContent: 'space-between', alignItems: 'center', background: 'linear-gradient(110deg,#fff 65%,#fff8cc)' }}><Box><Typography variant="overline" fontWeight={900} color="text.secondary" lineHeight={1}>Customer record</Typography><Typography variant="h6" fontWeight={850}>Customer activity · {activity?.customer.name ?? 'Loading…'}</Typography></Box><IconButton aria-label="Close" onClick={onClose}><CloseOutlined /></IconButton></DialogTitle>
    {activity && <><Grid container spacing={1.2} sx={{ px: 2.5, py: 1.2 }}>{[{ label: 'Bookings', value: activity.summary.bookings, tone: '#e9f2ff' }, { label: 'Quotations', value: activity.summary.quotations, tone: '#fff4cf' }, { label: 'Invoice', value: activity.summary.invoices, tone: '#e5f5eb' }, { label: 'Outstanding', value: `$${activity.summary.outstanding.toFixed(2)}`, tone: '#fff0cf' }].map(item => <Grid key={item.label} size={{ xs: 6, md: 3 }}><Card variant="outlined" sx={{ height: '100%', borderColor: item.label === 'Outstanding' && activity.summary.outstanding > 0 ? 'warning.light' : 'divider' }}><CardContent sx={{ py: 1, px: 1.5, '&:last-child': { pb: 1 }, display: 'flex', alignItems: 'center', gap: 1.2 }}><Box sx={{ width: 38, height: 38, borderRadius: 2, bgcolor: item.tone, display: 'grid', placeItems: 'center', fontWeight: 900 }}>{item.label === 'Outstanding' ? '!' : item.label[0]}</Box><Box><Typography fontWeight={850}>{item.value}</Typography><Typography variant="body2" color="text.secondary" fontWeight={750}>{item.label}</Typography></Box></CardContent></Card></Grid>)}</Grid><Tabs value={tab} onChange={(_, value) => onTab(value)} variant="scrollable" sx={{ minHeight: 43, px: 2.5, bgcolor: '#fafaf7', borderBottom: 1, borderColor: 'divider', '& .MuiTab-root': { minHeight: 43, py: .5, fontWeight: 800, fontSize: 15 } }}><Tab value="overview" label="Overview" /><Tab value="bookings" label={`Bookings ${activity.summary.bookings}`} /><Tab value="quotations" label={`Quotations ${activity.summary.quotations}`} /><Tab value="invoice" label={`Invoice ${activity.summary.invoices}`} /></Tabs></>}
    <DialogContent sx={{ overflow: { xs: 'auto', md: 'hidden' }, px: 2.5, py: 1.5 }}>{loading ? <Box sx={{ minHeight: 240, display: 'grid', placeItems: 'center' }}><CircularProgress /></Box> : activity && <Stack spacing={1.25}>
      {tab === 'overview' && <Grid container spacing={2}>
        <Grid size={{ xs: 12, md: 6 }}><Card variant="outlined" sx={{ height: '100%', borderLeft: '5px solid #4b93df', bgcolor: '#f8fbff' }}><CardContent sx={{ p: 2.5 }}><Typography variant="overline" fontWeight={900} color="text.secondary">Portal access</Typography><Typography variant="h6" fontWeight={850}>Online account</Typography><Typography mt={1.5}><Box component="span" sx={{ color: activity.account?.emailConfirmed ? 'success.main' : 'warning.main', mr: 1 }}>●</Box>{activity.account ? activity.account.emailConfirmed ? 'Verified' : 'Activation pending' : 'No online account'}</Typography><Typography color="text.secondary" mt={1}>Last activity: {activity.account?.lastActivityAt ? new Date(activity.account.lastActivityAt).toLocaleString('en-FJ') : 'Never'}</Typography><Typography mt={1}>Rental preferences: {activity.customer.hirePreferences?.length ? activity.customer.hirePreferences.map(value => preferenceLabel[value]).join(', ') : 'All rentals'}</Typography></CardContent></Card></Grid>
        <Grid size={{ xs: 12, md: 6 }}><Card variant="outlined" sx={{ height: '100%', borderLeft: '5px solid #2ca56c', bgcolor: '#f7fcf9' }}><CardContent sx={{ p: 2.5 }}><Typography variant="overline" fontWeight={900} color="text.secondary">Identification</Typography><Typography variant="h6" fontWeight={850}>Driver licence</Typography><Typography mt={1.5} fontWeight={750}><Box component="span" sx={{ color: activity.licence ? 'success.main' : 'error.main', mr: 1 }}>●</Box>{activity.licence ? 'Licence verified' : 'Licence required'}</Typography><Typography mt={1}>{activity.licence ? `${activity.licence.licenceNumber} · Classes ${activity.licence.licenceClasses}` : 'No verified licence recorded.'}</Typography>{activity.licence && <Button variant="outlined" sx={{ mt: 1.5 }} onClick={() => void openProtectedFile(`/customers/${activity.customer.id}/licence/image`)}>View licence</Button>}</CardContent></Card></Grid>
        {activity.account && <Grid size={12}><Card variant="outlined" sx={{ borderLeft: '5px solid #f1d400', bgcolor: '#fffef5' }}><CardContent sx={{ p: 2.5 }}><Typography variant="overline" fontWeight={900} color="text.secondary">Account management</Typography><Typography variant="h6" fontWeight={850}>Customer security controls</Typography><Typography color="text.secondary" my={1.5}>Manage portal access without changing the customer’s rental records.</Typography><Stack direction="row" gap={1} flexWrap="wrap"><Button variant="outlined" disabled={saving} onClick={() => onSecurity('reset-password')}>Reset password</Button><Button variant="outlined" disabled={saving} onClick={() => onSecurity('revoke-sessions')}>Revoke sessions</Button><Button variant="outlined" disabled={saving} onClick={() => onSecurity(activity.account?.lockoutEnd ? 'unlock' : 'lock')}>{activity.account.lockoutEnd ? 'Unlock portal' : 'Lock portal'}</Button><Button variant="outlined" color={activity.account.isActive ? 'error' : 'success'} disabled={saving} onClick={() => onSecurity(activity.account?.isActive ? 'disable' : 'enable')}>{activity.account.isActive ? 'Disable portal' : 'Enable portal'}</Button></Stack></CardContent></Card></Grid>}
      </Grid>}
      {tab !== 'overview' && <>{tab === 'invoice' && <Card variant="outlined" sx={{ borderColor: 'warning.light', bgcolor: '#fffaf0' }}><CardContent sx={{ py: 1.1, '&:last-child': { pb: 1.1 } }}><Typography variant="overline" fontWeight={900} color="text.secondary">Current balance</Typography><Stack direction="row" justifyContent="space-between" alignItems="center"><Typography variant="h6" fontWeight={850}>${activity.summary.outstanding.toFixed(2)} outstanding</Typography>{activity.summary.outstanding > 0 && <Chip label="Payment due" color="warning" size="small" />}</Stack></CardContent></Card>}<Stack direction={{ xs: 'column', sm: 'row' }} gap={1} justifyContent="space-between"><TextField size="small" placeholder={`Search ${tab === 'invoice' ? 'invoice' : tab.slice(0, -1)} reference or branch`} value={search} onChange={event => onSearch(event.target.value)} onKeyDown={event => { if (event.key === 'Enter') onLoad(1, search) }} sx={{ width: { xs: '100%', sm: 400 } }} /><FormControl size="small" sx={{ minWidth: 155 }}><InputLabel>Status</InputLabel><Select label="Status" value={recordStatus} onChange={event => setRecordStatus(event.target.value)}><MenuItem value="All">All statuses</MenuItem>{Array.from(new Set(tab === 'bookings' ? activity.bookings.map(item => item.status) : tab === 'quotations' ? activity.quotations.map(item => item.status) : activity.invoices.map(item => item.status))).map(value => <MenuItem key={value} value={value}>{value}</MenuItem>)}</Select></FormControl></Stack><TableContainer component={Card} variant="outlined" sx={{ overflow: 'hidden', '& .MuiTableCell-root': { py: .8, px: 1.5, whiteSpace: 'nowrap' } }}><Table size="small"><TableHead><TableRow>{tab === 'bookings' ? <><TableCell>Booking reference</TableCell><TableCell>Branch</TableCell><TableCell>Status</TableCell><TableCell>Assets</TableCell><TableCell>Created</TableCell></> : tab === 'quotations' ? <><TableCell>Quotation reference</TableCell><TableCell>Branch</TableCell><TableCell>Status</TableCell><TableCell>Value</TableCell><TableCell>Created</TableCell></> : <><TableCell>Invoice</TableCell><TableCell>Status</TableCell><TableCell>Total</TableCell><TableCell>Outstanding</TableCell><TableCell>Issued</TableCell></>}</TableRow></TableHead><TableBody>{tab === 'bookings' && activity.bookings.filter(item => statusMatches(item.status)).map(item => <TableRow key={item.id}><TableCell>{item.bookingNumber}</TableCell><TableCell>{item.branchName}</TableCell><TableCell>{item.status}</TableCell><TableCell>{item.assetCount}</TableCell><TableCell>{new Date(item.createdAt).toLocaleDateString('en-FJ')}</TableCell></TableRow>)}{tab === 'quotations' && activity.quotations.filter(item => statusMatches(item.status)).map(item => <TableRow key={item.id}><TableCell>{item.quoteNumber}</TableCell><TableCell>{item.branchName}</TableCell><TableCell>{item.status}</TableCell><TableCell>FJD {item.total.toLocaleString('en-FJ')}</TableCell><TableCell>{new Date(item.createdAt).toLocaleDateString('en-FJ')}</TableCell></TableRow>)}{tab === 'invoice' && activity.invoices.filter(item => statusMatches(item.status)).map(item => <TableRow key={item.id}><TableCell>{item.invoiceNumber}</TableCell><TableCell>{item.status}</TableCell><TableCell>FJD {item.total.toLocaleString('en-FJ')}</TableCell><TableCell>FJD {item.balanceDue.toLocaleString('en-FJ')}</TableCell><TableCell>{new Date(item.issuedAt).toLocaleDateString('en-FJ')}</TableCell></TableRow>)}</TableBody></Table></TableContainer><Stack direction="row" spacing={1} alignItems="center" justifyContent="flex-end"><Button size="small" variant="outlined" disabled={page <= 1} onClick={() => onLoad(page - 1, search)}>Previous</Button><Typography variant="body2">Page {page} of {pageCount}</Typography><Button size="small" variant="outlined" disabled={page >= pageCount} onClick={() => onLoad(page + 1, search)}>Next</Button></Stack></>}
    </Stack>}</DialogContent>
  </Dialog>
}
type CustomerForm = {
  customerNumber: string; name: string; email: string
  phone: string; address: string; identificationNumber: string
}
type CustomerActivity = {
  customer: { id: string; customerNumber: string; name: string; email: string | null; phone: string | null; address: string | null; hirePreferences: HirePreference[] }
  account: { emailConfirmed: boolean; isActive: boolean; lastLoginAt: string | null; lastActivityAt: string | null; lockoutEnd: string | null } | null
  licence: { licenceNumber: string; licenceClasses: string; status: string; updatedAt: string } | null
  summary: { bookings: number; quotations: number; invoices: number; totalBilled: number; outstanding: number; openCases?: number }
  bookings: { id: string; bookingNumber: string; status: string; createdAt: string; branchName: string; assetCount: number }[]
  quotations: { id: string; quoteNumber: string; status: string; total: number; validUntil: string; createdAt: string; branchName: string }[]
  invoices: { id: string; invoiceNumber: string; status: string; total: number; balanceDue: number; issuedAt: string }[]
  pagination: { page: number; pageSize: number; bookings: number; quotations: number; invoices: number }
  cases?: { id: string; caseNumber: string; type: string; priority: string; subject: string; status: string; createdAt: string }[]
}
type HirePreference = 'Vehicles' | 'Equipment' | 'WasteAndSiteHire'
const preferenceLabel: Record<HirePreference, string> = { Vehicles: 'Vehicles', Equipment: 'Equipment', WasteAndSiteHire: 'Waste & site hire' }
function customerPreferences(customer: Pick<Customer, 'hirePreferences' | 'hirePreference'>): HirePreference[] {
  if (Array.isArray(customer.hirePreferences)) return customer.hirePreferences
  return customer.hirePreference && customer.hirePreference !== 'NoPreference' ? [customer.hirePreference] : []
}
const emptyForm: CustomerForm = {
  customerNumber: '', name: '', email: '', phone: '', address: '', identificationNumber: '',
}

export function CustomersPage({ administrationView = false }: { administrationView?: boolean }) {
  const [customers, setCustomers] = useState<Customer[]>([])
  const [loading, setLoading] = useState(true)
  const [open, setOpen] = useState(false)
  const [saving, setSaving] = useState(false)
  const [editing, setEditing] = useState<Customer | null>(null)
  const [form, setForm] = useState<CustomerForm>(emptyForm)
  const [search, setSearch] = useState(() => new URLSearchParams(window.location.search).get('search') ?? '')
  const [statusFilter, setStatusFilter] = useState<'All' | 'Verified' | 'Pending' | 'Blocked'>('All')
  const [page, setPage] = useState(1)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [activity, setActivity] = useState<CustomerActivity | null>(null)
  const [activityLoading, setActivityLoading] = useState(false)
  const [activityTab, setActivityTab] = useState<'overview' | 'bookings' | 'quotations' | 'invoice'>('overview')
  const [activitySearch, setActivitySearch] = useState('')
  const [activityPage, setActivityPage] = useState(1)
  const [licenceMode, setLicenceMode] = useState<'scan' | 'manual' | null>(null)
  const [confirmation, setConfirmation] = useState<{ kind: 'block' | 'restore' | 'activation' | 'remove-licence'; customer: Customer } | null>(null)

  const loadCustomers = useCallback(async () => {
    setLoading(true)
    try {
      const response = await api.get<Customer[]>('/customers')
      setCustomers(response.data)
    } catch { setError('Unable to load customers. Confirm that the backend is running.') }
    finally { setLoading(false) }
  }, [])

  useEffect(() => {
    // Initial loading synchronizes the register with the API.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void loadCustomers()
  }, [loadCustomers])

  const visibleCustomers = useMemo(() => {
    const term = search.trim().toLowerCase()
    return customers.filter((customer) => {
      const matchesSearch = !term || [customer.customerNumber, customer.name, customer.email, customer.phone, customer.identificationNumber]
        .some((value) => value?.toLowerCase().includes(term))
      const matchesStatus = statusFilter === 'All'
        || (statusFilter === 'Verified' && customer.emailConfirmed)
        || (statusFilter === 'Pending' && customer.hasOnlineAccount && !customer.emailConfirmed)
        || (statusFilter === 'Blocked' && customer.isBlocked)
      return matchesSearch && matchesStatus
    })
  }, [customers, search, statusFilter])
  const customerPageCount = Math.max(1, Math.ceil(visibleCustomers.length / 20))
  const pagedCustomers = visibleCustomers.slice((page - 1) * 20, page * 20)

  const customerSummary = useMemo(() => [
    { label: 'Total customers', value: customers.length, color: 'text.primary' },
    { label: 'Online verified', value: customers.filter((item) => item.emailConfirmed).length, color: 'success.main' },
    { label: 'Licence verified', value: customers.filter((item) => item.licenceStatus === 'Verified').length, color: 'info.main' },
    { label: 'Pending activation', value: customers.filter((item) => item.hasOnlineAccount && !item.emailConfirmed).length, color: 'warning.main' },
    { label: 'Blocked', value: customers.filter((item) => item.isBlocked).length, color: 'error.main' },
  ], [customers])

  function openCreate() {
    const next = customers.map(customer => Number(customer.customerNumber.replace(/^CUS-/, '')) || 0).reduce((max, value) => Math.max(max, value), 0) + 1
    setEditing(null); setForm({ ...emptyForm, customerNumber: `CUS-${String(next).padStart(6, '0')}` }); setError(''); setOpen(true)
  }
  function openEdit(customer: Customer) {
    setEditing(customer)
    setForm({ customerNumber: customer.customerNumber, name: customer.name,
      email: customer.email ?? '', phone: customer.phone ?? '', address: customer.address ?? '',
      identificationNumber: customer.identificationNumber ?? '' })
    setError(''); setOpen(true)
  }

  async function save(event: FormEvent) {
    event.preventDefault(); setSaving(true); setError('')
    try {
      if (editing) await api.put(`/customers/${editing.id}`, form)
      else await api.post('/customers', form)
      setOpen(false); await loadCustomers()
    } catch (requestError: unknown) {
      const data = axios.isAxiosError(requestError) ? requestError.response?.data : undefined
      const errors = data?.errors as Record<string, string[]> | undefined
      setError(errors ? Object.values(errors).flat().join(' ') : 'Unable to save the customer.')
    } finally { setSaving(false) }
  }

  async function setStatus(customer: Customer, change: Partial<Pick<Customer, 'isActive' | 'isBlocked'>>) {
    setError('')
    try {
      await api.patch(`/customers/${customer.id}/status`, {
        isActive: change.isActive ?? customer.isActive,
        isBlocked: change.isBlocked ?? customer.isBlocked,
      })
      await loadCustomers()
    } catch { setError('Unable to update the customer status.') }
  }
  async function enableOnlineAccess(customer: Customer) {
    setError(''); setNotice('')
    try { const response = await api.post<{ message: string }>(`/customers/${customer.id}/online-access`); setNotice(response.data.message); await loadCustomers() }
    catch (reason) { const data = axios.isAxiosError(reason) ? reason.response?.data : undefined; setError(data?.message ?? 'Unable to enable online access.') }
  }
  async function confirmCustomerAction() {
    if (!confirmation) return
    const { kind, customer } = confirmation
    setConfirmation(null)
    if (kind === 'activation') await enableOnlineAccess(customer)
    else if (kind === 'remove-licence') {
      setSaving(true); setError(''); setNotice('')
      try {
        await api.delete(`/customers/${customer.id}/licence`)
        const response = await api.get<Customer[]>('/customers')
        setCustomers(response.data)
        setEditing(response.data.find(item => item.id === customer.id) ?? null)
        setNotice('Driver licence removed.')
      } catch (reason) {
        const data = axios.isAxiosError(reason) ? reason.response?.data : undefined
        setError(data?.message ?? 'Unable to remove the driver licence.')
      } finally { setSaving(false) }
    } else await setStatus(customer, { isBlocked: kind === 'block' })
  }
  async function viewActivity(customer: Customer, nextPage = 1, term = activitySearch) {
    const openingCustomer = !activity || activity.customer.id !== customer.id
    if (openingCustomer) {
      setActivityTab('overview'); setActivitySearch(''); setActivityPage(1)
      term = ''; nextPage = 1
    }
    setActivityLoading(true); setError('')
    try {
      setActivity((await api.get<CustomerActivity>(`/customers/${customer.id}/activity`, { params: { page: nextPage, pageSize: 10, search: term || undefined } })).data)
      setActivityPage(nextPage)
    }
    catch { setError('Unable to load this customer’s activity.') }
    finally { setActivityLoading(false) }
  }
  async function customerSecurity(path: string) { if (!activity) return; setSaving(true); setError(''); try { await api.post(`/customers/${activity.customer.id}/security/${path}`); await viewActivity({ id: activity.customer.id } as Customer); setNotice('Customer portal security was updated.') } catch (reason) { setError(axios.isAxiosError(reason) ? reason.response?.data?.message ?? 'Unable to update customer security.' : 'Unable to update customer security.') } finally { setSaving(false) } }
  async function uploadLicence(file?: File) {
    if (!editing || !file) return
    setSaving(true); setError('')
    try {
      const data = new FormData(); data.append('file', file)
      await api.post(`/customers/${editing.id}/driver-licence`, data, { headers: { 'Content-Type': 'multipart/form-data' } })
      await loadCustomers(); setNotice('Driver licence updated.'); setOpen(false)
    } catch (reason) { setError(axios.isAxiosError(reason) ? reason.response?.data?.message ?? 'Unable to upload the driver licence.' : 'Unable to upload the driver licence.') }
    finally { setSaving(false) }
  }
  function viewLicence(customer: Customer) {
    const endpoint = customer.licenceStatus === 'Verified'
      ? `/customers/${customer.id}/licence/image`
      : customer.driverLicenceDocumentId
        ? `/customers/${customer.id}/driver-licence/${customer.driverLicenceDocumentId}`
        : null
    if (endpoint) void openProtectedFile(endpoint)
  }

  return <Box sx={{ p: { xs: 2, sm: 3, lg: 4 }, maxWidth: 1500, mx: 'auto' }}>
    <PageHeader icon={<PeopleAltOutlined />} title={administrationView ? 'Customer accounts' : 'Customers'} subtitle={administrationView ? 'Manage customer identities, portal access and account activity separately from staff.' : 'Maintain customer details and rental eligibility.'} actions={<Button variant="contained" startIcon={<AddOutlined />} onClick={openCreate}>Add customer</Button>} />
    {error && !open && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
    {notice && <Alert severity="success" sx={{ mb: 2 }}>{notice}</Alert>}
    <Grid container spacing={2} mb={2.5}>
      {customerSummary.map((item) => <Grid key={item.label} size={{ xs: 6, md: 4, lg: 2.4 }}><Card variant="outlined" sx={{ height: '100%' }}><CardContent sx={{ py: 2 }}><Typography variant="h4" fontWeight={800} color={item.color}>{item.value}</Typography><Typography variant="caption" color="text.secondary" fontWeight={700} textTransform="uppercase" letterSpacing={.6}>{item.label}</Typography></CardContent></Card></Grid>)}
    </Grid>
    <Card variant="outlined"><CardContent sx={{ p: 0 }}>
      <Stack direction={{ xs: 'column', lg: 'row' }} gap={1.5} sx={{ p: 2, borderBottom: 1, borderColor: 'divider' }}>
        <TextField size="small" placeholder="Search by name, customer number, email or phone" value={search}
          onChange={(event) => { setSearch(event.target.value); setPage(1) }} sx={{ flex: 1, minWidth: 260 }}
          InputProps={{ startAdornment: <InputAdornment position="start"><SearchOutlined /></InputAdornment> }} />
        <FormControl size="small" sx={{ minWidth: 170 }}><InputLabel>Account status</InputLabel><Select label="Account status" value={statusFilter} onChange={(event) => { setStatusFilter(event.target.value as typeof statusFilter); setPage(1) }}>{['All', 'Verified', 'Pending', 'Blocked'].map((value) => <MenuItem key={value} value={value}>{value}</MenuItem>)}</Select></FormControl>
      </Stack>
      {loading ? <Box sx={{ minHeight: 280, display: 'grid', placeItems: 'center' }}><CircularProgress /></Box> :
        <TableContainer><Table><TableHead><TableRow sx={{ bgcolor: '#f6f6f3' }}>
          <TableCell>Customer</TableCell><TableCell>Contact</TableCell>
          <TableCell>Identification</TableCell><TableCell>Status</TableCell><TableCell align="right">Actions</TableCell>
        </TableRow></TableHead><TableBody>
          {visibleCustomers.length === 0 && <TableRow><TableCell colSpan={5} align="center" sx={{ py: 8, color: 'text.secondary' }}>No matching customers found.</TableCell></TableRow>}
          {pagedCustomers.map((customer) => <TableRow key={customer.id} hover>
            <TableCell><Typography fontWeight={700}>{customer.name}</Typography><Typography variant="body2" color="text.secondary">{customer.customerNumber}</Typography></TableCell>
            <TableCell><Typography variant="body2">{customer.email || '—'}</Typography><Typography variant="body2" color="text.secondary">{customer.phone || '—'}</Typography></TableCell>
            <TableCell><Typography variant="body2">{customer.licenceNumber || '—'}</Typography><Typography variant="caption" display="block" color="text.secondary">Classes: {customer.licenceClasses || '—'}</Typography><Chip size="small" label={customer.licenceStatus === 'Verified' ? 'Licence verified' : 'Licence required'} color={customer.licenceStatus === 'Verified' ? 'success' : 'error'} variant="outlined" />{customer.licenceStatus === 'Verified' && <Button size="small" sx={{ ml: 1 }} onClick={() => void openProtectedFile(`/customers/${customer.id}/licence/image`)}>View licence</Button>}</TableCell>
            <TableCell><Stack direction="row" gap={0.75} flexWrap="wrap">
              <Chip size="small" label={customer.isActive ? 'Active' : 'Inactive'} color={customer.isActive ? 'success' : 'default'} variant="outlined" />
              {customer.isBlocked && <Chip size="small" label="Blocked" color="error" variant="outlined" />}
              <Chip size="small" label={customer.emailConfirmed ? 'Online verified' : customer.hasOnlineAccount ? 'Invite pending' : 'Offline only'} color={customer.emailConfirmed ? 'success' : 'default'} variant="outlined" />
            </Stack></TableCell>
            <TableCell align="right"><Tooltip title="View customer activity"><IconButton onClick={() => void viewActivity(customer)}><VisibilityOutlined /></IconButton></Tooltip><Tooltip title="Edit customer"><IconButton onClick={() => openEdit(customer)}><EditOutlined /></IconButton></Tooltip>
              {!customer.emailConfirmed && <Tooltip title={customer.hasOnlineAccount ? 'Send a new activation code' : 'Enable online access'}><span><IconButton color="primary" disabled={!customer.email || !customer.isActive || customer.isBlocked} onClick={() => setConfirmation({ kind: 'activation', customer })}><MarkEmailReadOutlined /></IconButton></span></Tooltip>}
              <Tooltip title={customer.isBlocked ? 'Restore rental access' : 'Block from rentals'}><IconButton color={customer.isBlocked ? 'success' : 'error'} onClick={() => setConfirmation({ kind: customer.isBlocked ? 'restore' : 'block', customer })}>{customer.isBlocked ? <CheckCircleOutline /> : <BlockOutlined />}</IconButton></Tooltip>
            </TableCell>
          </TableRow>)}
        </TableBody></Table></TableContainer>}
      {visibleCustomers.length > 20 && <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ p: 2, borderTop: 1, borderColor: 'divider' }}><Typography variant="body2" color="text.secondary">Showing {(page - 1) * 20 + 1}–{Math.min(page * 20, visibleCustomers.length)} of {visibleCustomers.length}</Typography><Pagination count={customerPageCount} page={page} onChange={(_, value) => setPage(value)} size="small" /></Stack>}
    </CardContent></Card>
    <Dialog open={open} onClose={() => !saving && setOpen(false)} disableScrollLock fullWidth maxWidth="lg" PaperProps={{ sx: { borderRadius: 2.5 } }}>
      <Box component="form" onSubmit={save}><DialogTitle sx={{ px: 3.5, py: 2.5, display: 'flex', justifyContent: 'space-between', alignItems: 'center', fontSize: 26, fontWeight: 850 }}>{editing ? 'Edit customer' : 'Add customer'}<IconButton aria-label="Close" onClick={() => setOpen(false)}><CloseOutlined /></IconButton></DialogTitle><DialogContent dividers sx={{ px: 3.5, py: 2.5 }}>
        <Stack spacing={2.25} mt={1}>{error && <Alert severity="error">{error}</Alert>}
          <TextField fullWidth required label="Customer number" helperText="CREMS uses one customer-number sequence." inputProps={{ maxLength: 50, pattern: 'CUS-[0-9]{6}' }} value={form.customerNumber} onChange={(e) => setForm({ ...form, customerNumber: e.target.value.toUpperCase() })} />
          <TextField required label="Full name" value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} />
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
            <TextField fullWidth type="email" label="Email" value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })} />
            <TextField fullWidth label="Phone" value={form.phone} onChange={(e) => setForm({ ...form, phone: e.target.value })} />
          </Stack>
          <TextField label="Address" multiline minRows={2} value={form.address} onChange={(e) => setForm({ ...form, address: e.target.value })} />
          {editing && <Card variant="outlined" sx={{ borderRadius: 2 }}><CardContent sx={{ p: 2 }}><Stack direction="row" justifyContent="space-between" alignItems="flex-start"><Box><Typography variant="h6" fontWeight={800}>Driver licence</Typography><Typography color="text.secondary">{editing.licenceStatus === 'Verified' || editing.driverLicenceDocumentId ? 'Licence document securely stored.' : 'No licence document uploaded.'}</Typography></Box><Stack direction="row" alignItems="center" gap={.5}><Chip size="small" label={editing.licenceStatus === 'Verified' ? 'Licence verified' : 'Licence required'} color={editing.licenceStatus === 'Verified' ? 'success' : 'default'} variant="outlined" />{administrationView && editing.licenceStatus === 'Verified' && <Tooltip title="Remove driver licence"><IconButton size="small" color="error" aria-label="Remove driver licence" onClick={() => setConfirmation({ kind: 'remove-licence', customer: editing })}><CloseOutlined /></IconButton></Tooltip>}</Stack></Stack><Grid container spacing={1.25} mt={.5} alignItems="center"><Grid size={{ xs: 12, sm: 6, md: 2.5 }}><TextField fullWidth size="small" label="Licence number" value={editing.licenceNumber ?? 'Not recorded'} InputProps={{ readOnly: true }} /></Grid><Grid size={{ xs: 12, sm: 6, md: 2 }}><TextField fullWidth size="small" label="Licence classes" value={editing.licenceClasses ?? 'Not recorded'} InputProps={{ readOnly: true }} /></Grid><Grid size={{ xs: 12, md: 7.5 }}><Stack direction="row" gap={1} flexWrap={{ xs: 'wrap', md: 'nowrap' }} alignItems="center"><Button variant="contained" size="small" sx={{ whiteSpace: 'nowrap' }} onClick={() => setLicenceMode('scan')}>Upload &amp; scan</Button><Button variant="outlined" size="small" sx={{ whiteSpace: 'nowrap' }} onClick={() => setLicenceMode('manual')}>Enter manually</Button>{(editing.licenceStatus === 'Verified' || editing.driverLicenceDocumentId) && <Button variant="outlined" size="small" sx={{ whiteSpace: 'nowrap' }} onClick={() => void viewLicence(editing)}>View licence</Button>}</Stack></Grid></Grid></CardContent></Card>}
        </Stack>
      </DialogContent><DialogActions sx={{ p: 3, pt: 1 }}><Button onClick={() => setOpen(false)} disabled={saving}>Cancel</Button><Button type="submit" variant="contained" disabled={saving}>{saving ? 'Saving…' : 'Save customer'}</Button></DialogActions></Box>
    </Dialog>
    {editing && <LicenceWorkflowDialog open={Boolean(licenceMode)} manual={licenceMode === 'manual'} customerId={editing.id} onClose={() => setLicenceMode(null)} onSaved={() => {
      const editedId = editing.id
      setLicenceMode(null); setNotice('Driver licence saved.')
      void api.get<Customer[]>('/customers').then(response => {
        setCustomers(response.data)
        setEditing(response.data.find(customer => customer.id === editedId) ?? null)
      }).catch(() => setError('The licence was saved, but the customer display could not be refreshed.'))
    }} />}
    <Dialog open={Boolean(confirmation)} onClose={() => !saving && setConfirmation(null)} disableScrollLock fullWidth maxWidth="sm" PaperProps={{ sx: { borderRadius: 0 } }}>
      <DialogTitle sx={{ px: 3.75, py: 3, display: 'flex', justifyContent: 'space-between', alignItems: 'center', fontSize: 26, fontWeight: 850 }}>{confirmation?.kind === 'activation' ? 'Send a new activation code' : confirmation?.kind === 'restore' ? 'Restore rental access' : confirmation?.kind === 'remove-licence' ? 'Remove driver licence' : 'Block customer account'}<IconButton aria-label="Close" onClick={() => setConfirmation(null)}><CloseOutlined /></IconButton></DialogTitle>
      <DialogContent dividers sx={{ px: 3.75, py: 4 }}><Typography fontSize={20}>{confirmation?.kind === 'activation' ? 'A new activation code will be sent to:' : confirmation?.kind === 'restore' ? `Restore ${confirmation.customer.name}’s rental access?` : confirmation?.kind === 'remove-licence' ? `Remove ${confirmation.customer.name}’s stored driver licence?` : `Block ${confirmation?.customer.name} from making rental requests?`}</Typography>{confirmation?.kind === 'activation' && <Typography fontSize={19} fontWeight={800} mt={2}>{confirmation.customer.email}</Typography>}<Typography color="text.secondary" fontSize={19} mt={2}>{confirmation?.kind === 'activation' ? 'The previous unused code will stop working. The new code expires after 10 minutes.' : confirmation?.kind === 'remove-licence' ? 'The stored document and confirmed licence details will be deleted. The customer will need to upload the licence again.' : 'This does not delete the customer or their rental history.'}</Typography></DialogContent>
      <DialogActions sx={{ p: 2.5 }}><Button variant="outlined" onClick={() => setConfirmation(null)}>Cancel</Button><Button variant="contained" color={confirmation?.kind === 'remove-licence' ? 'error' : 'primary'} disabled={saving} onClick={() => void confirmCustomerAction()}>{confirmation?.kind === 'activation' ? 'Send a new activation code' : confirmation?.kind === 'restore' ? 'Restore rental access' : confirmation?.kind === 'remove-licence' ? 'Remove licence' : 'Block account'}</Button></DialogActions>
    </Dialog>
    <AdminActivityDialog activity={activity} loading={activityLoading} tab={activityTab} search={activitySearch} page={activityPage} saving={saving} onTab={value => { setActivityTab(value); setActivityPage(1) }} onSearch={setActivitySearch} onLoad={(nextPage, term) => activity && void viewActivity({ id: activity.customer.id } as Customer, nextPage, term)} onSecurity={path => void customerSecurity(path)} onClose={() => setActivity(null)} />
    <Dialog open={false} onClose={() => !activityLoading && setActivity(null)} fullWidth maxWidth="lg">
      <DialogTitle>Customer activity · {activity?.customer.name ?? 'Loading…'}</DialogTitle><DialogContent>
        {activityLoading ? <Box sx={{ minHeight: 240, display: 'grid', placeItems: 'center' }}><CircularProgress /></Box> : activity && <Stack spacing={3} mt={1}>
          <Stack direction={{ xs: 'column', md: 'row' }} gap={1.5} flexWrap="wrap">
            <Chip label={`${activity.summary.bookings} bookings`} /><Chip label={`${activity.summary.invoices} invoices`} /><Chip label={`$${activity.summary.totalBilled.toFixed(2)} billed`} /><Chip color={activity.summary.outstanding > 0 ? 'warning' : 'success'} label={`$${activity.summary.outstanding.toFixed(2)} outstanding`} /><Chip color={(activity.summary.openCases ?? 0) > 0 ? 'warning' : 'default'} label={`${activity.summary.openCases ?? 0} open cases`} />
          </Stack>
          <Card variant="outlined"><CardContent><Typography fontWeight={800}>Online account</Typography><Typography variant="body2" color="text.secondary">{activity.account ? `${activity.account.emailConfirmed ? 'Verified' : 'Activation pending'} · Last activity: ${activity.account.lastActivityAt ? new Date(activity.account.lastActivityAt).toLocaleString('en-FJ') : 'Never'}` : 'No online customer account has been created.'}</Typography><Typography variant="body2" mt={1}>Rental preferences: {activity.customer.hirePreferences?.length ? activity.customer.hirePreferences.map(value => preferenceLabel[value]).join(', ') : 'All rentals'}</Typography></CardContent></Card>
          {activity.account && <Card variant="outlined"><CardContent><Typography fontWeight={800} mb={1.5}>Customer security controls</Typography><Stack direction="row" gap={1} flexWrap="wrap"><Button disabled={saving} onClick={() => void customerSecurity('reset-password')}>Reset password</Button><Button disabled={saving} onClick={() => void customerSecurity('revoke-sessions')}>Revoke sessions</Button><Button disabled={saving} onClick={() => void customerSecurity(activity.account?.lockoutEnd ? 'unlock' : 'lock')}>{activity.account.lockoutEnd ? 'Unlock portal' : 'Lock portal'}</Button>{!activity.account.emailConfirmed && <Button disabled={saving} onClick={() => void customerSecurity('verify-email')}>Verify email</Button>}<Button color={activity.account.isActive ? 'error' : 'success'} disabled={saving} onClick={() => void customerSecurity(activity.account?.isActive ? 'disable' : 'enable')}>{activity.account.isActive ? 'Disable portal' : 'Enable portal'}</Button></Stack><Typography variant="caption" color="text.secondary">Portal access is separate from the customer’s rental eligibility and business status.</Typography></CardContent></Card>}
          <Box><Typography variant="h6" fontWeight={800} mb={1}>Rental history</Typography><TableContainer component={Card} variant="outlined"><Table size="small"><TableHead><TableRow><TableCell>Reference</TableCell><TableCell>Branch</TableCell><TableCell>Status</TableCell><TableCell>Assets</TableCell><TableCell>Created</TableCell></TableRow></TableHead><TableBody>{activity.bookings.map(item => <TableRow key={item.id}><TableCell>{item.bookingNumber}</TableCell><TableCell>{item.branchName}</TableCell><TableCell>{item.status}</TableCell><TableCell>{item.assetCount}</TableCell><TableCell>{new Date(item.createdAt).toLocaleDateString('en-FJ')}</TableCell></TableRow>)}{activity.bookings.length === 0 && <TableRow><TableCell colSpan={5}>No bookings recorded.</TableCell></TableRow>}</TableBody></Table></TableContainer></Box>
          <Box><Typography variant="h6" fontWeight={800} mb={1}>Invoices</Typography><TableContainer component={Card} variant="outlined"><Table size="small"><TableHead><TableRow><TableCell>Invoice</TableCell><TableCell>Status</TableCell><TableCell>Total</TableCell><TableCell>Outstanding</TableCell></TableRow></TableHead><TableBody>{activity.invoices.map(item => <TableRow key={item.id}><TableCell>{item.invoiceNumber}</TableCell><TableCell>{item.status}</TableCell><TableCell>${item.total.toFixed(2)}</TableCell><TableCell>${item.balanceDue.toFixed(2)}</TableCell></TableRow>)}{activity.invoices.length === 0 && <TableRow><TableCell colSpan={4}>No invoices recorded.</TableCell></TableRow>}</TableBody></Table></TableContainer></Box>
          <Box><Typography variant="h6" fontWeight={800} mb={1}>Customer cases</Typography><TableContainer component={Card} variant="outlined"><Table size="small"><TableHead><TableRow><TableCell>Case</TableCell><TableCell>Subject</TableCell><TableCell>Priority</TableCell><TableCell>Status</TableCell></TableRow></TableHead><TableBody>{(activity.cases ?? []).map(item => <TableRow key={item.id}><TableCell>{item.caseNumber}</TableCell><TableCell>{item.subject}</TableCell><TableCell>{item.priority}</TableCell><TableCell>{item.status}</TableCell></TableRow>)}</TableBody></Table></TableContainer></Box>
        </Stack>}
      </DialogContent><DialogActions><Button onClick={() => setActivity(null)}>Close</Button></DialogActions>
    </Dialog>
  </Box>
}
