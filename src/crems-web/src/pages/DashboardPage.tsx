import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import ArrowForwardOutlined from '@mui/icons-material/ArrowForwardOutlined'
import AssessmentOutlined from '@mui/icons-material/AssessmentOutlined'
import BuildCircleOutlined from '@mui/icons-material/BuildCircleOutlined'
import CalendarMonthOutlined from '@mui/icons-material/CalendarMonthOutlined'
import DirectionsCarOutlined from '@mui/icons-material/DirectionsCarOutlined'
import EventBusyOutlined from '@mui/icons-material/EventBusyOutlined'
import ManageAccountsOutlined from '@mui/icons-material/ManageAccountsOutlined'
import QrCodeScannerOutlined from '@mui/icons-material/QrCodeScannerOutlined'
import ReceiptLongOutlined from '@mui/icons-material/ReceiptLongOutlined'
import SettingsOutlined from '@mui/icons-material/SettingsOutlined'
import TrendingUpOutlined from '@mui/icons-material/TrendingUpOutlined'
import { Alert, Avatar, Box, Button, Card, CardContent, Chip, Grid, LinearProgress, Skeleton, Stack, Typography } from '@mui/material'
import { canAccessPage, formatRole, getPrimaryRole, roles, type AppPage } from '../auth/access'
import { api } from '../api/client'
import { isPageEnabledForDemo } from '../config/demoMode'

type DashboardPageProps = { userName: string; userRoles: string[]; onNavigate: (page: AppPage) => void }
type DashboardSummary = {
  availableAssets: number; activeRentals: number; underMaintenance: number; overdueRentals: number
  pendingRequests: number; upcomingBookings: number; servicesDueSoon: number
  vehicleUtilization: number; equipmentUtilization: number; totalAssets: number
}
type MetricKey = keyof DashboardSummary
type Action = { label: string; description: string; page: AppPage; icon: ReactNode }
type RoleView = {
  eyebrow: string; title: string; description: string
  metricKeys: MetricKey[]; actions: Action[]; showUtilization?: boolean
}

const emptySummary: DashboardSummary = { availableAssets: 0, activeRentals: 0, underMaintenance: 0, overdueRentals: 0, pendingRequests: 0, upcomingBookings: 0, servicesDueSoon: 0, vehicleUtilization: 0, equipmentUtilization: 0, totalAssets: 0 }
const metricDetails: Record<MetricKey, { label: string; helper: string; icon: ReactNode; tone: string }> = {
  availableAssets: { label: 'Assets ready', helper: 'Available for allocation', icon: <DirectionsCarOutlined />, tone: '#ffed00' },
  activeRentals: { label: 'Currently on hire', helper: 'Active customer rentals', icon: <ReceiptLongOutlined />, tone: '#d8ca00' },
  underMaintenance: { label: 'In maintenance', helper: 'Unavailable during service', icon: <BuildCircleOutlined />, tone: '#f3a712' },
  overdueRentals: { label: 'Overdue returns', helper: 'Require follow-up', icon: <EventBusyOutlined />, tone: '#d14343' },
  pendingRequests: { label: 'Requests to review', helper: 'Awaiting staff action', icon: <CalendarMonthOutlined />, tone: '#ffed00' },
  upcomingBookings: { label: 'Upcoming collections', helper: 'Confirmed future bookings', icon: <CalendarMonthOutlined />, tone: '#d8ca00' },
  servicesDueSoon: { label: 'Services due soon', helper: 'Due within 30 days', icon: <BuildCircleOutlined />, tone: '#f3a712' },
  vehicleUtilization: { label: 'Vehicle utilization', helper: 'Currently hired out', icon: <TrendingUpOutlined />, tone: '#ffed00' },
  equipmentUtilization: { label: 'Equipment utilization', helper: 'Currently hired out', icon: <TrendingUpOutlined />, tone: '#d8ca00' },
  totalAssets: { label: 'Assets in scope', helper: 'Active register records', icon: <DirectionsCarOutlined />, tone: '#ffed00' },
}

