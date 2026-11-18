import { Box, Chip, Stack, Typography } from '@mui/material'
const labels: Record<string,string> = {
  body: 'Body / structure', brakes: 'Tyres, steering and brakes', safety: 'Lights and safety equipment',
  operation: 'Engine / controls and operation', guards: 'Guards and safety devices', accessories: 'Cables and accessories',
}
type InspectionReport = { checks?: Record<string,string>; meterReading?: number | null; fuelPercent?: number | null; assessment?: string; custody?: string; recordedBy?: string; recordedAt?: string; assetNumber?: string }
export function IncidentInspectionSummary({ json, outcome }: { json?: string | null; outcome?: string | null }) {
  let report: { checks?: Record<string,string>; meterReading?: number | null; fuelPercent?: number | null;
    assessment?: string; custody?: string; recordedBy?: string; recordedAt?: string; assetNumber?: string } | null = null
  try { const value: unknown = JSON.parse(json || 'null'); if (value && typeof value === 'object' && !Array.isArray(value)) report = value as InspectionReport } catch { /* Historical reports can lack a quick inspection. */ }
  if (!report) return null
  return <Box sx={{ mt: 1.5, p: 1.5, bgcolor: 'action.hover', borderRadius: 1 }}>
    <Typography fontWeight={700}>Staff condition inspection {report.assetNumber ? `· ${report.assetNumber}` : ''}</Typography>
    {outcome && <Chip size="small" variant="outlined" sx={{ my: 1 }} label={outcome === 'ContinueRental' ? 'Rental continues — normal return' : outcome === 'ReplaceAsset' ? 'Replacement handed over' : 'Hire ended — incident settlement'} />}
    <Stack spacing={0.5}>{Object.entries(report.checks ?? {}).map(([key,value]) => <Typography key={key} variant="body2">{labels[key] ?? key}: {value === 'NotInspected' ? 'Not inspected' : value === 'Issue' ? 'Issue found' : value}</Typography>)}</Stack>
    {report.meterReading != null && <Typography variant="body2">Meter: {report.meterReading}</Typography>}
    {report.fuelPercent != null && <Typography variant="body2">Fuel: {report.fuelPercent}%</Typography>}
    {report.assessment && <Typography variant="body2" sx={{ mt: 1, whiteSpace: 'pre-wrap' }}>{report.assessment}</Typography>}
    {report.custody && <Typography variant="body2">Custody: {report.custody}</Typography>}
    <Typography variant="caption" color="text.secondary">{report.recordedBy}{report.recordedAt ? ` · ${new Date(report.recordedAt).toLocaleString('en-FJ', { timeZone: 'Pacific/Fiji' })} Fiji time` : ''}</Typography>
  </Box>
}
