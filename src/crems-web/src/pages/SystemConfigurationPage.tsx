import { useEffect, useState } from 'react'
import SettingsOutlined from '@mui/icons-material/SettingsOutlined'
import { Accordion, AccordionDetails, AccordionSummary, Alert, Box, Button, Card, CardContent, Grid, Stack, Tab, Table, TableBody, TableCell, TableHead, TableRow, Tabs, TextField, Typography } from '@mui/material'
import ExpandMoreOutlined from '@mui/icons-material/ExpandMoreOutlined'
import { api } from '../api/client'
import { PageHeader } from '../components/PageHeader'
import { ChargeDefinitionsPage } from './ChargeDefinitionsPage'
import { BookingApprovalRulesPage } from './BookingApprovalRulesPage'

type Setting = { id: string; key: string; value: string; category: string; description: string; isSecret: boolean }
type Template = { id: string; key: string; name: string; channel: string; subject: string; body: string; isActive: boolean }
type Health = { status: string; databaseConnected: boolean; pendingMigrations: string[]; queuedNotifications: number; failedNotifications: number; environment: string; version: string; emailProviderConfigured: boolean; smsProviderConfigured: boolean }
type Audit = { id: string; userName: string; action: string; summary: string; occurredAt: string }

const settingNames: Record<string, string> = {
  'rentals.vatRate': 'VAT rate',
  'rentals.defaultDeposit': 'Default refundable bond',
  'rentals.defaultCurrency': 'Currency',
  'bookings.holdMinutes': 'Reservation hold time (minutes)',
  'notifications.emailFrom': 'Sender email address',
}

