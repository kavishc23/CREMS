import { Chip, Stack, Typography } from '@mui/material'
import { closed, date, label, type Job } from './types'

export function MaintenanceStatusDisplay({ job }: { job: Job }) {
  const status = job.status === 'Open' ? 'Open / not started' : job.status === 'Completed' ? 'Work completed' : label(job.status)
  const color = job.status === 'Completed' ? 'success' : job.status === 'InProgress' ? 'info' : job.status === 'WaitingForParts' ? 'warning' : 'default'
  return <Stack spacing={0.5} alignItems="flex-start">
    <Chip label={status} color={color} variant="outlined" />
    {closed(job) && <Typography variant="caption" color="text.secondary">
      {job.releasedAt ? `Safety passed: ${date(job.releasedAt, true)}` : 'Awaiting safety check'}
    </Typography>}
  </Stack>
}
