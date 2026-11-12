import { useState } from 'react'
import { Alert, Button, Stack, Typography } from '@mui/material'
import { api } from '../api/client'
import { apiErrorMessage } from '../api/errors'

export type BookingUpload = { id: string; type: string; fileName: string; createdAt?: string }
export function BookingDocumentUploads({ required, documents, assetId, bookingId, customer = false, onUploaded, onBusyChange }: {
  required: string[]; documents: BookingUpload[]; assetId?: string; bookingId?: string; customer?: boolean;
  onUploaded: (document: BookingUpload) => void | Promise<void>; onBusyChange?: (busy: boolean) => void;
}) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  async function upload(type: string, file?: File) {
    if (!file) return
    if (!/\.(pdf|jpe?g|png)$/i.test(file.name) || file.size <= 0 || file.size > 5242880) {
      setError('Choose a PDF, JPEG or PNG up to 5 MB.'); return
    }
    setError(''); setBusy(true); onBusyChange?.(true)
    try {
      const data = new FormData(); data.append('file', file); data.append('type', type)
      if (customer && assetId) data.append('assetId', assetId)
      if (customer && bookingId) data.append('bookingId', bookingId)
      const result = await api.post<BookingUpload>(customer ? '/customer-account/documents/upload' : `/bookings/${bookingId}/documents`, data)
      await onUploaded(result.data)
    } catch (reason) { setError(apiErrorMessage(reason, 'The document could not be uploaded. Try again.')) }
    finally { setBusy(false); onBusyChange?.(false) }
  }
  async function download(document: BookingUpload) {
    setError('')
    try {
      const { data } = await api.get(customer ? `/customer-account/documents/${document.id}/download` : `/bookings/${bookingId}/documents/${document.id}/download`, { responseType: 'blob' })
      const url = URL.createObjectURL(data); const link = window.document.createElement('a')
      link.href = url; link.download = document.fileName; link.click(); setTimeout(() => URL.revokeObjectURL(url), 1000)
    } catch (reason) { setError(apiErrorMessage(reason, 'This document could not be downloaded.')) }
  }
  return <Stack spacing={2}>
    {!!required.length && <><Typography fontWeight={750}>Required customer documents</Typography><Typography variant="body2" color="text.secondary">Attach each document required for this service. PDF, JPEG or PNG, up to 5 MB per file.</Typography></>}
    {error && <Alert severity="error">{error}</Alert>}
    {required.map(type => {
      const attached = documents.filter(x => x.type.toLowerCase() === type.toLowerCase())
      return <Stack key={type} spacing={1} sx={{ p: 2, border: 1, borderColor: 'divider', borderRadius: 1 }}>
        <Typography fontWeight={700}>{type} · {attached.length ? 'Attached' : 'Required — awaiting upload'}</Typography>
        <Button component="label" variant="outlined" disabled={busy}>{busy ? 'Uploading…' : attached.length ? `Upload another ${type}` : `Upload ${type}`}<input hidden aria-label={`Upload ${type}`} type="file" accept=".pdf,.jpg,.jpeg,.png" disabled={busy} onChange={event => { const file = event.target.files?.[0]; event.target.value = ''; void upload(type, file) }} /></Button>
      </Stack>
    })}
    {documents.map(document => <Stack key={document.id} direction={{ xs: 'column', sm: 'row' }} gap={1} justifyContent="space-between" sx={{ p: 2, border: 1, borderColor: 'divider', borderRadius: 1 }}>
      <div><Typography fontWeight={700}>{document.fileName}</Typography><Typography variant="caption">{document.type}{document.createdAt ? ` · ${new Date(document.createdAt).toLocaleDateString('en-FJ')}` : ''}</Typography></div>
      <Button disabled={busy} onClick={() => void download(document)}>Download</Button>
    </Stack>)}
  </Stack>
}
