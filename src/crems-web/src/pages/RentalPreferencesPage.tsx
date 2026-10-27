import { useEffect, useState } from 'react'
import SearchOutlined from '@mui/icons-material/SearchOutlined'
import TuneOutlined from '@mui/icons-material/TuneOutlined'
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
    <Card variant="outlined" sx={{ borderRadius: 3, overflow: 'hidden', boxShadow: '0 8px 32px rgba(20,20,10,.04)' }}>
      <Box sx={{ px: { xs: 2.5, md: 3 }, py: 2.75, bgcolor: '#141414', color: 'white', borderBottom: '4px solid', borderColor: 'secondary.main' }}>
        <Stack direction="row" gap={2} alignItems="center" justifyContent="space-between" flexWrap="wrap">
          <Stack direction="row" gap={1.75} alignItems="center"><Box sx={{ width: 46, height: 46, flexShrink: 0, display: 'grid', placeItems: 'center', borderRadius: 1.5, bgcolor: 'secondary.main', color: '#111' }}><TuneOutlined /></Box><Box><Typography variant="h6" fontWeight={800}>Make it your catalogue</Typography><Typography variant="body2" sx={{ mt: .5, color: '#d1d1c9' }}>Choose a division, then select the rentals you use.</Typography></Box></Stack>
          <Box sx={{ px: 1.5, py: .75, borderRadius: 1.25, border: '1px solid #555548', color: '#ffed00', fontSize: 12, fontWeight: 750 }}>{values.length ? values.length + ' selected' : 'All rentals'}</Box>
        </Stack>
      </Box>
      <Stack direction={{ xs: 'column', sm: 'row' }} gap={2} alignItems={{ sm: 'center' }} justifyContent="space-between" sx={{ px: { xs: 2.5, md: 3 }, py: 2.25 }}>
        <TextField size="small" placeholder="Find a division or category" value={query} onChange={event => setQuery(event.target.value)} inputProps={{ 'aria-label': 'Search divisions or asset categories' }} InputProps={{ startAdornment: <InputAdornment position="start"><SearchOutlined /></InputAdornment> }} sx={{ width: { xs: '100%', sm: 410 }, '& .MuiOutlinedInput-root': { bgcolor: '#fafaf7', borderRadius: 1.5 } }} />
        <FormControlLabel sx={{ m: 0, px: 1.25, py: .25, border: '1px solid', borderColor: values.length ? 'divider' : '#bcae00', bgcolor: values.length ? 'white' : '#fffde5', borderRadius: 1.5, '& .MuiFormControlLabel-label': { fontSize: 14, fontWeight: 650 } }} disabled={saving} control={<Checkbox size="small" checked={!values.length} onChange={() => { setValues([]); setNotice('') }} />} label="Show all rentals" />
      </Stack>
      <Box sx={{ display: 'grid', gap: 1.5, p: { xs: 1.5, md: 2.5 }, bgcolor: '#f5f5f0', borderBlock: '1px solid', borderColor: 'divider' }}>
        {shown.map(division => {
          const ids = division.categories.map(category => category.id)
          const count = ids.filter(id => values.includes(id)).length
          const categories = division.name.toLowerCase().includes(normalized) ? division.categories : division.categories.filter(category => category.name.toLowerCase().includes(normalized))
          return <Box key={division.id} component="section" sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'minmax(230px, .85fr) minmax(0, 2fr)' }, border: '1px solid', borderColor: count ? '#d7cd6c' : '#deded7', borderRadius: 2, overflow: 'hidden', bgcolor: 'white', boxShadow: '0 2px 5px rgba(0,0,0,.025)' }}>
            <Box sx={{ bgcolor: '#22231f', color: 'white', borderLeft: '4px solid', borderColor: count ? 'secondary.main' : '#66685b', px: 1.5, py: 2, display: 'flex', alignItems: 'center' }}>
              <FormControlLabel sx={{ m: 0, width: '100%', gap: .75, alignItems: 'flex-start', '& .MuiFormControlLabel-label': { flex: 1, minWidth: 0, overflowWrap: 'anywhere' }, '& .MuiFormControlLabel-label.Mui-disabled': { color: '#b0b1a9' } }} disabled={saving || !ids.length} control={<Checkbox sx={{ mt: .25, color: '#a8aa9e', '&.Mui-checked, &.MuiCheckbox-indeterminate': { color: '#ffed00' }, '&.Mui-disabled': { color: '#717367' } }} checked={!!ids.length && count === ids.length} indeterminate={count > 0 && count < ids.length} onChange={event => select(ids, event.target.checked)} inputProps={{ 'aria-label': 'Select all categories in ' + division.name }} />} label={<Box><Typography component="span" sx={{ display: 'block', mb: .5, color: '#cecfb9', fontSize: 10, fontWeight: 750, letterSpacing: '1.5px', textTransform: 'uppercase' }}>Division</Typography><Typography component="h3" sx={{ m: 0, color: 'inherit', fontSize: 17, fontWeight: 800, lineHeight: 1.4 }}>{division.name}</Typography><Typography component="span" sx={{ display: 'block', mt: .75, color: '#bcbeb2', fontSize: 11 }}>{ids.length ? count + ' of ' + ids.length + ' selected' : 'Coming soon'}</Typography></Box>} />
            </Box>
            <Box sx={{ p: { xs: 2, md: 2.25 }, alignSelf: 'center', minWidth: 0 }}><Typography variant="caption" sx={{ display: 'block', mb: 1.25, color: 'text.secondary', fontSize: 10, fontWeight: 750, textTransform: 'uppercase', letterSpacing: '1px' }}>Asset categories</Typography><Grid container spacing={1}>{categories.map(category => <Grid key={category.id} size={{ xs: 12, sm: 6, lg: 4 }} sx={{ display: 'flex' }}><FormControlLabel disabled={saving} sx={{ m: 0, px: .5, py: .25, width: '100%', minHeight: 44, border: '1px solid', borderColor: values.includes(category.id) ? '#b9a900' : '#deded7', borderRadius: 1.5, bgcolor: values.includes(category.id) ? '#fffbd9' : '#fff', transition: 'background-color .15s, border-color .15s', '&:hover': { borderColor: '#8e8870', bgcolor: values.includes(category.id) ? '#fff5ae' : '#f8f8f3' }, '&:focus-within': { outline: '2px solid #111', outlineOffset: 2 }, '& .MuiFormControlLabel-label': { minWidth: 0, overflowWrap: 'anywhere' } }} control={<Checkbox size="small" checked={values.includes(category.id)} onChange={event => select([category.id], event.target.checked)} />} label={<Typography variant="body2" fontWeight={values.includes(category.id) ? 700 : 500}>{category.name}</Typography>} /></Grid>)}</Grid>{!ids.length && <Typography variant="body2" color="text.secondary">No rental categories yet.</Typography>}</Box>
          </Box>
        })}
        {!shown.length && <Typography color="text.secondary" sx={{ p: 3 }}>{query ? 'No divisions or categories match your search.' : 'No divisions are currently published.'}</Typography>}
      </Box>
      <Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" alignItems={{ sm: 'center' }} gap={2} sx={{ px: { xs: 2.5, md: 3 }, py: 2.5 }}>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.25 }}><Box sx={{ width: 9, height: 9, bgcolor: 'secondary.main', border: '1px solid #b9a900', borderRadius: '50%', flexShrink: 0 }} /><Box><Typography variant="body2" fontWeight={750}>{values.length ? values.length + (values.length === 1 ? ' category selected' : ' categories selected') : 'All rentals selected'}</Typography>{dirty && <Typography variant="caption" color="text.secondary">Unsaved changes</Typography>}</Box></Box>
        <Stack direction="row" gap={1} flexWrap="wrap"><Button variant="outlined" disabled={saving || dirty} onClick={onBrowse} sx={{ minHeight: 44 }}>View homepage</Button><Button variant="contained" disabled={saving || !dirty || !!error && !divisions.length} onClick={() => void save()} sx={{ minHeight: 44, px: 2.5, bgcolor: 'secondary.main', color: '#111', '&:hover': { bgcolor: 'secondary.dark' } }}>{saving ? 'Saving…' : 'Save preferences'}</Button></Stack>
      </Stack>
    </Card>
  </Box>
}
