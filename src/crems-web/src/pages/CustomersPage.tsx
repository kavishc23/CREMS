import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import axios from 'axios'
import AddOutlined from '@mui/icons-material/AddOutlined'
import BlockOutlined from '@mui/icons-material/BlockOutlined'
import CheckCircleOutline from '@mui/icons-material/CheckCircleOutline'
import EditOutlined from '@mui/icons-material/EditOutlined'
import SearchOutlined from '@mui/icons-material/SearchOutlined'
import MarkEmailReadOutlined from '@mui/icons-material/MarkEmailReadOutlined'
import VisibilityOutlined from '@mui/icons-material/VisibilityOutlined'
import {
  Alert, Box, Button, Card, CardContent, Chip, CircularProgress, Dialog,
  DialogActions, DialogContent, DialogTitle, FormControl, IconButton,
  InputAdornment, InputLabel, MenuItem, Select, Stack, Table, TableBody,
  TableCell, TableContainer, TableHead, TableRow, TextField, Tooltip, Typography,
  Grid,
} from '@mui/material'
import { api } from '../api/client'

type Customer = {
  id: string; customerNumber: string; name: string
  email: string | null; phone: string | null; address: string | null
  identificationNumber: string | null; isBlocked: boolean; isActive: boolean
  hirePreferences: HirePreference[]
  hirePreference?: HirePreference | 'NoPreference'
  hasOnlineAccount: boolean; emailConfirmed: boolean
  driverLicenceDocumentId: string | null; driverLicenceFileName: string | null
}
type CustomerForm = {
  customerNumber: string; name: string; email: string
  phone: string; address: string; identificationNumber: string
}
type CustomerActivity = {
  customer: { id: string; customerNumber: string; name: string; email: string | null; phone: string | null; hirePreferences: HirePreference[] }
  account: { emailConfirmed: boolean; isActive: boolean; lastLoginAt: string | null; lastActivityAt: string | null; lockoutEnd: string | null } | null
  summary: { bookings: number; invoices: number; totalBilled: number; outstanding: number; openCases: number }
  bookings: { id: string; bookingNumber: string; status: string; createdAt: string; branchName: string; assetCount: number }[]
  invoices: { id: string; invoiceNumber: string; status: string; total: number; balanceDue: number; issuedAt: string }[]
  cases: { id: string; caseNumber: string; type: string; priority: string; subject: string; status: string; createdAt: string }[]
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
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [activity, setActivity] = useState<CustomerActivity | null>(null)
  const [activityLoading, setActivityLoading] = useState(false)

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

  const customerSummary = useMemo(() => [
    { label: 'Total customers', value: customers.length, color: 'text.primary' },
    { label: 'Online verified', value: customers.filter((item) => item.emailConfirmed).length, color: 'success.main' },
    { label: 'Licence on file', value: customers.filter((item) => item.driverLicenceDocumentId).length, color: 'info.main' },
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
  async function viewActivity(customer: Customer) {
    setActivityLoading(true); setError('')
    try { setActivity((await api.get<CustomerActivity>(`/customers/${customer.id}/activity`)).data) }
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
  async function viewLicence(customer: Customer) {
    if (!customer.driverLicenceDocumentId) return
    try {
      const response = await api.get(`/customers/${customer.id}/driver-licence/${customer.driverLicenceDocumentId}`, { responseType: 'blob' })
      const url = URL.createObjectURL(response.data); window.open(url, '_blank', 'noopener,noreferrer'); setTimeout(() => URL.revokeObjectURL(url), 60_000)
    } catch { setError('Unable to open the driver licence file.') }
  }

  return <Box sx={{ p: { xs: 2, sm: 3, lg: 4 }, maxWidth: 1500, mx: 'auto' }}>
    <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" gap={2} mb={3}>
      <Box><Typography variant="h4" fontWeight={750}>{administrationView ? 'Customer accounts' : 'Customers'}</Typography>
        <Typography color="text.secondary" mt={0.5}>{administrationView ? 'Manage customer identities, portal access and account activity separately from staff.' : 'Maintain customer details and rental eligibility.'}</Typography></Box>
      <Button variant="contained" startIcon={<AddOutlined />} onClick={openCreate}>Add customer</Button>
    </Stack>
    {error && !open && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
    {notice && <Alert severity="success" sx={{ mb: 2 }}>{notice}</Alert>}
    <Grid container spacing={2} mb={2.5}>
      {customerSummary.map((item) => <Grid key={item.label} size={{ xs: 6, md: 4, lg: 2.4 }}><Card variant="outlined" sx={{ height: '100%' }}><CardContent sx={{ py: 2 }}><Typography variant="h4" fontWeight={800} color={item.color}>{item.value}</Typography><Typography variant="caption" color="text.secondary" fontWeight={700} textTransform="uppercase" letterSpacing={.6}>{item.label}</Typography></CardContent></Card></Grid>)}
    </Grid>
    <Card variant="outlined"><CardContent sx={{ p: 0 }}>
      <Stack direction={{ xs: 'column', lg: 'row' }} gap={1.5} sx={{ p: 2, borderBottom: 1, borderColor: 'divider' }}>
        <TextField size="small" placeholder="Search by name, customer number, email or phone" value={search}
          onChange={(event) => setSearch(event.target.value)} sx={{ flex: 1, minWidth: 260 }}
          InputProps={{ startAdornment: <InputAdornment position="start"><SearchOutlined /></InputAdornment> }} />
        <FormControl size="small" sx={{ minWidth: 170 }}><InputLabel>Account status</InputLabel><Select label="Account status" value={statusFilter} onChange={(event) => setStatusFilter(event.target.value as typeof statusFilter)}>{['All', 'Verified', 'Pending', 'Blocked'].map((value) => <MenuItem key={value} value={value}>{value}</MenuItem>)}</Select></FormControl>
      </Stack>
      {loading ? <Box sx={{ minHeight: 280, display: 'grid', placeItems: 'center' }}><CircularProgress /></Box> :
        <TableContainer><Table><TableHead><TableRow sx={{ bgcolor: '#f6f6f3' }}>
          <TableCell>Customer</TableCell><TableCell>Contact</TableCell>
          <TableCell>Identification</TableCell><TableCell>Status</TableCell><TableCell align="right">Actions</TableCell>
        </TableRow></TableHead><TableBody>
          {visibleCustomers.length === 0 && <TableRow><TableCell colSpan={5} align="center" sx={{ py: 8, color: 'text.secondary' }}>No matching customers found.</TableCell></TableRow>}
          {visibleCustomers.map((customer) => <TableRow key={customer.id} hover>
            <TableCell><Typography fontWeight={700}>{customer.name}</Typography><Typography variant="body2" color="text.secondary">{customer.customerNumber}</Typography></TableCell>
            <TableCell><Typography variant="body2">{customer.email || '—'}</Typography><Typography variant="body2" color="text.secondary">{customer.phone || '—'}</Typography></TableCell>
            <TableCell><Typography variant="body2">{customer.identificationNumber || '—'}</Typography>{customer.driverLicenceDocumentId && <Button size="small" sx={{ px: 0, minWidth: 0 }} onClick={() => void viewLicence(customer)}>View licence</Button>}<Stack direction="row" gap={.5} flexWrap="wrap" mt={.5}>{customerPreferences(customer).map(value => <Chip key={value} size="small" label={preferenceLabel[value]} />)}{customerPreferences(customer).length === 0 && <Typography variant="caption" color="text.secondary">All rentals</Typography>}</Stack></TableCell>
            <TableCell><Stack direction="row" gap={0.75} flexWrap="wrap">
              <Chip size="small" label={customer.isActive ? 'Active' : 'Inactive'} color={customer.isActive ? 'success' : 'default'} variant="outlined" />
              {customer.isBlocked && <Chip size="small" label="Blocked" color="error" variant="outlined" />}
              <Chip size="small" label={customer.emailConfirmed ? 'Online verified' : customer.hasOnlineAccount ? 'Invite pending' : 'Offline only'} color={customer.emailConfirmed ? 'success' : 'default'} variant="outlined" />
            </Stack></TableCell>
            <TableCell align="right"><Tooltip title="View customer activity"><IconButton onClick={() => void viewActivity(customer)}><VisibilityOutlined /></IconButton></Tooltip><Tooltip title="Edit customer"><IconButton onClick={() => openEdit(customer)}><EditOutlined /></IconButton></Tooltip>
              {!customer.emailConfirmed && <Tooltip title={customer.hasOnlineAccount ? 'Send a new activation code' : 'Enable online access'}><span><IconButton color="primary" disabled={!customer.email || !customer.isActive || customer.isBlocked} onClick={() => void enableOnlineAccess(customer)}><MarkEmailReadOutlined /></IconButton></span></Tooltip>}
              <Tooltip title={customer.isBlocked ? 'Restore rental access' : 'Block from rentals'}><IconButton color={customer.isBlocked ? 'success' : 'error'} onClick={() => void setStatus(customer, { isBlocked: !customer.isBlocked })}>{customer.isBlocked ? <CheckCircleOutline /> : <BlockOutlined />}</IconButton></Tooltip>
            </TableCell>
          </TableRow>)}
        </TableBody></Table></TableContainer>}
    </CardContent></Card>
    <Dialog open={open} onClose={() => !saving && setOpen(false)} fullWidth maxWidth="md">
      <Box component="form" onSubmit={save}><DialogTitle>{editing ? 'Edit customer' : 'Add customer'}</DialogTitle><DialogContent>
        <Stack spacing={2.25} mt={1}>{error && <Alert severity="error">{error}</Alert>}
          <TextField fullWidth required label="Customer number" helperText="CREMS uses one customer-number sequence." inputProps={{ maxLength: 50, pattern: 'CUS-[0-9]{6}' }} value={form.customerNumber} onChange={(e) => setForm({ ...form, customerNumber: e.target.value.toUpperCase() })} />
          <TextField required label="Full name" value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} />
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
            <TextField fullWidth type="email" label="Email" value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })} />
            <TextField fullWidth label="Phone" value={form.phone} onChange={(e) => setForm({ ...form, phone: e.target.value })} />
          </Stack>
          <TextField label="Driver licence / identification number" value={form.identificationNumber} onChange={(e) => setForm({ ...form, identificationNumber: e.target.value })} />
          {editing && <Card variant="outlined"><CardContent><Typography fontWeight={750}>Driver licence file</Typography><Typography variant="body2" color="text.secondary" mb={1.5}>{editing.driverLicenceFileName ?? 'No driver licence file uploaded.'}</Typography><Stack direction="row" gap={1}>{editing.driverLicenceDocumentId && <Button onClick={() => void viewLicence(editing)}>View current</Button>}<Button component="label" variant="outlined">{editing.driverLicenceDocumentId ? 'Replace file' : 'Upload file'}<input hidden type="file" accept="application/pdf,image/jpeg,image/png" onChange={event => { const file = event.target.files?.[0]; event.target.value = ''; void uploadLicence(file) }} /></Button></Stack></CardContent></Card>}
          <TextField label="Address" multiline minRows={2} value={form.address} onChange={(e) => setForm({ ...form, address: e.target.value })} />
        </Stack>
      </DialogContent><DialogActions sx={{ p: 3, pt: 1 }}><Button onClick={() => setOpen(false)} disabled={saving}>Cancel</Button><Button type="submit" variant="contained" disabled={saving}>{saving ? 'Saving…' : 'Save customer'}</Button></DialogActions></Box>
    </Dialog>
    <Dialog open={Boolean(activity) || activityLoading} onClose={() => !activityLoading && setActivity(null)} fullWidth maxWidth="lg">
      <DialogTitle>Customer activity · {activity?.customer.name ?? 'Loading…'}</DialogTitle><DialogContent>
        {activityLoading ? <Box sx={{ minHeight: 240, display: 'grid', placeItems: 'center' }}><CircularProgress /></Box> : activity && <Stack spacing={3} mt={1}>
          <Stack direction={{ xs: 'column', md: 'row' }} gap={1.5} flexWrap="wrap">
            <Chip label={`${activity.summary.bookings} bookings`} /><Chip label={`${activity.summary.invoices} invoices`} /><Chip label={`$${activity.summary.totalBilled.toFixed(2)} billed`} /><Chip color={activity.summary.outstanding > 0 ? 'warning' : 'success'} label={`$${activity.summary.outstanding.toFixed(2)} outstanding`} /><Chip color={activity.summary.openCases > 0 ? 'warning' : 'default'} label={`${activity.summary.openCases} open cases`} />
          </Stack>
          <Card variant="outlined"><CardContent><Typography fontWeight={800}>Online account</Typography><Typography variant="body2" color="text.secondary">{activity.account ? `${activity.account.emailConfirmed ? 'Verified' : 'Activation pending'} · Last activity: ${activity.account.lastActivityAt ? new Date(activity.account.lastActivityAt).toLocaleString('en-FJ') : 'Never'}` : 'No online customer account has been created.'}</Typography><Typography variant="body2" mt={1}>Rental preferences: {activity.customer.hirePreferences?.length ? activity.customer.hirePreferences.map(value => preferenceLabel[value]).join(', ') : 'All rentals'}</Typography></CardContent></Card>
          {activity.account && <Card variant="outlined"><CardContent><Typography fontWeight={800} mb={1.5}>Customer security controls</Typography><Stack direction="row" gap={1} flexWrap="wrap"><Button disabled={saving} onClick={() => void customerSecurity('reset-password')}>Reset password</Button><Button disabled={saving} onClick={() => void customerSecurity('revoke-sessions')}>Revoke sessions</Button><Button disabled={saving} onClick={() => void customerSecurity(activity.account?.lockoutEnd ? 'unlock' : 'lock')}>{activity.account.lockoutEnd ? 'Unlock portal' : 'Lock portal'}</Button>{!activity.account.emailConfirmed && <Button disabled={saving} onClick={() => void customerSecurity('verify-email')}>Verify email</Button>}<Button color={activity.account.isActive ? 'error' : 'success'} disabled={saving} onClick={() => void customerSecurity(activity.account?.isActive ? 'disable' : 'enable')}>{activity.account.isActive ? 'Disable portal' : 'Enable portal'}</Button></Stack><Typography variant="caption" color="text.secondary">Portal access is separate from the customer’s rental eligibility and business status.</Typography></CardContent></Card>}
          <Box><Typography variant="h6" fontWeight={800} mb={1}>Rental history</Typography><TableContainer component={Card} variant="outlined"><Table size="small"><TableHead><TableRow><TableCell>Reference</TableCell><TableCell>Branch</TableCell><TableCell>Status</TableCell><TableCell>Assets</TableCell><TableCell>Created</TableCell></TableRow></TableHead><TableBody>{activity.bookings.map(item => <TableRow key={item.id}><TableCell>{item.bookingNumber}</TableCell><TableCell>{item.branchName}</TableCell><TableCell>{item.status}</TableCell><TableCell>{item.assetCount}</TableCell><TableCell>{new Date(item.createdAt).toLocaleDateString('en-FJ')}</TableCell></TableRow>)}{activity.bookings.length === 0 && <TableRow><TableCell colSpan={5}>No bookings recorded.</TableCell></TableRow>}</TableBody></Table></TableContainer></Box>
          <Box><Typography variant="h6" fontWeight={800} mb={1}>Invoices</Typography><TableContainer component={Card} variant="outlined"><Table size="small"><TableHead><TableRow><TableCell>Invoice</TableCell><TableCell>Status</TableCell><TableCell>Total</TableCell><TableCell>Outstanding</TableCell></TableRow></TableHead><TableBody>{activity.invoices.map(item => <TableRow key={item.id}><TableCell>{item.invoiceNumber}</TableCell><TableCell>{item.status}</TableCell><TableCell>${item.total.toFixed(2)}</TableCell><TableCell>${item.balanceDue.toFixed(2)}</TableCell></TableRow>)}{activity.invoices.length === 0 && <TableRow><TableCell colSpan={4}>No invoices recorded.</TableCell></TableRow>}</TableBody></Table></TableContainer></Box>
          <Box><Typography variant="h6" fontWeight={800} mb={1}>Customer cases</Typography><TableContainer component={Card} variant="outlined"><Table size="small"><TableHead><TableRow><TableCell>Case</TableCell><TableCell>Subject</TableCell><TableCell>Priority</TableCell><TableCell>Status</TableCell></TableRow></TableHead><TableBody>{activity.cases.map(item => <TableRow key={item.id}><TableCell>{item.caseNumber}</TableCell><TableCell>{item.subject}</TableCell><TableCell>{item.priority}</TableCell><TableCell>{item.status}</TableCell></TableRow>)}{activity.cases.length === 0 && <TableRow><TableCell colSpan={4}>No cases recorded.</TableCell></TableRow>}</TableBody></Table></TableContainer></Box>
        </Stack>}
      </DialogContent><DialogActions><Button onClick={() => setActivity(null)}>Close</Button></DialogActions>
    </Dialog>
  </Box>
}
