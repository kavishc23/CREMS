import { useEffect, useState } from 'react'
import axios from 'axios'
import { Alert, Avatar, Box, Button, Card, CardContent, Chip, CircularProgress, Dialog, DialogActions, DialogContent, DialogTitle, Divider, Grid, List, ListItemButton, ListItemIcon, ListItemText, MenuItem, Stack, Tab, Table, TableBody, TableCell, TableHead, TableRow, Tabs, TextField, Typography } from '@mui/material'
import RefreshOutlined from '@mui/icons-material/RefreshOutlined'
import AssignmentTurnedInOutlined from '@mui/icons-material/AssignmentTurnedInOutlined'
import LocalShippingOutlined from '@mui/icons-material/LocalShippingOutlined'
import RequestQuoteOutlined from '@mui/icons-material/RequestQuoteOutlined'
import ApprovalOutlined from '@mui/icons-material/ApprovalOutlined'
import SupportAgentOutlined from '@mui/icons-material/SupportAgentOutlined'
import EngineeringOutlined from '@mui/icons-material/EngineeringOutlined'
import TuneOutlined from '@mui/icons-material/TuneOutlined'
import Inventory2Outlined from '@mui/icons-material/Inventory2Outlined'
import AccountBalanceOutlined from '@mui/icons-material/AccountBalanceOutlined'
import WarningAmberOutlined from '@mui/icons-material/WarningAmberOutlined'
import CorporateFareOutlined from '@mui/icons-material/CorporateFareOutlined'
import ArrowBackOutlined from '@mui/icons-material/ArrowBackOutlined'
import { api } from '../api/client'
import { QuoteWorkspace } from '../components/QuoteWorkspace'
import { ApprovalWorkspace } from '../components/ApprovalWorkspace'
import { HireReadinessPanel, type HireReadinessRow } from '../components/HireReadinessPanel'
import { OperationsRecordDialog } from '../components/OperationsRecordDialog'
import { OperationsCreateDialog } from '../components/OperationsCreateDialog'
import { PageHeader } from '../components/PageHeader'
import { useAuth } from '../auth/AuthContext'

type Row = Record<string, unknown> & { id?: string }
type Workspace = Record<string, Row[]>
type Overview = Record<string, number>
type Section = { key: string; label: string; description: string; icon: React.ReactNode }

const daily: Section[] = [
  { key: 'readiness', label: 'Hire readiness', description: 'Pickup and return blockers today', icon: <AssignmentTurnedInOutlined /> },
  { key: 'tasks', label: 'My work', description: 'Tasks that need action today', icon: <AssignmentTurnedInOutlined /> },
  { key: 'dispatches', label: 'Deliveries', description: 'Collections and customer deliveries', icon: <LocalShippingOutlined /> },
  { key: 'quotes', label: 'Quotes', description: 'Prepare and follow up quotations', icon: <RequestQuoteOutlined /> },
  { key: 'approvals', label: 'Approvals', description: 'Controlled decisions waiting for review', icon: <ApprovalOutlined /> },
  { key: 'cases', label: 'Customer help', description: 'Questions, incidents and complaints', icon: <SupportAgentOutlined /> },
]
const tools: Section[] = [
  { key: 'personnel', label: 'Operators & drivers', description: 'People, rates and qualifications', icon: <EngineeringOutlined /> },
  { key: 'assignments', label: 'Personnel schedule', description: 'Who is assigned to each hire', icon: <AssignmentTurnedInOutlined /> },
  { key: 'deliveryZones', label: 'Delivery zones', description: 'Distance and trip pricing', icon: <LocalShippingOutlined /> },
  { key: 'pricing', label: 'Rate cards', description: 'Customer, branch and division pricing', icon: <TuneOutlined /> },
  { key: 'alerts', label: 'Business alerts', description: 'Items requiring attention', icon: <WarningAmberOutlined /> },
  { key: 'parts', label: 'Parts stock', description: 'Availability and reorder levels', icon: <Inventory2Outlined /> },
  { key: 'suppliers', label: 'Suppliers', description: 'Approved service and parts suppliers', icon: <Inventory2Outlined /> },
  { key: 'purchaseOrders', label: 'Purchase orders', description: 'Parts and service purchasing', icon: <AccountBalanceOutlined /> },
  { key: 'corporateAccounts', label: 'Business accounts', description: 'Credit, contacts and payment terms', icon: <AccountBalanceOutlined /> },
  { key: 'transfers', label: 'Branch transfers', description: 'Asset movement between locations', icon: <LocalShippingOutlined /> },
  { key: 'lifecycle', label: 'Asset lifecycle', description: 'Commissioning through disposal', icon: <EngineeringOutlined /> },
  { key: 'meters', label: 'Meter history', description: 'Kilometres and operating hours', icon: <TuneOutlined /> },
]
const hidden = new Set(['id','createdAt','updatedAt','lineItemsJson','linesJson','proofJson','inspectionJson','faultCodesJson','billingContactJson','authorizedContactsJson','jobSitesJson','contractPricingJson','qualifications','timesheets'])
const heading = (value: string) => value.replace(/([A-Z])/g, ' $1').replace(/^./, x => x.toUpperCase())
const value = (field: string, input: unknown) => {
  if (input === null || input === undefined || input === '') return '—'
  if (typeof input === 'boolean') return input ? 'Yes' : 'No'
  if (typeof input === 'number') return field.toLowerCase().includes('rate') || field.toLowerCase().includes('cost') || field.toLowerCase().includes('charge') || field.toLowerCase().includes('total') ? `FJD ${input.toLocaleString('en-FJ', { minimumFractionDigits: 2 })}` : input.toLocaleString('en-FJ')
  if (typeof input === 'string' && /^\d{4}-\d{2}-\d{2}T/.test(input)) return new Date(input).toLocaleString('en-FJ')
  return String(input)
}
const dispatchNext: Record<string, string[]> = {
  Scheduled: ['Assigned', 'Cancelled'], Assigned: ['EnRoute', 'Cancelled'],
  EnRoute: ['Arrived', 'Failed'], Arrived: ['Completed', 'Failed'],
  Failed: ['Assigned', 'Cancelled'],
}
const caseNext: Record<string, string[]> = {
  Open: ['InProgress', 'WaitingForCustomer', 'Resolved'],
  InProgress: ['WaitingForCustomer', 'Resolved'],
  WaitingForCustomer: ['InProgress', 'Resolved'], Resolved: ['Closed', 'InProgress'],
}