const actions = {
  bookings: { label: 'Review booking requests', description: 'Confirm customers, dates and asset availability.', page: 'bookings', icon: <CalendarMonthOutlined /> },
  rentals: { label: 'Manage pickups and returns', description: 'Prepare agreements, check out and receive assets.', page: 'rentals', icon: <ReceiptLongOutlined /> },
  assets: { label: 'Open asset register', description: 'Find an asset and review its operational profile.', page: 'assets', icon: <DirectionsCarOutlined /> },
  maintenance: { label: 'Manage maintenance', description: 'Record faults, servicing, costs and completion.', page: 'maintenance', icon: <BuildCircleOutlined /> },
  scan: { label: 'Scan an asset QR', description: 'Start a field check-in, check-out or inspection.', page: 'scan', icon: <QrCodeScannerOutlined /> },
  reports: { label: 'Open management reports', description: 'Review utilization, profitability and trends.', page: 'reports', icon: <AssessmentOutlined /> },
  users: { label: 'Manage staff access', description: 'Review users, roles and security controls.', page: 'users', icon: <ManageAccountsOutlined /> },
  divisions: { label: 'Configure organization', description: 'Manage divisions, services and operational settings.', page: 'divisions', icon: <SettingsOutlined /> },
} satisfies Record<string, Action>

const roleViews: Record<string, RoleView> = {
  [roles.superAdministrator]: { eyebrow: 'Group administration', title: 'System and group overview', description: 'Monitor group operations, then move into configuration or access only when required.', metricKeys: ['totalAssets', 'activeRentals', 'overdueRentals'], actions: [actions.users, actions.divisions, actions.reports], showUtilization: true },
  [roles.administrator]: { eyebrow: 'Administration', title: 'Operations and access overview', description: 'Review group-wide activity and handle the administrative work requiring attention.', metricKeys: ['totalAssets', 'pendingRequests', 'overdueRentals'], actions: [actions.users, actions.reports, actions.assets], showUtilization: true },
  [roles.branchManager]: { eyebrow: 'Branch management', title: 'Your branch at a glance', description: 'Focus on exceptions, fleet use and work waiting for branch approval.', metricKeys: ['activeRentals', 'overdueRentals', 'underMaintenance'], actions: [actions.bookings, actions.maintenance, actions.reports], showUtilization: true },
  [roles.rentalOfficer]: { eyebrow: 'Rental desk', title: 'Today’s rental work', description: 'Work through requests, collections and returns without management-only information.', metricKeys: ['pendingRequests', 'upcomingBookings', 'overdueRentals'], actions: [actions.bookings, actions.rentals, actions.assets] },
  [roles.maintenanceOfficer]: { eyebrow: 'Workshop', title: 'Maintenance work today', description: 'Prioritize unavailable assets and services approaching their due date.', metricKeys: ['underMaintenance', 'servicesDueSoon', 'availableAssets'], actions: [actions.maintenance, actions.scan, actions.assets] },
  [roles.financeOfficer]: { eyebrow: 'Finance', title: 'Financial reporting workspace', description: 'Move directly into asset performance and management reporting for your assigned scope.', metricKeys: ['activeRentals', 'totalAssets', 'overdueRentals'], actions: [actions.reports, actions.assets] },
  [roles.driver]: { eyebrow: 'Field operations', title: 'Your field tools', description: 'Identify assets quickly and complete the required handover or inspection workflow.', metricKeys: ['activeRentals', 'upcomingBookings', 'overdueRentals'], actions: [actions.scan, actions.assets] },
}

