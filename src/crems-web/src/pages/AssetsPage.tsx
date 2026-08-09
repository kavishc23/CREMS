import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import axios from 'axios'
import AddOutlined from '@mui/icons-material/AddOutlined'
import EditOutlined from '@mui/icons-material/EditOutlined'
import QrCode2Outlined from '@mui/icons-material/QrCode2Outlined'
import SearchOutlined from '@mui/icons-material/SearchOutlined'
import {
  Alert, Box, Button, Card, CardContent, Chip, CircularProgress, Dialog,
  DialogActions, DialogContent, DialogTitle, FormControl, InputAdornment,
  InputLabel, MenuItem, Select, Stack, Table, TableBody, TableCell,
  TableContainer, TableHead, TableRow, TextField, Tooltip, IconButton, Typography,
} from '@mui/material'
import { api } from '../api/client'
import { roles } from '../auth/access'
import { AssetQrLabelDialog } from '../components/AssetQrLabelDialog'

const assetTypes = ['Vehicle', 'Equipment'] as const
const assetStatuses = ['Available', 'Reserved', 'Rented', 'Inspection', 'Maintenance', 'OutOfService', 'Retired'] as const
type AssetType = typeof assetTypes[number]
type AssetStatus = typeof assetStatuses[number]
type Asset = {
  id: string; assetNumber: string; name: string; type: AssetType; status: AssetStatus
  divisionId: string | null; divisionName: string | null
  branchId: string; branchName: string; registrationNumber: string | null
  serialNumber: string | null; dailyRate: number; nextServiceDate: string | null; isActive: boolean
}
type Branch = { id: string; name: string; isActive: boolean; divisionIds: string[] }
type Division = { id: string; name: string; isActive: boolean }
type AssetForm = {
  assetNumber: string; name: string; type: AssetType; status: AssetStatus; divisionId: string; branchId: string
  registrationNumber: string; serialNumber: string; dailyRate: string; nextServiceDate: string
}
const emptyForm: AssetForm = {
  assetNumber: '', name: '', type: 'Vehicle', status: 'Available', divisionId: '', branchId: '',
  registrationNumber: '', serialNumber: '', dailyRate: '', nextServiceDate: '',
}

const statusColors: Partial<Record<AssetStatus, 'success' | 'warning' | 'error' | 'info' | 'default'>> = {
  Available: 'success', Reserved: 'info', Rented: 'warning', Maintenance: 'error', Retired: 'default',
}

