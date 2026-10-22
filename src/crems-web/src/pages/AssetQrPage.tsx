import { useEffect, useState } from 'react'
import axios from 'axios'
import QrCodeScannerOutlined from '@mui/icons-material/QrCodeScannerOutlined'
import { Alert, Box, Button, Card, CardContent, Chip, CircularProgress, Stack, TextField, Typography } from '@mui/material'
import { AssetProfileDialog } from '../components/AssetProfileDialog'
import { PageHeader } from '../components/PageHeader'
import { QrCameraScanner } from '../components/QrCameraScanner'
import { api } from '../api/client'

type Context = {
  asset: { id:string;assetNumber:string;name:string;status:string;isActive:boolean;branchName:string;divisionName:string|null;registrationNumber:string|null;serialNumber:string|null }
  action:'CheckOut'|'CheckIn'|'ViewOnly'
  booking:null|{id:string;bookingNumber:string;customerName:string;startAt:string;endAt:string}
  availability:{availableNow:boolean;reason:string;checkedAt:string;maintenance:{jobNumber:string;serviceType:string;status:string;expectedReleaseAt:string|null}[];upcoming:{bookingNumber:string;startAt:string;endAt:string;status:string}[]}
  message:string|null
}
const date=(value:string)=>new Date(value).toLocaleString('en-FJ',{timeZone:'Pacific/Fiji',dateStyle:'medium',timeStyle:'short'})
const words=(value:string)=>value.replace(/([a-z])([A-Z])/g,'$1 $2')

export function AssetQrPage(){
  const initialCode=new URLSearchParams(window.location.search).get('code')??''
  const [code,setCode]=useState(initialCode),[context,setContext]=useState<Context|null>(null),[loading,setLoading]=useState(false),[error,setError]=useState(''),[scanning,setScanning]=useState(false),[profileId,setProfileId]=useState<string|null>(null)
  // Resolve links printed on existing asset labels.
  useEffect(()=>{if(initialCode)void resolve(initialCode)},[]) // eslint-disable-line react-hooks/exhaustive-deps
  async function resolve(value=code){
    if(!value.trim()||loading)return
    setLoading(true);setError('');setContext(null)
    try{const response=await api.post<Context>('/asset-qr/resolve',{code:value.trim()});setContext(response.data);setCode(response.data.asset.assetNumber)}
    catch(reason){setError(axios.isAxiosError(reason)?reason.response?.data?.message??(reason.response?.status===403?'This asset is outside your assigned operating scope.':'The asset could not be loaded. Please try again.'):'The asset could not be loaded. Please try again.')}
    finally{setLoading(false)}
  }
  return <Box data-fill-workspace sx={{p:{xs:2,sm:3,lg:4},width:'100%',minHeight:'calc(100dvh - 112px)',display:'flex',flexDirection:'column'}}>
    <PageHeader icon={<QrCodeScannerOutlined/>} title="Scan asset QR" subtitle="Scan a label or enter an asset number to check live availability and hire activity." dense/>
    <Card variant="outlined" sx={{flex:1,borderTop:'4px solid',borderTopColor:'secondary.main',display:'flex',minHeight:0}}>
      <CardContent sx={{width:'100%',p:{xs:2,md:3},display:'flex',flexDirection:'column',justifyContent:'center','&:last-child':{pb:3}}}>
        {error&&<Alert severity="error" sx={{mb:2}} onClose={()=>setError('')}>{error}</Alert>}
        <Stack spacing={scanning?1.5:2.5} alignItems="center" justifyContent="center" sx={{width:'100%',mx:'auto',maxWidth:context?1100:900}}>
          {!scanning&&!context&&<><Box sx={{width:72,height:72,borderRadius:2,bgcolor:'primary.main',color:'secondary.main',display:'grid',placeItems:'center'}}><QrCodeScannerOutlined sx={{fontSize:40}}/></Box><Box textAlign="center"><Typography variant="h5" fontWeight={800}>Find an asset</Typography><Typography color="text.secondary">See its current condition, bookings and maintenance in one place.</Typography></Box></>}
          {(!context||scanning)&&<QrCameraScanner centered onScanningChange={active=>{setScanning(active);if(active){setContext(null);setError('')}}} onScan={value=>{setCode(value);return resolve(value)}}/>}
          {!scanning&&!context&&<Box component="form" onSubmit={event=>{event.preventDefault();void resolve()}} sx={{width:'100%',maxWidth:780}}><Stack direction={{xs:'column',sm:'row'}} gap={1.5}><TextField fullWidth label="Asset number or QR value" value={code} onChange={event=>setCode(event.target.value)} placeholder="e.g. SHP-SUV-3001" autoComplete="off"/><Button type="submit" variant="contained" disabled={loading||!code.trim()} sx={{minWidth:140}}>{loading?<CircularProgress size={22}/>:'Find asset'}</Button></Stack></Box>}
          {!scanning&&context&&<ScanResult context={context} openProfile={()=>setProfileId(context.asset.id)} reset={()=>{setContext(null);setCode('')}}/>}
        </Stack>
      </CardContent>
    </Card><AssetProfileDialog assetId={profileId} onClose={()=>setProfileId(null)}/>
  </Box>
}

