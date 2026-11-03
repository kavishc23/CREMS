import { useEffect, useMemo, useState, useImperativeHandle, type Ref, type FormEvent } from 'react'
import AddOutlined from '@mui/icons-material/AddOutlined'
import EditOutlined from '@mui/icons-material/EditOutlined'
import LockResetOutlined from '@mui/icons-material/LockResetOutlined'
import LogoutOutlined from '@mui/icons-material/LogoutOutlined'
import LockOpenOutlined from '@mui/icons-material/LockOpenOutlined'
import SecurityOutlined from '@mui/icons-material/SecurityOutlined'
import SearchOutlined from '@mui/icons-material/SearchOutlined'
import ManageAccountsOutlined from '@mui/icons-material/ManageAccountsOutlined'
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  Chip,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  IconButton,
  InputAdornment,
  Grid,
  MenuItem,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Pagination,
  TextField,
  Tooltip,
  Typography,
} from '@mui/material'
import { api } from '../api/client'
import { formatRole, roles } from '../auth/access'
import { PageHeader } from '../components/PageHeader'
import axios from 'axios'
import { PermissionGroups } from '../components/PermissionGroups'
import { type StaffRoleCatalog } from '../auth/staffAccess'
import { Autocomplete, FormControlLabel, Switch, Tab, Tabs } from '@mui/material'

type UserRecord = {
  id: string
  email: string
  fullName: string
  divisionId: string | null
  branchId: string | null
  isActive: boolean
  roles: string[]
  mustChangePassword: boolean; lastLoginAt: string | null; lastLoginIp: string | null; lastActivityAt: string | null
  accessFailedCount: number; lockoutEnd: string | null; adminNote: string | null; suspensionReason: string | null
}

type Branch = { id: string; name: string; isActive: boolean; divisionIds: string[] }
type Division = { id: string; name: string; isActive: boolean }
type AccessDetail = { accountStatus: string; mfaRequired: boolean; scopes: { divisionId: string | null; branchId: string | null; isActive: boolean }[]; permissions: { permission: string; isGranted: boolean }[]; events: { id: string; type: string; occurredAt: string; detail: string | null }[] }
type UserForm = { fullName: string; email: string; password: string; role: string; divisionId: string; branchId: string; isActive: boolean; adminNote: string; suspensionReason: string }
const initialForm: UserForm = { fullName: '', email: '', password: '', role: roles.rentalOfficer, divisionId: '', branchId: '', isActive: true, adminNote: '', suspensionReason: '' }
const roleHelp: Record<string, string> = {
  [roles.superAdministrator]: 'Group-wide system owner. Can add divisions and control all configuration.',
  [roles.administrator]: 'Group-wide operational administrator without division or system-configuration control.',
  [roles.branchManager]: 'Manages rentals, assets, staff work and reports for one division and branch.',
  [roles.rentalOfficer]: 'Processes customers, bookings, agreements, collections and returns.',
  [roles.maintenanceOfficer]: 'Records inspections, servicing, parts, costs and asset availability.',
}

