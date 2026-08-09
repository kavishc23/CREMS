import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import axios from 'axios'
import AddOutlined from '@mui/icons-material/AddOutlined'
import BlockOutlined from '@mui/icons-material/BlockOutlined'
import CheckCircleOutline from '@mui/icons-material/CheckCircleOutline'
import EditOutlined from '@mui/icons-material/EditOutlined'
import SearchOutlined from '@mui/icons-material/SearchOutlined'
import MarkEmailReadOutlined from '@mui/icons-material/MarkEmailReadOutlined'
import {
  Alert, Box, Button, Card, CardContent, Chip, CircularProgress, Dialog,
  DialogActions, DialogContent, DialogTitle, FormControl, IconButton,
  InputAdornment, InputLabel, MenuItem, Select, Stack, Table, TableBody,
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
const emptyForm: CustomerForm = {
  customerNumber: '', type: 'Individual', name: '', email: '', phone: '', address: '', identificationNumber: '',
}

export function CustomersPage() {
  const [customers, setCustomers] = useState<Customer[]>([])
  const [loading, setLoading] = useState(true)
  const [open, setOpen] = useState(false)
  const [saving, setSaving] = useState(false)
  const [editing, setEditing] = useState<Customer | null>(null)
  const [form, setForm] = useState<CustomerForm>(emptyForm)
  const [search, setSearch] = useState('')
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')

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
    return term ? customers.filter((customer) => [customer.customerNumber, customer.name, customer.email, customer.phone, customer.identificationNumber]
      .some((value) => value?.toLowerCase().includes(term))) : customers
  }, [customers, search])

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

  return <Box sx={{ p: { xs: 2, sm: 3, lg: 4 }, maxWidth: 1500, mx: 'auto' }}>
    <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" gap={2} mb={3}>
      <Box><Typography variant="h4" fontWeight={750}>Customers</Typography>
        <Typography color="text.secondary" mt={0.5}>Maintain customer details and rental eligibility.</Typography></Box>
      <Button variant="contained" startIcon={<AddOutlined />} onClick={openCreate}>Add customer</Button>
    </Stack>
    {error && !open && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
    {notice && <Alert severity="success" sx={{ mb: 2 }}>{notice}</Alert>}
    <Card variant="outlined"><CardContent sx={{ p: 0 }}>
      <Box sx={{ p: 2, borderBottom: 1, borderColor: 'divider' }}><TextField size="small" placeholder="Search customers" value={search}
        onChange={(event) => setSearch(event.target.value)} sx={{ width: { xs: '100%', sm: 380 } }}
        InputProps={{ startAdornment: <InputAdornment position="start"><SearchOutlined /></InputAdornment> }} /></Box>
      {loading ? <Box sx={{ minHeight: 280, display: 'grid', placeItems: 'center' }}><CircularProgress /></Box> :
        <TableContainer><Table><TableHead><TableRow sx={{ bgcolor: '#f6f6f3' }}>
          <TableCell>Customer</TableCell><TableCell>Type</TableCell><TableCell>Contact</TableCell>
          <TableCell>Identification</TableCell><TableCell>Status</TableCell><TableCell align="right">Actions</TableCell>
        </TableRow></TableHead><TableBody>
          {visibleCustomers.length === 0 && <TableRow><TableCell colSpan={6} align="center" sx={{ py: 8, color: 'text.secondary' }}>No matching customers found.</TableCell></TableRow>}
          {visibleCustomers.map((customer) => <TableRow key={customer.id} hover>
            <TableCell><Typography fontWeight={700}>{customer.name}</Typography><Typography variant="body2" color="text.secondary">{customer.customerNumber}</Typography></TableCell>
            <TableCell>{customer.type}</TableCell>
            <TableCell><Typography variant="body2">{customer.email || '—'}</Typography><Typography variant="body2" color="text.secondary">{customer.phone || '—'}</Typography></TableCell>
            <TableCell>{customer.identificationNumber || '—'}</TableCell>
            <TableCell><Stack direction="row" gap={0.75} flexWrap="wrap">
              <Chip size="small" label={customer.isActive ? 'Active' : 'Inactive'} color={customer.isActive ? 'success' : 'default'} variant="outlined" />
              {customer.isBlocked && <Chip size="small" label="Blocked" color="error" variant="outlined" />}
              <Chip size="small" label={customer.emailConfirmed ? 'Online verified' : customer.hasOnlineAccount ? 'Invite pending' : 'Offline only'} color={customer.emailConfirmed ? 'success' : 'default'} variant="outlined" />
            </Stack></TableCell>
            <TableCell align="right"><Tooltip title="Edit customer"><IconButton onClick={() => openEdit(customer)}><EditOutlined /></IconButton></Tooltip>
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
  </Box>
}
