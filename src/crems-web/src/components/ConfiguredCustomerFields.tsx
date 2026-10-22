import { Grid, MenuItem, TextField } from '@mui/material'
import type { CustomerQuestion } from '../config/organizationSetup'

export function ConfiguredCustomerFields({ questions, answers, onChange }: { questions: CustomerQuestion[]; answers: Record<string, string>; onChange: (answers: Record<string, string>) => void }) {
  return <Grid container spacing={2}>{questions.map(q => <Grid key={q.code} size={{ xs: 12, sm: q.type === 'LongText' ? 12 : 6 }}>
    <TextField fullWidth required={q.required} label={q.label} helperText={q.help || undefined}
      value={answers[q.code] ?? ''} onChange={e => onChange({ ...answers, [q.code]: e.target.value })}
      select={q.type === 'Choice' || q.type === 'Boolean'} type={q.type === 'Number' || q.type === 'Integer' ? 'number' : q.type === 'Date' ? 'date' : 'text'}
      inputProps={{ maxLength: q.type === 'LongText' ? 2000 : 250, step: q.type === 'Integer' ? 1 : 'any' }}
      multiline={q.type === 'LongText'} minRows={q.type === 'LongText' ? 3 : undefined} InputLabelProps={q.type === 'Date' ? { shrink: true } : undefined}>
      {q.type === 'Choice' && (q.options ?? []).map(option => <MenuItem key={option} value={option}>{option}</MenuItem>)}
      {q.type === 'Boolean' && [<MenuItem key="yes" value="true">Yes</MenuItem>, <MenuItem key="no" value="false">No</MenuItem>]}
    </TextField>
  </Grid>)}</Grid>
}
