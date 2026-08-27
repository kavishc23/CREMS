import { useCallback, useEffect, useState, type ReactNode } from 'react'
import BuildCircleOutlined from '@mui/icons-material/BuildCircleOutlined'
import CalendarMonthOutlined from '@mui/icons-material/CalendarMonthOutlined'
import DirectionsCarOutlined from '@mui/icons-material/DirectionsCarOutlined'
import EventBusyOutlined from '@mui/icons-material/EventBusyOutlined'
import ReceiptLongOutlined from '@mui/icons-material/ReceiptLongOutlined'
import TrendingUpOutlined from '@mui/icons-material/TrendingUpOutlined'
import { Alert, Avatar, Box, Button, Card, CardContent, Chip, Grid, Skeleton, Stack, Typography } from '@mui/material'
import { formatRole, getPrimaryRole, roles, type AppPage } from '../auth/access'
import { api } from '../api/client'

type DashboardPageProps = { userName: string; userRoles: string[]; onNavigate: (page: AppPage) => void }
type DashboardSummary = {
  availableAssets: number; activeRentals: number; underMaintenance: number; overdueRentals: number
  pendingRequests: number; upcomingBookings: number; servicesDueSoon: number
  vehicleUtilization: number; equipmentUtilization: number; totalAssets: number
}
type OperationsSummary = { currentRevenue:number;previousRevenue:number;currentCosts:number;previousCosts:number;updatedAt:string;fleet:{division:string;status:string;count:number}[];alerts:{type:string;detail:string;priority:string;page:AppPage}[];activity:{id:string;action:string;summary:string;userName:string;occurredAt:string;branchName:string|null}[] }
type MetricKey = keyof DashboardSummary
type RoleView = {
  eyebrow: string; title: string; metricKeys: MetricKey[]
}

const emptySummary: DashboardSummary = { availableAssets: 0, activeRentals: 0, underMaintenance: 0, overdueRentals: 0, pendingRequests: 0, upcomingBookings: 0, servicesDueSoon: 0, vehicleUtilization: 0, equipmentUtilization: 0, totalAssets: 0 }
const metricDetails: Record<MetricKey, { label: string; helper: string; icon: ReactNode; tone: string }> = {
  availableAssets: { label: 'Assets ready', helper: 'Available for allocation', icon: <DirectionsCarOutlined />, tone: '#ffed00' },
  activeRentals: { label: 'Currently on hire', helper: 'Active customer rentals', icon: <ReceiptLongOutlined />, tone: '#d8ca00' },
  underMaintenance: { label: 'In maintenance', helper: 'Unavailable during service', icon: <BuildCircleOutlined />, tone: '#f3a712' },
  overdueRentals: { label: 'Overdue returns', helper: 'Require follow-up', icon: <EventBusyOutlined />, tone: '#d14343' },
  pendingRequests: { label: 'Requests to review', helper: 'Awaiting staff action', icon: <CalendarMonthOutlined />, tone: '#ffed00' },
  upcomingBookings: { label: 'Upcoming collections', helper: 'Confirmed future bookings', icon: <CalendarMonthOutlined />, tone: '#d8ca00' },
  servicesDueSoon: { label: 'Services due soon', helper: 'Due within 30 days', icon: <BuildCircleOutlined />, tone: '#f3a712' },
  vehicleUtilization: { label: 'Vehicle utilization', helper: 'Currently hired out', icon: <TrendingUpOutlined />, tone: '#ffed00' },
  equipmentUtilization: { label: 'Equipment utilization', helper: 'Currently hired out', icon: <TrendingUpOutlined />, tone: '#d8ca00' },
  totalAssets: { label: 'Assets in scope', helper: 'Active register records', icon: <DirectionsCarOutlined />, tone: '#ffed00' },
}

const roleViews: Record<string, RoleView> = {
  [roles.superAdministrator]: { eyebrow: 'Group administration', title: 'System and group overview', metricKeys: ['totalAssets', 'activeRentals', 'overdueRentals'] },
  [roles.administrator]: { eyebrow: 'Administration', title: 'Operations and access overview', metricKeys: ['totalAssets', 'pendingRequests', 'overdueRentals'] },
  [roles.branchManager]: { eyebrow: 'Branch management', title: 'Your branch at a glance', metricKeys: ['activeRentals', 'overdueRentals', 'underMaintenance'] },
  [roles.rentalOfficer]: { eyebrow: 'Rental desk', title: 'Today’s rental work', metricKeys: ['pendingRequests', 'upcomingBookings', 'overdueRentals'] },
  [roles.maintenanceOfficer]: { eyebrow: 'Workshop', title: 'Maintenance work today', metricKeys: ['underMaintenance', 'servicesDueSoon', 'availableAssets'] },
}

