import { lazy, Suspense, useEffect, useState, type ComponentType } from 'react'
import { Box, CircularProgress } from '@mui/material'
import { useAuth } from './auth/AuthContext'
import { canAccessPage, type AppPage } from './auth/access'
import { AppShell } from './layout/AppShell'
import { isPageEnabledForDemo } from './config/demoMode'

const staffRoutes: Record<AppPage, string> = {
  dashboard: '/staff/dashboard',
  bookings: '/staff/bookings',
  rentals: '/staff/hire-operations',
  assets: '/staff/assets',
  customers: '/staff/customers',
  scan: '/staff/scan',
  maintenance: '/staff/maintenance',
  operations: '/staff/operations',
  finance: '/staff/finance',
  reports: '/staff/reports',
  users: '/staff/staff-access',
  customerAccounts: '/staff/customer-accounts',
  branches: '/staff/branches',
  divisions: '/staff/organization',
  configuration: '/staff/configuration',
}

function staffPageFromPath(pathname: string): AppPage | null {
  const match = Object.entries(staffRoutes).find(([, path]) => path === pathname)
  return match?.[0] as AppPage | undefined ?? null
}

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
const CustomerAccountsPage = lazyNamed(() => import('./pages/CustomerAccountsPage'), 'CustomerAccountsPage')
const PublicRentalPage = lazy(() => import('./pages/PublicRentalPage').then(module => ({ default: module.PublicRentalPage })))
const BookingsPage = lazyNamed(() => import('./pages/BookingsPage'), 'BookingsPage')
const RentalsPage = lazyNamed(() => import('./pages/RentalsPage'), 'RentalsPage')
const MaintenancePage = lazyNamed(() => import('./pages/MaintenancePage'), 'MaintenancePage')
const ReportsPage = lazyNamed(() => import('./pages/ReportsPage'), 'ReportsPage')
const ChangePasswordPage = lazyNamed(() => import('./pages/ChangePasswordPage'), 'ChangePasswordPage')
const AssetsPage = lazy(() => import('./pages/AssetsPage').then(module => ({ default: module.AssetsPage })))
const ManagementPage = lazyNamed(() => import('./pages/ManagementPage'), 'ManagementPage')
const FinancePage = lazyNamed(() => import('./pages/FinancePage'), 'FinancePage')
const AssetQrPage = lazyNamed(() => import('./pages/AssetQrPage'), 'AssetQrPage')
const OrganizationWorkspacePage = lazy(() => import('./pages/OrganizationWorkspacePage').then(module => ({ default: module.OrganizationWorkspacePage })))
const SystemConfigurationPage = lazyNamed(() => import('./pages/SystemConfigurationPage'), 'SystemConfigurationPage')
const CustomerPortalPage = lazy(() => import('./pages/CustomerPortalPage').then(module => ({ default: module.CustomerPortalPage })))

function LoadingScreen() {
  return <Box role="status" aria-label="Loading page" sx={{ minHeight: '45vh', display: 'grid', placeItems: 'center' }}><CircularProgress size={34} /></Box>
}

export default function App() {
  const [page, setPage] = useState<AppPage>(() => staffPageFromPath(window.location.pathname) ?? 'dashboard')
  const [staffView, setStaffView] = useState(() => window.location.pathname.startsWith('/staff'))
  const [customerView, setCustomerView] = useState(() => window.location.pathname.startsWith('/account'))
  const [customerSignedIn, setCustomerSignedIn] = useState(false)
  const { user, checkingSession, logout } = useAuth()

  useEffect(() => {
    function synchronizeRoute() {
      setCustomerView(window.location.pathname.startsWith('/account'))
      setStaffView(window.location.pathname.startsWith('/staff'))
      const staffPage = staffPageFromPath(window.location.pathname)
      if (staffPage) setPage(staffPage)
    }
    window.addEventListener('popstate', synchronizeRoute)
    return () => window.removeEventListener('popstate', synchronizeRoute)
  }, [])

  useEffect(() => {
    if (checkingSession || !user || user.roles.includes('Customer') || !staffView) return
    const requestedPage = staffPageFromPath(window.location.pathname) ?? 'dashboard'
    const allowedPage = canAccessPage(user.roles, requestedPage) && isPageEnabledForDemo(requestedPage)
      ? requestedPage
      : 'dashboard'
    // Synchronize authenticated application state with the requested browser route.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setPage(allowedPage)
    const canonicalPath = staffRoutes[allowedPage]
    if (window.location.pathname !== canonicalPath) window.history.replaceState({}, '', canonicalPath)
  }, [checkingSession, staffView, user])

  function navigateStaff(nextPage: AppPage) {
    window.history.pushState({}, '', staffRoutes[nextPage])
    setPage(nextPage)
  }

  async function logoutStaff() {
    await logout()
    window.history.replaceState({}, '', '/staff/login')
    setPage('dashboard')
  }

  if (checkingSession) return <LoadingScreen />

  if (customerView) return <Suspense fallback={<LoadingScreen />}><CustomerPortalPage onSessionChange={setCustomerSignedIn} onBack={() => {
    window.history.pushState({}, '', '/')
    setCustomerView(false)
  }} /></Suspense>

  if ((!user || user.roles.includes('Customer')) && !staffView) return <Suspense fallback={<LoadingScreen />}><PublicRentalPage customerAuthenticated={customerSignedIn || user?.roles.includes('Customer')} onCustomerAccount={() => {
    window.history.pushState({}, '', '/account')
    setCustomerView(true)
  }} onCustomerSignOut={async () => {
    await logout()
    setCustomerSignedIn(false)
  }} /></Suspense>

  if (!user) return <Suspense fallback={<LoadingScreen />}><LoginPage onBackToWebsite={() => {
    window.history.pushState({}, '', '/')
    setStaffView(false)
  }} /></Suspense>

  if (user.mustChangePassword) return <Suspense fallback={<LoadingScreen />}><ChangePasswordPage /></Suspense>

  const activePage = canAccessPage(user.roles, page) && isPageEnabledForDemo(page) ? page : 'dashboard'
  const content = activePage === 'dashboard'
    ? <DashboardPage userName={user.fullName || user.email} userRoles={user.roles} onNavigate={navigateStaff} />
    : activePage === 'assets' ? <AssetsPage userRoles={user.roles} />
    : activePage === 'customers' ? <CustomersPage />
    : activePage === 'bookings' ? <BookingsPage />
    : activePage === 'rentals' ? <RentalsPage />
    : activePage === 'scan' ? <AssetQrPage />
    : activePage === 'maintenance' ? <MaintenancePage />
    : activePage === 'operations' ? <ManagementPage />
    : activePage === 'finance' ? <FinancePage />
    : activePage === 'reports' ? <ReportsPage />
    : activePage === 'users' ? <AdministrationPage userRoles={user.roles} />
    : activePage === 'customerAccounts' ? <CustomerAccountsPage />
    : activePage === 'divisions' ? <OrganizationWorkspacePage userRoles={user.roles} />
    : activePage === 'configuration' ? <SystemConfigurationPage />
    : <BranchesPage userRoles={user.roles} />

  return <AppShell activePage={activePage} onNavigate={navigateStaff} userName={user.fullName || user.email} userRoles={user.roles} divisionName={user.divisionName} branchName={user.branchName} onLogout={logoutStaff}>
    <Suspense fallback={<LoadingScreen />}>{content}</Suspense>
  </AppShell>
}