function formatToday() {
  return new Intl.DateTimeFormat('en-FJ', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' }).format(new Date())
}

export function DashboardPage({ userName, userRoles, onNavigate }: DashboardPageProps) {
  const [summary, setSummary] = useState<DashboardSummary>(emptySummary)
  const [loading, setLoading] = useState(true)
  const [loadFailed, setLoadFailed] = useState(false)
  const loadSummary = useCallback(async () => {
    setLoading(true); setLoadFailed(false)
    try { setSummary((await api.get<DashboardSummary>('/dashboard/summary')).data) }
    catch { setLoadFailed(true) }
    finally { setLoading(false) }
  }, [])
  useEffect(() => { void loadSummary() }, [loadSummary])

  const primaryRole = getPrimaryRole(userRoles)
  const view = roleViews[primaryRole] ?? roleViews[roles.rentalOfficer]
  const availableActions = useMemo(() => view.actions.filter(action => canAccessPage(userRoles, action.page) && isPageEnabledForDemo(action.page)).slice(0, 3), [userRoles, view.actions])
  const firstName = userName.trim().split(/\s+/)[0] || 'there'
  const calculatedPriority = getPriority(primaryRole, summary)
  const priority = isPageEnabledForDemo(calculatedPriority.page)
    ? calculatedPriority
    : { title: 'Your asset register is ready to review', description: 'Explore the assets available within your assigned division and branch.', page: 'assets' as AppPage, button: 'Open asset register' }

  return <Box sx={{ p: { xs: 2.5, sm: 4, lg: 5 }, maxWidth: 1380, mx: 'auto' }}>
    <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" alignItems={{ md: 'flex-end' }} gap={2} mb={3}>
      <Box>
        <Stack direction="row" gap={1} alignItems="center" mb={.75}>
          <Typography variant="overline" fontWeight={800} letterSpacing={1.2} color="text.secondary">{view.eyebrow}</Typography>
          <Chip size="small" label={formatRole(primaryRole)} sx={{ fontWeight: 750, bgcolor: 'secondary.main' }} />
        </Stack>
        <Typography variant="h4" fontWeight={850}>{view.title}</Typography>
        <Typography color="text.secondary" mt={.5}>{formatToday()} · Welcome, {firstName}</Typography>
      </Box>
      {availableActions[0] && <Button variant="contained" endIcon={<ArrowForwardOutlined />} onClick={() => onNavigate(availableActions[0].page)}>{availableActions[0].label}</Button>}
    </Stack>

    {loadFailed && <Alert severity="warning" action={<Button color="inherit" size="small" onClick={() => void loadSummary()}>Retry</Button>} sx={{ mb: 2 }}>Live dashboard figures are temporarily unavailable.</Alert>}

    <Grid container spacing={{ xs: 2.5, md: 3.5 }}>
      <Grid size={{ xs: 12, lg: 8 }}>
        <Card sx={{ bgcolor: '#111', color: 'white', border: 0, minHeight: 190 }}>
          <CardContent sx={{ p: { xs: 3.5, md: 4.5 } }}>
            <Typography variant="overline" sx={{ color: 'secondary.main', fontWeight: 850, letterSpacing: 1.1 }}>Priority</Typography>
            {loading ? <><Skeleton variant="text" width="60%" height={48} sx={{ bgcolor: 'rgba(255,255,255,.12)' }} /><Skeleton variant="text" width="80%" sx={{ bgcolor: 'rgba(255,255,255,.08)' }} /></> : <>
              <Typography variant="h5" fontWeight={850} mt={.5}>{priority.title}</Typography>
              <Typography sx={{ color: 'rgba(255,255,255,.68)', mt: 1, maxWidth: 720 }}>{priority.description}</Typography>
              <Button variant="contained" sx={{ mt: 2.5 }} endIcon={<ArrowForwardOutlined />} onClick={() => onNavigate(priority.page)}>{priority.button}</Button>
            </>}
          </CardContent>
        </Card>
      </Grid>
      <Grid size={{ xs: 12, lg: 4 }}>
        <Card variant="outlined" sx={{ height: '100%' }}><CardContent sx={{ p: { xs: 3, md: 3.5 } }}>
          <Typography variant="h6" fontWeight={800}>Your focus</Typography>
          <Typography variant="body2" color="text.secondary" mt={.5}>{view.description}</Typography>
          <Stack direction="row" gap={.75} mt={2.5} flexWrap="wrap"><Chip size="small" label="Your assigned scope" /><Chip size="small" label="Live operational data" /></Stack>
        </CardContent></Card>
      </Grid>
    </Grid>

    <Grid container spacing={{ xs: 2.5, md: 3.5 }} sx={{ mt: { xs: 1, md: 1.5 } }}>
      {view.metricKeys.map(key => <Grid key={key} size={{ xs: 12, sm: 4 }}><MetricCard metricKey={key} value={summary[key]} loading={loading} /></Grid>)}
    </Grid>

    <Grid container spacing={{ xs: 2.5, md: 3.5 }} sx={{ mt: { xs: 1, md: 1.5 }, pb: 2 }}>
      <Grid size={{ xs: 12, lg: view.showUtilization ? 7 : 12 }}>
        <Card variant="outlined"><CardContent sx={{ p: { xs: 3, md: 3.5 } }}>
          <Typography variant="h6" fontWeight={800}>Common tasks</Typography>
          <Typography variant="body2" color="text.secondary" mb={2.25}>Only actions available to your role are shown.</Typography>
          <Grid container spacing={2}>{availableActions.map(action => <Grid key={action.label} size={{ xs: 12, md: availableActions.length === 2 ? 6 : 4 }}><Button color="inherit" onClick={() => onNavigate(action.page)} sx={{ width: '100%', height: '100%', minHeight: 128, alignItems: 'flex-start', justifyContent: 'flex-start', textAlign: 'left', p: 2.5, border: 1, borderColor: 'divider', borderRadius: 2.5 }}><Stack alignItems="flex-start" gap={1.25}><Avatar sx={{ bgcolor: 'secondary.main', color: '#111', width: 40, height: 40 }}>{action.icon}</Avatar><Box><Typography fontWeight={800}>{action.label}</Typography><Typography variant="caption" color="text.secondary">{action.description}</Typography></Box></Stack></Button></Grid>)}</Grid>
        </CardContent></Card>
      </Grid>
      {view.showUtilization && <Grid size={{ xs: 12, lg: 5 }}><Utilization summary={summary} loading={loading} /></Grid>}
    </Grid>
  </Box>
}

function MetricCard({ metricKey, value, loading }: { metricKey: MetricKey; value: number; loading: boolean }) {
  const detail = metricDetails[metricKey]
  const percentage = metricKey === 'vehicleUtilization' || metricKey === 'equipmentUtilization'
  return <Card variant="outlined" sx={{ height: '100%', borderTop: 4, borderTopColor: detail.tone }}><CardContent sx={{ p: { xs: 3, md: 3.25 } }}><Stack direction="row" justifyContent="space-between" gap={2.5}><Box><Typography variant="body2" color="text.secondary">{detail.label}</Typography>{loading ? <Skeleton width={72} height={52} /> : <Typography variant="h3" fontWeight={850} mt={.5}>{value}{percentage ? '%' : ''}</Typography>}<Typography variant="caption" color="text.secondary">{detail.helper}</Typography></Box><Avatar variant="rounded" sx={{ bgcolor: detail.tone, color: detail.tone === '#d14343' ? 'white' : '#111', width: 46, height: 46 }}>{detail.icon}</Avatar></Stack></CardContent></Card>
}

function Utilization({ summary, loading }: { summary: DashboardSummary; loading: boolean }) {
  return <Card variant="outlined" sx={{ height: '100%' }}><CardContent sx={{ p: { xs: 3, md: 3.5 } }}><Typography variant="h6" fontWeight={800}>Utilization</Typography><Typography variant="body2" color="text.secondary" mb={3.5}>Assets currently on hire within your scope.</Typography><Stack gap={3}>{[['Vehicles', summary.vehicleUtilization], ['Equipment', summary.equipmentUtilization]].map(([label, value]) => <Box key={String(label)}><Stack direction="row" justifyContent="space-between" mb={1}><Typography variant="body2" fontWeight={700}>{label}</Typography><Typography variant="body2" fontWeight={800}>{loading ? '—' : `${value}%`}</Typography></Stack><LinearProgress variant="determinate" value={loading ? 0 : Number(value)} sx={{ height: 9, borderRadius: 8, bgcolor: '#eeeeea', '& .MuiLinearProgress-bar': { bgcolor: 'secondary.main', borderRadius: 8 } }} /></Box>)}</Stack></CardContent></Card>
}

function getPriority(role: string, summary: DashboardSummary): { title: string; description: string; page: AppPage; button: string } {
  if (role === roles.maintenanceOfficer) return summary.underMaintenance > 0
    ? { title: `${summary.underMaintenance} asset${summary.underMaintenance === 1 ? '' : 's'} currently in maintenance`, description: `${summary.servicesDueSoon} additional services are due within 30 days. Review work status and return completed assets to service.`, page: 'maintenance', button: 'Open maintenance' }
    : { title: 'No assets are currently in maintenance', description: `${summary.servicesDueSoon} services are approaching their due date.`, page: 'maintenance', button: 'Review service schedule' }
  if (role === roles.driver) return { title: 'Start by scanning the assigned asset', description: 'Confirm the asset identity before recording a handover, return, meter reading or inspection.', page: 'scan', button: 'Scan QR' }
  if (role === roles.financeOfficer) return { title: 'Review asset profitability and exceptions', description: `${summary.activeRentals} active rentals are contributing to the current operating position.`, page: 'reports', button: 'Open reports' }
  if (summary.overdueRentals > 0) return { title: `${summary.overdueRentals} overdue return${summary.overdueRentals === 1 ? '' : 's'} need attention`, description: 'Contact the customer, confirm the asset position and record the agreed return arrangement.', page: 'rentals', button: 'Review overdue rentals' }
  if (summary.pendingRequests > 0) return { title: `${summary.pendingRequests} booking request${summary.pendingRequests === 1 ? '' : 's'} waiting`, description: 'Confirm the customer, branch, dates and asset before approving the request.', page: 'bookings', button: 'Review requests' }
  if (role === roles.superAdministrator || role === roles.administrator) return { title: 'Operations are within normal thresholds', description: 'No overdue rental or request requires immediate group-level intervention.', page: 'reports', button: 'Review reports' }
  return { title: `${summary.upcomingBookings} upcoming collection${summary.upcomingBookings === 1 ? '' : 's'}`, description: 'Prepare customer details, the assigned asset and the rental agreement before collection.', page: 'bookings', button: 'Open booking schedule' }
}
