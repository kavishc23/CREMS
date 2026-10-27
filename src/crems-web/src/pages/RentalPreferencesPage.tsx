import { useEffect, useState } from 'react'
import SearchOutlined from '@mui/icons-material/SearchOutlined'
import { Alert, Box, Button, Card, Checkbox, CircularProgress, FormControlLabel, Grid, InputAdornment, Stack, TextField, Typography } from '@mui/material'
import { api } from '../api/client'
import { apiErrorMessage } from '../api/errors'

type Division = { id: string; name: string; description: string | null; categories: { id: string; name: string }[] }
type Preferences = { divisions: Division[]; preferredAssetCategoryIds: string[] }

export function RentalPreferencesPage({ onBrowse }: { onBrowse: () => void }) {
  const [divisions, setDivisions] = useState<Division[]>([])
  const [values, setValues] = useState<string[]>([])
  const [original, setOriginal] = useState<string[]>([])
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [query, setQuery] = useState('')
  useEffect(() => {
    let active = true
    async function load() {
      try {
        const { data } = await api.get<Preferences>('/customer-account/rental-preferences')
        if (!active) return
        setDivisions(data.divisions)
        setValues(data.preferredAssetCategoryIds)
        setOriginal(data.preferredAssetCategoryIds)
      } catch (reason) { if (active) setError(apiErrorMessage(reason, 'Unable to load rental preferences. Please reopen this page to try again.')) }
      finally { if (active) setLoading(false) }
    }
    void load()
    // Refresh configuration without replacing the customer's unsaved selections.
    const refreshCatalogue = () => {
      if (document.hidden) return
      void api.get<Preferences>('/customer-account/rental-preferences').then(({ data }) => {
        if (active) setDivisions(data.divisions)
      }).catch(() => undefined)
    }
    window.addEventListener('focus', refreshCatalogue)
    const timer = window.setInterval(refreshCatalogue, 60_000)
    return () => { active = false; window.removeEventListener('focus', refreshCatalogue); window.clearInterval(timer) }
  }, [])
  const dirty = [...values].sort().join() !== [...original].sort().join()
  const select = (ids: string[], checked: boolean) => { setNotice(''); setValues(current => checked ? [...new Set([...current, ...ids])] : current.filter(id => !ids.includes(id))) }
  async function save() {
    setSaving(true); setError(''); setNotice('')
    try {
      const { data } = await api.put<{ preferredAssetCategoryIds: string[] }>('/customer-account/rental-preferences', { assetCategoryIds: values })
      setValues(data.preferredAssetCategoryIds); setOriginal(data.preferredAssetCategoryIds)
      setNotice('Your rental preferences have been saved.')
    } catch (reason) { setError(apiErrorMessage(reason, 'Unable to save your rental preferences. Please try again.')) }
    finally { setSaving(false) }
  }
  const normalized = query.trim().toLowerCase()
  const shown = divisions.filter(division => division.name.toLowerCase().includes(normalized) || division.categories.some(category => category.name.toLowerCase().includes(normalized)))
  if (loading) return <Box sx={{ py: 8, textAlign: 'center' }}><CircularProgress aria-label="Loading rental preferences" /></Box>
  return <Box sx={{ maxWidth: 1320, mx: 'auto' }}>
    {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
    {notice && <Alert severity="success" sx={{ mb: 2 }}>{notice}</Alert>}
    <Card variant="outlined" sx={{ borderRadius: 3 }}>
      <Box sx={{ p: { xs: 2.5, md: 3 } }}>
        <Typography color="text.secondary">Choose the rentals you want on your homepage. Leave everything clear to see all rentals.</Typography>
        <Stack direction={{ xs: 'column', sm: 'row' }} gap={2} alignItems={{ sm: 'center' }} justifyContent="space-between" mt={2.5}>
          <TextField size="small" placeholder="Find a division or category" value={query} onChange={event => setQuery(event.target.value)} inputProps={{ 'aria-label': 'Search divisions or asset categories' }} InputProps={{ startAdornment: <InputAdornment position="start"><SearchOutlined /></InputAdornment> }} sx={{ width: { xs: '100%', sm: 380 } }} />
          <FormControlLabel sx={{ mr: 0 }} disabled={saving} control={<Checkbox checked={!values.length} onChange={() => { setValues([]); setNotice('') }} />} label="Show all rentals" />
        </Stack>
      </Box>
      <Box sx={{ borderTop: '1px solid', borderColor: 'divider' }}>
        {shown.map(division => {
          const ids = division.categories.map(category => category.id)
          const count = ids.filter(id => values.includes(id)).length
          const categories = division.name.toLowerCase().includes(normalized) ? division.categories : division.categories.filter(category => category.name.toLowerCase().includes(normalized))
          return <Box key={division.id} component="section" sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'minmax(220px, .9fr) minmax(0, 2fr)' }, gap: { xs: 1, md: 3 }, px: { xs: 2, md: 3 }, py: 2, borderBottom: '1px solid', borderColor: 'divider' }}>
            <FormControlLabel sx={{ m: 0, alignSelf: 'start', '& .MuiFormControlLabel-label': { overflowWrap: 'anywhere' } }} disabled={saving || !ids.length} control={<Checkbox checked={!!ids.length && count === ids.length} indeterminate={count > 0 && count < ids.length} onChange={event => select(ids, event.target.checked)} inputProps={{ 'aria-label': 'Select all categories in ' + division.name }} />} label={<Typography fontWeight={800}>{division.name}</Typography>} />
            <Box><Grid container spacing={.5}>{categories.map(category => <Grid key={category.id} size={{ xs: 12, sm: 6, lg: 4 }}><FormControlLabel disabled={saving} sx={{ m: 0, width: '100%', borderRadius: 1.5, bgcolor: values.includes(category.id) ? '#fffbe0' : 'transparent', '&:hover': { bgcolor: '#f5f5ef' }, '& .MuiFormControlLabel-label': { overflowWrap: 'anywhere' } }} control={<Checkbox size="small" checked={values.includes(category.id)} onChange={event => select([category.id], event.target.checked)} />} label={<Typography variant="body2" fontWeight={600}>{category.name}</Typography>} /></Grid>)}</Grid>{!ids.length && <Typography variant="body2" color="text.secondary" sx={{ py: 1 }}>No rental categories yet.</Typography>}</Box>
          </Box>
        })}
        {!shown.length && <Typography color="text.secondary" sx={{ p: 3 }}>{query ? 'No divisions or categories match your search.' : 'No divisions are currently published.'}</Typography>}
      </Box>
      <Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" alignItems={{ sm: 'center' }} gap={2} sx={{ px: { xs: 2.5, md: 3 }, py: 2.5 }}>
        <Box><Typography variant="body2" fontWeight={650}>{values.length ? values.length + (values.length === 1 ? ' category selected' : ' categories selected') : 'All rentals selected'}</Typography>{dirty && <Typography variant="caption" color="text.secondary">Unsaved changes</Typography>}</Box>
        <Stack direction="row" gap={1} flexWrap="wrap"><Button variant="outlined" disabled={saving || dirty} onClick={onBrowse}>View homepage</Button><Button variant="contained" disabled={saving || !dirty || !!error && !divisions.length} onClick={() => void save()}>{saving ? 'Saving…' : 'Save preferences'}</Button></Stack>
      </Stack>
    </Card>
  </Box>
}
