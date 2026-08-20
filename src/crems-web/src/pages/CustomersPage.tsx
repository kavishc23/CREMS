import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import axios from 'axios'
import AddOutlined from '@mui/icons-material/AddOutlined'
import BlockOutlined from '@mui/icons-material/BlockOutlined'
import BusinessOutlined from '@mui/icons-material/BusinessOutlined'
import CheckCircleOutline from '@mui/icons-material/CheckCircleOutline'
import EditOutlined from '@mui/icons-material/EditOutlined'
import ManageAccountsOutlined from '@mui/icons-material/ManageAccountsOutlined'
import SearchOutlined from '@mui/icons-material/SearchOutlined'
import MarkEmailReadOutlined from '@mui/icons-material/MarkEmailReadOutlined'
import PersonOutlineOutlined from '@mui/icons-material/PersonOutlineOutlined'
import VerifiedUserOutlined from '@mui/icons-material/VerifiedUserOutlined'
import VisibilityOutlined from '@mui/icons-material/VisibilityOutlined'
import WarningAmberOutlined from '@mui/icons-material/WarningAmberOutlined'
import {
  Alert, Avatar, Box, Button, Card, CardContent, Chip, CircularProgress, Dialog,
  DialogActions, DialogContent, DialogTitle, FormControl, IconButton,
  Grid, InputAdornment, InputLabel, MenuItem, Select, Stack, Table, TableBody,
  TableCell, TableContainer, TableHead, TableRow, TextField, Tooltip, Typography,
} from '@mui/material'
import { api } from '../api/client'

const customerTypes = ['Individual', 'Business'] as const
type CustomerType = typeof customerTypes[number]
type Customer = {
  id: string; customerNumber: string; type: CustomerType; name: string
  email: string | null; phone: string | null; address: string | null
  identificationNumber: string | null; isBlocked: boolean; isActive: boolean
  hasOnlineAccount: boolean; emailConfirmed: boolean
}
type CustomerForm = {
  customerNumber: string; type: CustomerType; name: string; email: string
  phone: string; address: string; identificationNumber: string
}
type CustomerActivity = {
  customer: { id: string; customerNumber: string; name: string; type: string; email: string | null; phone: string | null }
  account: { emailConfirmed: boolean; isActive: boolean; lastLoginAt: string | null; lastActivityAt: string | null; lockoutEnd: string | null } | null
  summary: { bookings: number; invoices: number; totalBilled: number; outstanding: number; openCases: number }
  bookings: { id: string; bookingNumber: string; status: string; createdAt: string; branchName: string; assetCount: number }[]
  invoices: { id: string; invoiceNumber: string; status: string; total: number; balanceDue: number; issuedAt: string }[]
  cases: { id: string; caseNumber: string; type: string; priority: string; subject: string; status: string; createdAt: string }[]
}
const emptyForm: CustomerForm = {
  customerNumber: '', type: 'Individual', name: '', email: '', phone: '', address: '', identificationNumber: '',
}
const customerStatusFilters = ['All', 'Active', 'Online', 'Blocked'] as const
type CustomerStatusFilter = typeof customerStatusFilters[number]

function formatMoney(value: number) {
  return `FJD ${value.toLocaleString('en-FJ', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`
}

function initials(name: string) {
  return name.split(/\s+/).filter(Boolean).slice(0, 2).map((part) => part[0]?.toUpperCase()).join('') || 'C'
}

