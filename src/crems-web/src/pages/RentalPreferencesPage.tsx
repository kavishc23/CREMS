import { useEffect, useState } from 'react'
import SearchOutlined from '@mui/icons-material/SearchOutlined'
import TuneOutlined from '@mui/icons-material/TuneOutlined'
import { Alert, Box, Button, Card, Checkbox, Chip, CircularProgress, FormControlLabel, Grid, InputAdornment, Stack, TextField, Typography } from '@mui/material'
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
      setNotice('Preferences saved. Your homepage will show rentals from your selected categories.')
    } catch (reason) { setError(apiErrorMessage(reason, 'Unable to save your rental preferences. Please try again.')) }
    finally { setSaving(false) }
  }
  const normalized = query.trim().toLowerCase()
  const shown = divisions.filter(division => division.name.toLowerCase().includes(normalized) || division.categories.some(category => category.name.toLowerCase().includes(normalized)))
  if (loading) return <Box sx={{ py: 8, textAlign: 'center' }}><CircularProgress aria-label="Loading rental preferences" /></Box>
  return <Box sx={{ maxWidth: 1120, mx: 'auto' }}>
    {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
    {notice && <Alert severity="success" sx={{ mb: 2 }}>{notice}</Alert>}
    <Card variant="outlined" sx={{ borderRadius: 3, overflow: 'hidden' }}>
      <Box sx={{ p: { xs: 2.5, md: 3.5 }, background: 'linear-gradient(120deg, #fff 50%, #fffbe4)' }}>
        <Stack direction="row" justifyContent="space-between" alignItems="flex-start" gap={2}>
          <Box><Stack direction="row" alignItems="center" gap={1}><TuneOutlined /><Typography variant="overline" fontWeight={800}>Personalise your homepage</Typography></Stack><Typography variant="h5" fontWeight={850} mt={1}>Choose what you hire</Typography></Box>
          <Chip label={values.length ? `${values.length} selected` : 'All rentals'} sx={{ bgcolor: 'secondary.main', fontWeight: 800 }} />
        </Stack>
        <Typography color="text.secondary" mt={1}>Select asset categories under each division to choose which rentals appear on your homepage. Your searches will continue to include all rentals.</Typography>
        <Stack direction={{ xs: 'column', sm: 'row' }} gap={2} justifyContent="space-between" mt={3}>
          <TextField size="small" placeholder="Search divisions or asset categories" value={query} onChange={event => setQuery(event.target.value)} inputProps={{ 'aria-label': 'Search divisions or asset categories' }} InputProps={{ startAdornment: <InputAdornment position="start"><SearchOutlined /></InputAdornment> }} sx={{ width: { xs: '100%', sm: 420 }, bgcolor: 'white' }} />
          <Button disabled={saving || !values.length} onClick={() => { setValues([]); setNotice('') }}>Clear selections</Button>
        </Stack>
      </Box>
      <Box sx={{ p: { xs: 2.5, md: 3.5 }, borderTop: '1px solid', borderColor: 'divider' }}>
        <Box sx={{ border: '1px solid', borderColor: values.length ? 'divider' : '#c7ae00', borderRadius: 2, px: 2, py: 1, mb: 2.5, bgcolor: values.length ? 'white' : '#fffdf0' }}><FormControlLabel disabled={saving} control={<Checkbox checked={!values.length} onChange={() => { setValues([]); setNotice('') }} />} label={<Box><Typography fontWeight={750}>Show all rentals</Typography><Typography variant="body2" color="text.secondary">Include every division and category when no preferences are selected.</Typography></Box>} /></Box>
        <Stack spacing={2}>
          {shown.map(division => {
            const ids = division.categories.map(category => category.id)
            const count = ids.filter(id => values.includes(id)).length
            const categories = division.name.toLowerCase().includes(normalized) ? division.categories : division.categories.filter(category => category.name.toLowerCase().includes(normalized))
            return <Box key={division.id} component="section" sx={{ border: '1px solid', borderColor: 'divider', borderRadius: 2.5, overflow: 'hidden' }}>
              <Stack direction="row" alignItems="center" justifyContent="space-between" gap={1} sx={{ bgcolor: '#f7f7f3', px: 2.5, py: 1.5 }}>
                <FormControlLabel sx={{ m: 0, gap: 1 }} disabled={saving || !ids.length} control={<Checkbox checked={!!ids.length && count === ids.length} indeterminate={count > 0 && count < ids.length} onChange={event => select(ids, event.target.checked)} inputProps={{ 'aria-label': `Select all categories in ${division.name}` }} />} label={<Box><Typography variant="overline" color="text.secondary" sx={{ fontSize: 10, letterSpacing: 1.5 }}>Division</Typography><Typography fontWeight={850} fontSize={18}>{division.name}</Typography></Box>} />
                <Typography variant="caption" color="text.secondary" sx={{ whiteSpace: 'nowrap' }}>{count} / {ids.length} selected</Typography>
              </Stack>
              <Box sx={{ px: 3, pt: 2, pb: 2.5 }}><Typography variant="overline" color="text.secondary" fontWeight={750} sx={{ fontSize: 10, letterSpacing: 1.3 }}>Asset category preferences</Typography><Grid container spacing={1} mt={.5}>{categories.map(category => <Grid key={category.id} size={{ xs: 12, sm: 6, md: 4 }}><FormControlLabel disabled={saving} sx={{ m: 0, width: '100%', borderRadius: 1.5, bgcolor: values.includes(category.id) ? '#fffbe0' : 'transparent', '&:hover': { bgcolor: '#f5f5ef' } }} control={<Checkbox size="small" checked={values.includes(category.id)} onChange={event => select([category.id], event.target.checked)} />} label={<Typography variant="body2" fontWeight={650}>{category.name}</Typography>} /></Grid>)}</Grid>{!ids.length && <Typography variant="body2" color="text.secondary" mt={1}>No asset categories are currently configured for this division.</Typography>}</Box>
            </Box>
          })}
          {!shown.length && <Typography color="text.secondary">{query ? 'No divisions or asset categories match your search.' : 'No divisions are currently published.'}</Typography>}
        </Stack>
      </Box>
      <Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" alignItems={{ sm: 'center' }} gap={2} sx={{ borderTop: '1px solid', borderColor: 'divider', px: { xs: 2.5, md: 3.5 }, py: 2.5 }}>
        <Box><Typography fontWeight={750} variant="body2">{values.length ? `${values.length} asset categories selected` : 'Your homepage includes all rentals'}</Typography><Typography variant="caption" color="text.secondary">{dirty ? 'You have unsaved changes.' : 'New divisions and categories appear here when configured.'}</Typography></Box>
        <Stack direction="row" gap={1}><Button variant="outlined" disabled={saving || dirty} onClick={onBrowse}>View homepage</Button><Button variant="contained" disabled={saving || !dirty || !!error && !divisions.length} onClick={() => void save()}>{saving ? 'Saving…' : 'Save preferences'}</Button></Stack>
      </Stack>
    </Card>
  </Box>
}
