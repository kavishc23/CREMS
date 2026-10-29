import { Accordion, AccordionDetails, AccordionSummary, Box, Button, Chip, Stack, TextField, MenuItem, Typography } from '@mui/material'
import ExpandMoreOutlined from '@mui/icons-material/ExpandMoreOutlined'

export function RentalChecklist({items,passed,failed,onChange}:{items:string[];passed:string[];failed:string[];onChange:(passed:string[],failed:string[])=>void}) {
  const groups = new Map<string, string[]>()
  items.forEach(label => { const group = label.includes(':') ? label.split(':')[0] : 'General inspection'; groups.set(group, [...(groups.get(group) ?? []), label]) })
  const result = (label: string, value: string) => onChange(value === 'Pass' ? [...passed.filter(item=>item!==label),label] : passed.filter(item=>item!==label), value === 'Fail' ? [...failed.filter(item=>item!==label),label] : failed.filter(item=>item!==label))
  return <Stack spacing={1}>
    <Stack direction="row" alignItems="center" justifyContent="space-between" gap={1} flexWrap="wrap"><Typography variant="body2" color="text.secondary">{passed.length + failed.length} / {items.length} checked · {failed.length} issues</Typography><Button size="small" variant="outlined" onClick={()=>onChange([...items],[])}>Mark all as passed</Button></Stack>
    {Array.from(groups.entries()).map(([group,labels])=><Accordion key={group} defaultExpanded={groups.size===1} disableGutters elevation={0} sx={{border:1,borderColor:'divider',borderRadius:1,'&:before':{display:'none'}}}>
      <AccordionSummary expandIcon={<ExpandMoreOutlined/>}><Stack direction="row" alignItems="center" gap={1.5}><Typography fontWeight={750}>{group}</Typography><Chip size="small" label={labels.filter(label=>passed.includes(label)||failed.includes(label)).length+'/'+labels.length} />{labels.some(label=>failed.includes(label))&&<Chip size="small" color="error" label="Issue recorded"/>}</Stack></AccordionSummary>
      <AccordionDetails sx={{pt:0}}><Box sx={{display:'grid',gridTemplateColumns:{xs:'1fr',md:'1fr 1fr'},columnGap:3}}>{labels.map(label=><Stack key={label} direction="row" gap={1.5} alignItems="center" justifyContent="space-between" sx={{py:1,borderBottom:1,borderColor:'divider'}}><Typography variant="body2">{label.includes(':')?label.slice(label.indexOf(':')+1).trim():label}</Typography><TextField select size="small" value={passed.includes(label)?'Pass':failed.includes(label)?'Fail':''} onChange={event=>result(label,event.target.value)} inputProps={{'aria-label':label}} sx={{minWidth:112}}><MenuItem value="">Unchecked</MenuItem><MenuItem value="Pass">Passed</MenuItem><MenuItem value="Fail">Issue found</MenuItem></TextField></Stack>)}</Box></AccordionDetails>
    </Accordion>)}
  </Stack>
}
