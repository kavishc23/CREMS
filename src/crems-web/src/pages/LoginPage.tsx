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
import { api } from '../api/client'

export function LoginPage({ onBackToWebsite }: { onBackToWebsite: () => void }) {
  const { login } = useAuth()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [showPassword, setShowPassword] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState('')
  const [recovery, setRecovery] = useState(false)
  const [codeSent, setCodeSent] = useState(false)
  const [resetCode, setResetCode] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [notice, setNotice] = useState('')

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

  async function requestReset(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setSubmitting(true); setError(''); setNotice('')
    try { const response = await api.post<{ message: string }>('/auth/password-reset/request', { email: email.trim() }); setCodeSent(true); setNotice(response.data.message) }
    catch { setError('Password recovery is temporarily unavailable. Please try again.') }
    finally { setSubmitting(false) }
  }

  async function completeReset(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setError(''); if (newPassword !== confirmPassword) { setError('The new passwords do not match.'); return } setSubmitting(true)
    try { await api.post('/auth/password-reset/complete', { email: email.trim(), code: resetCode.trim(), newPassword }); setRecovery(false); setCodeSent(false); setPassword(''); setResetCode(''); setNewPassword(''); setConfirmPassword(''); setNotice('Password changed successfully. You can now sign in.') }
    catch (reason: unknown) { const data = axios.isAxiosError(reason) ? reason.response?.data : undefined; const errors = data?.errors as Record<string, string[]> | undefined; setError(errors ? Object.values(errors).flat().join(' ') : data?.message ?? 'The code is invalid or has expired.') }
    finally { setSubmitting(false) }
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
              <Typography variant="h4" fontWeight={700}>{recovery ? 'Reset password' : 'Welcome back'}</Typography>
              <Typography color="text.secondary" mt={1}>{recovery ? 'Use the six-digit code sent to your email' : 'Sign in to your CREMS account'}</Typography>
            </Stack>

            {!recovery ? <Box component="form" onSubmit={handleSubmit} noValidate>
              <Stack spacing={2.5}>
                {error && <Alert severity="error">{error}</Alert>}
                {notice && <Alert severity="success">{notice}</Alert>}
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
                <Button type="button" variant="text" onClick={() => { setRecovery(true); setError(''); setNotice('') }}>Forgot password?</Button>
                <Button type="button" variant="text" onClick={onBackToWebsite}>Back to rental website</Button>
              </Stack>
            </Box> : <Box component="form" onSubmit={codeSent ? completeReset : requestReset} noValidate><Stack spacing={2.5}>{error && <Alert severity="error">{error}</Alert>}{notice && <Alert severity="info">{notice}</Alert>}<TextField label="Staff email address" type="email" value={email} onChange={(event) => setEmail(event.target.value)} autoComplete="email" required fullWidth disabled={codeSent} />{codeSent && <><TextField label="Six-digit code" value={resetCode} onChange={(event) => setResetCode(event.target.value.replace(/\D/g, '').slice(0, 6))} inputProps={{ inputMode: 'numeric', maxLength: 6 }} required fullWidth /><TextField label="New password" type="password" value={newPassword} onChange={(event) => setNewPassword(event.target.value)} autoComplete="new-password" helperText="At least 10 characters with an uppercase letter and number" required fullWidth /><TextField label="Confirm new password" type="password" value={confirmPassword} onChange={(event) => setConfirmPassword(event.target.value)} autoComplete="new-password" required fullWidth /></>}<Button type="submit" variant="contained" size="large" disabled={submitting || !email || (codeSent && (resetCode.length !== 6 || !newPassword || !confirmPassword))} sx={{ minHeight: 48 }}>{submitting ? <CircularProgress size={24} color="inherit" /> : codeSent ? 'Change password' : 'Send reset code'}</Button>{codeSent && <Button type="button" onClick={() => { setCodeSent(false); setResetCode(''); setNotice('') }}>Request a new code</Button>}<Button type="button" onClick={() => { setRecovery(false); setCodeSent(false); setError(''); setNotice('') }}>Back to sign in</Button></Stack></Box>}
            <Typography variant="caption" color="text.secondary" display="block" textAlign="center" mt={4}>
              Authorized Carpenters Fiji staff only
            </Typography>
          </CardContent>
        </Card>
      </Box>
    </Box>
  )
}
