import AddOutlined from '@mui/icons-material/AddOutlined'
import ArrowForwardOutlined from '@mui/icons-material/ArrowForwardOutlined'
import BuildCircleOutlined from '@mui/icons-material/BuildCircleOutlined'
import CalendarMonthOutlined from '@mui/icons-material/CalendarMonthOutlined'
import DirectionsCarOutlined from '@mui/icons-material/DirectionsCarOutlined'
import EventBusyOutlined from '@mui/icons-material/EventBusyOutlined'
import HandymanOutlined from '@mui/icons-material/HandymanOutlined'
import PeopleOutline from '@mui/icons-material/PeopleOutline'
import ReceiptLongOutlined from '@mui/icons-material/ReceiptLongOutlined'
import TrendingUpOutlined from '@mui/icons-material/TrendingUpOutlined'
import {
  Avatar,
  Box,
  Button,
  Card,
  CardContent,
  Chip,
  Divider,
  Grid,
  LinearProgress,
  Stack,
  Typography,
} from '@mui/material'

type DashboardPageProps = {
  userName: string
  onNavigate: (page: string) => void
}

const metrics = [
  { label: 'Available assets', value: '0', helper: 'Ready for rental', icon: <DirectionsCarOutlined />, accent: '#ffed00' },
  { label: 'Currently rented', value: '0', helper: 'Active agreements', icon: <ReceiptLongOutlined />, accent: '#d5c600' },
  { label: 'Under maintenance', value: '0', helper: 'Awaiting service', icon: <HandymanOutlined />, accent: '#a89d00' },
  { label: 'Overdue rentals', value: '0', helper: 'Require attention', icon: <EventBusyOutlined />, accent: '#111111' },
]

const utilization = [
  { label: 'Vehicles', value: 0 },
  { label: 'Equipment', value: 0 },
]

const quickActions = [
  { label: 'New booking', page: 'bookings', icon: <CalendarMonthOutlined /> },
  { label: 'Add asset', page: 'assets', icon: <DirectionsCarOutlined /> },
  { label: 'Add customer', page: 'customers', icon: <PeopleOutline /> },
  { label: 'Log maintenance', page: 'maintenance', icon: <BuildCircleOutlined /> },
]

function formatToday() {
  return new Intl.DateTimeFormat('en-FJ', {
    weekday: 'long',
    day: 'numeric',
    month: 'long',
    year: 'numeric',
  }).format(new Date())
}

