import { Alert, Button, Checkbox, FormControlLabel, MenuItem, Stack, TextField, Typography } from '@mui/material'
import { taxCategories, type TaxMode, type TaxSettings } from './maintenanceTax'

export function MaintenanceTaxFields({ value, onChange, manualTax, onManualTax, defaultRate, issuedStockCost, tax, taxable }: {
  value: TaxSettings; onChange: (settings: TaxSettings) => void; manualTax: string; onManualTax: (value: string) => void
  defaultRate: number; issuedStockCost: number; tax: number; taxable: number
}) {
  const automatic = value.mode === 'Exclusive' || value.mode === 'Inclusive'
  return <Stack spacing={1.5}>
    <TextField select label="Tax treatment" value={value.mode} onChange={event => onChange({ ...value, mode: event.target.value as TaxMode, rate: value.mode === 'Manual' && Number(value.rate) === 0 ? String(defaultRate) : value.rate })}>
      <MenuItem value="Exclusive">Tax-exclusive — add tax</MenuItem>
      <MenuItem value="Inclusive">Tax-inclusive — tax already included</MenuItem>
      <MenuItem value="None">No tax</MenuItem>
      <MenuItem value="Manual">Manual tax override — add invoice tax</MenuItem>
    </TextField>
    {automatic && <>
      <Stack direction="row" gap={1} alignItems="center">
        <TextField required type="number" label="Tax rate (%)" value={value.rate} inputProps={{ min: 0, max: 100, step: 0.000001 }} onChange={event => onChange({ ...value, rate: event.target.value })} />
        <Button onClick={() => onChange({ ...value, rate: String(defaultRate) })}>Use division rate ({defaultRate}%)</Button>
      </Stack>
      <Typography variant="body2">Select only taxable supplier expenses. Leave internal costs and non-taxable expenses unselected. Split mixed taxable and non-taxable amounts into separate expense categories.</Typography>
      <Stack direction="row" flexWrap="wrap">{taxCategories.map(category => <FormControlLabel key={category.flag} label={category.label}
        control={<Checkbox checked={Boolean(value.categories & category.flag)} onChange={event => onChange({ ...value, categories: event.target.checked ? value.categories | category.flag : value.categories & ~category.flag })} />} />)}</Stack>
      <Typography variant="body2">Issued stock excluded from taxable parts: FJD {issuedStockCost.toFixed(2)}. Selected taxable expenses: FJD {taxable.toFixed(2)}.</Typography>
    </>}
    {value.mode === 'Manual' && <>
      <Alert severity="info">Use expense amounts before tax. The manual tax amount is added once. Existing recorded tax is preserved; enter a reason when changing it.</Alert>
      <TextField type="number" label="Tax (FJD)" value={manualTax} inputProps={{ min: 0, max: 100000000, step: 0.01 }} onChange={event => onManualTax(event.target.value)} />
      <TextField label="Tax override reason" value={value.reason} inputProps={{ maxLength: 1000 }} onChange={event => onChange({ ...value, reason: event.target.value })} />
    </>}
    <Typography role="status" variant="body2">{value.mode === 'Inclusive' ? 'Included tax' : 'Tax'}: FJD {tax.toFixed(2)}{value.mode === 'Inclusive' ? ' (already in expenses; not added again)' : ''}</Typography>
  </Stack>
}
