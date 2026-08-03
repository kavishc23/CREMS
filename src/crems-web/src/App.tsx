import { useState } from 'react'
import { AppShell } from './layout/AppShell'
import { DashboardPage } from './pages/DashboardPage'
import { PlaceholderPage } from './pages/PlaceholderPage'
import { LoginPage } from './pages/LoginPage'
import { useAuth } from './auth/AuthContext'
import { Box, CircularProgress } from '@mui/material'

export default function App() {
  const [page, setPage] = useState('dashboard')
  const { user, checkingSession, logout } = useAuth()

  if (checkingSession) {
    return <Box sx={{ minHeight: '100vh', display: 'grid', placeItems: 'center' }}><CircularProgress /></Box>
  }

  if (!user) return <LoginPage />

  const content = page === 'dashboard'
    ? <DashboardPage userName={user.fullName || user.email} onNavigate={setPage} />
    : <PlaceholderPage title={page[0].toUpperCase() + page.slice(1)} />

  return (
    <AppShell activePage={page} onNavigate={setPage} userName={user.fullName || user.email} onLogout={logout}>
      {content}
    </AppShell>
  )
}
