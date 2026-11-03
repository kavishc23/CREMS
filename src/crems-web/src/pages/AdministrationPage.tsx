import { useRef, useState } from 'react'
import ManageAccountsOutlined from '@mui/icons-material/ManageAccountsOutlined'
import AddOutlined from '@mui/icons-material/AddOutlined'
import { Box, Button, Tab, Tabs } from '@mui/material'
import { roles } from '../auth/access'
import { PageHeader } from '../components/PageHeader'
import { MaintenanceDirectoryPage } from './MaintenanceDirectoryPage'
import { UsersPage } from './UsersPage'
import { StaffRolesPage } from './StaffRolesPage'

export function AdministrationPage({ userRoles }: { userRoles: string[] }) {
  const isSuperAdministrator = userRoles.includes(roles.superAdministrator)
  const [tab, setTab] = useState(0)
  const staffEditor = useRef<{ openCreate: () => void }>(null)
  const [roleRevision, setRoleRevision] = useState(0)
  return <Box>
    <Box sx={{ px: { xs: 2, sm: 3, lg: 4 }, pt: 3 }}><PageHeader icon={<ManageAccountsOutlined />} title="Staff & Access" subtitle="Manage staff accounts, operating scope, security and role permissions." actions={<Button variant="contained" startIcon={<AddOutlined />} onClick={() => { setTab(0); staffEditor.current?.openCreate() }}>Create staff account</Button>} /></Box>
    {isSuperAdministrator && <Tabs value={tab} onChange={(_, value) => { setTab(value) }} variant="scrollable" sx={{ px: { xs: 2, sm: 3, lg: 4 }, borderBottom: 1, borderColor: 'divider' }}>
      <Tab label="Staff accounts" /><Tab label="Roles & permissions" /><Tab label="Technicians & suppliers" />
    </Tabs>}
    <Box hidden={tab !== 0}><UsersPage ref={staffEditor} userRoles={userRoles} embedded roleRevision={roleRevision} /></Box>
    {tab === 1 && <StaffRolesPage onChanged={() => setRoleRevision(value => value + 1)} />}
    {tab === 2 && <MaintenanceDirectoryPage />}
  </Box>
}