export function CustomersPage({ administrationView = false }: { administrationView?: boolean }) {
  const [customers, setCustomers] = useState<Customer[]>([])
  const [loading, setLoading] = useState(true)
  const [open, setOpen] = useState(false)
  const [saving, setSaving] = useState(false)
  const [editing, setEditing] = useState<Customer | null>(null)
  const [form, setForm] = useState<CustomerForm>(emptyForm)
  const [search, setSearch] = useState('')
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [activity, setActivity] = useState<CustomerActivity | null>(null)
  const [activityLoading, setActivityLoading] = useState(false)
  const [statusFilter, setStatusFilter] = useState<CustomerStatusFilter>('All')

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
      const matchesSearch = term ? [customer.customerNumber, customer.name, customer.email, customer.phone, customer.identificationNumber]
        .some((value) => value?.toLowerCase().includes(term)) : true
      const matchesStatus = statusFilter === 'All'
        || (statusFilter === 'Active' && customer.isActive && !customer.isBlocked)
        || (statusFilter === 'Online' && customer.hasOnlineAccount)
        || (statusFilter === 'Blocked' && customer.isBlocked)
      return matchesSearch && matchesStatus
    })
  }, [customers, search, statusFilter])

  const summary = useMemo(() => ({
    total: customers.length,
    active: customers.filter((customer) => customer.isActive && !customer.isBlocked).length,
    online: customers.filter((customer) => customer.hasOnlineAccount).length,
    blocked: customers.filter((customer) => customer.isBlocked).length,
  }), [customers])

  function openCreate() { setEditing(null); setForm(emptyForm); setError(''); setOpen(true) }
  function openEdit(customer: Customer) {
    setEditing(customer)
    setForm({ customerNumber: customer.customerNumber, type: customer.type, name: customer.name,
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

  return <Box sx={{ p: { xs: 2, sm: 3, lg: 4 }, maxWidth: 1500, mx: 'auto' }}>
    <Stack direction={{ xs: 'column', lg: 'row' }} justifyContent="space-between" alignItems={{ lg: 'flex-end' }} gap={2.5} mb={3}>
      <Box>
        <Typography variant="overline" color="text.secondary" fontWeight={850} letterSpacing={1.1}>Customer register</Typography>
        <Typography variant="h4" fontWeight={850}>{administrationView ? 'Customer accounts' : 'Customers'}</Typography>
        <Typography color="text.secondary" mt={0.75} maxWidth={720}>{administrationView ? 'Manage customer identities, portal access and account activity separately from staff.' : 'Maintain customer details, rental eligibility and secure online access from one tidy workspace.'}</Typography>
      </Box>
      <Button variant="contained" startIcon={<AddOutlined />} onClick={openCreate} sx={{ alignSelf: { xs: 'stretch', sm: 'flex-start', lg: 'center' } }}>Add customer</Button>
    </Stack>
    {error && !open && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
    {notice && <Alert severity="success" sx={{ mb: 2 }}>{notice}</Alert>}

    <Grid container spacing={2.5} mb={2.5}>
      <Grid size={{ xs: 12, sm: 6, lg: 3 }}><SummaryCard label="Total customers" value={summary.total} helper="Registered customer records" icon={<ManageAccountsOutlined />} tone="#111111" /></Grid>
      <Grid size={{ xs: 12, sm: 6, lg: 3 }}><SummaryCard label="Rental eligible" value={summary.active} helper="Active and not blocked" icon={<CheckCircleOutline />} tone="#2e7d32" /></Grid>
      <Grid size={{ xs: 12, sm: 6, lg: 3 }}><SummaryCard label="Online access" value={summary.online} helper="Portal account enabled" icon={<VerifiedUserOutlined />} tone="#dfcf00" /></Grid>
      <Grid size={{ xs: 12, sm: 6, lg: 3 }}><SummaryCard label="Blocked" value={summary.blocked} helper="Restricted from rentals" icon={<WarningAmberOutlined />} tone="#d14343" /></Grid>
    </Grid>

    <Card variant="outlined" sx={{ overflow: 'hidden' }}><CardContent sx={{ p: 0 }}>
      <Stack direction={{ xs: 'column', lg: 'row' }} justifyContent="space-between" alignItems={{ lg: 'center' }} gap={2} sx={{ p: 2.25, borderBottom: 1, borderColor: 'divider', bgcolor: 'background.paper' }}>
        <TextField size="small" placeholder="Search name, customer number, email or ID" value={search}
          onChange={(event) => setSearch(event.target.value)} sx={{ width: { xs: '100%', lg: 440 } }}
          InputProps={{ startAdornment: <InputAdornment position="start"><SearchOutlined /></InputAdornment> }} />
        <Stack direction="row" gap={1} flexWrap="wrap" alignItems="center">
          {customerStatusFilters.map((filter) => <Chip key={filter} label={filter} clickable color={statusFilter === filter ? 'primary' : 'default'} variant={statusFilter === filter ? 'filled' : 'outlined'} onClick={() => setStatusFilter(filter)} sx={{ fontWeight: 750 }} />)}
        </Stack>
      </Stack>
      <Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" alignItems={{ sm: 'center' }} sx={{ px: 2.25, py: 1.5, bgcolor: '#fbfbf8', borderBottom: 1, borderColor: 'divider' }} gap={1}>
        <Typography variant="body2" color="text.secondary"><strong>{visibleCustomers.length}</strong> of {customers.length} customers shown</Typography>
        <Typography variant="caption" color="text.secondary">Search before creating a new customer to avoid duplicate accounts.</Typography>
      </Stack>
      {loading ? <Box sx={{ minHeight: 280, display: 'grid', placeItems: 'center' }}><CircularProgress /></Box> :
        <TableContainer><Table><TableHead><TableRow>
          <TableCell>Customer</TableCell><TableCell>Type</TableCell><TableCell>Contact</TableCell>
          <TableCell>Identification</TableCell><TableCell>Status</TableCell><TableCell align="right">Actions</TableCell>
        </TableRow></TableHead><TableBody>
          {visibleCustomers.length === 0 && <TableRow><TableCell colSpan={6} align="center" sx={{ py: 8, color: 'text.secondary' }}>No matching customers found.</TableCell></TableRow>}
          {visibleCustomers.map((customer) => <TableRow key={customer.id} hover sx={{ '&:last-child td': { borderBottom: 0 } }}>
            <TableCell><Stack direction="row" alignItems="center" gap={1.5}><Avatar sx={{ width: 42, height: 42, bgcolor: customer.type === 'Business' ? '#111' : 'secondary.main', color: customer.type === 'Business' ? 'white' : '#111', fontWeight: 850 }}>{initials(customer.name)}</Avatar><Box><Typography fontWeight={800}>{customer.name}</Typography><Typography variant="body2" color="text.secondary">{customer.customerNumber}</Typography></Box></Stack></TableCell>
            <TableCell><Chip size="small" icon={customer.type === 'Business' ? <BusinessOutlined /> : <PersonOutlineOutlined />} label={customer.type} variant="outlined" /></TableCell>
            <TableCell><Typography variant="body2">{customer.email || '—'}</Typography><Typography variant="body2" color="text.secondary">{customer.phone || '—'}</Typography></TableCell>
            <TableCell>{customer.identificationNumber || '—'}</TableCell>
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
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
            <TextField fullWidth required label="Customer number" helperText="For example CUS-0001" inputProps={{ maxLength: 50 }} value={form.customerNumber} onChange={(e) => setForm({ ...form, customerNumber: e.target.value })} />
            <FormControl fullWidth><InputLabel>Customer type</InputLabel><Select label="Customer type" value={form.type} onChange={(e) => setForm({ ...form, type: e.target.value as CustomerType })}>{customerTypes.map((value) => <MenuItem key={value} value={value}>{value}</MenuItem>)}</Select></FormControl>
          </Stack>
          <TextField required label={form.type === 'Business' ? 'Business name' : 'Full name'} value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} />
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
            <TextField fullWidth type="email" label="Email" value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })} />
            <TextField fullWidth label="Phone" value={form.phone} onChange={(e) => setForm({ ...form, phone: e.target.value })} />
          </Stack>
          <TextField label={form.type === 'Business' ? 'Registration / TIN' : 'Driver licence / ID number'} value={form.identificationNumber} onChange={(e) => setForm({ ...form, identificationNumber: e.target.value })} />
          <TextField label="Address" multiline minRows={2} value={form.address} onChange={(e) => setForm({ ...form, address: e.target.value })} />
        </Stack>
      </DialogContent><DialogActions sx={{ p: 3, pt: 1 }}><Button onClick={() => setOpen(false)} disabled={saving}>Cancel</Button><Button type="submit" variant="contained" disabled={saving}>{saving ? 'Saving…' : 'Save customer'}</Button></DialogActions></Box>
    </Dialog>
    <Dialog open={Boolean(activity) || activityLoading} onClose={() => !activityLoading && setActivity(null)} fullWidth maxWidth="lg">
      <DialogTitle sx={{ pb: 1 }}>{activity ? <Stack direction="row" gap={1.5} alignItems="center"><Avatar sx={{ bgcolor: 'secondary.main', color: '#111', fontWeight: 850 }}>{initials(activity.customer.name)}</Avatar><Box><Typography variant="h6" fontWeight={850}>{activity.customer.name}</Typography><Typography variant="body2" color="text.secondary">{activity.customer.customerNumber} · {activity.customer.type}</Typography></Box></Stack> : 'Customer activity'}</DialogTitle><DialogContent dividers>
        {activityLoading ? <Box sx={{ minHeight: 240, display: 'grid', placeItems: 'center' }}><CircularProgress /></Box> : activity && <Stack spacing={3} mt={1}>
          <Grid container spacing={1.5}>
            <Grid size={{ xs: 12, sm: 6, md: 2.4 }}><ActivityMetric label="Bookings" value={activity.summary.bookings} /></Grid>
            <Grid size={{ xs: 12, sm: 6, md: 2.4 }}><ActivityMetric label="Invoices" value={activity.summary.invoices} /></Grid>
            <Grid size={{ xs: 12, sm: 6, md: 2.4 }}><ActivityMetric label="Billed" value={formatMoney(activity.summary.totalBilled)} /></Grid>
            <Grid size={{ xs: 12, sm: 6, md: 2.4 }}><ActivityMetric label="Outstanding" value={formatMoney(activity.summary.outstanding)} tone={activity.summary.outstanding > 0 ? 'warning.main' : 'success.main'} /></Grid>
            <Grid size={{ xs: 12, sm: 6, md: 2.4 }}><ActivityMetric label="Open cases" value={activity.summary.openCases} tone={activity.summary.openCases > 0 ? 'warning.main' : 'text.primary'} /></Grid>
          </Grid>
          <Card variant="outlined"><CardContent sx={{ p: 2.5 }}><Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" gap={2}><Box><Typography fontWeight={850}>Online account</Typography><Typography variant="body2" color="text.secondary" mt={0.5}>{activity.account ? `${activity.account.emailConfirmed ? 'Verified' : 'Activation pending'} · Last activity: ${activity.account.lastActivityAt ? new Date(activity.account.lastActivityAt).toLocaleString('en-FJ') : 'Never'}` : 'No online customer account has been created.'}</Typography></Box>{activity.account && <Chip color={activity.account.isActive ? 'success' : 'default'} variant="outlined" label={activity.account.isActive ? 'Portal active' : 'Portal disabled'} />}</Stack></CardContent></Card>
          {activity.account && <Card variant="outlined"><CardContent sx={{ p: 2.5 }}><Typography fontWeight={850} mb={1.5}>Customer security controls</Typography><Stack direction="row" gap={1} flexWrap="wrap"><Button disabled={saving} onClick={() => void customerSecurity('reset-password')}>Reset password</Button><Button disabled={saving} onClick={() => void customerSecurity('revoke-sessions')}>Revoke sessions</Button><Button disabled={saving} onClick={() => void customerSecurity(activity.account?.lockoutEnd ? 'unlock' : 'lock')}>{activity.account.lockoutEnd ? 'Unlock portal' : 'Lock portal'}</Button>{!activity.account.emailConfirmed && <Button disabled={saving} onClick={() => void customerSecurity('verify-email')}>Verify email</Button>}<Button color={activity.account.isActive ? 'error' : 'success'} disabled={saving} onClick={() => void customerSecurity(activity.account?.isActive ? 'disable' : 'enable')}>{activity.account.isActive ? 'Disable portal' : 'Enable portal'}</Button></Stack><Typography variant="caption" color="text.secondary" display="block" mt={1.25}>Portal access is separate from the customer’s rental eligibility and business status.</Typography></CardContent></Card>}
          <Box><Typography variant="h6" fontWeight={800} mb={1}>Rental history</Typography><TableContainer component={Card} variant="outlined"><Table size="small"><TableHead><TableRow><TableCell>Reference</TableCell><TableCell>Branch</TableCell><TableCell>Status</TableCell><TableCell>Assets</TableCell><TableCell>Created</TableCell></TableRow></TableHead><TableBody>{activity.bookings.map(item => <TableRow key={item.id}><TableCell>{item.bookingNumber}</TableCell><TableCell>{item.branchName}</TableCell><TableCell>{item.status}</TableCell><TableCell>{item.assetCount}</TableCell><TableCell>{new Date(item.createdAt).toLocaleDateString('en-FJ')}</TableCell></TableRow>)}{activity.bookings.length === 0 && <TableRow><TableCell colSpan={5}>No bookings recorded.</TableCell></TableRow>}</TableBody></Table></TableContainer></Box>
          <Box><Typography variant="h6" fontWeight={800} mb={1}>Invoices</Typography><TableContainer component={Card} variant="outlined"><Table size="small"><TableHead><TableRow><TableCell>Invoice</TableCell><TableCell>Status</TableCell><TableCell>Total</TableCell><TableCell>Outstanding</TableCell></TableRow></TableHead><TableBody>{activity.invoices.map(item => <TableRow key={item.id}><TableCell>{item.invoiceNumber}</TableCell><TableCell>{item.status}</TableCell><TableCell>{formatMoney(item.total)}</TableCell><TableCell>{formatMoney(item.balanceDue)}</TableCell></TableRow>)}{activity.invoices.length === 0 && <TableRow><TableCell colSpan={4}>No invoices recorded.</TableCell></TableRow>}</TableBody></Table></TableContainer></Box>
          <Box><Typography variant="h6" fontWeight={800} mb={1}>Customer cases</Typography><TableContainer component={Card} variant="outlined"><Table size="small"><TableHead><TableRow><TableCell>Case</TableCell><TableCell>Subject</TableCell><TableCell>Priority</TableCell><TableCell>Status</TableCell></TableRow></TableHead><TableBody>{activity.cases.map(item => <TableRow key={item.id}><TableCell>{item.caseNumber}</TableCell><TableCell>{item.subject}</TableCell><TableCell>{item.priority}</TableCell><TableCell>{item.status}</TableCell></TableRow>)}{activity.cases.length === 0 && <TableRow><TableCell colSpan={4}>No cases recorded.</TableCell></TableRow>}</TableBody></Table></TableContainer></Box>
        </Stack>}
      </DialogContent><DialogActions><Button onClick={() => setActivity(null)}>Close</Button></DialogActions>
    </Dialog>
  </Box>
}

function SummaryCard({ label, value, helper, icon, tone }: { label: string; value: number; helper: string; icon: React.ReactNode; tone: string }) {
  return <Card variant="outlined" sx={{ height: '100%', borderTop: 4, borderTopColor: tone }}>
    <CardContent sx={{ p: 2.5 }}>
      <Stack direction="row" justifyContent="space-between" gap={2}>
        <Box><Typography variant="body2" color="text.secondary">{label}</Typography><Typography variant="h4" fontWeight={850} mt={0.5}>{value}</Typography><Typography variant="caption" color="text.secondary">{helper}</Typography></Box>
        <Avatar variant="rounded" sx={{ bgcolor: tone, color: tone === '#dfcf00' ? '#111' : '#fff', width: 44, height: 44 }}>{icon}</Avatar>
      </Stack>
    </CardContent>
  </Card>
}

function ActivityMetric({ label, value, tone = 'text.primary' }: { label: string; value: string | number; tone?: string }) {
  return <Box sx={{ p: 2, border: 1, borderColor: 'divider', borderRadius: 2, bgcolor: '#fbfbf8' }}>
    <Typography variant="caption" color="text.secondary">{label}</Typography>
    <Typography variant="h6" fontWeight={850} color={tone}>{value}</Typography>
  </Box>
}
