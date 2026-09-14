import { useState, type ChangeEvent } from 'react'
import { Alert, Box, Button, Stack, Typography } from '@mui/material'

export function InspectionPhotos({ photos, onChange }: { photos: string[]; onChange: (photos: string[]) => void }) {
  const [reading, setReading] = useState(false)
  const [error, setError] = useState('')
  async function attach(event: ChangeEvent<HTMLInputElement>) {
    const files = Array.from(event.currentTarget.files ?? [])
    event.currentTarget.value = ''
    if (!files.length) return
    setError('')
    if (photos.length + files.length > 5) { setError('Attach up to five photos. Remove an existing photo first.'); return }
    if (files.some(file => !file.type.startsWith('image/'))) { setError('Choose image files only.'); return }
    if (files.some(file => file.size > 3 * 1024 * 1024)) { setError('Each photo must be 3 MB or smaller. Resize larger photos and try again.'); return }
    setReading(true)
    try {
      const added = await Promise.all(files.map(file => new Promise<string>((resolve, reject) => {
        const reader = new FileReader()
        reader.onload = () => resolve(String(reader.result))
        reader.onerror = () => reject(new Error('A photo could not be read. Please select it again.'))
        reader.onabort = () => reject(new Error('Photo reading was interrupted. Please try again.'))
        reader.readAsDataURL(file)
      })))
      onChange([...photos, ...added])
    } catch (reason) { setError(reason instanceof Error ? reason.message : 'Photos could not be attached. Please try again.') }
    finally { setReading(false) }
  }
  return <Stack spacing={1}>
    <Button component="label" variant="outlined" disabled={reading || photos.length >= 5}>
      {reading ? 'Reading photos…' : 'Attach inspection photos'}
      <input hidden multiple type="file" accept="image/*" onChange={event => void attach(event)} />
    </Button>
    <Typography variant="caption">{photos.length} of 5 photos attached · maximum 3 MB each</Typography>
    {error && <Alert severity="error">{error}</Alert>}
    <Stack direction="row" gap={1} flexWrap="wrap">
      {photos.map((photo, index) => <Box key={index}>
        <Box component="img" src={photo} alt={`Inspection photo ${index + 1}`} sx={{ width: 120, height: 90, objectFit: 'cover', display: 'block' }} />
        <Button size="small" disabled={reading} onClick={() => onChange(photos.filter((_, i) => i !== index))}>Remove photo {index + 1}</Button>
      </Box>)}
    </Stack>
  </Stack>
}
