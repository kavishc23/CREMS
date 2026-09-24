import { lazy, Suspense, useCallback, useEffect, useRef, useState } from 'react'
import NotificationsOutlined from '@mui/icons-material/NotificationsOutlined'
import { Alert, Badge, Box, Button, Checkbox, Chip, Divider, FormControlLabel, IconButton, List, ListItemButton, ListItemText, MenuItem, Pagination, Popover, Stack, TextField, Typography } from '@mui/material'
import { api } from '../api/client'
import { toast } from './ToastHost'
const ComposeNotification = lazy(() => import('./ComposeNotification'))
const StaffNotificationSettings = lazy(() => import('./StaffNotificationSettings'))
const ComposeStaffNotification = lazy(() => import('./ComposeStaffNotification'))
type Notice = { id:string;title:string;message:string;url:string|null;createdAt:string;isRead:boolean;kind:string;severity:string;actionLabel:string|null;actionType:string|null;isExpired:boolean;systemGenerated:boolean;toastEnabled:boolean }
type Inbox = { items:Notice[];unreadCount:number;total:number;page:number;pageSize:number }
const empty:Inbox = {items:[],unreadCount:0,total:0,page:1,pageSize:20}
const categories = ['Booking','Approval','Maintenance','Finance','Administration','Announcement']

