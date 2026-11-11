import { Alert, Box, Button, Stack, Typography } from '@mui/material'
import { useState } from 'react'
const allowed = /^data:(image\/(?:png|jpeg|webp)|application\/pdf);base64,/
export function IncidentAttachments({ value, onChange, onBusyChange }: { value: string[]; onBusyChange?: (busy: boolean) => void; onChange?: (value: string[]) => void }) {
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  async function attach(files: FileList | null) {
    if (!files) return
    setError(''); setBusy(true); onBusyChange?.(true)
    try {
      if (value.length + files.length > 5) throw new Error('Attach up to 5 files.')
      const added = await Promise.all(Array.from(files).map(async file => {
        if (!['image/png', 'image/jpeg', 'image/webp', 'application/pdf'].includes(file.type) || file.size > 2 * 1024 * 1024) throw new Error('Use JPG, PNG, WebP or PDF files up to 2 MB each.')
        return await new Promise<string>((resolve, reject) => { const reader = new FileReader(); reader.onload = () => resolve(String(reader.result)); reader.onerror = reject; reader.readAsDataURL(file) })
      }))
      onChange?.([...value, ...added])
    } catch (reason) { setError(reason instanceof Error ? reason.message : 'Files could not be attached.') } finally { setBusy(false); onBusyChange?.(false) }
  }
  return <Stack spacing={1}>{onChange && <><Button component="label" variant="outlined" disabled={busy}>{busy ? 'Reading attachments…' : 'Attach photos or documents'}<input hidden type="file" multiple accept="image/jpeg,image/png,image/webp,application/pdf" onChange={event => { void attach(event.target.files); event.target.value = '' }} /></Button><Typography variant="caption">Up to 5 photos or PDF documents, 2 MB each. Wait for the attachments to appear before submitting.</Typography></>}{error && <Alert severity="error">{error}</Alert>}{value.filter(url => allowed.test(url)).map((url, index) => <Box key={index}>{url.startsWith('data:image/') && <Box component="img" src={url} alt={`Incident photo ${index + 1}`} sx={{ width: 180, height: 120, objectFit: 'contain', display: 'block' }} />}<Button component="a" href={url} download={`incident-evidence-${index + 1}.${url.startsWith('data:application/pdf') ? 'pdf' : url.startsWith('data:image/png') ? 'png' : url.startsWith('data:image/webp') ? 'webp' : 'jpg'}`}>Download attachment {index + 1}</Button>{onChange && <Button color="error" onClick={() => onChange(value.filter((_, i) => i !== index))}>Remove</Button>}</Box>)}</Stack>
}
export function incidentEvidence(json: string | null | undefined): string[] {
  try { const data: unknown = JSON.parse(json || '[]'); return Array.isArray(data) ? data.filter((item): item is string => typeof item === 'string' && allowed.test(item)) : [] } catch { return [] }
}
