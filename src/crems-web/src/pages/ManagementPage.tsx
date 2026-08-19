import { useState } from 'react'
import { Box, Tab, Tabs } from '@mui/material'
import { CorporateOperationsPage } from './CorporateOperationsPage'
import { ReportsPage } from './ReportsPage'
import { PlanningCalendarPage } from './PlanningCalendarPage'

export function ManagementPage() {
  const [tab, setTab] = useState(0)
  return <Box><Tabs value={tab} onChange={(_, value) => setTab(value)} sx={{ px: { xs: 2, sm: 3, lg: 4 }, pt: 2, borderBottom: 1, borderColor: 'divider' }}><Tab label="Daily operations" /><Tab label="Availability" /><Tab label="Reports" /></Tabs>{tab === 0 ? <CorporateOperationsPage /> : tab===1?<PlanningCalendarPage/>:<ReportsPage />}</Box>
}
