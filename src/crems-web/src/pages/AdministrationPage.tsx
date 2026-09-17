import { useEffect, useState } from 'react'
import ManageAccountsOutlined from '@mui/icons-material/ManageAccountsOutlined'
import { Alert, Box, Card, CardContent, Chip, Grid, Stack, Tab, Tabs, Typography } from '@mui/material'
import { api } from '../api/client'
import { formatRole, roles } from '../auth/access'
import { PageHeader } from '../components/PageHeader'
import { UsersPage } from './UsersPage'

type Permission = { role: string; scope: string; permissions: string[] }

export function AdministrationPage({ userRoles }: { userRoles: string[] }) {
  const isSuperAdministrator = userRoles.includes(roles.superAdministrator)
  const [tab, setTab] = useState(0)
  const [permissions, setPermissions] = useState<Permission[]>([])
  const [error, setError] = useState('')

  useEffect(() => {
    if (!isSuperAdministrator) return
    void api.get<Permission[]>('/administration/permissions')
      .then((response) => setPermissions(response.data))
      .catch(() => setError('Role and permission information could not be loaded.'))
  }, [isSuperAdministrator])

  if (!isSuperAdministrator) {
    return <Box>
      <Box sx={{ px: { xs: 2, sm: 3, lg: 4 }, pt: 3 }}><PageHeader icon={<ManageAccountsOutlined />} title="Staff access & activity" subtitle="Manage staff accounts, operating scope, security and role permissions." /></Box>
      <UsersPage userRoles={userRoles} />
    </Box>
  }

  return <Box>
    <Box sx={{ px: { xs: 2, sm: 3, lg: 4 }, pt: 3 }}><PageHeader icon={<ManageAccountsOutlined />} title="Staff access & activity" subtitle="Manage staff accounts, operating scope, security and role permissions." /></Box>
    {error && <Alert severity="error" sx={{ mx: { xs: 2, sm: 3, lg: 4 }, mt: 2 }}>{error}</Alert>}
    <Tabs value={tab} onChange={(_, value) => setTab(value)} variant="scrollable" sx={{ px: { xs: 2, sm: 3, lg: 4 }, borderBottom: 1, borderColor: 'divider' }}>
      <Tab label="Staff accounts" />
      <Tab label="Roles & permissions" />
    </Tabs>
    {tab === 0 && <UsersPage userRoles={userRoles} />}
    {tab === 1 && <Box sx={{ p: { xs: 2, sm: 3, lg: 4 } }}><Typography variant="h5" fontWeight={800}>Roles and permissions</Typography><Typography color="text.secondary" mt={.5} mb={3}>Review the responsibilities and default access assigned to each staff role.</Typography><Grid container spacing={2}>{permissions.map(item => <Grid key={item.role} size={{ xs: 12, md: 6, xl: 4 }}><Card variant="outlined" sx={{ height: '100%' }}><CardContent><Stack direction="row" justifyContent="space-between" gap={1}><Typography variant="h6" fontWeight={800}>{formatRole(item.role)}</Typography><Chip size="small" label={item.scope} /></Stack><Stack component="ul" spacing={1} sx={{ pl: 2, mb: 0 }}>{item.permissions.map(permission => <Typography component="li" variant="body2" key={permission}>{permission}</Typography>)}</Stack></CardContent></Card></Grid>)}</Grid></Box>}
  </Box>
}