function ScanResult({context,openProfile,reset}:{context:Context;openProfile:()=>void;reset:()=>void}){
  const activeHire=context.action==='CheckIn'
  const actionLabel=activeHire?'Begin return':context.action==='CheckOut'?'Begin pickup':'View booking'
  const hireUrl=context.booking?`/staff/hire-operations?bookingId=${encodeURIComponent(context.booking.id)}${activeHire?'&workflow=return':context.action==='CheckOut'?'&workflow=pickup':''}`:''
  return <Box sx={{width:'100%'}}>
    <Box sx={{display:'flex',justifyContent:'space-between',alignItems:{xs:'flex-start',md:'center'},flexDirection:{xs:'column',md:'row'},gap:2,mb:2}}>
      <Box><Typography variant="overline" color="text.secondary">{context.asset.divisionName} · {context.asset.branchName}</Typography><Typography variant="h4" fontWeight={850}>{context.asset.name}</Typography><Typography color="text.secondary">Asset {context.asset.assetNumber}{context.asset.registrationNumber?` · Registration ${context.asset.registrationNumber}`:context.asset.serialNumber?` · Serial ${context.asset.serialNumber}`:''}</Typography></Box>
      <Stack direction="row" gap={1}><Chip label={words(context.asset.status)} variant="outlined"/><Chip color={context.availability.availableNow?'success':'warning'} label={context.availability.availableNow?'Available for hire':'Unavailable for hire'}/></Stack>
    </Box>
    <Alert severity={context.availability.availableNow?'success':activeHire?'info':'warning'} sx={{mb:2}}>{activeHire?`On hire${context.booking?` to ${context.booking.customerName}`:''}. Confirm its condition and complete the return below.`:context.availability.reason}</Alert>
    <Box sx={{display:'grid',gridTemplateColumns:{xs:'1fr',md:'1.15fr 1fr'},gap:2}}>
      <Box sx={{border:1,borderColor:'divider',borderRadius:2,p:2.5,bgcolor:'background.paper'}}><Typography variant="overline" color="text.secondary">Hire activity</Typography>{context.booking?<><Typography variant="h6" fontWeight={800}>{context.booking.bookingNumber}</Typography><Typography variant="body2" color="text.secondary" mb={1.5}>{context.booking.customerName} · {date(context.booking.startAt)} to {date(context.booking.endAt)}</Typography><Button variant="contained" size="large" href={hireUrl}>{actionLabel}</Button></>:<Typography color="text.secondary">No active hire or pickup is linked to this asset.</Typography>}{context.availability.upcoming.length>1&&<Box mt={2}><Typography variant="subtitle2" fontWeight={750}>Other scheduled hires</Typography>{context.availability.upcoming.filter(item=>item.bookingNumber!==context.booking?.bookingNumber).map(item=><Typography key={item.bookingNumber+item.startAt} variant="body2" color="text.secondary">{item.bookingNumber} · {date(item.startAt)} to {date(item.endAt)}</Typography>)}</Box>}</Box>
      <Box sx={{border:1,borderColor:'divider',borderRadius:2,p:2.5,bgcolor:'background.paper'}}><Typography variant="overline" color="text.secondary">Asset condition</Typography><Typography variant="h6" fontWeight={800}>{context.availability.maintenance.length?'Maintenance hold recorded':'No maintenance hold'}</Typography><Typography variant="body2" color="text.secondary">{context.availability.maintenance.length?context.availability.maintenance.map(item=>`${item.jobNumber} · ${item.serviceType} (${words(item.status)})`).join('; '):'No open repair or safety release is blocking this asset.'}</Typography><Typography variant="caption" display="block" color="text.secondary" mt={2}>Checked {date(context.availability.checkedAt)}</Typography></Box>
    </Box>
    <Stack direction="row" useFlexGap flexWrap="wrap" gap={1} mt={2}><Button variant="outlined" onClick={openProfile}>Full asset profile</Button><Button variant="outlined" href={`/staff/availability?assetId=${encodeURIComponent(context.asset.id)}`}>Availability calendar</Button><Box sx={{flex:1}}/><Button onClick={reset}>Check another asset</Button></Stack>
  </Box>
}
