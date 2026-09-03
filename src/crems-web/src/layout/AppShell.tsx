import { type ReactNode, useState } from 'react'
import DashboardOutlined from '@mui/icons-material/DashboardOutlined'
import Inventory2Outlined from '@mui/icons-material/Inventory2Outlined'
import EventAvailableOutlined from '@mui/icons-material/EventAvailableOutlined'
import ReceiptLongOutlined from '@mui/icons-material/ReceiptLongOutlined'
import ManageAccountsOutlined from '@mui/icons-material/ManageAccountsOutlined'
import CorporateFareOutlined from '@mui/icons-material/CorporateFareOutlined'
import AccountTreeOutlined from '@mui/icons-material/AccountTreeOutlined'
import QrCodeScannerOutlined from '@mui/icons-material/QrCodeScannerOutlined'
import BuildOutlined from '@mui/icons-material/BuildOutlined'
import AssessmentOutlined from '@mui/icons-material/AssessmentOutlined'
import MenuIcon from '@mui/icons-material/Menu'
import LogoutOutlined from '@mui/icons-material/LogoutOutlined'
import ChevronLeftOutlined from '@mui/icons-material/ChevronLeftOutlined'
import ChevronRightOutlined from '@mui/icons-material/ChevronRightOutlined'
import HomeOutlined from '@mui/icons-material/HomeOutlined'
import HelpOutlineOutlined from '@mui/icons-material/HelpOutlineOutlined'
import SettingsOutlined from '@mui/icons-material/SettingsOutlined'
import AccountBalanceWalletOutlined from '@mui/icons-material/AccountBalanceWalletOutlined'
import PeopleAltOutlined from '@mui/icons-material/PeopleAltOutlined'
import {
  AppBar,
  Box,
  Breadcrumbs,
  Button,
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
  ListSubheader,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Stack,
} from '@mui/material'
import { canAccessPage, formatRole, getPrimaryRole, type AppPage } from '../auth/access'
import { isPageEnabledForDemo } from '../config/demoMode'
const drawerWidth = 248
const collapsedDrawerWidth = 76
const navigation = [
  { label: 'Dashboard', id: 'dashboard', icon: <DashboardOutlined />, section: 'Rental operations' },
  { label: 'Bookings & quotations', id: 'bookings', icon: <EventAvailableOutlined />, section: 'Rental operations' },
  { label: 'Pickups & returns', id: 'rentals', icon: <ReceiptLongOutlined />, section: 'Rental operations' },
  { label: 'Asset register', id: 'assets', icon: <Inventory2Outlined />, section: 'Asset operations' },
  { label: 'Maintenance work', id: 'maintenance', icon: <BuildOutlined />, section: 'Asset operations' },
  { label: 'Scan asset QR', id: 'scan', icon: <QrCodeScannerOutlined />, section: 'Asset operations' },
  { label: 'Approvals & operations', id: 'operations', icon: <CorporateFareOutlined />, section: 'Management' },
  { label: 'Finance', id: 'finance', icon: <AccountBalanceWalletOutlined />, section: 'Management' },
  { label: 'Reports & performance', id: 'reports', icon: <AssessmentOutlined />, section: 'Management' },
  { label: 'Staff & access', id: 'users', icon: <ManageAccountsOutlined />, section: 'Administration' },
  { label: 'Customer accounts', id: 'customerAccounts', icon: <PeopleAltOutlined />, section: 'Administration' },
  { label: 'Organization', id: 'divisions', icon: <AccountTreeOutlined />, section: 'Administration' },
  { label: 'System configuration', id: 'configuration', icon: <SettingsOutlined />, section: 'Administration' },
] satisfies { label: string; id: AppPage; icon: ReactNode; section?: string }[]
const sections = ['Rental operations', 'Asset operations', 'Management', 'Administration']
const pageHelp: Record<string, { purpose: string; steps: string[] }> = {
  dashboard: { purpose: 'See what needs attention today and start the most common tasks.', steps: ['Check overdue returns and pending requests.', 'Choose a quick action.', 'Use the left menu to move to another area.'] },
  bookings: { purpose: 'Review customer requests and turn approved requests into confirmed bookings.', steps: ['Open a pending request.', 'Confirm the customer, dates, branch and asset.', 'Approve it when everything is correct.'] },
  rentals: { purpose: 'Work through today’s pickups, active hires and controlled returns.', steps: ['Choose the relevant work queue.', 'Follow each guided check in order.', 'Complete check-out or return only when every requirement is ready.'] },
  scan: { purpose: 'Scan a CREMS QR label to find the correct asset quickly.', steps: ['Scan or enter the asset code.', 'Confirm the asset details.', 'Choose the suggested check-out, check-in or view action.'] },
  customers: { purpose: 'Find customer details, check eligibility and enable secure online access.', steps: ['Search before creating a duplicate customer.', 'Check contact and identification details.', 'Use the email icon to invite an existing customer online.'] },
  assets: { purpose: 'Manage every rentable asset from one register.', steps: ['Search by asset number, name, registration or serial number.', 'Open the asset profile to review its status, location and history.', 'Use Maintenance from the Fleet menu when servicing or repairs are required.'] },
  maintenance: { purpose: 'Plan servicing and resolve faults before assets return to service.', steps: ['Review overdue and upcoming work.', 'Open or update the maintenance job.', 'Return the asset to available only after work is complete.'] },
  operations: { purpose: 'Review manager-level exceptions, approvals and operational work.', steps: ['Start with urgent items.', 'Assign or complete the required action.', 'Use reports for trends rather than daily processing.'] },
  finance: { purpose: 'Review invoices, payments and outstanding customer balances.', steps: ['Start with unpaid and partially paid invoices.', 'Confirm the related booking and customer.', 'Use controlled finance actions to correct or allocate transactions.'] },
  reports: { purpose: 'Review utilization, revenue, rental history and maintenance performance.', steps: ['Choose the report needed.', 'Confirm the date and operating scope.', 'Export or use the result for management decisions.'] },
  users: { purpose: 'Control staff accounts, roles and access boundaries.', steps: ['Choose the correct role.', 'Assign the staff member’s division and branch.', 'Use security actions only when required.'] },
  customerAccounts: { purpose: 'Manage customer portal accounts separately from staff access.', steps: ['Search for the customer before creating access.', 'Review portal status and account activity.', 'Use reset, lock or session controls only when required.'] },
  divisions: { purpose: 'Maintain divisions, branches, services and asset categories in one organization workspace.', steps: ['Choose the relevant organization tab.', 'Update only confirmed operating details.', 'Save and verify the affected branch or service.'] },
  configuration: { purpose: 'Manage group rental defaults, rates, notifications and technical controls.', steps: ['Choose the configuration area.', 'Review the current value and business impact.', 'Save only an approved change.'] },
}

