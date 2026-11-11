import { Alert, Box, Card, CardContent, Grid, Stack, Typography } from '@mui/material'

export type AssetUsage = {
  from: string; to: string; requestedDays: number; totalHours: number; inServiceHours: number
  rentedHours: number; availableHours: number; maintenanceHours: number; reservedHours: number
  otherUnavailableHours: number; utilisationPercent: number; downtimePercent: number
  rentalCount: number; maintenanceCount: number; estimatedRentalCount: number; conflictingHours: number
  basis: string; breakdown: { activity: string; hours: number; days: number; percent: number }[]
}
const colors: Record<string, string> = { Rented: '#2c6a98', Available: '#238552', Maintenance: '#b77812', Reserved: '#8473b4', 'Other unavailable': '#777' }
const number = (value: number) => value.toLocaleString('en-FJ', { maximumFractionDigits: 2 })
export function AssetUsagePanel({ usage }: { usage?: AssetUsage }) {
  if (!usage) return null
  return <Card variant="outlined"><CardContent sx={{ p: 3 }}>
    <Typography variant="h6" fontWeight={850}>Rental use and maintenance downtime</Typography>
    <Typography color="text.secondary" variant="body2" mt={0.5}>
      {new Date(usage.from).toLocaleString('en-FJ')} – {new Date(usage.to).toLocaleString('en-FJ')}
    </Typography>
    <Grid container spacing={2} mt={1}>
      {([
        ['In service', usage.inServiceHours, 'Rented + available'],
        ['Rented', usage.rentedHours, `${number(usage.utilisationPercent)}% of the period`],
        ['Available for hire', usage.availableHours, 'Ready time with no recorded hire or hold'],
        ['Maintenance downtime', usage.maintenanceHours, `${number(usage.downtimePercent)}% of the period`],
      ] as const).map(([label, hours, detail]) => <Grid key={label} size={{ xs: 12, sm: 6, lg: 3 }}>
        <Typography variant="body2" color="text.secondary">{label}</Typography>
        <Typography variant="h5" fontWeight={800}>{number(hours)} hrs</Typography>
        <Typography variant="body2">{number(hours / 24)} days</Typography>
        <Typography variant="caption" color="text.secondary">{detail}</Typography>
      </Grid>)}
    </Grid>
    <Stack direction="row" sx={{ height: 18, mt: 3, overflow: 'hidden', borderRadius: 1, bgcolor: 'grey.100' }} aria-label="Breakdown of recorded asset time">
      {usage.breakdown.filter(row => row.hours > 0).map(row => <Box key={row.activity} title={`${row.activity}: ${number(row.hours)} hours`} sx={{ width: `${usage.totalHours ? row.hours / usage.totalHours * 100 : 0}%`, bgcolor: colors[row.activity] }} />)}
    </Stack>
    <Stack direction="row" useFlexGap flexWrap="wrap" gap={2} mt={1}>
      {usage.breakdown.map(row => <Typography key={row.activity} variant="body2"><Box component="span" sx={{ display: 'inline-block', width: 9, height: 9, mr: 0.75, bgcolor: colors[row.activity] }} />{row.activity}: {number(row.hours)} hrs ({number(row.percent)}%)</Typography>)}
    </Stack>
    <Typography variant="body2" mt={2}>{usage.rentalCount} hires and {usage.maintenanceCount} maintenance jobs overlap this period. Compare rental use with downtime when reviewing whether to keep an asset in the hire fleet.</Typography>
    {usage.estimatedRentalCount > 0 && <Alert severity="info" sx={{ mt: 2 }}>{usage.estimatedRentalCount} hires use booked start times because pickup records are missing. These figures include estimates.</Alert>}
    {usage.conflictingHours > 0 && <Alert severity="warning" sx={{ mt: 2 }}>{number(usage.conflictingHours)} hours have both hire and maintenance records. They count as maintenance only. Review overlapping records before making a business decision.</Alert>}
    <Typography variant="caption" color="text.secondary" display="block" mt={2}>{usage.basis}</Typography>
  </CardContent></Card>
}
