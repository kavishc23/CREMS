import { useState } from 'react'
import { Box, Checkbox, Chip, FormControlLabel, Grid, Stack, Tab, Tabs, Typography } from '@mui/material'
import { permissionLabel } from '../auth/staffAccess'

const groups = [
  { title: 'Staff & customer accounts', prefix: ['users.', 'customers.'], description: 'Account creation, recovery and portal access.' },
  { title: 'Assets', prefix: ['assets.'], description: 'Asset records, inspections and financial details.' },
  { title: 'Rentals, maintenance & finance', prefix: ['rentals.', 'maintenance.', 'payments.', 'reports.'], description: 'Operational approvals and financial actions.' },
  { title: 'Organization & configuration', prefix: ['divisions.', 'branches.', 'services.', 'asset_categories.', 'branch_calendar.', 'pricing.'], description: 'Operating structure and system configuration.' },
]
export function PermissionGroups({ permissions, selected, onChange, disabled = false }: { permissions: string[]; selected: string[]; onChange: (permission: string) => void; disabled?: boolean }) {
  const [category, setCategory] = useState('')
  const availableGroups = groups.filter(group => permissions.some(permission => group.prefix.some(prefix => permission.startsWith(prefix))))
  const activeCategory = availableGroups.some(group => group.title === category) ? category : availableGroups[0]?.title ?? ''
  return <Stack spacing={1.5}>
    <Tabs value={activeCategory || false} onChange={(_, value: string) => setCategory(value)} variant="scrollable" scrollButtons="auto" aria-label="Permission categories" sx={{ bgcolor: 'background.paper', border: 1, borderColor: 'divider', borderRadius: 1, '& .MuiTab-root': { minHeight: 46, minWidth: 0, fontSize: 12, px: 1.5 } }}>
      {availableGroups.map(group => <Tab key={group.title} value={group.title} label={group.title + ' (' + permissions.filter(permission => selected.includes(permission) && group.prefix.some(prefix => permission.startsWith(prefix))).length + ')'} />)}
    </Tabs>
    {availableGroups.filter(group => group.title === activeCategory).map(group => {
    const items = permissions.filter(permission => group.prefix.some(prefix => permission.startsWith(prefix)))
    if (!items.length) return null
    return <Box key={group.title} sx={{ border: 1, borderColor: 'divider', borderRadius: 1, overflow: 'hidden' }}>
      <Stack direction="row" justifyContent="space-between" alignItems="center" gap={1} sx={{ px: 2, py: 1.25, bgcolor: '#f6f6f3', borderBottom: 1, borderColor: 'divider' }}>
        <Box><Typography fontWeight={800} variant="body2">{group.title}</Typography><Typography variant="caption" color="text.secondary">{group.description}</Typography></Box>
        <Chip size="small" variant="outlined" label={items.filter(item => selected.includes(item)).length + ' / ' + items.length} />
      </Stack>
      <Grid container sx={{ p: 1 }}>{items.map(item => <Grid key={item} size={{ xs: 12, sm: 6 }}>
        <FormControlLabel sx={{ m: 0, width: '100%', px: .5, py: .25, borderRadius: 1, bgcolor: selected.includes(item) ? '#fffce0' : 'transparent', '& .MuiFormControlLabel-label': { fontSize: 13, lineHeight: 1.4 } }} control={<Checkbox size="small" disabled={disabled} checked={selected.includes(item)} onChange={() => onChange(item)} />} label={permissionLabel(item)} />
      </Grid>)}</Grid>
    </Box>
  })}</Stack>
}

