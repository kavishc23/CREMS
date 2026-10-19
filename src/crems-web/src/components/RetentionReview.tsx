import { useEffect, useState } from 'react'
import { Alert, Button, CircularProgress, Stack, Typography } from '@mui/material'
import { api } from '../api/client'

type Review = {
  years: number; cutoff: string; page: number; pageSize: number; bookingCount: number; auditCount: number
  bookings: { id: string; reference: string; status: string; lastActivity: string }[]
  audits: { id: string; action: string; entityType: string; occurredAt: string }[]
}
export function RetentionReview() {
  const [page,setPage]=useState(1)
  const [data,setData]=useState<Review|null>(null)
  const [error,setError]=useState('')
  const [loading,setLoading]=useState(true)
  const [retry,setRetry]=useState(0)
  useEffect(()=>{
    let active=true
    api.get<Review>('/administration/retention-review',{params:{page}})
      .then(response=>{if(active)setData(response.data)})
      .catch(()=>{if(active)setError('Retention review could not be loaded.')})
      .finally(()=>{if(active)setLoading(false)})
    return()=>{active=false}
  },[page,retry])
  return <Stack spacing={1} sx={{mt:3}}>
    <Typography variant="h6">Records flagged for retention review</Typography>
    <Typography color="text.secondary">Closed rentals are flagged using their last update and scheduled rental end dates. Audit events use their event date. Review linked financial records and retention obligations before taking action. Nothing is deleted or archived automatically.</Typography>
    {error&&<Alert severity="error" action={<Button onClick={()=>{setLoading(true);setError('');setRetry(x=>x+1)}}>Retry</Button>}>{error}</Alert>}
    {loading?<CircularProgress size={24}/>:!error&&data&&<>
      <Typography>{data.years}-year policy · before {new Date(data.cutoff).toLocaleDateString('en-FJ')} · {data.bookingCount} closed rentals · {data.auditCount} audit events</Typography>
      {data.bookings.map(row=><Typography key={row.id}>{row.reference} · {row.status} · {new Date(row.lastActivity).toLocaleDateString('en-FJ')}</Typography>)}
      {data.audits.map(row=><Typography key={row.id}>{row.action} · {row.entityType} · {new Date(row.occurredAt).toLocaleDateString('en-FJ')}</Typography>)}
      {data.bookingCount+data.auditCount===0&&<Typography>No records currently require review.</Typography>}
      <Stack direction="row" spacing={1} alignItems="center"><Button disabled={page===1} onClick={()=>{setLoading(true);setPage(x=>x-1)}}>Previous</Button><Typography>Page {page}</Typography><Button disabled={page*data.pageSize>=Math.max(data.bookingCount,data.auditCount)} onClick={()=>{setLoading(true);setPage(x=>x+1)}}>Next</Button></Stack>
    </>}
  </Stack>
}