export function NotificationBell({customer=false,canSend=false}:{customer?:boolean;canSend?:boolean}) {
  const endpoint = customer?'/customer-account/notifications':'/notifications'
  const [anchor,setAnchor]=useState<HTMLElement|null>(null),[data,setData]=useState<Inbox>(empty),[count,setCount]=useState(0),[error,setError]=useState('')
  const [compose,setCompose]=useState(false),[staffCompose,setStaffCompose]=useState(false),[preferences,setPreferences]=useState(false)
  const [category,setCategory]=useState(''),[severity,setSeverity]=useState(''),[unread,setUnread]=useState(false),[history,setHistory]=useState(false),[page,setPage]=useState(1)
  const seen=useRef<Set<string>|null>(null)
  const version=useRef<string|undefined>(undefined)
  const load=useCallback(async(signal?:AbortSignal,wait=false)=>{
    try {
      const latest=await api.get<Inbox>(endpoint,{signal,timeout:35000,params:wait&&version.current?{since:version.current}:undefined})
      if(signal?.aborted)return
      version.current=latest.headers["x-notification-version"]
      const latestInbox=latest.data
      const fresh=latestInbox.items.filter(n=>!n.isRead&&!n.isExpired&&n.toastEnabled&&!seen.current?.has(n.id))
      if(seen.current&&fresh.length)toast(fresh.length===1?fresh[0].title+': '+fresh[0].message:fresh.length+' new notifications',fresh.some(n=>n.severity==='Urgent')?'error':'info')
      seen.current??=new Set()
      latestInbox.items.forEach(n=>seen.current?.add(n.id))
      setCount(latestInbox.unreadCount)
      const filtered=category||severity||unread||history||page!==1
        ? await api.get<Inbox>(endpoint,{signal,params:{category:category||undefined,severity:severity||undefined,unread,history,page}})
        : {data:latestInbox}
      if(signal?.aborted)return
      setData(filtered.data);setError('')
    }catch{if(!signal?.aborted)setError('Notifications unavailable. Please refresh.')}
  },[endpoint,category,severity,unread,history,page])
  useEffect(()=>{
    const controller=new AbortController()
    let retry:ReturnType<typeof setTimeout>|undefined
    const run=async()=>{
      await load(controller.signal)
      while(!controller.signal.aborted){
        await load(controller.signal,true)
        // Also prevents a tight retry loop when offline or signed out.
        await new Promise<void>(resolve=>{const done=()=>{clearTimeout(retry);controller.signal.removeEventListener('abort',done);resolve()};retry=setTimeout(done,1000);controller.signal.addEventListener('abort',done,{once:true});if(controller.signal.aborted)done()})
      }
    }
    void run()
    const refresh=()=>{void load(controller.signal)}
    window.addEventListener('crems:data-changed',refresh)
    return()=>{controller.abort();clearTimeout(retry);window.removeEventListener('crems:data-changed',refresh)}
  },[load])
  async function read(ids:string[],all=false){try{await api.post(`${endpoint}/read`,{ids,all});await load()}catch{setError('Could not mark notifications as read.')}}
  async function visit(n:Notice){
    await read([n.id])
    if(!n.isExpired&&n.url?.startsWith('/')&&!n.url.startsWith('//')&&!n.url.includes('\\')){setAnchor(null);window.location.assign(n.url)}
  }
  return <>
    <IconButton color="inherit" aria-label={`Notifications, ${count} unread`} onClick={e=>setAnchor(e.currentTarget)}><Badge color="error" badgeContent={count} max={99}><NotificationsOutlined/></Badge></IconButton>
    <Popover open={Boolean(anchor)} anchorEl={anchor} onClose={()=>setAnchor(null)} anchorOrigin={{vertical:'bottom',horizontal:'right'}} transformOrigin={{vertical:'top',horizontal:'right'}}>
      <Box sx={{width:{xs:340,sm:480},maxHeight:'80vh',overflow:'auto'}}>
        <Stack p={2} direction="row" alignItems="center" justifyContent="space-between"><Box><Typography variant="h6">Notification centre</Typography><Typography variant="caption" color="text.secondary">{count} unread · Changes and actions</Typography></Box><Button size="small" onClick={()=>void load()}>Refresh</Button></Stack>
        <Stack px={2} pb={1} direction="row" gap={1} flexWrap="wrap"><Button size="small" disabled={!count} onClick={()=>void read([],true)}>Mark all as read</Button>{!customer&&<Button size="small" onClick={()=>setPreferences(true)}>Preferences</Button>}{canSend&&<><Button size="small" onClick={()=>setStaffCompose(true)}>Notify staff</Button><Button size="small" onClick={()=>setCompose(true)}>Notify customers</Button></>}</Stack>
        {!customer&&<Box px={2} pb={1.5}><Stack direction="row" gap={1}><TextField select fullWidth size="small" label="Category" value={category} onChange={e=>{setCategory(e.target.value);setPage(1)}}><MenuItem value="">All categories</MenuItem>{categories.map(x=><MenuItem key={x} value={x}>{x==='Booking'?'Rentals':x==='Approval'?'Approvals':x}</MenuItem>)}</TextField><TextField select fullWidth size="small" label="Priority" value={severity} onChange={e=>{setSeverity(e.target.value);setPage(1)}}><MenuItem value="">All priorities</MenuItem>{['Info','Warning','Urgent'].map(x=><MenuItem key={x} value={x}>{x}</MenuItem>)}</TextField></Stack><FormControlLabel control={<Checkbox size="small" checked={unread} onChange={e=>{setUnread(e.target.checked);setPage(1)}}/>} label="Unread only"/><FormControlLabel control={<Checkbox size="small" checked={history} onChange={e=>{setHistory(e.target.checked);setPage(1)}}/>} label="Include history"/></Box>}
        <Divider/>{error&&<Alert severity="warning">{error}</Alert>}
        <List disablePadding>{data.items.map(n=><ListItemButton key={n.id} onClick={()=>void visit(n)} alignItems="flex-start" sx={{px:2,py:1.5,bgcolor:n.isRead?'transparent':'#fff8d9',borderBottom:1,borderColor:'divider',opacity:n.isExpired?.65:1}}><ListItemText disableTypography primary={<Stack direction="row" gap={1} alignItems="center"><Typography fontWeight={n.isRead?500:750} sx={{flex:1}}>{n.title}</Typography>{n.severity!=='Info'&&<Chip size="small" label={n.severity} color={n.severity==='Urgent'?'error':'warning'}/>}</Stack>} secondary={<><Typography variant="body2" sx={{mt:.5,whiteSpace:'pre-wrap',overflowWrap:'anywhere'}}>{n.message}</Typography><Typography variant="caption" color="text.secondary">{new Date(n.createdAt).toLocaleString()} · {n.systemGenerated?'System':'Staff message'}</Typography>{n.isExpired?<Typography variant="caption" display="block">Expired / superseded · history only</Typography>:n.url&&<Typography variant="body2" fontWeight={750} mt={1}>{n.actionLabel||'Open related record'} →</Typography>}</>}/></ListItemButton>)}</List>
        {!data.items.length&&!error&&<Typography p={3} color="text.secondary">No notifications match this view.</Typography>}
        {data.total>data.pageSize&&<Stack alignItems="center" p={2}><Pagination size="small" page={data.page} count={Math.ceil(data.total/data.pageSize)} onChange={(_,v)=>setPage(v)}/></Stack>}
        <Typography px={2} py={1} variant="caption" color="text.secondary" display="block">90-day history · Actions always require current record access.</Typography>
      </Box>
    </Popover>
    <Suspense fallback={null}>
      {canSend&&compose&&<ComposeNotification open onClose={()=>setCompose(false)}/>}
      {canSend&&staffCompose&&<ComposeStaffNotification onClose={()=>setStaffCompose(false)}/>}
      {!customer&&preferences&&<StaffNotificationSettings onClose={()=>setPreferences(false)} onSaved={()=>void load()}/>}
    </Suspense>
  </>
}
