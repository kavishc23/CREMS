import { useCallback, useEffect, useState, type FormEvent } from 'react'
import axios from 'axios'
import AddOutlined from '@mui/icons-material/AddOutlined'
import EditOutlined from '@mui/icons-material/EditOutlined'
import QrCode2Outlined from '@mui/icons-material/QrCode2Outlined'
import InsightsOutlined from '@mui/icons-material/InsightsOutlined'
import SearchOutlined from '@mui/icons-material/SearchOutlined'
import {
  Alert, Box, Button, Card, CardContent, Chip, CircularProgress, Dialog,
  DialogActions, DialogContent, DialogTitle, FormControl, InputAdornment,
  InputLabel, MenuItem, Pagination, Select, Stack, Table, TableBody, TableCell,
  TableContainer, TableHead, TableRow, TextField, Tooltip, IconButton, Typography,
} from '@mui/material'
import { api } from '../api/client'
import { roles } from '../auth/access'
import { AssetQrLabelDialog } from '../components/AssetQrLabelDialog'
import { AssetProfileDialog } from '../components/AssetProfileDialog'
import { isWeek2Demo } from '../config/demoMode'

const assetTypes = ['Vehicle', 'Equipment'] as const
const assetStatuses = ['Available', 'Reserved', 'Rented', 'Inspection', 'Maintenance', 'OutOfService', 'Retired'] as const
type AssetType = typeof assetTypes[number]
type AssetStatus = typeof assetStatuses[number]
type Asset = {
  id: string; assetNumber: string; name: string; type: AssetType; status: AssetStatus
  divisionId: string | null; divisionName: string | null
  branchId: string; branchName: string; registrationNumber: string | null
  serialNumber: string | null; dailyRate: number; nextServiceDate: string | null; isActive: boolean
  category: string | null; manufacturer: string | null; model: string | null; modelYear: number | null; vinOrChassisNumber: string | null; engineNumber: string | null
  meterUnit: string | null; currentMeterReading: number | null; acquisitionDate: string | null; acquisitionCost: number; currentBookValue: number | null
  ownershipType: string | null; insurancePolicyNumber: string | null; insuranceExpiry: string | null; warrantyExpiry: string | null
}
type Branch = { id: string; name: string; isActive: boolean; divisionIds: string[] }
type Division = { id: string; name: string; isActive: boolean }
type AssetForm = {
  assetNumber: string; name: string; type: AssetType; status: AssetStatus; divisionId: string; branchId: string
  registrationNumber: string; serialNumber: string; dailyRate: string; nextServiceDate: string
  category: string; manufacturer: string; model: string; modelYear: string; vinOrChassisNumber: string; engineNumber: string; meterUnit: string; currentMeterReading: string
  acquisitionDate: string; acquisitionCost: string; currentBookValue: string; ownershipType: string; insurancePolicyNumber: string; insuranceExpiry: string; warrantyExpiry: string
}
const emptyForm: AssetForm = {
  assetNumber: '', name: '', type: 'Vehicle', status: 'Available', divisionId: '', branchId: '',
  registrationNumber: '', serialNumber: '', dailyRate: '', nextServiceDate: '',
  category: '', manufacturer: '', model: '', modelYear: '', vinOrChassisNumber: '', engineNumber: '', meterUnit: 'km', currentMeterReading: '', acquisitionDate: '', acquisitionCost: '', currentBookValue: '', ownershipType: 'Owned', insurancePolicyNumber: '', insuranceExpiry: '', warrantyExpiry: '',
}
type Performance = { assetNumber:string; name:string; rentalRevenue:number; maintenanceExpense:number; operatingExpense:number; transferExpense:number; totalExpense:number; operatingProfit:number; acquisitionCost:number; currentBookValue:number|null; lifetimeNetAfterAcquisition:number; rentalCount:number; rentalDays:number; inspectionCount:number; maintenance:{id:string;jobNumber:string;serviceType:string;actualCost:number|null;status:string}[]; costs:{id:string;category:string;description:string;amount:number;occurredOn:string}[] }

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
  const [debouncedSearch, setDebouncedSearch] = useState('')
  const [page, setPage] = useState(1)
  const [totalAssets, setTotalAssets] = useState(0)
  const [error, setError] = useState('')
  const [qrAsset, setQrAsset] = useState<Asset | null>(null)
  const [profileAssetId, setProfileAssetId] = useState<string | null>(null)
  const [performance, setPerformance] = useState<Performance | null>(null); const [performanceOpen,setPerformanceOpen]=useState(false); const [performanceLoading,setPerformanceLoading]=useState(false)
  const canManage = userRoles.includes(roles.superAdministrator) || userRoles.includes(roles.administrator) || userRoles.includes(roles.branchManager)

  const loadData = useCallback(async () => {
    setLoading(true)
    try {
      const assetResponse = await api.get<Asset[]>('/assets', { params: { search: debouncedSearch || undefined, page, pageSize: 50 } })
      setAssets(assetResponse.data)
      setTotalAssets(Number(assetResponse.headers['x-total-count'] ?? assetResponse.data.length))
    } catch { setError('Unable to load the asset register. Confirm that the backend is running.') }
    finally { setLoading(false) }
  }, [debouncedSearch, page])

  useEffect(() => {
    // Initial loading synchronizes the register with the API.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void loadData()
  }, [loadData])

  useEffect(() => {
    const timer = window.setTimeout(() => setDebouncedSearch(search.trim()), 300)
    return () => window.clearTimeout(timer)
  }, [search])

  useEffect(() => {
    if (!canManage) return
    void Promise.all([api.get<Branch[]>('/branches'), api.get<Division[]>('/divisions')]).then(([branchResponse, divisionResponse]) => {
      setBranches(branchResponse.data.filter(branch => branch.isActive))
      setDivisions(divisionResponse.data.filter(division => division.isActive))
    }).catch(() => setError('Unable to load branch and division options.'))
  }, [canManage])

  function openCreate() {
    const divisionId = divisions[0]?.id ?? ''; setEditing(null); setForm({ ...emptyForm, divisionId, branchId: branches.find(x => x.divisionIds.includes(divisionId))?.id ?? '' }); setError(''); setOpen(true)
  }

  function openEdit(asset: Asset) {
    setEditing(asset)
    setForm({ assetNumber: asset.assetNumber, name: asset.name, type: asset.type, status: asset.status,
      divisionId: asset.divisionId ?? '', branchId: asset.branchId, registrationNumber: asset.registrationNumber ?? '', serialNumber: asset.serialNumber ?? '',
      dailyRate: String(asset.dailyRate), nextServiceDate: asset.nextServiceDate ?? '', category:asset.category??'',manufacturer:asset.manufacturer??'',model:asset.model??'',modelYear:asset.modelYear?String(asset.modelYear):'',vinOrChassisNumber:asset.vinOrChassisNumber??'',engineNumber:asset.engineNumber??'',meterUnit:asset.meterUnit??'',currentMeterReading:asset.currentMeterReading==null?'':String(asset.currentMeterReading),acquisitionDate:asset.acquisitionDate??'',acquisitionCost:String(asset.acquisitionCost??0),currentBookValue:asset.currentBookValue==null?'':String(asset.currentBookValue),ownershipType:asset.ownershipType??'',insurancePolicyNumber:asset.insurancePolicyNumber??'',insuranceExpiry:asset.insuranceExpiry??'',warrantyExpiry:asset.warrantyExpiry??'' })
    setError(''); setOpen(true)
  }

  async function save(event: FormEvent) {
    event.preventDefault(); setSaving(true); setError('')
    try {
      const payload = { ...form, dailyRate: Number(form.dailyRate), nextServiceDate: form.nextServiceDate || null, modelYear:form.modelYear?Number(form.modelYear):null,currentMeterReading:form.currentMeterReading?Number(form.currentMeterReading):null,acquisitionDate:form.acquisitionDate||null,acquisitionCost:Number(form.acquisitionCost||0),currentBookValue:form.currentBookValue?Number(form.currentBookValue):null,insuranceExpiry:form.insuranceExpiry||null,warrantyExpiry:form.warrantyExpiry||null }
      if (editing) await api.put(`/assets/${editing.id}`, payload)
      else await api.post('/assets', payload)
      setOpen(false); await loadData()
    } catch (requestError: unknown) {
      const data = axios.isAxiosError(requestError) ? requestError.response?.data : undefined
      const errors = data?.errors as Record<string, string[]> | undefined
      setError(errors ? Object.values(errors).flat().join(' ') : 'Unable to save the asset.')
    } finally { setSaving(false) }
  }
  async function openPerformance(asset:Asset){setPerformanceOpen(true);setPerformanceLoading(true);setPerformance(null);setError('');try{setPerformance((await api.get<Performance>(`/assets/${asset.id}/performance`)).data)}catch{setError('Unable to load this asset’s performance ledger.')}finally{setPerformanceLoading(false)}}

  return <Box sx={{ p: { xs: 2, sm: 3, lg: 4 }, maxWidth: 1500, mx: 'auto' }}>
    <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" gap={2} mb={3}>
      <Box><Typography variant="h4" fontWeight={750}>Asset register</Typography>
        <Typography color="text.secondary" mt={0.5}>Manage vehicles and rental equipment across all branches.</Typography></Box>
      {canManage && <Button variant="contained" startIcon={<AddOutlined />} onClick={openCreate}>Add asset</Button>}
    </Stack>
    {error && !open && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
    <Card variant="outlined"><CardContent sx={{ p: 0 }}>
      <Box sx={{ p: 2, borderBottom: 1, borderColor: 'divider' }}><TextField size="small" placeholder="Search assets" value={search}
        onChange={(event) => { setSearch(event.target.value); setPage(1) }} sx={{ width: { xs: '100%', sm: 360 } }}
        InputProps={{ startAdornment: <InputAdornment position="start"><SearchOutlined /></InputAdornment> }} /></Box>
      {loading ? <Box sx={{ minHeight: 280, display: 'grid', placeItems: 'center' }}><CircularProgress /></Box> :
        <TableContainer><Table><TableHead><TableRow sx={{ bgcolor: '#f6f6f3' }}>
          <TableCell>Asset</TableCell><TableCell>Division</TableCell><TableCell>Type</TableCell><TableCell>Branch</TableCell><TableCell>Status</TableCell>
          <TableCell>Registration / serial</TableCell><TableCell align="right">Daily rate</TableCell>{canManage && <TableCell align="right">Actions</TableCell>}
        </TableRow></TableHead><TableBody>
          {assets.length === 0 && <TableRow><TableCell colSpan={canManage ? 8 : 7} align="center" sx={{ py: 8, color: 'text.secondary' }}>No matching assets found.</TableCell></TableRow>}
          {assets.map((asset) => <TableRow key={asset.id} hover>
            <TableCell><Typography fontWeight={700}>{asset.assetNumber}</Typography><Typography variant="body2" color="text.secondary">{asset.name}</Typography></TableCell>
            <TableCell>{asset.divisionName || 'Unassigned'}</TableCell><TableCell>{asset.type}</TableCell><TableCell>{asset.branchName}</TableCell>
            <TableCell><Chip size="small" label={asset.status.replace(/([a-z])([A-Z])/g, '$1 $2')} color={statusColors[asset.status] ?? 'default'} variant="outlined" /></TableCell>
            <TableCell>{asset.registrationNumber || asset.serialNumber || '—'}</TableCell>
            <TableCell align="right">${asset.dailyRate.toFixed(2)}</TableCell>
            {canManage && <TableCell align="right">{!isWeek2Demo && <Button size="small" onClick={() => setProfileAssetId(asset.id)}>Open profile</Button>}<Tooltip title="Revenue, expenses and history"><IconButton onClick={() => void openPerformance(asset)}><InsightsOutlined /></IconButton></Tooltip><Tooltip title="Print QR label"><IconButton onClick={() => setQrAsset(asset)}><QrCode2Outlined /></IconButton></Tooltip><Tooltip title="Edit asset"><IconButton onClick={() => openEdit(asset)}><EditOutlined /></IconButton></Tooltip></TableCell>}
          </TableRow>)}
        </TableBody></Table></TableContainer>}
      {totalAssets > 50 && <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{p:2,borderTop:1,borderColor:'divider'}}><Typography variant="body2" color="text.secondary">{totalAssets.toLocaleString()} assets</Typography><Pagination page={page} count={Math.ceil(totalAssets / 50)} onChange={(_, value) => setPage(value)} color="primary" /></Stack>}
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
          <Typography variant="subtitle2">Identification and specifications</Typography><Stack direction={{xs:'column',sm:'row'}} spacing={2}><TextField fullWidth label="Category" placeholder="Vehicle, container, portable toilet, scaffolding…" value={form.category} onChange={e=>setForm({...form,category:e.target.value})}/><TextField fullWidth label="Manufacturer" value={form.manufacturer} onChange={e=>setForm({...form,manufacturer:e.target.value})}/><TextField fullWidth label="Model" value={form.model} onChange={e=>setForm({...form,model:e.target.value})}/><TextField fullWidth type="number" label="Model year" value={form.modelYear} onChange={e=>setForm({...form,modelYear:e.target.value})}/></Stack><Stack direction={{xs:'column',sm:'row'}} spacing={2}><TextField fullWidth label="VIN / chassis number" value={form.vinOrChassisNumber} onChange={e=>setForm({...form,vinOrChassisNumber:e.target.value})}/><TextField fullWidth label="Engine number" value={form.engineNumber} onChange={e=>setForm({...form,engineNumber:e.target.value})}/><TextField fullWidth label="Current meter" type="number" value={form.currentMeterReading} onChange={e=>setForm({...form,currentMeterReading:e.target.value})}/><TextField fullWidth label="Meter unit" placeholder="km or hours" value={form.meterUnit} onChange={e=>setForm({...form,meterUnit:e.target.value})}/></Stack>
          <Typography variant="subtitle2">Ownership and financial baseline</Typography><Stack direction={{xs:'column',sm:'row'}} spacing={2}><TextField fullWidth type="date" label="Acquisition date" InputLabelProps={{shrink:true}} value={form.acquisitionDate} onChange={e=>setForm({...form,acquisitionDate:e.target.value})}/><TextField fullWidth type="number" label="Acquisition cost (FJD)" value={form.acquisitionCost} onChange={e=>setForm({...form,acquisitionCost:e.target.value})}/><TextField fullWidth type="number" label="Current book value (FJD)" value={form.currentBookValue} onChange={e=>setForm({...form,currentBookValue:e.target.value})}/><TextField fullWidth label="Ownership" placeholder="Owned, leased or financed" value={form.ownershipType} onChange={e=>setForm({...form,ownershipType:e.target.value})}/></Stack><Stack direction={{xs:'column',sm:'row'}} spacing={2}><TextField fullWidth label="Insurance policy" value={form.insurancePolicyNumber} onChange={e=>setForm({...form,insurancePolicyNumber:e.target.value})}/><TextField fullWidth type="date" label="Insurance expiry" InputLabelProps={{shrink:true}} value={form.insuranceExpiry} onChange={e=>setForm({...form,insuranceExpiry:e.target.value})}/><TextField fullWidth type="date" label="Warranty expiry" InputLabelProps={{shrink:true}} value={form.warrantyExpiry} onChange={e=>setForm({...form,warrantyExpiry:e.target.value})}/></Stack>
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
            <TextField fullWidth required type="number" label="Daily rate (FJD)" inputProps={{ min: 0, step: '0.01' }} value={form.dailyRate} onChange={(e) => setForm({ ...form, dailyRate: e.target.value })} />
            <TextField fullWidth type="date" label="Next service date" InputLabelProps={{ shrink: true }} value={form.nextServiceDate} onChange={(e) => setForm({ ...form, nextServiceDate: e.target.value })} />
          </Stack>
        </Stack>
      </DialogContent><DialogActions sx={{ p: 3, pt: 1 }}><Button onClick={() => setOpen(false)} disabled={saving}>Cancel</Button><Button type="submit" variant="contained" disabled={saving || !form.divisionId || !form.branchId}>{saving ? 'Saving…' : 'Save asset'}</Button></DialogActions></Box>
    </Dialog>
    <AssetQrLabelDialog asset={qrAsset} open={Boolean(qrAsset)} onClose={() => setQrAsset(null)} />
    <AssetProfileDialog assetId={profileAssetId} onClose={() => setProfileAssetId(null)} />
    <Dialog open={performanceOpen} onClose={()=>setPerformanceOpen(false)} fullWidth maxWidth="md"><DialogTitle>Asset performance</DialogTitle><DialogContent dividers>{performanceLoading?<Box sx={{py:8,display:'grid',placeItems:'center'}}><CircularProgress/></Box>:performance&&<Stack spacing={2.5}><Box><Typography variant="h6" fontWeight={800}>{performance.assetNumber} — {performance.name}</Typography><Typography color="text.secondary">Lifetime operating position based on recorded rentals and costs.</Typography></Box><Stack direction={{xs:'column',sm:'row'}} gap={1}>{[{l:'Rental revenue',v:performance.rentalRevenue},{l:'Recorded expenditure',v:performance.totalExpense},{l:'Operating profit',v:performance.operatingProfit},{l:'Net after acquisition',v:performance.lifetimeNetAfterAcquisition}].map(x=><Card key={x.l} variant="outlined" sx={{flex:1,p:2}}><Typography variant="caption" color="text.secondary">{x.l}</Typography><Typography variant="h6" fontWeight={800} color={x.v<0?'error.main':'text.primary'}>FJD {x.v.toFixed(2)}</Typography></Card>)}</Stack><Stack direction="row" gap={1} flexWrap="wrap"><Chip label={`${performance.rentalCount} rentals`}/><Chip label={`${performance.rentalDays} rental days`}/><Chip label={`${performance.inspectionCount} inspections`}/><Chip label={`Maintenance FJD ${performance.maintenanceExpense.toFixed(2)}`}/><Chip label={`Transport/other FJD ${(performance.operatingExpense+performance.transferExpense).toFixed(2)}`}/></Stack><Typography variant="subtitle1" fontWeight={750}>Recent maintenance</Typography>{performance.maintenance.length?performance.maintenance.slice(0,6).map(x=><Box key={x.id}><Typography fontWeight={650}>{x.jobNumber} · {x.serviceType}</Typography><Typography variant="body2" color="text.secondary">{x.status} · FJD {(x.actualCost??0).toFixed(2)}</Typography></Box>):<Alert severity="info">No maintenance expense has been recorded.</Alert>}<Typography variant="subtitle1" fontWeight={750}>Other asset costs</Typography>{performance.costs.length?performance.costs.slice(0,6).map(x=><Box key={x.id}><Typography fontWeight={650}>{x.category} · {x.description}</Typography><Typography variant="body2" color="text.secondary">{x.occurredOn} · FJD {x.amount.toFixed(2)}</Typography></Box>):<Alert severity="info">No transport, labour or other operating costs have been recorded.</Alert>}</Stack>}</DialogContent><DialogActions><Button onClick={()=>setPerformanceOpen(false)}>Close</Button></DialogActions></Dialog>
  </Box>
}
