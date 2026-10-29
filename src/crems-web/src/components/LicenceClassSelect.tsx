import { Checkbox, FormControl, InputLabel, MenuItem, Select } from '@mui/material'
import { useId } from 'react'

export function LicenceClassSelect({ value, onChange, disabled = false }: { value: string; onChange: (value: string) => void; disabled?: boolean }) {
  const id = useId()
  const selected = value.split(',').map(item => Number(item.trim())).filter(item => Number.isInteger(item) && item >= 1 && item <= 8)
  return <FormControl required fullWidth disabled={disabled}>
    <InputLabel id={id}>Licence classes</InputLabel>
    <Select labelId={id} multiple label="Licence classes" value={selected} renderValue={values => values.join(', ')}
      onChange={event => { const values = typeof event.target.value === 'string' ? event.target.value.split(',').map(Number) : event.target.value; onChange([...new Set(values)].sort((a,b)=>a-b).join(',')) }}
      MenuProps={{ PaperProps: { sx: { mt: .5, border: 1, borderColor: 'divider', borderRadius: 2, '& .MuiList-root': { display: 'grid', gridTemplateColumns: 'repeat(3, minmax(72px, 1fr))', gap: .25, p: 1.25 } } } }}>
      {[1,2,3,4,5,6,7,8].map(item=><MenuItem key={item} value={item} sx={{ minHeight: 40, gap: .5, px: 1 }}><Checkbox size="small" checked={selected.includes(item)} sx={{ p: .5 }}/>{item}</MenuItem>)}
    </Select>
  </FormControl>
}
