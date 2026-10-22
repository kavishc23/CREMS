import MonitorHeartOutlined from '@mui/icons-material/MonitorHeartOutlined'
import { Box } from '@mui/material'
import { PageHeader } from '../components/PageHeader'
import { SystemConfigurationSection } from './SystemConfigurationPage'

export function SystemHealthPage() {
  return <Box><Box sx={{ px: { xs: 2, sm: 3, lg: 4 }, pt: 3 }}><PageHeader icon={<MonitorHeartOutlined />} title="System health & audit" subtitle="Monitor service health and recent administrator activity." dense /></Box><SystemConfigurationSection section="health" /></Box>
}