export function CorporateOperationsPage({ embedded = false }: { embedded?: boolean }) {
  const { user } = useAuth()
  const [overview,setOverview]=useState<Overview|null>(null),[workspace,setWorkspace]=useState<Workspace|null>(null),[selected,setSelected]=useState('tasks'),[mode,setMode]=useState<'daily'|'tools'>('daily'),[loading,setLoading]=useState(true),[error,setError]=useState('')
  const [readiness,setReadiness]=useState<HireReadinessRow[]>([])
  const [search,setSearch]=useState('')
  const [actionRow,setActionRow]=useState<Row|null>(null),[actionStatus,setActionStatus]=useState(''),[actionNote,setActionNote]=useState(''),[actionSignature,setActionSignature]=useState(''),[savingAction,setSavingAction]=useState(false)
  const [detailRow,setDetailRow]=useState<Row|null>(null)
  const [createOpen,setCreateOpen]=useState(false)
  async function load(){
    setLoading(true);setError('')
    const results=await Promise.allSettled([api.get<Overview>('/corporate-operations/overview'),api.get<Workspace>('/corporate-operations/workspace'),api.get<Workspace>('/business-operations/workspace'),api.get<HireReadinessRow[]>('/hire-readiness')])
    const [overviewResult,operationsResult,businessResult,readinessResult]=results
    setOverview(overviewResult.status==='fulfilled'?overviewResult.value.data as Overview:null)
    setWorkspace({...operationsResult.status==='fulfilled'?operationsResult.value.data as Workspace:{},...businessResult.status==='fulfilled'?businessResult.value.data as Workspace:{}})
    setReadiness(readinessResult.status==='fulfilled'?readinessResult.value.data:[])
    const failed=results.flatMap((result,index)=>result.status==='rejected'?[['Summary','Daily operations','Business tools','Hire readiness'][index]]:[])
    if(failed.length)setError(`${failed.join(', ')} could not be loaded. Use Refresh to retry. Available sections are shown below.`)
    setLoading(false)
  }
  // Load the operational workspace once when the page opens.
  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(()=>{void load()},[])
  const sections=mode==='daily'?daily:tools
  const section=sections.find(x=>x.key===selected)??sections[0]
  const rows=workspace?.[section.key]??[]
  const visibleRows=search.trim()?rows.filter(row=>Object.entries(row).some(([key,item])=>!hidden.has(key)&&typeof item!=='object'&&String(item??'').toLowerCase().includes(search.trim().toLowerCase()))):rows
  const preferredColumns: Record<string,string[]>={tasks:['title','priority','category','dueAt','description'],dispatches:['dispatchNumber','type','status','scheduledAt','address','assignedDriver'],cases:['caseNumber','subject','status','priority','dueAt']}
  const columns=rows.length?(preferredColumns[section.key]?.filter(key=>key in rows[0])??Object.keys(rows[0]).filter(key=>!hidden.has(key)&&!key.toLowerCase().endsWith('id')).slice(0,6)):[]
  const switchMode=(next:'daily'|'tools')=>{setMode(next);setSelected(next==='daily'?'tasks':'personnel');setSearch('')}
  const actionOptions=section.key==='dispatches'?dispatchNext[String(actionRow?.status)]??[]:section.key==='cases'?caseNext[String(actionRow?.status)]??[]:[]
  function openAction(row:Row){setActionRow(row);setActionNote('');setActionSignature('');setActionStatus((section.key==='dispatches'?dispatchNext[String(row.status)]:section.key==='cases'?caseNext[String(row.status)]:[])?.[0]??'')}
  async function saveAction(){if(!actionRow?.id)return;setSavingAction(true);setError('');try{
    if(section.key==='tasks')await api.patch(`/corporate-operations/tasks/${actionRow.id}/complete`)
    else if(section.key==='cases')await api.patch(`/corporate-operations/cases/${actionRow.id}/status`,{status:actionStatus,resolution:actionNote})
    else if(section.key==='dispatches')await api.patch(`/corporate-operations/dispatches/${actionRow.id}/status`,{status:actionStatus,signatureName:actionSignature,notes:actionNote,evidenceDataUrls:[]})
    setActionRow(null);await load()
  }catch(e){const data=axios.isAxiosError(e)?e.response?.data:undefined;setError(data?.message??(data?.errors?Object.values(data.errors).flat().join(' '):'The action could not be saved. Check the required details and try again.'))}finally{setSavingAction(false)}}
  const metricKeys=['activeRentals','overdueRentals','todayDispatches','pendingApprovals','outstandingReceivables','utilizationPercent']
  const isSpecial=['quotes','approvals','readiness'].includes(section.key)
  const sectionCount=(key:string)=>key==='readiness'?readiness.length:workspace?.[key]?.length??0
  const canCreate=['personnel','parts','suppliers','deliveryZones','pricing','transfers'].includes(section.key)
  const selectSection=(key:string)=>{setSelected(key);setSearch('')}
  return <Box sx={{p:{xs:2,sm:3,lg:4},pt:embedded?3:undefined,maxWidth:1600,mx:'auto'}}>
    {!embedded&&<PageHeader icon={<CorporateFareOutlined/>} title="Operations centre" subtitle="One place for daily work, decisions and supporting business records." actions={<Button startIcon={<RefreshOutlined/>} onClick={()=>void load()} disabled={loading}>Refresh</Button>}/>}
    {embedded&&<Stack direction="row" justifyContent="flex-end" mb={2}><Button startIcon={<RefreshOutlined/>} onClick={()=>void load()} disabled={loading}>Refresh</Button></Stack>}
    <Card variant="outlined" sx={{mb:2}}><Tabs value={mode} onChange={(_,next:'daily'|'tools')=>switchMode(next)} variant="fullWidth"><Tab value="daily" label="Today's work"/><Tab value="tools" label="Business tools"/></Tabs></Card>
    <Alert severity="info" sx={{mb:2}}>{user?.divisionName??'Allocated divisions'} · {user?.branchName??'Allocated branches'}. Work and decisions follow your assigned access.</Alert>
    {error&&<Alert severity="error" sx={{mb:2}} onClose={()=>setError('')}>{error}</Alert>}
    {loading?<Box sx={{minHeight:360,display:'grid',placeItems:'center'}}><CircularProgress/></Box>:<>
      {mode==='daily'&&overview&&<Grid container spacing={1.5} mb={2}>{metricKeys.map(key=>{const number=overview[key]??0;const attention=(key==='overdueRentals'||key==='pendingApprovals')&&number>0;return <Grid key={key} size={{xs:6,sm:4,lg:2}}><Card variant="outlined" sx={{height:'100%',borderTop:3,borderTopColor:attention?'error.main':'secondary.main'}}><CardContent><Typography variant="caption" color="text.secondary">{heading(key)}</Typography><Typography variant="h5" fontWeight={800} mt={.5}>{key.includes('Receivables')?`FJD ${number.toLocaleString('en-FJ')}`:key.includes('Percent')?`${number}%`:number.toLocaleString('en-FJ')}</Typography></CardContent></Card></Grid>})}</Grid>}
      <Grid container spacing={2} alignItems="flex-start">
        <Grid size={{xs:12,md:3,lg:2.7}}>
          <Card variant="outlined" sx={{position:{md:'sticky'},top:{md:80}}}>
            <Box sx={{p:2,display:{xs:'none',md:'block'}}}><Typography fontWeight={800}>{mode==='daily'?'Work queues':'Business tools'}</Typography><Typography variant="body2" color="text.secondary">Choose a section</Typography></Box>
            <Divider sx={{display:{xs:'none',md:'block'}}}/>
            <Tabs value={sections.findIndex(x=>x.key===section.key)} onChange={(_,index)=>selectSection(sections[index].key)} variant="scrollable" scrollButtons="auto" sx={{display:{xs:'flex',md:'none'}}}>{sections.map(item=><Tab key={item.key} label={item.label}/>)}</Tabs>
            <List disablePadding sx={{display:{xs:'none',md:'block'},maxHeight:'calc(100vh - 190px)',overflowY:'auto'}}>{sections.map(item=><ListItemButton key={item.key} selected={item.key===section.key} onClick={()=>selectSection(item.key)} sx={{py:1.2}}><ListItemIcon sx={{minWidth:38,color:item.key===section.key?'secondary.dark':'text.secondary'}}>{item.icon}</ListItemIcon><ListItemText primary={item.label} secondary={item.description} primaryTypographyProps={{fontWeight:item.key===section.key?800:600}}/><Chip size="small" label={sectionCount(item.key)}/></ListItemButton>)}</List>
          </Card>
        </Grid>
        <Grid size={{xs:12,md:9,lg:9.3}}><Card variant="outlined" sx={{minHeight:420}}>
          <Box sx={{px:{xs:2,sm:3},pt:2,pb:isSpecial?1:2,borderBottom:1,borderColor:'divider'}}>
            <Stack direction={{xs:'column',sm:'row'}} alignItems={{sm:'center'}} justifyContent="space-between" gap={1}>
              {!isSpecial&&<Stack direction="row" alignItems="center" gap={1.5}><Avatar sx={{bgcolor:'secondary.main',color:'#111'}}>{section.icon}</Avatar><Box><Typography variant="h6" fontWeight={800}>{section.label}</Typography><Typography variant="body2" color="text.secondary">{section.description}</Typography></Box></Stack>}
              <Stack direction="row" gap={1}>{canCreate&&<Button size="small" variant="contained" onClick={()=>setCreateOpen(true)}>Add {section.label.toLowerCase()}</Button>}{(mode!=='daily'||section.key!=='tasks')&&<Button size="small" startIcon={<ArrowBackOutlined/>} onClick={()=>switchMode('daily')}>Back to today's work</Button>}</Stack>
            </Stack>
            {!isSpecial&&<Stack direction={{xs:'column',sm:'row'}} alignItems={{sm:'center'}} justifyContent="space-between" gap={1} mt={2}><TextField size="small" label={`Search ${section.label.toLowerCase()}`} value={search} onChange={e=>setSearch(e.target.value)} sx={{minWidth:{sm:300}}}/><Typography variant="body2" color="text.secondary">Showing {visibleRows.length} of {rows.length}</Typography></Stack>}
          </Box>
          {section.key==='readiness'?<HireReadinessPanel rows={readiness}/>:section.key==='quotes'?<QuoteWorkspace quotes={rows as never[]} reload={load}/>:section.key==='approvals'?<ApprovalWorkspace approvals={rows as never[]} reload={load}/>:<Content section={section} rows={visibleRows} columns={columns} onAction={openAction} onInspect={setDetailRow} userId={user?.id} isAdmin={Boolean(user?.roles.some(role=>['SuperAdministrator','Administrator'].includes(role)))}/>}
        </Card></Grid>
      </Grid>
    </>}
    <Dialog open={Boolean(actionRow)} onClose={()=>!savingAction&&setActionRow(null)} fullWidth maxWidth="sm">
      <DialogTitle>{section.key==='tasks'?'Complete task':section.key==='cases'?'Update customer case':'Update dispatch'}</DialogTitle>
      <DialogContent><Stack gap={2} mt={1}>
        <Typography fontWeight={700}>{String(actionRow?.title??actionRow?.subject??actionRow?.dispatchNumber??'')}</Typography>
        {section.key==='tasks'&&<Typography color="text.secondary">Mark this task complete? It will leave the active work queue.</Typography>}
        {section.key!=='tasks'&&<TextField select label="Next status" value={actionStatus} onChange={e=>setActionStatus(e.target.value)}>{actionOptions.map(x=><MenuItem key={x} value={x}>{heading(x)}</MenuItem>)}</TextField>}
        {section.key==='dispatches'&&actionStatus==='Completed'&&<TextField required label="Received by" value={actionSignature} onChange={e=>setActionSignature(e.target.value)} helperText="Name of the person who accepted delivery or collection."/>}
        {section.key!=='tasks'&&<TextField multiline minRows={3} label={section.key==='cases'?'Resolution or update note':'Dispatch note'} value={actionNote} onChange={e=>setActionNote(e.target.value)} required={section.key==='cases'&&['Resolved','Closed'].includes(actionStatus)}/>}
      </Stack></DialogContent>
      <DialogActions><Button onClick={()=>setActionRow(null)} disabled={savingAction}>Cancel</Button><Button variant="contained" onClick={()=>void saveAction()} disabled={savingAction||section.key==='cases'&&['Resolved','Closed'].includes(actionStatus)&&!actionNote.trim()||section.key==='dispatches'&&actionStatus==='Completed'&&!actionSignature.trim()}>{savingAction?'Saving…':section.key==='tasks'?'Complete task':'Save status'}</Button></DialogActions>
    </Dialog>
    <OperationsRecordDialog sectionKey={section.key} row={detailRow} onClose={()=>setDetailRow(null)} reload={load}/>
    <OperationsCreateDialog sectionKey={section.key} open={createOpen} onClose={()=>setCreateOpen(false)} reload={load}/>
  </Box>
}
function Content({section,rows,columns,onAction,onInspect,userId,isAdmin}:{section:Section;rows:Row[];columns:string[];onAction:(row:Row)=>void;onInspect:(row:Row)=>void;userId?:string;isAdmin:boolean}){
  const actionable=['tasks','cases','dispatches'].includes(section.key)
  const inspectable=tools.some(x=>x.key===section.key)
  const canAct=(row:Row)=>(section.key==='tasks'&&(isAdmin||row.assignedUserId===userId))||(section.key==='cases'&&(isAdmin||row.assignedUserId===userId)&&Boolean(caseNext[String(row.status)]?.length))||section.key==='dispatches'&&Boolean(dispatchNext[String(row.status)]?.length)
  return <>{rows.length?<Box sx={{overflowX:'auto'}}><Table><TableHead><TableRow>{columns.map(column=><TableCell key={column} sx={{fontWeight:800,bgcolor:'grey.50'}}>{heading(column)}</TableCell>)}{(actionable||inspectable)&&<TableCell align="right" sx={{fontWeight:800,bgcolor:'grey.50'}}>Action</TableCell>}</TableRow></TableHead><TableBody>{rows.map((row,index)=><TableRow key={row.id??index} hover>{columns.map(column=><TableCell key={column}>{['status','priority','type','availability'].includes(column)?<Chip size="small" label={value(column,row[column])} variant="outlined" color={String(row[column]).match(/critical|overdue|failed|rejected/i)?'error':'default'}/>:value(column,row[column])}</TableCell>)}{(actionable||inspectable)&&<TableCell align="right">{inspectable?<Button size="small" variant="outlined" onClick={()=>onInspect(row)}>View details</Button>:canAct(row)&&<Button size="small" variant="outlined" onClick={()=>onAction(row)}>{section.key==='tasks'?'Complete':'Update'}</Button>}</TableCell>}</TableRow>)}</TableBody></Table></Box>:<Box sx={{py:8,px:3,textAlign:'center'}}><Avatar sx={{mx:'auto',mb:2,bgcolor:'grey.100',color:'text.secondary'}}>{section.icon}</Avatar><Typography fontWeight={800}>No {section.label.toLowerCase()} to show</Typography><Typography color="text.secondary" mt={.5}>This area will update automatically when records are created.</Typography></Box>}</>
}
