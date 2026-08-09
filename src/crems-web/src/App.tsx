import { useState } from 'react'
import { AppShell } from './layout/AppShell'
import { DashboardPage } from './pages/DashboardPage'
import { LoginPage } from './pages/LoginPage'
import { useAuth } from './auth/AuthContext'
import { Box, CircularProgress } from '@mui/material'
import { canAccessPage, type AppPage } from './auth/access'
import { AdministrationPage } from './pages/AdministrationPage'
import { BranchesPage } from './pages/BranchesPage'
import { CustomersPage } from './pages/CustomersPage'
import { PublicRentalPage } from './pages/PublicRentalPage'
import { BookingsPage } from './pages/BookingsPage'
import { RentalsPage } from './pages/RentalsPage'
import { MaintenancePage } from './pages/MaintenancePage'
import { ReportsPage } from './pages/ReportsPage'
import { ChangePasswordPage } from './pages/ChangePasswordPage'
import { FleetPage } from './pages/FleetPage'
import { ManagementPage } from './pages/ManagementPage'
import { AssetQrPage } from './pages/AssetQrPage'
import { DivisionsPage } from './pages/DivisionsPage'
import { CustomerPortalPage } from './pages/CustomerPortalPage'

export default function App() {
  const [page, setPage] = useState<AppPage>(() => window.location.pathname.startsWith('/staff/scan') ? 'scan' : 'dashboard')
  const [staffView, setStaffView] = useState(() => window.location.pathname.startsWith('/staff'))
  const [customerView, setCustomerView] = useState(() => window.location.pathname.startsWith('/account'))
  const [customerSignedIn, setCustomerSignedIn] = useState(false)
  const { user, checkingSession, logout } = useAuth()

  if (checkingSession) {
    return <Box sx={{ minHeight: '100vh', display: 'grid', placeItems: 'center' }}><CircularProgress /></Box>
  }

  if (customerView) return <CustomerPortalPage onSessionChange={setCustomerSignedIn} onBack={() => {
    window.history.pushState({}, '', '/')
    setCustomerView(false)
  }} />

  if ((!user || user.roles.includes('Customer')) && !staffView) return <PublicRentalPage customerAuthenticated={customerSignedIn || user?.roles.includes('Customer')} onCustomerAccount={() => {
    window.history.pushState({}, '', '/account')
    setCustomerView(true)
  }} />

  if (!user) return <LoginPage onBackToWebsite={() => {
    window.history.pushState({}, '', '/')
    setStaffView(false)
  }} />

  if (user.mustChangePassword) return <ChangePasswordPage />

  const activePage = canAccessPage(user.roles, page) ? page : 'dashboard'
  const content = activePage === 'dashboard'
    ? <DashboardPage userName={user.fullName || user.email} userRoles={user.roles} onNavigate={setPage} />
    : activePage === 'assets'
      ? <FleetPage userRoles={user.roles} />
    : activePage === 'customers'
      ? <CustomersPage />
    : activePage === 'bookings'
      ? <BookingsPage />
    : activePage === 'rentals'
      ? <RentalsPage />
    : activePage === 'scan'
      ? <AssetQrPage />
    : activePage === 'maintenance'
      ? <MaintenancePage />
    : activePage === 'operations'
      ? <ManagementPage />
    : activePage === 'reports'
      ? <ReportsPage />
    : activePage === 'users'
      ? <AdministrationPage />
    : activePage === 'divisions'
      ? <DivisionsPage />
      : <BranchesPage userRoles={user.roles} />

  return (
    <AppShell activePage={activePage} onNavigate={setPage} userName={user.fullName || user.email} userRoles={user.roles} divisionName={user.divisionName} branchName={user.branchName} onLogout={logout}>
      {content}
    </AppShell>
  )
}
