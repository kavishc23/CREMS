import { Box, Chip, Grid, MenuItem, Stack, Switch, TextField, Typography } from '@mui/material'
import { capabilityOptions, capabilityValue, patchJson, readJson } from '../config/organizationSetup'

type OperationalSettings = { capabilities: number | string; isPublic: boolean; isActive: boolean }
export function DivisionControls({ value, onChange, compact = false }: { value: OperationalSettings; onChange: (patch: Partial<OperationalSettings>) => void; compact?: boolean }) {
  const flags = capabilityValue(value.capabilities)
  const selected = capabilityOptions.filter(([, bit]) => Boolean(flags & bit)).map(([, bit]) => String(bit))
  return <Stack gap={compact ? 1 : 2} mt={compact ? 0 : 2}>
    <TextField select fullWidth size={compact ? "small" : "medium"} label="Operational scope" helperText="Select the activities this division manages. Services below define the actual customer offering; staff permissions are managed in Staff & access." value={selected}
      SelectProps={{ multiple: true, renderValue: () => <Stack direction="row" gap={.5} flexWrap="wrap">{capabilityOptions.filter(([, bit]) => Boolean(flags & bit)).map(([label]) => <Chip key={label} size="small" label={label} />)}</Stack> }}
      onChange={event => { const values = typeof event.target.value === 'string' ? event.target.value.split(',') : event.target.value as string[]; const known = capabilityOptions.reduce((mask, [, bit]) => mask | bit, 0); onChange({ capabilities: values.reduce((mask, bit) => mask | Number(bit), flags & ~known) }) }}>
      {capabilityOptions.map(([label, bit]) => <MenuItem key={bit} value={String(bit)}>{label}</MenuItem>)}
    </TextField>
    <SettingSwitch label="Division active" detail="Allow staff to operate this division. Active hires and assets must be resolved before deactivation." checked={value.isActive} compact={compact} onChange={isActive => onChange({ isActive })} />
    <SettingSwitch label="List on customer website" detail="Publish this division's eligible services. The branch, service and asset must also be available for customer requests." checked={value.isPublic} compact={compact} onChange={isPublic => onChange({ isPublic })} />
  </Stack>
}
export function SettingSwitch({ label, detail, checked, onChange, disabled = false, compact = false }: { label: string; detail?: string; checked: boolean; onChange: (value: boolean) => void; disabled?: boolean; compact?: boolean }) {
  return <Stack direction="row" alignItems="center" justifyContent="space-between" gap={2} sx={{ py: compact ? .5 : 1, borderBottom: 1, borderColor: 'divider' }}><Box><Typography fontWeight={650}>{label}</Typography>{detail && <Typography variant="body2" color="text.secondary" sx={{ lineHeight: compact ? 1.35 : undefined }}>{detail}</Typography>}</Box><Switch checked={checked} disabled={disabled} onChange={e => onChange(e.target.checked)} slotProps={{ input: { 'aria-label': label } }} /></Stack>
}
export function DivisionBranding({ value, onChange, compact = false }: { value: string; onChange: (value: string) => void; compact?: boolean }) {
  const settings = readJson<Record<string, unknown>>(value, {})
  function field(key: string) { return typeof settings[key] === 'string' ? settings[key] as string : '' }
  return <Stack gap={compact ? 1 : 2}><Typography variant="body2" color="text.secondary" sx={{ lineHeight: compact ? 1.35 : undefined }}>Optional presentation for the customer division heading. Leave blank to use the division name and CREMS styling.</Typography><Grid container spacing={compact ? 1 : 2}>
    <Grid size={{ xs: 12, sm: 6 }}><TextField fullWidth size={compact ? "small" : "medium"} label="Customer display name" value={field('displayName')} inputProps={{ maxLength: 150 }} onChange={e => onChange(patchJson(value, { displayName: e.target.value }))} /></Grid>
    <Grid size={{ xs: 12, sm: 6 }}><TextField fullWidth size={compact ? "small" : "medium"} label="Accent colour" placeholder="#FFF000" value={field('accentColor')} error={Boolean(field('accentColor')) && !/^#[0-9a-f]{6}$/i.test(field('accentColor'))} helperText="Optional six-digit colour, e.g. #FFF000" onChange={e => onChange(patchJson(value, { accentColor: e.target.value }))} /></Grid>
    <Grid size={12}><TextField fullWidth size={compact ? "small" : "medium"} label="Logo URL" placeholder="https://…" value={field('logoUrl')} helperText="Use an HTTPS image address." onChange={e => onChange(patchJson(value, { logoUrl: e.target.value }))} /></Grid>
  </Grid></Stack>
}
