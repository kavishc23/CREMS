import { useState } from 'react'
import { AppShell } from './layout/AppShell'
import { DashboardPage } from './pages/DashboardPage'
import { LoginPage } from './pages/LoginPage'
import { useAuth } from './auth/AuthContext'
import { Box, CircularProgress } from '@mui/material'
import { canAccessPage, type AppPage } from './auth/access'
import { UsersPage } from './pages/UsersPage'
import { BranchesPage } from './pages/BranchesPage'
import { AssetsPage } from './pages/AssetsPage'
import { CustomersPage } from './pages/CustomersPage'
import { PublicRentalPage } from './pages/PublicRentalPage'
import { BookingsPage } from './pages/BookingsPage'
import { RentalsPage } from './pages/RentalsPage'
import { MaintenancePage } from './pages/MaintenancePage'
import { ReportsPage } from './pages/ReportsPage'

export default function App() {
  const [page, setPage] = useState<AppPage>('dashboard')
  const [staffView, setStaffView] = useState(() => window.location.pathname.startsWith('/staff'))
  const { user, checkingSession, logout } = useAuth()

  if (checkingSession) {
    return <Box sx={{ minHeight: '100vh', display: 'grid', placeItems: 'center' }}><CircularProgress /></Box>
  }

  if (!user && !staffView) return <PublicRentalPage onStaffLogin={() => {
    window.history.pushState({}, '', '/staff/login')
    setStaffView(true)
  }} />

  if (!user) return <LoginPage onBackToWebsite={() => {
    window.history.pushState({}, '', '/')
    setStaffView(false)
  }} />

  const activePage = canAccessPage(user.roles, page) ? page : 'dashboard'
  const content = activePage === 'dashboard'
    ? <DashboardPage userName={user.fullName || user.email} userRoles={user.roles} onNavigate={setPage} />
    : activePage === 'assets'
      ? <AssetsPage userRoles={user.roles} />
    : activePage === 'customers'
      ? <CustomersPage />
    : activePage === 'bookings'
      ? <BookingsPage />
    : activePage === 'rentals'
      ? <RentalsPage />
    : activePage === 'maintenance'
      ? <MaintenancePage />
    : activePage === 'reports'
      ? <ReportsPage />
    : activePage === 'users'
      ? <UsersPage />
      : <BranchesPage userRoles={user.roles} />

  return (
    <AppShell activePage={activePage} onNavigate={setPage} userName={user.fullName || user.email} userRoles={user.roles} onLogout={logout}>
      {content}
    </AppShell>
  )
}
