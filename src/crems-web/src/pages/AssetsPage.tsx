import { useCallback, useEffect, useState, type FormEvent } from 'react'
import axios from 'axios'
import AddOutlined from '@mui/icons-material/AddOutlined'
import EditOutlined from '@mui/icons-material/EditOutlined'
import QrCode2Outlined from '@mui/icons-material/QrCode2Outlined'
import InsightsOutlined from '@mui/icons-material/InsightsOutlined'
import SearchOutlined from '@mui/icons-material/SearchOutlined'
import ImageOutlined from '@mui/icons-material/ImageOutlined'
import DeleteOutline from '@mui/icons-material/DeleteOutline'
import Inventory2Outlined from '@mui/icons-material/Inventory2Outlined'
import {
  Alert, Box, Button, Card, CardContent, Chip, CircularProgress, Dialog,
  DialogActions, DialogContent, DialogTitle, FormControl, InputAdornment,
  InputLabel, MenuItem, Pagination, Select, Stack, Table, TableBody, TableCell,
  TableContainer, TableHead, TableRow, TextField, IconButton, Typography,
} from '@mui/material'
import { api } from '../api/client'
import { roles } from '../auth/access'
import { AssetQrLabelDialog } from '../components/AssetQrLabelDialog'
import { AssetProfileDialog } from '../components/AssetProfileDialog'

const assetTypes = ['PassengerVehicle','CommercialVehicle','HeavyEquipment','MaterialHandlingEquipment','PowerEquipment','LightEquipment','Scaffolding','PortableSanitation','WasteContainer'] as const
const assetStatuses = ['Available', 'Reserved', 'Rented', 'Inspection', 'Maintenance', 'OutOfService', 'Retired'] as const
type AssetType = typeof assetTypes[number]
type AssetStatus = typeof assetStatuses[number]
type Asset = {
  id: string; assetNumber: string; name: string; type: AssetType; status: AssetStatus
  divisionId: string | null; divisionName: string | null
  branchId: string; branchName: string; registrationNumber: string | null
  serialNumber: string | null; dailyRate: number; defaultBondAmount: number; nextServiceDate: string | null; isActive: boolean
  category: string | null; manufacturer: string | null; model: string | null; modelYear: number | null; vinOrChassisNumber: string | null; engineNumber: string | null
  meterUnit: string | null; currentMeterReading: number | null; acquisitionDate: string | null; acquisitionCost: number; currentBookValue: number | null
  ownershipType: string | null; insurancePolicyNumber: string | null; insuranceExpiry: string | null; warrantyExpiry: string | null
  photoUrlsJson: string; serviceOfferingId: string | null; assetCategoryId: string | null; personnelRequirement: 'None'|'Optional'|'Required'
}
type Branch = { id: string; name: string; isActive: boolean; divisionIds: string[] }
type Division = { id: string; name: string; isActive: boolean }
type AssetCategory = { id:string; code:string; name:string; divisionId:string; serviceOfferingId:string|null; defaultMeterType:string|null; personnelRequirement:'None'|'Optional'|'Required' }
type AssetForm = {
  assetNumber: string; name: string; type: AssetType; status: AssetStatus; divisionId: string; branchId: string
  registrationNumber: string; serialNumber: string; dailyRate: string; defaultBondAmount: string; nextServiceDate: string
  category: string; manufacturer: string; model: string; modelYear: string; vinOrChassisNumber: string; engineNumber: string; meterUnit: string; currentMeterReading: string
  acquisitionDate: string; acquisitionCost: string; currentBookValue: string; ownershipType: string; insurancePolicyNumber: string; insuranceExpiry: string; warrantyExpiry: string; photoUrlsJson: string; serviceOfferingId:string; assetCategoryId:string; personnelRequirement:'None'|'Optional'|'Required'
}
const emptyForm: AssetForm = {
  assetNumber: '', name: '', type: 'PassengerVehicle', status: 'Available', divisionId: '', branchId: '',
  registrationNumber: '', serialNumber: '', dailyRate: '', defaultBondAmount: '0', nextServiceDate: '',
  category: '', manufacturer: '', model: '', modelYear: '', vinOrChassisNumber: '', engineNumber: '', meterUnit: 'km', currentMeterReading: '', acquisitionDate: '', acquisitionCost: '', currentBookValue: '', ownershipType: 'Owned', insurancePolicyNumber: '', insuranceExpiry: '', warrantyExpiry: '', photoUrlsJson: '[]', serviceOfferingId:'', assetCategoryId:'', personnelRequirement:'None',
}
type Performance = { assetNumber:string; name:string; rentalRevenue:number; maintenanceExpense:number; operatingExpense:number; transferExpense:number; totalExpense:number; operatingProfit:number; acquisitionCost:number; currentBookValue:number|null; lifetimeNetAfterAcquisition:number; rentalCount:number; rentalDays:number; inspectionCount:number; maintenance:{id:string;jobNumber:string;serviceType:string;actualCost:number|null;status:string}[]; costs:{id:string;category:string;description:string;amount:number;occurredOn:string}[] }
type AssetSummary = { total:number;available:number;onHire:number;reserved:number;maintenance:number;inspection:number;outOfService:number;categories:string[] }

