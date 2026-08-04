import { useCallback, useEffect, useMemo, useState } from 'react'
import axios from 'axios'
import CancelOutlined from '@mui/icons-material/CancelOutlined'
import CheckCircleOutlined from '@mui/icons-material/CheckCircleOutlined'
import EventAvailableOutlined from '@mui/icons-material/EventAvailableOutlined'
import SearchOutlined from '@mui/icons-material/SearchOutlined'
import VisibilityOutlined from '@mui/icons-material/VisibilityOutlined'
import EditOutlined from '@mui/icons-material/EditOutlined'
import {
  Alert, Box, Button, Card, CardContent, Chip, CircularProgress, Dialog,
  DialogActions, DialogContent, DialogTitle, Divider, Grid, IconButton, InputAdornment,
  MenuItem, Stack, Table, TableBody, TableCell, TableContainer, TableHead,
  TableRow, TextField, Tooltip, Typography,
} from '@mui/material'
import { api } from '../api/client'

const statuses = ['All', 'Draft', 'Confirmed', 'Cancelled', 'ConvertedToRental', 'Completed', 'Expired'] as const
type BookingStatus = Exclude<typeof statuses[number], 'All'>
type BookingItem = {
  id: string; assetId: string; assetNumber: string; assetName: string
  startAt: string; endAt: string; dailyRate: number
}
type Booking = {
  id: string; bookingNumber: string; status: BookingStatus; createdAt: string; notes: string | null
  customerId: string; customerName: string; customerEmail: string | null; customerPhone: string | null
  customerIsBlocked: boolean; branchId: string; branchName: string; discountAmount: number; taxRate: number
  depositRequired: number; additionalCharges: number; additionalChargesDescription: string | null; items: BookingItem[]
}
type Asset = { id: string; assetNumber: string; name: string; branchId: string; dailyRate: number; isActive: boolean }
type Customer = { id: string; customerNumber: string; name: string; isActive: boolean }
type EditForm = { customerId: string; assetId: string; startAt: string; endAt: string; dailyRate: string; notes: string; discountAmount: string; taxRate: string; depositRequired: string; additionalCharges: string; additionalChargesDescription: string }

const statusColors: Record<BookingStatus, 'warning' | 'success' | 'error' | 'info' | 'default'> = {
  Draft: 'warning', Confirmed: 'success', Cancelled: 'error', ConvertedToRental: 'info', Completed: 'default', Expired: 'default',
}
function displayStatus(status: string) { return status.replace(/([a-z])([A-Z])/g, '$1 $2') }
function displayDate(value: string) { return new Intl.DateTimeFormat('en-FJ', { day: 'numeric', month: 'short', year: 'numeric' }).format(new Date(value)) }

