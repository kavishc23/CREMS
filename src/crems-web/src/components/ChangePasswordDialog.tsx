import { useState, type FormEvent } from 'react'
import axios from 'axios'
import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, Stack, TextField, Typography } from '@mui/material'
import { api } from '../api/client'

type Props = { open: boolean; endpoint: string; requireOtp?: boolean; onClose: () => void; onChanged?: () => void }

export function ChangePasswordDialog(props: Props) { return props.open ? <ChangePasswordContent {...props}/> : null }

function ChangePasswordContent({ open, endpoint, requireOtp = false, onClose, onChanged }: Props) {
  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [error, setError] = useState('')
  const [saving, setSaving] = useState(false)
  const [challengeId, setChallengeId] = useState('')
  const [code, setCode] = useState('')
  const [maskedDestination, setMaskedDestination] = useState('your email address')


  async function submit(event: FormEvent) {
    event.preventDefault(); setError('')
    if (newPassword === currentPassword) return setError('Your new password must be different from your current password.')
    if (newPassword !== confirmPassword) return setError('The new passwords do not match.')
    if (newPassword.length < 10 || !/[A-Z]/.test(newPassword) || !/\d/.test(newPassword)) return setError('Use at least 10 characters with an uppercase letter and a number.')
    setSaving(true)
    try {
      if (requireOtp && !challengeId) {
        const response = await api.post<{ challengeId: string; maskedDestination: string }>(`${endpoint}/request`, { currentPassword, newPassword })
        setChallengeId(response.data.challengeId); setMaskedDestination(response.data.maskedDestination); return
      }
      await api.post(requireOtp ? `${endpoint}/confirm` : endpoint, requireOtp ? { challengeId, code, currentPassword, newPassword } : { currentPassword, newPassword })
      onChanged?.(); onClose()
    } catch (reason) {
      const data = axios.isAxiosError(reason) ? reason.response?.data : undefined
      const errors = data?.errors as Record<string, string[]> | undefined
      setError(errors ? Object.values(errors).flat().join(' ') : 'Unable to change your password. Check your current password and try again.')
    } finally { setSaving(false) }
  }

  return <Dialog open={open} onClose={() => !saving && onClose()} fullWidth maxWidth="xs">
    <DialogTitle>Change password</DialogTitle>
    <DialogContent dividers><Stack component="form" id="change-password-form" onSubmit={submit} spacing={2} sx={{ pt: .5 }}>
      <Typography color="text.secondary">{challengeId ? `Enter the six-digit code sent to ${maskedDestination}. It expires in 10 minutes.` : 'Enter your current password, then choose a different password for future sign-ins.'}</Typography>
      {error && <Alert severity="error">{error}</Alert>}
      {!challengeId ? <>
        <TextField autoFocus required type="password" autoComplete="current-password" label="Current password" value={currentPassword} onChange={event => setCurrentPassword(event.target.value)} />
        <TextField required type="password" autoComplete="new-password" label="New password" helperText="At least 10 characters with an uppercase letter and a number." value={newPassword} onChange={event => setNewPassword(event.target.value)} />
        <TextField required type="password" autoComplete="new-password" label="Confirm new password" value={confirmPassword} onChange={event => setConfirmPassword(event.target.value)} />
      </> : <TextField autoFocus required label="Verification code" value={code} onChange={event => setCode(event.target.value.replace(/\D/g, '').slice(0, 6))} inputProps={{ inputMode: 'numeric', maxLength: 6, style: { textAlign: 'center', fontSize: 25, letterSpacing: '.25em' } }} />}
    </Stack></DialogContent>
    <DialogActions sx={{ p: 2.5 }}>{challengeId && <Button onClick={() => { setChallengeId(''); setCode(''); setError('') }} disabled={saving}>Back</Button>}<Button onClick={onClose} disabled={saving}>Cancel</Button><Button type="submit" form="change-password-form" variant="contained" disabled={saving || (challengeId ? code.length !== 6 : !currentPassword || !newPassword || !confirmPassword)}>{saving ? 'Please wait…' : challengeId ? 'Verify and change password' : requireOtp ? 'Send verification code' : 'Change password'}</Button></DialogActions>
  </Dialog>
}