function formatToday() {
  return new Intl.DateTimeFormat('en-FJ', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' }).format(new Date())
}

export function DashboardPage({ userName, userRoles, onNavigate }: DashboardPageProps) {
  const primaryRole = getPrimaryRole(userRoles)
  const canViewOperations = new Set<string>([roles.superAdministrator,roles.administrator,roles.branchManager]).has(primaryRole)
  const [summary, setSummary] = useState<DashboardSummary>(emptySummary)
  const [operations,setOperations]=useState<OperationsSummary|null>(null)
  const [loading, setLoading] = useState(true)
  const [loadFailed, setLoadFailed] = useState(false)
  const loadSummary = useCallback(async () => {
    setLoading(true); setLoadFailed(false)
    try { const [summaryResponse,operationsResponse]=await Promise.all([api.get<DashboardSummary>('/dashboard/summary'),canViewOperations?api.get<OperationsSummary>('/dashboard/operations'):Promise.resolve(null)]);setSummary(summaryResponse.data);setOperations(operationsResponse?.data??null) }
    catch { setLoadFailed(true) }
    finally { setLoading(false) }
  }, [canViewOperations])
  useEffect(() => {
    // Initial synchronization with the role-scoped dashboard summary.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void loadSummary()
  }, [loadSummary])

  const view = roleViews[primaryRole] ?? roleViews[roles.rentalOfficer]
  const firstName = userName.trim().split(/\s+/)[0] || 'there'
  return <Box sx={{ p: { xs: 2.5, sm: 4, lg: 5 }, maxWidth: 1380, mx: 'auto' }}>
    <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" alignItems={{ md: 'flex-end' }} gap={2} mb={3}>
      <Box>
        <Stack direction="row" gap={1} alignItems="center" mb={.75}>
          <Typography variant="overline" fontWeight={800} letterSpacing={1.2} color="text.secondary">{view.eyebrow}</Typography>
          <Chip size="small" label={formatRole(primaryRole)} sx={{ fontWeight: 750, bgcolor: 'secondary.main' }} />
        </Stack>
        <Typography variant="h4" fontWeight={850}>{view.title}</Typography>
        <Typography color="text.secondary" mt={.5}>{formatToday()} · Welcome, {firstName}</Typography>
      </Box>
    </Stack>

    {loadFailed && <Alert severity="warning" action={<Button color="inherit" size="small" onClick={() => void loadSummary()}>Retry</Button>} sx={{ mb: 2 }}>Live dashboard figures are temporarily unavailable.</Alert>}

    <Grid container spacing={{ xs: 2.5, md: 3.5 }}>
      {view.metricKeys.map(key => <Grid key={key} size={{ xs: 12, sm: 4 }}><MetricCard metricKey={key} value={summary[key]} loading={loading} /></Grid>)}
    </Grid>

    {operations&&<OperationsPanel data={operations} summary={summary} onNavigate={onNavigate}/>}

  </Box>
}

function OperationsPanel({data,summary,onNavigate}:{data:OperationsSummary;summary:DashboardSummary;onNavigate:(page:AppPage)=>void}){
  const revenueDelta=data.previousRevenue?Math.round((data.currentRevenue-data.previousRevenue)*100/data.previousRevenue):null
  const costDelta=data.previousCosts?Math.round((data.currentCosts-data.previousCosts)*100/data.previousCosts):null
  const totals=data.fleet.reduce<Record<string,number>>((result,item)=>({...result,[item.status]:(result[item.status]??0)+item.count}),{})
  const divisions=Array.from(new Set(data.fleet.map(item=>item.division)))
  const money=(value:number)=>`FJD ${value.toLocaleString('en-FJ',{maximumFractionDigits:0})}`
  return <Box mt={{xs:3,md:4}}><Stack direction={{xs:'column',sm:'row'}} justifyContent="space-between" gap={1} mb={2}><Box><Typography variant="h5" fontWeight={850}>Operational performance</Typography><Typography variant="body2" color="text.secondary">Live financial, fleet and exception information for your assigned scope.</Typography></Box><Typography variant="caption" color="text.secondary">Updated {new Date(data.updatedAt).toLocaleString('en-FJ')}</Typography></Stack><Grid container spacing={2.5}><Grid size={{xs:12,md:6,lg:3}}><TrendCard label="Revenue this month" value={money(data.currentRevenue)} delta={revenueDelta} goodWhenPositive/></Grid><Grid size={{xs:12,md:6,lg:3}}><TrendCard label="Maintenance cost" value={money(data.currentCosts)} delta={costDelta}/></Grid><Grid size={{xs:12,md:6,lg:3}}><TrendCard label="Fleet utilization" value={`${summary.totalAssets?Math.round(summary.activeRentals*100/summary.totalAssets):0}%`} detail={`${summary.activeRentals} assets currently on hire`}/></Grid><Grid size={{xs:12,md:6,lg:3}}><TrendCard label="Overdue returns" value={String(summary.overdueRentals)} detail={summary.overdueRentals?'Immediate follow-up required':'No overdue returns'}/></Grid>
    <Grid size={{xs:12,lg:7}}><FleetStatus fleet={data.fleet} totals={totals} divisions={divisions}/></Grid>
    <Grid size={{xs:12,lg:5}}><Card variant="outlined" sx={{height:'100%'}}><CardContent sx={{p:{xs:2.5,md:3}}}><Typography variant="h6" fontWeight={800}>Alerts requiring action</Typography><Typography variant="body2" color="text.secondary" mb={1.5}>Highest-priority exceptions are shown first.</Typography><Stack>{data.alerts.length?data.alerts.map((alert,index)=><Button key={`${alert.type}-${index}`} color="inherit" onClick={()=>onNavigate(alert.page)} sx={{p:1.5,textAlign:'left',justifyContent:'flex-start',borderBottom:index<data.alerts.length-1?1:0,borderColor:'divider',borderRadius:0}}><Stack direction="row" gap= {1.25} alignItems="flex-start"><Box sx={{width:9,height:9,borderRadius:'50%',mt:.7,bgcolor:alert.priority==='High'?'error.main':'warning.main',flex:'0 0 auto'}}/><Box><Typography variant="body2" fontWeight={750}>{alert.type}</Typography><Typography variant="caption" color="text.secondary">{alert.detail}</Typography></Box></Stack></Button>):<Typography color="text.secondary" py={3}>No urgent operational alerts.</Typography>}</Stack></CardContent></Card></Grid>
    <Grid size={{xs:12}}><Card variant="outlined"><CardContent sx={{p:{xs:2.5,md:3}}}><Typography variant="h6" fontWeight={800}>Recent activity</Typography><Typography variant="body2" color="text.secondary" mb={1}>Latest recorded events in your scope.</Typography><Grid container spacing={0}>{data.activity.map(item=><Grid key={item.id} size={{xs:12,md:6}}><Box sx={{py:1.5,pr:2,borderBottom:1,borderColor:'divider'}}><Typography variant="body2" fontWeight={750}>{item.action}</Typography><Typography variant="caption" color="text.secondary" display="block">{item.summary}</Typography><Typography variant="caption" color="text.disabled">{item.userName} · {new Date(item.occurredAt).toLocaleString('en-FJ')}{item.branchName?` · ${item.branchName}`:''}</Typography></Box></Grid>)}</Grid></CardContent></Card></Grid></Grid></Box>
}

function TrendCard({label,value,delta,detail,goodWhenPositive=false}:{label:string;value:string;delta?:number|null;detail?:string;goodWhenPositive?:boolean}){const favorable=delta==null?null:goodWhenPositive?delta>=0:delta<=0;return <Card variant="outlined" sx={{height:'100%',borderTop:3,borderTopColor:'secondary.main'}}><CardContent><Typography variant="body2" color="text.secondary">{label}</Typography><Typography variant="h4" fontWeight={850} mt={.75}>{value}</Typography>{delta==null?<Typography variant="caption" color="text.secondary">{detail??'No previous-period comparison'}</Typography>:<Typography variant="caption" color={favorable?'success.main':'error.main'} fontWeight={750}>{delta>=0?'▲':'▼'} {Math.abs(delta)}% versus last month</Typography>}</CardContent></Card>}
function FleetStatus({fleet,totals,divisions}:{fleet:OperationsSummary['fleet'];totals:Record<string,number>;divisions:string[]}){
  const statuses=['Available','Rented','Reserved','Maintenance','Inspection','OutOfService']
  const fleetTotal=Object.values(totals).reduce((sum,count)=>sum+count,0)
  const utilization=fleetTotal?Math.round(((totals.Rented??0)+(totals.Reserved??0))*100/fleetTotal):0
  return <Card variant="outlined" sx={{height:'100%'}}><CardContent sx={{p:{xs:2.5,md:3}}}><Stack direction="row" justifyContent="space-between" alignItems="flex-start" gap={2}><Box><Typography variant="h6" fontWeight={800}>Fleet status</Typography><Typography variant="body2" color="text.secondary">Availability and utilization by division.</Typography></Box><Box textAlign="right"><Typography variant="h4" fontWeight={850}>{utilization}%</Typography><Typography variant="caption" color="text.secondary">utilized or reserved</Typography></Box></Stack><Stack gap={3} mt={3}>{divisions.map(division=>{const rows=fleet.filter(item=>item.division===division);const total=rows.reduce((sum,item)=>sum+item.count,0);return <Box key={division}><Stack direction="row" justifyContent="space-between" alignItems="baseline"><Typography fontWeight={800}>{division}</Typography><Typography variant="caption" color="text.secondary">{total} assets</Typography></Stack><Stack direction="row" sx={{height:18,borderRadius:2,overflow:'hidden',bgcolor:'grey.100',mt:1}}>{statuses.map(status=>{const count=rows.find(item=>item.status===status)?.count??0;return count?<Box key={status} title={`${statusLabel(status)}: ${count} (${Math.round(count*100/total)}%)`} sx={{width:`${count*100/total}%`,bgcolor:statusTone(status),minWidth:4}}/>:null})}</Stack><Stack direction="row" gap={1.5} mt={1} flexWrap="wrap">{statuses.map(status=>{const count=rows.find(item=>item.status===status)?.count??0;return count?<Typography key={status} variant="caption" color="text.secondary"><Box component="span" sx={{display:'inline-block',width:8,height:8,borderRadius:.5,bgcolor:statusTone(status),mr:.6}}/>{statusLabel(status)} {count}</Typography>:null})}</Stack></Box>})}</Stack><Stack direction="row" gap={2} mt={3} pt={2} borderTop={1} borderColor="divider" flexWrap="wrap">{statuses.map(status=>{const count=totals[status]??0;return count?<Typography key={status} variant="caption" fontWeight={700}><Box component="span" sx={{display:'inline-block',width:9,height:9,borderRadius:.5,bgcolor:statusTone(status),mr:.7}}/>{statusLabel(status)} · {count}</Typography>:null})}</Stack></CardContent></Card>
}
function statusLabel(status:string){return status==='Rented'?'On hire':status==='OutOfService'?'Out of service':status}
function statusTone(status:string){if(status==='Available')return 'success.main';if(status==='Rented')return '#3e5c76';if(status==='Reserved')return 'info.light';if(status==='Maintenance')return 'warning.main';if(status==='Inspection')return 'secondary.dark';return 'error.main'}

function MetricCard({ metricKey, value, loading }: { metricKey: MetricKey; value: number; loading: boolean }) {
  const detail = metricDetails[metricKey]
  const percentage = metricKey === 'vehicleUtilization' || metricKey === 'equipmentUtilization'
  return <Card variant="outlined" sx={{ height: '100%', borderTop: 4, borderTopColor: detail.tone }}><CardContent sx={{ p: { xs: 3, md: 3.25 } }}><Stack direction="row" justifyContent="space-between" gap={2.5}><Box><Typography variant="body2" color="text.secondary">{detail.label}</Typography>{loading ? <Skeleton width={72} height={52} /> : <Typography variant="h3" fontWeight={850} mt={.5}>{value}{percentage ? '%' : ''}</Typography>}<Typography variant="caption" color="text.secondary">{detail.helper}</Typography></Box><Avatar variant="rounded" sx={{ bgcolor: detail.tone, color: detail.tone === '#d14343' ? 'white' : '#111', width: 46, height: 46 }}>{detail.icon}</Avatar></Stack></CardContent></Card>
}
