import { useCallback, useEffect, useMemo, useState } from 'react'
import axios from 'axios'
import CancelOutlined from '@mui/icons-material/CancelOutlined'
import CheckCircleOutlined from '@mui/icons-material/CheckCircleOutlined'
import EventAvailableOutlined from '@mui/icons-material/EventAvailableOutlined'
import SearchOutlined from '@mui/icons-material/SearchOutlined'
import VisibilityOutlined from '@mui/icons-material/VisibilityOutlined'
import {
  Alert, Box, Button, Card, CardContent, Chip, CircularProgress, Dialog,
  DialogActions, DialogContent, DialogTitle, Divider, IconButton, InputAdornment,
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
  customerIsBlocked: boolean; branchId: string; branchName: string; items: BookingItem[]
}

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

  const loadBookings = useCallback(async () => {
    setLoading(true)
    try { setBookings((await api.get<Booking[]>('/bookings')).data) }
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

    <Dialog open={Boolean(selected)} onClose={() => !saving && setSelected(null)} fullWidth maxWidth="sm"><DialogTitle>Booking {selected?.bookingNumber}</DialogTitle><DialogContent>
      {selected && <Stack spacing={2.25} mt={1}>{error && <Alert severity="error">{error}</Alert>}
        <Stack direction="row" justifyContent="space-between"><Box><Typography variant="caption" color="text.secondary">Status</Typography><Box mt={.5}><Chip size="small" label={displayStatus(selected.status)} color={statusColors[selected.status]} /></Box></Box><Box textAlign="right"><Typography variant="caption" color="text.secondary">Branch</Typography><Typography fontWeight={650}>{selected.branchName}</Typography></Box></Stack>
        <Divider /><Box><Typography variant="overline" color="text.secondary">Customer</Typography><Typography fontWeight={700}>{selected.customerName}</Typography><Typography variant="body2">{selected.customerEmail || 'No email'} · {selected.customerPhone || 'No phone'}</Typography>{selected.customerIsBlocked && <Alert severity="error" sx={{ mt: 1 }}>This customer is blocked from renting.</Alert>}</Box>
        <Divider />{selected.items.map((item) => <Box key={item.id}><Typography variant="overline" color="text.secondary">Rental item</Typography><Typography fontWeight={700}>{item.assetName} ({item.assetNumber})</Typography><Typography variant="body2">{displayDate(item.startAt)} to {displayDate(item.endAt)}</Typography><Typography variant="body2" color="text.secondary">${item.dailyRate.toFixed(2)} per day</Typography></Box>)}
        {selected.notes && <><Divider /><Box><Typography variant="overline" color="text.secondary">Notes</Typography><Typography variant="body2" sx={{ whiteSpace: 'pre-line' }}>{selected.notes}</Typography></Box></>}
        {action && <TextField label={action === 'Cancelled' ? 'Cancellation reason (optional)' : 'Staff note (optional)'} multiline minRows={2} value={note} onChange={(e) => setNote(e.target.value)} />}
      </Stack>}
    </DialogContent><DialogActions sx={{ p: 3, pt: 1 }}>
      {!action && selected?.status === 'Draft' && <><Button color="error" startIcon={<CancelOutlined />} onClick={() => beginAction('Cancelled')}>Decline</Button><Box sx={{ flex: 1 }} /><Button variant="contained" startIcon={<CheckCircleOutlined />} disabled={selected.customerIsBlocked} onClick={() => beginAction('Confirmed')}>Confirm booking</Button></>}
      {action && <><Button onClick={() => setAction(null)} disabled={saving}>Back</Button><Box sx={{ flex: 1 }} /><Button variant="contained" color={action === 'Cancelled' ? 'error' : 'primary'} disabled={saving} onClick={() => void updateStatus()}>{saving ? 'Saving…' : action === 'Cancelled' ? 'Decline request' : 'Confirm booking'}</Button></>}
      {!action && selected?.status !== 'Draft' && <Button onClick={() => setSelected(null)}>Close</Button>}
    </DialogActions></Dialog>
  </Box>
}