export function BookingsPage() {
  const [bookings, setBookings] = useState<Booking[]>([])
  const [loading, setLoading] = useState(true)
  const [filter, setFilter] = useState<typeof statuses[number]>('All')
  const [search, setSearch] = useState('')
  const [selected, setSelected] = useState<Booking | null>(null)
  const [action, setAction] = useState<'Confirmed' | 'Cancelled' | null>(null)
  const [note, setNote] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [assets, setAssets] = useState<Asset[]>([]); const [customers, setCustomers] = useState<Customer[]>([])
  const [editing, setEditing] = useState(false); const [editForm, setEditForm] = useState<EditForm | null>(null)

  const loadBookings = useCallback(async () => {
    setLoading(true)
    try { const [bookingResult, assetResult, customerResult] = await Promise.all([api.get<Booking[]>('/bookings'), api.get<Asset[]>('/assets'), api.get<Customer[]>('/customers')]); setBookings(bookingResult.data); setAssets(assetResult.data); setCustomers(customerResult.data) }
    catch { setError('Unable to load bookings. Confirm that the backend is running.') }
    finally { setLoading(false) }
  }, [])
  useEffect(() => {
    // Load the staff queue when the page opens.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void loadBookings()
  }, [loadBookings])

  const visible = useMemo(() => {
    const term = search.trim().toLowerCase()
    return bookings.filter((booking) => (filter === 'All' || booking.status === filter) &&
      (!term || [booking.bookingNumber, booking.customerName, booking.customerEmail, booking.customerPhone, booking.branchName,
        ...booking.items.flatMap((item) => [item.assetName, item.assetNumber])]
        .some((value) => value?.toLowerCase().includes(term))))
  }, [bookings, filter, search])

  function beginAction(status: 'Confirmed' | 'Cancelled') { setAction(status); setNote(''); setError('') }
  function beginEdit() { if (!selected?.items[0]) return; const item = selected.items[0]; setEditForm({ customerId: selected.customerId, assetId: item.assetId, startAt: item.startAt.slice(0, 10), endAt: item.endAt.slice(0, 10), dailyRate: String(item.dailyRate), notes: selected.notes ?? '', discountAmount: String(selected.discountAmount), taxRate: String(selected.taxRate), depositRequired: String(selected.depositRequired), additionalCharges: String(selected.additionalCharges), additionalChargesDescription: selected.additionalChargesDescription ?? '' }); setEditing(true); setError('') }
  async function saveEdit() { if (!selected || !editForm) return; setSaving(true); setError(''); try { await api.put(`/bookings/${selected.id}`, { branchId: selected.branchId, customerId: editForm.customerId, assetId: editForm.assetId, startAt: `${editForm.startAt}T09:00:00+12:00`, endAt: `${editForm.endAt}T09:00:00+12:00`, dailyRate: Number(editForm.dailyRate), notes: editForm.notes || null, discountAmount: Number(editForm.discountAmount || 0), taxRate: Number(editForm.taxRate || 0), depositRequired: Number(editForm.depositRequired || 0), additionalCharges: Number(editForm.additionalCharges || 0), additionalChargesDescription: editForm.additionalChargesDescription || null }); setEditing(false); setSelected(null); await loadBookings() } catch (requestError: unknown) { const data = axios.isAxiosError(requestError) ? requestError.response?.data : undefined; const errors = data?.errors as Record<string, string[]> | undefined; setError(errors ? Object.values(errors).flat().join(' ') : 'Unable to update this booking.') } finally { setSaving(false) } }
  async function updateStatus() {
    if (!selected || !action) return
    setSaving(true); setError('')
    try {
      await api.patch(`/bookings/${selected.id}/status`, { status: action, note: note || null })
      setAction(null); setSelected(null); await loadBookings()
    } catch (requestError: unknown) {
      const data = axios.isAxiosError(requestError) ? requestError.response?.data : undefined
      const errors = data?.errors as Record<string, string[]> | undefined
      setError(errors ? Object.values(errors).flat().join(' ') : 'Unable to update this booking.')
    } finally { setSaving(false) }
  }

  return <Box sx={{ p: { xs: 2, sm: 3, lg: 4 }, maxWidth: 1500, mx: 'auto' }}>
    <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" gap={2} mb={3}>
      <Box><Typography variant="h4" fontWeight={750}>Bookings</Typography><Typography color="text.secondary" mt={0.5}>Review public requests and manage confirmed reservations.</Typography></Box>
      <Chip icon={<EventAvailableOutlined />} label={`${bookings.filter((item) => item.status === 'Draft').length} awaiting review`} color="warning" variant="outlined" />
    </Stack>
    {error && !selected && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
    <Card variant="outlined"><CardContent sx={{ p: 0 }}><Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} sx={{ p: 2, borderBottom: 1, borderColor: 'divider' }}>
      <TextField size="small" placeholder="Search bookings" value={search} onChange={(e) => setSearch(e.target.value)} sx={{ width: { xs: '100%', sm: 360 } }} InputProps={{ startAdornment: <InputAdornment position="start"><SearchOutlined /></InputAdornment> }} />
      <TextField select size="small" label="Status" value={filter} onChange={(e) => setFilter(e.target.value as typeof filter)} sx={{ minWidth: 190 }}>{statuses.map((status) => <MenuItem key={status} value={status}>{displayStatus(status)}</MenuItem>)}</TextField>
    </Stack>
      {loading ? <Box sx={{ minHeight: 280, display: 'grid', placeItems: 'center' }}><CircularProgress /></Box> : <TableContainer><Table><TableHead><TableRow sx={{ bgcolor: '#f6f6f3' }}>
        <TableCell>Reference</TableCell><TableCell>Customer</TableCell><TableCell>Rental item</TableCell><TableCell>Dates</TableCell><TableCell>Branch</TableCell><TableCell>Status</TableCell><TableCell align="right">View</TableCell>
      </TableRow></TableHead><TableBody>
        {visible.length === 0 && <TableRow><TableCell colSpan={7} align="center" sx={{ py: 8, color: 'text.secondary' }}>No matching bookings found.</TableCell></TableRow>}
        {visible.map((booking) => { const item = booking.items[0]; return <TableRow key={booking.id} hover>
          <TableCell><Typography fontWeight={700}>{booking.bookingNumber}</Typography><Typography variant="caption" color="text.secondary">Received {displayDate(booking.createdAt)}</Typography></TableCell>
          <TableCell><Typography variant="body2" fontWeight={600}>{booking.customerName}</Typography><Typography variant="caption" color="text.secondary">{booking.customerEmail || booking.customerPhone || '—'}</Typography></TableCell>
          <TableCell>{item ? <><Typography variant="body2">{item.assetName}</Typography><Typography variant="caption" color="text.secondary">{item.assetNumber}</Typography></> : '—'}</TableCell>
          <TableCell>{item ? `${displayDate(item.startAt)} – ${displayDate(item.endAt)}` : '—'}</TableCell><TableCell>{booking.branchName}</TableCell>
          <TableCell><Chip size="small" label={displayStatus(booking.status)} color={statusColors[booking.status]} variant="outlined" /></TableCell>
          <TableCell align="right"><Tooltip title="View booking"><IconButton onClick={() => { setSelected(booking); setError('') }}><VisibilityOutlined /></IconButton></Tooltip></TableCell>
        </TableRow> })}
      </TableBody></Table></TableContainer>}
    </CardContent></Card>

    <Dialog open={Boolean(selected)} onClose={() => !saving && setSelected(null)} fullWidth maxWidth="md"><DialogTitle><Stack direction="row" alignItems="center" justifyContent="space-between">Booking {selected?.bookingNumber}{selected && !editing && !['ConvertedToRental', 'Completed'].includes(selected.status) && <Button startIcon={<EditOutlined />} onClick={beginEdit}>Edit booking</Button>}</Stack></DialogTitle><DialogContent>
      {selected && <Stack spacing={2.25} mt={1}>{error && <Alert severity="error">{error}</Alert>}
        {editing && editForm ? <><Alert severity="info">Assets and customers are limited to records your account is authorized to access.</Alert><Grid container spacing={2}><Grid size={{ xs: 12, sm: 6 }}><TextField fullWidth select label="Customer" value={editForm.customerId} onChange={(e) => setEditForm({ ...editForm, customerId: e.target.value })}>{customers.filter((item) => item.isActive).map((item) => <MenuItem key={item.id} value={item.id}>{item.customerNumber} — {item.name}</MenuItem>)}</TextField></Grid><Grid size={{ xs: 12, sm: 6 }}><TextField fullWidth select label="Rental asset" value={editForm.assetId} onChange={(e) => { const asset = assets.find((item) => item.id === e.target.value); setEditForm({ ...editForm, assetId: e.target.value, dailyRate: asset ? String(asset.dailyRate) : editForm.dailyRate }) }}>{assets.filter((item) => item.isActive && item.branchId === selected.branchId).map((item) => <MenuItem key={item.id} value={item.id}>{item.assetNumber} — {item.name}</MenuItem>)}</TextField></Grid><Grid size={{ xs: 6, sm: 3 }}><TextField fullWidth type="date" label="Start" InputLabelProps={{ shrink: true }} value={editForm.startAt} onChange={(e) => setEditForm({ ...editForm, startAt: e.target.value })} /></Grid><Grid size={{ xs: 6, sm: 3 }}><TextField fullWidth type="date" label="End" InputLabelProps={{ shrink: true }} value={editForm.endAt} onChange={(e) => setEditForm({ ...editForm, endAt: e.target.value })} /></Grid><Grid size={{ xs: 6, sm: 3 }}><TextField fullWidth type="number" label="Daily rate" value={editForm.dailyRate} onChange={(e) => setEditForm({ ...editForm, dailyRate: e.target.value })} /></Grid><Grid size={{ xs: 6, sm: 3 }}><TextField fullWidth type="number" label="Deposit" value={editForm.depositRequired} onChange={(e) => setEditForm({ ...editForm, depositRequired: e.target.value })} /></Grid><Grid size={{ xs: 6, sm: 3 }}><TextField fullWidth type="number" label="Discount" value={editForm.discountAmount} onChange={(e) => setEditForm({ ...editForm, discountAmount: e.target.value })} /></Grid><Grid size={{ xs: 6, sm: 3 }}><TextField fullWidth type="number" label="Tax rate %" value={editForm.taxRate} onChange={(e) => setEditForm({ ...editForm, taxRate: e.target.value })} /></Grid><Grid size={{ xs: 6, sm: 3 }}><TextField fullWidth type="number" label="Other charges" value={editForm.additionalCharges} onChange={(e) => setEditForm({ ...editForm, additionalCharges: e.target.value })} /></Grid><Grid size={{ xs: 6, sm: 3 }}><TextField fullWidth label="Charge reason" value={editForm.additionalChargesDescription} onChange={(e) => setEditForm({ ...editForm, additionalChargesDescription: e.target.value })} /></Grid></Grid><TextField multiline minRows={2} label="Internal notes" value={editForm.notes} onChange={(e) => setEditForm({ ...editForm, notes: e.target.value })} /></> : <>
        <Stack direction="row" justifyContent="space-between"><Box><Typography variant="caption" color="text.secondary">Status</Typography><Box mt={.5}><Chip size="small" label={displayStatus(selected.status)} color={statusColors[selected.status]} /></Box></Box><Box textAlign="right"><Typography variant="caption" color="text.secondary">Branch</Typography><Typography fontWeight={650}>{selected.branchName}</Typography></Box></Stack>
        <Divider /><Box><Typography variant="overline" color="text.secondary">Customer</Typography><Typography fontWeight={700}>{selected.customerName}</Typography><Typography variant="body2">{selected.customerEmail || 'No email'} · {selected.customerPhone || 'No phone'}</Typography>{selected.customerIsBlocked && <Alert severity="error" sx={{ mt: 1 }}>This customer is blocked from renting.</Alert>}</Box>
        <Divider />{selected.items.map((item) => <Box key={item.id}><Typography variant="overline" color="text.secondary">Rental item</Typography><Typography fontWeight={700}>{item.assetName} ({item.assetNumber})</Typography><Typography variant="body2">{displayDate(item.startAt)} to {displayDate(item.endAt)}</Typography><Typography variant="body2" color="text.secondary">${item.dailyRate.toFixed(2)} per day</Typography></Box>)}
        <Divider /><Grid container spacing={2}><Grid size={3}><Typography variant="caption" color="text.secondary">Deposit</Typography><Typography fontWeight={650}>${selected.depositRequired.toFixed(2)}</Typography></Grid><Grid size={3}><Typography variant="caption" color="text.secondary">Discount</Typography><Typography fontWeight={650}>${selected.discountAmount.toFixed(2)}</Typography></Grid><Grid size={3}><Typography variant="caption" color="text.secondary">Tax</Typography><Typography fontWeight={650}>{selected.taxRate}%</Typography></Grid><Grid size={3}><Typography variant="caption" color="text.secondary">Other charges</Typography><Typography fontWeight={650}>${selected.additionalCharges.toFixed(2)}</Typography></Grid></Grid>{selected.notes && <><Divider /><Box><Typography variant="overline" color="text.secondary">Notes</Typography><Typography variant="body2" sx={{ whiteSpace: 'pre-line' }}>{selected.notes}</Typography></Box></>}
        {action && <TextField label={action === 'Cancelled' ? 'Cancellation reason (optional)' : 'Staff note (optional)'} multiline minRows={2} value={note} onChange={(e) => setNote(e.target.value)} />}
      </>}</Stack>}
    </DialogContent><DialogActions sx={{ p: 3, pt: 1 }}>
      {editing && <><Button onClick={() => setEditing(false)} disabled={saving}>Cancel editing</Button><Box sx={{ flex: 1 }} /><Button variant="contained" onClick={() => void saveEdit()} disabled={saving}>Save booking</Button></>}
      {!editing && !action && selected?.status === 'Draft' && <><Button color="error" startIcon={<CancelOutlined />} onClick={() => beginAction('Cancelled')}>Decline</Button><Box sx={{ flex: 1 }} /><Button variant="contained" startIcon={<CheckCircleOutlined />} disabled={selected.customerIsBlocked} onClick={() => beginAction('Confirmed')}>Confirm booking</Button></>}
      {action && <><Button onClick={() => setAction(null)} disabled={saving}>Back</Button><Box sx={{ flex: 1 }} /><Button variant="contained" color={action === 'Cancelled' ? 'error' : 'primary'} disabled={saving} onClick={() => void updateStatus()}>{saving ? 'Saving…' : action === 'Cancelled' ? 'Decline request' : 'Confirm booking'}</Button></>}
      {!editing && !action && selected?.status !== 'Draft' && <Button onClick={() => setSelected(null)}>Close</Button>}
    </DialogActions></Dialog>
  </Box>
}
