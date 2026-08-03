import '@fontsource/inter/400.css'
import '@fontsource/inter/500.css'
import '@fontsource/inter/600.css'
import React from 'react'
import ReactDOM from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { CssBaseline, ThemeProvider, createTheme } from '@mui/material'
import App from './App'
import { AuthProvider } from './auth/AuthContext'

const queryClient = new QueryClient()
const theme = createTheme({
  palette: {
    primary: { main: '#111111', dark: '#000000', contrastText: '#ffffff' },
    secondary: { main: '#ffed00', dark: '#dfcf00', contrastText: '#111111' },
    background: { default: '#f6f6f3', paper: '#ffffff' },
    text: { primary: '#171717', secondary: '#686868' },
  },
  typography: { fontFamily: 'Inter, sans-serif' },
  shape: { borderRadius: 10 },
  components: {
    MuiButton: {
      styleOverrides: {
        root: { textTransform: 'none', fontWeight: 600 },
        containedPrimary: { boxShadow: 'none', '&:hover': { boxShadow: 'none', backgroundColor: '#292929' } },
      },
    },
    MuiCard: {
      styleOverrides: { root: { borderColor: '#e3e3de' } },
    },
  },
})

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <ThemeProvider theme={theme}>
      <CssBaseline />
      <QueryClientProvider client={queryClient}>
        <AuthProvider>
          <App />
        </AuthProvider>
      </QueryClientProvider>
    </ThemeProvider>
  </React.StrictMode>,
)