export function DashboardPage({ userName, onNavigate }: DashboardPageProps) {
  const firstName = userName.trim().split(/\s+/)[0] || 'there'

  return (
    <Box sx={{ p: { xs: 2, sm: 3, lg: 4 }, maxWidth: 1500, mx: 'auto' }}>
      <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" alignItems={{ md: 'flex-end' }} gap={2} mb={3.5}>
        <Box>
          <Typography variant="overline" color="text.secondary" fontWeight={700} letterSpacing={1.2}>
            {formatToday()}
          </Typography>
          <Typography variant="h4" fontWeight={750} mt={0.25}>Good day, {firstName}</Typography>
          <Typography color="text.secondary" mt={0.5}>Here is what is happening across rental operations.</Typography>
        </Box>
        <Stack direction="row" spacing={1.25}>
          <Button variant="outlined" color="primary" startIcon={<AddOutlined />} onClick={() => onNavigate('customers')}>
            Customer
          </Button>
          <Button variant="contained" color="primary" startIcon={<AddOutlined />} onClick={() => onNavigate('bookings')}>
            New booking
          </Button>
        </Stack>
      </Stack>

      <Grid container spacing={2.5}>
        {metrics.map((metric) => (
          <Grid key={metric.label} size={{ xs: 12, sm: 6, xl: 3 }}>
            <Card variant="outlined" sx={{ height: '100%', position: 'relative', overflow: 'hidden' }}>
              <Box sx={{ position: 'absolute', top: 0, left: 0, right: 0, height: 4, bgcolor: metric.accent }} />
              <CardContent sx={{ p: 2.5 }}>
                <Stack direction="row" justifyContent="space-between" alignItems="flex-start">
                  <Box>
                    <Typography color="text.secondary" variant="body2" fontWeight={500}>{metric.label}</Typography>
                    <Typography variant="h3" fontWeight={750} mt={0.75}>{metric.value}</Typography>
                    <Typography color="text.secondary" variant="caption">{metric.helper}</Typography>
                  </Box>
                  <Avatar variant="rounded" sx={{ bgcolor: metric.accent, color: metric.accent === '#111111' ? 'white' : '#111111', width: 46, height: 46 }}>
                    {metric.icon}
                  </Avatar>
                </Stack>
              </CardContent>
            </Card>
          </Grid>
        ))}
      </Grid>

      <Grid container spacing={2.5} mt={0}>
        <Grid size={{ xs: 12, lg: 8 }}>
          <Card variant="outlined" sx={{ height: '100%' }}>
            <CardContent sx={{ p: { xs: 2.5, sm: 3 } }}>
              <Stack direction="row" justifyContent="space-between" alignItems="flex-start" mb={3}>
                <Box>
                  <Typography variant="h6" fontWeight={700}>Fleet utilization</Typography>
                  <Typography variant="body2" color="text.secondary">Share of active assets currently rented</Typography>
                </Box>
                <Chip size="small" icon={<TrendingUpOutlined />} label="This month" variant="outlined" />
              </Stack>

              <Stack spacing={3}>
                {utilization.map((item) => (
                  <Box key={item.label}>
                    <Stack direction="row" justifyContent="space-between" mb={1}>
                      <Typography variant="body2" fontWeight={600}>{item.label}</Typography>
                      <Typography variant="body2" fontWeight={700}>{item.value}%</Typography>
                    </Stack>
                    <LinearProgress
                      variant="determinate"
                      value={item.value}
                      sx={{ height: 10, borderRadius: 8, bgcolor: '#eeeeea', '& .MuiLinearProgress-bar': { bgcolor: 'secondary.main', borderRadius: 8 } }}
                    />
                  </Box>
                ))}
              </Stack>

              <Box sx={{ mt: 3.5, p: 2, borderRadius: 2, bgcolor: '#f6f6f3', display: 'flex', alignItems: 'center', gap: 1.5 }}>
                <Avatar sx={{ bgcolor: 'secondary.main', color: 'secondary.contrastText', width: 38, height: 38 }}><DirectionsCarOutlined fontSize="small" /></Avatar>
                <Box>
                  <Typography variant="body2" fontWeight={650}>No assets recorded yet</Typography>
                  <Typography variant="caption" color="text.secondary">Add your fleet to begin tracking utilization and availability.</Typography>
                </Box>
                <Button size="small" sx={{ ml: 'auto', whiteSpace: 'nowrap' }} endIcon={<ArrowForwardOutlined />} onClick={() => onNavigate('assets')}>Add asset</Button>
              </Box>
            </CardContent>
          </Card>
        </Grid>

        <Grid size={{ xs: 12, lg: 4 }}>
          <Card variant="outlined" sx={{ height: '100%' }}>
            <CardContent sx={{ p: { xs: 2.5, sm: 3 } }}>
              <Typography variant="h6" fontWeight={700}>Quick actions</Typography>
              <Typography variant="body2" color="text.secondary" mb={2.25}>Start a common task</Typography>
              <Stack divider={<Divider flexItem />}>
                {quickActions.map((action) => (
                  <Button
                    key={action.label}
                    color="inherit"
                    startIcon={action.icon}
                    endIcon={<ArrowForwardOutlined />}
                    onClick={() => onNavigate(action.page)}
                    sx={{ justifyContent: 'flex-start', py: 1.55, px: 0.5, borderRadius: 0, '& .MuiButton-endIcon': { ml: 'auto' } }}
                  >
                    {action.label}
                  </Button>
                ))}
              </Stack>
            </CardContent>
          </Card>
        </Grid>
      </Grid>

      <Grid container spacing={2.5} mt={0}>
        <Grid size={{ xs: 12, lg: 7 }}>
          <Card variant="outlined">
            <CardContent sx={{ p: { xs: 2.5, sm: 3 } }}>
              <Stack direction="row" justifyContent="space-between" alignItems="center" mb={2.5}>
                <Box>
                  <Typography variant="h6" fontWeight={700}>Upcoming bookings</Typography>
                  <Typography variant="body2" color="text.secondary">Next scheduled collections</Typography>
                </Box>
                <Button size="small" endIcon={<ArrowForwardOutlined />} onClick={() => onNavigate('bookings')}>View all</Button>
              </Stack>
              <EmptyPanel icon={<CalendarMonthOutlined />} title="No upcoming bookings" description="Confirmed reservations will appear here in collection order." />
            </CardContent>
          </Card>
        </Grid>

        <Grid size={{ xs: 12, lg: 5 }}>
          <Card variant="outlined">
            <CardContent sx={{ p: { xs: 2.5, sm: 3 } }}>
              <Stack direction="row" justifyContent="space-between" alignItems="center" mb={2.5}>
                <Box>
                  <Typography variant="h6" fontWeight={700}>Maintenance attention</Typography>
                  <Typography variant="body2" color="text.secondary">Due and overdue service items</Typography>
                </Box>
                <Button size="small" endIcon={<ArrowForwardOutlined />} onClick={() => onNavigate('maintenance')}>View all</Button>
              </Stack>
              <EmptyPanel icon={<HandymanOutlined />} title="Nothing requires attention" description="Maintenance reminders and open requests will appear here." />
            </CardContent>
          </Card>
        </Grid>
      </Grid>
    </Box>
  )
}

function EmptyPanel({ icon, title, description }: { icon: React.ReactNode; title: string; description: string }) {
  return (
    <Box sx={{ minHeight: 155, display: 'grid', placeItems: 'center', textAlign: 'center', bgcolor: '#fafaf8', border: '1px dashed', borderColor: '#d8d8d2', borderRadius: 2.5, px: 2 }}>
      <Box>
        <Avatar sx={{ mx: 'auto', mb: 1.25, bgcolor: 'secondary.main', color: 'secondary.contrastText' }}>{icon}</Avatar>
        <Typography variant="body2" fontWeight={650}>{title}</Typography>
        <Typography variant="caption" color="text.secondary">{description}</Typography>
      </Box>
    </Box>
  )
}
