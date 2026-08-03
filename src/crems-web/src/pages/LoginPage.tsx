import { type FormEvent, useState } from 'react'
import VisibilityOutlined from '@mui/icons-material/VisibilityOutlined'
import VisibilityOffOutlined from '@mui/icons-material/VisibilityOffOutlined'
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  CircularProgress,
  IconButton,
  InputAdornment,
  Stack,
  TextField,
  Typography,
} from '@mui/material'
import axios from 'axios'
import { useAuth } from '../auth/AuthContext'

export function LoginPage() {
  const { login } = useAuth()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [showPassword, setShowPassword] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState('')

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setError('')
    setSubmitting(true)
    try {
      await login(email.trim(), password)
    } catch (reason) {
      if (axios.isAxiosError(reason) && reason.response?.status === 401) {
        setError('The email address or password is incorrect.')
      } else {
        setError('Sign-in is temporarily unavailable. Please try again.')
      }
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <Box sx={{ minHeight: '100vh', display: 'grid', gridTemplateColumns: { xs: '1fr', lg: '1.1fr 0.9fr' }, bgcolor: 'background.default' }}>
      <Box sx={{ display: { xs: 'none', lg: 'flex' }, position: 'relative', overflow: 'hidden', bgcolor: 'secondary.main', color: 'secondary.contrastText', p: 8, alignItems: 'flex-end' }}>
        <Box sx={{ position: 'absolute', width: 520, height: 520, borderRadius: '50%', bgcolor: 'rgba(0,0,0,.045)', top: -180, right: -120 }} />
        <Box sx={{ position: 'absolute', width: 300, height: 300, borderRadius: '50%', border: '1px solid rgba(0,0,0,.13)', top: 90, left: 80 }} />
        <Box sx={{ position: 'relative', maxWidth: 600 }}>
          <Box component="img" src="/brand/carpenters-logo.png" alt="Carpenters Fiji" sx={{ width: 96, height: 96, objectFit: 'cover', mb: 4 }} />
          <Typography variant="overline" sx={{ letterSpacing: 2.5, fontWeight: 700 }}>Carpenters Fiji</Typography>
          <Typography variant="h2" fontWeight={700} lineHeight={1.1} mt={1}>Rental operations, all in one place.</Typography>
          <Typography variant="h6" sx={{ opacity: 0.7, fontWeight: 400, mt: 3 }}>Manage vehicles, equipment, bookings, inspections and maintenance securely across every branch.</Typography>
        </Box>
      </Box>

      <Box sx={{ display: 'grid', placeItems: 'center', p: { xs: 2, sm: 5 } }}>
        <Card elevation={0} sx={{ width: '100%', maxWidth: 460, border: { xs: 0, sm: 1 }, borderColor: 'divider', bgcolor: 'background.paper' }}>
          <CardContent sx={{ p: { xs: 3, sm: 5 } }}>
            <Stack alignItems="center" mb={4}>
              <Box component="img" src="/brand/carpenters-logo.png" alt="Carpenters Fiji" sx={{ display: { xs: 'block', lg: 'none' }, width: 72, height: 72, objectFit: 'cover', mb: 2 }} />
              <Typography variant="h4" fontWeight={700}>Welcome back</Typography>
              <Typography color="text.secondary" mt={1}>Sign in to your CREMS account</Typography>
            </Stack>

            <Box component="form" onSubmit={handleSubmit} noValidate>
              <Stack spacing={2.5}>
                {error && <Alert severity="error">{error}</Alert>}
                <TextField
                  label="Email address"
                  type="email"
                  value={email}
                  onChange={(event) => setEmail(event.target.value)}
                  autoComplete="email"
                  autoFocus
                  required
                  fullWidth
                />
                <TextField
                  label="Password"
                  type={showPassword ? 'text' : 'password'}
                  value={password}
                  onChange={(event) => setPassword(event.target.value)}
                  autoComplete="current-password"
                  required
                  fullWidth
                  slotProps={{
                    input: {
                      endAdornment: (
                        <InputAdornment position="end">
                          <IconButton aria-label={showPassword ? 'Hide password' : 'Show password'} onClick={() => setShowPassword((value) => !value)} edge="end">
                            {showPassword ? <VisibilityOffOutlined /> : <VisibilityOutlined />}
                          </IconButton>
                        </InputAdornment>
                      ),
                    },
                  }}
                />
                <Button type="submit" variant="contained" size="large" disabled={submitting || !email || !password} sx={{ minHeight: 48 }}>
                  {submitting ? <CircularProgress size={24} color="inherit" /> : 'Sign in'}
                </Button>
              </Stack>
            </Box>
            <Typography variant="caption" color="text.secondary" display="block" textAlign="center" mt={4}>
              Authorized Carpenters Fiji staff only
            </Typography>
          </CardContent>
        </Card>
      </Box>
    </Box>
  )
}
