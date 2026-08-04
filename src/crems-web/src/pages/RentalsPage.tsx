import { useCallback, useEffect, useMemo, useState } from 'react'
import AssignmentTurnedInOutlined from '@mui/icons-material/AssignmentTurnedInOutlined'
import PlayArrowOutlined from '@mui/icons-material/PlayArrowOutlined'
import SearchOutlined from '@mui/icons-material/SearchOutlined'
import {
  Alert, Box, Button, Card, CardContent, Chip, CircularProgress, InputAdornment,
  Table, TableBody, TableCell, TableContainer, TableHead, TableRow,
  TextField, Typography,
} from '@mui/material'
import { api } from '../api/client'

type RentalBooking = {
  id: string; bookingNumber: string; status: string; customerName: string; customerPhone: string | null
  branchName: string; items: { assetNumber: string; assetName: string; startAt: string; endAt: string; dailyRate: number }[]
}
const fmt = (value: string) => new Intl.DateTimeFormat('en-FJ', { day: 'numeric', month: 'short', year: 'numeric' }).format(new Date(value))

export function RentalsPage() {
  const [records, setRecords] = useState<RentalBooking[]>([])
  const [loading, setLoading] = useState(true)
  const [search, setSearch] = useState('')
  const [error, setError] = useState('')
  const [savingId, setSavingId] = useState('')
  const load = useCallback(async () => {
    setLoading(true)
    try { setRecords((await api.get<RentalBooking[]>('/bookings')).data.filter((item) => ['Confirmed', 'ConvertedToRental', 'Completed'].includes(item.status))) }
    catch { setError('Unable to load rental records.') } finally { setLoading(false) }
  }, [])
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load()
  }, [load])
  const visible = useMemo(() => { const term = search.toLowerCase(); return records.filter((record) => [record.bookingNumber, record.customerName, record.branchName, ...record.items.flatMap((item) => [item.assetName, item.assetNumber])].some((value) => value.toLowerCase().includes(term))) }, [records, search])
  async function transition(record: RentalBooking) {
    setSavingId(record.id); setError('')
    try { await api.patch(`/bookings/${record.id}/status`, { status: record.status === 'Confirmed' ? 'ConvertedToRental' : 'Completed', note: null }); await load() }
    catch { setError('Unable to update the rental. Check its current status and try again.') } finally { setSavingId('') }
  }
  return <Box sx={{ p: { xs: 2, sm: 3, lg: 4 }, maxWidth: 1500, mx: 'auto' }}><Box mb={3}><Typography variant="h4" fontWeight={750}>Rentals</Typography><Typography color="text.secondary" mt={.5}>Manage scheduled collections, active rentals and completed returns.</Typography></Box>
    {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}<Card variant="outlined"><CardContent sx={{ p: 0 }}><Box sx={{ p: 2, borderBottom: 1, borderColor: 'divider' }}><TextField size="small" placeholder="Search rentals" value={search} onChange={(e) => setSearch(e.target.value)} sx={{ width: { xs: '100%', sm: 380 } }} InputProps={{ startAdornment: <InputAdornment position="start"><SearchOutlined /></InputAdornment> }} /></Box>
      {loading ? <Box sx={{ minHeight: 280, display: 'grid', placeItems: 'center' }}><CircularProgress /></Box> : <TableContainer><Table><TableHead><TableRow sx={{ bgcolor: '#f6f6f3' }}><TableCell>Rental</TableCell><TableCell>Customer</TableCell><TableCell>Asset</TableCell><TableCell>Period</TableCell><TableCell>Branch</TableCell><TableCell>Status</TableCell><TableCell align="right">Action</TableCell></TableRow></TableHead><TableBody>
        {visible.length === 0 && <TableRow><TableCell colSpan={7} align="center" sx={{ py: 8, color: 'text.secondary' }}>No rental records found. Confirmed bookings will appear here.</TableCell></TableRow>}
        {visible.map((record) => { const item = record.items[0]; return <TableRow key={record.id} hover><TableCell sx={{ fontWeight: 700 }}>{record.bookingNumber}</TableCell><TableCell><Typography variant="body2" fontWeight={600}>{record.customerName}</Typography><Typography variant="caption" color="text.secondary">{record.customerPhone || '—'}</Typography></TableCell><TableCell>{item ? `${item.assetName} (${item.assetNumber})` : '—'}</TableCell><TableCell>{item ? `${fmt(item.startAt)} – ${fmt(item.endAt)}` : '—'}</TableCell><TableCell>{record.branchName}</TableCell><TableCell><Chip size="small" label={record.status === 'ConvertedToRental' ? 'Active' : record.status} color={record.status === 'ConvertedToRental' ? 'info' : record.status === 'Confirmed' ? 'success' : 'default'} variant="outlined" /></TableCell><TableCell align="right">{record.status !== 'Completed' && <Button size="small" variant={record.status === 'Confirmed' ? 'contained' : 'outlined'} startIcon={record.status === 'Confirmed' ? <PlayArrowOutlined /> : <AssignmentTurnedInOutlined />} disabled={savingId === record.id} onClick={() => void transition(record)}>{record.status === 'Confirmed' ? 'Start rental' : 'Complete return'}</Button>}</TableCell></TableRow> })}
      </TableBody></Table></TableContainer>}</CardContent></Card></Box>
}