export function AssetsPage({ userRoles }: { userRoles: string[] }) {
  const [assets, setAssets] = useState<Asset[]>([])
  const [branches, setBranches] = useState<Branch[]>([])
  const [divisions, setDivisions] = useState<Division[]>([])
  const [loading, setLoading] = useState(true)
  const [open, setOpen] = useState(false)
  const [saving, setSaving] = useState(false)
  const [editing, setEditing] = useState<Asset | null>(null)
  const [form, setForm] = useState<AssetForm>(emptyForm)
  const [search, setSearch] = useState('')
  const [error, setError] = useState('')
  const [qrAsset, setQrAsset] = useState<Asset | null>(null)
  const canManage = userRoles.includes(roles.administrator) || userRoles.includes(roles.branchManager)

  const loadData = useCallback(async () => {
    setLoading(true)
    try {
      const assetResponse = await api.get<Asset[]>('/assets')
      setAssets(assetResponse.data)
      if (canManage) {
        const [branchResponse, divisionResponse] = await Promise.all([api.get<Branch[]>('/branches'), api.get<Division[]>('/divisions')])
        setBranches(branchResponse.data.filter((branch) => branch.isActive)); setDivisions(divisionResponse.data.filter(x => x.isActive))
      }
    } catch { setError('Unable to load the asset register. Confirm that the backend is running.') }
    finally { setLoading(false) }
  }, [canManage])

  useEffect(() => {
    // Initial loading synchronizes the register with the API.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void loadData()
  }, [loadData])

  const visibleAssets = useMemo(() => {
    const term = search.trim().toLowerCase()
    return term ? assets.filter((asset) => [asset.assetNumber, asset.name, asset.branchName, asset.registrationNumber, asset.serialNumber]
      .some((value) => value?.toLowerCase().includes(term))) : assets
  }, [assets, search])

  function openCreate() {
    const divisionId = divisions[0]?.id ?? ''; setEditing(null); setForm({ ...emptyForm, divisionId, branchId: branches.find(x => x.divisionIds.includes(divisionId))?.id ?? '' }); setError(''); setOpen(true)
  }

  function openEdit(asset: Asset) {
    setEditing(asset)
    setForm({ assetNumber: asset.assetNumber, name: asset.name, type: asset.type, status: asset.status,
      divisionId: asset.divisionId ?? '', branchId: asset.branchId, registrationNumber: asset.registrationNumber ?? '', serialNumber: asset.serialNumber ?? '',
      dailyRate: String(asset.dailyRate), nextServiceDate: asset.nextServiceDate ?? '' })
    setError(''); setOpen(true)
  }

  async function save(event: FormEvent) {
    event.preventDefault(); setSaving(true); setError('')
    try {
      const payload = { ...form, dailyRate: Number(form.dailyRate), nextServiceDate: form.nextServiceDate || null }
      if (editing) await api.put(`/assets/${editing.id}`, payload)
      else await api.post('/assets', payload)
      setOpen(false); await loadData()
    } catch (requestError: unknown) {
      const data = axios.isAxiosError(requestError) ? requestError.response?.data : undefined
      const errors = data?.errors as Record<string, string[]> | undefined
      setError(errors ? Object.values(errors).flat().join(' ') : 'Unable to save the asset.')
    } finally { setSaving(false) }
  }

  return <Box sx={{ p: { xs: 2, sm: 3, lg: 4 }, maxWidth: 1500, mx: 'auto' }}>
    <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" gap={2} mb={3}>
      <Box><Typography variant="h4" fontWeight={750}>Asset register</Typography>
        <Typography color="text.secondary" mt={0.5}>Manage vehicles and rental equipment across all branches.</Typography></Box>
      {canManage && <Button variant="contained" startIcon={<AddOutlined />} onClick={openCreate}>Add asset</Button>}
    </Stack>
    {error && !open && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
    <Card variant="outlined"><CardContent sx={{ p: 0 }}>
      <Box sx={{ p: 2, borderBottom: 1, borderColor: 'divider' }}><TextField size="small" placeholder="Search assets" value={search}
        onChange={(event) => setSearch(event.target.value)} sx={{ width: { xs: '100%', sm: 360 } }}
        InputProps={{ startAdornment: <InputAdornment position="start"><SearchOutlined /></InputAdornment> }} /></Box>
      {loading ? <Box sx={{ minHeight: 280, display: 'grid', placeItems: 'center' }}><CircularProgress /></Box> :
        <TableContainer><Table><TableHead><TableRow sx={{ bgcolor: '#f6f6f3' }}>
          <TableCell>Asset</TableCell><TableCell>Division</TableCell><TableCell>Type</TableCell><TableCell>Branch</TableCell><TableCell>Status</TableCell>
          <TableCell>Registration / serial</TableCell><TableCell align="right">Daily rate</TableCell>{canManage && <TableCell align="right">Actions</TableCell>}
        </TableRow></TableHead><TableBody>
          {visibleAssets.length === 0 && <TableRow><TableCell colSpan={canManage ? 8 : 7} align="center" sx={{ py: 8, color: 'text.secondary' }}>No matching assets found.</TableCell></TableRow>}
          {visibleAssets.map((asset) => <TableRow key={asset.id} hover>
            <TableCell><Typography fontWeight={700}>{asset.assetNumber}</Typography><Typography variant="body2" color="text.secondary">{asset.name}</Typography></TableCell>
            <TableCell>{asset.divisionName || 'Unassigned'}</TableCell><TableCell>{asset.type}</TableCell><TableCell>{asset.branchName}</TableCell>
            <TableCell><Chip size="small" label={asset.status.replace(/([a-z])([A-Z])/g, '$1 $2')} color={statusColors[asset.status] ?? 'default'} variant="outlined" /></TableCell>
            <TableCell>{asset.registrationNumber || asset.serialNumber || '—'}</TableCell>
            <TableCell align="right">${asset.dailyRate.toFixed(2)}</TableCell>
            {canManage && <TableCell align="right"><Tooltip title="Print QR label"><IconButton onClick={() => setQrAsset(asset)}><QrCode2Outlined /></IconButton></Tooltip><Tooltip title="Edit asset"><IconButton onClick={() => openEdit(asset)}><EditOutlined /></IconButton></Tooltip></TableCell>}
          </TableRow>)}
        </TableBody></Table></TableContainer>}
    </CardContent></Card>
    <Dialog open={open} onClose={() => !saving && setOpen(false)} fullWidth maxWidth="md">
      <Box component="form" onSubmit={save}><DialogTitle>{editing ? 'Edit asset' : 'Add asset'}</DialogTitle><DialogContent>
        <Stack spacing={2.25} mt={1}>{error && <Alert severity="error">{error}</Alert>}
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
            <TextField fullWidth required label="Asset number" value={form.assetNumber} onChange={(e) => setForm({ ...form, assetNumber: e.target.value })} />
            <TextField fullWidth required label="Asset name" value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} />
          </Stack>
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
            <FormControl fullWidth required><InputLabel>Division</InputLabel><Select label="Division" value={form.divisionId} onChange={(e) => { const divisionId = e.target.value; setForm({ ...form, divisionId, branchId: branches.find(x => x.divisionIds.includes(divisionId))?.id ?? '' }) }}>{divisions.map(value => <MenuItem key={value.id} value={value.id}>{value.name}</MenuItem>)}</Select></FormControl>
            <FormControl fullWidth><InputLabel>Type</InputLabel><Select label="Type" value={form.type} onChange={(e) => setForm({ ...form, type: e.target.value as AssetType })}>{assetTypes.map((value) => <MenuItem key={value} value={value}>{value}</MenuItem>)}</Select></FormControl>
            <FormControl fullWidth><InputLabel>Status</InputLabel><Select label="Status" value={form.status} onChange={(e) => setForm({ ...form, status: e.target.value as AssetStatus })}>{assetStatuses.map((value) => <MenuItem key={value} value={value}>{value.replace(/([a-z])([A-Z])/g, '$1 $2')}</MenuItem>)}</Select></FormControl>
            <FormControl fullWidth required><InputLabel>Branch</InputLabel><Select label="Branch" value={form.branchId} onChange={(e) => setForm({ ...form, branchId: e.target.value })}>{branches.filter(branch => branch.divisionIds.includes(form.divisionId)).map((branch) => <MenuItem key={branch.id} value={branch.id}>{branch.name}</MenuItem>)}</Select></FormControl>
          </Stack>
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
            <TextField fullWidth label="Registration number" value={form.registrationNumber} onChange={(e) => setForm({ ...form, registrationNumber: e.target.value })} />
            <TextField fullWidth label="Serial number" value={form.serialNumber} onChange={(e) => setForm({ ...form, serialNumber: e.target.value })} />
          </Stack>
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
            <TextField fullWidth required type="number" label="Daily rate (FJD)" inputProps={{ min: 0, step: '0.01' }} value={form.dailyRate} onChange={(e) => setForm({ ...form, dailyRate: e.target.value })} />
            <TextField fullWidth type="date" label="Next service date" InputLabelProps={{ shrink: true }} value={form.nextServiceDate} onChange={(e) => setForm({ ...form, nextServiceDate: e.target.value })} />
          </Stack>
        </Stack>
      </DialogContent><DialogActions sx={{ p: 3, pt: 1 }}><Button onClick={() => setOpen(false)} disabled={saving}>Cancel</Button><Button type="submit" variant="contained" disabled={saving || !form.divisionId || !form.branchId}>{saving ? 'Saving…' : 'Save asset'}</Button></DialogActions></Box>
    </Dialog>
    <AssetQrLabelDialog asset={qrAsset} open={Boolean(qrAsset)} onClose={() => setQrAsset(null)} />
  </Box>
}
