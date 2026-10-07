import { useCallback, useEffect, useState } from 'react'
import axios from 'axios'
import AddOutlined from '@mui/icons-material/AddOutlined'
import BuildOutlined from '@mui/icons-material/BuildOutlined'
import SearchOutlined from '@mui/icons-material/SearchOutlined'
import RefreshOutlined from '@mui/icons-material/RefreshOutlined'
import { Alert, Autocomplete, Box, Button, Card, CardContent, Chip, CircularProgress, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, Checkbox, Grid, InputAdornment, MenuItem, Stack, Tab, Tabs, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, TablePagination, TextField, Typography } from '@mui/material'
import { api } from '../api/client'
import { MaintenanceStatusDisplay } from './maintenance/MaintenanceStatusDisplay'
import { MaintenanceServicePlan } from './maintenance/MaintenanceServicePlan'
import { MaintenanceWorkspace } from './maintenance/MaintenanceWorkspace'
import { closed, date, label, money, repairOverdue, type ScheduledAsset, type Asset, type Options, type Register, type Schedule, type Job } from './maintenance/types'

type Filters = { queue: string; search: string; branchId: string; technician: string; status: string; priority: string; unassigned: boolean; dueOnly: boolean; overdueOnly: boolean; overdueRepairs: boolean; sort: string; ascending: boolean; page: number; pageSize: number }
const defaults: Filters = { queue: 'Active', search: '', branchId: '', technician: '', status: '', priority: '', unassigned: false, dueOnly: false, overdueOnly: false, overdueRepairs: false, sort: 'reported', ascending: false, page: 1, pageSize: 25 }
const filterKey = 'crems.maintenance.filters.v2'
function initialFilters(): Filters {
  try { const saved = JSON.parse(sessionStorage.getItem(filterKey) ?? '{}'); return { ...defaults, ...saved, search: new URLSearchParams(window.location.search).get('search') ?? saved.search ?? '' } } catch { return defaults }
}
const emptyOptions: Options = { branches: [], technicians: [], suppliers: [], technicianNames: [] }
const zero = { active: 0, unassigned: 0, inProgress: 0, waiting: 0, awaitingRelease: 0, preventive: 0, overdueRepairs: 0 }
export function MaintenancePage() {
  const [filters, setFilters] = useState(initialFilters)
  const [register, setRegister] = useState<Register | null>(null)
  const [schedule, setSchedule] = useState<Schedule | null>(null)
  const [options, setOptions] = useState<Options>(emptyOptions)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [showFilters, setShowFilters] = useState(false)
  const [revision, setRevision] = useState(0)
  const [ruleAsset, setRuleAsset] = useState<ScheduledAsset | null>(null)
  const [selected, setSelected] = useState<string | null>(null)
  const [creating, setCreating] = useState(false)
  const [initialAsset, setInitialAsset] = useState<Asset | null>(null)
  const refresh = useCallback(() => setRevision(value => value + 1), [])
  const update = (values: Partial<Filters>) => setFilters(current => ({ ...current, page: 1, ...values }))
  useEffect(() => { try { sessionStorage.setItem(filterKey, JSON.stringify(filters)) } catch { /* Storage can be unavailable. */ } }, [filters])
  useEffect(() => {
    let active = true
    api.get<Options>('/maintenance-jobs/options').then(result => { if (active) setOptions(result.data) }).catch(() => { if (active) setError('Assignment choices could not be loaded. Refresh to retry.') })
    return () => { active = false }
  }, [revision])
  useEffect(() => {
    const controller = new AbortController()
    const timer = window.setTimeout(() => {
      setLoading(true); setError('')
      const params = { ...filters, queue: filters.queue === 'Schedule' ? 'Preventive' : filters.queue, branchId: filters.branchId || undefined, status: filters.status || undefined, priority: filters.priority || undefined, technician: filters.technician || undefined }
      Promise.all([api.get<Register>('/maintenance-jobs/workspace', { params, signal: controller.signal }), api.get<Schedule>('/maintenance-jobs/schedule', { params: { search: filters.queue === 'Schedule' ? filters.search : '', branchId: filters.branchId || undefined, dueOnly: filters.dueOnly, overdueOnly: filters.overdueOnly, page: filters.queue === 'Schedule' ? filters.page : 1, pageSize: filters.pageSize }, signal: controller.signal })])
        .then(([jobs, assets]) => { if (!controller.signal.aborted) { setRegister(jobs.data); setSchedule(assets.data); const total = filters.queue === 'Schedule' ? assets.data.total : jobs.data.total; if (filters.page > 1 && (filters.page - 1) * filters.pageSize >= total) setFilters(current => ({ ...current, page: Math.max(1, Math.ceil(total / current.pageSize)) })) } })
        .catch(reason => { if (!axios.isCancel(reason)) setError('Maintenance records could not be loaded. Retry without losing your filters.') })
        .finally(() => { if (!controller.signal.aborted) setLoading(false) })
    }, 250)
    return () => { clearTimeout(timer); controller.abort() }
  }, [filters, revision])
  const summaryFilter = (values: Partial<Filters>) => update({ ...defaults, branchId: filters.branchId, ...values })
  const counts = register?.counts ?? zero
  const scheduled = filters.queue === 'Schedule'
  const isFiltered = Boolean(filters.search || filters.branchId || filters.technician || filters.status || filters.priority || filters.unassigned || filters.dueOnly || filters.overdueOnly || filters.overdueRepairs)
  const primaryAction = (job: Job) => closed(job) ? job.releasedAt ? 'View history' : 'Safety check' : job.status === 'Open' ? 'Plan work' : 'Continue work'
  const openCreate = (asset: Asset | null = null) => { setInitialAsset(asset); setCreating(true) }
  return <Box sx={{ p: { xs: 2, sm: 3 }, maxWidth: 1680, mx: 'auto', minWidth: 0 }}>
    <Stack direction={{ xs: 'column', sm: 'row' }} gap={2} justifyContent="space-between" alignItems={{ sm: 'center' }} mb={3}>
      <Box><Typography variant="overline" color="text.secondary">Asset operations</Typography><Typography variant="h4">Maintenance</Typography><Typography color="text.secondary" mt={.5}>Plan the work. Record the repair. Verify the asset is safe to hire.</Typography></Box>
      <Stack direction="row" gap={1}><Button startIcon={<RefreshOutlined />} onClick={refresh}>Refresh</Button><Button startIcon={<AddOutlined />} variant="contained" onClick={() => openCreate()} disabled={!register}>Log maintenance job</Button></Stack>
    </Stack>
    {notice && <Alert severity="success" onClose={() => setNotice('')} sx={{ mb: 2 }}>{notice}</Alert>}
    {error && <Alert severity="error" action={<Button color="inherit" onClick={refresh}>Retry</Button>} sx={{ mb: 2 }}>{error}</Alert>}
    <Grid container spacing={1.5} mb={2.5}>{[
      { name: 'Overdue service', value: schedule?.overdueCount ?? 0, action: () => summaryFilter({ queue: 'Schedule', overdueOnly: true, dueOnly: false, overdueRepairs: false, status: '', unassigned: false }) },
      { name: 'Missed repair deadlines', value: counts.overdueRepairs ?? 0, action: () => summaryFilter({ queue: 'History', overdueRepairs: true, overdueOnly: false, dueOnly: false, status: '', unassigned: false, sort: 'release', ascending: true }) },
      { name: 'Unassigned jobs', value: counts.unassigned, action: () => summaryFilter({ queue: 'Active', unassigned: true, status: '', overdueRepairs: false }) },
      { name: 'In progress', value: counts.inProgress, action: () => summaryFilter({ queue: 'Active', status: 'InProgress', unassigned: false, overdueRepairs: false }) },
      { name: 'Waiting for parts', value: counts.waiting, action: () => summaryFilter({ queue: 'Active', status: 'WaitingForParts', unassigned: false, overdueRepairs: false }) },
      { name: 'Jobs awaiting safety check', value: counts.awaitingRelease, action: () => summaryFilter({ queue: 'AwaitingRelease', status: '', unassigned: false, overdueRepairs: false }) },
    ].map(item => <Grid size={{ xs: 6, lg: 2 }} key={item.name}><Card variant="outlined"><Button onClick={item.action} sx={{ width: '100%', justifyContent: 'flex-start', textAlign: 'left', p: 2 }}><Box><Typography variant="h5">{item.value}</Typography><Typography variant="body2" color="text.secondary">{item.name}</Typography></Box></Button></Card></Grid>)}</Grid>
    {Boolean(schedule?.dueCount) && <Alert severity="warning" sx={{ mb: 2, flexWrap: 'wrap', '& .MuiAlert-message': { minWidth: 0 }, '& .MuiAlert-action': { flexBasis: { xs: '100%', sm: 'auto' }, ml: { xs: 0, sm: 'auto' }, justifyContent: 'flex-start' } }} action={<Button color="inherit" onClick={() => update({ queue: 'Schedule', dueOnly: true, overdueOnly: false, overdueRepairs: false, status: '', technician: '', priority: '', unassigned: false })}>Review due assets</Button>}>{schedule?.dueCount} {schedule?.dueCount === 1 ? 'asset has' : 'assets have'} reached a service date or meter threshold. Check them before the next hire.</Alert>}
    <Card variant="outlined" sx={{ minWidth: 0 }}>
      <Tabs value={filters.queue} onChange={(_, queue: string) => update({ queue, status: '', unassigned: false, dueOnly: false, overdueOnly: false, overdueRepairs: false, technician: '', priority: '' })} variant="scrollable" scrollButtons="auto" sx={{ px: 1, borderBottom: 1, borderColor: 'divider' }}>
        <Tab value="Active" label={`Active work (${counts.active})`} /><Tab value="Schedule" label="Preventive schedule" /><Tab value="AwaitingRelease" label={`Safety checks (${counts.awaitingRelease})`} /><Tab value="History" label="Service history" />
      </Tabs>
      <Box p={2}>
        <Stack direction="row" useFlexGap flexWrap="wrap" gap={1.5}>
          <TextField size="small" label={scheduled ? 'Search assets' : 'Search maintenance jobs'} value={filters.search} onChange={e => update({ search: e.target.value })} placeholder="Asset number, name or job reference" sx={{ flex: '1 1 280px', minWidth: 0, maxWidth: 500 }} slotProps={{ input: { startAdornment: <InputAdornment position="start"><SearchOutlined /></InputAdornment> } }} />
          <Button sx={{ display: { md: 'none' } }} onClick={() => setShowFilters(value => !value)} aria-expanded={showFilters}>{showFilters ? 'Hide filters' : isFiltered ? 'Filters applied' : 'Filters'}</Button>
          <Box sx={{ display: { xs: showFilters ? 'contents' : 'none', md: 'contents' } }}><TextField select label="Branch" value={filters.branchId} onChange={e => update({ branchId: e.target.value })} sx={{ minWidth: 180 }}><MenuItem value="">All branches</MenuItem>{options.branches.map(branch => <MenuItem key={branch.branchId} value={branch.branchId}>{branch.name}</MenuItem>)}</TextField>
          {!scheduled && <><Autocomplete options={options.technicianNames.filter((x): x is string => Boolean(x))} value={filters.technician || null} onChange={(_, value) => update({ technician: value ?? '' })} sx={{ minWidth: 190 }} renderInput={params => <TextField {...params} label="Technician" />} />
            <TextField select label="Priority" value={filters.priority} onChange={e => update({ priority: e.target.value })} sx={{ minWidth: 145 }}><MenuItem value="">All priorities</MenuItem>{['Low', 'Normal', 'High', 'Critical'].map(value => <MenuItem key={value} value={value}>{value}</MenuItem>)}</TextField>
            <TextField select label="Status" value={filters.status} onChange={e => update({ status: e.target.value })} sx={{ minWidth: 160 }}><MenuItem value="">All statuses</MenuItem>{(filters.queue === 'Active' ? ['Open', 'InProgress', 'WaitingForParts'] : filters.queue === 'AwaitingRelease' ? ['Completed', 'Cancelled'] : ['Open', 'InProgress', 'WaitingForParts', 'Completed', 'Cancelled']).map(value => <MenuItem key={value} value={value}>{label(value)}</MenuItem>)}</TextField></>}
          {!scheduled && <TextField select label="Sort by" value={filters.sort} onChange={e => update({ sort: e.target.value })} sx={{ minWidth: 150 }}>{[['reported', 'Reported date'], ['priority', 'Priority'], ['asset', 'Asset number'], ['reference', 'Job reference'], ['status', 'Status'], ['branch', 'Branch'], ['release', 'Expected release']].map(([value, text]) => <MenuItem key={value} value={value}>{text}</MenuItem>)}</TextField>}
          {!scheduled && <Button onClick={() => update({ ascending: !filters.ascending })}>{filters.ascending ? 'Ascending' : 'Descending'}</Button>}
          </Box>{isFiltered && <Button onClick={() => setFilters({ ...defaults, queue: filters.queue })}>Clear filters</Button>}
        </Stack>
        <Stack direction="row" useFlexGap flexWrap="wrap" gap={2} mt={1}>{scheduled ? <FormControlLabel control={<Checkbox checked={filters.dueOnly} onChange={e => update({ dueOnly: e.target.checked, overdueOnly: false })} />} label="Due assets only" /> : <FormControlLabel control={<Checkbox checked={filters.unassigned} onChange={e => update({ unassigned: e.target.checked })} />} label="Unassigned only" />}
          {scheduled ? <FormControlLabel control={<Checkbox checked={filters.overdueOnly} onChange={e => update({ overdueOnly: e.target.checked, dueOnly: false })} />} label="Overdue service only" /> : <FormControlLabel control={<Checkbox checked={filters.overdueRepairs} onChange={e => update({ overdueRepairs: e.target.checked })} />} label="Missed repair deadlines only" />}
          <Typography variant="body2" color="text.secondary" alignSelf="center">{loading ? 'Loading…' : `${scheduled ? schedule?.total ?? 0 : register?.total ?? 0} matching ${scheduled ? 'assets' : 'jobs'}`} · {scheduled ? 'Service targets include recorded dates, completed work and configured meter intervals.' : filters.queue === 'AwaitingRelease' ? 'Work closed; asset availability still requires verification.' : 'Times shown in Fiji local time.'}</Typography></Stack>
      </Box>
      {loading ? <Box sx={{ height: 280, display: 'grid', placeItems: 'center' }}><CircularProgress aria-label="Loading maintenance" /></Box> : scheduled ? <>
        <TableContainer sx={{ display: { xs: 'none', md: 'block' } }}><Table><TableHead><TableRow>{['Asset', 'Branch', 'Service date', 'Meter target', 'Status', 'Action'].map(text => <TableCell key={text}>{text}</TableCell>)}</TableRow></TableHead><TableBody>{schedule?.items.map(asset => <TableRow key={asset.id}><TableCell><Typography fontWeight={700}>{asset.assetNumber}</Typography><Typography variant="body2">{asset.name}</Typography></TableCell><TableCell>{asset.branchName}</TableCell><TableCell>{date(asset.nextServiceDate)}{asset.nextServiceDate && asset.nextServiceDate <= (schedule?.today ?? '') && <Chip label={asset.nextServiceDate < (schedule?.today ?? '') ? 'Overdue' : 'Due today'} color="warning" sx={{ ml: 1 }} />}</TableCell><TableCell>{asset.nextServiceMeter == null ? 'Not set' : `${asset.currentMeterReading ?? '—'} / ${asset.nextServiceMeter} ${asset.meterUnit ?? ''}`}<Typography variant="caption" display="block" color="text.secondary">{asset.meterTargetSource}</Typography>{asset.isOverdue && <Chip label="Service overdue" color="warning" size="small" />}<Button size="small" onClick={() => setRuleAsset(asset)}>View service rules</Button></TableCell><TableCell>{label(asset.status)}</TableCell><TableCell><Button onClick={() => asset.activeJobId ? setSelected(asset.activeJobId) : openCreate(asset)} disabled={!asset.activeJobId && !['Available', 'Maintenance', 'OutOfService'].includes(asset.status)}>{asset.activeJobId ? 'Open work' : 'Log service'}</Button></TableCell></TableRow>)}</TableBody></Table></TableContainer>
        <Stack spacing={1.5} sx={{ display: { md: 'none' }, p: 2 }}>{schedule?.items.map(asset => <Card key={asset.id} variant="outlined"><CardContent><Typography fontWeight={700}>{asset.assetNumber} · {asset.name}</Typography><Typography variant="body2">{asset.branchName} · {label(asset.status)}</Typography><Typography variant="body2">Service date: {date(asset.nextServiceDate)}</Typography><Typography variant="body2">Meter target: {asset.nextServiceMeter ?? 'Not set'} {asset.meterUnit}</Typography>{asset.isOverdue && <Chip label="Service overdue" color="warning" size="small" />}<Button onClick={() => setRuleAsset(asset)}>View service rules</Button><Button onClick={() => asset.activeJobId ? setSelected(asset.activeJobId) : openCreate(asset)} disabled={!asset.activeJobId && !['Available', 'Maintenance', 'OutOfService'].includes(asset.status)}>{asset.activeJobId ? 'Open work' : 'Log service'}</Button></CardContent></Card>)}</Stack>
        {schedule?.total === 0 && <Empty text="No assets match this schedule. Clear the filters or record a next-service target in the asset's maintenance workspace." />}
      </> : <>
        <TableContainer sx={{ display: { xs: 'none', md: 'block' } }}><Table><TableHead><TableRow><TableCell>Asset / job</TableCell><TableCell>Work</TableCell><TableCell>Assigned</TableCell><TableCell>Expected release</TableCell>{register?.permissions.canFinancial && <TableCell align="right">Cost (FJD)</TableCell>}<TableCell>Status</TableCell><TableCell align="right">Next action</TableCell></TableRow></TableHead><TableBody>{register?.items.map(job => <TableRow key={job.id} hover><TableCell sx={{ maxWidth: 250 }}><Typography fontWeight={700}>{job.assetNumber}</Typography><Typography variant="body2">{job.assetName}</Typography><Typography variant="caption" color="text.secondary" sx={{ overflowWrap: 'anywhere' }}>{job.jobNumber} · {job.branchName}</Typography></TableCell><TableCell><Typography variant="body2">{job.serviceType}</Typography><Chip label={job.priority} color={job.priority === 'Critical' ? 'error' : job.priority === 'High' ? 'warning' : 'default'} /></TableCell><TableCell>{job.assignedTo || job.supplier || 'Unassigned'}</TableCell><TableCell>{date(job.expectedReleaseAt, true)}{repairOverdue(job) && <Chip label="Repair deadline missed" color="error" size="small" sx={{ mt: .5 }} />}</TableCell>{register.permissions.canFinancial && <TableCell align="right">{job.actualCost == null && !job.hasEstimate ? 'Not estimated' : money(job.actualCost ?? job.estimatedCost ?? 0)}<Typography variant="caption" display="block" color="text.secondary">{job.actualCost == null ? job.hasEstimate ? 'Estimated' : 'Estimate pending' : 'Actual'}</Typography></TableCell>}<TableCell><MaintenanceStatusDisplay job={job} /></TableCell><TableCell align="right"><Button variant="outlined" onClick={() => setSelected(job.id)} aria-label={`${primaryAction(job)} ${job.jobNumber}`}>{primaryAction(job)}</Button></TableCell></TableRow>)}</TableBody></Table></TableContainer>
        <Stack spacing={1.5} sx={{ display: { md: 'none' }, p: 2 }}>{register?.items.map(job => <Card variant="outlined" key={job.id}><CardContent><Stack direction="row" gap={1} justifyContent="space-between"><Typography fontWeight={700}>{job.assetNumber}</Typography><MaintenanceStatusDisplay job={job} /></Stack><Typography>{job.assetName}</Typography><Typography variant="caption" sx={{ overflowWrap: 'anywhere' }}>{job.jobNumber} · {job.branchName}</Typography><Typography variant="body2" mt={1}>{job.serviceType} · {job.priority}</Typography><Typography variant="body2">{job.assignedTo || job.supplier || 'Unassigned'}</Typography><Typography variant="body2">Expected release: {date(job.expectedReleaseAt, true)}</Typography>{repairOverdue(job) && <Chip label="Repair deadline missed" color="error" size="small" />}<Button variant="contained" sx={{ mt: 2 }} onClick={() => setSelected(job.id)}>{primaryAction(job)}</Button></CardContent></Card>)}</Stack>
        {register?.total === 0 && <Empty text={isFiltered ? 'No jobs match these filters. Clear filters to see this queue.' : 'No jobs in this queue. Log maintenance work or choose another view.'} />}
      </>}
      <TablePagination component="div" count={scheduled ? schedule?.total ?? 0 : register?.total ?? 0} page={filters.page - 1} rowsPerPage={filters.pageSize} rowsPerPageOptions={[25, 50, 100]} onPageChange={(_, page) => update({ page: page + 1 })} onRowsPerPageChange={e => update({ pageSize: Number(e.target.value) })} />
    </Card>
    <Dialog open={Boolean(ruleAsset)} onClose={() => setRuleAsset(null)} maxWidth="sm" fullWidth><DialogTitle>Preventive service rules</DialogTitle><DialogContent>{ruleAsset && <MaintenanceServicePlan plan={ruleAsset} />}</DialogContent><DialogActions><Button onClick={() => setRuleAsset(null)}>Close service rules</Button></DialogActions></Dialog>
    {(selected || creating) && <MaintenanceWorkspace key={selected ?? 'new'} jobId={selected} initialAsset={initialAsset} options={options} onOpenJob={id => setSelected(id)} permissions={register?.permissions ?? { canComplete: false, canInspect: false, canFinancial: false }} onClose={() => { setSelected(null); setCreating(false) }} onSaved={(message, id) => { setNotice(message); refresh(); if (id) { setCreating(false); setSelected(id) } }} />}
  </Box>
}
function Empty({ text }: { text: string }) { return <Stack alignItems="center" spacing={1} py={6} px={3}><BuildOutlined color="disabled" sx={{ fontSize: 40 }} /><Typography color="text.secondary" textAlign="center">{text}</Typography></Stack> }
