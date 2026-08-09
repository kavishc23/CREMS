import { useState } from 'react'
import { Box, Tab, Tabs } from '@mui/material'
import { AssetsPage } from './AssetsPage'
import { MaintenancePage } from './MaintenancePage'

export function FleetPage({ userRoles }: { userRoles: string[] }) {
  const [tab, setTab] = useState(0)
  return <Box><Tabs value={tab} onChange={(_, value) => setTab(value)} sx={{ px: { xs: 2, sm: 3, lg: 4 }, pt: 2, borderBottom: 1, borderColor: 'divider' }}><Tab label="Vehicles & equipment" /><Tab label="Maintenance" /></Tabs>{tab === 0 ? <AssetsPage userRoles={userRoles} /> : <MaintenancePage />}</Box>
}
