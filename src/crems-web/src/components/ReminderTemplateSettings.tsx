import { useActionNotice } from './useActionNotice'
import { useEffect, useState } from 'react'
import { Alert, Box, Button, Card, CardContent, FormControlLabel, Grid, Stack, Switch, TextField, Typography } from '@mui/material'
import { api } from '../api/client'

type Template = { id: string; key: string; name: string; channel: string; subject: string; body: string; isActive: boolean }
type Timing = { key: string; value: string; description: string }
const schedule: Record<string, string> = {
  'reminder.quotationExpiry': 'Sent quotations expiring within 24 hours',
  'reminder.pickup': 'Confirmed pickups due within 24 hours',
  'reminder.overdueReturn': 'Overdue active hires, at most once per day for 14 days',
  'reminder.invoiceDue': 'Outstanding issued invoices older than 14 days',
  'reminder.maintenanceComplete': 'Released maintenance work affecting a confirmed booking',
  'reminder.documentExpiry': 'Customer documents expiring within 30 days',
}
export function ReminderTemplateSettings() {
  const [templates, setTemplates] = useState<Template[]>([])
  const [timings, setTimings] = useState<Timing[]>([])
  const [error, setError] = useState('')
  const setNotice = useActionNotice()
  const [saving, setSaving] = useState<string | null>(null)
  useEffect(() => { void Promise.all([api.get<Template[]>('/administration/templates'), api.get<Timing[]>('/administration/settings')]).then(([messages, settings]) => { setTemplates(messages.data.filter(item => item.key.startsWith('reminder.'))); setTimings(settings.data.filter(item => item.key.startsWith('reminders.'))) }).catch(() => setError('Reminder settings could not be loaded.')) }, [])
  function change(id: string, update: Partial<Template>) { setTemplates(current => current.map(item => item.id === id ? { ...item, ...update } : item)) }
  async function save(item: Template) {
    setSaving(item.id); setError(''); setNotice('')
    try { await api.put(`/administration/templates/${item.id}`, item); setNotice(`${item.name} saved.`) }
    catch { setError(`Could not save ${item.name}. Please try again.`) }
    finally { setSaving(null) }
  }
  async function saveTiming(item: Timing) {
    setSaving(item.key); setError(''); setNotice('')
    try { await api.put(`/administration/settings/${item.key}`, { value: item.value }); setNotice('Reminder timing saved.') }
    catch { setError('The timing must be a whole number from 1 to 365.') }
    finally { setSaving(null) }
  }
  return <Box sx={{ p: { xs: 2, sm: 3, lg: 4 }, maxWidth: 1300 }}>
    <Typography variant="h5" fontWeight={800}>Automatic customer email reminders</Typography>
    <Typography color="text.secondary" mb={2}>Turn each reminder on or off and edit the approved wording. Messages use the configured email provider and are queued only for eligible records.</Typography>
    <Alert severity="info" sx={{ mb: 2 }}>Available fields: {'{{bookingNumber}}'}, {'{{quoteNumber}}'}, {'{{invoiceNumber}}'}, {'{{branchName}}'}, {'{{dueAt}}'}, {'{{balanceDue}}'}, {'{{documentType}}'} and {'{{instructions}}'}. Only fields present in a reminder are replaced.</Alert>
    {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
    <Card variant="outlined" sx={{ mb: 3 }}><CardContent><Typography variant="h6" fontWeight={800} mb={2}>Reminder timing</Typography><Grid container spacing={2}>{timings.map(item => <Grid key={item.key} size={{ xs: 12, md: 6 }}><Stack direction="row" gap={1} alignItems="flex-start"><TextField fullWidth type="number" label={item.key.replace('reminders.', '').replace(/([A-Z])/g, ' $1')} helperText={item.description} value={item.value} inputProps={{ min: 1, max: 365, step: 1 }} onChange={event => setTimings(current => current.map(entry => entry.key === item.key ? { ...entry, value: event.target.value } : entry))} /><Button variant="outlined" disabled={saving !== null || !/^\d+$/.test(item.value) || Number(item.value) < 1 || Number(item.value) > 365} onClick={() => void saveTiming(item)}>Save</Button></Stack></Grid>)}</Grid></CardContent></Card>
    <Grid container spacing={2}>{templates.map(item => <Grid key={item.id} size={{ xs: 12, lg: 6 }}><Card variant="outlined" sx={{ height: '100%' }}><CardContent><Typography variant="h6" fontWeight={800}>{item.name}</Typography><Typography variant="body2" color="text.secondary" mb={2}>{schedule[item.key] ?? 'Configured automatic reminder'}</Typography><Stack spacing={2}><TextField fullWidth label="Email subject" value={item.subject} onChange={event => change(item.id, { subject: event.target.value })} inputProps={{ maxLength: 160 }} /><TextField fullWidth multiline minRows={3} label="Message" value={item.body} onChange={event => change(item.id, { body: event.target.value })} inputProps={{ maxLength: 2000 }} /><Stack direction="row" justifyContent="space-between" alignItems="center" gap={1}><FormControlLabel control={<Switch checked={item.isActive} onChange={event => change(item.id, { isActive: event.target.checked })} />} label={item.isActive ? 'Enabled' : 'Paused'} /><Button variant="contained" disabled={saving !== null || !item.subject.trim() || !item.body.trim()} onClick={() => void save(item)}>{saving === item.id ? 'Saving…' : 'Save reminder'}</Button></Stack></Stack></CardContent></Card></Grid>)}</Grid>
    {!templates.length && !error && <Alert severity="info">Reminder templates will appear after the API initializes its default settings.</Alert>}
  </Box>
}
