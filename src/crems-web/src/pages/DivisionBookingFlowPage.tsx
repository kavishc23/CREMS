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
type DivisionOption = {id:string;name:string;isActive:boolean;services:{isActive:boolean;isBookableOnline:boolean}[]}

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
  const onlineServices=selectedDivision?.services?.filter(item=>item.isActive&&item.isBookableOnline).length??0
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
      <Card variant="outlined"><CardContent><Typography variant="h6" fontWeight={750} mb={.5}>Customer request route</Typography><Typography variant="body2" color="text.secondary" mb={2}>These rules decide whether a new online request becomes a staff-reviewed quotation or a booking request.</Typography>
        <Grid container spacing={2}>
          <Grid size={{xs:12,sm:6}}><TextField select fullWidth label="Quotation route" value={policy.flow.quoteMode} onChange={event=>updateFlow({quoteMode:event.target.value as Flow['quoteMode']})}><MenuItem value="Automatic">Automatic using conditions below</MenuItem><MenuItem value="Always">Always request a quotation</MenuItem><MenuItem value="DirectWhenPriced">Direct booking when priced</MenuItem></TextField></Grid>
          <Grid size={{xs:12,sm:6}}><TextField select fullWidth label="Professional driver / operator" helperText="Division default; assets may override with None, Optional or Required." value={policy.flow.personnelRequirement} onChange={event=>updateFlow({personnelRequirement:event.target.value as Flow['personnelRequirement']})}>{['None','Optional','Required'].map(value=><MenuItem key={value} value={value}>{value}</MenuItem>)}</TextField></Grid>
        </Grid>
        <Stack mt={2}><FormControlLabel control={<Checkbox checked={policy.flow.onlineRequestsEnabled} onChange={event=>updateFlow({onlineRequestsEnabled:event.target.checked})}/>} label="Accept online requests for this division"/><FormControlLabel control={<Checkbox checked={policy.flow.businessCustomersRequireQuote} onChange={event=>updateFlow({businessCustomersRequireQuote:event.target.checked})} disabled={policy.flow.quoteMode!=='Automatic'}/>} label="Business customers receive a quotation"/><FormControlLabel control={<Checkbox checked={policy.flow.unpricedPersonnelRequiresQuote} onChange={event=>updateFlow({unpricedPersonnelRequiresQuote:event.target.checked})} disabled={policy.flow.quoteMode!=='Automatic'}/>} label="Unpriced driver/operator work requires a quotation"/><FormControlLabel control={<Checkbox checked={policy.flow.unpricedDeliveryRequiresQuote} onChange={event=>updateFlow({unpricedDeliveryRequiresQuote:event.target.checked})} disabled={policy.flow.quoteMode!=='Automatic'}/>} label="Unpriced delivery requires a quotation"/><FormControlLabel control={<Checkbox checked={policy.flow.requireVerifiedLicenceForVehicles} onChange={event=>updateFlow({requireVerifiedLicenceForVehicles:event.target.checked})}/>} label="Require a verified customer licence for self-driven vehicles"/></Stack>
      </CardContent></Card>
      <Card variant="outlined"><CardContent><Typography variant="h6" fontWeight={750} mb={2}>Hire duration and quotation</Typography><Grid container spacing={2}><Grid size={{xs:12,sm:4}}><TextField fullWidth type="number" label="Minimum hire days" inputProps={{min:1,max:366}} value={policy.flow.minimumHireDays} onChange={event=>updateFlow({minimumHireDays:Number(event.target.value)})}/></Grid><Grid size={{xs:12,sm:4}}><TextField fullWidth type="number" label="Maximum hire days" inputProps={{min:1,max:366}} value={policy.flow.maximumHireDays} onChange={event=>updateFlow({maximumHireDays:Number(event.target.value)})}/></Grid><Grid size={{xs:12,sm:4}}><TextField fullWidth type="number" label="Quote valid for days" inputProps={{min:1,max:90}} value={policy.flow.quoteValidityDays} onChange={event=>updateFlow({quoteValidityDays:Number(event.target.value)})}/></Grid></Grid></CardContent></Card>
      <Card variant="outlined"><CardContent><Typography variant="h6" fontWeight={750} mb={.5}>Division financial defaults</Typography><Typography variant="body2" color="text.secondary" mb={2}>Charges and fees are maintained in Fees & charges. The refundable bond is held separately from hire income.</Typography><Grid container spacing={2}><Grid size={{xs:12,sm:4}}><TextField fullWidth type="number" label="Default refundable bond" inputProps={{min:0,max:1000000,step:0.01}} value={policy.defaultBondAmount} onChange={event=>setPolicy({...policy,defaultBondAmount:Number(event.target.value)})}/></Grid><Grid size={{xs:12,sm:4}}><TextField fullWidth type="number" label="VAT rate (%)" inputProps={{min:0,max:100,step:0.01}} value={policy.defaultTaxRate} onChange={event=>setPolicy({...policy,defaultTaxRate:Number(event.target.value)})}/></Grid><Grid size={{xs:12,sm:4}}><TextField fullWidth disabled label="Currency" helperText="FJD only until multi-currency invoicing is supported." value={policy.defaultCurrency}/></Grid><Grid size={12}><TextField fullWidth multiline minRows={3} label="Default rental terms" value={policy.defaultRentalTerms??''} onChange={event=>setPolicy({...policy,defaultRentalTerms:event.target.value})}/></Grid></Grid></CardContent></Card>
      <Box><Button variant="contained" disabled={saving} onClick={()=>void save()}>{saving?'Saving…':'Save division booking flow'}</Button></Box>
    </Stack>}
  </Box>
}
