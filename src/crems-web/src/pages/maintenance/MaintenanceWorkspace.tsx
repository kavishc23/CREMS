import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import axios from 'axios'
import CloseOutlined from '@mui/icons-material/CloseOutlined'
import { Accordion, AccordionDetails, AccordionSummary, Alert, Autocomplete, Box, Button, Checkbox, Chip, CircularProgress, Dialog, DialogActions, DialogContent, DialogTitle, Divider, Drawer, FormControlLabel, Grid, IconButton, MenuItem, Stack, Tab, Tabs, TextField, Typography } from '@mui/material'
import ExpandMoreOutlined from '@mui/icons-material/ExpandMoreOutlined'
import { api } from '../../api/client'
import { MaintenanceTaxFields } from '../MaintenanceTaxFields'
import { calculateMaintenanceTax, parseTaxCategories, type TaxSettings } from '../maintenanceTax'
import { MaintenanceServicePlan } from './MaintenanceServicePlan'
import { MaintenanceInspectionRecord } from './MaintenanceInspectionRecord'
import { MaintenancePartsDialog } from '../MaintenancePartsDialog'
import { closed, date, label, money, repairOverdue, type Asset, type Detail, type Job, type Options, type Permissions } from './types'

type Form = { assetId: string; serviceType: string; faultDescription: string; priority: string; isPreventive: boolean; assignedTo: string; assignedPersonnelId: string; supplier: string; supplierId: string; sourceType: string; sourceReference: string; sourceInspectionId: string; expectedReleaseAt: string; completionNotes: string; nextServiceDate: string; nextServiceMeter: string; serviceIntervalMonths: string; meterReading: string; downtimeHours: string; parentFailureJobId: string; warrantyCovered: boolean; warrantyClaimNumber: string; estimatedCost: string; actualCost: string; partsCost: string; labourCost: string; labourHours: string; labourRate: string; transportCost: string; externalServiceCost: string; fuelCost: string; taxCost: string; otherCost: string; invoiceNumber: string; partsUsed: string; useDetailedCosts: boolean }
const empty: Form = { assetId: '', serviceType: 'Corrective repair', faultDescription: '', priority: 'Normal', isPreventive: false, assignedTo: '', assignedPersonnelId: '', supplier: '', supplierId: '', sourceType: 'Manual', sourceReference: '', sourceInspectionId: '', expectedReleaseAt: '', completionNotes: '', nextServiceDate: '', nextServiceMeter: '', serviceIntervalMonths: '', meterReading: '', downtimeHours: '', parentFailureJobId: '', warrantyCovered: false, warrantyClaimNumber: '', estimatedCost: '', actualCost: '', partsCost: '', labourCost: '', labourHours: '', labourRate: '', transportCost: '', externalServiceCost: '', fuelCost: '', taxCost: '', otherCost: '', invoiceNumber: '', partsUsed: '', useDetailedCosts: true }
const numericFields = ['estimatedCost', 'partsCost', 'labourCost', 'transportCost', 'externalServiceCost', 'fuelCost', 'taxCost', 'otherCost', 'downtimeHours'] as const
function localDateTime(value: string | null) { if (!value) return ''; const d = new Date(value); return new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 16) }
function jobForm(job: Job): Form {
  const form = { ...empty }
  for (const key of Object.keys(empty) as (keyof Form)[]) {
    if (key === 'useDetailedCosts') continue
    const value = job[key as keyof Job]
    if (typeof empty[key] === 'boolean') Object.assign(form, { [key]: Boolean(value) })
    else Object.assign(form, { [key]: value == null ? '' : String(value) })
  }
  if (job.hasEstimate === false) form.estimatedCost = ''
  form.expectedReleaseAt = localDateTime(job.expectedReleaseAt)
  form.useDetailedCosts = job.useDetailedCosts ?? (job.actualCost == null || ['partsCost', 'labourCost', 'transportCost', 'externalServiceCost', 'fuelCost', 'taxCost', 'otherCost'].some(key => Number(job[key as keyof Job]) > 0))
  return form
}
function failure(reason: unknown) {
  if (!axios.isAxiosError(reason)) return 'The request could not be completed. Your entered details have been kept.'
  const response = reason.response?.data
  return response?.errors ? Object.values(response.errors).flat().join(' ') : response?.message ?? (reason.response?.status === 403 ? 'You do not have permission for this action.' : 'The request could not be completed. Retry when the connection is available.')
}
type Document = { id: string; fileName: string; type: string }
type Inspection = { id: string; stage: string; outcome: string; completedAt: string; notes: string | null }
export function MaintenanceWorkspace({ jobId, initialAsset, options, permissions, onClose, onSaved, onOpenJob }: { jobId: string | null; initialAsset: Asset | null; options: Options; permissions: Permissions; onClose: () => void; onOpenJob: (id: string) => void; onSaved: (message: string, id?: string) => void }) {
  const [taxSettings, setTaxSettings] = useState<TaxSettings>({ mode: 'Exclusive', rate: '0', categories: 9, reason: '' })
  const [inspectionId, setInspectionId] = useState<string | null>(null)
  const [auditRecord, setAuditRecord] = useState<Detail['audits'][number] | null>(null)
  const [warrantyDetails, setWarrantyDetails] = useState(false)
  const [detail, setDetail] = useState<Detail | null>(null)
  const [form, setForm] = useState<Form>({ ...empty, ...(initialAsset ? { assetId: initialAsset.id, isPreventive: true, sourceType: 'Preventive', serviceType: 'Preventive maintenance' } : {}) })
  const [asset, setAsset] = useState<Asset | null>(initialAsset)
  const [assets, setAssets] = useState<Asset[]>(initialAsset ? [initialAsset] : [])
  const [assetSearch, setAssetSearch] = useState('')
  const [inspections, setInspections] = useState<Inspection[]>([])
  const [previousJobs, setPreviousJobs] = useState<Job[]>([])
  const [busy, setBusy] = useState(false)
  const [loading, setLoading] = useState(Boolean(jobId))
  const [error, setError] = useState('')
  const [tab, setTab] = useState('Details')
  const [documents, setDocuments] = useState<Document[]>([])
  const [documentType, setDocumentType] = useState('FaultPhoto')
  const [parts, setParts] = useState(false)
  const [transition, setTransition] = useState<string | null>(null)
  const [reason, setReason] = useState('')
  const [discardOpen, setDiscardOpen] = useState(false)
  const [dirty, setDirty] = useState(false)
  const [checks, setChecks] = useState<string[]>([])
  const [safetyNotes, setSafetyNotes] = useState('')
  const [safetyMeter, setSafetyMeter] = useState('')
  const pending = useRef<(() => void) | null>(null)
  const formElement = useRef<HTMLFormElement>(null)
  const job = detail?.job
  const access = detail?.permissions ?? permissions
  const readOnly = Boolean(job && closed(job) && !access.canComplete)
  const guard = (next: () => void) => { if (dirty) { pending.current = next; setDiscardOpen(true) } else next() }
  const change = <K extends keyof Form>(key: K, value: Form[K]) => { setForm(current => ({ ...current, [key]: value })); setDirty(true) }
  const loadDetail = useCallback(async (id: string) => {
    setLoading(true); setError('')
    try {
      const [record, attachments] = await Promise.all([api.get<Detail>(`/maintenance-jobs/${id}/workspace`), api.get<Document[]>(`/maintenance-jobs/${id}/documents`)])
      setDetail(record.data); setForm(jobForm(record.data.job));
      setTaxSettings({ mode: record.data.job.taxMode ?? 'Manual', rate: String(record.data.job.taxRate ?? 0), categories: parseTaxCategories(record.data.job.taxableCosts), reason: record.data.job.taxOverrideReason ?? '' }); setDocuments(attachments.data); setDirty(false); setChecks([]); setSafetyNotes(''); setSafetyMeter(String(record.data.job.currentMeterReading ?? record.data.job.meterReading ?? ''))
      const result = await api.get<Asset[]>('/maintenance-jobs/assets', { params: { id: record.data.job.assetId } }); setAsset(result.data[0] ?? null)
    } catch (requestError) { setError(failure(requestError)) } finally { setLoading(false) }
  }, [])
  useEffect(() => {
    // The record editor synchronizes with the selected server record.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    if (jobId) void loadDetail(jobId)
  }, [jobId, loadDetail])
  useEffect(() => {
    if (jobId) return
    const controller = new AbortController()
    const timer = setTimeout(() => { api.get<Asset[]>('/maintenance-jobs/assets', { params: { search: assetSearch }, signal: controller.signal }).then(result => setAssets(result.data)).catch(reason => { if (!axios.isCancel(reason)) setError('Asset choices could not be loaded. Retry by searching again.') }) }, 250)
    return () => { clearTimeout(timer); controller.abort() }
  }, [assetSearch, jobId])
  useEffect(() => {
    if (!asset?.id) return
    const controller = new AbortController()
    Promise.all([api.get<Inspection[]>('/maintenance-jobs/inspections', { params: { assetId: asset.id }, signal: controller.signal }), api.get<{ items: Job[] }>('/maintenance-jobs/workspace', { params: { queue: 'History', assetId: asset.id, pageSize: 25 }, signal: controller.signal })]).then(([records, history]) => { setInspections(records.data); setPreviousJobs(history.data.items.filter(x => x.id !== jobId)) }).catch(reason => { if (!axios.isCancel(reason)) setError('Asset history choices could not be loaded.') })
    return () => controller.abort()
  }, [asset?.id, jobId])
  useEffect(() => {
    const beforeUnload = (event: BeforeUnloadEvent) => { if (dirty) event.preventDefault() }
    window.addEventListener('beforeunload', beforeUnload)
    return () => window.removeEventListener('beforeunload', beforeUnload)
  }, [dirty])
  const labour = form.labourHours !== '' && form.labourRate !== '' ? Math.round(Number(form.labourHours) * Number(form.labourRate) * 100) / 100 : Number(form.labourCost || 0)
  const calculated = calculateMaintenanceTax({ partsCost: Number(form.partsCost || 0), labourCost: labour, transportCost: Number(form.transportCost || 0), externalServiceCost: Number(form.externalServiceCost || 0), fuelCost: Number(form.fuelCost || 0), otherCost: Number(form.otherCost || 0) }, taxSettings, Number(form.taxCost || 0), job?.issuedStockCost ?? 0)
  const total = calculated.total
  const field = (key: keyof Form, title: string, extra: Record<string, unknown> = {}) => <TextField fullWidth label={title} value={form[key]} disabled={busy || readOnly} onChange={e => change(key, e.target.value as never)} {...extra} />
  const number = (key: keyof Form, title: string, extra: Record<string, unknown> = {}) => field(key, title, { type: 'number', slotProps: { htmlInput: { min: 0, max: 100000000, step: 'any' } }, ...extra })
  function payload(status: string, transitionReason = '') {
    const result: Record<string, unknown> = { ...form, hasEstimate: form.estimatedCost !== '', calculateDowntime: form.downtimeHours === '', status, expectedVersion: job?.version, transitionReason, nextServiceDate: form.nextServiceDate || null, expectedReleaseAt: form.expectedReleaseAt ? new Date(form.expectedReleaseAt).toISOString() : null }
    for (const key of numericFields) result[key] = Number(form[key] || 0)
    for (const key of ['actualCost', 'meterReading', 'nextServiceMeter', 'labourHours', 'labourRate', 'serviceIntervalMonths'] as const) result[key] = form[key] === '' ? null : Number(form[key])
    for (const key of ['supplierId', 'assignedPersonnelId', 'sourceInspectionId', 'parentFailureJobId'] as const) result[key] = form[key] || null
    result.labourCost = labour
    Object.assign(result, { taxMode: taxSettings.mode, taxRate: Number(taxSettings.rate || 0), taxableCosts: taxSettings.categories, taxOverrideReason: taxSettings.reason, taxCost: calculated.tax })
    if (!form.useDetailedCosts) for (const key of ['partsCost', 'labourCost', 'transportCost', 'externalServiceCost', 'fuelCost', 'taxCost', 'otherCost']) result[key] = 0
    return result
  }
  async function save(event?: FormEvent, nextStatus = job?.status ?? 'Open') {
    event?.preventDefault()
    if (!form.assetId || !form.serviceType.trim() || !form.faultDescription.trim()) { setError('Select an asset and enter the repair type and work required.'); setTab('Details'); return }
    if (nextStatus === 'Completed' && (!form.completionNotes.trim() || (asset?.meterUnit && form.meterReading === ''))) { setError('Record the work performed and final meter reading before completing the work.'); setTab('Work'); setTransition(null); return }
    if (['Cancelled', 'WaitingForParts'].includes(nextStatus) && nextStatus !== job?.status && !reason.trim()) return
    setBusy(true); setError('')
    try {
      if (jobId) { await api.put(`/maintenance-jobs/${jobId}`, payload(nextStatus, reason)); setTransition(null); await loadDetail(jobId); onSaved(`${job?.jobNumber} saved. ${nextStatus === 'Completed' ? 'Work completed; verify safety before returning the asset to service.' : 'Open the workspace for the next action.'}`) }
      else { const result = await api.post<{ id: string; jobNumber: string }>('/maintenance-jobs', payload('Open')); setDirty(false); onSaved(`${result.data.jobNumber} logged. The asset is unavailable while maintenance is open.`, result.data.id) }
    } catch (requestError) { setError(failure(requestError)); setTransition(null) } finally { setBusy(false) }
  }
  async function release() {
    if (!jobId || !job) return
    setBusy(true); setError('')
    try { const result = await api.post<{ message: string }>(`/maintenance-jobs/${jobId}/release`, { expectedVersion: job.version, notes: safetyNotes, meterReading: safetyMeter === '' ? null : Number(safetyMeter), passedChecks: checks }); setDirty(false); await loadDetail(jobId); onSaved(result.data.message) }
    catch (requestError) { setError(failure(requestError)) } finally { setBusy(false) }
  }
  async function upload(file?: File) {
    if (!file || !jobId) return
    if (file.size > 5 * 1024 * 1024) { setError('Choose a PDF, JPEG or PNG up to 5 MB.'); return }
    setBusy(true); setError('')
    try { const body = new FormData(); body.append('file', file); body.append('type', documentType); await api.post(`/maintenance-jobs/${jobId}/documents`, body); const result = await api.get<Document[]>(`/maintenance-jobs/${jobId}/documents`); setDocuments(result.data); onSaved(`${job?.jobNumber}: ${file.name} attached.`) }
    catch (requestError) { setError(failure(requestError)) } finally { setBusy(false) }
  }
  async function download(item: Document) {
    try { const response = await api.get(`/maintenance-jobs/${jobId}/documents/${item.id}`, { responseType: 'blob' }); const url = URL.createObjectURL(response.data); const link = document.createElement('a'); link.href = url; link.download = item.fileName; link.click(); setTimeout(() => URL.revokeObjectURL(url), 1000) }
    catch (requestError) { setError(failure(requestError)) }
  }
  const wantsReason = transition === 'Cancelled' || transition === 'WaitingForParts' || Boolean(job && closed(job) && transition === 'InProgress')
  return <>
    <Drawer anchor="right" open onClose={() => !busy && guard(onClose)} slotProps={{ paper: { sx: { width: { xs: '100%', lg: 1000 }, maxWidth: '100%', bgcolor: 'background.default' } } }}>
      <Box role="dialog" aria-label={job ? `Maintenance ${job.jobNumber}` : 'Log maintenance job'} sx={{ height: '100%', display: 'flex', flexDirection: 'column', minWidth: 0 }}>
        <Box p={{ xs: 2, sm: 3 }} bgcolor="background.paper">
          <Stack direction="row" alignItems="flex-start" justifyContent="space-between" gap={2}><Box><Typography variant="overline" color="text.secondary">{job ? job.assetNumber : 'New maintenance job'}</Typography><Typography variant="h5" sx={{ overflowWrap: 'anywhere' }}>{job?.jobNumber ?? 'Log maintenance job'}</Typography><Typography color="text.secondary">{job ? `${job.assetName} · ${job.branchName}` : 'Choose an asset and describe the work required.'}</Typography></Box><IconButton aria-label="Close maintenance workspace" disabled={busy} onClick={() => guard(onClose)}><CloseOutlined /></IconButton></Stack>
          {job && <Stack direction="row" gap={1} mt={2} useFlexGap flexWrap="wrap"><Chip label={job.releasedAt ? 'Returned to service' : job.status === 'Completed' ? 'Work completed · safety check pending' : label(job.status)} color={job.releasedAt ? 'success' : 'warning'} /><Chip label={job.priority} variant="outlined" />{repairOverdue(job) && <Chip label="Repair deadline missed" color="error" />}<Chip label={`Asset: ${label(job.assetStatus)}`} variant="outlined" /></Stack>}
          {job && !closed(job) && <Stack direction="row" gap={1} mt={2} useFlexGap flexWrap="wrap">
            {job.status !== 'InProgress' && <Button variant="outlined" disabled={busy} onClick={() => { setTransition('InProgress'); setReason('') }}>Start work</Button>}
            {job.status !== 'WaitingForParts' && <Button disabled={busy} onClick={() => { setTransition('WaitingForParts'); setReason('') }}>Wait for parts</Button>}
            {access.canComplete && <Button variant="contained" disabled={busy} onClick={() => { setTransition('Completed'); setReason('') }}>Complete work</Button>}
            <Button color="error" disabled={busy} onClick={() => { setTransition('Cancelled'); setReason('') }}>Cancel job</Button>
          </Stack>}
          {job && closed(job) && access.canComplete && <Button sx={{ mt: 1 }} disabled={busy} onClick={() => { setTransition('InProgress'); setReason('') }}>Reopen work</Button>}
        </Box>
        <Tabs value={tab} onChange={(_, value: string) => setTab(value)} variant="scrollable" scrollButtons="auto" sx={{ px: 1, borderBottom: 1, borderColor: 'divider' }}><Tab value="Details" label="Job details" /><Tab value="Work" label={job ? "Work and service targets" : "Service targets"} />{access.canFinancial && <Tab value="Costs" label="Expenses" />}<Tab value="Evidence" label="Evidence" />{job && <Tab value="Safety" label="Safety check" />}{job && <Tab value="History" label="Asset history" />}</Tabs>
        <Box component="form" ref={formElement} onSubmit={save} sx={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column' }}>
          <Box sx={{ flex: 1, overflowY: 'auto', px: { xs: 2, sm: 3 }, py: 3 }}>
            {error && <Alert severity="error" sx={{ mb: 2 }} role="alert">{error}{jobId && !dirty && <Button onClick={() => void loadDetail(jobId)}>Refresh record</Button>}</Alert>}
            {loading ? <Box py={5} textAlign="center"><CircularProgress /></Box> : <>
              {tab === 'Details' && <Stack spacing={2.5}>
                {!job && <Alert severity="info">Logging work makes the selected asset unavailable for hire immediately.</Alert>}
                {!job ? <Autocomplete options={assets} value={asset} isOptionEqualToValue={(a, b) => a.id === b.id} getOptionLabel={value => `${value.assetNumber} · ${value.name} (${value.branchName})`} filterOptions={values => values} onInputChange={(_, value, why) => { if (why === 'input') setAssetSearch(value) }} onChange={(_, value) => { setAsset(value); change('assetId', value?.id ?? ''); change('assignedPersonnelId', ''); change('assignedTo', '') }} renderOption={(props, value) => <li {...props} key={value.id}><Box><Typography>{value.assetNumber} · {value.name}</Typography><Typography variant="caption">{value.branchName} · {label(value.status)} · {value.currentMeterReading ?? '—'} {value.meterUnit}</Typography></Box></li>} renderInput={params => <TextField {...params} label="Search or scan asset number" required helperText="Search by asset number, name, registration, or paste the number from its QR label." />} /> : <Alert severity="info">{job.assetNumber} · {job.assetName} · Current meter: {job.currentMeterReading ?? '—'} {job.meterUnit}</Alert>}
                {field('serviceType', 'Service / repair type', { required: true })}
                {field('faultDescription', 'Fault description / work required', { required: true, multiline: true, minRows: 3 })}
                <Grid container spacing={2}><Grid size={{ xs: 12, sm: 6 }}>{field('priority', 'Priority', { select: true, children: ['Low', 'Normal', 'High', 'Critical'].map(x => <MenuItem key={x} value={x}>{x}</MenuItem>) })}</Grid><Grid size={{ xs: 12, sm: 6 }}>{field('expectedReleaseAt', 'Expected release (your local time)', { type: 'datetime-local', slotProps: { inputLabel: { shrink: true } }, helperText: 'An estimate for rental planning; safety checks still control availability.' })}</Grid></Grid>
                {!job && <><TextField select label="Job source" value={form.sourceType} onChange={e => change('sourceType', e.target.value)}>{['Manual', 'Inspection', 'Damage', 'Preventive'].map(x => <MenuItem key={x} value={x}>{x}</MenuItem>)}</TextField>
                  {['Inspection', 'Damage'].includes(form.sourceType) && <Autocomplete options={inspections} value={inspections.find(x => x.id === form.sourceInspectionId) ?? null} getOptionLabel={x => `${date(x.completedAt)} · ${label(x.stage)} · ${label(x.outcome)}`} onChange={(_, value) => { change('sourceInspectionId', value?.id ?? ''); if (value) change('sourceReference', `${label(value.stage)} inspection ${value.id}`) }} renderInput={params => <TextField {...params} label="Originating inspection" />} />}
                  {field('sourceReference', 'Source / damage reference', { required: ['Inspection', 'Damage'].includes(form.sourceType) && !form.sourceInspectionId, helperText: 'For an external report, enter its reference so staff can trace the fault.' })}</>}
                {job && <Typography variant="body2" color="text.secondary">Reported {date(job.reportedAt, true)} by {job.reportedByName || 'Not recorded'} · Source: {job.sourceType}{job.sourceReference ? ` · ${job.sourceReference}` : ''}</Typography>}
                {job?.sourceInspectionId && <Button onClick={() => setInspectionId(job.sourceInspectionId)}>View originating inspection</Button>}
                {job?.parentFailureJobId && <Button onClick={() => guard(() => onOpenJob(job.parentFailureJobId!))}>Open related failure</Button>}
                <Divider />
                <Typography variant="subtitle1" fontWeight={700}>Assignment</Typography>
                <Autocomplete options={options.technicians.filter(x => !asset || x.branchId === asset.branchId && x.divisionId === asset.divisionId)} value={options.technicians.find(x => x.id === form.assignedPersonnelId) ?? null} getOptionLabel={x => `${x.fullName} · ${x.employeeNumber} · ${label(x.availability)}`} disabled={busy || readOnly} getOptionDisabled={x => ['Leave', 'Training', 'Unavailable'].includes(x.availability)} onChange={(_, value) => { change('assignedPersonnelId', value?.id ?? ''); change('assignedTo', value?.fullName ?? '') }} renderInput={params => <TextField {...params} label="Assigned technician" helperText={form.assignedTo && !form.assignedPersonnelId ? `Existing assignment: ${form.assignedTo}. Select a master record to update it.` : 'Only technicians in the asset branch and division are offered.'} />} />
                <Autocomplete options={options.suppliers} value={options.suppliers.find(x => x.id === form.supplierId) ?? null} getOptionLabel={x => `${x.name} · ${x.supplierNumber}`} disabled={busy || readOnly} onChange={(_, value) => { change('supplierId', value?.id ?? ''); change('supplier', value?.name ?? '') }} renderInput={params => <TextField {...params} label="External supplier" helperText={form.supplier && !form.supplierId ? `Existing supplier: ${form.supplier}` : 'Optional when work is handled internally.'} />} />
                <Autocomplete options={previousJobs} value={previousJobs.find(x => x.id === form.parentFailureJobId) ?? null} disabled={busy || readOnly} getOptionLabel={x => `${x.jobNumber} · ${x.serviceType}`} onChange={(_, value) => change('parentFailureJobId', value?.id ?? '')} renderInput={params => <TextField {...params} label="Related previous failure" helperText="Link repeat faults on this asset to support replacement decisions." />} />
              </Stack>}
              {tab === 'Work' && <Stack spacing={2.5}>
                {job && <><Typography variant="subtitle1" fontWeight={700}>Work performed</Typography>
                {field('completionNotes', 'Completion notes / work performed', { multiline: true, minRows: 4, helperText: 'Required to complete work. Describe repairs, tests and remaining concerns.', slotProps: { htmlInput: { maxLength: 4000 } } })}
                <Grid container spacing={2}><Grid size={{ xs: 12, sm: 6 }}>{number('meterReading', `Final meter reading${asset?.meterUnit ? ` (${asset.meterUnit})` : ''}`, { helperText: `Current reading: ${asset?.currentMeterReading ?? job?.currentMeterReading ?? 'Not recorded'}` })}</Grid><Grid size={{ xs: 12, sm: 6 }}>{number('downtimeHours', 'Downtime hours', { slotProps: { htmlInput: { min: 0, max: 100000, step: 1 } }, helperText: 'Leave blank to calculate elapsed unavailability when work completes.' })}</Grid></Grid>
                {field('partsUsed', 'Parts and repair notes', { multiline: true, minRows: 2, helperText: 'Stock quantities and issued costs remain in Manage parts.' })}
                <Button variant="outlined" disabled={!jobId || busy || dirty} onClick={() => setParts(true)}>Manage parts and stock movements</Button>
                {dirty && jobId && <Typography variant="caption" color="text.secondary">Save details before opening inventory so your expense changes stay in sync.</Typography>}
                <Divider /></>}<Typography variant="subtitle1" fontWeight={700}>Next service targets</Typography>
                <FormControlLabel control={<Checkbox checked={form.isPreventive} disabled={busy || readOnly} onChange={e => change('isPreventive', e.target.checked)} />} label="Preventive service" />
                <Grid container spacing={2}><Grid size={{ xs: 12, sm: 6 }}>{field('nextServiceDate', 'Next service date', { type: 'date', slotProps: { inputLabel: { shrink: true } } })}</Grid><Grid size={{ xs: 12, sm: 6 }}>{number('nextServiceMeter', `Next service threshold${asset?.meterUnit ? ` (${asset.meterUnit})` : ''}`)}</Grid></Grid>
                {form.isPreventive && number('serviceIntervalMonths', 'Repeat service every (months)', { slotProps: { htmlInput: { min: 1, max: 120, step: 1 } }, helperText: 'On completion, calculates the next date when no explicit date is entered. Date and meter targets use whichever is reached first.' })}
                <Alert severity="info">Completing work records service targets. A separate safety check returns the asset to service.</Alert>
              </Stack>}
              {tab === 'Costs' && access.canFinancial && <Stack spacing={2.5}>
                <Typography variant="subtitle1" fontWeight={700}>Expenses in FJD</Typography>{number('estimatedCost', 'Estimated cost (FJD)')}
                {job && <><FormControlLabel control={<Checkbox checked={form.useDetailedCosts} disabled={busy || readOnly} onChange={e => change('useDetailedCosts', e.target.checked)} />} label="Calculate total from expense breakdown" />
                {form.useDetailedCosts ? <><Grid container spacing={2}>{([['partsCost', 'Parts'], ['labourCost', 'Labour total'], ['transportCost', 'Transport'], ['externalServiceCost', 'Contractor / external service'], ['fuelCost', 'Fuel'], ['otherCost', 'Other']] as const).map(([key, title]) => <Grid size={{ xs: 12, sm: 6 }} key={key}>{number(key, `${title} (FJD)`, { disabled: busy || readOnly || key === 'labourCost' && form.labourHours !== '' && form.labourRate !== '', value: key === 'labourCost' && form.labourHours !== '' && form.labourRate !== '' ? labour : form[key] })}</Grid>)}</Grid><Grid container spacing={2}><Grid size={{ xs: 12, sm: 6 }}>{number('labourHours', 'Labour hours')}</Grid><Grid size={{ xs: 12, sm: 6 }}>{number('labourRate', 'Labour hourly rate (FJD)')}</Grid></Grid><Typography variant="caption">Issued stock is included in Parts. Return unused stock before reducing its net cost. Enter both labour hours and rate to calculate labour.</Typography><Box component="fieldset" disabled={busy || readOnly} sx={{ border: 0, p: 0, m: 0, minWidth: 0 }}><MaintenanceTaxFields value={taxSettings} onChange={value => { setTaxSettings(value); setDirty(true) }} manualTax={form.taxCost} onManualTax={value => change('taxCost', value)} defaultRate={job.defaultTaxRate ?? 0} issuedStockCost={job.issuedStockCost ?? 0} tax={calculated.tax} taxable={calculated.taxable} /></Box></> : number('actualCost', 'Total cost without breakdown (FJD)', { helperText: 'Use when only the supplier invoice total is known. Issued inventory still needs reconciliation.' })}
                <Alert severity="info" role="status">Actual total: {money(form.useDetailedCosts ? total : Number(form.actualCost || 0))}</Alert>
                {field('invoiceNumber', 'Supplier invoice / reference')}</>}
                {!form.warrantyCovered && !warrantyDetails && <Button onClick={() => setWarrantyDetails(true)}>Add warranty details</Button>}
                {(form.warrantyCovered || warrantyDetails) && <FormControlLabel control={<Checkbox checked={form.warrantyCovered} disabled={busy || readOnly} onChange={e => change('warrantyCovered', e.target.checked)} />} label="Covered by warranty" />}
                {form.warrantyCovered && field('warrantyClaimNumber', 'Warranty claim reference')}
              </Stack>}
              {tab === 'Evidence' && <Stack spacing={2}>
                <Typography variant="subtitle1" fontWeight={700}>Fault photos, repair evidence and invoices</Typography>
                {!jobId ? <Alert severity="info">Save the job first to attach evidence to its permanent reference.</Alert> : <><TextField select label="Attachment category" value={documentType} onChange={e => setDocumentType(e.target.value)}><MenuItem value="FaultPhoto">Fault photo</MenuItem>{access.canFinancial && <MenuItem value="Invoice">Supplier invoice</MenuItem>}<MenuItem value="CompletionEvidence">Completion evidence</MenuItem></TextField>
                  <Button component="label" variant="outlined" disabled={busy}>Attach PDF or photo (up to 5 MB)<input hidden type="file" accept=".pdf,.jpg,.jpeg,.png" onChange={e => { void upload(e.target.files?.[0]); e.target.value = '' }} /></Button>
                  {documents.length === 0 && <Typography color="text.secondary">No attachments recorded.</Typography>}
                  {documents.map(item => <Button key={item.id} onClick={() => void download(item)} sx={{ justifyContent: 'flex-start', overflowWrap: 'anywhere' }}>{item.fileName} · {label(item.type)}</Button>)}</>}
              </Stack>}
              {tab === 'Safety' && job && <Stack spacing={2.5}>
                {job.releasedAt ? <Alert severity="success">Returned to service {date(job.releasedAt, true)} by {job.releasedByName}.</Alert> : !closed(job) ? <Alert severity="info">Complete or cancel the work before recording the final safety check.</Alert> : <Alert severity="warning">The work is closed. The asset remains unavailable until its safety check passes and all other blockers are resolved.</Alert>}
                {!job.releasedAt && closed(job) && access.canComplete && access.canInspect ? <>
                  <Typography variant="subtitle1" fontWeight={700}>Confirm every applicable safety check</Typography>
                  {detail?.safetyChecks.map(check => <FormControlLabel key={check} control={<Checkbox checked={checks.includes(check)} disabled={busy} onChange={e => { setChecks(values => e.target.checked ? [...values, check] : values.filter(x => x !== check)); setDirty(true) }} />} label={check} />)}
                  <TextField label={`Verified meter reading${job.meterUnit ? ` (${job.meterUnit})` : ''}`} type="number" value={safetyMeter} required={Boolean(job.meterUnit)} slotProps={{ htmlInput: { min: job.currentMeterReading ?? 0, step: 'any' } }} onChange={e => { setSafetyMeter(e.target.value); setDirty(true) }} />
                  <TextField label="Safety check / test run notes" value={safetyNotes} multiline minRows={3} required onChange={e => { setSafetyNotes(e.target.value); setDirty(true) }} />
                  <Button variant="contained" disabled={busy || !safetyNotes.trim() || !detail?.safetyChecks.length || checks.length !== detail.safetyChecks.length || Boolean(job.meterUnit && !safetyMeter)} onClick={() => void release()}>Confirm safety and return to service</Button>
                  <Typography variant="caption" color="text.secondary">Your account is recorded as the inspector. A failed check must be resolved before release.</Typography>
                </> : !job.releasedAt && <Typography color="text.secondary">Return to service requires maintenance completion and asset inspection permissions.</Typography>}
              </Stack>}
              {tab === 'History' && detail && <Stack spacing={2.5}>
                <Typography variant="subtitle1" fontWeight={700}>Upcoming service and preventive rules</Typography>{detail.servicePlan && <MaintenanceServicePlan plan={detail.servicePlan} />}
                <Grid container spacing={2}>{[['Completed jobs', detail.metrics.completedJobs], ['Recorded downtime', `${detail.metrics.downtimeHours} h`], ['Repeat failures', detail.metrics.repeatFailures], ...(access.canFinancial ? [['Total recorded cost', money(detail.metrics.totalRecordedCost ?? detail.metrics.totalCost ?? 0)], ['Total completed cost', money(detail.metrics.totalCost ?? 0)], ['Book value', detail.metrics.bookValue == null ? 'Not recorded' : money(detail.metrics.bookValue)], ['Maintenance / book value', detail.metrics.costToBookValuePercent == null ? 'Not available' : `${detail.metrics.costToBookValuePercent}%`]] : [])].map(([name, value]) => <Grid key={name} size={{ xs: 6, sm: 4 }}><Typography variant="caption" color="text.secondary">{name}</Typography><Typography fontWeight={700}>{value}</Typography></Grid>)}</Grid>
                <Typography variant="caption" color="text.secondary">Recorded cost includes actual costs on active, cancelled and completed jobs; completed cost and downtime cover completed jobs. Estimates are excluded. The timeline shows the latest 50 jobs.</Typography>
                <Accordion defaultExpanded><AccordionSummary expandIcon={<ExpandMoreOutlined />}><Typography fontWeight={700}>Service timeline</Typography></AccordionSummary><AccordionDetails><Stack spacing={2}>{detail.history.map(item => <Box key={item.id}><Button disabled={item.id === jobId} onClick={() => guard(() => onOpenJob(item.id))} sx={{ justifyContent: 'flex-start', overflowWrap: 'anywhere' }}>{item.jobNumber} · {label(item.status)}</Button><Typography variant="body2">{date(item.reportedAt)} · {item.serviceType} · {item.assignedTo || item.supplier || 'Unassigned'}</Typography><Typography variant="body2">{item.completionNotes || item.faultDescription}</Typography><Typography variant="caption">Next date: {date(item.nextServiceDate)} · Next meter: {item.nextServiceMeter ?? '—'} {item.meterUnit}</Typography></Box>)}</Stack></AccordionDetails></Accordion>
                <Accordion><AccordionSummary expandIcon={<ExpandMoreOutlined />}><Typography fontWeight={700}>Inspection history</Typography></AccordionSummary><AccordionDetails>{detail.inspections.map(item => <Box key={item.id} mb={2}><Button onClick={() => setInspectionId(item.id)}>{date(item.completedAt, true)} · {label(item.stage)} · {label(item.outcome)}</Button><Typography variant="body2">{item.completedByName}{item.notes ? ` · ${item.notes}` : ''}</Typography></Box>)}</AccordionDetails></Accordion>
                <Accordion><AccordionSummary expandIcon={<ExpandMoreOutlined />}><Typography fontWeight={700}>Meter history</Typography></AccordionSummary><AccordionDetails>{detail.meters.map((item, index) => <Typography key={index} variant="body2" mb={1}>{date(item.recordedAt, true)} · {item.reading} {item.unit} · {label(item.source)}</Typography>)}</AccordionDetails></Accordion>
                <Accordion><AccordionSummary expandIcon={<ExpandMoreOutlined />}><Typography fontWeight={700}>Job audit trail</Typography></AccordionSummary><AccordionDetails>{detail.audits.map((item, index) => <Box key={index} mb={2}><Button onClick={() => setAuditRecord(item)}>{item.action} · {item.userName}</Button><Typography variant="caption">{date(item.occurredAt, true)} · {item.summary}</Typography></Box>)}</AccordionDetails></Accordion>
              </Stack>}
            </>}
          </Box>
          {!['Safety', 'History'].includes(tab) && <Stack direction="row" justifyContent="space-between" alignItems="center" gap={2} sx={{ px: { xs: 2, sm: 3 }, py: 2, borderTop: 1, borderColor: 'divider', bgcolor: 'background.paper' }}><Typography variant="caption" color="text.secondary">{dirty ? 'Unsaved changes' : jobId ? 'Record loaded' : 'New job'}</Typography><Button type="submit" variant="contained" disabled={busy || readOnly || loading || !form.assetId}>{busy ? 'Saving…' : jobId ? 'Save details' : 'Save maintenance job'}</Button></Stack>}
        </Box>
      </Box>
    </Drawer>
    {inspectionId && <MaintenanceInspectionRecord key={inspectionId} id={inspectionId} onClose={() => setInspectionId(null)} />}
    <Dialog open={Boolean(auditRecord)} onClose={() => setAuditRecord(null)} fullWidth maxWidth="sm"><DialogTitle>Audit record</DialogTitle><DialogContent>{auditRecord && <Stack spacing={2}><Typography fontWeight={700}>{auditRecord.action}</Typography><Typography>{auditRecord.userName} · {date(auditRecord.occurredAt, true)}</Typography><Typography sx={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{auditRecord.summary}</Typography><Typography variant="caption" sx={{ overflowWrap: 'anywhere' }}>Job: {job?.jobNumber}{auditRecord.id ? ` · Audit reference: ${auditRecord.id}` : ''}</Typography></Stack>}</DialogContent><DialogActions><Button onClick={() => setAuditRecord(null)}>Close audit record</Button></DialogActions></Dialog>
    {parts && job && <MaintenancePartsDialog job={job} onClose={() => setParts(false)} onChanged={async () => { await loadDetail(job.id); onSaved(`${job.jobNumber}: inventory and expenses updated.`) }} />}
    <Dialog open={Boolean(transition)} onClose={() => !busy && setTransition(null)} fullWidth maxWidth="sm"><DialogTitle>{transition === 'Completed' ? 'Complete the work?' : transition === 'Cancelled' ? 'Cancel this job?' : wantsReason && transition === 'InProgress' ? 'Reopen this job?' : transition === 'WaitingForParts' ? 'Wait for parts?' : 'Start the work?'}</DialogTitle><DialogContent><Typography mb={2}>{transition === 'Completed' ? 'The work summary and meter reading will be saved. The asset remains unavailable until the final safety check passes.' : transition === 'Cancelled' ? 'The job remains in history. Cancellation does not certify that the asset is safe for hire.' : 'This action saves your details and records the status change in the audit trail.'}</Typography>{wantsReason && <TextField autoFocus required fullWidth multiline minRows={2} label="Reason for status change" value={reason} onChange={e => setReason(e.target.value)} />}</DialogContent><DialogActions><Button disabled={busy} onClick={() => setTransition(null)}>Keep editing</Button><Button variant="contained" color={transition === 'Cancelled' ? 'error' : 'primary'} disabled={busy || wantsReason && !reason.trim()} onClick={() => void save(undefined, transition ?? 'Open')}>Confirm</Button></DialogActions></Dialog>
    <Dialog open={discardOpen} onClose={() => setDiscardOpen(false)} maxWidth="xs" fullWidth><DialogTitle>Discard unsaved changes?</DialogTitle><DialogContent>Your saved job will remain. Changes entered since the last save will be discarded.</DialogContent><DialogActions><Button onClick={() => setDiscardOpen(false)}>Keep editing</Button><Button color="error" onClick={() => { setDiscardOpen(false); setDirty(false); pending.current?.(); pending.current = null }}>Discard changes</Button></DialogActions></Dialog>
  </>
}
