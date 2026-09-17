import { useEffect, useState } from 'react'
import { Alert, Avatar, Box, Button, Card, CardContent, Chip, CircularProgress, Divider, Grid, List, ListItemButton, ListItemIcon, ListItemText, Stack, Tab, Table, TableBody, TableCell, TableHead, TableRow, Tabs, Typography } from '@mui/material'
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
import { PageHeader } from '../components/PageHeader'
import { useAuth } from '../auth/AuthContext'

type Row = Record<string, unknown> & { id?: string }
type Workspace = Record<string, Row[]>
type Overview = Record<string, number>
type Section = { key: string; label: string; description: string; icon: React.ReactNode }

const daily: Section[] = [
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

export function CorporateOperationsPage({ embedded = false }: { embedded?: boolean }) {
  const { user } = useAuth()
  const [overview,setOverview]=useState<Overview|null>(null),[workspace,setWorkspace]=useState<Workspace|null>(null),[selected,setSelected]=useState('tasks'),[mode,setMode]=useState<'daily'|'tools'>('daily'),[loading,setLoading]=useState(true),[error,setError]=useState('')
  async function load(){
    setLoading(true);setError('')
    const results=await Promise.allSettled([api.get<Overview>('/corporate-operations/overview'),api.get<Workspace>('/corporate-operations/workspace'),api.get<Workspace>('/business-operations/workspace')])
    const [overviewResult,operationsResult,businessResult]=results
    setOverview(overviewResult.status==='fulfilled'?overviewResult.value.data as Overview:null)
    setWorkspace({...operationsResult.status==='fulfilled'?operationsResult.value.data as Workspace:{},...businessResult.status==='fulfilled'?businessResult.value.data as Workspace:{}})
    const failed=results.flatMap((result,index)=>result.status==='rejected'?[['Summary','Daily operations','Business tools'][index]]:[])
    if(failed.length)setError(`${failed.join(', ')} could not be loaded. Use Refresh to retry. Available sections are shown below.`)
    setLoading(false)
  }
  // Load the operational workspace once when the page opens.
  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(()=>{void load()},[])
  const sections=mode==='daily'?daily:tools
  const section=sections.find(x=>x.key===selected)??sections[0]
  const rows=workspace?.[section.key]??[]
  const columns=rows.length?Object.keys(rows[0]).filter(key=>!hidden.has(key)&&!key.toLowerCase().endsWith('id')).slice(0,6):[]
  const switchMode=(next:'daily'|'tools')=>{setMode(next);setSelected(next==='daily'?'tasks':'personnel')}
  if(!loading&&workspace?.quotes&&section.key==='quotes')return <Box sx={{p:{xs:2,sm:3,lg:4},maxWidth:1600,mx:'auto'}}><PageHeading title="Customer quotations" subtitle="Quotations within your assigned branch and division." back={()=>switchMode('daily')} /><QuoteWorkspace quotes={rows as never[]} reload={load}/></Box>
  if(!loading&&workspace?.approvals&&section.key==='approvals')return <Box sx={{p:{xs:2,sm:3,lg:4},maxWidth:1300,mx:'auto'}}><PageHeading title="Approval decisions" subtitle="Review scoped requests. Decision buttons appear when the current stage is assigned to your role." back={()=>switchMode('daily')} /><ApprovalWorkspace approvals={rows as never[]} reload={load}/></Box>
  const metricKeys=['activeRentals','overdueRentals','todayDispatches','pendingApprovals','outstandingReceivables','utilizationPercent']
  return <Box sx={{p:{xs:2,sm:3,lg:4},pt:embedded?3:undefined,maxWidth:1600,mx:'auto'}}>
    {embedded ? <Stack direction="row" justifyContent="flex-end" gap={1} mb={2}><Button variant={mode==='daily'?'contained':'outlined'} onClick={()=>switchMode('daily')}>Today’s work</Button><Button variant={mode==='tools'?'contained':'outlined'} onClick={()=>switchMode('tools')}>Business tools</Button><Button aria-label="Refresh" startIcon={<RefreshOutlined/>} onClick={()=>void load()}>Refresh</Button></Stack> : <PageHeader icon={<CorporateFareOutlined/>} title="Operations centre" subtitle="Start with today’s work. Open business tools only when you need to maintain supporting records." actions={<Stack direction="row" gap={1} flexWrap="wrap"><Button variant={mode==='daily'?'contained':'outlined'} onClick={()=>switchMode('daily')}>Today’s work</Button><Button variant={mode==='tools'?'contained':'outlined'} onClick={()=>switchMode('tools')}>Business tools</Button><Button aria-label="Refresh" startIcon={<RefreshOutlined/>} onClick={()=>void load()}>Refresh</Button></Stack>} />}
    <Alert severity="info" sx={{mb:2}}>{user?.divisionName ?? 'Allocated divisions'} · {user?.branchName ?? 'Allocated branches'}. Rental officers review and prepare requests; the assigned branch manager decides manager approval stages.</Alert>
    {error&&<Alert severity="error" sx={{mb:2}}>{error}</Alert>}
    {loading?<Box sx={{minHeight:420,display:'grid',placeItems:'center'}}><CircularProgress/></Box>:<>
      {mode==='daily'&&<Grid container spacing={2} mb={3}>{metricKeys.map(key=>{const number=overview?.[key]??0;const attention=key==='overdueRentals'&&number>0||key==='pendingApprovals'&&number>0;return <Grid key={key} size={{xs:6,md:4,lg:2}}><Card variant="outlined" sx={{height:'100%',borderTop:3,borderTopColor:attention?'error.main':'secondary.main'}}><CardContent><Typography variant="caption" color="text.secondary">{heading(key)}</Typography><Typography variant="h4" fontWeight={850} mt={.5}>{key.includes('Receivables')?`$${number.toLocaleString('en-FJ')}`:key.includes('Percent')?`${number}%`:number.toLocaleString('en-FJ')}</Typography></CardContent></Card></Grid>})}</Grid>}
      {mode==='daily'?<Card variant="outlined"><Tabs value={sections.findIndex(x=>x.key===section.key)} onChange={(_,i)=>setSelected(sections[i].key)} variant="scrollable" scrollButtons="auto" sx={{px:1,borderBottom:1,borderColor:'divider'}}>{sections.map(item=><Tab key={item.key} icon={item.icon as React.ReactElement} iconPosition="start" label={<Stack direction="row" gap={1} alignItems="center"><span>{item.label}</span>{(workspace?.[item.key]?.length??0)>0&&<Chip size="small" label={workspace?.[item.key].length}/>}</Stack>}/>)}</Tabs><Content section={section} rows={rows} columns={columns}/></Card>:<Grid container spacing={2}><Grid size={{xs:12,md:4,lg:3}}><Card variant="outlined" sx={{position:{md:'sticky'},top:{md:80}}}><Box p={2}><Typography fontWeight={800}>Business tools</Typography><Typography variant="body2" color="text.secondary">Configuration and supporting records</Typography></Box><Divider/><List disablePadding>{sections.map(item=><ListItemButton key={item.key} selected={item.key===section.key} onClick={()=>setSelected(item.key)} sx={{py:1.2}}><ListItemIcon sx={{minWidth:40,color:item.key===section.key?'secondary.dark':'text.secondary'}}>{item.icon}</ListItemIcon><ListItemText primary={item.label} secondary={item.description} primaryTypographyProps={{fontWeight:item.key===section.key?800:600}}/><Chip size="small" label={workspace?.[item.key]?.length??0}/></ListItemButton>)}</List></Card></Grid><Grid size={{xs:12,md:8,lg:9}}><Card variant="outlined"><Content section={section} rows={rows} columns={columns}/></Card></Grid></Grid>}
    </>}
  </Box>
}

function PageHeading({title,subtitle,back}:{title:string;subtitle:string;back:()=>void}){return <PageHeader icon={<CorporateFareOutlined/>} title={title} subtitle={subtitle} actions={<Button startIcon={<ArrowBackOutlined/>} onClick={back}>Back to operations</Button>} />}
function Content({section,rows,columns}:{section:Section;rows:Row[];columns:string[]}){return <><Box sx={{p:{xs:2,sm:3},borderBottom:1,borderColor:'divider'}}><Stack direction="row" gap={2} alignItems="center"><Avatar sx={{bgcolor:'secondary.main',color:'#111'}}>{section.icon}</Avatar><Box><Typography variant="h6" fontWeight={800}>{section.label}</Typography><Typography variant="body2" color="text.secondary">{section.description}</Typography></Box></Stack></Box>{rows.length?<Box sx={{overflowX:'auto'}}><Table><TableHead><TableRow>{columns.map(column=><TableCell key={column} sx={{fontWeight:800,bgcolor:'grey.50'}}>{heading(column)}</TableCell>)}</TableRow></TableHead><TableBody>{rows.map((row,index)=><TableRow key={row.id??index} hover>{columns.map(column=><TableCell key={column}>{['status','priority','type','availability'].includes(column)?<Chip size="small" label={value(column,row[column])} variant="outlined" color={String(row[column]).match(/critical|overdue|failed|rejected/i)?'error':'default'}/>:value(column,row[column])}</TableCell>)}</TableRow>)}</TableBody></Table></Box>:<Box sx={{py:8,px:3,textAlign:'center'}}><Avatar sx={{mx:'auto',mb:2,bgcolor:'grey.100',color:'text.secondary'}}>{section.icon}</Avatar><Typography fontWeight={800}>No {section.label.toLowerCase()} to show</Typography><Typography color="text.secondary" mt={.5}>This area will update automatically when records are created.</Typography></Box>}</>}
