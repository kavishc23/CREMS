import { useCallback, useEffect, useState, type FormEvent } from 'react'
import axios from 'axios'
import { Alert, Autocomplete, Box, Button, Checkbox, Chip, CircularProgress, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, MenuItem, Stack, Tab, Tabs, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, TextField, Typography } from '@mui/material'
import { api } from '../api/client'

type Technician = { id: string; employeeNumber: string; fullName: string; branchId: string; divisionId: string; locations?: { branchId: string; divisionId: string }[]; standardCostRate: number; availability: string; isActive: boolean; version: string }
type Supplier = { id: string; supplierNumber: string; name: string; email: string | null; phone: string | null; address: string | null; taxNumber: string | null; paymentTermsDays: number; isActive: boolean; version: string }
type Location = { branchId: string; divisionId: string; branchName: string; divisionName: string }
type Directory = { technicians: Technician[]; suppliers: Supplier[]; locations: Location[] }
type Editor = { kind: 'technicians' | 'suppliers'; id?: string; version?: string; number: string; name: string; locations: string[]; rate: string; availability: string; isActive: boolean; email: string; phone: string; address: string; taxNumber: string; paymentTermsDays: string }
const fresh = (kind: Editor['kind']): Editor => ({ kind, number: '', name: '', locations: [], rate: '', availability: 'Available', isActive: true, email: '', phone: '', address: '', taxNumber: '', paymentTermsDays: '30' })
const locationKey = (value: { branchId: string; divisionId: string }) => `${value.branchId}:${value.divisionId}`
function failure(error: unknown) {
  if (axios.isAxiosError(error)) return error.response?.data?.message ?? (error.response?.data?.errors ? Object.values(error.response.data.errors).flat().join(' ') : 'Unable to save or load the directory. Please retry.')
  return 'Unable to save or load the directory. Please retry.'
}
export function MaintenanceDirectoryPage() {
  const [data, setData] = useState<Directory>({ technicians: [], suppliers: [], locations: [] })
  const [tab, setTab] = useState<Editor['kind']>('technicians')
  const [editor, setEditor] = useState<Editor | null>(null)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [editError, setEditError] = useState('')
  const [notice, setNotice] = useState('')
  const load = useCallback(async () => {
    try { setData((await api.get<Directory>('/administration/maintenance-directory')).data) }
    catch (error) { setError(failure(error)) } finally { setLoading(false) }
  }, [])
  useEffect(() => {
    const controller = new AbortController()
    api.get<Directory>('/administration/maintenance-directory', { signal: controller.signal })
      .then(response => { if (!controller.signal.aborted) setData(response.data) })
      .catch(error => { if (!controller.signal.aborted) setError(failure(error)) })
      .finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [])
  const open = (value: Editor) => { setEditError(''); setEditor(value) }
  const change = (key: keyof Editor, value: string | boolean | string[]) => setEditor(current => current ? { ...current, [key]: value } : null)
  async function save(event: FormEvent) {
    event.preventDefault(); if (!editor) return
    if (editor.kind === 'technicians' && editor.locations.length === 0) { setEditError('Select at least one branch/division assignment.'); return }
    setSaving(true); setEditError('')
    const locations = editor.locations.map(value => { const [branchId, divisionId] = value.split(':'); return { branchId, divisionId } })
    const { branchId, divisionId } = locations[0] ?? {}
    const payload = editor.kind === 'technicians'
      ? { fullName: editor.name.trim(), branchId, divisionId, locations, standardCostRate: Number(editor.rate), availability: editor.availability, isActive: editor.isActive, expectedVersion: editor.version }
      : { supplierNumber: editor.number.trim(), name: editor.name.trim(), email: editor.email.trim() || null, phone: editor.phone.trim() || null, address: editor.address.trim() || null, taxNumber: editor.taxNumber.trim() || null, paymentTermsDays: Number(editor.paymentTermsDays), isActive: editor.isActive, expectedVersion: editor.version }
    try {
      const endpoint = `/administration/maintenance-directory/${editor.kind}`
      if (editor.id) await api.put(`${endpoint}/${editor.id}`, payload); else await api.post(endpoint, payload)
      setEditor(null); setNotice(editor.kind === 'technicians' ? 'Technician saved. New assignments use this hourly rate; existing job rates stay unchanged.' : 'Supplier saved. Active suppliers are available in maintenance dropdowns.')
      setLoading(true); setError(''); await load()
    } catch (error) { setEditError(failure(error)) } finally { setSaving(false) }
  }
  const text = (key: keyof Editor, label: string, required = false, type = 'text') => <TextField fullWidth label={label} value={editor?.[key] ?? ''} required={required} type={type} onChange={event => change(key, event.target.value)} />
  return <Box sx={{ p: { xs: 2, sm: 3, lg: 4 } }}>
    <Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" gap={2} mb={2}>
      <Box><Typography variant="h5">Technicians & suppliers</Typography><Typography color="text.secondary">Manage maintenance dropdowns and each technician's hourly labour rate.</Typography></Box>
      <Stack direction="row" gap={1}><Button disabled={loading} onClick={() => { setLoading(true); setError(''); void load() }}>Refresh directory</Button><Button variant="contained" disabled={loading} onClick={() => open(fresh(tab))}>{tab === 'technicians' ? 'Add technician' : 'Add supplier'}</Button></Stack>
    </Stack>
    {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
    {notice && <Alert severity="success" onClose={() => setNotice('')} sx={{ mb: 2 }}>{notice}</Alert>}
    <Tabs value={tab} onChange={(_, value: Editor['kind']) => setTab(value)} aria-label="Maintenance directory"><Tab value="technicians" label="Technicians" /><Tab value="suppliers" label="Suppliers" /></Tabs>
    <Typography variant="body2" color="text.secondary" my={2}>Deactivate entries to hide them from new assignments while keeping past records.</Typography>
    {loading ? <CircularProgress aria-label="Loading directory" /> : <TableContainer><Table size="small" aria-label={tab === 'technicians' ? 'Technician directory' : 'Supplier directory'}><TableHead><TableRow><TableCell>Name / reference</TableCell><TableCell>{tab === 'technicians' ? 'Branch / division' : 'Contact'}</TableCell>{tab === 'technicians' && <TableCell>Labour rate (FJD/hour)</TableCell>}<TableCell>Status</TableCell><TableCell>Action</TableCell></TableRow></TableHead><TableBody>
      {tab === 'technicians' ? data.technicians.map(item => {
        const assignedLocations = item.locations?.length ? item.locations : [item]
        const locationNames = assignedLocations.map(value => { const location = data.locations.find(option => locationKey(option) === locationKey(value)); return location ? `${location.branchName} / ${location.divisionName}` : 'Inactive location' })
        return <TableRow key={item.id}><TableCell><Typography>{item.fullName}</Typography><Typography variant="caption">{item.employeeNumber}</Typography></TableCell><TableCell>{locationNames.map((name, index) => <Typography variant="body2" key={index}>{name}</Typography>)}</TableCell><TableCell>{item.standardCostRate.toFixed(2)}</TableCell><TableCell><Chip size="small" label={item.isActive ? item.availability : 'Inactive'} /></TableCell><TableCell><Button aria-label={`Edit technician ${item.fullName}`} onClick={() => open({ ...fresh('technicians'), id: item.id, version: item.version, number: item.employeeNumber, name: item.fullName, locations: assignedLocations.map(locationKey), rate: String(item.standardCostRate), availability: item.availability, isActive: item.isActive })}>Edit</Button></TableCell></TableRow>
      }) : data.suppliers.map(item => <TableRow key={item.id}><TableCell><Typography>{item.name}</Typography><Typography variant="caption">{item.supplierNumber}</Typography></TableCell><TableCell>{item.email || item.phone || 'Not recorded'}</TableCell><TableCell><Chip size="small" label={item.isActive ? 'Active' : 'Inactive'} /></TableCell><TableCell><Button aria-label={`Edit supplier ${item.name}`} onClick={() => open({ ...fresh('suppliers'), id: item.id, version: item.version, number: item.supplierNumber, name: item.name, email: item.email ?? '', phone: item.phone ?? '', address: item.address ?? '', taxNumber: item.taxNumber ?? '', paymentTermsDays: String(item.paymentTermsDays), isActive: item.isActive })}>Edit</Button></TableCell></TableRow>)}
      {!data[tab].length && <TableRow><TableCell colSpan={tab === 'technicians' ? 5 : 4}>No {tab} yet. Add the first entry above.</TableCell></TableRow>}
    </TableBody></Table></TableContainer>}
    <Dialog open={Boolean(editor)} onClose={() => !saving && setEditor(null)} fullWidth maxWidth="sm">
      {editor && <Box component="form" onSubmit={save}><DialogTitle>{editor.id ? 'Edit' : 'Add'} {editor.kind === 'technicians' ? 'technician' : 'supplier'}</DialogTitle><DialogContent><Stack spacing={2} pt={1}>
        {editError && <Alert severity="error">{editError}</Alert>}
        <Box component="fieldset" disabled={saving} sx={{ border: 0, p: 0, m: 0, minWidth: 0 }}><Stack spacing={2}>
          {text('name', editor.kind === 'technicians' ? 'Technician name' : 'Supplier name', true)}
          {editor.kind === 'technicians' ? <TextField label="Employee number" value={editor.id ? editor.number : 'Generated automatically on save'} slotProps={{ input: { readOnly: true } }} helperText="Assigned automatically and kept unchanged when editing." /> : text('number', 'Supplier number', true)}
          {editor.kind === 'technicians' ? <>
            <Autocomplete multiple disableCloseOnSelect options={data.locations} isOptionEqualToValue={(a, b) => locationKey(a) === locationKey(b)}
              value={editor.locations.map(key => data.locations.find(item => locationKey(item) === key) ?? { branchId: key.split(':')[0], divisionId: key.split(':')[1], branchName: 'Current location', divisionName: 'inactive' })}
              getOptionLabel={item => `${item.branchName} / ${item.divisionName}`}
              onChange={(_, values) => change('locations', values.map(locationKey))}
              renderOption={(props, item, { selected }) => <li {...props} key={locationKey(item)}><Checkbox checked={selected} tabIndex={-1} sx={{ pointerEvents: 'none' }} />{item.branchName} / {item.divisionName}</li>}
              renderInput={params => <TextField {...params} required={!editor.locations.length} label="Branch / division assignments" helperText="Select every branch/division this technician can work in. The same hourly rate applies to all." />} />
            <TextField required label="Hourly labour rate (FJD)" type="number" value={editor.rate} onChange={event => change('rate', event.target.value)} slotProps={{ htmlInput: { min: 0, max: 1000000, step: '0.01' } }} helperText="Internal labour cost per hour. Applies when this technician is assigned to a job." />
            <TextField select label="Availability" value={editor.availability} onChange={event => change('availability', event.target.value)}>{['Available', 'Assigned', 'Leave', 'Training', 'Unavailable'].map(value => <MenuItem key={value} value={value}>{value}</MenuItem>)}</TextField>
          </> : <>{text('email', 'Email', false, 'email')}{text('phone', 'Phone')}{text('address', 'Address')}{text('taxNumber', 'Tax number')}<TextField label="Payment terms (days)" type="number" required value={editor.paymentTermsDays} onChange={event => change('paymentTermsDays', event.target.value)} slotProps={{ htmlInput: { min: 0, max: 365, step: 1 } }} /></>}
          <FormControlLabel control={<Checkbox checked={editor.isActive} onChange={event => change('isActive', event.target.checked)} />} label="Active" />
        </Stack></Box>
      </Stack></DialogContent><DialogActions><Button disabled={saving} onClick={() => setEditor(null)}>Cancel</Button><Button type="submit" variant="contained" disabled={saving}>{saving ? 'Saving...' : 'Save'}</Button></DialogActions></Box>}
    </Dialog>
  </Box>
}
