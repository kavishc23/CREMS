import { Alert, Grid, MenuItem, Stack, TextField, Typography } from '@mui/material'
import { readJson, serviceTracking, setServiceTracking, type ServiceSettings } from '../config/organizationSetup'

type Requirements = Pick<ServiceSettings, 'meterType' | 'inspectionRequirementsJson' | 'maintenanceRulesJson'>
export function MaintenanceSchedule({ value, onChange }: { value: Requirements; onChange: (patch: Partial<Requirements>) => void }) {
  const rules = readJson<Record<string, number>>(value.maintenanceRulesJson, {})
  const mode = rules.meterInterval != null ? rules.serviceIntervalDays != null ? 'both' : 'usage' : rules.serviceIntervalDays != null ? 'calendar' : 'manual'
  const usesMeter = mode === 'usage' || mode === 'both'
  function changeMode(next: string) {
    const nextRules = { ...rules }
    if (next === 'usage' || next === 'both') nextRules.meterInterval ??= 250; else delete nextRules.meterInterval
    if (next === 'calendar' || next === 'both') nextRules.serviceIntervalDays ??= 90; else delete nextRules.serviceIntervalDays
    onChange({ maintenanceRulesJson: JSON.stringify(nextRules), ...(next === 'usage' || next === 'both' ? { inspectionRequirementsJson: JSON.stringify({ ...readJson<Record<string, unknown>>(value.inspectionRequirementsJson, {}), tracksMeter: true }) } : {}) })
  }
  function interval(key: string, raw: string) { const next = { ...rules }; next[key] = raw ? Number(raw) : 0; onChange({ maintenanceRulesJson: JSON.stringify(next) }) }
  return <Stack gap={2}><TextField select fullWidth label="When should routine maintenance be due?" value={mode} onChange={e => changeMode(e.target.value)}>
    <MenuItem value="manual">On demand — staff create work when needed</MenuItem><MenuItem value="calendar">By calendar — after a number of days</MenuItem><MenuItem value="usage">By usage — after a measured amount of use</MenuItem><MenuItem value="both">By calendar or usage — whichever comes first</MenuItem>
  </TextField><Grid container spacing={2}>
    {(mode === 'calendar' || mode === 'both') && <Grid size={{ xs: 12, sm: 6 }}><TextField required fullWidth type="number" label="Every (days)" value={rules.serviceIntervalDays || ''} inputProps={{ min: 1, max: 3650 }} onChange={e => interval('serviceIntervalDays', e.target.value)} /></Grid>}
    {usesMeter && <><Grid size={{ xs: 12, sm: 6 }}><TextField required fullWidth type="number" label="Every (usage units)" value={rules.meterInterval || ''} inputProps={{ min: 1, max: 100000000 }} onChange={e => interval('meterInterval', e.target.value)} /></Grid><Grid size={12}><TextField required fullWidth label="Usage unit" placeholder="e.g. hours, kilometres, cycles" value={value.meterType ?? ''} onChange={e => onChange({ meterType: e.target.value || null })} helperText="Only choose usage scheduling for assets with a measurable counter." /></Grid></>}
  </Grid><Typography variant="body2" color="text.secondary">Calendar schedules suit premises, furniture, site assets and periodic safety checks. Usage schedules suit any asset with a counter. Existing work orders and explicit service dates remain in effect.</Typography></Stack>
}
export function InspectionReadings({ value, onChange }: { value: Requirements; onChange: (patch: Partial<Requirements>) => void }) {
  const tracking = serviceTracking(value), schedule = readJson<Record<string, number>>(value.maintenanceRulesJson, {})
  const requirements = readJson<Record<string, unknown>>(value.inspectionRequirementsJson, {})
  const inherited = requirements.tracksMeter == null && requirements.tracksFuel == null
  const mode = inherited ? 'inherit' : tracking.meter ? tracking.fuel ? 'both' : 'usage' : tracking.fuel ? 'fuel' : 'none'
  return <Stack gap={2}><TextField select fullWidth label="Additional readings at handover and return" value={mode} onChange={e => { const next = e.target.value; onChange(setServiceTracking(value, next === 'usage' || next === 'both', next === 'fuel' || next === 'both')) }} helperText="Condition, quantity, keys, documents and other checks belong in the category checklists below.">
    {inherited && <MenuItem value="inherit">Use existing asset and category defaults</MenuItem>}<MenuItem value="none" disabled={schedule.meterInterval != null}>None — use the condition checklists</MenuItem><MenuItem value="usage">Usage counter</MenuItem><MenuItem value="fuel" disabled={schedule.meterInterval != null}>Fuel / battery level</MenuItem><MenuItem value="both">Usage counter and fuel / battery level</MenuItem>
  </TextField>{!inherited && tracking.meter && <TextField required label="Usage unit" value={value.meterType ?? ''} placeholder="e.g. kilometres, operating hours, cycles" onChange={e => onChange({ meterType: e.target.value || null })} />}
  {schedule.meterInterval != null && <Alert severity="info">A usage counter is enabled by the maintenance schedule. Change that schedule first to stop collecting usage readings.</Alert>}</Stack>
}
