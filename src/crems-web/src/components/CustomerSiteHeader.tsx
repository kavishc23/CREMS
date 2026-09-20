import { useState } from 'react'
import { NotificationBell } from './NotificationBell'
import AccountCircleOutlined from '@mui/icons-material/AccountCircleOutlined'
import CloseOutlined from '@mui/icons-material/CloseOutlined'
import LogoutOutlined from '@mui/icons-material/LogoutOutlined'
import MenuOutlined from '@mui/icons-material/MenuOutlined'
import {
  AppBar, Box, Button, Container, Divider, Drawer, IconButton, Stack, Toolbar, Typography,
} from '@mui/material'
import { isNotificationModuleEnabled } from '../config/demoMode'

export type CustomerSiteSection = 'top' | 'search' | 'services' | 'rentals' | 'how-it-works' | 'faq' | 'contact'

type CustomerSiteHeaderProps = {
  accountActive?: boolean
  authenticated?: boolean
  accountName?: string
  onAccount: () => void
  onAccountSection?: (section: 'overview' | 'bookings') => void
  onNavigate: (section: CustomerSiteSection) => void
  onSignOut?: () => void | Promise<void>
}

const links: { section: CustomerSiteSection; label: string }[] = [{ section: 'contact', label: 'Help & contact' }]

export function CustomerSiteHeader({ accountActive = false, authenticated = false, accountName, onAccount, onAccountSection, onNavigate, onSignOut }: CustomerSiteHeaderProps) {
  const [mobileOpen, setMobileOpen] = useState(false)

  function navigate(section: CustomerSiteSection) {
    setMobileOpen(false)
    onNavigate(section)
  }

  function openAccount() {
    setMobileOpen(false)
    if (authenticated && onAccountSection) onAccountSection('overview')
    else onAccount()
  }

  const accountLabel = authenticated ? accountName ?? 'My account' : 'Sign in'
  const desktopNavigation = <Stack direction="row" spacing={.5} alignItems="center">
    <Button variant="contained" color="secondary" onClick={() => navigate('top')} sx={{ color: '#111' }}>Book a rental</Button>
    {authenticated && <Button color="inherit" onClick={() => onAccountSection?.('bookings')} sx={{ color: 'rgba(255,255,255,.82)', px: 1.25 }}>Manage bookings</Button>}
    {links.map(link => <Button key={link.section} color="inherit" onClick={() => navigate(link.section)} sx={{ color: 'rgba(255,255,255,.82)', px: 1.25 }}>{link.label}</Button>)}
    <Button
      variant="contained"
      color="secondary"
      startIcon={<AccountCircleOutlined />}
      onClick={() => openAccount()}
      aria-current={accountActive ? 'page' : undefined}
      sx={{ ml: 1, color: '#111', boxShadow: accountActive ? '0 0 0 2px #fff' : 'none' }}
    >{accountLabel}</Button>
    {authenticated && onSignOut && <IconButton color="inherit" aria-label="Sign out" title="Sign out" onClick={() => void onSignOut()} sx={{ ml: .25 }}><LogoutOutlined /></IconButton>}
  </Stack>

  return <>
    <AppBar position="fixed" elevation={0} sx={{ bgcolor: '#0b0b0b', color: 'white', borderBottom: '1px solid rgba(255,255,255,.12)' }}>
      <Container maxWidth="xl" disableGutters>
        <Toolbar sx={{ minHeight: { xs: 68, md: 76 }, px: { xs: 2, sm: 3 } }}>
          <Box component="button" onClick={() => navigate('top')} aria-label="Carpenters Rentals and Hire home" sx={{ display: 'flex', alignItems: 'center', gap: 1.5, p: 0, border: 0, bgcolor: 'transparent', color: 'inherit', cursor: 'pointer', textAlign: 'left', minWidth: 0 }}>
          <Box component="img" src="/brand/carpenters-logo.png" alt="Carpenters Fiji" sx={{ width: { xs: 42, md: 48 }, height: { xs: 42, md: 48 }} } />
          <Box sx={{ minWidth: 0 }}>
            <Typography fontWeight={800} lineHeight={1.05} noWrap>Carpenters Rentals &amp; Hire</Typography>
          </Box>
          </Box>
          <Box sx={{ flexGrow: 1 }} />
          {authenticated && isNotificationModuleEnabled && <NotificationBell customer key={accountName}/> }
          <Box sx={{ display: { xs: 'none', lg: 'block' } }}>{desktopNavigation}</Box>
          <IconButton color="inherit" aria-label="Open navigation" sx={{ display: { lg: 'none' } }} onClick={() => setMobileOpen(true)}><MenuOutlined /></IconButton>
        </Toolbar>
      </Container>
    </AppBar>
    <Toolbar sx={{ minHeight: { xs: 68, md: 76 } }} />

    <Drawer anchor="right" open={mobileOpen} onClose={() => setMobileOpen(false)}>
      <Box sx={{ width: { xs: 300, sm: 340 }, minHeight: '100%', p: 2.5 }}>
        <Stack direction="row" alignItems="center" justifyContent="space-between" mb={2}>
          <Stack direction="row" alignItems="center" gap={1.25}>
            <Box component="img" src="/brand/carpenters-logo.png" alt="" sx={{ width: 40, height: 40 }} />
            <Box><Typography fontWeight={800}>Rentals &amp; Hire</Typography><Typography variant="caption" color="text.secondary">Carpenters Fiji</Typography></Box>
          </Stack>
          <IconButton aria-label="Close navigation" onClick={() => setMobileOpen(false)}><CloseOutlined /></IconButton>
        </Stack>
        <Divider sx={{ mb: 2 }} />
        <Stack spacing={.5} alignItems="stretch">
          <Button variant="contained" color="secondary" onClick={() => navigate('top')} sx={{ justifyContent: 'flex-start', color: '#111' }}>Book a rental</Button>
          {authenticated && <Button onClick={() => { setMobileOpen(false); onAccountSection?.('bookings') }} sx={{ justifyContent: 'flex-start', color: 'text.primary', py: 1.2 }}>Manage bookings</Button>}
          {links.map(link => <Button key={link.section} onClick={() => navigate(link.section)} sx={{ justifyContent: 'flex-start', color: 'text.primary', py: 1.2 }}>{link.label}</Button>)}
          <Divider sx={{ my: 1 }} />
          <Button variant="contained" color="secondary" startIcon={<AccountCircleOutlined />} onClick={() => openAccount()} sx={{ justifyContent: 'flex-start', color: '#111' }}>{accountLabel}</Button>
          {authenticated && onSignOut && <Button color="inherit" startIcon={<LogoutOutlined />} onClick={() => { setMobileOpen(false); void onSignOut() }} sx={{ justifyContent: 'flex-start' }}>Sign out</Button>}
        </Stack>
      </Box>
    </Drawer>
  </>
}
