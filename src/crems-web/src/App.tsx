import { lazy, Suspense, useState, type ComponentType, type LazyExoticComponent } from 'react'
import { Box, CircularProgress } from '@mui/material'
import { useAuth } from './auth/AuthContext'
import { canAccessPage, type AppPage } from './auth/access'
import { AppShell } from './layout/AppShell'
import { isPageEnabledForDemo } from './config/demoMode'

function lazyNamed<T extends ComponentType<Record<string, never>>>(
  loader: () => Promise<Record<string, unknown>>,
  exportName: string,
) {
  return lazy(async () => ({ default: (await loader())[exportName] as T }))
}

const DashboardPage = lazy(() => import('./pages/DashboardPage').then(module => ({ default: module.DashboardPage })))
const LoginPage = lazy(() => import('./pages/LoginPage').then(module => ({ default: module.LoginPage })))
const AdministrationPage = lazy(() => import('./pages/AdministrationPage').then(module => ({ default: module.AdministrationPage })))
const BranchesPage = lazy(() => import('./pages/BranchesPage').then(module => ({ default: module.BranchesPage })))
const CustomersPage = lazyNamed(() => import('./pages/CustomersPage'), 'CustomersPage')
const PublicRentalPage = lazy(() => import('./pages/PublicRentalPage').then(module => ({ default: module.PublicRentalPage })))
const BookingsPage = lazyNamed(() => import('./pages/BookingsPage'), 'BookingsPage')
const RentalsPage = lazyNamed(() => import('./pages/RentalsPage'), 'RentalsPage')
const MaintenancePage = lazyNamed(() => import('./pages/MaintenancePage'), 'MaintenancePage')
const ReportsPage = lazyNamed(() => import('./pages/ReportsPage'), 'ReportsPage')
const ChangePasswordPage = lazyNamed(() => import('./pages/ChangePasswordPage'), 'ChangePasswordPage')
const AssetsPage = lazy(() => import('./pages/AssetsPage').then(module => ({ default: module.AssetsPage })))
const ManagementPage = lazyNamed(() => import('./pages/ManagementPage'), 'ManagementPage')
const AssetQrPage = lazyNamed(() => import('./pages/AssetQrPage'), 'AssetQrPage')
const OrganizationWorkspacePage = lazy(() => import('./pages/OrganizationWorkspacePage').then(module => ({ default: module.OrganizationWorkspacePage })))
const SystemConfigurationPage = lazyNamed(() => import('./pages/SystemConfigurationPage'), 'SystemConfigurationPage')
const CustomerPortalPage = lazy(() => import('./pages/CustomerPortalPage').then(module => ({ default: module.CustomerPortalPage })))

function LoadingScreen() {
  return <Box role="status" aria-label="Loading page" sx={{ minHeight: '45vh', display: 'grid', placeItems: 'center' }}><CircularProgress size={34} /></Box>
}

export default function App() {
  const [page, setPage] = useState<AppPage>(() => window.location.pathname.startsWith('/staff/scan') ? 'scan' : 'dashboard')
  const [staffView, setStaffView] = useState(() => window.location.pathname.startsWith('/staff'))
  const [customerView, setCustomerView] = useState(() => window.location.pathname.startsWith('/account'))
  const [customerSignedIn, setCustomerSignedIn] = useState(false)
  const { user, checkingSession, logout } = useAuth()

  if (checkingSession) return <LoadingScreen />

  if (customerView) return <Suspense fallback={<LoadingScreen />}><CustomerPortalPage onSessionChange={setCustomerSignedIn} onBack={() => {
    window.history.pushState({}, '', '/')
    setCustomerView(false)
  }} /></Suspense>

  if ((!user || user.roles.includes('Customer')) && !staffView) return <Suspense fallback={<LoadingScreen />}><PublicRentalPage customerAuthenticated={customerSignedIn || user?.roles.includes('Customer')} onCustomerAccount={() => {
    window.history.pushState({}, '', '/account')
    setCustomerView(true)
  }} /></Suspense>

  if (!user) return <Suspense fallback={<LoadingScreen />}><LoginPage onBackToWebsite={() => {
    window.history.pushState({}, '', '/')
    setStaffView(false)
  }} /></Suspense>

  if (user.mustChangePassword) return <Suspense fallback={<LoadingScreen />}><ChangePasswordPage /></Suspense>

  const activePage = canAccessPage(user.roles, page) && isPageEnabledForDemo(page) ? page : 'dashboard'
  const content = activePage === 'dashboard'
    ? <DashboardPage userName={user.fullName || user.email} userRoles={user.roles} onNavigate={setPage} />
    : activePage === 'assets' ? <AssetsPage userRoles={user.roles} />
    : activePage === 'customers' ? <CustomersPage />
    : activePage === 'bookings' ? <BookingsPage />
    : activePage === 'rentals' ? <RentalsPage />
    : activePage === 'scan' ? <AssetQrPage />
    : activePage === 'maintenance' ? <MaintenancePage />
    : activePage === 'operations' ? <ManagementPage />
    : activePage === 'reports' ? <ReportsPage />
    : activePage === 'users' ? <AdministrationPage userRoles={user.roles} />
    : activePage === 'divisions' ? <OrganizationWorkspacePage userRoles={user.roles} />
    : activePage === 'configuration' ? <SystemConfigurationPage />
    : <BranchesPage userRoles={user.roles} />

  return <AppShell activePage={activePage} onNavigate={setPage} userName={user.fullName || user.email} userRoles={user.roles} divisionName={user.divisionName} branchName={user.branchName} onLogout={logout}>
    <Suspense fallback={<LoadingScreen />}>{content}</Suspense>
  </AppShell>
}
