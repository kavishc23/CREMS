import { useCallback, useEffect, useMemo, useState } from 'react'
import BuildOutlined from '@mui/icons-material/BuildOutlined'
import CheckCircleOutline from '@mui/icons-material/CheckCircleOutline'
import SearchOutlined from '@mui/icons-material/SearchOutlined'
import {
  Alert, Box, Button, Card, CardContent, Chip, CircularProgress, InputAdornment,
  Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow,
  TextField, Typography,
} from '@mui/material'
import { api } from '../api/client'

type Asset = { id: string; assetNumber: string; name: string; type: string; status: string; branchName: string; nextServiceDate: string | null; isActive: boolean }
export function MaintenancePage() {
  const [assets, setAssets] = useState<Asset[]>([]); const [loading, setLoading] = useState(true); const [search, setSearch] = useState(''); const [error, setError] = useState(''); const [savingId, setSavingId] = useState('')
  const load = useCallback(async () => { setLoading(true); try { setAssets((await api.get<Asset[]>('/assets')).data) } catch { setError('Unable to load maintenance information.') } finally { setLoading(false) } }, [])
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load()
  }, [load])
  const visible = useMemo(() => { const term = search.toLowerCase(); return assets.filter((asset) => [asset.assetNumber, asset.name, asset.branchName].some((value) => value.toLowerCase().includes(term))) }, [assets, search])
  async function toggle(asset: Asset) { setSavingId(asset.id); setError(''); try { await api.patch(`/assets/${asset.id}/status`, { status: asset.status === 'Maintenance' ? 'Available' : 'Maintenance', isActive: asset.isActive }); await load() } catch { setError('Unable to update maintenance status.') } finally { setSavingId('') } }
  return <Box sx={{ p: { xs: 2, sm: 3, lg: 4 }, maxWidth: 1500, mx: 'auto' }}><Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" gap={2} mb={3}><Box><Typography variant="h4" fontWeight={750}>Maintenance</Typography><Typography color="text.secondary" mt={.5}>Monitor service dates and remove assets from public availability when work is required.</Typography></Box><Chip icon={<BuildOutlined />} label={`${assets.filter((asset) => asset.status === 'Maintenance').length} under maintenance`} color="warning" variant="outlined" /></Stack>
    {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}<Card variant="outlined"><CardContent sx={{ p: 0 }}><Box sx={{ p: 2, borderBottom: 1, borderColor: 'divider' }}><TextField size="small" placeholder="Search assets" value={search} onChange={(e) => setSearch(e.target.value)} sx={{ width: { xs: '100%', sm: 380 } }} InputProps={{ startAdornment: <InputAdornment position="start"><SearchOutlined /></InputAdornment> }} /></Box>
      {loading ? <Box sx={{ minHeight: 280, display: 'grid', placeItems: 'center' }}><CircularProgress /></Box> : <TableContainer><Table><TableHead><TableRow sx={{ bgcolor: '#f6f6f3' }}><TableCell>Asset</TableCell><TableCell>Type</TableCell><TableCell>Branch</TableCell><TableCell>Next service</TableCell><TableCell>Status</TableCell><TableCell align="right">Action</TableCell></TableRow></TableHead><TableBody>{visible.map((asset) => <TableRow key={asset.id} hover><TableCell><Typography fontWeight={700}>{asset.assetNumber}</Typography><Typography variant="body2" color="text.secondary">{asset.name}</Typography></TableCell><TableCell>{asset.type}</TableCell><TableCell>{asset.branchName}</TableCell><TableCell>{asset.nextServiceDate ? new Date(`${asset.nextServiceDate}T00:00:00`).toLocaleDateString('en-FJ') : 'Not scheduled'}</TableCell><TableCell><Chip size="small" label={asset.status} color={asset.status === 'Maintenance' ? 'warning' : asset.status === 'Available' ? 'success' : 'default'} variant="outlined" /></TableCell><TableCell align="right"><Button size="small" variant={asset.status === 'Maintenance' ? 'contained' : 'outlined'} startIcon={asset.status === 'Maintenance' ? <CheckCircleOutline /> : <BuildOutlined />} disabled={savingId === asset.id || asset.status === 'Rented'} onClick={() => void toggle(asset)}>{asset.status === 'Maintenance' ? 'Return to service' : 'Log maintenance'}</Button></TableCell></TableRow>)}{visible.length === 0 && <TableRow><TableCell colSpan={6} align="center" sx={{ py: 8, color: 'text.secondary' }}>No matching assets found.</TableCell></TableRow>}</TableBody></Table></TableContainer>}</CardContent></Card></Box>
}
