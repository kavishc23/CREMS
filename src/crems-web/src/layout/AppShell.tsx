import { type ReactNode, useState } from 'react'
import DashboardOutlined from '@mui/icons-material/DashboardOutlined'
import DirectionsCarOutlined from '@mui/icons-material/DirectionsCarOutlined'
import EventAvailableOutlined from '@mui/icons-material/EventAvailableOutlined'
import HandymanOutlined from '@mui/icons-material/HandymanOutlined'
import PeopleOutline from '@mui/icons-material/PeopleOutline'
import ReceiptLongOutlined from '@mui/icons-material/ReceiptLongOutlined'
import AssessmentOutlined from '@mui/icons-material/AssessmentOutlined'
import ManageAccountsOutlined from '@mui/icons-material/ManageAccountsOutlined'
import StorefrontOutlined from '@mui/icons-material/StorefrontOutlined'
import MenuIcon from '@mui/icons-material/Menu'
import LogoutOutlined from '@mui/icons-material/LogoutOutlined'
import ChevronLeftOutlined from '@mui/icons-material/ChevronLeftOutlined'
import ChevronRightOutlined from '@mui/icons-material/ChevronRightOutlined'
import {
  AppBar,
  Box,
  Drawer,
  IconButton,
  List,
  ListItemButton,
  ListItemIcon,
  ListItemText,
  Toolbar,
  Tooltip,
  Typography,
  useMediaQuery,
  useTheme,
  Chip,
} from '@mui/material'
import { canAccessPage, formatRole, getPrimaryRole, type AppPage } from '../auth/access'
const drawerWidth = 248
const collapsedDrawerWidth = 76
const navigation = [
  { label: 'Dashboard', id: 'dashboard', icon: <DashboardOutlined /> },
  { label: 'Assets', id: 'assets', icon: <DirectionsCarOutlined /> },
  { label: 'Customers', id: 'customers', icon: <PeopleOutline /> },
  { label: 'Bookings', id: 'bookings', icon: <EventAvailableOutlined /> },
  { label: 'Rentals', id: 'rentals', icon: <ReceiptLongOutlined /> },
  { label: 'Maintenance', id: 'maintenance', icon: <HandymanOutlined /> },
  { label: 'Reports', id: 'reports', icon: <AssessmentOutlined /> },
  { label: 'Users & roles', id: 'users', icon: <ManageAccountsOutlined /> },
  { label: 'Branches', id: 'branches', icon: <StorefrontOutlined /> },
] satisfies { label: string; id: AppPage; icon: ReactNode }[]

type AppShellProps = {
  activePage: string
  onNavigate: (page: AppPage) => void
  userName: string
  userRoles: string[]
  onLogout: () => Promise<void>
  children: ReactNode
}

