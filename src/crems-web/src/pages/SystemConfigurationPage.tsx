import { useActionNotice } from '../components/useActionNotice'
import { useCallback, useEffect, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Accordion, AccordionDetails, AccordionSummary, Alert, Box, Button, Card, CardContent, Chip, FormControlLabel, Grid, MenuItem, Stack, Switch, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, TextField, Typography } from '@mui/material'
import ExpandMoreOutlined from '@mui/icons-material/ExpandMoreOutlined'
import { apiErrorMessage } from '../api/errors'
import { api } from '../api/client'
import MonitorHeartOutlined from '@mui/icons-material/MonitorHeartOutlined'
import { PageHeader } from '../components/PageHeader'

type Setting = { id: string; key: string; value: string; category: string; description: string; isSecret: boolean }
type Template = { id: string; key: string; name: string; channel: string; subject: string; body: string; isActive: boolean }
type Health = { status: string; databaseConnected: boolean; pendingMigrations: string[]; queuedNotifications: number; failedNotifications: number; environment: string; version: string; emailProviderConfigured: boolean; smsProviderConfigured: boolean }
type Audit = { id: string; userName: string; action: string; summary: string; occurredAt: string }

const settingNames: Record<string, string> = {
  'rentals.vatRate': 'VAT rate (%)',
  'rentals.bookingPrefix': 'Booking reference prefix',
  'rentals.agreementPrefix': 'Rental agreement reference prefix',
  'data.retentionYears': 'Record retention review age (years)',
  'security.lockoutMinutes': 'Account lockout duration (minutes)',
  'security.maxFailedAttempts': 'Failed password attempts before lockout',
  'rentals.defaultDeposit': 'Default refundable bond',
  'rentals.defaultCurrency': 'Currency',
  'rentals.lateFeeMultiplier': 'Late return fee multiplier',
  'bookings.holdMinutes': 'Reservation hold time (minutes)',
  'notifications.emailFrom': 'Sender email address',
}

