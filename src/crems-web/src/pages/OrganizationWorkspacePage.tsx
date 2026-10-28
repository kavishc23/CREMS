import { useActionNotice } from '../components/useActionNotice'
import { useEffect, useMemo, useState } from 'react'
import axios from 'axios'
import AddOutlined from '@mui/icons-material/AddOutlined'
import AccountTreeOutlined from '@mui/icons-material/AccountTreeOutlined'
import { Alert, Box, Button, Card, CardContent, Checkbox, Chip, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, Grid, MenuItem, Stack, Tab, Tabs, TextField, Typography } from '@mui/material'
import { api } from '../api/client'
import { SettingSwitch } from '../components/DivisionControls'
import { PageHeader } from '../components/PageHeader'
import { ReminderTemplateSettings } from '../components/ReminderTemplateSettings'
import { DivisionsPage } from './DivisionsPage'
import { BranchesPage } from './BranchesPage'
import { DivisionBookingFlowPage } from './DivisionBookingFlowPage'
import { ChargeDefinitionsPage } from './ChargeDefinitionsPage'
import { BookingApprovalRulesPage } from './BookingApprovalRulesPage'
import { OperatingConfigurationPage } from './OperatingConfigurationPage'
import { SystemConfigurationSection } from './SystemConfigurationPage'

type Service={id:string;name:string;code:string;isActive:boolean;isBookableOnline:boolean}
type Division={id:string;name:string;code:string;isActive:boolean;services:Service[]}
type Branch={id:string;name:string;code:string;divisionIds:string[];isActive:boolean}
type Period={dayOfWeek:string;opensAt:string;closesAt:string;pickupCutoff:string|null;returnCutoff:string|null;isClosed:boolean;afterHoursCharge:number}
type Exception={date:string;name:string;isClosed:boolean;opensAt:string|null;closesAt:string|null;afterHoursCharge:number}
type BranchService={divisionId:string;serviceOfferingId:string;isActive:boolean;isBookable:boolean}
type Category={id:string;divisionId:string;serviceOfferingId:string|null;code:string;name:string;description:string|null;defaultMeterType:string|null;defaultAssetType?:string|null;personnelRequirement:string;isActive:boolean;attributeDefinitions:Attribute[]}
type Attribute={isCustomerVisible:boolean;isReportable:boolean;code:string;name:string;dataType:string;unit:string|null;isRequired:boolean;isSearchable:boolean;displayOrder:number;optionsJson:string}
const days=['Sunday','Monday','Tuesday','Wednesday','Thursday','Friday','Saturday']
const standardPeriods=days.map((day,i)=>({dayOfWeek:day,opensAt:'08:00:00',closesAt:'17:00:00',pickupCutoff:'16:30:00',returnCutoff:'16:30:00',isClosed:i===0,afterHoursCharge:0}))
const workspaceGroups = [
 {id:'business',label:'Business setup',firstTab:0,description:'Divisions, services, branches and the asset data they use.'},
 {id:'commerce',label:'Booking & pricing',firstTab:4,description:'Customer journeys, fees, approvals and group-wide rental defaults.'},
 {id:'platform',label:'Communications',firstTab:8,description:'Customer notifications and automated message templates.'},
] as const
const workspaceSections: Record<string,{tab:number;label:string}[]> = {
 business:[{tab:0,label:'Divisions & services'},{tab:3,label:'Branches'},{tab:2,label:'Asset categories'}],
 commerce:[{tab:4,label:'Booking & quotations'},{tab:5,label:'Fees & charges'},{tab:6,label:'Approval rules'},{tab:7,label:'Rental defaults'}],
 platform:[{tab:8,label:'Notifications'}],
}

