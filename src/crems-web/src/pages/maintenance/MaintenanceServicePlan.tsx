import { Alert, Box, Stack, Typography } from '@mui/material'
import { date, type ScheduledAsset } from './types'

export function MaintenanceServicePlan({ plan }: { plan: ScheduledAsset }) {
  return <Stack spacing={2}>
    <Typography fontWeight={700}>{plan.assetNumber} · {plan.name}</Typography>
    <Alert severity={plan.isOverdue ? 'warning' : 'info'}>{plan.isOverdue ? 'Service overdue' : plan.isDue ? 'Service due now' : !plan.nextServiceDate && plan.nextServiceMeter == null ? 'Service targets not set' : 'Upcoming service'}: the date or meter target reached first determines when service is due.</Alert>
    <Box><Typography>Next service date: {date(plan.nextServiceDate)}</Typography><Typography>Current meter: {plan.currentMeterReading ?? 'Not recorded'} {plan.meterUnit}</Typography><Typography>Next meter target: {plan.nextServiceMeter ?? 'Not configured'} {plan.meterUnit}</Typography><Typography variant="body2" color="text.secondary">{plan.meterTargetSource ?? 'Recorded service target'}</Typography></Box>
    <Typography>Configured meter interval: {plan.meterInterval == null ? 'Not configured' : `${plan.meterInterval} ${plan.meterUnit ?? ''}`}</Typography>
    <Typography>Repeat service: {plan.serviceIntervalMonths == null ? 'No monthly interval recorded' : `Every ${plan.serviceIntervalMonths} months after completion`}</Typography>
    {plan.nextServiceMeter != null && plan.currentMeterReading != null && <Typography>{plan.currentMeterReading > plan.nextServiceMeter ? `${plan.currentMeterReading - plan.nextServiceMeter} ${plan.meterUnit ?? ''} beyond the target` : plan.currentMeterReading === plan.nextServiceMeter ? 'Meter target reached' : `${plan.nextServiceMeter - plan.currentMeterReading} ${plan.meterUnit ?? ''} until the target`}</Typography>}
    {plan.ruleWarning && <Alert severity="warning">{plan.ruleWarning}</Alert>}
    <Typography variant="body2" color="text.secondary">A completed service target takes precedence over the configured meter interval. The initial interval starts from zero; later intervals use the recorded completed-service meter. An explicit next date takes precedence over monthly recurrence. Record updated targets when completing work. Administrators manage the base interval in the service offering.</Typography>
    <Typography variant="body2" color="text.secondary">Automatic jobs are created for available assets or assets already under maintenance. Hired and transferred assets require staff review. Existing open work prevents duplicate jobs.</Typography>
  </Stack>
}