export function SystemConfigurationSection({ section }: { section: 'defaults' | 'notifications' | 'health' }) {
  const [settings, setSettings] = useState<Setting[]>([])
  const [templates, setTemplates] = useState<Template[]>([])
  const [draftTemplate,setDraftTemplate]=useState({key:'',name:'',channel:'InApp',subject:'',body:'',isActive:true})
  const [showTemplateForm,setShowTemplateForm]=useState(false)
  const healthQuery = useQuery({
    queryKey: ['administration', 'health'],
    queryFn: async ({ signal }) => (await api.get<Health>('/administration/health', { signal })).data,
    enabled: section === 'health',
    refetchInterval: 5000,
    refetchIntervalInBackground: false,
    refetchOnWindowFocus: true,
    staleTime: 0,
    gcTime: 0,
    retry: false,
  })
  const auditQuery = useQuery({
    queryKey: ['administration', 'audit'],
    queryFn: async ({ signal }) => (await api.get<Audit[]>('/administration/audit?take=100', { signal })).data,
    enabled: section === 'health',
    refetchInterval: 15000,
    refetchIntervalInBackground: false,
    staleTime: 0,
    gcTime: 0,
    retry: false,
  })
  const health = healthQuery.data
  const audit = auditQuery.data ?? []
  const [saving, setSaving] = useState(false)
  const setSaved = useActionNotice()
  const [error, setError] = useState('')

  const load = useCallback(async () => {
    try {
      if (section === 'defaults') {
        const response = await api.get<Setting[]>('/administration/settings')
        setSettings(response.data.filter(item => item.key !== 'rentals.defaultDeposit' && !item.key.startsWith('rentals.fijiPricingDefaults.')))
      } else if (section === 'notifications') {
        setTemplates((await api.get<Template[]>('/administration/templates')).data)
      }
      setError('')
    } catch { setError('This configuration section could not be loaded. Please try again.') }
  }, [section])
  useEffect(() => { if (section !== 'health') void Promise.resolve().then(load) }, [load, section])

  async function saveSetting(item: Setting) {
    setSaving(true);setError('');setSaved('')
    try {
      await api.put(`/administration/settings/${encodeURIComponent(item.key)}`, { value: item.value })
      setSettings(current=>current.map(entry=>entry.key===item.key?{...entry,value:item.key.endsWith('Prefix')?item.value.trim().toUpperCase():item.value.trim()}:entry))
      setSaved(`${settingNames[item.key]??item.key} saved.`)
    } catch(reason) { setError(apiErrorMessage(reason,'This setting could not be saved.')) }
    finally { setSaving(false) }
  }

  async function saveTemplate(item: Template) { setSaving(true);setError('');try { await api.put(`/administration/templates/${item.id}`, item); await load();setSaved(`${item.name} saved.`) } catch(reason) { setError(apiErrorMessage(reason,'This message template could not be saved.')) } finally { setSaving(false) } }
  async function createTemplate(){setSaving(true);setError('');try{await api.post('/administration/templates',{...draftTemplate,key:`manual.${draftTemplate.key.trim().toLowerCase()}`});setDraftTemplate({key:'',name:'',channel:'InApp',subject:'',body:'',isActive:true});setShowTemplateForm(false);await load();setSaved('Message template added. Staff can now select it when composing a message.')}catch(reason){setError(apiErrorMessage(reason,'The message template could not be added.'))}finally{setSaving(false)}}

  return <Box>

    {error && <Alert severity="error" sx={{ mx: { xs: 2, sm: 3, lg: 4 }, mb: 2 }}>{error}</Alert>}
    {section === 'defaults' && <Box sx={{ p: { xs: 2, sm: 3, lg: 4 }, maxWidth: 1100 }}><Alert severity="info" sx={{ mb: 2 }}>Refundable bonds are configured in Booking &amp; pricing → Booking &amp; quotations, with individual overrides in Asset register → Edit asset. The retired global bond setting is no longer used.</Alert><Typography variant="h5" fontWeight={800}>Rental defaults</Typography><Typography color="text.secondary" mt={.5} mb={3}>Saving VAT here applies it to all divisions for new customer requests. Existing bookings and invoices keep their agreed rates.</Typography><Card variant="outlined" sx={{mb:2}}><CardContent><Typography fontWeight={700}>Default currency</Typography><Typography variant="h6">FJD � Fiji dollar</Typography><Typography variant="body2" color="text.secondary">Customer estimates, bookings and invoices use FJD.</Typography></CardContent></Card><Stack spacing={2}>{settings.map((item, index) => <Card key={item.key} variant="outlined"><CardContent><Stack direction={{ xs: 'column', sm: 'row' }} gap={2} alignItems={{ sm: 'center' }}><Box sx={{ flex: 1 }}><Typography fontWeight={700}>{settingNames[item.key] ?? item.description ?? item.key}</Typography><Typography variant="body2" color="text.secondary">{item.key === 'rentals.vatRate' ? 'Applies to all divisions. Set a division-specific rate afterwards in Booking &amp; quotations if required.' : item.description}</Typography></Box><TextField size="small" sx={{width:{xs:'100%',sm:200},flexShrink:0}} label={item.key.endsWith('Prefix')?'Prefix':item.key==='data.retentionYears'?'Years':item.key==='security.lockoutMinutes'?'Minutes':item.key==='security.maxFailedAttempts'?'Attempts':item.key==='rentals.vatRate'?'Percent':'Multiplier'} disabled={saving} type={item.isSecret?'password':item.key.endsWith('Prefix')?'text':'number'} inputProps={item.key.endsWith('Prefix')?{'aria-label':settingNames[item.key],maxLength:12}:{'aria-label':settingNames[item.key],min:item.key.startsWith('rentals.')?0:1,max:item.key==='security.lockoutMinutes'?1440:item.key==='rentals.lateFeeMultiplier'?10:100,step:item.key.startsWith('rentals.')?'any':1}} value={item.value} onChange={event => setSettings(current => current.map((entry, i) => i === index ? { ...entry, value: event.target.value } : entry))} /><Button variant="outlined" disabled={saving||!item.value.trim()} onClick={() => void saveSetting(item)}>{item.key === 'rentals.vatRate' ? 'Save & apply to all divisions' : 'Save'}</Button></Stack></CardContent></Card>)}</Stack></Box>}
    {section === 'notifications' && <Box sx={{ p: { xs: 2, sm: 3, lg: 4 }, maxWidth: 1300 }}><Stack direction={{xs:'column',sm:'row'}} justifyContent="space-between" gap={2} alignItems={{sm:'center'}} mb={2}><Box><Typography variant="h5" fontWeight={800}>Message templates</Typography><Typography color="text.secondary">Reusable wording for staff-created customer and staff messages.</Typography></Box><Button variant="contained" onClick={()=>setShowTemplateForm(value=>!value)}>{showTemplateForm?'Cancel new template':'Add template'}</Button></Stack><Alert severity="info" sx={{mb:2}}>Manual templates are available in the Send a message window. Existing legacy automation drafts are listed separately because booking and invoice emails currently use their own application wording.</Alert>{showTemplateForm&&<Card variant="outlined" sx={{mb:2,borderTop:'3px solid',borderTopColor:'secondary.main'}}><CardContent><Typography variant="h6" fontWeight={800} mb={2}>New manual message template</Typography><Grid container spacing={2}><Grid size={{xs:12,md:4}}><TextField fullWidth label="Template key" helperText="Letters, numbers and hyphens; manual. is added automatically" value={draftTemplate.key} onChange={e=>setDraftTemplate(v=>({...v,key:e.target.value}))}/></Grid><Grid size={{xs:12,md:5}}><TextField fullWidth label="Display name" value={draftTemplate.name} onChange={e=>setDraftTemplate(v=>({...v,name:e.target.value}))}/></Grid><Grid size={{xs:12,md:3}}><TextField select fullWidth label="Channel" value={draftTemplate.channel} onChange={e=>setDraftTemplate(v=>({...v,channel:e.target.value}))}><MenuItem value="InApp">In-app only</MenuItem><MenuItem value="Email">In-app + optional email</MenuItem></TextField></Grid><Grid size={12}><TextField fullWidth label="Title / email subject" value={draftTemplate.subject} onChange={e=>setDraftTemplate(v=>({...v,subject:e.target.value}))}/></Grid><Grid size={12}><TextField fullWidth multiline minRows={4} label="Message" value={draftTemplate.body} onChange={e=>setDraftTemplate(v=>({...v,body:e.target.value}))}/></Grid></Grid><Button variant="contained" sx={{mt:2}} disabled={saving||!draftTemplate.key.trim()||!draftTemplate.name.trim()||!draftTemplate.subject.trim()||!draftTemplate.body.trim()} onClick={()=>void createTemplate()}>Create template</Button></CardContent></Card>}<Stack spacing={2}>{templates.filter(item=>item.key.startsWith('manual.')).map(item=><Card key={item.id} variant="outlined"><CardContent><Stack direction="row" justifyContent="space-between" alignItems="center" mb={2}><Box><Typography variant="h6" fontWeight={800}>{item.name}</Typography><Typography variant="caption" color="text.secondary">{item.key}</Typography></Box><Chip size="small" color={item.isActive?'success':'default'} label={item.isActive?'Available in compose':'Inactive'}/></Stack><Grid container spacing={2}><Grid size={{xs:12,md:6}}><TextField fullWidth label="Display name" value={item.name} onChange={event=>setTemplates(current=>current.map(entry=>entry.id===item.id?{...entry,name:event.target.value}:entry))}/></Grid><Grid size={{xs:12,md:6}}><TextField select fullWidth label="Channel" value={item.channel} onChange={event=>setTemplates(current=>current.map(entry=>entry.id===item.id?{...entry,channel:event.target.value}:entry))}><MenuItem value="InApp">In-app only</MenuItem><MenuItem value="Email">In-app + optional email</MenuItem></TextField></Grid><Grid size={12}><TextField fullWidth label="Title / email subject" value={item.subject} onChange={event=>setTemplates(current=>current.map(entry=>entry.id===item.id?{...entry,subject:event.target.value}:entry))}/></Grid><Grid size={12}><TextField fullWidth multiline minRows={3} label="Message" value={item.body} onChange={event=>setTemplates(current=>current.map(entry=>entry.id===item.id?{...entry,body:event.target.value}:entry))}/></Grid></Grid><Stack direction="row" justifyContent="space-between" alignItems="center" mt={2}><FormControlLabel control={<Switch checked={item.isActive} onChange={event=>setTemplates(current=>current.map(entry=>entry.id===item.id?{...entry,isActive:event.target.checked}:entry))}/>} label="Available to staff"/><Button variant="contained" disabled={saving||!item.name.trim()||!item.subject.trim()||!item.body.trim()} onClick={()=>void saveTemplate(item)}>Save template</Button></Stack></CardContent></Card>)}</Stack>{!templates.some(item=>item.key.startsWith('manual.'))&&<Alert severity="info">No manual templates yet. Add one to give staff approved wording when sending a message.</Alert>}<Accordion sx={{mt:3}}><AccordionSummary expandIcon={<ExpandMoreOutlined/>}><Box><Typography fontWeight={800}>Legacy automation drafts</Typography><Typography variant="body2" color="text.secondary">Stored wording not currently connected to booking or invoice sends.</Typography></Box></AccordionSummary><AccordionDetails><Stack gap={1}>{templates.filter(item=>!item.key.startsWith('manual.')).map(item=><Card key={item.id} variant="outlined"><CardContent><Typography fontWeight={700}>{item.name}</Typography><Typography variant="caption" color="text.secondary">{item.key} · {item.channel}</Typography><Typography variant="body2" mt={1}>{item.subject}</Typography><Typography variant="body2" color="text.secondary">{item.body}</Typography></CardContent></Card>)}</Stack></AccordionDetails></Accordion></Box>}
    {section === 'health' && <Box sx={{ p: { xs: 2, sm: 3, lg: 4 }, maxWidth: 1500, mx: 'auto' }}><PageHeader icon={<MonitorHeartOutlined />} title="System Health & Audit" subtitle="Monitor service health and recent administrator activity." actions={<Stack alignItems={{ xs: 'flex-start', md: 'flex-end' }} spacing={1}><Typography variant="caption" color="text.secondary">Updates every 5 seconds while this tab is visible. {healthQuery.dataUpdatedAt ? `Last updated: ${new Date(healthQuery.dataUpdatedAt).toLocaleTimeString('en-FJ')}` : 'Loading health status…'}</Typography><Button variant="outlined" disabled={healthQuery.isFetching || auditQuery.isFetching} onClick={() => { void healthQuery.refetch(); void auditQuery.refetch() }}>Refresh now</Button></Stack>} />{healthQuery.isError && <Alert severity="warning" sx={{ mb: 2 }}>Health refresh failed. Any values shown are from the last successful update.</Alert>}{auditQuery.isError && <Alert severity="warning" sx={{ mb: 2 }}>Administrator activity could not be refreshed.</Alert>}{health && !healthQuery.isError && <Alert severity={health.status === 'Healthy' ? 'success' : 'warning'} sx={{ mb: 2 }}>{health.status === 'Healthy' ? 'CREMS is operating normally.' : 'One or more services need attention.'}</Alert>}{health && <Grid container spacing={2} mb={3}>{Object.entries({ Database: health.databaseConnected ? 'Connected' : 'Unavailable', 'Database updates waiting': health.pendingMigrations.length, 'Messages waiting': health.queuedNotifications, 'Failed messages': health.failedNotifications, Email: health.emailProviderConfigured ? 'Ready' : 'Not configured', Environment: health.environment, Version: health.version }).map(([label, value]) => <Grid key={label} size={{ xs: 12, sm: 6, md: 3 }}><Card variant="outlined"><CardContent><Typography color="text.secondary">{label}</Typography><Typography fontWeight={800}>{String(value)}</Typography></CardContent></Card></Grid>)}</Grid>}
      <Card variant="outlined"><CardContent><Typography variant="h6" fontWeight={800} mb={2}>Administrator activity</Typography><TableContainer sx={{ overflowX: 'auto' }}><Table size="small"><TableHead><TableRow><TableCell>DATE</TableCell><TableCell>Administrator</TableCell><TableCell>Action</TableCell><TableCell>Details</TableCell></TableRow></TableHead><TableBody>{audit.map(item => <TableRow key={item.id}><TableCell>{new Date(item.occurredAt).toLocaleString('en-FJ')}</TableCell><TableCell>{item.userName}</TableCell><TableCell>{item.action}</TableCell><TableCell>{item.summary}</TableCell></TableRow>)}</TableBody></Table></TableContainer></CardContent></Card></Box>}
  </Box>
}
