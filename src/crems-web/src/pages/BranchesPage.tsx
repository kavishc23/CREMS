import { useEffect, useState, type FormEvent } from 'react'
import axios from 'axios'
import ExpandMoreOutlined from '@mui/icons-material/ExpandMoreOutlined'
import AddOutlined from '@mui/icons-material/AddOutlined'
import EditOutlined from '@mui/icons-material/EditOutlined'
import StoreOutlined from '@mui/icons-material/StoreOutlined'
import {
  Alert, Box, Button, Card, CardContent, Chip, CircularProgress, Dialog,
  DialogActions, DialogContent, DialogTitle, FormControlLabel, Checkbox, Accordion, AccordionSummary, AccordionDetails, IconButton, Stack, Switch,
  Table, TableBody, TableCell, TableContainer, TableHead, TableRow,
  TextField, Tooltip, Typography,
} from '@mui/material'
import { api } from '../api/client'
import { roles } from '../auth/access'
import { PageHeader } from '../components/PageHeader'

type Branch = {
  id: string
  code: string
  name: string
  address: string | null
  phone: string | null
  isActive: boolean
  divisionIds: string[]
  postalAddress: string | null; email: string | null; latitude: number | null; longitude: number | null; branchManagerUserId: string | null; pickupInstructions: string | null; returnInstructions: string | null; deliveryCoverage: string | null; isPublic: boolean
}

type Division = { id: string; name: string; isActive: boolean; services:{id:string;name:string;isActive:boolean}[] }
type BranchForm = { code: string; name: string; address: string; postalAddress: string; phone: string; email: string; latitude: string; longitude: string; branchManagerUserId: string; pickupInstructions: string; returnInstructions: string; deliveryCoverage: string; isPublic: boolean; divisionIds: string[] }
const emptyForm: BranchForm = { code: '', name: '', address: '', postalAddress: '', phone: '', email: '', latitude: '', longitude: '', branchManagerUserId: '', pickupInstructions: '', returnInstructions: '', deliveryCoverage: '', isPublic: true, divisionIds: [] }

