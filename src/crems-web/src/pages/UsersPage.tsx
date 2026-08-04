import { useEffect, useState, type FormEvent } from 'react'
import AddOutlined from '@mui/icons-material/AddOutlined'
import EditOutlined from '@mui/icons-material/EditOutlined'
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
  branchId: string | null
  isActive: boolean
  roles: string[]
}

type Branch = { id: string; name: string; isActive: boolean }
type UserForm = { fullName: string; email: string; password: string; role: string; branchId: string; isActive: boolean }
const initialForm: UserForm = { fullName: '', email: '', password: '', role: roles.rentalOfficer, branchId: '', isActive: true }

export function UsersPage() {
  const [users, setUsers] = useState<UserRecord[]>([])
  const [branches, setBranches] = useState<Branch[]>([])
  const [loading, setLoading] = useState(true)
  const [open, setOpen] = useState(false)
  const [saving, setSaving] = useState(false)
  const [editing, setEditing] = useState<UserRecord | null>(null)
  const [error, setError] = useState('')
  const [form, setForm] = useState(initialForm)

  async function loadUsers() {
    try {
      const [userResponse, branchResponse] = await Promise.all([
        api.get<UserRecord[]>('/users'),
        api.get<Branch[]>('/branches'),
      ])
      setUsers(userResponse.data)
      setBranches(branchResponse.data.filter((branch) => branch.isActive))
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
      if (editing) {
        await api.put(`/users/${editing.id}`, { ...form, branchId, password: undefined })
        if (form.password) await api.patch(`/users/${editing.id}/password`, { password: form.password })
      } else {
        await api.post('/users', { ...form, branchId })
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
      branchId: user.branchId ?? '', isActive: user.isActive })
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
        <CardContent sx={{ p: 0 }}>
          {loading ? (
            <Box sx={{ minHeight: 240, display: 'grid', placeItems: 'center' }}><CircularProgress /></Box>
          ) : (
            <TableContainer>
              <Table>
                <TableHead><TableRow sx={{ bgcolor: '#f6f6f3' }}>
                  <TableCell>Name</TableCell><TableCell>Email</TableCell><TableCell>Role</TableCell><TableCell>Branch</TableCell><TableCell>Status</TableCell><TableCell align="right">Actions</TableCell>
                </TableRow></TableHead>
                <TableBody>
                  {users.map((user) => <TableRow key={user.id} hover>
                    <TableCell sx={{ fontWeight: 600 }}>{user.fullName || 'Unnamed user'}</TableCell>
                    <TableCell>{user.email}</TableCell>
                    <TableCell>{user.roles.map((role) => <Chip key={role} label={formatRole(role)} size="small" sx={{ bgcolor: 'secondary.main', fontWeight: 600 }} />)}</TableCell>
                    <TableCell>{user.roles.includes(roles.administrator) ? 'All branches' : branches.find((branch) => branch.id === user.branchId)?.name ?? <Chip label="Unassigned" size="small" color="error" variant="outlined" />}</TableCell>
                    <TableCell><Chip label={user.isActive ? 'Active' : 'Disabled'} size="small" color={user.isActive ? 'success' : 'default'} variant="outlined" /></TableCell>
                    <TableCell align="right"><Tooltip title="Edit staff account"><IconButton onClick={() => openEdit(user)}><EditOutlined /></IconButton></Tooltip></TableCell>
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
              <TextField label={editing ? 'New temporary password (optional)' : 'Temporary password'} type="password" required={!editing} helperText={editing ? 'Leave blank to keep the current password.' : 'At least 10 characters with uppercase, lowercase and a number.'} value={form.password} onChange={(event) => setForm({ ...form, password: event.target.value })} />
              <TextField select label="Role" value={form.role} onChange={(event) => setForm({ ...form, role: event.target.value })}>
                {Object.values(roles).map((role) => <MenuItem key={role} value={role}>{formatRole(role)}</MenuItem>)}
              </TextField>
              {form.role !== roles.administrator && <TextField select required label="Assigned branch" value={form.branchId} onChange={(event) => setForm({ ...form, branchId: event.target.value })} helperText="This account will only be able to access data from this branch.">
                {branches.map((branch) => <MenuItem key={branch.id} value={branch.id}>{branch.name}</MenuItem>)}
              </TextField>}
              {editing && <TextField select label="Account status" value={form.isActive ? 'active' : 'disabled'} onChange={(event) => setForm({ ...form, isActive: event.target.value === 'active' })}>
                <MenuItem value="active">Active</MenuItem><MenuItem value="disabled">Disabled</MenuItem>
              </TextField>}
            </Stack>
          </DialogContent>
          <DialogActions sx={{ p: 3, pt: 1 }}>
            <Button onClick={() => setOpen(false)} disabled={saving}>Cancel</Button>
            <Button type="submit" variant="contained" disabled={saving || (form.role !== roles.administrator && !form.branchId)}>{saving ? 'Saving…' : editing ? 'Save changes' : 'Create user'}</Button>
          </DialogActions>
        </Box>
      </Dialog>
    </Box>
  )
}