type AppShellProps = {
  activePage: string
  onNavigate: (page: AppPage) => void
  userName: string
  userRoles: string[]
  divisionName: string | null
  branchName: string | null
  onLogout: () => Promise<void>
  children: ReactNode
}

export function AppShell({ activePage, onNavigate, userName, userRoles, divisionName, branchName, onLogout, children }: AppShellProps) {
  const theme = useTheme()
  const desktop = useMediaQuery(theme.breakpoints.up('md'))
  const [open, setOpen] = useState(false)
  const [collapsed, setCollapsed] = useState(false)
  const [helpOpen, setHelpOpen] = useState(false)
  const activeDrawerWidth = desktop && collapsed ? collapsedDrawerWidth : drawerWidth
  const currentPageLabel = navigation.find((item) => item.id === activePage)?.label ?? 'Home'

  const drawer = (
    <Box sx={{ height: '100dvh', minHeight: 0, display: 'flex', flexDirection: 'column', overflow: 'hidden', bgcolor: '#090909', color: 'white' }}>
      <Toolbar sx={{ px: collapsed && desktop ? 2 : 2.5, minHeight: 76, flex: '0 0 auto', justifyContent: collapsed && desktop ? 'center' : 'flex-start' }}>
        <Box component="img" src="/brand/carpenters-logo.png" alt="Carpenters Fiji" sx={{ width: 44, height: 44, objectFit: 'cover', mr: collapsed && desktop ? 0 : 1.5 }} />
        <Box sx={{ display: collapsed && desktop ? 'none' : 'block' }}>
          <Typography variant="h6" fontWeight={700} lineHeight={1.1}>CREMS</Typography>
          <Typography variant="caption" sx={{ color: 'secondary.main' }}>Carpenters Fiji</Typography>
        </Box>
      </Toolbar>
      <List sx={{ px: 1.5, pt: 1, pb: 4, flex: '1 1 auto', minHeight: 0, overflowY: 'auto', overflowX: 'hidden', overscrollBehavior: 'contain', scrollbarGutter: 'stable', '&::-webkit-scrollbar': { width: 7 }, '&::-webkit-scrollbar-thumb': { bgcolor: 'rgba(255,255,255,.24)', borderRadius: 8 }, '&::-webkit-scrollbar-track': { bgcolor: 'transparent' } }}>
        {(!collapsed || !desktop) && <Box sx={{ mx: .75, mb: 1.25, p: 1.5, borderRadius: 2, bgcolor: 'rgba(255,255,255,.07)', border: '1px solid rgba(255,255,255,.08)' }}><Typography variant="caption" sx={{ color: 'rgba(255,255,255,.5)', textTransform: 'uppercase', letterSpacing: .8 }}>Working in</Typography><Typography variant="body2" fontWeight={750} noWrap>{divisionName || 'Carpenters Fiji Group'}</Typography><Typography variant="caption" sx={{ color: 'rgba(255,255,255,.62)' }}>{branchName || 'All branches'}</Typography></Box>}
        {sections.map((section) => {
          const items = navigation.filter((item) => item.section === section && canAccessPage(userRoles, item.id) && isPageEnabledForDemo(item.id))
          if (!items.length) return null
          return <Box key={section}>{!collapsed || !desktop ? <ListSubheader disableSticky sx={{ bgcolor: 'transparent', color: 'rgba(255,255,255,.42)', fontSize: 11, fontWeight: 800, lineHeight: '32px', letterSpacing: 1.1, textTransform: 'uppercase', px: 2, mt: 1 }}>{section}</ListSubheader> : <Box sx={{ height: 12 }} />}{items.map((item) => {
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
                mb: 0.35,
                minHeight: 46,
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
              <ListItemText primary={item.label} primaryTypographyProps={{ fontSize: 14.5, fontWeight: activePage === item.id ? 700 : 500 }} sx={{ display: collapsed && desktop ? 'none' : 'block' }} />
            </ListItemButton>
          )

          return collapsed && desktop ? (
            <Tooltip key={item.id} title={item.label} placement="right" arrow>
              {button}
            </Tooltip>
          ) : button
        })}</Box>})}
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
          <Box><Typography variant="subtitle1" fontWeight={750} lineHeight={1.15}>{currentPageLabel}</Typography><Typography variant="caption" color="text.secondary" sx={{ display: { xs: 'none', sm: 'block' } }}>{divisionName || 'Carpenters Fiji Group'}{branchName ? ` · ${branchName}` : ' · Group-wide access'}</Typography></Box>
          <Box sx={{ flexGrow: 1 }} />
          <Button size="small" color="inherit" startIcon={<HelpOutlineOutlined />} onClick={() => setHelpOpen(true)} sx={{ mr: 1, display: { xs: 'none', sm: 'inline-flex' } }}>Help</Button>
          <Typography variant="body2" fontWeight={600} sx={{ display: { xs: 'none', lg: 'block' } }}>{userName}</Typography>
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
            height: '100dvh',
            border: 0,
            boxSizing: 'border-box',
            overflow: 'hidden',
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
        <Box
          component="nav"
          aria-label="Breadcrumb"
          sx={{
            px: { xs: 2, sm: 3, lg: 4 },
            py: 1.25,
            bgcolor: 'background.paper',
            borderBottom: 1,
            borderColor: 'divider',
          }}
        >
          <Breadcrumbs aria-label="Current location">
            {activePage === 'dashboard' ? (
              <Typography variant="body2" fontWeight={700} color="text.primary" sx={{ display: 'flex', alignItems: 'center', gap: 0.75 }}>
                <HomeOutlined fontSize="small" /> Home
              </Typography>
            ) : (
              <Button
                size="small"
                color="inherit"
                startIcon={<HomeOutlined fontSize="small" />}
                onClick={() => onNavigate('dashboard')}
                sx={{ minWidth: 0, px: 0.5, color: 'text.secondary', textTransform: 'none' }}
              >
                Home
              </Button>
            )}
            {activePage !== 'dashboard' && <Typography variant="body2" fontWeight={700} color="text.primary">{currentPageLabel}</Typography>}
          </Breadcrumbs>
        </Box>
        {children}
      </Box>
      <Dialog open={helpOpen} onClose={() => setHelpOpen(false)} fullWidth maxWidth="sm"><DialogTitle>Help with {currentPageLabel}</DialogTitle><DialogContent><Typography color="text.secondary" mb={2}>{pageHelp[activePage]?.purpose ?? 'Use this page to complete your current CREMS task.'}</Typography><Stack spacing={1.25}>{(pageHelp[activePage]?.steps ?? []).map((step, index) => <Stack key={step} direction="row" gap={1.5} alignItems="flex-start"><Box sx={{ width: 28, height: 28, flex: '0 0 auto', display: 'grid', placeItems: 'center', borderRadius: '50%', bgcolor: 'secondary.main', fontWeight: 800 }}>{index + 1}</Box><Typography sx={{ pt: .35 }}>{step}</Typography></Stack>)}</Stack></DialogContent><DialogActions sx={{ p: 3 }}><Button variant="contained" onClick={() => setHelpOpen(false)}>Got it</Button></DialogActions></Dialog>
    </Box>
  )
}