export function BranchesPage({ userRoles, onSaved, onConfigure }: { userRoles: string[]; onSaved?:()=>void; onConfigure?:(id:string)=>void }) {
  const [branches, setBranches] = useState<Branch[]>([])
  const [categories,setCategories]=useState<{name:string;divisionId:string;serviceOfferingId:string|null}[]>([])
  const [divisions, setDivisions] = useState<Division[]>([])
  const [loading, setLoading] = useState(true)
  const [open, setOpen] = useState(false)
  const [saving, setSaving] = useState(false)
  const [editing, setEditing] = useState<Branch | null>(null)
  const [form, setForm] = useState<BranchForm>(emptyForm)
  const [error, setError] = useState('')
  const [serviceIds,setServiceIds]=useState<string[]>([]),[serviceReady,setServiceReady]=useState(true)
  const isAdministrator = userRoles.includes(roles.superAdministrator) || userRoles.includes(roles.administrator)

  async function loadBranches() {
    try {
      const [response, divisionResponse,categoryResponse] = await Promise.all([api.get<Branch[]>('/branches'), api.get<Division[]>('/divisions'),api.get<{name:string;divisionId:string;serviceOfferingId:string|null}[]>('/asset-categories')])
      setCategories(categoryResponse.data);setBranches(response.data); setDivisions(divisionResponse.data.filter(x => x.isActive))
    } catch {
      setError('Unable to load branches. Confirm that the backend is running.')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    // Initial loading synchronizes the page with the branch administration API.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void loadBranches()
  }, [])

  useEffect(()=>{if(!open||!editing||!isAdministrator)return;let active=true;void api.get<{services:{serviceOfferingId:string;isActive:boolean}[]}>(`/branches/${editing.id}/configuration`).then(r=>{if(active){setServiceIds(r.data.services.filter(s=>s.isActive).map(s=>s.serviceOfferingId));setServiceReady(true)}}).catch(()=>{if(active)setError('Operating services could not be loaded. Reopen the branch before saving.')});return()=>{active=false}},[open,editing,isAdministrator])
  function openCreate() {
    setEditing(null); setServiceIds([]);setServiceReady(true); setForm(emptyForm); setError(''); setOpen(true)
  }

  function openEdit(branch: Branch) {
    setEditing(branch)
    setServiceReady(!isAdministrator);setServiceIds([]);
    setForm({ code: branch.code, name: branch.name, address: branch.address ?? '', postalAddress: branch.postalAddress ?? '', phone: branch.phone ?? '', email: branch.email ?? '', latitude: branch.latitude?.toString() ?? '', longitude: branch.longitude?.toString() ?? '', branchManagerUserId: branch.branchManagerUserId ?? '', pickupInstructions: branch.pickupInstructions ?? '', returnInstructions: branch.returnInstructions ?? '', deliveryCoverage: branch.deliveryCoverage ?? '', isPublic: branch.isPublic, divisionIds: branch.divisionIds })
    setError(''); setOpen(true)
  }

  async function save(event: FormEvent) {
    event.preventDefault(); if (!form.divisionIds.length) { setError('Assign at least one business/division to this branch.'); return } setSaving(true); setError('')
    try {
      const payload = { ...form, serviceOfferingIds:isAdministrator?serviceIds.filter(id=>divisions.some(d=>form.divisionIds.includes(d.id)&&d.services.some(s=>s.id===id))):undefined, latitude: form.latitude ? Number(form.latitude) : null, longitude: form.longitude ? Number(form.longitude) : null, branchManagerUserId: form.branchManagerUserId || null }
      if (editing) await api.put(`/branches/${editing.id}`, payload)
      else await api.post('/branches', payload)
      setOpen(false); await loadBranches();onSaved?.();window.dispatchEvent(new Event('crems:configuration-changed'))
    } catch (requestError: unknown) {
      const data = axios.isAxiosError(requestError) ? requestError.response?.data : undefined
      const errors = data?.errors as Record<string, string[]> | undefined
      setError(errors ? Object.values(errors).flat().join(' ') : data?.message ?? 'Unable to save the branch.')
    } finally { setSaving(false) }
  }

  async function toggleStatus(branch: Branch) {
    setError('')
    try {
      await api.patch(`/branches/${branch.id}/status`, { isActive: !branch.isActive })
      await loadBranches()
    } catch { setError('Unable to update the branch status.') }
  }

  return <Box sx={{ p: { xs: 2, sm: 3, lg: 4 }, maxWidth: 1400, mx: 'auto' }}>
    <PageHeader icon={<StoreOutlined />} title="Branches" subtitle="Assign each location to the businesses operating there and manage local contact details." actions={isAdministrator && <Button variant="contained" startIcon={<AddOutlined />} onClick={openCreate}>Add branch</Button>} />
    {error && !open && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
    <Card variant="outlined"><CardContent sx={{ p: 0 }}>
      {loading ? <Box sx={{ minHeight: 240, display: 'grid', placeItems: 'center' }}><CircularProgress /></Box> :
        <TableContainer><Table><TableHead><TableRow sx={{ bgcolor: '#f6f6f3' }}>
          <TableCell>Code</TableCell><TableCell>Branch</TableCell><TableCell>Divisions</TableCell><TableCell>Address</TableCell><TableCell>Phone</TableCell><TableCell>Status</TableCell><TableCell align="right">Actions</TableCell>
        </TableRow></TableHead><TableBody>
          {branches.length === 0 && <TableRow><TableCell colSpan={7} align="center" sx={{ py: 8, color: 'text.secondary' }}>No branches have been added yet.</TableCell></TableRow>}
          {branches.map((branch) => <TableRow key={branch.id} hover>
            <TableCell sx={{ fontWeight: 700 }}>{branch.code}</TableCell><TableCell>{branch.name}</TableCell><TableCell><Stack direction="row" gap={.5} flexWrap="wrap">{branch.divisionIds.map(id => <Chip key={id} size="small" label={divisions.find(x => x.id === id)?.name ?? 'Division'} />)}</Stack></TableCell><TableCell>{branch.address || '—'}</TableCell><TableCell>{branch.phone || '—'}</TableCell>
            <TableCell><Chip label={branch.isActive ? 'Active' : 'Inactive'} size="small" color={branch.isActive ? 'success' : 'default'} variant="outlined" /></TableCell>
            <TableCell align="right">{onConfigure&&<Button size="small" onClick={()=>onConfigure(branch.id)}>Services & hours</Button>}<Tooltip title="Edit branch details"><IconButton onClick={() => openEdit(branch)}><EditOutlined /></IconButton></Tooltip>{isAdministrator && <Tooltip title={branch.isActive ? 'Deactivate' : 'Activate'}><Switch checked={branch.isActive} onChange={() => void toggleStatus(branch)} /></Tooltip>}</TableCell>
          </TableRow>)}
        </TableBody></Table></TableContainer>}
    </CardContent></Card>
    <Dialog open={open} onClose={() => !saving && setOpen(false)} fullWidth maxWidth="md">
      <Box component="form" onSubmit={save} sx={{display:'flex',flexDirection:'column',minHeight:0,overflow:'hidden'}} ><DialogTitle>{editing ? 'Edit branch' : 'Add branch'}</DialogTitle><DialogContent>
        <Stack spacing={2.25} mt={1}>{error && <Alert severity="error">{error}</Alert>}
          <TextField label="Branch code" required disabled={!isAdministrator} inputProps={{ maxLength: 20 }} helperText={isAdministrator ? 'A short unique code, for example SUV or LTK.' : 'Only an administrator can change the branch code.'} value={form.code} onChange={(e) => setForm({ ...form, code: e.target.value })} />
          <TextField label="Branch name" required inputProps={{ maxLength: 150 }} value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} />
          <TextField label="Address" multiline minRows={2} value={form.address} onChange={(e) => setForm({ ...form, address: e.target.value })} />
          <TextField label="Phone" value={form.phone} onChange={(e) => setForm({ ...form, phone: e.target.value })} />
          <TextField type="email" label="Public contact email" value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })} />
          <Accordion disableGutters variant="outlined"><AccordionSummary expandIcon={<ExpandMoreOutlined/>}><Typography fontWeight={700}>Location details & customer instructions</Typography></AccordionSummary><AccordionDetails><Stack gap={2}>          <TextField label="Postal address" multiline minRows={2} value={form.postalAddress} onChange={(e) => setForm({ ...form, postalAddress: e.target.value })} />
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}><TextField fullWidth type="number" label="Latitude" value={form.latitude} onChange={e => setForm({ ...form, latitude: e.target.value })} /><TextField fullWidth type="number" label="Longitude" value={form.longitude} onChange={e => setForm({ ...form, longitude: e.target.value })} /></Stack>
          <TextField label="Pickup instructions" multiline minRows={2} value={form.pickupInstructions} onChange={e => setForm({ ...form, pickupInstructions: e.target.value })} />
          <TextField label="Return instructions" multiline minRows={2} value={form.returnInstructions} onChange={e => setForm({ ...form, returnInstructions: e.target.value })} />
          <TextField label="Delivery coverage" multiline minRows={2} value={form.deliveryCoverage} onChange={e => setForm({ ...form, deliveryCoverage: e.target.value })} />
