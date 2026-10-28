import { useState, type FormEvent } from 'react'
import axios from 'axios'
import { Alert, Box, Button, Card, CardContent, Stack, TextField, Typography } from '@mui/material'
import { api } from '../api/client'
import { useAuth } from '../auth/AuthContext'

export function ChangePasswordPage() {
  const { refreshSession, logout } = useAuth(); const [currentPassword, setCurrentPassword] = useState(''); const [newPassword, setNewPassword] = useState(''); const [confirm, setConfirm] = useState(''); const [challengeId, setChallengeId] = useState(''); const [code, setCode] = useState(''); const [maskedDestination, setMaskedDestination] = useState('your email'); const [error, setError] = useState(''); const [saving, setSaving] = useState(false)
  async function submit(event: FormEvent) {
    event.preventDefault(); setError('')
    if (!challengeId && newPassword === currentPassword) return setError('Your new password must be different from your current password.')
    if (!challengeId && newPassword !== confirm) return setError('The new passwords do not match.')
    setSaving(true)
    try {
      if (!challengeId) { const response = await api.post<{ challengeId: string; maskedDestination: string }>('/auth/change-password/request', { currentPassword, newPassword }); setChallengeId(response.data.challengeId); setMaskedDestination(response.data.maskedDestination); return }
      await api.post('/auth/change-password/confirm', { challengeId, code, currentPassword, newPassword }); await refreshSession()
    } catch (requestError) { const data = axios.isAxiosError(requestError) ? requestError.response?.data : undefined; const errors = data?.errors as Record<string, string[]> | undefined; setError(errors ? Object.values(errors).flat().join(' ') : 'Unable to change the password.') } finally { setSaving(false) }
  }
  return <Box sx={{ minHeight: '100vh', bgcolor: '#f5f5f2', display: 'grid', placeItems: 'center', p: 2 }}><Card sx={{ width: '100%', maxWidth: 500 }}><CardContent sx={{ p: 4 }}><Box component="img" src="/brand/carpenters-logo.png" alt="Carpenters Fiji" sx={{ width: 56, height: 56, mb: 2 }} /><Typography variant="h4" fontWeight={800}>Create a new password</Typography><Typography color="text.secondary" mt={1} mb={3}>{challengeId ? `Enter the six-digit code sent to ${maskedDestination}.` : 'An administrator issued a temporary password. Replace it and verify the change by email before accessing CREMS.'}</Typography><Box component="form" onSubmit={submit}><Stack spacing={2}>{error && <Alert severity="error">{error}</Alert>}{!challengeId ? <><TextField required type="password" label="Temporary password" value={currentPassword} onChange={(e) => setCurrentPassword(e.target.value)} /><TextField required type="password" label="New password" helperText="Use at least 10 characters with an uppercase letter and a number." value={newPassword} onChange={(e) => setNewPassword(e.target.value)} /><TextField required type="password" label="Confirm new password" value={confirm} onChange={(e) => setConfirm(e.target.value)} /></> : <TextField autoFocus required label="Verification code" value={code} onChange={event => setCode(event.target.value.replace(/\D/g, '').slice(0, 6))} inputProps={{ inputMode: 'numeric', maxLength: 6 }} />}<Button type="submit" variant="contained" size="large" disabled={saving || (challengeId ? code.length !== 6 : newPassword.length < 10)}>{saving ? 'Please wait…' : challengeId ? 'Verify and continue' : 'Send verification code'}</Button><Button onClick={() => void logout()}>Sign out</Button></Stack></Box></CardContent></Card></Box>
}
