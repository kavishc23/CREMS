import { InlineIssueNavigation } from './InlineValidation'
import { FormError } from './FormError'
import { useState } from 'react'
import axios from 'axios'
import { Alert, Box, Button, Chip, Dialog, DialogActions, DialogContent, DialogTitle, Divider, MenuItem, Stack, TextField, Typography } from '@mui/material'
import { api } from '../api/client'

type RecordRow = Record<string, unknown> & {id?:string}
const transferNext:Record<string,string[]>={Requested:['Approved','Cancelled'],Approved:['InTransit','Cancelled'],InTransit:['Received'],Received:['Inspected']}
const names:Record<string,string>={personnel:'Operator / driver',assignments:'Personnel assignment',deliveryZones:'Delivery zone',pricing:'Rate card',alerts:'Business alert',parts:'Parts stock',suppliers:'Supplier',purchaseOrders:'Purchase order',corporateAccounts:'Business account',transfers:'Branch transfer',lifecycle:'Asset lifecycle event',meters:'Meter reading'}
const hidden=new Set(['id','createdAt','updatedAt','proofJson','inspectionJson','linesJson','lineItemsJson','billingContactJson','authorizedContactsJson','jobSitesJson','contractPricingJson','qualifications','timesheets'])
function title(row:RecordRow){return String(row.name??row.fullName??row.partNumber??row.transferNumber??row.purchaseOrderNumber??row.employeeNumber??row.supplierNumber??row.message??row.category??'Record')}
function display(input:unknown){if(input===null||input===undefined||input==='')return '—';if(typeof input==='boolean')return input?'Yes':'No';if(typeof input==='string'&&/^\d{4}-\d{2}-\d{2}T/.test(input))return new Date(input).toLocaleString('en-FJ');return String(input)}
function label(key:string){return key.replace(/([a-z])([A-Z])/g,'$1 $2').replace(/^./,x=>x.toUpperCase())}

export function OperationsRecordDialog({sectionKey,row,onClose,reload}:{sectionKey:string;row:RecordRow|null;onClose:()=>void;reload:()=>Promise<void>}){
  const [nextStatus,setNextStatus]=useState(''),[meter,setMeter]=useState(''),[note,setNote]=useState(''),[saving,setSaving]=useState(false),[error,setError]=useState('')
  const [attempted,setAttempted]=useState(false)
  const options=transferNext[String(row?.status)]??[]
  const status=options.includes(nextStatus)?nextStatus:options[0]??''
  const issues=[
    {target:'transfer-inspection-note',message:status==='Inspected'&&!note.trim()?'Describe the receiving inspection result.':''},
    {target:'transfer-meter-reading',message:meter.trim()&&(!Number.isFinite(Number(meter))||Number(meter)<0||Number(meter)>10_000_000)?'Enter a meter reading from 0 to 10,000,000.':''},
  ]
  async function act(){if(!row?.id)return;setAttempted(true);setError('');if(sectionKey==='transfers'&&issues.some(issue=>issue.message))return;setSaving(true);try{
    if(sectionKey==='alerts')await api.post(`/business-operations/alerts/${row.id}/acknowledge`)
    if(sectionKey==='transfers')await api.patch(`/corporate-operations/transfers/${row.id}/status`,{status,meterReading:meter.trim()?Number(meter):null,conditionNotes:note||null,damageNotes:null,evidenceDataUrls:[]})
    onClose();await reload()
  }catch(e){const data=axios.isAxiosError(e)?e.response?.data:undefined;setError(data?.message??(data?.errors?Object.values(data.errors).flat().join(' '):'This action could not be saved.'))}finally{setSaving(false)}}
  return <Dialog open={Boolean(row)} onClose={()=>!saving&&onClose()} fullWidth maxWidth="sm"><DialogTitle>{names[sectionKey]??'Record details'} · {row?title(row):''}</DialogTitle><DialogContent dividers><Stack gap={2}>{error&&<FormError>{error}</FormError>}{row&&<><Stack direction="row" gap={1}>{row.status!=null&&<Chip size="small" label={display(row.status)}/>} {row.priority!=null&&<Chip size="small" color={String(row.priority)==='Critical'?'error':'default'} label={display(row.priority)}/>}</Stack><Box component="dl" sx={{display:'grid',gridTemplateColumns:{xs:'1fr',sm:'minmax(120px,1fr) 2fr'},gap:1,m:0}}>{Object.entries(row).filter(([key,value])=>!hidden.has(key)&&!key.endsWith('Id')&&value!=null&&typeof value!=='object').map(([key,value])=><Box key={key} sx={{display:'contents'}}><Typography component="dt" variant="body2" color="text.secondary">{label(key)}</Typography><Typography component="dd" variant="body2" sx={{m:0,overflowWrap:'anywhere'}}>{display(value)}</Typography></Box>)}</Box></>}
    {sectionKey==='alerts'&&<Alert severity="info">Acknowledging removes this alert from the active queue. Review the underlying issue first.</Alert>}
    {sectionKey==='transfers'&&options.length>0&&<><Divider/><Typography fontWeight={750}>Advance transfer</Typography><Alert severity="info">Approval blocks new hire allocation. Receipt updates physical location; inspection completes branch ownership. Cancellation is available only before departure.</Alert><TextField select label="Next status" value={status} onChange={e=>setNextStatus(e.target.value)}>{options.map(x=><MenuItem key={x} value={x}>{label(x)}</MenuItem>)}</TextField><TextField type="number" inputProps={{min:0,max:10000000}} id="transfer-meter-reading" label="Meter reading (if applicable)" value={meter} onChange={e=>setMeter(e.target.value)}/><TextField required={status==='Inspected'} helperText={status==='Inspected'?'Record the receiving inspection result to complete the transfer.':undefined} multiline minRows={2} id="transfer-inspection-note" label="Condition notes" value={note} onChange={e=>setNote(e.target.value)}/></>}
  </Stack></DialogContent><DialogActions><InlineIssueNavigation attempted={Boolean(row)&&sectionKey==='transfers'&&attempted} issues={issues}/><Button onClick={onClose} disabled={saving}>Close</Button>{sectionKey==='alerts'&&<Button variant="contained" disabled={saving} onClick={()=>void act()}>Acknowledge</Button>}{sectionKey==='transfers'&&options.length>0&&<Button variant="contained" disabled={saving} onClick={()=>void act()}>Save transfer status</Button>}</DialogActions></Dialog>
}
