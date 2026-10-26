import { useEffect, useState } from 'react'
import { Alert, Box, Button, Chip, CircularProgress, Dialog, DialogContent, DialogTitle, IconButton, InputAdornment, List, ListItemButton, ListItemText, Stack, TextField, Tooltip, Typography } from '@mui/material'
import SearchOutlined from '@mui/icons-material/SearchOutlined'
import HistoryOutlined from '@mui/icons-material/HistoryOutlined'
import { api } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import { canAccessPage, type AppPage } from '../auth/access'
const workspaces: {page:AppPage;title:string;url:string}[] = [{page:'dashboard',title:'Dashboard',url:'/staff/dashboard'},{page:'bookings',title:'Bookings and quotations',url:'/staff/bookings'},{page:'rentals',title:'Pickups and returns',url:'/staff/hire-operations'},{page:'assets',title:'Asset register',url:'/staff/assets'},{page:'availability',title:'Availability',url:'/staff/availability'},{page:'customers',title:'Customers',url:'/staff/customers'},{page:'finance',title:'Finance',url:'/staff/finance'},{page:'reports',title:'Reports and performance',url:'/staff/reports'},{page:'users',title:'Staff and access',url:'/staff/staff-access'},{page:'divisions',title:'Organization, bookings and system settings',url:'/staff/organization'},{page:'operations',title:'Operations centre',url:'/staff/operations'},{page:'maintenance',title:'Maintenance work',url:'/staff/maintenance'},{page:'scan',title:'Scan asset QR',url:'/staff/scan'}]
export type SearchHit = {id:string;kind:string;title:string;detail:string;url:string}
const resultKinds = ['Asset', 'Booking', 'Customer account', 'Invoice', 'Maintenance job', 'Quotation', 'Approval']

