import { useEffect, useState } from 'react'
import ManageAccountsOutlined from '@mui/icons-material/ManageAccountsOutlined'
import { Alert, Box, Card, CardContent, Chip, Grid, Stack, Tab, Tabs, Typography } from '@mui/material'
import { api } from '../api/client'
import { formatRole, roles } from '../auth/access'
import { CustomersPage } from './CustomersPage'
import { UsersPage } from './UsersPage'

type Summary = {
  totalUsers: number
  activeUsers: number
  disabledUsers: number
  lockedUsers: number
  passwordChangeRequired: number
  administrators: number
}
type Permission = { role: string; scope: string; permissions: string[] }

export function AdministrationPage({ userRoles }: { userRoles: string[] }) {
  const isSuperAdministrator = userRoles.includes(roles.superAdministrator)
  const [tab, setTab] = useState(0)
  const [summary, setSummary] = useState<Summary | null>(null)
  const [permissions, setPermissions] = useState<Permission[]>([])
  const [error, setError] = useState('')

  useEffect(() => {
    if (!isSuperAdministrator) return
    void Promise.all([
      api.get<Summary>('/users/summary'),
      api.get<Permission[]>('/administration/permissions'),
    ]).then(([summaryResponse, permissionResponse]) => {
      setSummary(summaryResponse.data)
      setPermissions(permissionResponse.data)
    }).catch(() => setError('Staff and access information could not be loaded.'))
  }, [isSuperAdministrator])

  if (!isSuperAdministrator) {
    return <Box>
      <PageHeader />
      <Tabs value={tab} onChange={(_, value) => setTab(value)} sx={{ px: { xs: 2, sm: 3, lg: 4 }, borderBottom: 1, borderColor: 'divider' }}>
        <Tab label="Staff accounts" />
        <Tab label="Customer accounts" />
      </Tabs>
      {tab === 1 ? <CustomersPage administrationView /> : <UsersPage userRoles={userRoles} />}
    </Box>
  }

  const cards = summary ? [
    ['Staff accounts', summary.totalUsers],
    ['Active staff', summary.activeUsers],
    ['Locked or disabled', summary.lockedUsers + summary.disabledUsers],
    ['Password action required', summary.passwordChangeRequired],
  ] as const : []

  return <Box>
    <PageHeader />
    {error && <Alert severity="error" sx={{ mx: { xs: 2, sm: 3, lg: 4 }, mt: 2 }}>{error}</Alert>}
    <Tabs value={tab} onChange={(_, value) => setTab(value)} variant="scrollable" sx={{ px: { xs: 2, sm: 3, lg: 4 }, borderBottom: 1, borderColor: 'divider' }}>
      <Tab label="Overview" />
      <Tab label="Staff accounts" />
      <Tab label="Customer accounts" />
      <Tab label="Roles & permissions" />
    </Tabs>
    {tab === 0 && <Box sx={{ p: { xs: 2, sm: 3, lg: 4 } }}>
      <Grid container spacing={2}>{cards.map(([label, value]) => <Grid key={label} size={{ xs: 12, sm: 6, lg: 3 }}><Card variant="outlined"><CardContent><Typography color="text.secondary">{label}</Typography><Typography variant="h3" fontWeight={800}>{value}</Typography></CardContent></Card></Grid>)}</Grid>
      <Card variant="outlined" sx={{ mt: 3 }}><CardContent><Typography variant="h6" fontWeight={800}>Account administration</Typography><Typography color="text.secondary" mt={.5}>Create staff accounts, assign roles and operating scope, reset passwords, revoke sessions, or disable access when a staff member leaves.</Typography></CardContent></Card>
    </Box>}
    {tab === 1 && <UsersPage userRoles={userRoles} />}
    {tab === 2 && <CustomersPage administrationView />}
    {tab === 3 && <Box sx={{ p: { xs: 2, sm: 3, lg: 4 } }}><Typography variant="h5" fontWeight={800}>Roles and permissions</Typography><Typography color="text.secondary" mt={.5} mb={3}>Review the responsibilities and default access assigned to each staff role.</Typography><Grid container spacing={2}>{permissions.map(item => <Grid key={item.role} size={{ xs: 12, md: 6, xl: 4 }}><Card variant="outlined" sx={{ height: '100%' }}><CardContent><Stack direction="row" justifyContent="space-between" gap={1}><Typography variant="h6" fontWeight={800}>{formatRole(item.role)}</Typography><Chip size="small" label={item.scope} /></Stack><Stack component="ul" spacing={1} sx={{ pl: 2, mb: 0 }}>{item.permissions.map(permission => <Typography component="li" variant="body2" key={permission}>{permission}</Typography>)}</Stack></CardContent></Card></Grid>)}</Grid></Box>}
  </Box>
}

function PageHeader() {
  return <Stack direction="row" gap={1.5} alignItems="center" sx={{ px: { xs: 2, sm: 3, lg: 4 }, pt: 3, pb: 2 }}><ManageAccountsOutlined /><Box><Typography variant="h4" fontWeight={800}>Staff & access</Typography><Typography color="text.secondary">Manage staff, customer portal accounts, roles and access boundaries.</Typography></Box></Stack>
}
