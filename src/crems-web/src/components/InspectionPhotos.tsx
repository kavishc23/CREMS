import { useRef, useState, type ChangeEvent } from 'react'
import { Alert, Box, Button, Stack, Typography } from '@mui/material'

export function InspectionPhotos({ photos, onChange, onBusyChange }: { photos: string[]; onChange: (photos: string[]) => void; onBusyChange?: (busy: boolean) => void }) {
  const inputRef = useRef<HTMLInputElement>(null)
  const [reading, setReading] = useState(false)
  const [error, setError] = useState('')
  async function attach(event: ChangeEvent<HTMLInputElement>) {
    const files = Array.from(event.currentTarget.files ?? [])
    event.currentTarget.value = ''
    if (!files.length) return
    setError('')
    if (photos.length + files.length > 5) { setError('Attach up to five photos. Remove an existing photo first.'); return }
    if (files.some(file => !file.type.startsWith('image/'))) { setError('Choose image files only.'); return }
    if (files.some(file => file.size > 20 * 1024 * 1024)) { setError('Each source photo must be 20 MB or smaller.'); return }
    setReading(true); onBusyChange?.(true)
    try {
      const added = await Promise.all(files.map(async file => {
        const url = URL.createObjectURL(file)
        try {
          const image = new Image()
          image.src = url
          await image.decode()
          const scale = Math.min(1, 1600 / Math.max(image.naturalWidth, image.naturalHeight))
          const canvas = document.createElement('canvas')
          canvas.width = Math.max(1, Math.round(image.naturalWidth * scale))
          canvas.height = Math.max(1, Math.round(image.naturalHeight * scale))
          const context = canvas.getContext('2d')
          if (!context) throw new Error('Image processing is unavailable in this browser.')
          context.fillStyle = '#fff'
          context.fillRect(0, 0, canvas.width, canvas.height)
          context.drawImage(image, 0, 0, canvas.width, canvas.height)
          const encoded = canvas.toDataURL('image/jpeg', 0.82)
          if (encoded.length > 3 * 1024 * 1024) throw new Error('This photo is too large after resizing. Choose a smaller image.')
          return encoded
        } catch {
          throw new Error('This photo could not be opened. Choose a JPEG, PNG or WebP image. Convert HEIC photos to JPEG first.')
        } finally { URL.revokeObjectURL(url) }
      }))
      onChange([...photos, ...added])
    } catch (reason) { setError(reason instanceof Error ? reason.message : 'Photos could not be attached. Please try again.') }
    finally { setReading(false); onBusyChange?.(false) }
  }
  return <Stack spacing={1}>
    <Button type="button" variant="outlined" onClick={() => inputRef.current?.click()} disabled={reading || photos.length >= 5}>
      {reading ? 'Reading photos…' : 'Attach inspection photos'}

    </Button>
    <input ref={inputRef} aria-label="Inspection photo files" hidden multiple type="file" accept="image/jpeg,image/png,image/webp" onChange={event => void attach(event)} />
    <Typography variant="caption">{photos.length} of 5 photos attached · up to 20 MB each; photos are resized for upload</Typography>
    {error && <Alert severity="error">{error}</Alert>}
    <Stack direction="row" gap={1} flexWrap="wrap">
      {photos.map((photo, index) => <Box key={index}>
        <Box component="img" src={photo} alt={`Inspection photo ${index + 1}`} sx={{ width: 120, height: 90, objectFit: 'cover', display: 'block' }} />
        <Button size="small" disabled={reading} onClick={() => onChange(photos.filter((_, i) => i !== index))}>Remove photo {index + 1}</Button>
      </Box>)}
    </Stack>
  </Stack>
}