export function OrganizationWorkspacePage({userRoles}:{userRoles:string[]}){
 const canConfigure = userRoles.includes('SuperAdministrator')
 const [tab,setTab]=useState(0),[showBranchOperations,setShowBranchOperations]=useState(false),[divisions,setDivisions]=useState<Division[]>([]),[branches,setBranches]=useState<Branch[]>([]),[branchId,setBranchId]=useState(''),[periods,setPeriods]=useState<Period[]>(standardPeriods),[exceptions,setExceptions]=useState<Exception[]>([]),[branchServices,setBranchServices]=useState<BranchService[]>([]),[categories,setCategories]=useState<Category[]>([]),[categoryOpen,setCategoryOpen]=useState(false),[attributeCategory,setAttributeCategory]=useState<Category|null>(null),[error,setError]=useState(''),setSaved = useActionNotice()
 const [editingCategory,setEditingCategory]=useState<string|null>(null)
 const [category,setCategory]=useState({divisionId:'',serviceOfferingId:'',code:'',name:'',description:'',defaultMeterType:'',defaultAssetType:'',personnelRequirement:'None'})
 const [attributes,setAttributes]=useState<Attribute[]>([])
 const [loadedBranchId,setLoadedBranchId]=useState(''),[branchRevision,setBranchRevision]=useState<string|null>(null),[branchSaving,setBranchSaving]=useState(false),[branchReload,setBranchReload]=useState(0)
 const selectedBranch=branches.find(x=>x.id===branchId)
 const branchDivisions=useMemo(()=>divisions.filter(x=>selectedBranch?.divisionIds.includes(x.id)),[divisions,selectedBranch])
 async function load(){if(!canConfigure)return;try{const [d,b,c]=await Promise.all([api.get<Division[]>('/divisions'),api.get<Branch[]>('/branches'),api.get<Category[]>('/asset-categories')]);setDivisions(d.data);setBranches(b.data);setCategories(c.data);if(!branchId&&b.data[0])setBranchId(b.data[0].id)}catch{setError('Unable to load organization configuration.')}}
 // Initial lookup load; later refreshes are triggered by explicit edits.
 // eslint-disable-next-line react-hooks/exhaustive-deps
 useEffect(()=>{void Promise.resolve().then(load)},[])
 useEffect(()=>{if(!canConfigure||!branchId)return;let active=true;void api.get(`/branches/${branchId}/configuration`).then(r=>{if(!active)return;setPeriods(r.data.periods.length?r.data.periods:standardPeriods);setExceptions(r.data.exceptions);setBranchServices(r.data.services);setBranchRevision(r.data.revision??null);setLoadedBranchId(branchId)}).catch(()=>{if(active){setError('Unable to load branch configuration. Switch branches or reload to retry.');setLoadedBranchId('')}});return()=>{active=false}},[branchId,branchReload,canConfigure])
 function enabled(serviceId:string){return branchServices.some(x=>x.serviceOfferingId===serviceId&&x.isActive)}
 function toggleService(divisionId:string,serviceId:string){setBranchServices(current=>enabled(serviceId)?current.filter(x=>x.serviceOfferingId!==serviceId):[...current.filter(x=>x.serviceOfferingId!==serviceId),{divisionId,serviceOfferingId:serviceId,isActive:true,isBookable:true}])}
 async function saveBranch(){if(loadedBranchId!==branchId||branchSaving)return;setBranchSaving(true);setError('');try{await api.put(`/branches/${branchId}/operating-configuration`,{periods,exceptions,expectedRevision:branchRevision,services:branchServices.filter(x=>divisions.some(d=>d.id===x.divisionId&&d.services.some(s=>s.id===x.serviceOfferingId&&s.isActive)))});setSaved('Branch services and calendar saved.');setLoadedBranchId('');setBranchReload(x=>x+1)}catch(reason){const data=axios.isAxiosError(reason)?reason.response?.data:undefined;setError(data?.message??'The branch configuration could not be saved.')}finally{setBranchSaving(false)}}
 async function addCategory(){try{const payload={...category,defaultAssetType:category.defaultAssetType||null,serviceOfferingId:category.serviceOfferingId||null,isActive:true};if(editingCategory)await api.put(`/asset-categories/${editingCategory}`,payload);else await api.post('/asset-categories',payload);setCategoryOpen(false);setCategory({divisionId:'',serviceOfferingId:'',code:'',name:'',description:'',defaultMeterType:'',defaultAssetType:'',personnelRequirement:'None'});await load()}catch(e){const data=axios.isAxiosError(e)?e.response?.data:undefined;setError(data?.message??(data?.errors?Object.values(data.errors).flat().join(' '):'Unable to save the asset category.'))}}
 async function saveAttributes(){if(!attributeCategory)return;try{await api.put(`/asset-categories/${attributeCategory.id}/attributes`,attributes.map((x,i)=>({...x,displayOrder:i})));setAttributeCategory(null);await load()}catch{setError('Unable to save category attributes.')}}
 const now=new Date(),today=days[now.getDay()],current=periods.find(x=>x.dayOfWeek===today);const openNow=Boolean(current&&!current.isClosed&&now.toTimeString().slice(0,8)>=current.opensAt&&now.toTimeString().slice(0,8)<=current.closesAt)


 if(!canConfigure)return <OperatingConfigurationPage/>
 return <Box sx={{maxWidth:1500,mx:'auto'}}>
  <Box sx={{px:{xs:2,sm:3,lg:4},pt:3}}>
   <PageHeader icon={<AccountTreeOutlined/>} title="Organization & settings" subtitle="Manage divisions, branch operations, booking requirements and shared business settings." dense />
   {error&&<Alert severity="error" sx={{mt:2}} onClose={()=>setError('')}>{error}</Alert>}

  </Box>
  <Box sx={{px:{xs:2,sm:3,lg:4},pb:4}}>
   <Box component="nav" aria-label="Organization settings" sx={{bgcolor:'background.paper',border:1,borderColor:'divider',borderRadius:1,mb:2}}>
    <Tabs value={tab} onChange={(_,value:number)=>{setTab(value);setShowBranchOperations(false);if(value<=3&&value>=0)void load()}} variant="scrollable" scrollButtons="auto" aria-label="Organization settings sections" sx={{px:1}}>
     {workspaceGroups.flatMap(area=>workspaceSections[area.id].map(item=><Tab key={item.tab} value={item.tab} label={item.label}/>))}
    </Tabs>
   </Box>
   <Box sx={{minWidth:0,'& > .MuiBox-root':{p:'0 !important'},'& [data-page-header]':{boxShadow:'none',border:0,p:0,bgcolor:'transparent',mb:2,'&:before,&:after':{display:'none'},'& > .MuiStack-root > .MuiBox-root:first-of-type':{display:'none'},'& h4':{fontSize:24}}}}>
 {tab===0&&<DivisionsPage onConfigurationSaved={()=>void load()}/>}
 {tab===3&&showBranchOperations&&<Box sx={{p:{xs:2,sm:3,lg:4},pt:2}}><Button variant="outlined" onClick={()=>setShowBranchOperations(false)} sx={{mb:2}}>Back to branches</Button><Typography variant="h6" fontWeight={800} mb={2}>Branch services & operating hours</Typography><TextField select fullWidth label="Branch to configure" value={branchId} onChange={e=>setBranchId(e.target.value)} sx={{maxWidth:520,mb:3}}>{branches.filter(x=>x.isActive).map(x=><MenuItem key={x.id} value={x.id}>{x.code} — {x.name}</MenuItem>)}</TextField>{!selectedBranch&&<Alert severity="info">No branch is available yet. Add one and assign it to a business/division first.</Alert>}{selectedBranch&&loadedBranchId!==branchId&&<Typography>Loading branch settings…</Typography>}{selectedBranch&&loadedBranchId===branchId&&<Grid container spacing={2.5}><Grid size={{xs:12,lg:5}}><Card variant="outlined"><CardContent><Typography variant="h6" fontWeight={750}>Services at this location</Typography><Typography variant="body2" color="text.secondary" mb={2}>Only services belonging to a division assigned to this branch can be enabled.</Typography>{branchDivisions.map(d=><Box key={d.id} mb={2}><Typography fontWeight={700}>{d.name}</Typography>{d.services.filter(s=>s.isActive).map(s=><FormControlLabel key={s.id} control={<Checkbox checked={enabled(s.id)} onChange={()=>toggleService(d.id,s.id)}/>} label={`${s.name}${s.isBookableOnline?' · online':''}`}/>)}</Box>)}</CardContent></Card></Grid><Grid size={{xs:12,lg:7}}><Card variant="outlined"><CardContent><Stack direction="row" justifyContent="space-between"><Box><Typography variant="h6" fontWeight={750}>Operating calendar</Typography><Typography variant="body2" color="text.secondary">Pickup and return validation uses these hours.</Typography></Box><Chip color={openNow?'success':'default'} label={openNow?'Open now':'Closed now'}/></Stack><Stack mt={2} gap={1}>{periods.map((p,i)=><Stack key={p.dayOfWeek} direction={{xs:'column',sm:'row'}} gap={1} alignItems={{sm:'center'}}><FormControlLabel sx={{width:130}} control={<Checkbox checked={!p.isClosed} onChange={e=>setPeriods(v=>v.map((x,j)=>j===i?{...x,isClosed:!e.target.checked}:x))}/>} label={p.dayOfWeek}/><TextField size="small" type="time" label="Opens" disabled={p.isClosed} value={p.opensAt.slice(0,5)} onChange={e=>setPeriods(v=>v.map((x,j)=>j===i?{...x,opensAt:e.target.value+':00'}:x))}/><TextField size="small" type="time" label="Closes" disabled={p.isClosed} value={p.closesAt.slice(0,5)} onChange={e=>setPeriods(v=>v.map((x,j)=>j===i?{...x,closesAt:e.target.value+':00'}:x))}/><TextField size="small" type="time" label="Pickup cutoff" disabled={p.isClosed} value={p.pickupCutoff?.slice(0,5)||''} onChange={e=>setPeriods(v=>v.map((x,j)=>j===i?{...x,pickupCutoff:e.target.value?e.target.value+':00':null}:x))}/><TextField size="small" type="number" label="After-hours FJD" value={p.afterHoursCharge} onChange={e=>setPeriods(v=>v.map((x,j)=>j===i?{...x,afterHoursCharge:Number(e.target.value)}:x))}/></Stack>)}</Stack><Typography variant="subtitle1" fontWeight={700} mt={3}>Holidays and temporary closures</Typography>{exceptions.map((x,i)=><Stack key={`${x.date}-${i}`} direction={{xs:'column',sm:'row'}} gap={1} mt={1}><TextField size="small" type="date" value={x.date} onChange={e=>setExceptions(v=>v.map((q,j)=>j===i?{...q,date:e.target.value}:q))}/><TextField size="small" label="Reason" value={x.name} onChange={e=>setExceptions(v=>v.map((q,j)=>j===i?{...q,name:e.target.value}:q))}/><FormControlLabel control={<Checkbox checked={x.isClosed} onChange={e=>setExceptions(v=>v.map((q,j)=>j===i?{...q,isClosed:e.target.checked}:q))}/>} label="Closed"/><Button color="error" onClick={()=>setExceptions(v=>v.filter((_,j)=>j!==i))}>Remove</Button></Stack>)}<Button startIcon={<AddOutlined/>} sx={{mt:1}} onClick={()=>setExceptions(v=>[...v,{date:new Date().toISOString().slice(0,10),name:'',isClosed:true,opensAt:null,closesAt:null,afterHoursCharge:0}])}>Add closure</Button></CardContent></Card></Grid><Grid size={12}><Button variant="outlined" disabled={branchSaving} onClick={()=>{setLoadedBranchId('');setBranchReload(x=>x+1)}} sx={{mr:1}}>Reload branch settings</Button><Button variant="contained" disabled={branchSaving} onClick={()=>void saveBranch()}>{branchSaving?'Saving…':'Save branch configuration'}</Button></Grid></Grid>}</Box>}
 {tab===2&&<Box sx={{p:{xs:2,sm:3,lg:4},pt:2}}><Stack direction="row" justifyContent="space-between" mb={2}><Box><Typography variant="h6" fontWeight={750}>Flexible asset categories</Typography><Typography color="text.secondary">Define the data CREMS captures for each kind of asset.</Typography></Box><Button variant="contained" startIcon={<AddOutlined/>} onClick={()=>{setEditingCategory(null);setCategory({divisionId:'',serviceOfferingId:'',code:'',name:'',description:'',defaultMeterType:'',defaultAssetType:'',personnelRequirement:'None'});setCategoryOpen(true)}}>Add category</Button></Stack>{categories.length===0&&<Alert severity="info" sx={{mb:2}}>No asset categories yet. Add one for a business/division and its service.</Alert>}<Grid container spacing={2}>{categories.map(c=><Grid key={c.id} size={{xs:12,md:6,lg:4}}><Card variant="outlined"><CardContent><Stack direction="row" justifyContent="space-between"><Box><Typography variant="overline">{c.code}</Typography><Typography variant="h6" fontWeight={750}>{c.name}</Typography></Box><Chip size="small" label={c.defaultMeterType||'No meter'}/></Stack><Typography variant="body2" color="text.secondary" my={1}>{c.description||'No description'}</Typography><Stack direction="row" gap={.5} flexWrap="wrap">{(c.attributeDefinitions??[]).map(a=><Chip size="small" key={a.code} label={`${a.name}${a.unit?` (${a.unit})`:''}${a.isRequired?' *':''}`}/>)}</Stack><Button sx={{mt:2}} onClick={()=>{setEditingCategory(c.id);setCategory({divisionId:c.divisionId,serviceOfferingId:c.serviceOfferingId??'',code:c.code,name:c.name,description:c.description??'',defaultMeterType:c.defaultMeterType??'',defaultAssetType:c.defaultAssetType??'',personnelRequirement:c.personnelRequirement});setCategoryOpen(true)}}>Edit category</Button><Button sx={{mt:2}} onClick={()=>{setAttributeCategory(c);setAttributes(c.attributeDefinitions??[])}}>Configure fields</Button></CardContent></Card></Grid>)}</Grid></Box>}
 {tab===3&&!showBranchOperations&&<BranchesPage userRoles={userRoles} onSaved={()=>{void load();setBranchReload(x=>x+1)}} onConfigure={id=>{setBranchId(id);setLoadedBranchId('');setBranchReload(x=>x+1);setShowBranchOperations(true)}}/>}
 {tab===4&&<DivisionBookingFlowPage/>}
 {tab===5&&<ChargeDefinitionsPage/>}
 {tab===6&&<BookingApprovalRulesPage/>}
 {tab===7&&<SystemConfigurationSection section="defaults"/>}
 {tab===8&&<><ReminderTemplateSettings/><SystemConfigurationSection section="notifications"/></>}
   </Box>
  </Box>
 <Dialog open={categoryOpen} onClose={()=>setCategoryOpen(false)} fullWidth maxWidth="sm"><DialogTitle>{editingCategory?'Edit asset category':'Add asset category'}</DialogTitle><DialogContent><Stack mt={1} gap={2}>{error&&<Alert severity="error">{error}</Alert>}<TextField select disabled={Boolean(editingCategory)} label="Division" value={category.divisionId} onChange={e=>setCategory({...category,divisionId:e.target.value,serviceOfferingId:''})}>{divisions.filter(x=>x.isActive).map(x=><MenuItem key={x.id} value={x.id}>{x.name}</MenuItem>)}</TextField><TextField select label="Service" value={category.serviceOfferingId} onChange={e=>setCategory({...category,serviceOfferingId:e.target.value})}><MenuItem value="">All services in this division</MenuItem>{divisions.find(x=>x.id===category.divisionId)?.services.filter(x=>x.isActive).map(x=><MenuItem key={x.id} value={x.id}>{x.name}</MenuItem>)}</TextField><TextField label="Code" value={category.code} onChange={e=>setCategory({...category,code:e.target.value})}/><TextField label="Name" value={category.name} onChange={e=>setCategory({...category,name:e.target.value})}/><TextField multiline label="Description" value={category.description} onChange={e=>setCategory({...category,description:e.target.value})}/><TextField select required label="Asset type" value={category.defaultAssetType} onChange={e=>setCategory({...category,defaultAssetType:e.target.value})}>{['Equipment','PassengerVehicle','CommercialVehicle','HeavyEquipment','MaterialHandlingEquipment','PowerEquipment','LightEquipment','Scaffolding','PortableSanitation','WasteContainer','PropertyUnit'].map(x=><MenuItem key={x} value={x}>{x === 'Equipment' ? 'Other asset / resource' : x.replace(/([a-z])([A-Z])/g,'$1 $2')}</MenuItem>)}</TextField><SettingSwitch label="This category uses a usage counter" checked={Boolean(category.defaultMeterType)} onChange={enabled=>setCategory({...category,defaultMeterType:enabled?'Units':''})} />{category.defaultMeterType && <TextField label="Category usage unit" value={category.defaultMeterType} onChange={e=>setCategory({...category,defaultMeterType:e.target.value})} helperText="Use a meaningful unit such as hours, kilometres or cycles." />}</Stack></DialogContent><DialogActions><Button onClick={()=>setCategoryOpen(false)}>Cancel</Button><Button variant="contained" disabled={!category.divisionId||!category.code||!category.name} onClick={()=>void addCategory()}>{editingCategory?'Save changes':'Create'}</Button></DialogActions></Dialog>
 <Dialog open={Boolean(attributeCategory)} onClose={()=>setAttributeCategory(null)} fullWidth maxWidth="md">
  <DialogTitle>Fields · {attributeCategory?.name}</DialogTitle>
  <DialogContent>
   <Stack gap={2} mt={1}>
    {attributes.map((a,i)=><Box key={i} sx={{border:1,borderColor:'divider',borderRadius:2,p:2,minWidth:0}}>
     <Box sx={{display:'grid',gridTemplateColumns:{xs:'1fr',sm:'repeat(2, minmax(0, 1fr))',md:'repeat(4, minmax(0, 1fr))'},gap:1.5}}>
      <TextField fullWidth size="small" label="Code" value={a.code} onChange={e=>setAttributes(v=>v.map((x,j)=>j===i?{...x,code:e.target.value}:x))}/>
      <TextField fullWidth size="small" label="Field name" value={a.name} onChange={e=>setAttributes(v=>v.map((x,j)=>j===i?{...x,name:e.target.value}:x))}/>
      <TextField fullWidth size="small" select label="Type" value={a.dataType} onChange={e=>setAttributes(v=>v.map((x,j)=>j===i?{...x,dataType:e.target.value}:x))}>{['Text','Number','Integer','Choice','Date','Boolean'].map(x=><MenuItem key={x} value={x}>{x}</MenuItem>)}</TextField>
      <TextField fullWidth size="small" label="Unit" value={a.unit||''} onChange={e=>setAttributes(v=>v.map((x,j)=>j===i?{...x,unit:e.target.value}:x))}/>
     </Box>
     <Box sx={{display:'grid',gridTemplateColumns:{xs:'1fr',sm:'repeat(2, minmax(0, 1fr))',md:'repeat(4, minmax(0, 1fr))'},gap:1,mt:1.5,alignItems:'center'}}>
      <FormControlLabel sx={{m:0}} control={<Checkbox checked={a.isRequired} onChange={e=>setAttributes(v=>v.map((x,j)=>j===i?{...x,isRequired:e.target.checked}:x))}/>} label="Required"/>
      <FormControlLabel sx={{m:0}} control={<Checkbox checked={a.isSearchable} onChange={e=>setAttributes(v=>v.map((x,j)=>j===i?{...x,isSearchable:e.target.checked}:x))}/>} label="Search"/>
      <FormControlLabel sx={{m:0}} control={<Checkbox checked={a.isCustomerVisible} onChange={e=>setAttributes(v=>v.map((x,j)=>j===i?{...x,isCustomerVisible:e.target.checked}:x))}/>} label="Customer visible"/>
      <Button color="error" sx={{justifySelf:{xs:'start',md:'end'}}} onClick={()=>setAttributes(v=>v.filter((_,j)=>j!==i))}>Remove</Button>
     </Box>
    </Box>)}
    <Button startIcon={<AddOutlined/>} sx={{alignSelf:'flex-start'}} onClick={()=>setAttributes(v=>[...v,{isCustomerVisible:true,isReportable:true,code:'',name:'',dataType:'Text',unit:'',isRequired:false,isSearchable:false,displayOrder:v.length,optionsJson:'[]'}])}>Add field</Button>
   </Stack>
  </DialogContent>
  <DialogActions><Button onClick={()=>setAttributeCategory(null)}>Cancel</Button><Button variant="contained" onClick={()=>void saveAttributes()}>Save fields</Button></DialogActions>
 </Dialog>

 </Box>
}