export function SystemConfigurationPage() {
  const [tab, setTab] = useState(0)
  const [settings, setSettings] = useState<Setting[]>([])
  const [templates, setTemplates] = useState<Template[]>([])
  const [health, setHealth] = useState<Health | null>(null)
  const [audit, setAudit] = useState<Audit[]>([])
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')

  async function load() {
    try {
      const [settingResponse, templateResponse, healthResponse, auditResponse] = await Promise.all([
        api.get<Setting[]>('/administration/settings'), api.get<Template[]>('/administration/templates'),
        api.get<Health>('/administration/health'), api.get<Audit[]>('/administration/audit?take=100'),
      ])
      setSettings(settingResponse.data.filter(item => item.key !== 'rentals.defaultDeposit' && !item.key.startsWith('rentals.fijiPricingDefaults.'))); setTemplates(templateResponse.data); setHealth(healthResponse.data); setAudit(auditResponse.data); setError('')
    } catch { setError('System configuration could not be loaded.') }
  }
  useEffect(() => { void load() }, [])

  async function saveSetting(item: Setting) { setSaving(true); try { await api.put(`/administration/settings/${encodeURIComponent(item.key)}`, { value: item.value }); await load() } catch { setError('This setting could not be saved.') } finally { setSaving(false) } }
  async function saveTemplate(item: Template) { setSaving(true); try { await api.put(`/administration/templates/${item.id}`, item); await load() } catch { setError('This message template could not be saved.') } finally { setSaving(false) } }

  return <Box><Box sx={{ px: { xs: 2, sm: 3, lg: 4 }, pt: 3, pb: 2 }}><PageHeader icon={<SettingsOutlined />} title="System configuration" subtitle="Manage operating defaults, rates, communications and technical controls." /></Box>
    {error && <Alert severity="error" sx={{ mx: { xs: 2, sm: 3, lg: 4 }, mb: 2 }}>{error}</Alert>}
    <Tabs value={tab} onChange={(_, value) => setTab(value)} variant="scrollable" sx={{ px: { xs: 2, sm: 3, lg: 4 }, borderBottom: 1, borderColor: 'divider' }}><Tab label="Overview" /><Tab label="Rental defaults" /><Tab label="Rates & charges" /><Tab label="Notifications" /><Tab label="System health & audit" /><Tab label="Approval rules" /></Tabs>
    {tab === 0 && <Box sx={{ p: { xs: 2, sm: 3, lg: 4 } }}><Grid container spacing={2}>{[
      ['Rental defaults', 'Tax, currency, refundable bonds and booking hold rules.'], ['Rates & charges', 'Reusable customer rates and internal cost definitions.'], ['Notifications', 'Customer email wording and delivery configuration.'], ['System health', 'Database, messaging, version and administrator activity.'], ['Approval rules', 'Route higher-risk bookings through sequential approval stages.'],
    ].map(([title, detail], index) => <Grid key={title} size={{ xs: 12, md: 6 }}><Card variant="outlined" sx={{ height: '100%', cursor: 'pointer' }} onClick={() => setTab(index + 1)}><CardContent><Typography variant="h6" fontWeight={800}>{title}</Typography><Typography color="text.secondary" mt={.5}>{detail}</Typography><Button sx={{ mt: 2 }}>Open</Button></CardContent></Card></Grid>)}</Grid></Box>}
    {tab === 1 && <Box sx={{ p: { xs: 2, sm: 3, lg: 4 }, maxWidth: 1100 }}><Alert severity="info" sx={{ mb: 2 }}>Refundable bonds are configured in Organization → Divisions &amp; services, with individual overrides in Asset register → Edit asset. The retired global bond setting is no longer used.</Alert><Typography variant="h5" fontWeight={800}>Rental defaults</Typography><Typography color="text.secondary" mt={.5} mb={3}>Saving VAT here applies it to all divisions for new customer requests. Existing bookings and invoices keep their agreed rates.</Typography><Stack spacing={2}>{settings.map((item, index) => <Card key={item.id} variant="outlined"><CardContent><Stack direction={{ xs: 'column', sm: 'row' }} gap={2} alignItems={{ sm: 'center' }}><Box sx={{ flex: 1 }}><Typography fontWeight={700}>{settingNames[item.key] ?? item.description ?? item.key}</Typography><Typography variant="body2" color="text.secondary">{item.key === 'rentals.vatRate' ? 'Applies to all divisions. Set a division-specific rate afterwards in Organization if required.' : item.description}</Typography></Box><TextField size="small" label="Value" type={item.isSecret ? 'password' : 'text'} value={item.value} onChange={event => setSettings(current => current.map((entry, i) => i === index ? { ...entry, value: event.target.value } : entry))} /><Button variant="outlined" disabled={saving} onClick={() => void saveSetting(item)}>{item.key === 'rentals.vatRate' ? 'Save & apply to all divisions' : 'Save'}</Button></Stack></CardContent></Card>)}</Stack></Box>}
    {tab === 2 && <ChargeDefinitionsPage />}
    {tab === 5 && <BookingApprovalRulesPage />}
    {tab === 3 && <Box sx={{ p: { xs: 2, sm: 3, lg: 4 }, maxWidth: 1200 }}><Typography variant="h5" fontWeight={800}>Customer notifications</Typography><Typography color="text.secondary" mt={.5} mb={3}>Maintain approved wording used for confirmations, reminders and other automated messages.</Typography><Stack spacing={2}>{templates.map((item, index) => <Card key={item.id} variant="outlined"><CardContent><Grid container spacing={2}><Grid size={{ xs: 12, md: 4 }}><TextField fullWidth label="Message name" value={item.name} onChange={event => setTemplates(current => current.map((entry, i) => i === index ? { ...entry, name: event.target.value } : entry))} /></Grid><Grid size={{ xs: 12, md: 2 }}><TextField fullWidth label="Channel" value={item.channel} onChange={event => setTemplates(current => current.map((entry, i) => i === index ? { ...entry, channel: event.target.value } : entry))} /></Grid><Grid size={{ xs: 12, md: 6 }}><TextField fullWidth label="Subject" value={item.subject} onChange={event => setTemplates(current => current.map((entry, i) => i === index ? { ...entry, subject: event.target.value } : entry))} /></Grid><Grid size={12}><TextField fullWidth multiline minRows={3} label="Message" value={item.body} onChange={event => setTemplates(current => current.map((entry, i) => i === index ? { ...entry, body: event.target.value } : entry))} /></Grid></Grid><Button sx={{ mt: 2 }} variant="contained" disabled={saving} onClick={() => void saveTemplate(item)}>Save template</Button></CardContent></Card>)}</Stack></Box>}
    {tab === 4 && <Box sx={{ p: { xs: 2, sm: 3, lg: 4 }, maxWidth: 1250 }}><Alert severity={health?.status === 'Healthy' ? 'success' : 'warning'} sx={{ mb: 2 }}>{health?.status === 'Healthy' ? 'CREMS is operating normally.' : 'One or more services need attention.'}</Alert>{health && <Grid container spacing={2} mb={3}>{Object.entries({ Database: health.databaseConnected ? 'Connected' : 'Unavailable', 'Database updates waiting': health.pendingMigrations.length, 'Messages waiting': health.queuedNotifications, 'Failed messages': health.failedNotifications, Email: health.emailProviderConfigured ? 'Ready' : 'Not configured', Environment: health.environment, Version: health.version }).map(([label, value]) => <Grid key={label} size={{ xs: 12, sm: 6, md: 3 }}><Card variant="outlined"><CardContent><Typography color="text.secondary">{label}</Typography><Typography fontWeight={800}>{String(value)}</Typography></CardContent></Card></Grid>)}</Grid>}
      <Accordion><AccordionSummary expandIcon={<ExpandMoreOutlined />}><Box><Typography fontWeight={800}>Administrator activity</Typography><Typography variant="body2" color="text.secondary">Recent sensitive configuration and access events.</Typography></Box></AccordionSummary><AccordionDetails sx={{ overflowX: 'auto' }}><Table size="small"><TableHead><TableRow><TableCell>When</TableCell><TableCell>Administrator</TableCell><TableCell>Action</TableCell><TableCell>Details</TableCell></TableRow></TableHead><TableBody>{audit.map(item => <TableRow key={item.id}><TableCell>{new Date(item.occurredAt).toLocaleString('en-FJ')}</TableCell><TableCell>{item.userName}</TableCell><TableCell>{item.action}</TableCell><TableCell>{item.summary}</TableCell></TableRow>)}</TableBody></Table></AccordionDetails></Accordion></Box>}
  </Box>
}
