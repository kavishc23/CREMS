import { useEffect, useMemo, useState, type FormEvent } from 'react'
import AddOutlined from '@mui/icons-material/AddOutlined'
import EditOutlined from '@mui/icons-material/EditOutlined'
import LockResetOutlined from '@mui/icons-material/LockResetOutlined'
import LogoutOutlined from '@mui/icons-material/LogoutOutlined'
import LockOpenOutlined from '@mui/icons-material/LockOpenOutlined'
import SecurityOutlined from '@mui/icons-material/SecurityOutlined'
import SearchOutlined from '@mui/icons-material/SearchOutlined'
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
  TextField,
  Tooltip,
  Typography,
} from '@mui/material'
import { api } from '../api/client'
import { formatRole, roles } from '../auth/access'
import axios from 'axios'

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

export function UsersPage({ userRoles }: { userRoles: string[] }) {
  const isSuperAdministrator = userRoles.includes(roles.superAdministrator)
  const assignableRoles = Object.values(roles).filter(role => isSuperAdministrator || role !== roles.superAdministrator)
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
  const [roleFilter, setRoleFilter] = useState('All')
  const [accessUser, setAccessUser] = useState<UserRecord | null>(null); const [access, setAccess] = useState<AccessDetail | null>(null); const [permissionCatalog, setPermissionCatalog] = useState<string[]>([]); const [selectedPermissions, setSelectedPermissions] = useState<string[]>([]); const [selectedDivisions, setSelectedDivisions] = useState<string[]>([]); const [selectedBranches, setSelectedBranches] = useState<string[]>([]); const [accessExpiry, setAccessExpiry] = useState(''); const [accessReason, setAccessReason] = useState(''); const [accountStatus, setAccountStatus] = useState('Active'); const [mfaRequired, setMfaRequired] = useState(false)

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

  function openEdit(user: UserRecord) {
    setEditing(user)
    setForm({ fullName: user.fullName, email: user.email, password: '', role: user.roles[0] ?? roles.rentalOfficer,
      divisionId: user.divisionId ?? '', branchId: user.branchId ?? '', isActive: user.isActive, adminNote: user.adminNote ?? '', suspensionReason: user.suspensionReason ?? '' })
    setError(''); setOpen(true)
  }
  async function openAccess(user: UserRecord) { setAccessUser(user); setAccess(null); setAccessReason(''); try { const [detail, catalog] = await Promise.all([api.get<AccessDetail>(`/access-management/users/${user.id}`), api.get<{ permissions: string[] }>('/access-management/catalog')]); setAccess(detail.data); setPermissionCatalog(catalog.data.permissions); setSelectedPermissions(detail.data.permissions.filter(x => x.isGranted).map(x => x.permission)); setSelectedDivisions(detail.data.scopes.filter(x => x.isActive && x.divisionId).map(x => x.divisionId!)); setSelectedBranches(detail.data.scopes.filter(x => x.isActive && x.branchId).map(x => x.branchId!)); setAccountStatus(detail.data.accountStatus); setMfaRequired(detail.data.mfaRequired) } catch { setError('Unable to load access details.') } }
  async function saveAdvancedAccess() { if (!accessUser || !accessReason.trim()) return; setSaving(true); try { const expiresAt = accessExpiry ? new Date(`${accessExpiry}T23:59:59`).toISOString() : null; await api.put(`/access-management/users/${accessUser.id}/scopes`, { scopes: [...selectedDivisions.map(divisionId => ({ type: 'Division', divisionId, branchId: null, effectiveFrom: null, expiresAt })), ...selectedBranches.map(branchId => ({ type: 'Branch', divisionId: null, branchId, effectiveFrom: null, expiresAt }))], reason: accessReason }); await api.put(`/access-management/users/${accessUser.id}/permissions`, { items: selectedPermissions.map(permission => ({ permission, isGranted: true, expiresAt })), reason: accessReason }); await api.put(`/access-management/users/${accessUser.id}/lifecycle`, { status: accountStatus, mfaRequired, reason: accessReason }); setAccessUser(null); await loadUsers() } catch (reason) { setError(axios.isAxiosError(reason) ? reason.response?.data?.message ?? 'Unable to save advanced access.' : 'Unable to save advanced access.') } finally { setSaving(false) } }

  return (
    <Box sx={{ p: { xs: 2, sm: 3, lg: 4 }, maxWidth: 1400, mx: 'auto' }}>
      <Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" alignItems={{ sm: 'flex-end' }} gap={2} mb={2.5}>
        <Box>
          <Typography variant="h5" fontWeight={800}>Staff directory</Typography>
          <Typography color="text.secondary" mt={0.5}>Review staff status, assigned scope and account activity.</Typography>
        </Box>
        <Button variant="contained" startIcon={<AddOutlined />} onClick={openCreate}>
          Create user
        </Button>
      </Stack>

      {error && !open && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      <Grid container spacing={2} mb={2.5}>
        {staffSummary.map((item) => <Grid key={item.label} size={{ xs: 6, md: 4, lg: 2.4 }}><Card variant="outlined" sx={{ height: '100%' }}><CardContent sx={{ py: 2 }}><Typography variant="h4" fontWeight={800} color={item.color}>{item.value}</Typography><Typography variant="caption" color="text.secondary" fontWeight={700} textTransform="uppercase" letterSpacing={.6}>{item.label}</Typography></CardContent></Card></Grid>)}
      </Grid>
      <Card variant="outlined">
        <CardContent sx={{ p: 0 }}><Stack direction={{ xs: 'column', lg: 'row' }} gap={1.5} sx={{ p: 2, borderBottom: 1, borderColor: 'divider' }}><TextField size="small" placeholder="Search by staff name, email or role" value={search} onChange={(e) => setSearch(e.target.value)} sx={{ flex: 1, minWidth: 260 }} InputProps={{ startAdornment: <InputAdornment position="start"><SearchOutlined /></InputAdornment> }} /><Stack direction="row" gap={.75}>{(['All', 'Active', 'Suspended'] as const).map(value => <Button key={value} size="small" variant={statusFilter === value ? 'contained' : 'outlined'} color="inherit" onClick={() => setStatusFilter(value)}>{value}</Button>)}</Stack><TextField select size="small" label="Role" value={roleFilter} onChange={(event) => setRoleFilter(event.target.value)} sx={{ minWidth: 190 }}><MenuItem value="All">All roles</MenuItem>{Object.values(roles).map(role => <MenuItem key={role} value={role}>{formatRole(role)}</MenuItem>)}</TextField></Stack>
          {loading ? (
            <Box sx={{ minHeight: 240, display: 'grid', placeItems: 'center' }}><CircularProgress /></Box>
          ) : (
            <TableContainer>
              <Table>
                <TableHead><TableRow sx={{ bgcolor: '#f6f6f3' }}>
                  <TableCell>Name</TableCell><TableCell>Email</TableCell><TableCell>Role</TableCell><TableCell>Division / branch</TableCell><TableCell>Status</TableCell><TableCell align="right">Actions</TableCell>
                </TableRow></TableHead>
                <TableBody>
                  {visibleUsers.map((user) => <TableRow key={user.id} hover>
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
        </CardContent>
      </Card>

      <Dialog open={open} onClose={() => !saving && setOpen(false)} fullWidth maxWidth="sm">
        <Box component="form" onSubmit={saveUser}>
          <DialogTitle>{editing ? 'Edit staff account' : 'Create staff account'}</DialogTitle>
          <DialogContent>
            <Stack spacing={2.25} mt={1}>
              {error && <Alert severity="error">{error}</Alert>}
              <TextField label="Full name" required value={form.fullName} onChange={(event) => setForm({ ...form, fullName: event.target.value })} />
              <TextField label="Email" type="email" required value={form.email} onChange={(event) => setForm({ ...form, email: event.target.value })} />
              {!editing && <TextField label="Temporary password" type="password" required helperText="The user will be required to replace this after their first login." value={form.password} onChange={(event) => setForm({ ...form, password: event.target.value })} />}
              <TextField select label="Role" value={form.role} onChange={(event) => setForm({ ...form, role: event.target.value })}>
                {assignableRoles.map((role) => <MenuItem key={role} value={role}>{formatRole(role)}</MenuItem>)}
              </TextField>
              <Alert severity="info">{roleHelp[form.role]}</Alert>
              {form.role !== roles.administrator && form.role !== roles.superAdministrator && <TextField select required label="Assigned division" value={form.divisionId} onChange={(event) => setForm({ ...form, divisionId: event.target.value, branchId: '' })} helperText="The staff member will only see this division's operational records.">{divisions.map(division => <MenuItem key={division.id} value={division.id}>{division.name}</MenuItem>)}</TextField>}
              {form.role !== roles.administrator && form.role !== roles.superAdministrator && <TextField select required disabled={!form.divisionId} label="Assigned branch" value={form.branchId} onChange={(event) => setForm({ ...form, branchId: event.target.value })} helperText="Operational records are limited to this division and branch.">
                {branches.filter(branch => branch.divisionIds.includes(form.divisionId)).map((branch) => <MenuItem key={branch.id} value={branch.id}>{branch.name}</MenuItem>)}
              </TextField>}
              {editing && <TextField select label="Account status" value={form.isActive ? 'active' : 'disabled'} onChange={(event) => setForm({ ...form, isActive: event.target.value === 'active' })}>
                <MenuItem value="active">Active</MenuItem><MenuItem value="disabled">Disabled</MenuItem>
              </TextField>}
              {editing && !form.isActive && <TextField required label="Suspension reason" value={form.suspensionReason} onChange={(event) => setForm({ ...form, suspensionReason: event.target.value })} />}
              <TextField label="Administrator note" multiline minRows={2} value={form.adminNote} onChange={(event) => setForm({ ...form, adminNote: event.target.value })} helperText="Internal note. Never enter passwords or sensitive identification here." />
            </Stack>
          </DialogContent>
          <DialogActions sx={{ p: 3, pt: 1 }}>
            <Button onClick={() => setOpen(false)} disabled={saving}>Cancel</Button>
            <Button type="submit" variant="contained" disabled={saving || (form.role !== roles.administrator && form.role !== roles.superAdministrator && (!form.divisionId || !form.branchId))}>{saving ? 'Saving…' : editing ? 'Save changes' : 'Create user'}</Button>
          </DialogActions>
        </Box>
      </Dialog>
      <Dialog open={Boolean(securityUser)} onClose={() => !saving && setSecurityUser(null)} fullWidth maxWidth="sm"><DialogTitle>Account security · {securityUser?.fullName}</DialogTitle><DialogContent><Stack spacing={2} mt={1}>{error && <Alert severity="error">{error}</Alert>}<Typography variant="body2">Last activity: {securityUser?.lastActivityAt ? new Date(securityUser.lastActivityAt).toLocaleString('en-FJ') : 'Never'} · IP: {securityUser?.lastLoginIp || 'Not recorded'}</Typography><Typography variant="body2">Failed attempts: {securityUser?.accessFailedCount ?? 0}</Typography><TextField label="Administrative reason" multiline minRows={2} value={reason} onChange={(e) => setReason(e.target.value)} />{temporaryPassword && <Alert severity="warning"><Typography fontWeight={700}>Temporary password—shown once</Typography><Typography component="code" sx={{ fontSize: 18, userSelect: 'all' }}>{temporaryPassword}</Typography><Typography variant="body2">Give this securely to the user. CREMS will require them to replace it.</Typography></Alert>}<Button variant="contained" startIcon={<LockResetOutlined />} disabled={saving || !reason} onClick={async () => { if (!securityUser) return; setSaving(true); try { const response = await api.post<{ temporaryPassword: string }>(`/users/${securityUser.id}/reset-password`, { reason }); setTemporaryPassword(response.data.temporaryPassword); await loadUsers() } catch { setError('Unable to reset the password.') } finally { setSaving(false) } }}>Generate temporary password</Button><Button variant="outlined" startIcon={<LogoutOutlined />} disabled={saving || !reason} onClick={async () => { if (!securityUser) return; setSaving(true); try { await api.post(`/users/${securityUser.id}/revoke-sessions`, { reason }); await loadUsers() } catch { setError('Unable to revoke sessions.') } finally { setSaving(false) } }}>Revoke all sessions</Button><Button variant="outlined" startIcon={<LockOpenOutlined />} disabled={saving || !reason} onClick={async () => { if (!securityUser) return; setSaving(true); try { await api.post(`/users/${securityUser.id}/unlock`, { reason }); await loadUsers() } catch { setError('Unable to unlock the account.') } finally { setSaving(false) } }}>Unlock account</Button></Stack></DialogContent><DialogActions><Button onClick={() => setSecurityUser(null)}>Close</Button></DialogActions></Dialog>
      <Dialog open={Boolean(accessUser)} onClose={() => !saving && setAccessUser(null)} fullWidth maxWidth="md"><DialogTitle>Access control · {accessUser?.fullName}</DialogTitle><DialogContent>{!access ? <Box sx={{ minHeight: 220, display: 'grid', placeItems: 'center' }}><CircularProgress /></Box> : <Stack spacing={2.5} mt={1}><Alert severity="info">Role permissions are the default. Add explicit permissions and temporary division or branch access only when required.</Alert><Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}><TextField select fullWidth label="Account lifecycle" value={accountStatus} onChange={e => setAccountStatus(e.target.value)}>{['Invited','ActivationPending','Active','Locked','Suspended','Deactivated'].map(x => <MenuItem key={x} value={x}>{formatRole(x)}</MenuItem>)}</TextField><TextField fullWidth type="date" label="Access expiry" InputLabelProps={{ shrink: true }} value={accessExpiry} onChange={e => setAccessExpiry(e.target.value)} /></Stack><Chip clickable color={mfaRequired ? 'primary' : 'default'} label={mfaRequired ? 'MFA required' : 'Require MFA'} onClick={() => setMfaRequired(x => !x)} /><Box><Typography fontWeight={750} mb={1}>Divisions</Typography><Stack direction="row" gap={1} flexWrap="wrap">{divisions.map(item => <Chip clickable key={item.id} color={selectedDivisions.includes(item.id) ? 'primary' : 'default'} label={item.name} onClick={() => setSelectedDivisions(current => current.includes(item.id) ? current.filter(x => x !== item.id) : [...current, item.id])} />)}</Stack></Box><Box><Typography fontWeight={750} mb={1}>Branches</Typography><Stack direction="row" gap={1} flexWrap="wrap">{branches.map(item => <Chip clickable key={item.id} color={selectedBranches.includes(item.id) ? 'primary' : 'default'} label={item.name} onClick={() => setSelectedBranches(current => current.includes(item.id) ? current.filter(x => x !== item.id) : [...current, item.id])} />)}</Stack></Box><Box><Typography fontWeight={750} mb={1}>Additional permissions</Typography><Stack direction="row" gap={1} flexWrap="wrap">{permissionCatalog.map(item => <Chip clickable key={item} color={selectedPermissions.includes(item) ? 'secondary' : 'default'} label={item} onClick={() => setSelectedPermissions(current => current.includes(item) ? current.filter(x => x !== item) : [...current, item])} />)}</Stack></Box><TextField required multiline minRows={2} label="Reason for access change" value={accessReason} onChange={e => setAccessReason(e.target.value)} /><Box><Typography fontWeight={750}>Recent security history</Typography>{access.events.slice(0, 8).map(item => <Typography key={item.id} variant="body2" color="text.secondary">{new Date(item.occurredAt).toLocaleString('en-FJ')} · {formatRole(item.type)}{item.detail ? ` · ${item.detail}` : ''}</Typography>)}</Box></Stack>}</DialogContent><DialogActions><Button onClick={() => setAccessUser(null)}>Cancel</Button><Button variant="contained" disabled={saving || !accessReason.trim()} onClick={() => void saveAdvancedAccess()}>Save access</Button></DialogActions></Dialog>
    </Box>
  )
}
