import { useEffect, useState } from 'react'
import axios from 'axios'
import { Alert, Box, Button, Card, CardContent, Checkbox, FormControlLabel, Grid, MenuItem, Stack, TextField, Typography } from '@mui/material'
import { api } from '../api/client'

type Flow = {
  personnelRequirement: 'None'|'Optional'|'Required'; quoteMode: 'Automatic'|'Always'|'DirectWhenPriced'
  onlineRequestsEnabled: boolean; businessCustomersRequireQuote: boolean
  unpricedPersonnelRequiresQuote: boolean; unpricedDeliveryRequiresQuote: boolean
  requireVerifiedLicenceForVehicles: boolean; minimumHireDays: number; maximumHireDays: number
  quoteValidityDays: number
}
type Policy = {id:string;name:string;flow:Flow;defaultBondAmount:number;defaultTaxRate:number;defaultCurrency:string;defaultRentalTerms:string|null}
type DivisionOption = {id:string;name:string;isActive:boolean;services:{isActive:boolean;isBookableOnline:boolean;requiresQuote:boolean}[]}

export function DivisionBookingFlowPage(){
  const [divisions,setDivisions]=useState<DivisionOption[]>([]),[divisionId,setDivisionId]=useState('')
  const [policy,setPolicy]=useState<Policy|null>(null),[loading,setLoading]=useState(false),[saving,setSaving]=useState(false)
  const [error,setError]=useState(''),[saved,setSaved]=useState('')
  useEffect(()=>{void api.get<DivisionOption[]>('/divisions').then(response=>{
    setDivisions(response.data);setDivisionId(current=>current||response.data.find(x=>x.isActive)?.id||response.data[0]?.id||'')
  }).catch(()=>setError('Divisions could not be loaded.'))},[])
  useEffect(()=>{if(!divisionId){setPolicy(null);return}let active=true;setLoading(true);setError('');setSaved('')
    void api.get<Policy>(`/divisions/${divisionId}/booking-flow`).then(response=>{if(active)setPolicy(response.data)})
      .catch(()=>{if(active)setError('Booking flow could not be loaded for this division.')})
      .finally(()=>{if(active)setLoading(false)})
    return()=>{active=false}
  },[divisionId])
  const updateFlow=(change:Partial<Flow>)=>setPolicy(current=>current?{...current,flow:{...current.flow,...change}}:current)
  const selectedDivision=divisions.find(item=>item.id===divisionId)
  const onlineServices=selectedDivision?.services?.filter(item=>item.isActive&&(item.isBookableOnline||item.requiresQuote)).length??0
  async function save(){if(!policy)return;setError('');setSaved('')
    if(policy.flow.minimumHireDays<1||policy.flow.maximumHireDays>366||policy.flow.maximumHireDays<policy.flow.minimumHireDays||policy.flow.quoteValidityDays<1||policy.flow.quoteValidityDays>90||policy.defaultBondAmount<0||policy.defaultTaxRate<0||policy.defaultTaxRate>100||policy.defaultCurrency.trim().length!==3){setError('Check hire limits, quote validity, bond, VAT and the three-letter currency code.');return}
    setSaving(true)
    try{const response=await api.put<Policy>(`/divisions/${divisionId}/booking-flow`,{flow:policy.flow,defaultBondAmount:policy.defaultBondAmount,defaultTaxRate:policy.defaultTaxRate,defaultCurrency:policy.defaultCurrency,defaultRentalTerms:policy.defaultRentalTerms});setPolicy(response.data);setSaved(`${response.data.name} booking flow saved. New customer requests now use these rules.`)}
    catch(reason){const data=axios.isAxiosError(reason)?reason.response?.data:undefined;setError(data?.message??'The booking flow could not be saved.')}
    finally{setSaving(false)}
  }
  return <Box sx={{p:{xs:2,sm:3,lg:4},maxWidth:1200}}>
    <Typography variant="h5" fontWeight={800}>Booking & quotation flow</Typography>
    <Typography color="text.secondary" mt={.5} mb={2}>One policy per division. Every asset inherits its division’s bond and driver/operator rule unless it has an explicit asset override. Existing bookings keep their agreed amounts.</Typography>
    <TextField select fullWidth disabled={saving} label="Division" value={divisionId} onChange={event=>setDivisionId(event.target.value)} sx={{maxWidth:500,mb:2}}>{divisions.map(item=><MenuItem key={item.id} value={item.id}>{item.name}{item.isActive?'':' (inactive)'}</MenuItem>)}</TextField>
    {error&&<Alert severity="error" sx={{mb:2}}>{error}</Alert>}{saved&&<Alert severity="success" sx={{mb:2}}>{saved}</Alert>}
    {policy?.flow.onlineRequestsEnabled&&onlineServices===0&&<Alert severity="warning" sx={{mb:2}}>This division has no active online services. Enable a service in Divisions &amp; services, then enable it at a branch before customers can request its assets.</Alert>}
    {loading&&<Typography>Loading division settings…</Typography>}
    {!loading&&policy&&<Stack gap={2}>
      <Card variant="outlined"><CardContent><Typography variant="h6" fontWeight={750} mb={.5}>Customer request route</Typography><Typography variant="body2" color="text.secondary" mb={2}>These rules decide how eligible customer requests are reviewed. Each service controls which customer actions are shown.</Typography>
        <Grid container spacing={2}>
          <Grid size={{xs:12,sm:6}}><TextField select fullWidth label="Quotation route" value={policy.flow.quoteMode} onChange={event=>updateFlow({quoteMode:event.target.value as Flow['quoteMode']})}><MenuItem value="Automatic">Automatic using conditions below</MenuItem><MenuItem value="Always">Always request a quotation</MenuItem><MenuItem value="DirectWhenPriced">Direct booking when priced</MenuItem></TextField></Grid>
          {selectedDivision?.services?.some(service => service.isActive) && <Grid size={{xs:12,sm:6}}><TextField select fullWidth label="Driver/operator included" helperText="Applies only to assets that allow staff support; assets may override this default." value={policy.flow.personnelRequirement} onChange={event=>updateFlow({personnelRequirement:event.target.value as Flow['personnelRequirement']})}>{['None','Optional','Required'].map(value=><MenuItem key={value} value={value}>{value}</MenuItem>)}</TextField></Grid>}
        </Grid>
        <Stack mt={2}><FormControlLabel control={<Checkbox checked={policy.flow.onlineRequestsEnabled} onChange={event=>updateFlow({onlineRequestsEnabled:event.target.checked})}/>} label="Accept online requests for this division"/>{policy.flow.quoteMode==='Automatic'&&<FormControlLabel control={<Checkbox checked={policy.flow.businessCustomersRequireQuote} onChange={event=>updateFlow({businessCustomersRequireQuote:event.target.checked})} disabled={policy.flow.quoteMode!=='Automatic'}/>} label="Send business requests for quotation"/>}{policy.flow.quoteMode==='Automatic'&&policy.flow.personnelRequirement!=='None'&&<FormControlLabel control={<Checkbox checked={policy.flow.unpricedPersonnelRequiresQuote} onChange={event=>updateFlow({unpricedPersonnelRequiresQuote:event.target.checked})} disabled={policy.flow.quoteMode!=='Automatic'}/>} label="Quote when driver/operator charges need confirmation"/>}{policy.flow.quoteMode==='Automatic'&&<FormControlLabel control={<Checkbox checked={policy.flow.unpricedDeliveryRequiresQuote} onChange={event=>updateFlow({unpricedDeliveryRequiresQuote:event.target.checked})} disabled={policy.flow.quoteMode!=='Automatic'}/>} label="Quote when delivery has no set price"/>}<FormControlLabel control={<Checkbox checked={policy.flow.requireVerifiedLicenceForVehicles} onChange={event=>updateFlow({requireVerifiedLicenceForVehicles:event.target.checked})}/>} label="Verify licences when customers drive a vehicle"/></Stack>
      </CardContent></Card>
      <Box><Button variant="contained" disabled={saving} onClick={()=>void save()}>{saving?'Saving…':'Save division booking flow'}</Button></Box>
    </Stack>}
  </Box>
}