export function UsersPage({ userRoles, embedded = false, roleRevision = 0, ref }: { userRoles: string[]; embedded?: boolean; roleRevision?: number; ref?: Ref<{ openCreate: () => void }> }) {
  const isSuperAdministrator = userRoles.includes(roles.superAdministrator)
  const [roleNames, setRoleNames] = useState<string[]>(Object.values(roles))
  const assignableRoles = roleNames.filter(role => isSuperAdministrator || role !== roles.superAdministrator)
  const [accessTab, setAccessTab] = useState(0)
  const [users, setUsers] = useState<UserRecord[]>([])
  const [branches, setBranches] = useState<Branch[]>([])
  const [divisions, setDivisions] = useState<Division[]>([])
  const [loading, setLoading] = useState(true)
  const [open, setOpen] = useState(false)
  const [saving, setSaving] = useState(false)
  const [editing, setEditing] = useState<UserRecord | null>(null)
  const [error, setError] = useState('')
  const [form, setForm] = useState(initialForm)
  const [search, setSearch] = useState(''); const [securityUser, setSecurityUser] = useState<UserRecord | null>(null); const [reason, setReason] = useState(''); const [temporaryPassword, setTemporaryPassword] = useState('')
  const [statusFilter, setStatusFilter] = useState<'All' | 'Active' | 'Suspended'>('All')
  const [page, setPage] = useState(1)
  const [roleFilter, setRoleFilter] = useState('All')
  const [accessUser, setAccessUser] = useState<UserRecord | null>(null); const [access, setAccess] = useState<AccessDetail | null>(null); const [permissionCatalog, setPermissionCatalog] = useState<string[]>([]); const [selectedPermissions, setSelectedPermissions] = useState<string[]>([]); const [selectedDivisions, setSelectedDivisions] = useState<string[]>([]); const [selectedBranches, setSelectedBranches] = useState<string[]>([]); const [accessExpiry, setAccessExpiry] = useState(''); const [accessReason, setAccessReason] = useState(''); const [accountStatus, setAccountStatus] = useState('Active'); const [mfaRequired, setMfaRequired] = useState(false)

  useEffect(() => {
    void api.get<StaffRoleCatalog>('/staff-roles').then(response => setRoleNames(response.data.roles.map(role => role.name))).catch(() => setError('Role options could not be refreshed.'))
  }, [roleRevision])

  async function loadUsers() {
    try {
      const [userResponse, branchResponse, divisionResponse] = await Promise.all([
        api.get<UserRecord[]>('/users'),
        api.get<Branch[]>('/branches'),
        api.get<Division[]>('/divisions'),
      ])
      setUsers(userResponse.data)
      setBranches(branchResponse.data.filter((branch) => branch.isActive))
      setDivisions(divisionResponse.data.filter((division) => division.isActive))
    } catch {
      setError('Unable to load users. Confirm that the backend is running.')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    // Initial account loading synchronizes this page with the administrator API.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void loadUsers()
  }, [])

  const visibleUsers = useMemo(() => users.filter((user) => {
    const matchesSearch = [user.fullName, user.email, user.roles.join(' ')].join(' ').toLowerCase().includes(search.trim().toLowerCase())
    const matchesStatus = statusFilter === 'All' || (statusFilter === 'Active' ? user.isActive : !user.isActive)
    const matchesRole = roleFilter === 'All' || user.roles.includes(roleFilter)
    return matchesSearch && matchesStatus && matchesRole
  }), [roleFilter, search, statusFilter, users])
  const userPageCount = Math.max(1, Math.ceil(visibleUsers.length / 20))
  const pagedUsers = visibleUsers.slice((page - 1) * 20, page * 20)

  const staffSummary = useMemo(() => [
    { label: 'Total staff', value: users.length, color: 'text.primary' },
    { label: 'Active', value: users.filter((item) => item.isActive).length, color: 'success.main' },
    { label: 'Roles in use', value: new Set(users.flatMap((item) => item.roles)).size, color: 'info.main' },
    { label: 'Suspended', value: users.filter((item) => !item.isActive).length, color: 'warning.main' },
    { label: 'Locked', value: users.filter((item) => item.lockoutEnd && new Date(item.lockoutEnd) > new Date()).length, color: 'error.main' },
  ], [users])

  async function saveUser(event: FormEvent) {
    event.preventDefault()
    setSaving(true)
    setError('')
    try {
      const groupWide = form.role === roles.administrator || form.role === roles.superAdministrator
      const branchId = groupWide ? null : form.branchId
      const divisionId = groupWide ? null : form.divisionId
      if (editing) {
        await api.put(`/users/${editing.id}`, { ...form, branchId, divisionId, password: undefined })
      } else {
        await api.post('/users', { ...form, branchId, divisionId })
      }
      setForm(initialForm)
      setOpen(false)
      await loadUsers()
    } catch (requestError: unknown) {
      const responseData = axios.isAxiosError(requestError) ? requestError.response?.data : undefined
      const errors = responseData?.errors as Record<string, string[]> | undefined
      setError(errors ? Object.values(errors).flat().join(' ') : 'Unable to save the user.')
    } finally {
      setSaving(false)
    }
  }

  function openCreate() {
    setEditing(null); setForm(initialForm); setError(''); setOpen(true)
  }

  useImperativeHandle(ref, () => ({ openCreate }))

  function openEdit(user: UserRecord) {
    setEditing(user)
    setForm({ fullName: user.fullName, email: user.email, password: '', role: user.roles[0] ?? roles.rentalOfficer,
      divisionId: user.divisionId ?? '', branchId: user.branchId ?? '', isActive: user.isActive, adminNote: user.adminNote ?? '', suspensionReason: user.suspensionReason ?? '' })
    setError(''); setOpen(true)
  }
  async function openAccess(user: UserRecord) { setAccessUser(user); setAccess(null); setAccessReason(''); setAccessExpiry(''); setAccessTab(0); setError(''); try { const [detail, catalog] = await Promise.all([api.get<AccessDetail>(`/access-management/users/${user.id}`), api.get<{ permissions: string[] }>('/access-management/catalog')]); setAccess(detail.data); setPermissionCatalog(catalog.data.permissions); setSelectedPermissions(detail.data.permissions.filter(x => x.isGranted).map(x => x.permission)); setSelectedDivisions(detail.data.scopes.filter(x => x.isActive && x.divisionId).map(x => x.divisionId!)); setSelectedBranches(detail.data.scopes.filter(x => x.isActive && x.branchId).map(x => x.branchId!)); setAccountStatus(detail.data.accountStatus); setMfaRequired(detail.data.mfaRequired) } catch { setError('Unable to load access details.') } }
  async function saveAdvancedAccess() { if (!accessUser || !accessReason.trim()) return; setSaving(true); try { const expiresAt = accessExpiry ? new Date(`${accessExpiry}T23:59:59`).toISOString() : null; await api.put(`/access-management/users/${accessUser.id}/scopes`, { scopes: [...selectedDivisions.map(divisionId => ({ type: 'Division', divisionId, branchId: null, effectiveFrom: null, expiresAt })), ...selectedBranches.map(branchId => ({ type: 'Branch', divisionId: null, branchId, effectiveFrom: null, expiresAt }))], reason: accessReason }); await api.put(`/access-management/users/${accessUser.id}/permissions`, { items: selectedPermissions.map(permission => ({ permission, isGranted: true, expiresAt })), reason: accessReason }); await api.put(`/access-management/users/${accessUser.id}/lifecycle`, { status: accountStatus, mfaRequired, reason: accessReason }); setAccessUser(null); await loadUsers() } catch (reason) { setError(axios.isAxiosError(reason) ? reason.response?.data?.message ?? 'Unable to save advanced access.' : 'Unable to save advanced access.') } finally { setSaving(false) } }

  return (
    <Box sx={{ p: { xs: 2, sm: 3, lg: 4 }, maxWidth: 1400, mx: 'auto' }}>
      {!embedded && <PageHeader icon={<ManageAccountsOutlined />} title="Staff directory" subtitle="Review staff status, assigned scope and account activity." actions={<Button variant="contained" startIcon={<AddOutlined />} onClick={openCreate}>Create staff account</Button>} />}

      {error && !open && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      <Grid container spacing={2} mb={2.5}>
        {staffSummary.map((item) => <Grid key={item.label} size={{ xs: 6, md: 4, lg: 2.4 }}><Card variant="outlined" sx={{ height: '100%' }}><CardContent sx={{ py: 2 }}><Typography variant="h4" fontWeight={800} color={item.color}>{item.value}</Typography><Typography variant="caption" color="text.secondary" fontWeight={700} textTransform="uppercase" letterSpacing={.6}>{item.label}</Typography></CardContent></Card></Grid>)}
      </Grid>
      <Card variant="outlined">
        <CardContent sx={{ p: 0 }}><Stack direction={{ xs: 'column', lg: 'row' }} gap={1.5} sx={{ p: 2, borderBottom: 1, borderColor: 'divider' }}><TextField size="small" placeholder="Search by staff name, email or role" value={search} onChange={(e) => { setSearch(e.target.value); setPage(1) }} sx={{ flex: 1, minWidth: 260 }} InputProps={{ startAdornment: <InputAdornment position="start"><SearchOutlined /></InputAdornment> }} /><Stack direction="row" gap={.75}>{(['All', 'Active', 'Suspended'] as const).map(value => <Button key={value} size="small" variant={statusFilter === value ? 'contained' : 'outlined'} color="inherit" onClick={() => { setStatusFilter(value); setPage(1) }}>{value}</Button>)}</Stack><TextField select size="small" label="Role" value={roleFilter} onChange={(event) => { setRoleFilter(event.target.value); setPage(1) }} sx={{ minWidth: 190 }}><MenuItem value="All">All roles</MenuItem>{roleNames.map(role => <MenuItem key={role} value={role}>{formatRole(role)}</MenuItem>)}</TextField></Stack>
          {loading ? (
            <Box sx={{ minHeight: 240, display: 'grid', placeItems: 'center' }}><CircularProgress /></Box>
          ) : (
            <TableContainer>
              <Table>
                <TableHead><TableRow sx={{ bgcolor: '#f6f6f3' }}>
                  <TableCell>Name</TableCell><TableCell>Email</TableCell><TableCell>Role</TableCell><TableCell>Division / branch</TableCell><TableCell>Status</TableCell><TableCell align="right">Actions</TableCell>
                </TableRow></TableHead>
                <TableBody>
                  {pagedUsers.map((user) => <TableRow key={user.id} hover>
                    <TableCell sx={{ fontWeight: 600 }}>{user.fullName || 'Unnamed user'}</TableCell>
                    <TableCell><Typography variant="body2">{user.email}</Typography><Typography variant="caption" color="text.secondary">Last active {user.lastActivityAt ? new Date(user.lastActivityAt).toLocaleString('en-FJ') : 'never'}</Typography></TableCell>
                    <TableCell>{user.roles.map((role) => <Chip key={role} label={formatRole(role)} size="small" sx={{ bgcolor: 'secondary.main', fontWeight: 600 }} />)}</TableCell>
                    <TableCell>{user.roles.some(role => role === roles.administrator || role === roles.superAdministrator) ? 'All divisions and branches' : <><Typography variant="body2" fontWeight={650}>{divisions.find(x => x.id === user.divisionId)?.name ?? 'Division unassigned'}</Typography><Typography variant="caption" color="text.secondary">{branches.find((branch) => branch.id === user.branchId)?.name ?? 'Branch unassigned'}</Typography></>}</TableCell>
                    <TableCell><Stack direction="row" gap={.5} flexWrap="wrap"><Chip label={user.isActive ? 'Active' : 'Disabled'} size="small" color={user.isActive ? 'success' : 'default'} variant="outlined" />{user.lockoutEnd && new Date(user.lockoutEnd) > new Date() && <Chip label="Locked" size="small" color="error" />}{user.mustChangePassword && <Chip label="Password change required" size="small" color="warning" />}</Stack></TableCell>
                    <TableCell align="right">{(isSuperAdministrator || !user.roles.includes(roles.superAdministrator)) ? <><Tooltip title="Edit staff account"><IconButton onClick={() => openEdit(user)}><EditOutlined /></IconButton></Tooltip><Tooltip title="Scopes and permissions"><IconButton onClick={() => void openAccess(user)}><SecurityOutlined /></IconButton></Tooltip><Tooltip title="Security actions"><IconButton onClick={() => { setSecurityUser(user); setReason(''); setTemporaryPassword('') }}><LockResetOutlined /></IconButton></Tooltip></> : <Chip label="Super Admin only" size="small" variant="outlined" />}</TableCell>
                  </TableRow>)}
                </TableBody>
              </Table>
            </TableContainer>
          )}
          {visibleUsers.length > 20 && <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ p: 2, borderTop: 1, borderColor: 'divider' }}><Typography variant="body2" color="text.secondary">Showing {(page - 1) * 20 + 1}–{Math.min(page * 20, visibleUsers.length)} of {visibleUsers.length}</Typography><Pagination count={userPageCount} page={page} onChange={(_, value) => setPage(value)} size="small" /></Stack>}
        </CardContent>
      </Card>

      <Dialog open={open} onClose={() => !saving && setOpen(false)} fullWidth maxWidth="md" scroll="paper">
        <Box component="form" onSubmit={saveUser}>
          <DialogTitle>{editing ? 'Edit staff account' : 'Create staff account'}</DialogTitle>
          <DialogContent dividers>
            <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, minmax(0, 1fr))' }, gap: 2, pt: 1, '& .MuiTextField-root': { minWidth: 0 } }}>
              {error && <Alert severity="error" sx={{ gridColumn: '1 / -1' }}>{error}</Alert>}
              <TextField label="Full name" required value={form.fullName} onChange={(event) => setForm({ ...form, fullName: event.target.value })} />
              <TextField label="Email" type="email" required value={form.email} onChange={(event) => setForm({ ...form, email: event.target.value })} />
              {!editing && <TextField label="Temporary password" type="password" required helperText="The user will be required to replace this after their first login." value={form.password} onChange={(event) => setForm({ ...form, password: event.target.value })} />}
              <TextField select label="Role" value={form.role} onChange={(event) => setForm({ ...form, role: event.target.value })}>
                {assignableRoles.map((role) => <MenuItem key={role} value={role}>{formatRole(role)}</MenuItem>)}
              </TextField>
              <Typography variant="body2" color="text.secondary" sx={{ gridColumn: '1 / -1' }}>{roleHelp[form.role] ?? 'Uses the configured role permissions within the assigned division and branch.'}</Typography>
              {form.role !== roles.administrator && form.role !== roles.superAdministrator && <TextField select required label="Assigned division" value={form.divisionId} onChange={(event) => setForm({ ...form, divisionId: event.target.value, branchId: '' })} helperText="The staff member will only see this division's operational records.">{divisions.map(division => <MenuItem key={division.id} value={division.id}>{division.name}</MenuItem>)}</TextField>}
              {form.role !== roles.administrator && form.role !== roles.superAdministrator && <TextField select required disabled={!form.divisionId} label="Assigned branch" value={form.branchId} onChange={(event) => setForm({ ...form, branchId: event.target.value })} helperText="Operational records are limited to this division and branch.">
                {branches.filter(branch => branch.divisionIds.includes(form.divisionId)).map((branch) => <MenuItem key={branch.id} value={branch.id}>{branch.name}</MenuItem>)}
              </TextField>}
              {editing && <TextField select label="Account status" value={form.isActive ? 'active' : 'disabled'} onChange={(event) => setForm({ ...form, isActive: event.target.value === 'active' })}>
                <MenuItem value="active">Active</MenuItem><MenuItem value="disabled">Disabled</MenuItem>
              </TextField>}
              {editing && !form.isActive && <TextField required label="Suspension reason" value={form.suspensionReason} onChange={(event) => setForm({ ...form, suspensionReason: event.target.value })} />}
              <TextField sx={{ gridColumn: '1 / -1' }} label="Administrator note" multiline minRows={2} value={form.adminNote} onChange={(event) => setForm({ ...form, adminNote: event.target.value })} helperText="Internal note. Never enter passwords or sensitive identification here." />
            </Box>
          </DialogContent>
          <DialogActions sx={{ p: 3, pt: 1 }}>
            <Button onClick={() => setOpen(false)} disabled={saving}>Cancel</Button>
            <Button type="submit" variant="contained" disabled={saving || (form.role !== roles.administrator && form.role !== roles.superAdministrator && (!form.divisionId || !form.branchId))}>{saving ? 'Saving…' : editing ? 'Save changes' : 'Create staff account'}</Button>
          </DialogActions>
        </Box>
      </Dialog>
      <Dialog open={Boolean(securityUser)} onClose={() => !saving && setSecurityUser(null)} fullWidth maxWidth="sm"><DialogTitle>Account security · {securityUser?.fullName}</DialogTitle><DialogContent><Stack spacing={2} mt={1}>{error && <Alert severity="error">{error}</Alert>}<Typography variant="body2">Last activity: {securityUser?.lastActivityAt ? new Date(securityUser.lastActivityAt).toLocaleString('en-FJ') : 'Never'} · IP: {securityUser?.lastLoginIp || 'Not recorded'}</Typography><Typography variant="body2">Failed attempts: {securityUser?.accessFailedCount ?? 0}</Typography><TextField label="Administrative reason" multiline minRows={2} value={reason} onChange={(e) => setReason(e.target.value)} />{temporaryPassword && <Alert severity="warning"><Typography fontWeight={700}>Temporary password—shown once</Typography><Typography component="code" sx={{ fontSize: 18, userSelect: 'all' }}>{temporaryPassword}</Typography><Typography variant="body2">Give this securely to the user. CREMS will require them to replace it.</Typography></Alert>}<Button variant="contained" startIcon={<LockResetOutlined />} disabled={saving || !reason} onClick={async () => { if (!securityUser) return; setSaving(true); try { const response = await api.post<{ temporaryPassword: string }>(`/users/${securityUser.id}/reset-password`, { reason }); setTemporaryPassword(response.data.temporaryPassword); await loadUsers() } catch { setError('Unable to reset the password.') } finally { setSaving(false) } }}>Generate temporary password</Button><Button variant="outlined" startIcon={<LogoutOutlined />} disabled={saving || !reason} onClick={async () => { if (!securityUser) return; setSaving(true); try { await api.post(`/users/${securityUser.id}/revoke-sessions`, { reason }); await loadUsers() } catch { setError('Unable to revoke sessions.') } finally { setSaving(false) } }}>Revoke all sessions</Button><Button variant="outlined" startIcon={<LockOpenOutlined />} disabled={saving || !reason} onClick={async () => { if (!securityUser) return; setSaving(true); try { await api.post(`/users/${securityUser.id}/unlock`, { reason }); await loadUsers() } catch { setError('Unable to unlock the account.') } finally { setSaving(false) } }}>Unlock account</Button></Stack></DialogContent><DialogActions><Button onClick={() => setSecurityUser(null)}>Close</Button></DialogActions></Dialog>
      <Dialog open={Boolean(accessUser)} onClose={() => !saving && setAccessUser(null)} fullWidth maxWidth="md" scroll="paper">
        <DialogTitle sx={{ borderLeft: '5px solid', borderColor: 'secondary.main', bgcolor: '#fafaf7', py: 2 }}><Stack direction="row" alignItems="center" gap={1.5}><Box sx={{ bgcolor: 'primary.main', color: 'secondary.main', p: 1, borderRadius: 1, display: 'flex' }}><SecurityOutlined /></Box><Box><Typography variant="overline" color="text.secondary" lineHeight={1.4}>Staff access management</Typography><Typography variant="h6" fontWeight={800}>{accessUser?.fullName}</Typography><Typography variant="caption" color="text.secondary">{accessUser?.email} · {accessUser?.roles.map(formatRole).join(', ')}</Typography></Box></Stack></DialogTitle>
        <Tabs value={accessTab} onChange={(_, value) => setAccessTab(value)} sx={{ px: 3, borderBottom: 1, borderColor: 'divider' }}><Tab label="Account & scope" /><Tab label="Permissions" /><Tab label="Security history" /></Tabs>
        <DialogContent dividers sx={{ minHeight: 300, bgcolor: '#f8f8f5', p: 2.5 }}>
          {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
          {!access ? <Box sx={{ minHeight: 220, display: 'grid', placeItems: 'center' }}><CircularProgress /></Box> : <>
            {accessTab === 0 && <Stack spacing={2}>
              <Box sx={{ border: 1, borderColor: 'divider', bgcolor: 'background.paper', p: 2, borderRadius: 1 }}><Typography fontWeight={800} mb={.5}>Account security</Typography><Typography variant="body2" color="text.secondary" mb={2}>Control sign-in status and authentication requirements.</Typography>
              <Grid container spacing={2}><Grid size={{ xs: 12, sm: 6 }}><TextField select fullWidth label="Account status" value={accountStatus} onChange={event => setAccountStatus(event.target.value)}>{['Invited', 'ActivationPending', 'Active', 'Locked', 'Suspended', 'Deactivated'].map(value => <MenuItem key={value} value={value}>{formatRole(value)}</MenuItem>)}</TextField></Grid><Grid size={{ xs: 12, sm: 6 }}><TextField fullWidth type="date" label="Additional access expiry" InputLabelProps={{ shrink: true }} value={accessExpiry} onChange={event => setAccessExpiry(event.target.value)} /></Grid></Grid>
              <FormControlLabel sx={{ mt: 1.5 }} control={<Switch checked={mfaRequired} onChange={event => setMfaRequired(event.target.checked)} />} label="Require multi-factor authentication" /></Box><Box sx={{ border: 1, borderColor: 'divider', bgcolor: 'background.paper', p: 2, borderRadius: 1 }}><Typography fontWeight={800} mb={.5}>Additional operating scope</Typography><Typography variant="body2" color="text.secondary" mb={2}>Assigned scope: {divisions.find(item => item.id === accessUser?.divisionId)?.name ?? 'Group-wide'} · {branches.find(item => item.id === accessUser?.branchId)?.name ?? 'All branches'}</Typography><Stack spacing={2}>
              <Autocomplete multiple size="small" limitTags={2} options={divisions} getOptionLabel={item => item.name} isOptionEqualToValue={(a, b) => a.id === b.id} value={divisions.filter(item => selectedDivisions.includes(item.id))} onChange={(_, values) => setSelectedDivisions(values.map(item => item.id))} slotProps={{ listbox: { sx: { maxHeight: 200 } } }} renderInput={params => <TextField {...params} label="Additional divisions" helperText="The staff member’s assigned division is retained." />} />
              <Autocomplete multiple size="small" limitTags={2} options={branches} getOptionLabel={item => item.name} isOptionEqualToValue={(a, b) => a.id === b.id} value={branches.filter(item => selectedBranches.includes(item.id))} onChange={(_, values) => setSelectedBranches(values.map(item => item.id))} slotProps={{ listbox: { sx: { maxHeight: 200 } } }} renderInput={params => <TextField {...params} label="Additional branches" helperText="The staff member’s assigned branch is retained." />} /></Stack></Box>
            </Stack>}
            {accessTab === 1 && <Box><Typography fontWeight={800} mb={.5}>Additional action permissions</Typography><Typography variant="body2" color="text.secondary" mb={1.5}>Select individual exceptions to the role defaults. Highlighted items are additional grants for this staff account.</Typography><PermissionGroups permissions={permissionCatalog} selected={selectedPermissions} onChange={item => setSelectedPermissions(current => current.includes(item) ? current.filter(value => value !== item) : [...current, item])} /></Box>}
            {accessTab === 2 && <Stack spacing={1.5} sx={{ bgcolor: 'background.paper', p: 2, border: 1, borderColor: 'divider', borderRadius: 1 }}><Typography fontWeight={800}>Recent security history</Typography>{access.events.length === 0 && <Typography color="text.secondary">No security events recorded.</Typography>}{access.events.slice(0, 8).map(item => <Box key={item.id} sx={{ pb: 1.5, borderBottom: 1, borderColor: 'divider' }}><Typography variant="body2" fontWeight={700}>{formatRole(item.type)}</Typography><Typography variant="caption" color="text.secondary">{new Date(item.occurredAt).toLocaleString('en-FJ')}{item.detail ? ' · ' + item.detail : ''}</Typography></Box>)}</Stack>}
          </>}
        </DialogContent>
        <Box sx={{ px: 3, pt: 2, pb: .5, bgcolor: 'background.paper' }}><TextField fullWidth required size="small" label="Reason for access change" value={accessReason} onChange={event => setAccessReason(event.target.value)} /></Box>
        <DialogActions sx={{ px: 3, py: 2 }}><Button disabled={saving} onClick={() => setAccessUser(null)}>Cancel</Button><Button variant="contained" disabled={saving || !access || !accessReason.trim()} onClick={() => void saveAdvancedAccess()}>{saving ? 'Saving…' : 'Save access'}</Button></DialogActions>
      </Dialog>
    </Box>
  )
}