const statusColors: Partial<Record<AssetStatus, 'success' | 'warning' | 'error' | 'info' | 'default'>> = {
  Available: 'success', Reserved: 'info', Rented: 'warning', Maintenance: 'error', Retired: 'default',
}

export function AssetsPage({ userRoles }: { userRoles: string[] }) {
  const [assets, setAssets] = useState<Asset[]>([])
  const [branches, setBranches] = useState<Branch[]>([])
  const [divisions, setDivisions] = useState<Division[]>([])
  const [assetCategories,setAssetCategories]=useState<AssetCategory[]>([])
  const [loading, setLoading] = useState(true)
  const [open, setOpen] = useState(false)
  const [saving, setSaving] = useState(false)
  const [editing, setEditing] = useState<Asset | null>(null)
  const [form, setForm] = useState<AssetForm>(emptyForm)
  const [search, setSearch] = useState('')
  const [debouncedSearch, setDebouncedSearch] = useState('')
  const [page, setPage] = useState(1)
  const [totalAssets, setTotalAssets] = useState(0)
  const [summary,setSummary]=useState<AssetSummary>({total:0,available:0,onHire:0,reserved:0,maintenance:0,inspection:0,outOfService:0,categories:[]})
  const [selectedAssetId,setSelectedAssetId]=useState<string|null>(null)
  const [divisionFilter,setDivisionFilter]=useState('')
  const [branchFilter,setBranchFilter]=useState('')
  const [categoryFilter,setCategoryFilter]=useState('')
  const [statusFilter,setStatusFilter]=useState('')
  const [error, setError] = useState('')
  const [qrAsset, setQrAsset] = useState<Asset | null>(null)
  const [profileAssetId, setProfileAssetId] = useState<string | null>(null)
  const [performance, setPerformance] = useState<Performance | null>(null); const [performanceOpen,setPerformanceOpen]=useState(false); const [performanceLoading,setPerformanceLoading]=useState(false)
  const canManage = userRoles.includes(roles.superAdministrator) || userRoles.includes(roles.administrator) || userRoles.includes(roles.branchManager)
  const canManagePhotos = canManage || userRoles.includes(roles.rentalOfficer)
  const [photoAsset, setPhotoAsset] = useState<Asset | null>(null)
  const [photoSaving, setPhotoSaving] = useState(false)

  const loadData = useCallback(async () => {
    setLoading(true)
    try {
      const assetResponse = await api.get<Asset[]>('/assets', { params: { search: debouncedSearch || undefined, divisionId:divisionFilter||undefined, branchId:branchFilter||undefined, category:categoryFilter||undefined, status:statusFilter||undefined, page, pageSize: 50 } })
      setAssets(assetResponse.data)
      setTotalAssets(Number(assetResponse.headers['x-total-count'] ?? assetResponse.data.length))
      setSelectedAssetId(current=>assetResponse.data.some(asset=>asset.id===current)?current:assetResponse.data[0]?.id??null)
    } catch { setError('Unable to load the asset register. Confirm that the backend is running.') }
    finally { setLoading(false) }
  }, [debouncedSearch, page, divisionFilter, branchFilter, categoryFilter, statusFilter])
  const loadSummary = useCallback(async () => {
    const response = await api.get<AssetSummary>('/assets/summary')
    setSummary(response.data)
  }, [])

  useEffect(() => {
    // Initial loading synchronizes the register with the API.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void loadData()
  }, [loadData])

  useEffect(() => {
    void loadSummary()
      .catch(() => setError('Unable to load the fleet summary.'))
  }, [loadSummary])

  useEffect(() => {
    const timer = window.setTimeout(() => setDebouncedSearch(search.trim()), 300)
    return () => window.clearTimeout(timer)
  }, [search])

  useEffect(() => {
    if (!canManage) return
    void Promise.all([api.get<Branch[]>('/branches'), api.get<Division[]>('/divisions'),api.get<AssetCategory[]>('/asset-categories')]).then(([branchResponse, divisionResponse,categoryResponse]) => {
      setBranches(branchResponse.data.filter(branch => branch.isActive))
      setDivisions(divisionResponse.data.filter(division => division.isActive))
      setAssetCategories(categoryResponse.data)
    }).catch(() => setError('Unable to load branch and division options.'))
  }, [canManage])

  function openCreate() {
    const divisionId = divisions[0]?.id ?? ''; setEditing(null); setForm({ ...emptyForm, divisionId, branchId: branches.find(x => x.divisionIds.includes(divisionId))?.id ?? '' }); setError(''); setOpen(true)
  }

  function openEdit(asset: Asset) {
    setEditing(asset)
    setForm({ assetNumber: asset.assetNumber, name: asset.name, type: asset.type, status: asset.status,
      divisionId: asset.divisionId ?? '', branchId: asset.branchId, registrationNumber: asset.registrationNumber ?? '', serialNumber: asset.serialNumber ?? '',
      dailyRate: String(asset.dailyRate), defaultBondAmount: String(asset.defaultBondAmount ?? 0), nextServiceDate: asset.nextServiceDate ?? '', category:asset.category??'',manufacturer:asset.manufacturer??'',model:asset.model??'',modelYear:asset.modelYear?String(asset.modelYear):'',vinOrChassisNumber:asset.vinOrChassisNumber??'',engineNumber:asset.engineNumber??'',meterUnit:asset.meterUnit??'',currentMeterReading:asset.currentMeterReading==null?'':String(asset.currentMeterReading),acquisitionDate:asset.acquisitionDate??'',acquisitionCost:String(asset.acquisitionCost??0),currentBookValue:asset.currentBookValue==null?'':String(asset.currentBookValue),ownershipType:asset.ownershipType??'',insurancePolicyNumber:asset.insurancePolicyNumber??'',insuranceExpiry:asset.insuranceExpiry??'',warrantyExpiry:asset.warrantyExpiry??'',photoUrlsJson:asset.photoUrlsJson??'[]',serviceOfferingId:asset.serviceOfferingId??'',assetCategoryId:asset.assetCategoryId??'',personnelRequirement:asset.personnelRequirement })
    setError(''); setOpen(true)
  }

  async function save(event: FormEvent) {
    event.preventDefault(); setSaving(true); setError('')
    try {
      const payload = { ...form, dailyRate: Number(form.dailyRate), defaultBondAmount: Number(form.defaultBondAmount || 0), nextServiceDate: form.nextServiceDate || null, modelYear:form.modelYear?Number(form.modelYear):null,currentMeterReading:form.currentMeterReading?Number(form.currentMeterReading):null,acquisitionDate:form.acquisitionDate||null,acquisitionCost:Number(form.acquisitionCost||0),currentBookValue:form.currentBookValue?Number(form.currentBookValue):null,insuranceExpiry:form.insuranceExpiry||null,warrantyExpiry:form.warrantyExpiry||null }
      if (editing) await api.put(`/assets/${editing.id}`, payload)
      else await api.post('/assets', payload)
      setOpen(false); await Promise.all([loadData(), loadSummary()])
    } catch (requestError: unknown) {
      const data = axios.isAxiosError(requestError) ? requestError.response?.data : undefined
      const errors = data?.errors as Record<string, string[]> | undefined
      setError(errors ? Object.values(errors).flat().join(' ') : 'Unable to save the asset.')
    } finally { setSaving(false) }
  }
  async function openPerformance(asset:Asset){setPerformanceOpen(true);setPerformanceLoading(true);setPerformance(null);setError('');try{setPerformance((await api.get<Performance>(`/assets/${asset.id}/performance`)).data)}catch{setError('Unable to load this asset’s performance ledger.')}finally{setPerformanceLoading(false)}}
  function photoUrls(asset: Asset | null) { try { return JSON.parse(asset?.photoUrlsJson || '[]') as string[] } catch { return [] } }
  async function uploadAssetPhotos(selection?: File[] | null) {
    if (!photoAsset || !selection?.length) return
    const files = selection
    const remaining = 8 - photoUrls(photoAsset).length
    if (files.length > remaining) { setError(`You can upload ${remaining} more ${remaining === 1 ? 'photo' : 'photos'} for this asset.`); return }
    setPhotoSaving(true); setError('')
    let updated = photoAsset
    try {
      for (const file of files) {
        const formData = new FormData(); formData.append('file', file)
        const response = await api.post<{ photoUrls: string[] }>(`/assets/${photoAsset.id}/photos`, formData)
        updated = { ...updated, photoUrlsJson: JSON.stringify(response.data.photoUrls) }
        setPhotoAsset(updated)
        setAssets(current => current.map(asset => asset.id === updated.id ? updated : asset))
      }
    } catch (reason) {
      const data = axios.isAxiosError(reason) ? reason.response?.data : undefined
      setError(data?.message ?? 'One or more asset photos could not be uploaded.')
    } finally { setPhotoSaving(false) }
  }
  async function deleteAssetPhoto(url: string) { if (!photoAsset) return; const fileName = url.split('/').pop(); if (!fileName) return; setPhotoSaving(true); setError(''); try { const response = await api.delete<{ photoUrls: string[] }>(`/assets/${photoAsset.id}/photos/${encodeURIComponent(fileName)}`); const updated = { ...photoAsset, photoUrlsJson: JSON.stringify(response.data.photoUrls) }; setPhotoAsset(updated); setAssets(current => current.map(asset => asset.id === updated.id ? updated : asset)) } catch { setError('Unable to remove the asset photo.') } finally { setPhotoSaving(false) } }

  const selectedAsset=assets.find(asset=>asset.id===selectedAssetId)??null
  const divisionOptions=divisions.length?divisions.map(item=>({id:item.id,name:item.name})):Array.from(new Map(assets.filter(item=>item.divisionId).map(item=>[item.divisionId!,{id:item.divisionId!,name:item.divisionName??'Division'}])).values())
  const branchOptions=branches.length?branches.map(item=>({id:item.id,name:item.name})):Array.from(new Map(assets.map(item=>[item.branchId,{id:item.branchId,name:item.branchName}])).values())

  return <Box sx={{
    p: { xs: 2, sm: 3, lg: 4 },
    maxWidth: 1500,
    mx: 'auto',
  }}>
    <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" gap={2} mb={3}>
      <Box><Typography variant="overline" color="text.secondary" fontWeight={800}>Motors & Carptrac · {summary.total.toLocaleString()} active assets</Typography><Typography variant="h4" fontWeight={850}>Asset register</Typography>
        <Typography color="text.secondary" mt={0.5}>Search, compare and manage every asset within your assigned operating scope.</Typography></Box>
      {canManage && <Button variant="contained" startIcon={<AddOutlined />} onClick={openCreate}>Add asset</Button>}
    </Stack>
    {error && !open && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
    <Box sx={{display:'grid',gridTemplateColumns:{xs:'repeat(2,minmax(0,1fr))',md:'repeat(5,minmax(0,1fr))'},gap:2,mb:2.5}}>{[
      ['Total assets',summary.total,'text.primary'],['Available',summary.available,'success.main'],['On hire',summary.onHire,'info.main'],['Maintenance',summary.maintenance,'warning.dark'],['Inspection / unavailable',summary.inspection+summary.outOfService,'error.main'],
    ].map(([label,value,color])=><Card key={String(label)} variant="outlined"><CardContent sx={{p:2.25}}><Typography variant="h4" fontWeight={850} color={String(color)}>{value}</Typography><Typography variant="caption" color="text.secondary" fontWeight={750}>{label}</Typography></CardContent></Card>)}</Box>
    <Box sx={{display:'grid',gridTemplateColumns:{xs:'1fr',xl:'minmax(0,1fr) 390px'},gap:2,alignItems:'start'}}><Card variant="outlined"><CardContent sx={{ p: 0 }}>
      <Stack direction={{xs:'column',lg:'row'}} gap={1.25} sx={{ p: 2, borderBottom: 1, borderColor: 'divider' }}><TextField size="small" placeholder="Search asset, registration, VIN or serial" value={search}
        onChange={(event) => { setSearch(event.target.value); setPage(1) }} sx={{ flex:1,minWidth:220 }}
        InputProps={{ startAdornment: <InputAdornment position="start"><SearchOutlined /></InputAdornment> }} /><TextField select size="small" label="Division" value={divisionFilter} onChange={event=>{setDivisionFilter(event.target.value);setBranchFilter('');setPage(1)}} sx={{minWidth:145}}><MenuItem value="">All divisions</MenuItem>{divisionOptions.map(item=><MenuItem key={item.id} value={item.id}>{item.name}</MenuItem>)}</TextField><TextField select size="small" label="Category" value={categoryFilter} onChange={event=>{setCategoryFilter(event.target.value);setPage(1)}} sx={{minWidth:140}}><MenuItem value="">All categories</MenuItem>{summary.categories.map(item=><MenuItem key={item} value={item}>{item}</MenuItem>)}</TextField><TextField select size="small" label="Status" value={statusFilter} onChange={event=>{setStatusFilter(event.target.value);setPage(1)}} sx={{minWidth:135}}><MenuItem value="">All statuses</MenuItem>{assetStatuses.filter(item=>item!=='Retired').map(item=><MenuItem key={item} value={item}>{item.replace(/([a-z])([A-Z])/g,'$1 $2')}</MenuItem>)}</TextField><TextField select size="small" label="Branch" value={branchFilter} onChange={event=>{setBranchFilter(event.target.value);setPage(1)}} sx={{minWidth:135}}><MenuItem value="">All branches</MenuItem>{branchOptions.map(item=><MenuItem key={item.id} value={item.id}>{item.name}</MenuItem>)}</TextField></Stack>
      {loading ? <Box sx={{ minHeight: 280, display: 'grid', placeItems: 'center' }}><CircularProgress /></Box> :
        <TableContainer><Table><TableHead><TableRow sx={{ bgcolor: '#f6f6f3' }}>
          <TableCell>Asset</TableCell><TableCell>Category</TableCell><TableCell>Branch</TableCell><TableCell>Meter</TableCell><TableCell>Status</TableCell><TableCell align="right">Rate</TableCell><TableCell align="center">QR label</TableCell>
        </TableRow></TableHead><TableBody>
          {assets.length === 0 && <TableRow><TableCell colSpan={7} align="center" sx={{ py: 8, color: 'text.secondary' }}>No matching assets found.</TableCell></TableRow>}
          {assets.map((asset) => <TableRow key={asset.id} hover selected={asset.id===selectedAssetId} onClick={()=>setSelectedAssetId(asset.id)} sx={{cursor:'pointer'}}>
            <TableCell><Stack direction="row" gap={1.25} alignItems="center">{photoUrls(asset)[0]?<Box component="img" src={photoUrls(asset)[0]} alt="" sx={{width:42,height:42,borderRadius:1.5,objectFit:'contain',bgcolor:'grey.100'}}/>:<Box sx={{width:42,height:42,borderRadius:1.5,bgcolor:'grey.100',display:'grid',placeItems:'center'}}><Inventory2Outlined fontSize="small"/></Box>}<Box><Typography fontWeight={750}>{asset.name}</Typography><Typography variant="caption" color="text.secondary">{asset.assetNumber} · {asset.registrationNumber||asset.serialNumber||'No plate/serial'}</Typography></Box></Stack></TableCell>
            <TableCell><Typography variant="body2" fontWeight={650}>{asset.category||asset.type}</Typography><Typography variant="caption" color="text.secondary">{asset.divisionName||'Unassigned'}</Typography></TableCell><TableCell>{asset.branchName}</TableCell><TableCell>{asset.currentMeterReading==null?'—':`${asset.currentMeterReading.toLocaleString()} ${asset.meterUnit||''}`}</TableCell>
            <TableCell><Chip size="small" label={asset.status.replace(/([a-z])([A-Z])/g, '$1 $2')} color={statusColors[asset.status] ?? 'default'} variant="outlined" /></TableCell>
            <TableCell align="right"><Typography fontWeight={750}>FJD {asset.dailyRate.toFixed(2)}</Typography><Typography variant="caption" color="text.secondary">per day</Typography></TableCell><TableCell align="center"><IconButton aria-label={`View QR label for ${asset.assetNumber}`} onClick={event=>{event.stopPropagation();setQrAsset(asset)}}><QrCode2Outlined /></IconButton></TableCell>
          </TableRow>)}
        </TableBody></Table></TableContainer>}
      {totalAssets > 50 && <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{p:2,borderTop:1,borderColor:'divider'}}><Typography variant="body2" color="text.secondary">{totalAssets.toLocaleString()} assets</Typography><Pagination page={page} count={Math.ceil(totalAssets / 50)} onChange={(_, value) => setPage(value)} color="primary" /></Stack>}
    </CardContent></Card><Card variant="outlined" sx={{position:{xl:'sticky'},top:{xl:84},overflow:'hidden',display:{xs:selectedAsset?'block':'none',xl:'block'}}}>{selectedAsset?<><Box sx={{height:190,bgcolor:'grey.100',position:'relative'}}>{photoUrls(selectedAsset)[0]&&<Box component="img" src={photoUrls(selectedAsset)[0]} alt={selectedAsset.name} sx={{width:'100%',height:'100%',objectFit:'contain'}}/>}<Chip size="small" label={selectedAsset.status.replace(/([a-z])([A-Z])/g,'$1 $2')} color={statusColors[selectedAsset.status]??'default'} sx={{position:'absolute',top:14,left:14,bgcolor:'white'}}/><IconButton aria-label="QR label" onClick={()=>setQrAsset(selectedAsset)} sx={{position:'absolute',right:12,bottom:12,bgcolor:'white','&:hover':{bgcolor:'grey.100'}}}><QrCode2Outlined/></IconButton></Box><CardContent sx={{p:2.5}}><Typography variant="overline" color="text.secondary">{selectedAsset.assetNumber}</Typography><Typography variant="h5" fontWeight={850}>{selectedAsset.name}</Typography><Typography variant="body2" color="text.secondary">{selectedAsset.registrationNumber||selectedAsset.serialNumber||'Identification pending'} · {selectedAsset.branchName}</Typography><Stack direction="row" gap={1} mt={2} flexWrap="wrap"><Button variant="contained" size="small" onClick={()=>setProfileAssetId(selectedAsset.id)}>Open full profile</Button>{canManage&&<Button variant="outlined" size="small" startIcon={<EditOutlined/>} onClick={()=>openEdit(selectedAsset)}>Edit</Button>}</Stack><Box sx={{display:'grid',gridTemplateColumns:'1fr 1fr',gap:2,mt:3}}>{[['Division',selectedAsset.divisionName||'—'],['Category',selectedAsset.category||selectedAsset.type],['Meter',selectedAsset.currentMeterReading==null?'Not recorded':`${selectedAsset.currentMeterReading.toLocaleString()} ${selectedAsset.meterUnit||''}`],['Rate',`FJD ${selectedAsset.dailyRate.toFixed(2)}/day`],['Refundable bond',`FJD ${(selectedAsset.defaultBondAmount??0).toFixed(2)}`],['Next service',selectedAsset.nextServiceDate?new Date(selectedAsset.nextServiceDate).toLocaleDateString('en-FJ'):'Not scheduled'],['Model',[selectedAsset.manufacturer,selectedAsset.model,selectedAsset.modelYear].filter(Boolean).join(' ')||'—']].map(([label,value])=><Box key={label}><Typography variant="caption" color="text.secondary">{label}</Typography><Typography variant="body2" fontWeight={750}>{value}</Typography></Box>)}</Box>{canManagePhotos&&<Stack direction="row" gap={1} mt={3} flexWrap="wrap"><Button size="small" startIcon={<ImageOutlined/>} onClick={()=>setPhotoAsset(selectedAsset)}>Photos</Button>{canManage&&<><Button size="small" startIcon={<InsightsOutlined/>} onClick={()=>void openPerformance(selectedAsset)}>Financials</Button><Button size="small" startIcon={<QrCode2Outlined/>} onClick={()=>setQrAsset(selectedAsset)}>QR label</Button></>}</Stack>}</CardContent></>:<CardContent><Typography color="text.secondary">Choose an asset to see its details.</Typography></CardContent>}</Card></Box>
    <Dialog open={open} onClose={() => !saving && setOpen(false)} fullWidth maxWidth="md">
      <Box component="form" onSubmit={save}><DialogTitle>{editing ? 'Edit asset' : 'Add asset'}</DialogTitle><DialogContent>
        <Stack spacing={2.25} mt={1}>{error && <Alert severity="error">{error}</Alert>}
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
            <TextField fullWidth required label="Asset number" value={form.assetNumber} onChange={(e) => setForm({ ...form, assetNumber: e.target.value })} />
            <TextField fullWidth required label="Asset name" value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} />
          </Stack>
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
            <FormControl fullWidth required><InputLabel>Division</InputLabel><Select label="Division" value={form.divisionId} onChange={(e) => { const divisionId = e.target.value; setForm({ ...form, divisionId, branchId: branches.find(x => x.divisionIds.includes(divisionId))?.id ?? '', assetCategoryId:'', serviceOfferingId:'', category:'' }) }}>{divisions.map(value => <MenuItem key={value.id} value={value.id}>{value.name}</MenuItem>)}</Select></FormControl>
            <FormControl fullWidth required><InputLabel>Asset category</InputLabel><Select label="Asset category" value={form.assetCategoryId} onChange={(e)=>{const selected=assetCategories.find(x=>x.id===e.target.value);setForm({...form,assetCategoryId:e.target.value,serviceOfferingId:selected?.serviceOfferingId??'',category:selected?.name??'',meterUnit:selected?.defaultMeterType??form.meterUnit,personnelRequirement:selected?.personnelRequirement??'None',type:selected?.code==='RENTAL_VEHICLE'?'PassengerVehicle':selected?.code==='HEAVY_MACHINE'?'HeavyEquipment':selected?.code==='FORKLIFT'?'MaterialHandlingEquipment':selected?.code==='GENSET'?'PowerEquipment':selected?.code==='SCAFFOLD'?'Scaffolding':selected?.code==='PORTABLE_TOILET'?'PortableSanitation':selected?.code==='BIG_BIN'?'WasteContainer':'LightEquipment'})}}>{assetCategories.filter(x=>x.divisionId===form.divisionId).map(value=><MenuItem key={value.id} value={value.id}>{value.name} ({value.personnelRequirement==='Required'?'operator required':value.personnelRequirement==='Optional'?'operator optional':'no operator'})</MenuItem>)}</Select></FormControl>
            <FormControl fullWidth required><InputLabel>Operator provision</InputLabel><Select label="Operator provision" value={form.personnelRequirement} onChange={e=>setForm({...form,personnelRequirement:e.target.value as AssetForm['personnelRequirement']})}><MenuItem value="None">No operator</MenuItem><MenuItem value="Optional">Customer may add an operator</MenuItem><MenuItem value="Required">Carpenters operator included</MenuItem></Select></FormControl>
            <FormControl fullWidth><InputLabel>Status</InputLabel><Select label="Status" value={form.status} onChange={(e) => setForm({ ...form, status: e.target.value as AssetStatus })}>{assetStatuses.map((value) => <MenuItem key={value} value={value}>{value.replace(/([a-z])([A-Z])/g, '$1 $2')}</MenuItem>)}</Select></FormControl>
            <FormControl fullWidth required><InputLabel>Branch</InputLabel><Select label="Branch" value={form.branchId} onChange={(e) => setForm({ ...form, branchId: e.target.value })}>{branches.filter(branch => branch.divisionIds.includes(form.divisionId)).map((branch) => <MenuItem key={branch.id} value={branch.id}>{branch.name}</MenuItem>)}</Select></FormControl>
          </Stack>
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
            <TextField fullWidth label="Registration number" value={form.registrationNumber} onChange={(e) => setForm({ ...form, registrationNumber: e.target.value })} />
            <TextField fullWidth label="Serial number" value={form.serialNumber} onChange={(e) => setForm({ ...form, serialNumber: e.target.value })} />
          </Stack>
          <Typography variant="subtitle2">Identification and specifications</Typography><Stack direction={{xs:'column',sm:'row'}} spacing={2}><TextField fullWidth label="Classification" value={form.category} disabled helperText="Controls booking, inspection and maintenance requirements"/><TextField fullWidth label="Manufacturer" value={form.manufacturer} onChange={e=>setForm({...form,manufacturer:e.target.value})}/><TextField fullWidth label="Model" value={form.model} onChange={e=>setForm({...form,model:e.target.value})}/><TextField fullWidth type="number" label="Model year" value={form.modelYear} onChange={e=>setForm({...form,modelYear:e.target.value})}/></Stack><Stack direction={{xs:'column',sm:'row'}} spacing={2}><TextField fullWidth label="VIN / chassis number" value={form.vinOrChassisNumber} onChange={e=>setForm({...form,vinOrChassisNumber:e.target.value})}/><TextField fullWidth label="Engine number" value={form.engineNumber} onChange={e=>setForm({...form,engineNumber:e.target.value})}/><TextField fullWidth label="Current meter" type="number" value={form.currentMeterReading} onChange={e=>setForm({...form,currentMeterReading:e.target.value})}/><TextField fullWidth label="Meter unit" placeholder="km or hours" value={form.meterUnit} onChange={e=>setForm({...form,meterUnit:e.target.value})}/></Stack>
          <Typography variant="subtitle2">Ownership and financial baseline</Typography><Stack direction={{xs:'column',sm:'row'}} spacing={2}><TextField fullWidth type="date" label="Acquisition date" InputLabelProps={{shrink:true}} value={form.acquisitionDate} onChange={e=>setForm({...form,acquisitionDate:e.target.value})}/><TextField fullWidth type="number" label="Acquisition cost (FJD)" value={form.acquisitionCost} onChange={e=>setForm({...form,acquisitionCost:e.target.value})}/><TextField fullWidth type="number" label="Current book value (FJD)" value={form.currentBookValue} onChange={e=>setForm({...form,currentBookValue:e.target.value})}/><TextField fullWidth label="Ownership" placeholder="Owned, leased or financed" value={form.ownershipType} onChange={e=>setForm({...form,ownershipType:e.target.value})}/></Stack><Stack direction={{xs:'column',sm:'row'}} spacing={2}><TextField fullWidth label="Insurance policy" value={form.insurancePolicyNumber} onChange={e=>setForm({...form,insurancePolicyNumber:e.target.value})}/><TextField fullWidth type="date" label="Insurance expiry" InputLabelProps={{shrink:true}} value={form.insuranceExpiry} onChange={e=>setForm({...form,insuranceExpiry:e.target.value})}/><TextField fullWidth type="date" label="Warranty expiry" InputLabelProps={{shrink:true}} value={form.warrantyExpiry} onChange={e=>setForm({...form,warrantyExpiry:e.target.value})}/></Stack>
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
            <TextField fullWidth required type="number" label="Daily rate (FJD)" inputProps={{ min: 0, step: '0.01' }} value={form.dailyRate} onChange={(e) => setForm({ ...form, dailyRate: e.target.value })} />
            <TextField fullWidth required type="number" label="Refundable bond (FJD)" helperText="Overrides the service default for this asset" inputProps={{ min: 0, step: '0.01' }} value={form.defaultBondAmount} onChange={(e) => setForm({ ...form, defaultBondAmount: e.target.value })} />
            <TextField fullWidth type="date" label="Next service date" InputLabelProps={{ shrink: true }} value={form.nextServiceDate} onChange={(e) => setForm({ ...form, nextServiceDate: e.target.value })} />
          </Stack>
        </Stack>
      </DialogContent><DialogActions sx={{ p: 3, pt: 1 }}><Button onClick={() => setOpen(false)} disabled={saving}>Cancel</Button><Button type="submit" variant="contained" disabled={saving || !form.divisionId || !form.branchId}>{saving ? 'Saving…' : 'Save asset'}</Button></DialogActions></Box>
    </Dialog>
    <AssetQrLabelDialog asset={qrAsset} open={Boolean(qrAsset)} onClose={() => setQrAsset(null)} />
    <AssetProfileDialog assetId={profileAssetId} onClose={() => setProfileAssetId(null)} />
    <Dialog open={Boolean(photoAsset)} onClose={() => !photoSaving && setPhotoAsset(null)} fullWidth maxWidth="md"><DialogTitle>Asset photos · {photoAsset?.assetNumber}</DialogTitle><DialogContent dividers><Typography color="text.secondary" mb={2}>Upload up to eight clear photos. Every photo is displayed in full without cropping. The first photo is the catalogue cover and customers can browse the full gallery.</Typography>{error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}<Stack direction="row" flexWrap="wrap" gap={2}>{photoUrls(photoAsset).map((url, index) => <Card key={url} variant="outlined" sx={{ width: { xs: '100%', sm: 220 }, overflow: 'hidden' }}><Box component="img" src={url} alt={`${photoAsset?.name} photo ${index + 1}`} sx={{ width: '100%', height: 165, objectFit: 'contain', bgcolor: 'grey.100', display: 'block' }} /><CardContent sx={{ p: 1.5 }}><Stack direction="row" alignItems="center" justifyContent="space-between"><Chip size="small" label={index === 0 ? 'Catalogue cover' : `Photo ${index + 1}`} color={index === 0 ? 'secondary' : 'default'} /><IconButton size="small" color="error" disabled={photoSaving} aria-label={`Remove photo ${index + 1}`} onClick={() => void deleteAssetPhoto(url)}><DeleteOutline /></IconButton></Stack></CardContent></Card>)}{photoUrls(photoAsset).length === 0 && <Alert severity="info" sx={{ width: '100%' }}>No photos uploaded. Customers will see a neutral “photo coming soon” placeholder.</Alert>}</Stack></DialogContent><DialogActions sx={{ p: 2 }}><Button component="label" variant="contained" startIcon={<ImageOutlined />} disabled={photoSaving || photoUrls(photoAsset).length >= 8}>{photoSaving ? 'Uploading…' : 'Upload photos'}<input hidden multiple type="file" accept="image/jpeg,image/png,image/webp" onChange={event => { const files = Array.from(event.target.files ?? []); event.target.value = ''; void uploadAssetPhotos(files) }} /></Button><Typography variant="caption" color="text.secondary">{photoUrls(photoAsset).length}/8 uploaded</Typography><Box sx={{ flex: 1 }} /><Button onClick={() => setPhotoAsset(null)} disabled={photoSaving}>Close</Button></DialogActions></Dialog>
    <Dialog open={performanceOpen} onClose={()=>setPerformanceOpen(false)} fullWidth maxWidth="md"><DialogTitle>Asset performance</DialogTitle><DialogContent dividers>{performanceLoading?<Box sx={{py:8,display:'grid',placeItems:'center'}}><CircularProgress/></Box>:performance&&<Stack spacing={2.5}><Box><Typography variant="h6" fontWeight={800}>{performance.assetNumber} — {performance.name}</Typography><Typography color="text.secondary">Lifetime operating position based on recorded rentals and costs.</Typography></Box><Stack direction={{xs:'column',sm:'row'}} gap={1}>{[{l:'Rental revenue',v:performance.rentalRevenue},{l:'Recorded expenditure',v:performance.totalExpense},{l:'Operating profit',v:performance.operatingProfit},{l:'Net after acquisition',v:performance.lifetimeNetAfterAcquisition}].map(x=><Card key={x.l} variant="outlined" sx={{flex:1,p:2}}><Typography variant="caption" color="text.secondary">{x.l}</Typography><Typography variant="h6" fontWeight={800} color={x.v<0?'error.main':'text.primary'}>FJD {x.v.toFixed(2)}</Typography></Card>)}</Stack><Stack direction="row" gap={1} flexWrap="wrap"><Chip label={`${performance.rentalCount} rentals`}/><Chip label={`${performance.rentalDays} rental days`}/><Chip label={`${performance.inspectionCount} inspections`}/><Chip label={`Maintenance FJD ${performance.maintenanceExpense.toFixed(2)}`}/><Chip label={`Transport/other FJD ${(performance.operatingExpense+performance.transferExpense).toFixed(2)}`}/></Stack><Typography variant="subtitle1" fontWeight={750}>Recent maintenance</Typography>{performance.maintenance.length?performance.maintenance.slice(0,6).map(x=><Box key={x.id}><Typography fontWeight={650}>{x.jobNumber} · {x.serviceType}</Typography><Typography variant="body2" color="text.secondary">{x.status} · FJD {(x.actualCost??0).toFixed(2)}</Typography></Box>):<Alert severity="info">No maintenance expense has been recorded.</Alert>}<Typography variant="subtitle1" fontWeight={750}>Other asset costs</Typography>{performance.costs.length?performance.costs.slice(0,6).map(x=><Box key={x.id}><Typography fontWeight={650}>{x.category} · {x.description}</Typography><Typography variant="body2" color="text.secondary">{x.occurredOn} · FJD {x.amount.toFixed(2)}</Typography></Box>):<Alert severity="info">No transport, labour or other operating costs have been recorded.</Alert>}</Stack>}</DialogContent><DialogActions><Button onClick={()=>setPerformanceOpen(false)}>Close</Button></DialogActions></Dialog>
  </Box>
}