export function GlobalSearch({ collapsed = false }: { collapsed?: boolean }) {
  const {user}=useAuth()
  const accessible=workspaces.filter(w=>canAccessPage(user?.roles??[],w.page))
  const historyKey=`crems.staff.search.recent.${user?.email?.toLowerCase()??'anonymous'}`
  const [open,setOpen]=useState(false),[query,setQuery]=useState(''),[hits,setHits]=useState<SearchHit[]>([]),[loading,setLoading]=useState(false),[error,setError]=useState('')
  const [selected,setSelected]=useState(0)
  const [recent,setRecent]=useState<string[]>(()=>{try{return JSON.parse(localStorage.getItem(historyKey)??'[]') as string[]}catch{return []}})
  useEffect(()=>{const key=(event:KeyboardEvent)=>{if((event.metaKey||event.ctrlKey)&&event.key.toLowerCase()==='k'){event.preventDefault();setOpen(value=>!value)}};window.addEventListener('keydown',key);return()=>window.removeEventListener('keydown',key)},[])
  useEffect(()=>{if(!open)return;const controller=new AbortController();const timer=setTimeout(()=>{setHits([]);setError('');if(query.trim().length<2){setLoading(false);return}setLoading(true);void api.get<SearchHit[]>('/search',{params:{q:query.trim()},signal:controller.signal}).then(r=>setHits(r.data)).catch(()=>{if(!controller.signal.aborted)setError('Search could not be loaded. Please try again.')}).finally(()=>{if(!controller.signal.aborted)setLoading(false)})},300);return()=>{clearTimeout(timer);controller.abort()}},[open,query])
  const pages=query.trim()?accessible.filter(w=>w.title.toLowerCase().includes(query.trim().toLowerCase())):[]
  const grouped=resultKinds.map(kind=>({kind,items:hits.filter(hit=>hit.kind===kind)})).filter(group=>group.items.length)
  const results=[...pages.map(page=>({url:page.url})),...grouped.flatMap(group=>group.items.map(hit=>({url:hit.url}))),...(!query.trim()?recent.map(term=>({url:'',term})):[])]
  function remember(){const term=query.trim();if(term.length<2)return;const next=[term,...recent.filter(value=>value.toLowerCase()!==term.toLowerCase())].slice(0,6);setRecent(next);try{localStorage.setItem(historyKey,JSON.stringify(next))}catch{/* Private browsing may disable storage. */}}
  function openResult(index:number){const result=results[index];if(!result)return;if('term' in result&&typeof result.term==='string'){setQuery(result.term);setSelected(0);return}remember();setOpen(false);window.location.assign(result.url)}
  function onKeys(event:React.KeyboardEvent){if(event.key==='ArrowDown'&&results.length){event.preventDefault();setSelected(value=>(value+1)%results.length)}else if(event.key==='ArrowUp'&&results.length){event.preventDefault();setSelected(value=>(value-1+results.length)%results.length)}else if(event.key==='Enter'&&results[selected]){event.preventDefault();openResult(selected)}}
  let index=-1
  return <>{collapsed?<Tooltip title="Search CREMS" placement="right" arrow><IconButton onClick={()=>setOpen(true)} aria-label="Search CREMS" sx={{color:'inherit',border:'1px solid rgba(255,255,255,.18)',borderRadius:2,mx:'auto',display:'flex'}}><SearchOutlined fontSize="small"/></IconButton></Tooltip>:<Button fullWidth variant="outlined" startIcon={<SearchOutlined/>} onClick={()=>setOpen(true)} sx={{justifyContent:'flex-start',color:'inherit',borderColor:'#666',py:1.2,fontSize:12}}>Search CREMS</Button>}
    <Dialog open={open} onClose={()=>setOpen(false)} fullWidth maxWidth="sm" aria-labelledby="crems-search-title"><DialogTitle id="crems-search-title">Search CREMS</DialogTitle><DialogContent onKeyDown={onKeys}>
      <TextField autoFocus fullWidth placeholder="Asset, booking, customer, invoice, job or quotation" value={query} onChange={e=>{setQuery(e.target.value);setSelected(0);setLoading(e.target.value.trim().length>=2)}} inputProps={{maxLength:100,'aria-label':'Search CREMS records','aria-controls':'crems-search-results','aria-expanded':results.length>0,role:'combobox'}} InputProps={{startAdornment:<InputAdornment position="start"><SearchOutlined/></InputAdornment>}}/>
      <Typography variant="caption" color="text.secondary">Results respect your role, branch and division. Use ↑ ↓ to select and Enter to open.</Typography>
      {loading&&<Stack alignItems="center" p={3} role="status"><CircularProgress size={24}/></Stack>}{error&&<Alert severity="error" sx={{mt:2}}>{error}</Alert>}
      <List id="crems-search-results" sx={{maxHeight:'min(55vh,520px)',overflowY:'auto'}}>
        {pages.length>0&&<Typography variant="overline" sx={{px:2,color:'text.secondary'}}>Pages</Typography>}
        {pages.map(page=>{const current=++index;return <ListItemButton key={page.page} selected={selected===current} onMouseEnter={()=>setSelected(current)} onClick={()=>openResult(current)} sx={{minHeight:52,borderRadius:1}}><ListItemText primary={page.title}/><Chip size="small" label="Page" variant="outlined"/></ListItemButton>})}
        {grouped.map(group=><Box key={group.kind}><Typography variant="overline" sx={{display:'block',px:2,pt:1.5,color:'text.secondary'}}>{group.kind === 'Customer account'?'Customers':group.kind === 'Maintenance job'?'Maintenance jobs':`${group.kind}s`}</Typography>{group.items.map(hit=>{const current=++index;return <ListItemButton key={`${hit.kind}:${hit.id}`} selected={selected===current} onMouseEnter={()=>setSelected(current)} onClick={()=>openResult(current)} sx={{minHeight:52,borderRadius:1}}><ListItemText primary={hit.title} secondary={hit.detail}/><Chip label={hit.kind} size="small" variant="outlined"/></ListItemButton>})}</Box>)}
        {!query.trim()&&recent.length>0&&<Box><Typography variant="overline" sx={{display:'block',px:2,pt:1.5,color:'text.secondary'}}>Recent searches</Typography>{recent.map(term=>{const current=++index;return <ListItemButton key={term} selected={selected===current} onMouseEnter={()=>setSelected(current)} onClick={()=>openResult(current)} sx={{minHeight:52,borderRadius:1}}><HistoryOutlined sx={{mr:1.5,color:'text.secondary'}}/><ListItemText primary={term}/></ListItemButton>})}</Box>}
      </List>
      {!loading&&!error&&query.trim().length>=2&&results.length===0&&<Typography sx={{p:3}} color="text.secondary">No accessible records found. Try a name or reference number.</Typography>}
      {!query.trim()&&!recent.length&&<Typography sx={{p:2}} color="text.secondary">Start typing to search your accessible records.</Typography>}
    </DialogContent></Dialog></>
}