</Stack></AccordionDetails></Accordion>
          <FormControlLabel control={<Switch checked={form.isPublic} onChange={e => setForm({ ...form, isPublic: e.target.checked })} />} label="Show this branch to customers" />
          {isAdministrator && <Box><Typography variant="subtitle2" mb={1}>Divisions operating at this branch</Typography><Stack direction="row" flexWrap="wrap" gap={1}>{divisions.map(division => <Chip key={division.id} clickable color={form.divisionIds.includes(division.id) ? 'primary' : 'default'} variant={form.divisionIds.includes(division.id) ? 'filled' : 'outlined'} label={division.name} onClick={() => setForm({ ...form, divisionIds: form.divisionIds.includes(division.id) ? form.divisionIds.filter(id => id !== division.id) : [...form.divisionIds, division.id] })} />)}</Stack></Box>}
          {isAdministrator&&form.divisionIds.length>0&&<Box><Typography variant="subtitle2" mb={1}>Services available at this branch</Typography><Typography variant="body2" color="text.secondary" mb={1}>Asset categories and their fields follow the services selected here. Set operating hours from Services & hours after saving.</Typography>{!serviceReady?<CircularProgress size={20}/>:divisions.filter(d=>form.divisionIds.includes(d.id)).map(d=><Box key={d.id}><Typography variant="caption" fontWeight={750}>{d.name}</Typography><Stack direction="row" flexWrap="wrap">{d.services.filter(s=>s.isActive).map(s=><FormControlLabel key={s.id} label={<Box><Typography variant="body2">{s.name}</Typography><Typography variant="caption" color="text.secondary">{categories.filter(c=>c.divisionId===d.id&&(!c.serviceOfferingId||c.serviceOfferingId===s.id)).map(c=>c.name).join(', ')||'No categories configured yet'}</Typography></Box>} control={<Checkbox checked={serviceIds.includes(s.id)} onChange={e=>setServiceIds(v=>e.target.checked?[...v,s.id]:v.filter(id=>id!==s.id))}/>}/>)}</Stack></Box>)}</Box>}
        </Stack>
      </DialogContent><DialogActions sx={{ p: 3, pt: 1 }}><Button onClick={() => setOpen(false)} disabled={saving}>Cancel</Button><Button type="submit" variant="contained" disabled={saving||!serviceReady}>{saving ? 'Saving…' : 'Save branch'}</Button></DialogActions></Box>
    </Dialog>
  </Box>
}
