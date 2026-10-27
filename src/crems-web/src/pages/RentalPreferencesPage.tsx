import { useEffect, useState } from 'react'
import SearchOutlined from '@mui/icons-material/SearchOutlined'
import AddOutlined from '@mui/icons-material/AddOutlined'
import CheckCircleOutlined from '@mui/icons-material/CheckCircleOutlined'
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
  return <Box>
    {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
    {notice && <Alert severity="success" sx={{ mb: 2 }}>{notice}</Alert>}
    <Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" alignItems={{ sm: 'center' }} gap={2} mb={3}>
      <TextField size="small" placeholder="Explore divisions and categories" value={query} onChange={event => setQuery(event.target.value)} inputProps={{ 'aria-label': 'Search divisions or asset categories' }} InputProps={{ startAdornment: <InputAdornment position="start"><SearchOutlined /></InputAdornment> }} sx={{ width: { xs: '100%', sm: 420 }, '& .MuiOutlinedInput-root': { bgcolor: 'white' } }} />
      <FormControlLabel sx={{ m: 0 }} disabled={saving} control={<Checkbox checked={!values.length} onChange={() => { setValues([]); setNotice('') }} />} label={<Typography variant="body2" fontWeight={650}>Show all rentals</Typography>} />
    </Stack>
    <Grid container spacing={2.5}>
      {shown.map(division => {
        const ids = division.categories.map(category => category.id)
        const count = ids.filter(id => values.includes(id)).length
        const categories = division.name.toLowerCase().includes(normalized) ? division.categories : division.categories.filter(category => category.name.toLowerCase().includes(normalized))
        return <Grid key={division.id} size={{ xs: 12, lg: 6 }}>
          <Card component="section" variant="outlined" sx={{ p: { xs: 2, sm: 2.5 }, height: '100%', borderRadius: 2.5, borderColor: count ? '#c4b800' : 'divider', boxShadow: count ? '0 5px 18px rgba(105,94,0,.07)' : '0 2px 8px rgba(0,0,0,.025)' }}>
            <Stack direction="row" alignItems="center" gap={1.5} mb={2}>
              <Box aria-hidden sx={{ width: 43, height: 43, flexShrink: 0, display: 'grid', placeItems: 'center', bgcolor: count ? 'secondary.main' : '#f0f0e9', borderRadius: 1.5, fontSize: 15, fontWeight: 850 }}>{division.name.split(' ').filter(Boolean).map(word => word[0]).join('').slice(0, 2)}</Box>
              <Box sx={{ flex: 1, minWidth: 0 }}><Typography component="h2" fontSize={18} fontWeight={800} sx={{ overflowWrap: 'anywhere' }}>{division.name}</Typography><Typography variant="caption" color="text.secondary">{ids.length ? count ? count + ' selected' : ids.length + ' rental categories' : 'Coming soon'}</Typography></Box>
              <Checkbox disabled={saving || !ids.length} checked={!!ids.length && count === ids.length} indeterminate={count > 0 && count < ids.length} onChange={event => select(ids, event.target.checked)} inputProps={{ 'aria-label': 'Select all categories in ' + division.name }} />
            </Stack>
            <Stack direction="row" gap={1} flexWrap="wrap">{categories.map(category => <FormControlLabel key={category.id} disabled={saving} sx={{ m: 0, pl: .5, pr: 1.5, minHeight: 39, maxWidth: '100%', borderRadius: 99, border: '1px solid', borderColor: values.includes(category.id) ? '#c2b400' : '#deded5', bgcolor: values.includes(category.id) ? '#fffbd6' : '#fafaf7', '&:hover': { bgcolor: '#fff7b3' }, '&:focus-within': { outline: '2px solid #111', outlineOffset: 2 } }} control={<Checkbox size="small" icon={<AddOutlined fontSize="small" />} checkedIcon={<CheckCircleOutlined fontSize="small" />} checked={values.includes(category.id)} onChange={event => select([category.id], event.target.checked)} />} label={<Typography variant="body2" sx={{ overflowWrap: 'anywhere' }} fontWeight={values.includes(category.id) ? 700 : 500}>{category.name}</Typography>} />)}</Stack>
            {!ids.length && <Typography variant="body2" color="text.secondary">Rentals will appear when categories are available.</Typography>}
          </Card>
        </Grid>
      })}
      {!shown.length && <Grid size={12}><Typography color="text.secondary">{query ? 'No divisions or categories match your search.' : 'No divisions are currently published.'}</Typography></Grid>}
    </Grid>
    <Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" alignItems={{ sm: 'center' }} gap={2} sx={{ mt: 3, p: 2.5, bgcolor: 'white', border: '1px solid', borderColor: 'divider', borderRadius: 2 }}>
      <Box><Typography variant="body2" fontWeight={750}>{values.length ? values.length + (values.length === 1 ? ' category selected' : ' categories selected') : 'Your homepage includes all rentals'}</Typography>{dirty && <Typography variant="caption" color="text.secondary">Unsaved changes</Typography>}</Box>
      <Stack direction="row" gap={1} flexWrap="wrap"><Button variant="outlined" disabled={saving || dirty} onClick={onBrowse}>View homepage</Button><Button variant="contained" color="secondary" disabled={saving || !dirty || !!error && !divisions.length} onClick={() => void save()}>{saving ? 'Saving…' : 'Save preferences'}</Button></Stack>
    </Stack>
  </Box>
}
