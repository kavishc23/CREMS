import { useEffect, useState } from 'react'
import AddOutlined from '@mui/icons-material/AddOutlined'
import EditOutlined from '@mui/icons-material/EditOutlined'
import ShieldOutlined from '@mui/icons-material/ShieldOutlined'
import { Alert, Box, Button, Card, CardContent, Checkbox, Chip, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, Grid, Stack, TextField, Typography } from '@mui/material'
import { api } from '../api/client'
import { apiErrorMessage } from '../api/errors'
import { formatRole } from '../auth/access'
import { PermissionGroups } from '../components/PermissionGroups'
import { type StaffRoleCatalog, type StaffRole } from '../auth/staffAccess'

export function StaffRolesPage({ onChanged }: { onChanged: () => void }) {
  const [catalog, setCatalog] = useState<StaffRoleCatalog | null>(null)
  const [error, setError] = useState('')
  const [open, setOpen] = useState(false)
  const [editing, setEditing] = useState(false)
  const [saving, setSaving] = useState(false)
  const [draft, setDraft] = useState({ name: '', modules: [] as string[], permissions: [] as string[] })
  const builtIn = editing && Boolean(catalog?.roles.find(role => role.name === draft.name)?.isBuiltIn)
  const readOnly = draft.name === 'SuperAdministrator'
  const editorPermissions = readOnly ? draft.permissions : builtIn ? (catalog?.builtInPermissions ?? []).filter(permission => draft.name === 'Administrator' || !['users.create', 'users.reset_password', 'users.manage_access', 'divisions.configure'].includes(permission)) : catalog?.permissions ?? []
  async function load() {
    try { setCatalog((await api.get<StaffRoleCatalog>('/staff-roles')).data) }
    catch (reason) { setError(apiErrorMessage(reason, 'Roles could not be loaded.')) }
  }
  useEffect(() => {
    const controller = new AbortController()
    void api.get<StaffRoleCatalog>('/staff-roles', { signal: controller.signal })
      .then(response => setCatalog(response.data))
      .catch(reason => { if (!controller.signal.aborted) setError(apiErrorMessage(reason, 'Roles could not be loaded.')) })
    return () => controller.abort()
  }, [])
  function edit(role?: StaffRole) {
    setEditing(Boolean(role)); setDraft(role ? { name: role.name, modules: role.modules, permissions: role.permissions } : { name: '', modules: [], permissions: [] })
    setError(''); setOpen(true)
  }
  function toggle(key: 'modules' | 'permissions', value: string) {
    setDraft(current => ({ ...current, [key]: current[key].includes(value) ? current[key].filter(item => item !== value) : [...current[key], value] }))
  }
  async function save() {
    setSaving(true); setError('')
    try {
      if (editing) await api.put('/staff-roles/' + encodeURIComponent(draft.name), draft)
      else await api.post('/staff-roles', draft)
      await load(); onChanged(); setOpen(false)
    } catch (reason) { setError(apiErrorMessage(reason, 'This role could not be saved.')) }
    finally { setSaving(false) }
  }
  return <Box sx={{ p: { xs: 2, sm: 3, lg: 4 } }}>
    <Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" gap={2} mb={3}>
      <Box><Typography variant="h5" fontWeight={800}>Roles & permissions</Typography><Typography color="text.secondary" mt={.5}>Configure staff responsibilities using the existing CREMS modules and permissions.</Typography></Box>
      <Button variant="contained" disabled={!catalog} startIcon={<AddOutlined />} onClick={() => edit()}>Add role</Button>
    </Stack>
    {error && !open && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
    <Alert severity="info" sx={{ mb: 3 }}>Custom roles use the staff member’s assigned division and branch. Group administration remains with the built-in administrator roles.</Alert>
    <Grid container spacing={2}>
      {catalog?.roles.map(role => <Grid key={role.name} size={{ xs: 12, md: 6, xl: 4 }}><Card variant="outlined" sx={{ height: '100%', borderTop: '3px solid', borderTopColor: role.isBuiltIn ? 'divider' : 'secondary.main' }}><CardContent>
        <Stack direction="row" alignItems="flex-start" justifyContent="space-between" gap={1}><Box><Stack direction="row" alignItems="center" gap={1}><ShieldOutlined fontSize="small" /><Typography variant="h6" fontWeight={800}>{formatRole(role.name)}</Typography></Stack><Typography variant="caption" color="text.secondary">{role.isBuiltIn ? 'Built-in role' : 'Custom role · assigned division and branch'}</Typography></Box><Button size="small" startIcon={role.name === 'SuperAdministrator' ? undefined : <EditOutlined />} onClick={() => edit(role)}>{role.name === 'SuperAdministrator' ? 'View' : 'Edit'}</Button></Stack>
        <Typography variant="overline" display="block" mt={2}>Module access</Typography>
        <Stack direction="row" gap={.75} flexWrap="wrap">{role.modules.map(key => <Chip key={key} size="small" variant="outlined" label={catalog.modules.find(module => module.key === key)?.label ?? key} />)}{['SuperAdministrator', 'Administrator'].includes(role.name) && <Chip size="small" label="Administration" color="secondary" />}</Stack>
        <Typography variant="body2" color="text.secondary" mt={2}>{role.permissions.length} additional action permissions</Typography>
      </CardContent></Card></Grid>)}
    </Grid>
    <Dialog open={open} onClose={() => !saving && setOpen(false)} fullWidth maxWidth="md" scroll="paper"><DialogTitle>{readOnly ? 'Role permissions' : editing ? 'Edit role' : 'Add staff role'}</DialogTitle><DialogContent dividers>
      <Stack spacing={2.5}>{error && <Alert severity="error">{error}</Alert>}{readOnly && <Alert severity="info">The Super Administrator retains full system access.</Alert>}
        {builtIn && !readOnly && <Alert severity="info">Edit the action permissions below. This role keeps its existing module access and operating scope.</Alert>}
        <TextField label="Role name" required value={draft.name} disabled={editing} onChange={event => setDraft(current => ({ ...current, name: event.target.value }))} inputProps={{ maxLength: 80 }} helperText="Available in staff account forms and role filters after saving." />
        <Box><Typography fontWeight={800} mb={1}>Existing modules</Typography><Grid container spacing={.5}>{catalog?.modules.map(module => <Grid key={module.key} size={{ xs: 12, sm: 6 }}><FormControlLabel control={<Checkbox disabled={builtIn || readOnly} checked={draft.modules.includes(module.key)} onChange={() => toggle('modules', module.key)} />} label={module.label} /></Grid>)}</Grid></Box>
        <Box><Typography fontWeight={800} mb={1}>Additional action permissions</Typography><Typography variant="body2" color="text.secondary" mb={1}>These actions apply within the selected modules and the staff member’s operating scope.</Typography><PermissionGroups permissions={editorPermissions} selected={draft.permissions} disabled={readOnly} onChange={permission => toggle('permissions', permission)} /></Box>
      </Stack>
    </DialogContent><DialogActions sx={{ p: 2 }}><Button disabled={saving} onClick={() => setOpen(false)}>{readOnly ? 'Close' : 'Cancel'}</Button>{!readOnly && <Button variant="contained" disabled={saving || draft.name.trim().length < 2 || !draft.modules.length} onClick={() => void save()}>{saving ? 'Saving…' : 'Save role'}</Button>}</DialogActions></Dialog>
  </Box>
}

