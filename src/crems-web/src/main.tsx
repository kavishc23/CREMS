import '@fontsource/inter/latin-400.css'
import '@fontsource/inter/latin-500.css'
import '@fontsource/inter/latin-600.css'
import ReactDOM from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { CssBaseline, GlobalStyles, ThemeProvider, createTheme } from '@mui/material'
import App from './App'
import { ToastHost } from './components/ToastHost'
import { AuthProvider } from './auth/AuthContext'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      gcTime: 5 * 60_000,
      refetchOnWindowFocus: false,
      retry: 1,
    },
    mutations: { retry: 0 },
  },
})
const theme = createTheme({
  palette: {
    primary: { main: '#111111', dark: '#000000', contrastText: '#ffffff' },
    secondary: { main: '#ffed00', dark: '#dfcf00', contrastText: '#111111' },
    background: { default: '#f6f6f3', paper: '#ffffff' },
    text: { primary: '#171717', secondary: '#686868' },
  },
  typography: {
    fontFamily: 'Inter, sans-serif',
    h4: { letterSpacing: '-0.025em' },
    h5: { letterSpacing: '-0.018em' },
  },
  shape: { borderRadius: 12 },
  components: {
    MuiDialog: {
      styleOverrides: { paper: { maxHeight: 'calc(100dvh - 32px)' } },
    },
    MuiDialogContent: {
      styleOverrides: { root: { overflowY: 'auto' } },
    },
    MuiButton: {
      styleOverrides: {
        root: { textTransform: 'none', fontWeight: 700, minHeight: 40 },
        containedPrimary: { boxShadow: 'none', '&:hover': { boxShadow: 'none', backgroundColor: '#292929' } },
      },
    },
    MuiCard: {
      styleOverrides: { root: { borderColor: '#deded7', boxShadow: '0 1px 2px rgba(0,0,0,.025)' } },
    },
    MuiTableHead: { styleOverrides: { root: { backgroundColor: '#f1f1ec' } } },
    MuiTableCell: { styleOverrides: { head: { fontWeight: 750, color: '#3f3f3b' } } },
    MuiTextField: { defaultProps: { variant: 'outlined' } },
  },
})

ReactDOM.createRoot(document.getElementById('root')!).render(
  <ThemeProvider theme={theme}>
    <CssBaseline />
    <GlobalStyles styles={{
      'html, body': { maxWidth: '100%', overflowX: 'hidden' },
      '.customer-site-density': { zoom: '90%', width: '100%', minHeight: '100dvh' },
      '.customer-site-density .MuiCard-root, .customer-site-density .MuiGrid-root': { minWidth: 0 },
      '.customer-site-density img': { maxWidth: '100%' },
      '@media (max-width: 600px)': { '.customer-site-density': { zoom: 1 } },
      '@media print': { '.customer-site-density': { zoom: 1, width: '100%', minHeight: 0 }, 'body *': { visibility: 'hidden' }, '#crems-qr-label, #crems-qr-label *': { visibility: 'visible' }, '#crems-qr-label': { position: 'absolute', left: 0, top: 0, width: '90mm', border: '2px solid #000 !important' } },
    }} />
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <App />
        <ToastHost />
      </AuthProvider>
    </QueryClientProvider>
  </ThemeProvider>,
)