export function AppShell({ activePage, onNavigate, userName, userRoles, onLogout, children }: AppShellProps) {
  const theme = useTheme()
  const desktop = useMediaQuery(theme.breakpoints.up('md'))
  const [open, setOpen] = useState(false)
  const [collapsed, setCollapsed] = useState(false)
  const activeDrawerWidth = desktop && collapsed ? collapsedDrawerWidth : drawerWidth

  const drawer = (
    <Box sx={{ height: '100%', bgcolor: '#090909', color: 'white' }}>
      <Toolbar sx={{ px: collapsed && desktop ? 2 : 2.5, minHeight: 76, justifyContent: collapsed && desktop ? 'center' : 'flex-start' }}>
        <Box component="img" src="/brand/carpenters-logo.png" alt="Carpenters Fiji" sx={{ width: 44, height: 44, objectFit: 'cover', mr: collapsed && desktop ? 0 : 1.5 }} />
        <Box sx={{ display: collapsed && desktop ? 'none' : 'block' }}>
          <Typography variant="h6" fontWeight={700} lineHeight={1.1}>CREMS</Typography>
          <Typography variant="caption" sx={{ color: 'secondary.main' }}>Carpenters Fiji</Typography>
        </Box>
      </Toolbar>
      <List sx={{ px: 1.5, pt: 2 }}>
        {navigation.filter((item) => canAccessPage(userRoles, item.id)).map((item) => {
          const button = (
            <ListItemButton
              key={item.id}
              selected={activePage === item.id}
              aria-label={item.label}
              onClick={() => {
                onNavigate(item.id)
                setOpen(false)
              }}
              sx={{
                mb: 0.5,
                minHeight: 48,
                px: collapsed && desktop ? 1.5 : 2,
                justifyContent: collapsed && desktop ? 'center' : 'flex-start',
                borderRadius: 2,
                color: 'rgba(255,255,255,.72)',
                '&.Mui-selected': { bgcolor: 'secondary.main', color: 'secondary.contrastText' },
                '&.Mui-selected:hover': { bgcolor: 'secondary.dark' },
                '&:hover': { bgcolor: 'rgba(255,255,255,.08)' },
              }}
            >
              <ListItemIcon sx={{ color: 'inherit', minWidth: collapsed && desktop ? 0 : 40, justifyContent: 'center' }}>{item.icon}</ListItemIcon>
              <ListItemText primary={item.label} sx={{ display: collapsed && desktop ? 'none' : 'block' }} />
            </ListItemButton>
          )

          return collapsed && desktop ? (
            <Tooltip key={item.id} title={item.label} placement="right" arrow>
              {button}
            </Tooltip>
          ) : button
        })}
      </List>
    </Box>
  )

  return (
    <Box sx={{ display: 'flex', minHeight: '100vh' }}>
      <AppBar
        position="fixed"
        color="inherit"
        elevation={0}
        sx={{
          borderBottom: 1,
          borderColor: 'divider',
          ml: { md: `${activeDrawerWidth}px` },
          width: { md: `calc(100% - ${activeDrawerWidth}px)` },
          transition: theme.transitions.create(['margin-left', 'width'], { duration: theme.transitions.duration.shorter }),
        }}
      >
        <Toolbar>
          {!desktop && <IconButton onClick={() => setOpen(true)} sx={{ mr: 1 }}><MenuIcon /></IconButton>}
          {desktop && (
            <Tooltip title={collapsed ? 'Expand navigation' : 'Collapse navigation'}>
              <IconButton
                aria-label={collapsed ? 'Expand navigation' : 'Collapse navigation'}
                onClick={() => setCollapsed((value) => !value)}
                sx={{ mr: 1 }}
              >
                {collapsed ? <ChevronRightOutlined /> : <ChevronLeftOutlined />}
              </IconButton>
            </Tooltip>
          )}
          <Typography variant="subtitle1" fontWeight={600}>Rental Operations</Typography>
          <Box sx={{ flexGrow: 1 }} />
          <Typography variant="body2" color="text.secondary" sx={{ display: { xs: 'none', sm: 'block' } }}>{userName}</Typography>
          <Chip
            label={formatRole(getPrimaryRole(userRoles))}
            size="small"
            sx={{ ml: 1.5, display: { xs: 'none', md: 'flex' }, bgcolor: 'secondary.main', fontWeight: 600 }}
          />
          <IconButton aria-label="Sign out" onClick={() => void onLogout()} sx={{ ml: 1 }}>
            <LogoutOutlined />
          </IconButton>
        </Toolbar>
      </AppBar>
      <Drawer
        variant={desktop ? 'permanent' : 'temporary'}
        open={desktop || open}
        onClose={() => setOpen(false)}
        sx={{
          width: { md: activeDrawerWidth },
          flexShrink: { md: 0 },
          transition: theme.transitions.create('width', { duration: theme.transitions.duration.shorter }),
          '& .MuiDrawer-paper': {
            width: activeDrawerWidth,
            border: 0,
            boxSizing: 'border-box',
            overflowX: 'hidden',
            transition: theme.transitions.create('width', { duration: theme.transitions.duration.shorter }),
          },
        }}
      >
        {drawer}
      </Drawer>
      <Box
        component="main"
        sx={{
          flexGrow: 1,
          minWidth: 0,
          width: { xs: '100%', md: `calc(100% - ${activeDrawerWidth}px)` },
          pt: { xs: '56px', sm: '64px' },
          transition: theme.transitions.create('width', { duration: theme.transitions.duration.shorter }),
        }}
      >
        {children}
      </Box>
    </Box>
  )
}
