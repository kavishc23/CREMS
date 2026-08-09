import { useEffect, useState, type FormEvent } from 'react'
import AddOutlined from '@mui/icons-material/AddOutlined'
import EditOutlined from '@mui/icons-material/EditOutlined'
import LockResetOutlined from '@mui/icons-material/LockResetOutlined'
import LogoutOutlined from '@mui/icons-material/LogoutOutlined'
import LockOpenOutlined from '@mui/icons-material/LockOpenOutlined'
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
type UserForm = { fullName: string; email: string; password: string; role: string; divisionId: string; branchId: string; isActive: boolean; adminNote: string; suspensionReason: string }
const initialForm: UserForm = { fullName: '', email: '', password: '', role: roles.rentalOfficer, divisionId: '', branchId: '', isActive: true, adminNote: '', suspensionReason: '' }

export function UsersPage() {
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

  async function saveUser(event: FormEvent) {
    event.preventDefault()
    setSaving(true)
    setError('')
    try {
      const branchId = form.role === roles.administrator ? null : form.branchId
      const divisionId = form.role === roles.administrator ? null : form.divisionId
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

  return (
    <Box sx={{ p: { xs: 2, sm: 3, lg: 4 }, maxWidth: 1400, mx: 'auto' }}>
      <Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" gap={2} mb={3}>
        <Box>
          <Typography variant="h4" fontWeight={750}>Users & roles</Typography>
          <Typography color="text.secondary" mt={0.5}>Create accounts and control which CREMS areas each person can access.</Typography>
        </Box>
        <Button variant="contained" startIcon={<AddOutlined />} onClick={openCreate}>
          Create user
        </Button>
      </Stack>

      {error && !open && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      <Card variant="outlined">
        <CardContent sx={{ p: 0 }}><Box sx={{ p: 2, borderBottom: 1, borderColor: 'divider' }}><TextField size="small" label="Search users" value={search} onChange={(e) => setSearch(e.target.value)} sx={{ width: { xs: '100%', sm: 360 } }} /></Box>
          {loading ? (
            <Box sx={{ minHeight: 240, display: 'grid', placeItems: 'center' }}><CircularProgress /></Box>
          ) : (
            <TableContainer>
              <Table>
                <TableHead><TableRow sx={{ bgcolor: '#f6f6f3' }}>
                  <TableCell>Name</TableCell><TableCell>Email</TableCell><TableCell>Role</TableCell><TableCell>Division / branch</TableCell><TableCell>Status</TableCell><TableCell align="right">Actions</TableCell>
                </TableRow></TableHead>
                <TableBody>
                  {users.filter((user) => [user.fullName, user.email, user.roles.join(' ')].join(' ').toLowerCase().includes(search.toLowerCase())).map((user) => <TableRow key={user.id} hover>
                    <TableCell sx={{ fontWeight: 600 }}>{user.fullName || 'Unnamed user'}</TableCell>
                    <TableCell>{user.email}</TableCell>
                    <TableCell>{user.roles.map((role) => <Chip key={role} label={formatRole(role)} size="small" sx={{ bgcolor: 'secondary.main', fontWeight: 600 }} />)}</TableCell>
                    <TableCell>{user.roles.includes(roles.administrator) ? 'All divisions and branches' : <><Typography variant="body2" fontWeight={650}>{divisions.find(x => x.id === user.divisionId)?.name ?? 'Division unassigned'}</Typography><Typography variant="caption" color="text.secondary">{branches.find((branch) => branch.id === user.branchId)?.name ?? 'Branch unassigned'}</Typography></>}</TableCell>
                    <TableCell><Stack direction="row" gap={.5} flexWrap="wrap"><Chip label={user.isActive ? 'Active' : 'Disabled'} size="small" color={user.isActive ? 'success' : 'default'} variant="outlined" />{user.lockoutEnd && new Date(user.lockoutEnd) > new Date() && <Chip label="Locked" size="small" color="error" />}{user.mustChangePassword && <Chip label="Password change required" size="small" color="warning" />}</Stack></TableCell>
                    <TableCell align="right"><Tooltip title="Edit staff account"><IconButton onClick={() => openEdit(user)}><EditOutlined /></IconButton></Tooltip><Tooltip title="Security actions"><IconButton onClick={() => { setSecurityUser(user); setReason(''); setTemporaryPassword('') }}><LockResetOutlined /></IconButton></Tooltip></TableCell>
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
                {Object.values(roles).map((role) => <MenuItem key={role} value={role}>{formatRole(role)}</MenuItem>)}
              </TextField>
              {form.role !== roles.administrator && <TextField select required label="Assigned division" value={form.divisionId} onChange={(event) => setForm({ ...form, divisionId: event.target.value, branchId: '' })} helperText="The staff member will only see this division's operational records.">{divisions.map(division => <MenuItem key={division.id} value={division.id}>{division.name}</MenuItem>)}</TextField>}
              {form.role !== roles.administrator && <TextField select required disabled={!form.divisionId} label="Assigned branch" value={form.branchId} onChange={(event) => setForm({ ...form, branchId: event.target.value })} helperText="Operational records are limited to this division and branch.">
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
            <Button type="submit" variant="contained" disabled={saving || (form.role !== roles.administrator && (!form.divisionId || !form.branchId))}>{saving ? 'Saving…' : editing ? 'Save changes' : 'Create user'}</Button>
          </DialogActions>
        </Box>
      </Dialog>
      <Dialog open={Boolean(securityUser)} onClose={() => !saving && setSecurityUser(null)} fullWidth maxWidth="sm"><DialogTitle>Account security · {securityUser?.fullName}</DialogTitle><DialogContent><Stack spacing={2} mt={1}>{error && <Alert severity="error">{error}</Alert>}<Typography variant="body2">Last activity: {securityUser?.lastActivityAt ? new Date(securityUser.lastActivityAt).toLocaleString('en-FJ') : 'Never'} · IP: {securityUser?.lastLoginIp || 'Not recorded'}</Typography><Typography variant="body2">Failed attempts: {securityUser?.accessFailedCount ?? 0}</Typography><TextField label="Administrative reason" multiline minRows={2} value={reason} onChange={(e) => setReason(e.target.value)} />{temporaryPassword && <Alert severity="warning"><Typography fontWeight={700}>Temporary password—shown once</Typography><Typography component="code" sx={{ fontSize: 18, userSelect: 'all' }}>{temporaryPassword}</Typography><Typography variant="body2">Give this securely to the user. CREMS will require them to replace it.</Typography></Alert>}<Button variant="contained" startIcon={<LockResetOutlined />} disabled={saving || !reason} onClick={async () => { if (!securityUser) return; setSaving(true); try { const response = await api.post<{ temporaryPassword: string }>(`/users/${securityUser.id}/reset-password`, { reason }); setTemporaryPassword(response.data.temporaryPassword); await loadUsers() } catch { setError('Unable to reset the password.') } finally { setSaving(false) } }}>Generate temporary password</Button><Button variant="outlined" startIcon={<LogoutOutlined />} disabled={saving || !reason} onClick={async () => { if (!securityUser) return; setSaving(true); try { await api.post(`/users/${securityUser.id}/revoke-sessions`, { reason }); await loadUsers() } catch { setError('Unable to revoke sessions.') } finally { setSaving(false) } }}>Revoke all sessions</Button><Button variant="outlined" startIcon={<LockOpenOutlined />} disabled={saving || !reason} onClick={async () => { if (!securityUser) return; setSaving(true); try { await api.post(`/users/${securityUser.id}/unlock`, { reason }); await loadUsers() } catch { setError('Unable to unlock the account.') } finally { setSaving(false) } }}>Unlock account</Button></Stack></DialogContent><DialogActions><Button onClick={() => setSecurityUser(null)}>Close</Button></DialogActions></Dialog>
    </Box>
  )
}
