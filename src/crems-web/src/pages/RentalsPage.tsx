import { InlineIssueNavigation } from '../components/InlineValidation'
import { FormError } from '../components/FormError'
import { useCallback, useEffect, useRef, useState } from 'react'
import axios from 'axios'
import SaveOutlined from '@mui/icons-material/SaveOutlined'
import SearchOutlined from '@mui/icons-material/SearchOutlined'
import ArrowForwardOutlined from '@mui/icons-material/ArrowForwardOutlined'
import PhoneOutlined from '@mui/icons-material/PhoneOutlined'
import WarningAmberOutlined from '@mui/icons-material/WarningAmberOutlined'
import ReceiptLongOutlined from '@mui/icons-material/ReceiptLongOutlined'
import ExpandMoreOutlined from '@mui/icons-material/ExpandMoreOutlined'
import {
  Accordion, AccordionDetails, AccordionSummary, Alert, Box, Button, Card, CardContent, Checkbox, Chip, CircularProgress, Dialog, DialogActions,
  DialogContent, DialogTitle, Divider, FormControlLabel, Grid, InputAdornment, MenuItem, Pagination, Radio, RadioGroup, Stack,
  Step, StepLabel, Stepper, Tab, Tabs, Table, TableBody, TableCell, TableContainer, TableHead,
  TableRow, TextField, Typography,
} from '@mui/material'
import { api } from '../api/client'
import { paymentAmountError, receiptValidationError, previewAmount, roundMoney } from '../utils/rentalPaymentValidation'
import { EndHireAfterIncidentDialog } from '../components/EndHireAfterIncidentDialog'
import { IncidentSettlementDialog } from '../components/IncidentSettlementDialog'
import { useAuth } from '../auth/AuthContext'
import { canAccessPage } from '../auth/access'
import { RentalInspectionHistory } from '../components/RentalInspectionHistory'
import { InspectionPhotos } from '../components/InspectionPhotos'
import { LicenceClassSelect } from '../components/LicenceClassSelect'
import { RentalChecklist } from '../components/RentalChecklist'
import { useRentalInspection } from '../components/useRentalInspection'
import { EvidenceComparison } from '../components/RentalInspectionContext'
import { SignaturePad } from '../components/SignaturePad'
import { clearRentalDraft, readRentalDraft, saveRentalDraft } from '../components/rentalDrafts'
import { PageHeader } from '../components/PageHeader'
import { WorkQueueSummary } from '../components/WorkQueueSummary'
import { downloadRentalAgreementPdf, openRentalAgreementPdf, type RentalAgreementData } from '../utils/rentalAgreementPdf'

type QueueKey='PickupToday'|'OnHire'|'DueToday'|'Overdue'|'ReturnInProgress'|'RecentlyCompleted'|'AllCompleted'|'IncidentSettlements'
type Counts={pickupToday:number;onHire:number;dueToday:number;overdue:number;returnInProgress:number;recentlyCompleted:number}
type RentalRow={id:string;incidentClosedAt?:string|null;bookingNumber:string;status:string;customerName:string;customerPhone:string|null;branchName:string;assetNumber:string|null;assetName:string|null;assetCategoryCode:string|null;assetCategory:string|null;hasProfessionalPersonnel:boolean;startAt:string|null;endAt:string|null;hasAgreement:boolean;agreementStatus:string;amountPaid:number;rentalRevenue:number;rentalCharge:number;balanceDue:number;depositRequired:number;bondStatus:string;bondAmountHeld:number;bondDeductionAmount:number;bondRefundAmount:number;preHireInspectionComplete:boolean;returnInspectionComplete:boolean;preHireCondition:string|null;preHireMeterReading:number|null;preHireFuelLevelPercent:number|null;warning:string|null}
const displayedAgreementStatus=(row:RentalRow)=>{
  if(row.incidentClosedAt)return 'Ended after incident'
  if(row.status==='Confirmed'||!row.hasAgreement)return 'Not signed'
  if(row.status==='Completed'&&row.returnInspectionComplete)return 'Completed'
  if(row.status==='ConvertedToRental'||row.status==='Completed')return 'Active'
  return 'Not signed'
}
type QueueResponse={items:RentalRow[];counts:Counts;page:number;pageSize:number;total:number;incidentSettlements?:number}
const queues:{key:QueueKey;label:string;count?:keyof Counts}[]=[{key:'PickupToday',label:'Pickup today',count:'pickupToday'},{key:'OnHire',label:'On hire',count:'onHire'},{key:'DueToday',label:'Due today',count:'dueToday'},{key:'Overdue',label:'Overdue',count:'overdue'},{key:'ReturnInProgress',label:'Return in progress',count:'returnInProgress'},{key:'RecentlyCompleted',label:'Recently completed',count:'recentlyCompleted'},{key:'AllCompleted',label:'Completed history'}]
const date=(value:string|null,withTime=false)=>value?new Intl.DateTimeFormat('en-FJ',{timeZone:'Pacific/Fiji',day:'numeric',month:'short',year:'numeric',...(withTime?{hour:'numeric',minute:'2-digit'}:{})}).format(new Date(value)):'Not set'
const fijiInput=(value:string)=>new Date(Date.parse(value)+12*60*60*1000).toISOString().slice(0,16)
const money=(v:number)=>new Intl.NumberFormat('en-FJ',{style:'currency',currency:'FJD'}).format(v)
const err=(reason:unknown,fallback:string)=>{const d=axios.isAxiosError(reason)?reason.response?.data:undefined;const errors=d?.errors as Record<string,string[]>|undefined;return errors?Object.values(errors).flat().join(' '):d?.message??d?.detail??(reason instanceof Error&&!axios.isAxiosError(reason)?reason.message:fallback)}
const pickupSteps=['Verify customer and asset','','','','Inspect asset','Payment and release']
const paymentMethods=['Card','Cash','BankTransfer','MobileMoney']

export function RentalsPage({initialSearch='',initialQueue='PickupToday',embedded=false}:{initialSearch?:string;initialQueue?:QueueKey;embedded?:boolean}={}){
  const {user}=useAuth()
  const canSettle=Boolean(user&&canAccessPage(user.roles,'finance',user.pages))
  const [settlement,setSettlement]=useState<RentalRow|null>(null)
  const [pickupBlock,setPickupBlock]=useState<{row:RentalRow; reasons:string[]}|null>(null)
  const [checkingPickup,setCheckingPickup]=useState<string|null>(null)
  const pickupCheckInProgress=useRef(false)
  const [incidentHire,setIncidentHire]=useState<{row:RentalRow;incidentId?:string}|null>(null)
  const [closureNotice,setClosureNotice]=useState('')
  const requestedIncidentId=useRef(new URLSearchParams(window.location.search).get('incidentId'))
  const [inspectionBooking,setInspectionBooking]=useState<RentalRow|null>(null)
  const [focusedBookingId,setFocusedBookingId]=useState(()=>new URLSearchParams(window.location.search).get('bookingId')||undefined)
  const requestedWorkflow=useRef(new URLSearchParams(window.location.search).get('workflow'))
  const openedWorkflow=useRef(false)
  const [queue,setQueue]=useState<QueueKey>(initialQueue);const [data,setData]=useState<QueueResponse>({items:[],counts:{pickupToday:0,onHire:0,dueToday:0,overdue:0,returnInProgress:0,recentlyCompleted:0},page:1,pageSize:25,total:0});const [search,setSearch]=useState(initialSearch);const [page,setPage]=useState(1);const [loading,setLoading]=useState(true);const [error,setError]=useState('');const [pickup,setPickup]=useState<RentalRow|null>(null);const [canDeferHirePayment,setCanDeferHirePayment]=useState<boolean|undefined>(undefined);const [returning,setReturning]=useState<RentalRow|null>(null);const [agreement,setAgreement]=useState<RentalAgreementData|null>(null);const [agreementActions,setAgreementActions]=useState<RentalAgreementData|null>(null)
  const pendingQueueRequest=useRef<AbortController|null>(null)
  const searchTerm=search.trim()
  const lookupMode=Boolean(focusedBookingId||searchTerm)
  const load=useCallback(async()=>{
    pendingQueueRequest.current?.abort()
    const request=new AbortController()
    pendingQueueRequest.current=request
    setLoading(true)
    try{
      const response=await api.get<QueueResponse>('/rentals/work-queue',{
        params:{queue:searchTerm?'PickupToday':queue,search:searchTerm||undefined,page,pageSize:25,bookingId:focusedBookingId},
        signal:request.signal,
      })
      if(request.signal.aborted)return
      setData(response.data)
      setError('')
    }catch{
      if(!request.signal.aborted)setError('Hire operations could not be loaded. Check that the API is running.')
    }finally{
      if(!request.signal.aborted)setLoading(false)
    }
  },[queue,searchTerm,page,focusedBookingId])
  useEffect(()=>{
    const timer=window.setTimeout(()=>void load(),searchTerm?300:0)
    return()=>{clearTimeout(timer);pendingQueueRequest.current?.abort()}
  },[load,searchTerm])
  function leaveBookingLink(){
    if(!focusedBookingId)return
    setFocusedBookingId(undefined)
    const url=new URL(window.location.href)
    for(const key of ['bookingId','workflow','incidentId'])url.searchParams.delete(key)
    window.history.replaceState(window.history.state,'',url)
  }
  function openQueue(next:QueueKey){
    if(lookupMode||next!==queue||page!==1)setLoading(true)
    leaveBookingLink()
    setSearch('')
    setQueue(next)
    setPage(1)
  }
  function updateSearch(value:string){
    if(focusedBookingId||value.trim()!==searchTerm||page!==1)setLoading(true)
    leaveBookingLink()
    setSearch(value)
    setPage(1)
  }
  async function startPickup(row:RentalRow){
    if(pickupCheckInProgress.current)return
    pickupCheckInProgress.current=true
    setCheckingPickup(row.id)
    setPickupBlock(null)
    try{
      const {data:readiness}=await api.get<{canPickup:boolean;reasons:string[];canDeferHirePayment?:boolean;paidHire?:number;bondAmountHeld?:number;depositRequired?:number}>(`/rental-agreements/${row.id}/pickup-readiness`)
      if(!readiness.canPickup){
        setPickupBlock({row,reasons:readiness.reasons?.length?readiness.reasons:['Pickup readiness could not be confirmed. Refresh the hire and try again.']})
        return
      }
      const {data:prepared}=await api.get<RentalAgreementData>(`/rental-agreements/${row.id}`)
      setAgreement(prepared)
      setCanDeferHirePayment(readiness.canDeferHirePayment)
      setPickup({...row,amountPaid:readiness.paidHire??row.amountPaid,bondAmountHeld:readiness.bondAmountHeld??row.bondAmountHeld,
        depositRequired:readiness.depositRequired??row.depositRequired})
    }catch(reason){
      setPickupBlock({row,reasons:[err(reason,'Pickup could not be prepared. Try again.')]})
    }finally{
      pickupCheckInProgress.current=false
      setCheckingPickup(null)
    }
  }
  useEffect(()=>{
    if(!focusedBookingId||!requestedWorkflow.current||openedWorkflow.current||loading)return
    const row=data.items.find(item=>item.id===focusedBookingId)
  // eslint-disable-next-line react-hooks/set-state-in-effect -- Synchronize the selected rental and recovered inspection/draft state with the guided form.
    if(!row){setError('This hire is no longer available in your operating scope.');return}
    openedWorkflow.current=true
    if(requestedWorkflow.current==='return'&&row.status==='ConvertedToRental')setReturning(row)
    else if(requestedWorkflow.current==='incident'&&row.status==='ConvertedToRental')setIncidentHire({row,incidentId:requestedIncidentId.current??undefined})
    else if(requestedWorkflow.current==='pickup'&&row.status==='Confirmed')void startPickup(row)
    else setError('This hire has changed status. Choose its current action below.')
  },[data.items,focusedBookingId,loading])
  async function showAgreementActions(row:RentalRow){if(row.warning?.toLowerCase().includes('overdue')){setError('This rental has not been returned yet. Complete the return process before generating the final agreement.');return}try{setAgreementActions((await api.get<RentalAgreementData>(`/rental-agreements/${row.id}`)).data)}catch(reason){setError(err(reason,'The agreement could not be loaded.'))}}
  const nextAction=(row:RentalRow)=>row.status==='Confirmed'?<Button size="small" variant="contained" endIcon={<ArrowForwardOutlined/>} disabled={checkingPickup!==null} onClick={()=>void startPickup(row)}>{checkingPickup===row.id?'Checking pickup…':'Begin pickup'}</Button>:row.warning==='Return is overdue'||row.status==='ConvertedToRental'?<Button size="small" variant="contained" endIcon={<ArrowForwardOutlined/>} onClick={()=>setReturning(row)}>Begin return</Button>:row.hasAgreement?<Button size="small" variant="outlined" onClick={()=>void showAgreementActions(row)}>View agreement</Button>:<Button size="small" variant="outlined" disabled>Completed</Button>
  const action=(row:RentalRow)=><Stack spacing={1} alignItems="flex-start">{row.incidentClosedAt&&(canSettle||queue==='IncidentSettlements'&&!lookupMode)&&<Button size="small" variant="contained" onClick={()=>setSettlement(row)}>{canSettle?"Incident settlement":"Incident follow-up"}</Button>}{nextAction(row)}{row.status==='ConvertedToRental'&&<Button size="small" onClick={()=>setIncidentHire({row})}>Incident actions</Button>}<Button size="small" variant="outlined" onClick={()=>setInspectionBooking(row)}>View inspections</Button></Stack>
  return <Box sx={{p:embedded?0:{xs:2,sm:3,lg:4},maxWidth:1600,mx:'auto'}}>{embedded?<Stack direction={{xs:'column',sm:'row'}} justifyContent="space-between" gap={1} alignItems={{sm:'center'}} mb={2}><Typography variant="h6" fontWeight={800}>Booking workflow</Typography><TextField size="small" placeholder="Search rental, customer or asset" value={search} onChange={e=>updateSearch(e.target.value)} sx={{width:{xs:'100%',sm:360}}} InputProps={{startAdornment:<InputAdornment position="start"><SearchOutlined/></InputAdornment>}}/></Stack>:<PageHeader icon={<ReceiptLongOutlined/>} title="Hire operations" subtitle="Manage today’s pickups, active hires and controlled returns." actions={<TextField size="small" placeholder="Search rental, customer or asset" value={search} onChange={e=>updateSearch(e.target.value)} sx={{width:{xs:'100%',md:380}}} InputProps={{startAdornment:<InputAdornment position="start"><SearchOutlined/></InputAdornment>}}/>} />}{error&&!pickup&&!returning&&<FormError sx={{mb:2}}>{error}</FormError>}
    {closureNotice&&<Alert severity="success" onClose={()=>setClosureNotice('')} sx={{mb:2}}>{closureNotice}</Alert>}
    <WorkQueueSummary loading={loading} items={[
      {label:'Today’s handovers',count:data.counts.pickupToday,detail:'Prepare documents and release assets',onOpen:()=>openQueue('PickupToday')},
      {label:'Returns due today',count:data.counts.dueToday,detail:'Inspect condition and settle the bond',onOpen:()=>openQueue('DueToday')},
      {label:'Overdue returns',count:data.counts.overdue,detail:'Contact customers and review extensions',urgent:true,onOpen:()=>openQueue('Overdue')},
      {label:canSettle?'Incident settlements':'My incident follow-ups',count:data.incidentSettlements??0,detail:canSettle?'Review liability, invoices and bonds':'Review cases assigned to you',onOpen:()=>openQueue('IncidentSettlements')},
    ]}/>
    <Card variant="outlined" sx={{overflow:'hidden'}}><Tabs value={lookupMode?false:queue} onChange={(_,v:QueueKey)=>openQueue(v)} variant="scrollable" scrollButtons="auto" sx={{px:1,borderBottom:1,borderColor:'divider'}}>{([...queues,{key:"IncidentSettlements",label:canSettle?"Incident settlements":"My incident follow-ups"} as (typeof queues)[number]]).map(q=><Tab key={q.key} value={q.key} label={<Stack direction="row" gap={1} alignItems="center"><span>{q.label}</span>{q.count&&<Chip size="small" label={data.counts[q.count]} sx={{height:22,fontWeight:750}}/>}</Stack>}/>)}</Tabs>{lookupMode&&<Stack direction={{xs:"column",sm:"row"}} alignItems={{sm:"center"}} justifyContent="space-between" gap={1} sx={{px:2.5,py:1.5,borderBottom:1,borderColor:"divider"}}><Box><Typography fontWeight={800}>{focusedBookingId?"Selected booking":"Search results"}</Typography>{searchTerm&&!loading&&<Typography variant="body2" color="text.secondary">{data.total} {data.total===1?"rental matches":"rentals match"} “{searchTerm}”</Typography>}</Box><Button onClick={()=>openQueue(queue)}>{focusedBookingId?"Back to queue":"Clear search"}</Button></Stack>}{loading?<Box sx={{height:360,display:'grid',placeItems:'center'}}><CircularProgress/></Box>:<><TableContainer sx={{display:{xs:'none',md:'block'}}}><Table><TableHead><TableRow><TableCell>Rental</TableCell><TableCell>Customer</TableCell><TableCell>Asset</TableCell><TableCell>Pickup / return</TableCell><TableCell>Agreement</TableCell><TableCell>Rental revenue / payment</TableCell><TableCell>Readiness</TableCell><TableCell align="right">Next action</TableCell></TableRow></TableHead><TableBody>{data.items.map(row=><TableRow key={row.id} hover><TableCell><Typography fontWeight={800}>{row.bookingNumber}</Typography><Typography variant="caption" color="text.secondary">{row.branchName}</Typography></TableCell><TableCell><Typography fontWeight={650}>{row.customerName}</Typography>{row.customerPhone&&<Typography variant="caption" color="text.secondary"><PhoneOutlined sx={{fontSize:13,verticalAlign:'middle',mr:.4}}/>{row.customerPhone}</Typography>}</TableCell><TableCell><Typography variant="body2" fontWeight={650}>{row.assetName||'Not allocated'}</Typography><Typography variant="caption" color="text.secondary">{row.assetNumber}</Typography></TableCell><TableCell><Typography variant="body2">{date(row.startAt,true)}</Typography><Typography variant="caption" color="text.secondary">Return {date(row.endAt,true)}</Typography></TableCell><TableCell><Chip size="small" color={displayedAgreementStatus(row)==='Not signed'?'default':'success'} variant="outlined" label={displayedAgreementStatus(row)}/></TableCell><TableCell><Typography variant="body2" fontWeight={750}>Revenue (ex VAT): {money(row.rentalRevenue)}</Typography><Typography variant="caption" display="block">Rental charge (inc VAT): {money(row.rentalCharge)}</Typography><Typography variant="caption" display="block">Paid: {money(row.amountPaid)} · Due: {money(row.balanceDue)}</Typography><Typography variant="caption" display="block" fontWeight={700} mt={.5}>Refundable bond</Typography><Chip size="small" color={row.bondStatus==='Held'?'success':row.bondStatus==='Refunded'?'info':row.bondStatus==='PartiallyRefunded'?'warning':'default'} variant="outlined" label={row.bondStatus.replace(/([a-z])([A-Z])/g,'$1 $2')}/><Typography variant="caption" display="block" color="text.secondary">{money(row.bondAmountHeld)} collected · {money(row.depositRequired)} required</Typography><Typography variant="caption" display="block" color="text.secondary">{money(row.bondDeductionAmount)} applied · {money(row.bondRefundAmount)} refunded</Typography></TableCell><TableCell>{row.warning?<Chip icon={<WarningAmberOutlined/>} size="small" color="warning" label={row.warning}/>:<Chip size="small" color={row.preHireInspectionComplete?'success':'default'} variant="outlined" label={row.preHireInspectionComplete?'Inspection complete':'Inspection pending'}/>}</TableCell><TableCell align="right">{action(row)}</TableCell></TableRow>)}{data.items.length===0&&<TableRow><TableCell colSpan={8}><Empty search={Boolean(searchTerm)} focused={Boolean(focusedBookingId)}/></TableCell></TableRow>}</TableBody></Table></TableContainer><Stack spacing={1.5} sx={{display:{xs:'flex',md:'none'},p:2}}>{data.items.map(row=><Card variant="outlined" key={row.id}><CardContent><Stack direction="row" justifyContent="space-between"><Box><Typography fontWeight={800}>{row.bookingNumber}</Typography><Typography>{row.customerName}</Typography></Box><Chip size="small" label={row.status==='ConvertedToRental'?'On hire':row.status}/></Stack><Divider sx={{my:1.5}}/><Typography fontWeight={650}>{row.assetName} · {row.assetNumber}</Typography><Typography variant="body2" color="text.secondary">Return {date(row.endAt,true)}</Typography><Typography variant="body2" mt={1}>Revenue (ex VAT): {money(row.rentalRevenue)}</Typography><Typography variant="body2">Rental charge (inc VAT): {money(row.rentalCharge)} · Paid: {money(row.amountPaid)} · Due: {money(row.balanceDue)}</Typography><Typography variant="body2">Bond: {money(row.bondAmountHeld)} collected · {money(row.depositRequired)} required · {money(row.bondRefundAmount)} refunded</Typography>{row.warning&&<Alert severity="warning" sx={{mt:1.5,py:0}}>{row.warning}</Alert>}<Box mt={2}>{action(row)}</Box></CardContent></Card>)}{data.items.length===0&&<Empty search={Boolean(searchTerm)} focused={Boolean(focusedBookingId)}/>}</Stack>{data.total>data.pageSize&&<Stack alignItems="center" p={2}><Pagination count={Math.ceil(data.total/data.pageSize)} page={page} onChange={(_,v)=>setPage(v)}/></Stack>}</>}</Card>
    {incidentHire&&<EndHireAfterIncidentDialog bookingId={incidentHire.row.id} bookingNumber={incidentHire.row.bookingNumber} assetName={incidentHire.row.assetName} initialIncidentId={incidentHire.incidentId} onClose={()=>setIncidentHire(null)} onSaved={outcome=>{setIncidentHire(null);setClosureNotice(outcome==='EndHire'?'Hire ended and the incident settlement is pending.':outcome==='ReplaceAsset'?'Replacement handed over. The booking and bond remain active.':'Incident report saved. The rental stays active for normal return.');if(outcome==='EndHire'&&canSettle){openQueue('IncidentSettlements');setSettlement(incidentHire.row)}else void load()}}/>}
    {settlement&&<IncidentSettlementDialog bookingId={settlement.id} bookingNumber={settlement.bookingNumber} onClose={()=>setSettlement(null)} onUpdated={()=>void load()}/>}
    {inspectionBooking&&<RentalInspectionHistory bookingId={inspectionBooking.id} bookingNumber={inspectionBooking.bookingNumber} assetName={inspectionBooking.assetName} onClose={()=>setInspectionBooking(null)}/>}
    <Dialog open={Boolean(pickupBlock)} onClose={()=>setPickupBlock(null)} fullWidth maxWidth="sm"
      aria-labelledby="pickup-block-title" aria-describedby="pickup-block-reasons">
      <DialogTitle id="pickup-block-title">Pickup unavailable</DialogTitle>
      <DialogContent>
        <Typography fontWeight={700} sx={{mb:1.5}}>{pickupBlock?.row.bookingNumber}{pickupBlock?.row.assetNumber?' · '+pickupBlock.row.assetNumber:''}</Typography>
        <Alert severity="error" id="pickup-block-reasons">
          {pickupBlock?.reasons.map((reason,index)=><Typography key={index} variant="body2">{reason}</Typography>)}
        </Alert>
      </DialogContent>
      <DialogActions><Button autoFocus onClick={()=>setPickupBlock(null)}>Close</Button></DialogActions>
    </Dialog>
    <PickupWorkflow row={pickup} agreement={agreement} canDeferHirePayment={canDeferHirePayment} onClose={()=>{setPickup(null);setAgreement(null);setError('')}} onDone={async()=>{setPickup(null);setAgreement(null);await load()}}/>
    <Dialog open={Boolean(agreementActions)} onClose={()=>setAgreementActions(null)} fullWidth maxWidth="xs"><DialogTitle>Rental agreement</DialogTitle><DialogContent><Typography color="text.secondary">Choose how you would like to access {agreementActions?.agreementNumber ?? 'this agreement'}.</Typography></DialogContent><DialogActions sx={{p:3}}><Button onClick={()=>agreementActions&&void openRentalAgreementPdf(agreementActions)}>View in new tab</Button><Button variant="contained" onClick={()=>agreementActions&&void downloadRentalAgreementPdf(agreementActions)}>Download PDF</Button></DialogActions></Dialog>
    <ReturnWorkflowV2 row={returning} onClose={()=>{setReturning(null);setError('')}} onDone={async()=>{setReturning(null);await load()}}/>
  </Box>
}
function Empty({search,focused}:{search:boolean;focused:boolean}){
  return <Box textAlign="center" py={8}>
    <Typography fontWeight={750}>{focused?'Booking unavailable':search?'No matching rentals':'No work waiting in this queue'}</Typography>
    <Typography variant="body2" color="text.secondary">{focused?'The booking may have changed status or be outside your operating scope.':search?'Check the booking reference, customer details or asset number.':'CREMS moves rentals here automatically when their dates or workflow stage change.'}</Typography>
  </Box>
}

type PickupForm={hirePaidInFull:boolean;assetVerified:boolean;acceptExistingDamage:boolean;safeToOperate:boolean;existingDamageAcceptanceNote:string;rentalAmountCollected:string;payHireOnReturn:boolean;paymentDeferralReason:string;scannedAssetNumber:string;identificationVerified:boolean;driverLicenceVerified:boolean;paymentVerified:boolean;licenceNumber:string;licenceClass:string;licenceExpiry:string;meterReading:string;fuelLevelPercent:string;conditionNotes:string;damageNotes:string;signatureName:string;signatureDataUrl:string;accepted:boolean;agentApproved:boolean;amountCollected:string;paymentMethod:string;receiptNumber:string;evidenceDataUrls:string[];checklistItems:string[];failedChecklistItems:string[];accessoryNotes:string;damageZones:string[]}
type PickupDraft = PickupForm & { savedStep?:number }
const pickupInitial:PickupForm={hirePaidInFull:false,assetVerified:false,acceptExistingDamage:false,safeToOperate:false,existingDamageAcceptanceNote:'',rentalAmountCollected:'0',payHireOnReturn:false,paymentDeferralReason:'',scannedAssetNumber:'',identificationVerified:false,driverLicenceVerified:false,paymentVerified:false,licenceNumber:'',licenceClass:'',licenceExpiry:'',meterReading:'',fuelLevelPercent:'',conditionNotes:'',damageNotes:'',signatureName:'',signatureDataUrl:'',accepted:false,agentApproved:false,amountCollected:'',paymentMethod:'Card',receiptNumber:'',evidenceDataUrls:[],checklistItems:[],failedChecklistItems:[],accessoryNotes:'',damageZones:[]}
function categoryChecks(row:RentalRow|null){
  const code=row?.assetCategoryCode??''
  const tracksMeter=['RENTAL_VEHICLE','HEAVY_MACHINE','FORKLIFT','GENSET','GENERAL_EQUIPMENT'].includes(code)
  const requiresLicence=code==='RENTAL_VEHICLE'&&!row?.hasProfessionalPersonnel
  const guidance:Record<string,string>={RENTAL_VEHICLE:'Check body, glass, tyres, lights, cabin, safety equipment, fuel and odometer.',HEAVY_MACHINE:'Check engine, hydraulics, tracks or tyres, cab, alarms, attachments and leaks.',FORKLIFT:'Check forks, mast, chains, hydraulics, tyres, brakes, horn, alarms and capacity plate.',GENSET:'Check engine, battery, fuel, cooling, control panel, breakers, cables, earth stake and leaks.',SCAFFOLD:'Count frames, braces, platforms, pins, base plates and guardrails; check corrosion and distortion.',PORTABLE_TOILET:'Check structure, tank, door, locks, vents, cleanliness, consumables and placement.',BIG_BIN:'Check body, floor, doors, lifting points, load limits, waste type and placement.'}
  return {tracksMeter,tracksFuel:['RENTAL_VEHICLE','HEAVY_MACHINE','FORKLIFT','GENSET'].includes(code),requiresLicence,guidance:guidance[code]??'Check condition, controls, guards, accessories, safety labels and operation.'}
}
function checklistFor(row:RentalRow|null){if(row?.assetCategoryCode==='PROPERTY_UNIT')return ['Keys and access devices counted','Walls, floors, doors and windows checked','Plumbing, electricity and fixtures checked','Furnishings and inventory checked','Existing damage photographed'];const common=['Identity/serial number matches','Safety labels and guards present','Accessories and quantities complete','Existing damage photographed'];const specific:Record<string,string[]>= {RENTAL_VEHICLE:['Tyres, lights, glass and body checked','Cabin, tools and safety equipment checked','Odometer and fuel recorded'],HEAVY_MACHINE:['Engine, hydraulics and leaks checked','Tracks/tyres, alarms and attachments checked','Hour meter and fuel recorded'],FORKLIFT:['Forks, mast, chains and hydraulics checked','Brakes, horn, alarms and capacity plate checked'],GENSET:['Engine, battery, cooling and leaks checked','Panel, breakers, cables and earth stake checked'],SCAFFOLD:['Frames, braces, platforms and pins counted','Base plates, guardrails, corrosion and distortion checked'],PORTABLE_TOILET:['Tank, door, locks and vents checked','Cleanliness, consumables and placement checked'],BIG_BIN:['Body, floor, doors and lifting points checked','Load limit, waste type and placement checked']};return [...common,...(specific[row?.assetCategoryCode??'']??['Controls and operation checked','Cables, hoses and fittings checked'])]}
function PickupWorkflow({row,agreement,canDeferHirePayment,onClose,onDone}:{row:RentalRow|null;agreement:RentalAgreementData|null;canDeferHirePayment?:boolean;onClose:()=>void;onDone:()=>Promise<void>}){
  const [step,setStep]=useState(0);const [form,setForm]=useState<PickupForm>(pickupInitial);const [saving,setSaving]=useState(false);const [readingPhotos,setReadingPhotos]=useState(false);const [error,setError]=useState('');const [confirmClose,setConfirmClose]=useState(false)
  const [draftReady,setDraftReady]=useState(false),[draftRestored,setDraftRestored]=useState(false),[draftStatus,setDraftStatus]=useState('')
  const [savingDraft,setSavingDraft]=useState(false)
  const [draftFor,setDraftFor]=useState<string|null>(null)
  const [preparationSaved,setPreparationSaved]=useState(false)
  const [pickupAttempted,setPickupAttempted]=useState(false)
  // eslint-disable-next-line react-hooks/set-state-in-effect -- Synchronize the selected rental and recovered inspection/draft state with the guided form.
  useEffect(()=>{let active=true;setPreparationSaved(false);setDraftFor(null);setDraftReady(false);setDraftRestored(false);setDraftStatus('');setConfirmClose(false);setPickupAttempted(false);setError('');if(!row){setForm(pickupInitial);return}setStep(row.status==='Confirmed'?0:5);const booking=row;void readRentalDraft<PickupDraft>(`pickup:${booking.id}`).then(saved=>{if(!active)return;const matchingAsset=Boolean(saved?.assetVerified&&saved.scannedAssetNumber===booking.assetNumber);if(matchingAsset&&saved?.savedStep===4)setStep(4);if(matchingAsset&&(saved?.savedStep===5||saved?.savedStep===7))setStep(5);setForm(saved?{...pickupInitial,...saved,assetVerified:matchingAsset,hirePaidInFull:false,paymentVerified:false,signatureDataUrl:'',accepted:false,agentApproved:false,scannedAssetNumber:booking.assetNumber??''}:{...pickupInitial,scannedAssetNumber:booking.assetNumber??'',signatureName:booking.customerName,amountCollected:String(Math.max(0,booking.depositRequired-booking.bondAmountHeld)),paymentVerified:booking.depositRequired<=0||booking.bondAmountHeld>=booking.depositRequired});setDraftRestored(Boolean(saved));setDraftStatus(saved?'Recovered saved draft':'');setDraftFor(booking.id);setDraftReady(true)}).catch(()=>{if(active){setForm({...pickupInitial,scannedAssetNumber:booking.assetNumber??'',signatureName:booking.customerName});setDraftStatus('Draft recovery is unavailable in this browser.');setDraftFor(booking.id);setDraftReady(true)}});return()=>{active=false}},[row])
  // eslint-disable-next-line react-hooks/set-state-in-effect -- Synchronize the selected rental and recovered inspection/draft state with the guided form.
  useEffect(()=>{if(!row||!draftReady||draftFor!==row.id)return;setDraftStatus('Saving draft…');const timer=window.setTimeout(()=>{void saveRentalDraft(`pickup:${row.id}`,{...form,savedStep:step}).then(()=>setDraftStatus('Draft saved on this device')).catch(()=>setDraftStatus('Draft could not be saved on this device'))},500);return()=>window.clearTimeout(timer)},[row,form,step,draftReady,draftFor])
  const inspectionContext=useRentalInspection(row?.id,'PreHire');const legacyChecks=categoryChecks(row);const checks={...legacyChecks,requiresLicence:inspectionContext.requiresDrivingLicence??legacyChecks.requiresLicence,tracksMeter:inspectionContext.tracksMeter??legacyChecks.tracksMeter,tracksFuel:!['SCAFFOLD','PORTABLE_TOILET','BIG_BIN','PROPERTY_UNIT'].includes(row?.assetCategoryCode??'')&&(inspectionContext.tracksFuel??legacyChecks.tracksFuel)};const checklist=inspectionContext.items??checklistFor(row);const usageLabel=inspectionContext.meterUnit?`Usage reading (${inspectionContext.meterUnit})`:'Usage reading'
  const validPickupMeter=(!checks.tracksMeter||form.meterReading.trim()!==''&&Number.isFinite(Number(form.meterReading))&&Number(form.meterReading)>=Math.max(0,inspectionContext.assetCurrentMeterReading??0)&&Number(form.meterReading)<=10000000)
  const validPickupFuel=(!checks.tracksFuel||form.fuelLevelPercent.trim()!==''&&Number.isFinite(Number(form.fuelLevelPercent))&&Number(form.fuelLevelPercent)>=0&&Number(form.fuelLevelPercent)<=100&&Number.isInteger(Number(form.fuelLevelPercent)));const hireBalance = roundMoney(Math.max(0, (agreement?.pricing.total ?? row?.rentalCharge ?? 0) - (row?.amountPaid ?? 0)))
  const bondBalance = roundMoney(Math.max(0, (row?.depositRequired ?? 0) - (row?.bondAmountHeld ?? 0)))
  const hireInput = form.hirePaidInFull ? String(hireBalance) : form.payHireOnReturn ? form.rentalAmountCollected : '0'
  const hireCollected = previewAmount(hireInput)
  const bondCollected = form.paymentVerified ? bondBalance : 0
  const receivingPayment = hireCollected + bondCollected > 0
  const editPayment = (patch:Partial<PickupForm>) => setForm(current=>({...current,...patch,signatureDataUrl:'',accepted:false,agentApproved:false}))
  const hirePaymentError = paymentAmountError(hireInput,'Hire payment received',Math.min(hireBalance,1_000_000))
  const bondPaymentError = bondBalance>0&&!form.paymentVerified ? 'Confirm receipt of the remaining refundable bond.' : ''
  const hireDeferred = !hirePaymentError && hireCollected<hireBalance
  const deferralError = hireDeferred ? !form.payHireOnReturn ? 'Confirm full payment or select an approved pay-at-return arrangement.' :
    canDeferHirePayment!==true ? 'A branch manager or administrator must approve payment at return.' :
    !form.paymentDeferralReason.trim() ? 'Record the approved payment arrangement.' : '' : ''
  const pickupReceiptError = receivingPayment ? receiptValidationError(form.receiptNumber,form.paymentMethod!=='Cash',58) : ''
  const identityErrors = [
    !form.assetVerified ? 'Confirm the asset number on the physical label.' : '',
    !form.identificationVerified ? 'Confirm that the customer identification was sighted and matches this booking.' : '',
    checks.requiresLicence && !form.driverLicenceVerified ? 'Confirm that the driver licence was sighted and is valid for this vehicle.' : '',
    checks.requiresLicence && !/^[0-9]{1,80}$/.test(form.licenceNumber) ? 'Enter a numeric driver licence number.' : '',
    checks.requiresLicence && (!/^[1-8](,[1-8])*$/.test(form.licenceClass)||new Set(form.licenceClass.split(',')).size!==form.licenceClass.split(',').length) ? 'Select the applicable licence classes from 1 to 8.' : '',
    checks.requiresLicence && !(form.licenceExpiry > fijiInput(new Date().toISOString()).slice(0,10)) ? 'Enter a future driver licence expiry date.' : '',
  ].filter(Boolean)
  const pickupPaymentErrors = [!agreement ? 'The agreement and current charges must finish loading.' : '',
    hirePaymentError, bondPaymentError, deferralError, pickupReceiptError,
    !form.agentApproved ? 'Confirm staff approval to release the asset.' : '',
    !['Card','Cash','BankTransfer','MobileMoney'].includes(form.paymentMethod) ? 'Select a valid payment method.' : '',
    !form.signatureName.trim() ? 'Enter the customer signature name.' : '',
    !form.signatureDataUrl ? 'Draw the customer signature in the signature box below.' : '',
    !form.accepted ? 'Confirm that the customer accepts the agreement, charges and recorded condition.' : '',
  ].filter(Boolean)
  const ready=[identityErrors.length===0,form.scannedAssetNumber.trim().toUpperCase()===row?.assetNumber?.toUpperCase(),true,true,
    validPickupMeter&&validPickupFuel&&Boolean(form.conditionNotes.trim())&&(form.failedChecklistItems.length===0||Boolean(form.damageNotes.trim()))&&checklist.every(item=>form.checklistItems.includes(item)||form.failedChecklistItems.includes(item))&&(form.failedChecklistItems.length===0||form.acceptExistingDamage&&form.safeToOperate&&form.existingDamageAcceptanceNote.trim().length>=10)&&form.evidenceDataUrls.length>0,
    pickupPaymentErrors.length===0,true,form.agentApproved]

  const flowSteps=[0,4,5]
  const flowIndex=flowSteps.indexOf(step)
  useEffect(()=>{
    const saved=inspectionContext.preHire
    if(!saved||!draftReady||draftFor!==row?.id||draftRestored)return
    try{
      const evidence=JSON.parse(saved.evidenceJson||'{}')
  // eslint-disable-next-line react-hooks/set-state-in-effect -- Synchronize the selected rental and recovered inspection/draft state with the guided form.
      setForm(current=>({...current,conditionNotes:saved.conditionNotes||'',damageNotes:saved.damageNotes||'',
        meterReading:saved.meterReading==null?'':String(saved.meterReading),fuelLevelPercent:saved.fuelLevelPercent==null?'':String(saved.fuelLevelPercent),
        evidenceDataUrls:Array.isArray(evidence.photos)?evidence.photos:[],checklistItems:Array.isArray(evidence.checklist)?evidence.checklist:[],
        failedChecklistItems:Array.isArray(evidence.checklistResults)?evidence.checklistResults.filter((item:{label:string;passed:boolean|null})=>item.passed===false).map((item:{label:string})=>item.label):[],
        damageZones:Array.isArray(evidence.damageZones)?evidence.damageZones:[],accessoryNotes:typeof evidence.accessories==='string'?evidence.accessories:''}))
    }catch{setError('The saved inspection evidence could not be read. Please record the inspection again.')}
  },[inspectionContext.preHire,draftReady,draftFor,row?.id,draftRestored])
  async function saveProgress(){
    if(!row||!draftReady)return
    setSavingDraft(true);setError('');setDraftRestored(true)
    try{await saveRentalDraft(`pickup:${row.id}`,{...form,savedStep:step});setDraftStatus('Progress saved on this device. Close and reopen this pickup to resume.')}catch{setError('Progress could not be saved. Keep this checklist open and try again.')}finally{setSavingDraft(false)}
  }
  async function savePreparation(){
    if(!row)return
    setSaving(true);setError('')
    try{
      await api.post(`/rentals/${row.id}/pre-hire-inspection`,{
        scannedAssetNumber:form.scannedAssetNumber,meterReading:checks.tracksMeter?Number(form.meterReading):null,
        fuelLevelPercent:checks.tracksFuel?Number(form.fuelLevelPercent):null,conditionNotes:form.conditionNotes,damageNotes:form.damageNotes||null,
        evidenceDataUrls:form.evidenceDataUrls,checklistItems:form.checklistItems,checklistAllItems:checklist,checklistFailedItems:form.failedChecklistItems,damageZones:form.damageZones,accessoryNotes:form.accessoryNotes
      },{timeout:60000})
      setPreparationSaved(true)
      setDraftStatus('Pre-hire inspection saved to CREMS')
      return true
    }catch(reason){setError(err(reason,'The pre-hire inspection could not be saved.'));return false}finally{setSaving(false)}
  }
  async function complete(){if(!row||!flowSteps.every(index=>ready[index])||inspectionContext.loading||inspectionContext.error){setError('Complete all customer, inspection, payment and signature requirements before pickup.');return;}setSaving(true);setError('');try{await api.post(`/rental-agreements/${row.id}/pickup`,{acceptExistingDamage:form.acceptExistingDamage,safeToOperate:form.safeToOperate,existingDamageAcceptanceNote:form.existingDamageAcceptanceNote,scannedAssetNumber:form.scannedAssetNumber,identificationVerified:form.identificationVerified,driverLicenceVerified:form.driverLicenceVerified,paymentVerified:bondBalance===0||form.paymentVerified,licenceNumber:form.licenceNumber,licenceClass:form.licenceClass||null,licenceExpiry:form.licenceExpiry||null,meterReading:checks.tracksMeter?Number(form.meterReading):null,fuelLevelPercent:checks.tracksFuel?Number(form.fuelLevelPercent):null,conditionNotes:form.conditionNotes,damageNotes:form.damageNotes||null,customerAcceptedTerms:form.accepted,agentApproved:form.agentApproved,customerSignatureName:form.signatureName,customerSignatureDataUrl:form.signatureDataUrl,amountCollected:bondCollected,rentalAmountCollected:hireCollected,payHireOnReturn:hireDeferred,expectedHireBalance:hireBalance,expectedBondBalance:bondBalance,paymentDeferralReason:form.paymentDeferralReason,paymentType:'BondCollection',paymentMethod:form.paymentMethod,receiptNumber:receivingPayment?form.receiptNumber.trim():null,evidenceDataUrls:form.evidenceDataUrls,damageZones:form.damageZones,accessoryNotes:form.accessoryNotes,checklistItems:form.checklistItems,checklistAllItems:checklist,checklistFailedItems:form.failedChecklistItems},{timeout:60000});setDraftReady(false);await clearRentalDraft(`pickup:${row.id}`).catch(()=>{});await onDone()}catch(reason){setError(err(reason,'Pickup could not be completed.'))}finally{setSaving(false)}}
  const closeParent=onClose; const hasUnsavedChanges=Boolean(form.identificationVerified||form.conditionNotes||form.checklistItems.length||form.failedChecklistItems.length||form.evidenceDataUrls.length||form.signatureDataUrl||form.agentApproved)
  const pickupContent=useRef<HTMLDivElement>(null)
  const pickupInspectionErrors=[
 !validPickupMeter?'Enter a usage reading between the recorded reading and 10,000,000.':null,
 !validPickupFuel?'Enter a whole fuel percentage from 0 to 100.':null,
 !form.conditionNotes.trim()?'Describe the pre-hire condition.':null,
 !checklist.every(item=>form.checklistItems.includes(item)||form.failedChecklistItems.includes(item))?'Mark every checklist item as passed or issue found.':null,
 !form.evidenceDataUrls.length?'Attach at least one inspection photo.':null,
 form.failedChecklistItems.length>0&&!form.damageNotes.trim()?'Describe the existing damage below the acceptance section.':null,
 form.failedChecklistItems.length>0&&!form.acceptExistingDamage?'Record the customer’s acceptance of the existing damage.':null,
 form.failedChecklistItems.length>0&&!form.safeToOperate?'Confirm that the asset is safe and suitable, or choose a replacement.':null,
 form.failedChecklistItems.length>0&&form.existingDamageAcceptanceNote.trim().length<10?'Add an acceptance note of at least 10 characters describing the customer arrangement.':null
 ].filter((message): message is string=>message!==null)
  const pickupErrors=step===0?identityErrors:step===4?pickupInspectionErrors:[...identityErrors,...(!ready[4]?['Return to Inspect asset to complete the inspection.']:[]),...pickupPaymentErrors]
  const goToPickupStep=(next:number)=>{setError('');setPickupAttempted(false);setStep(next);pickupContent.current?.scrollTo({top:0})}
  const requestClose=()=>{if(!saving&&!readingPhotos){if(hasUnsavedChanges)setConfirmClose(true);else closeParent()}}
  const saveDraftAndClose=async()=>{if(row){try{await saveRentalDraft(`pickup:${row.id}`,{...form,savedStep:step})}catch{setError('Draft could not be saved. Keep editing or try again.');setConfirmClose(false);return}}setConfirmClose(false);closeParent()}
  return <Dialog open={Boolean(row)} onClose={requestClose} fullScreen><DialogTitle sx={{borderBottom:1,borderColor:'divider'}}><Typography variant="overline" color="text.secondary">Asset pickup</Typography><Typography variant="h5" fontWeight={850}>{row?.bookingNumber} · {row?.assetName}</Typography><Typography variant="caption" display="block" color="text.secondary">{draftStatus}</Typography></DialogTitle><DialogContent ref={pickupContent} sx={{p:{xs:2,md:3}}}><Stepper activeStep={flowIndex} alternativeLabel sx={{display:{xs:'none',md:'flex'},mb:4}}>{flowSteps.map((stepNumber,index)=><Step key={stepNumber} completed={index<flowIndex}><StepLabel>{pickupSteps[stepNumber]}</StepLabel></Step>)}</Stepper><Box sx={{maxWidth:900,mx:'auto'}}><Typography variant="overline" color="text.secondary">Step {flowIndex+1} of {flowSteps.length}</Typography><Typography variant="h5" fontWeight={800} mb={3}>{pickupSteps[step]}</Typography>{inspectionContext.error&&<FormError>{inspectionContext.error}</FormError>}
      {step===0&&<ReviewCard title="Customer and collection" rows={[['Customer',row?.customerName],['Phone',row?.customerPhone||'Not recorded'],['Branch',row?.branchName],['Expected pickup',date(row?.startAt??null,true)]]}/>}
{step===0&&<Stack spacing={2}>
                <FormControlLabel control={<Checkbox checked={form.assetVerified} onChange={e=>setForm({...form,assetVerified:e.target.checked})}/>} id="pickup-asset" label={'I checked the physical label matches '+(row?.assetNumber??'the allocated asset')}/>
        <FormControlLabel control={<Checkbox checked={form.identificationVerified} onChange={e=>setForm({...form,identificationVerified:e.target.checked})}/>} id="pickup-identification" label="Customer identification sighted and matches the booking"/>
        {checks.requiresLicence&&<>
          {inspectionContext.driverLicence?.number&&<Button variant="outlined" sx={{alignSelf:'flex-start'}} onClick={()=>{
            const licence=inspectionContext.driverLicence
            setForm(current=>({...current,licenceNumber:licence?.number??'',licenceClass:licence?.licenceClass??'',licenceExpiry:licence?.expiry?.slice(0,10)??'',driverLicenceVerified:false}))
          }}>Use licence details on file</Button>}
          <Box sx={{display:'grid',gridTemplateColumns:{xs:'minmax(0, 1fr)',md:'repeat(3, minmax(0, 1fr))'},gap:2,alignItems:'start'}}>
            <TextField required fullWidth id="pickup-licence-number" label="Licence number" value={form.licenceNumber} inputProps={{inputMode:'numeric',maxLength:80}} onChange={e=>setForm({...form,licenceNumber:e.target.value,driverLicenceVerified:false})}/>
            <LicenceClassSelect id="pickup-licence-class" value={form.licenceClass} onChange={licenceClass=>setForm({...form,licenceClass,driverLicenceVerified:false})}/>
            <TextField required fullWidth type="date" InputLabelProps={{shrink:true}} id="pickup-licence-expiry" label="Licence expiry" value={form.licenceExpiry} onChange={e=>setForm({...form,licenceExpiry:e.target.value,driverLicenceVerified:false})}/>
          </Box>
          <FormControlLabel control={<Checkbox checked={form.driverLicenceVerified} onChange={e=>setForm({...form,driverLicenceVerified:e.target.checked})}/>} id="pickup-licence-verified" label="Original licence sighted and valid for this vehicle"/>
        </>}
      </Stack>}
      {step===4&&<Stack spacing={2}>{(inspectionContext.preHire||preparationSaved)?<Alert severity="success">The saved inspection is shown below.</Alert>:null}{(checks.tracksMeter||checks.tracksFuel)?<Grid container spacing={2}>{checks.tracksMeter&&<Grid size={{xs:12,sm:6}}><TextField fullWidth required type="number" id="pickup-meter" label={usageLabel} value={form.meterReading} onChange={e=>setForm({...form,meterReading:e.target.value})}/></Grid>}{checks.tracksFuel&&<Grid size={{xs:12,sm:6}}><TextField fullWidth required type="number" inputProps={{min:0,max:100}} id="pickup-fuel" label="Fuel level (%)" value={form.fuelLevelPercent} onChange={e=>setForm({...form,fuelLevelPercent:e.target.value})}/></Grid>}</Grid>:<Alert severity="info">Meter and fuel readings do not apply to this asset.</Alert>}<Typography fontWeight={750}>{inspectionContext.templateName||"Required checklist"}</Typography><Box id="pickup-checklist" tabIndex={-1}><RentalChecklist items={checklist} passed={form.checklistItems} failed={form.failedChecklistItems} onChange={(passed,failed)=>setForm(current=>({...current,checklistItems:passed,failedChecklistItems:failed,conditionNotes:failed.length&&current.conditionNotes==='No condition issues observed.'?'':current.conditionNotes}))}/></Box>{form.failedChecklistItems.length>0&&<Stack spacing={2}><Alert severity="warning">Record the damage and agree the condition with the customer. Use a replacement for unsafe faults. Open maintenance and safety restrictions still prevent release.</Alert><FormControlLabel control={<Checkbox checked={form.acceptExistingDamage} onChange={e=>setForm({...form,acceptExistingDamage:e.target.checked})}/>} id="pickup-accept-damage" label="Customer agrees to continue with the recorded existing damage"/><FormControlLabel control={<Checkbox checked={form.safeToOperate} onChange={e=>setForm({...form,safeToOperate:e.target.checked})}/>} id="pickup-safety" label="I have checked the faults and confirm the asset is safe and suitable for this hire"/><TextField required multiline id="pickup-damage-note" label="Existing damage acceptance / customer arrangement" value={form.existingDamageAcceptanceNote} onChange={e=>setForm({...form,existingDamageAcceptanceNote:e.target.value})} error={pickupAttempted&&form.existingDamageAcceptanceNote.trim().length<10} helperText={pickupAttempted&&form.existingDamageAcceptanceNote.trim().length<10?undefined:`Enter at least 10 characters describing the accepted damage and arrangement (${form.existingDamageAcceptanceNote.trim().length}/10 minimum).`} inputProps={{maxLength:2000}}/><PickupReplacement bookingId={row?.id} onDone={async()=>{if(row)await clearRentalDraft(`pickup:${row.id}`);await onDone()}}/></Stack>}<FormControlLabel control={<Checkbox disabled={form.failedChecklistItems.length>0} checked={form.conditionNotes==="No condition issues observed."} onChange={event=>setForm({...form,conditionNotes:event.target.checked?"No condition issues observed.":""})}/>} label="No condition issues observed"/>{form.conditionNotes!=="No condition issues observed."&&<TextField required fullWidth multiline minRows={2} id="pickup-condition" label="Pre-hire condition and checklist result" value={form.conditionNotes} onChange={e=>setForm({...form,conditionNotes:e.target.value})} helperText={checks.guidance}/>}<TextField fullWidth multiline minRows={2} id="pickup-damage" label="Existing damage" value={form.damageNotes} onChange={e=>setForm({...form,damageNotes:e.target.value})} helperText="Record every existing mark, fault or missing item."/><TextField fullWidth multiline label="Accessories and quantities" helperText="Record issued / returned items, quantities and any missing items." value={form.accessoryNotes} onChange={e=>setForm({...form,accessoryNotes:e.target.value})}/><Box id="pickup-photos" tabIndex={-1}><InspectionPhotos onBusyChange={setReadingPhotos} photos={form.evidenceDataUrls} onChange={photos=>setForm(current=>({...current,evidenceDataUrls:photos}))}/></Box></Stack>}

      {step===5&&agreement&&<Stack spacing={2}>
                <ReviewCard title="Pickup account" rows={[
          ['Hire total',money(agreement.pricing.total)],['Hire already paid',money(row?.amountPaid??0)],
          ['Hire due at return',money(Math.max(0,hireBalance-hireCollected))],
          ['Refundable bond',bondBalance>0?money(bondBalance)+' to collect':(row?.bondAmountHeld??0)>0?money(row?.bondAmountHeld??0)+' held':'Not required'],
        ]}/>
        {hireBalance>0&&<>
          <FormControlLabel control={<Checkbox checked={form.hirePaidInFull} onChange={e=>editPayment({hirePaidInFull:e.target.checked,payHireOnReturn:false})}/>} id="pickup-payment-choice" label={'Full hire balance received · '+money(hireBalance)}/>
          {!form.hirePaidInFull&&canDeferHirePayment===true&&<FormControlLabel control={<Checkbox checked={form.payHireOnReturn} onChange={e=>editPayment({payHireOnReturn:e.target.checked})}/>} label="Approve remaining hire balance payable at return"/>}
          {!form.hirePaidInFull&&form.payHireOnReturn&&canDeferHirePayment===true&&<>
            <TextField type="number" id="pickup-payment-amount" label="Partial hire payment received (FJD)" inputProps={{min:0,max:hireBalance,step:0.01}} error={Boolean(hirePaymentError)} helperText={hirePaymentError?(pickupAttempted?undefined:hirePaymentError):'Enter 0 if no hire payment was received.'} value={form.rentalAmountCollected} onChange={e=>editPayment({rentalAmountCollected:e.target.value})}/>
            <TextField required id="pickup-payment-arrangement" label="Approved payment arrangement" multiline value={form.paymentDeferralReason} onChange={e=>editPayment({paymentDeferralReason:e.target.value})}/>
          </>}
        </>}
        {bondBalance>0&&<FormControlLabel control={<Checkbox checked={form.paymentVerified} onChange={e=>editPayment({paymentVerified:e.target.checked})}/>} id="pickup-bond" label={'Remaining refundable bond received · '+money(bondBalance)}/>}
        {receivingPayment&&<ReceiptFields id="pickup-receipt" method={form.paymentMethod} reference={form.receiptNumber} maxLength={58} error={pickupReceiptError} showErrorText={!pickupAttempted} onMethod={paymentMethod=>editPayment({paymentMethod,receiptNumber:''})} onReference={receiptNumber=>editPayment({receiptNumber})}/>}
        <Divider/>
        <Box component="details"><Typography component="summary" sx={{cursor:'pointer',fontWeight:700}}>Review agreement terms</Typography>
          <Stack spacing={1.5} sx={{mt:1.5}}>{(agreement.terms??[]).map((term,index)=><Box key={index}><Typography fontWeight={700}>{term.title}</Typography><Typography variant="body2" sx={{whiteSpace:'pre-line'}}>{term.content}</Typography></Box>)}</Stack>
        </Box>
        <TextField required id="pickup-signature-name" label="Customer signature name" value={form.signatureName} onChange={e=>editPayment({signatureName:e.target.value})}/>
        <Box id="pickup-signature" tabIndex={-1}><SignaturePad value={form.signatureDataUrl} onChange={signatureDataUrl=>setForm(current=>({...current,signatureDataUrl,accepted:false,agentApproved:false}))}/></Box>
        <FormControlLabel control={<Checkbox checked={form.accepted} onChange={e=>setForm({...form,accepted:e.target.checked})}/>} id="pickup-terms" label="Customer accepts the agreement, charges and recorded condition"/>
        <FormControlLabel control={<Checkbox checked={form.agentApproved} onChange={e=>setForm({...form,agentApproved:e.target.checked})}/>} id="pickup-approval" label="I witnessed the signature and authorize this asset release"/>
      </Stack>}
      <Dialog open={confirmClose} onClose={()=>setConfirmClose(false)} fullWidth maxWidth="sm"><DialogTitle>Discard pickup changes?</DialogTitle><DialogContent><Typography color="text.secondary">Save your progress to resume this pickup later, or discard the entered details.</Typography></DialogContent><DialogActions sx={{p:3,display:'grid',gridTemplateColumns:'1fr 1.35fr 1fr',gap:1.5}}><Button sx={{whiteSpace:'nowrap',borderRadius:2}} onClick={()=>setConfirmClose(false)}>Keep editing</Button><Button sx={{whiteSpace:'nowrap',borderRadius:2,boxShadow:'none'}} variant="contained" color="inherit" onClick={saveDraftAndClose}>Save changes & close</Button><Button sx={{whiteSpace:'nowrap',borderRadius:2,boxShadow:'0 4px 10px rgba(198,40,40,.24)'}} color="error" variant="contained" onClick={()=>{if(row){setDraftReady(false);void clearRentalDraft(`pickup:${row.id}`)}setConfirmClose(false);closeParent()}}>Discard changes</Button></DialogActions></Dialog>
    </Box></DialogContent>{error&&<FormError placement="footer">{error}</FormError>}<WorkflowValidation messages={pickupErrors} attempted={pickupAttempted} targets={{
      ...(hirePaymentError?{[hirePaymentError]:'pickup-payment-amount'}:{}),
      ...(pickupReceiptError?{[pickupReceiptError]:'pickup-receipt-reference'}:{}),
      'Select a valid payment method.':'pickup-receipt-method',
    }}/><DialogActions sx={{p:2,borderTop:1,borderColor:'divider',flexWrap:'wrap',gap:1}}><Button disabled={saving||savingDraft||readingPhotos} onClick={requestClose}>Close</Button><Button startIcon={<SaveOutlined/>} variant="outlined" disabled={saving||savingDraft||readingPhotos||!draftReady||draftFor!==row?.id} onClick={()=>void saveProgress()}>{savingDraft?'Saving progress…':'Save progress'}</Button><Box sx={{flex:1}}/>{row?.status==='Confirmed'&&<>
      <Button disabled={flowIndex===0||saving||readingPhotos} onClick={()=>{if(step===5)editPayment({});goToPickupStep(flowSteps[flowIndex-1])}}>Back</Button>
      <Button variant="contained" disabled={!draftReady||saving||readingPhotos||inspectionContext.loading||Boolean(inspectionContext.error)} onClick={()=>void (async()=>{
          setError('')
          setPickupAttempted(true)
          if(step===5){
            const previous=flowSteps.find(index=>index!==5&&!ready[index])
            if(previous!==undefined){goToPickupStep(previous);setPickupAttempted(true);return}
          }
          if(!ready[step])return
          if(step===5){await complete();return}
          if(step===4&&!await savePreparation())return
          goToPickupStep(flowSteps[flowIndex+1])
        })()}>{saving?'Saving…':step===5?'Release asset':step===4?'Save inspection and continue':'Continue'}</Button>
    </>}</DialogActions></Dialog>
}

function PickupReplacement({bookingId,onDone}:{bookingId?:string;onDone:()=>Promise<void>}){
 const [options,setOptions]=useState<{id:string;assetNumber:string;name:string}[]>([]),[loaded,setLoaded]=useState(false),[selected,setSelected]=useState(''),[reason,setReason]=useState(''),[busy,setBusy]=useState(false),[error,setError]=useState('')
 async function load(){setBusy(true);setError('');try{setOptions((await api.get(`/bookings/${bookingId}/replacement-assets`)).data);setLoaded(true)}catch(e){setError(err(e,'Could not load replacement assets.'))}finally{setBusy(false)}}
 async function replace(){setBusy(true);setError('');try{await api.post(`/bookings/${bookingId}/exception-resolution`,{replacementAssetId:selected,reason});await onDone()}catch(e){setError(err(e,'Could not transfer the booking.'))}finally{setBusy(false)}}
 return <Stack spacing={1.5}>{error&&<FormError>{error}</FormError>}<Button disabled={busy} onClick={()=>void load()}>Find an available replacement</Button>{loaded&&<>{options.length?<><TextField select label="Replacement asset" value={selected} onChange={e=>setSelected(e.target.value)}><MenuItem value="">Choose an asset</MenuItem>{options.map(x=><MenuItem key={x.id} value={x.id}>{x.assetNumber} · {x.name}</MenuItem>)}</TextField><TextField multiline label="Replacement reason and customer agreement" value={reason} onChange={e=>setReason(e.target.value)}/><Button variant="contained" disabled={busy||!selected||reason.trim().length<10} onClick={()=>void replace()}>Transfer booking to this asset</Button><Typography variant="caption">Same branch, service and category. Agreed charges are retained. Reopen pickup and inspect the replacement before release.</Typography></>:<Alert severity="info">No suitable replacement is currently available for this hire period. Contact the branch to arrange another asset or dates.</Alert>}</>}</Stack>
}

type ReturnForm={assetVerified:boolean;paidInFull:boolean;partialPayment:boolean;acceptOutstanding:boolean;applyBond:boolean;refundConfirmed:boolean;refundReference:string;lateOverride:boolean;lateConfirmed:boolean;lateReason:string;bondCoversReturnCharges:boolean;amountCollected:string;paymentMethod:string;receiptNumber:string;scannedAssetNumber:string;meter:string;fuel:string;condition:string;damage:string;late:string;excess:string;refuelling:string;cleaning:string;damageCharge:string;other:string;otherReason:string;bondDeduction:string;bondReason:string;bondRefundMethod:string;signature:string;signatureDataUrl:string;acknowledged:boolean;evidenceDataUrls:string[];checklistItems:string[];failedChecklistItems:string[];accessoryNotes:string;damageZones:string[]}
type ReturnDraft = ReturnForm & { savedStep?:number;quotedAt?:string }
const returnInitial:ReturnForm={assetVerified:false,paidInFull:false,partialPayment:false,acceptOutstanding:false,applyBond:false,refundConfirmed:false,refundReference:"",lateOverride:false,lateConfirmed:false,lateReason:"",bondCoversReturnCharges:true,amountCollected:'0',paymentMethod:'Card',receiptNumber:'',scannedAssetNumber:'',meter:'',fuel:'',condition:'',damage:'',late:'0',excess:'0',refuelling:'0',cleaning:'0',damageCharge:'0',other:'0',otherReason:'',bondDeduction:'0',bondReason:'',bondRefundMethod:'Card',signature:'',signatureDataUrl:'',acknowledged:false,evidenceDataUrls:[],checklistItems:[],failedChecklistItems:[],accessoryNotes:'',damageZones:[]}

function ReturnWorkflowV2({row,onClose,onDone}:{row:RentalRow|null;onClose:()=>void;onDone:()=>Promise<void>}){
  const [step,setStep]=useState(2),[form,setForm]=useState<ReturnForm>(returnInitial),[saving,setSaving]=useState(false),[readingPhotos,setReadingPhotos]=useState(false),[error,setError]=useState(''),[confirmClose,setConfirmClose]=useState(false)
  const [draftReady,setDraftReady]=useState(false),[draftStatus,setDraftStatus]=useState('')
  const [savingDraft,setSavingDraft]=useState(false)
  const [draftFor,setDraftFor]=useState<string|null>(null)
  const steps=['Inspect return','Settle account','Customer sign-off']
  const returnStages=[2,4,6]
  const flowIndex=returnStages.indexOf(step)
  const returnContent=useRef<HTMLDivElement>(null)
  const [returnAttempted,setReturnAttempted]=useState(false)
  const [returnTime,setReturnTime]=useState('')
  const [draftQuotedTime,setDraftQuotedTime]=useState<string>()
  const [chargePreview,setChargePreview]=useState<{returnedAt:string;lateFee:number;multiplier:number;hireSubtotal:number;taxableSubtotal:number;taxRate:number;paidHire:number;bondHeld?:number;bondAlreadySettled?:boolean;taxAmount?:number;netBeforeFloor?:number;appliedCredits?:number}|null>(null)
  const [chargeError,setChargeError]=useState(''),[chargeRetry,setChargeRetry]=useState(0)
  useEffect(()=>{
    let cancelled=false
  // eslint-disable-next-line react-hooks/set-state-in-effect -- Synchronize the selected rental and recovered inspection/draft state with the guided form.
    setDraftFor(null);setReturnAttempted(false);setStep(2);setForm(returnInitial);setDraftReady(false);setDraftStatus('');setConfirmClose(false);setError('');setChargePreview(null);setChargeError('');setReturnTime('');setDraftQuotedTime(undefined)
    if(row)void readRentalDraft<ReturnDraft>(`return:${row.id}`).then(saved=>{if(!cancelled){const matchingAsset=Boolean(saved?.assetVerified&&saved.scannedAssetNumber===row.assetNumber);if(matchingAsset&&(saved?.savedStep??0)>=4)setStep(4);setDraftQuotedTime(saved?.quotedAt);setForm({...returnInitial,...saved,assetVerified:matchingAsset,paidInFull:false,partialPayment:Boolean(saved?.partialPayment||previewAmount(saved?.amountCollected??'0')>0),acceptOutstanding:false,applyBond:Boolean(saved?.applyBond||previewAmount(saved?.bondDeduction??'0')>0),refundConfirmed:false,lateConfirmed:false,signatureDataUrl:'',acknowledged:false,signature:saved?.signature||row.customerName,scannedAssetNumber:row.assetNumber??''});setDraftStatus(saved?'Recovered saved draft':'');setDraftFor(row.id);setDraftReady(true)}}).catch(()=>{if(!cancelled){setForm({...returnInitial,signature:row.customerName,scannedAssetNumber:row.assetNumber??''});setDraftStatus('Draft recovery is unavailable in this browser.');setDraftFor(row.id);setDraftReady(true)}})
    return ()=>{cancelled=true}
  },[row])
  useEffect(()=>{
    let cancelled=false
  // eslint-disable-next-line react-hooks/set-state-in-effect -- Synchronize the selected rental and recovered inspection/draft state with the guided form.
    setChargeError('')
    if(row&&draftReady&&draftFor===row.id) void api.get<{returnedAt:string;lateFee:number;multiplier:number;hireSubtotal:number;taxableSubtotal:number;taxRate:number;paidHire:number;bondHeld?:number;bondAlreadySettled?:boolean;taxAmount?:number;netBeforeFloor?:number;appliedCredits?:number}>(`/rentals/${row.id}/return-charges`,{params:{returnedAt:returnTime?returnTime+':00+12:00':draftQuotedTime}}).then(({data})=>{
      if(!cancelled)setChargePreview(data)
    }).catch(reason=>{if(!cancelled)setChargeError(err(reason,'Automatic return charges could not be loaded.'))})
    return ()=>{cancelled=true}
  },[row,chargeRetry,draftReady,draftFor,returnTime,draftQuotedTime])
  // eslint-disable-next-line react-hooks/set-state-in-effect -- Synchronize the selected rental and recovered inspection/draft state with the guided form.
  useEffect(()=>{if(!row||!draftReady||draftFor!==row.id)return;setDraftStatus('Saving draft…');const timer=window.setTimeout(()=>{void saveRentalDraft(`return:${row.id}`,{...form,savedStep:step,quotedAt:chargePreview?.returnedAt??draftQuotedTime}).then(()=>setDraftStatus('Draft saved on this device')).catch(()=>setDraftStatus('Draft could not be saved on this device'))},500);return()=>window.clearTimeout(timer)},[row,form,step,draftReady,draftFor,chargePreview,draftQuotedTime])
  const inspectionContext=useRentalInspection(row?.id,'PostHire'),legacyChecks=categoryChecks(row),checks={...legacyChecks,tracksMeter:inspectionContext.tracksMeter??legacyChecks.tracksMeter,tracksFuel:!['SCAFFOLD','PORTABLE_TOILET','BIG_BIN','PROPERTY_UNIT'].includes(row?.assetCategoryCode??'')&&(inspectionContext.tracksFuel??legacyChecks.tracksFuel)},checklist=inspectionContext.items??checklistFor(row),usageLabel=inspectionContext.meterUnit?`Usage reading (${inspectionContext.meterUnit})`:'Usage reading'

  const update=(value:Partial<ReturnForm>)=>{
    const affectsBalance=[ 'late','lateOverride','excess','refuelling','cleaning','damageCharge','other','applyBond','bondDeduction','bondCoversReturnCharges' ]
      .some(field=>Object.prototype.hasOwnProperty.call(value,field))
    setForm(current=>({...current,...(affectsBalance?{paidInFull:false,acceptOutstanding:false}:{}),...value,signatureDataUrl:'',acknowledged:false}))
  }
  const late = chargePreview ? form.lateOverride ? previewAmount(form.late) : chargePreview.lateFee : 0
  const chargeFields = [['excess','Excess usage'],['refuelling','Refuelling'],['cleaning','Cleaning'],['damageCharge','Damage'],['other','Other']] as const
  const relevantCharges = chargeFields.filter(([key])=>(key!=='refuelling'||checks.tracksFuel)&&(key!=='excess'||checks.tracksMeter))
  const additionalCharges = roundMoney(relevantCharges.reduce((sum,[key])=>sum+previewAmount(form[key]),0))
  const charges = roundMoney(late+additionalCharges)
  const returnTax = chargePreview?.taxAmount??roundMoney((chargePreview?.taxableSubtotal??0)*(chargePreview?.taxRate??0)/100)
  const paidHire = chargePreview?.paidHire??0
  const appliedCredits = chargePreview?.appliedCredits??0
  const invoiceTotal = roundMoney(Math.max(0,Math.max(0,(chargePreview?.netBeforeFloor??chargePreview?.hireSubtotal??0)+charges)+returnTax-appliedCredits))
  const balanceBeforeBond = roundMoney(Math.max(0,invoiceTotal-paidHire))
  const bondHeld = chargePreview?.bondHeld??row?.bondAmountHeld??0
  const retainedBond = form.applyBond ? previewAmount(form.bondDeduction) : 0
  const maximumBondCredit = Math.min(bondHeld,balanceBeforeBond,1_000_000)
  const bondLimit = Math.min(maximumBondCredit,form.bondCoversReturnCharges?charges:maximumBondCredit)
  const bondRefund = roundMoney(Math.max(0,bondHeld-retainedBond))
  const returnBalance = roundMoney(Math.max(0,balanceBeforeBond-retainedBond))
  const paymentInput = form.paidInFull ? String(returnBalance) : form.partialPayment&&returnBalance>0 ? form.amountCollected : '0'
  const amountCollected = previewAmount(paymentInput)
  const remainingDue = roundMoney(Math.max(0,returnBalance-amountCollected))
  const returnPaymentError = paymentAmountError(paymentInput,'Payment received',Math.min(returnBalance,1_000_000))
  const returnReceiptError = amountCollected>0 ? receiptValidationError(form.receiptNumber,form.paymentMethod!=='Cash',60) : ''
  const retainedBondError = form.applyBond ? paymentAmountError(form.bondDeduction,'Bond applied',bondLimit) || (retainedBond===0?'Enter an amount to apply, or choose a full refund.':'') : ''
  const refundReferenceError = bondRefund>0 ? receiptValidationError(form.refundReference,form.bondRefundMethod!=='Cash',60).replace('receipt or transaction reference for the money received','refund reference').replace('receipt or transaction reference to','refund reference to') : ''
  const priorMeter = Math.max(0,row?.preHireMeterReading??0,inspectionContext.assetCurrentMeterReading??0)
  const validReturnMeter = (!checks.tracksMeter||form.meter.trim()!==''&&Number.isFinite(Number(form.meter))&&Number(form.meter)>=priorMeter&&Number(form.meter)<=10_000_000)
  const validReturnFuel = (!checks.tracksFuel||form.fuel.trim()!==''&&Number.isInteger(Number(form.fuel))&&Number(form.fuel)>=0&&Number(form.fuel)<=100)
  const returnStageErrors:Record<number,string[]> = {
    2:[
      !form.assetVerified||!row?.assetNumber?'Confirm the physical asset label matches this hire.':'',
      !validReturnMeter ? 'Enter a return usage reading from '+priorMeter+' to 10,000,000.' : '',
      !validReturnFuel ? 'Enter the returned fuel or battery level from 0 to 100.' : '',
      !form.condition.trim() ? 'Record the return condition.' : '',
      !checklist.length||!checklist.every(item=>form.checklistItems.includes(item)||form.failedChecklistItems.includes(item))?'Mark each required inspection item.':'',
      form.failedChecklistItems.length>0&&!form.damage.trim()?'Describe the faults found.':'',
      !form.evidenceDataUrls.length?'Attach at least one photo of the returned asset.':'',
    ].filter(Boolean),
    4:[
      ...relevantCharges.map(([key,label])=>paymentAmountError(form[key],label)),
      form.lateOverride ? paymentAmountError(form.late,'Late fee') : '',
      form.lateOverride&&!form.lateReason.trim()?'Record why the late fee was adjusted or waived.':'',
      !form.lateOverride&&(chargePreview?.lateFee??0)>0&&!form.lateConfirmed?'Confirm the calculated late fee or adjust it.':'',
      previewAmount(form.other)>0&&!form.otherReason.trim()?'Enter the reason for the other return charge.':'',
      retainedBondError, retainedBond>0&&!form.bondReason.trim()?'Record the reason for applying the bond.':'',
      bondRefund>0&&!form.refundConfirmed?'Confirm the bond refund has been paid.':'',
      bondRefund>0&&form.refundConfirmed?refundReferenceError:'',
      bondRefund>0&&!paymentMethods.includes(form.bondRefundMethod)?'Select the refund method.':'',
      bondRefund>0&&amountCollected>0&&form.refundReference.trim()&&form.refundReference.trim().toLowerCase()===form.receiptNumber.trim().toLowerCase()?'Use a separate reference for the refund.':'',
      returnPaymentError,returnReceiptError,
      amountCollected>0&&!paymentMethods.includes(form.paymentMethod)?'Select the receipt payment method.':'',
      form.partialPayment&&returnBalance>0&&amountCollected===0?'Enter the partial payment received or clear that option.':'',
      remainingDue>0&&!form.acceptOutstanding?(form.partialPayment&&amountCollected>0?'Confirm the amount to leave outstanding.':form.partialPayment?'':'Choose how to settle the remaining balance.'):'',
    ].filter(Boolean),
    6:[!form.signature.trim()?'Enter the name of the person acknowledging the return.':'',!form.signatureDataUrl?'Capture their signature.':'',!form.acknowledged?'Confirm customer acknowledgement.':''].filter(Boolean),
  }
  const ready=returnStages.every(stage=>returnStageErrors[stage].length===0)
  // eslint-disable-next-line react-hooks/set-state-in-effect -- Synchronize the selected rental and recovered inspection/draft state with the guided form.
  useEffect(()=>{if(draftReady&&!inspectionContext.loading)setForm(current=>({...current,...(!checks.tracksFuel?{refuelling:'0',fuel:''}:{}),...(!checks.tracksMeter?{excess:'0',meter:''}:{})}))},[draftReady,inspectionContext.loading,checks.tracksFuel,checks.tracksMeter])
  async function saveProgress(){
    if(!row||!draftReady)return
    setSavingDraft(true);setError('');
    try{await saveRentalDraft(`return:${row.id}`,{...form,savedStep:step,quotedAt:chargePreview?.returnedAt??draftQuotedTime});setDraftStatus('Progress saved on this device. Close and reopen this return to resume.')}catch{setError('Progress could not be saved. Keep this checklist open and try again.')}finally{setSavingDraft(false)}
  }
  const dirty=Boolean(form.condition||form.meter||form.fuel||form.checklistItems.length||form.failedChecklistItems.length||form.evidenceDataUrls.length||form.signatureDataUrl)
  async function complete(){if(!row||!chargePreview||inspectionContext.loading||inspectionContext.error||!ready){setError('Complete every required return detail and correct the payment amount before finishing.');return;}setError('');setSaving(true);try{await api.post(`/rentals/${row.id}/return`,{scannedAssetNumber:form.scannedAssetNumber,identificationVerified:false,driverLicenceVerified:false,meterReading:checks.tracksMeter?Number(form.meter):null,fuelLevelPercent:checks.tracksFuel?Number(form.fuel):null,conditionNotes:form.condition,damageNotes:form.damage||null,signatureName:form.signature,signatureDataUrl:form.signatureDataUrl,additionalCharges:Number(form.other),additionalChargesDescription:form.otherReason||null,returnedAt:chargePreview.returnedAt,lateFee:form.lateOverride?late:null,lateFeeAdjustmentReason:form.lateOverride?form.lateReason.trim():null,confirmLateFee:form.lateConfirmed,expectedInvoiceTotal:invoiceTotal,expectedOutstanding:returnBalance,expectedBondHeld:bondHeld,acceptOutstanding:form.acceptOutstanding,excessUsageCharge:Number(form.excess),refuellingCharge:Number(form.refuelling),cleaningCharge:Number(form.cleaning),damageCharge:Number(form.damageCharge),bondCoversReturnCharges:form.bondCoversReturnCharges,bondDeductionAmount:retainedBond,bondDeductionReason:form.bondReason||null,amountCollected,paymentMethod:form.paymentMethod,receiptNumber:amountCollected>0?form.receiptNumber.trim():null,bondRefundMethod:form.bondRefundMethod,bondRefundReference:bondRefund>0?form.refundReference.trim():null,evidenceDataUrls:form.evidenceDataUrls,damageZones:form.damageZones,accessoryNotes:form.accessoryNotes,checklistItems:form.checklistItems,checklistAllItems:checklist,checklistFailedItems:form.failedChecklistItems},{timeout:60000});setDraftReady(false);await clearRentalDraft(`return:${row.id}`).catch(()=>{});await onDone()}catch(reason){setError(err(reason,'The return could not be completed.'))}finally{setSaving(false)}}
  const goToStep=(next:number)=>{if(step===6)update({});setError('');setReturnAttempted(false);setStep(next);returnContent.current?.scrollTo({top:0})}
  return <Dialog open={Boolean(row)} onClose={()=>{if(!saving&&!readingPhotos){if(dirty)setConfirmClose(true);else onClose()}}} fullScreen><DialogTitle sx={{borderBottom:1,borderColor:'divider'}}><Typography variant="overline" color="text.secondary">Asset return</Typography><Typography variant="h5" fontWeight={850}>{row?.bookingNumber} · {row?.assetName}</Typography><Typography variant="caption" display="block" color="text.secondary">{draftStatus}</Typography></DialogTitle><DialogContent ref={returnContent} sx={{p:{xs:2,md:3}}}><Stepper activeStep={flowIndex} alternativeLabel sx={{display:{xs:'none',md:'flex'},mb:4}}>{steps.map((label,index)=><Step key={label} completed={index<flowIndex}><StepLabel>{label}</StepLabel></Step>)}</Stepper><Box sx={{maxWidth:900,mx:'auto'}}><Typography variant="overline" color="text.secondary">Step {flowIndex+1} of {steps.length}</Typography><Typography variant="h5" fontWeight={800} mb={3}>{steps[flowIndex]}</Typography>{chargeError?<FormError action={<Button onClick={()=>setChargeRetry(v=>v+1)}>Retry</Button>}>{chargeError}</FormError>:!chargePreview&&step===4&&<Alert severity="info">Calculating return charges…</Alert>}<Typography variant="body2" color="text.secondary" sx={{mb:2}}>{row?.customerName} · {row?.assetNumber} · Due {date(row?.endAt??null,true)}</Typography>{step===2&&<Stack spacing={2}><FormControlLabel control={<Checkbox checked={form.assetVerified} onChange={event=>update({assetVerified:event.target.checked})}/>} id="return-asset" label={"I checked the physical label matches "+(row?.assetNumber??"the allocated asset")}/>{checks.tracksMeter&&<Card variant="outlined" sx={{bgcolor:'action.hover'}}><CardContent sx={{py:1.5}}><Typography variant="subtitle2" fontWeight={800} gutterBottom>Handover readings</Typography><Grid container spacing={2}><Grid size={{xs:12,sm:6}}><Typography variant="body2" color="text.secondary">{usageLabel} at handover</Typography><Typography fontWeight={700}>{row?.preHireMeterReading==null?'Not recorded':row.preHireMeterReading}</Typography></Grid>{checks.tracksFuel&&<Grid size={{xs:12,sm:6}}><Typography variant="body2" color="text.secondary">Fuel / battery level at handover</Typography><Typography fontWeight={700}>{row?.preHireFuelLevelPercent==null?'Not recorded':`${row.preHireFuelLevelPercent}%`}</Typography></Grid>}</Grid></CardContent></Card>}{(checks.tracksMeter||checks.tracksFuel)&&<Grid container spacing={2}>{checks.tracksMeter&&<Grid size={{xs:12,sm:6}}><TextField fullWidth required type="number" id="return-meter" label={`Return ${usageLabel.toLowerCase()}`} value={form.meter} error={form.meter!==''&&Number(form.meter)<priorMeter} helperText={form.meter!==''&&Number(form.meter)<priorMeter?'Minimum recorded reading: '+priorMeter:undefined} onChange={e=>update({meter:e.target.value})}/></Grid>}{checks.tracksFuel&&<Grid size={{xs:12,sm:6}}><TextField fullWidth required type="number" inputProps={{min:0,max:100}} id="return-fuel" label="Returned fuel / battery level (%)" value={form.fuel} error={form.fuel!==''&&(Number(form.fuel)<0||Number(form.fuel)>100)} onChange={e=>update({fuel:e.target.value})}/></Grid>}</Grid>}<Typography fontWeight={750}>{inspectionContext.templateName||'Required checklist'}</Typography><Box id="return-checklist" tabIndex={-1}><RentalChecklist items={checklist} passed={form.checklistItems} failed={form.failedChecklistItems} onChange={(passed,failed)=>update({checklistItems:passed,failedChecklistItems:failed,...(failed.length&&form.condition==="No condition issues observed."?{condition:""}:{})})}/></Box><FormControlLabel control={<Checkbox disabled={form.failedChecklistItems.length>0} checked={form.condition==="No condition issues observed."} onChange={event=>update({condition:event.target.checked?"No condition issues observed.":""})}/>} label="No condition issues observed"/>{form.condition!=="No condition issues observed."&&<TextField fullWidth required multiline minRows={2} id="return-condition" label="Post-hire inspection result" value={form.condition} onChange={e=>update({condition:e.target.value})} helperText={checks.guidance}/>}<TextField fullWidth multiline minRows={2} id="return-damage" label="New damage or fault" value={form.damage} onChange={e=>update({damage:e.target.value})}/><TextField fullWidth multiline label="Accessories and quantities" helperText="Record issued / returned items, quantities and any missing items." value={form.accessoryNotes} onChange={e=>update({accessoryNotes:e.target.value})}/><Box id="return-photos" tabIndex={-1}><InspectionPhotos onBusyChange={setReadingPhotos} photos={form.evidenceDataUrls} onChange={evidenceDataUrls=>update({evidenceDataUrls})}/></Box>{inspectionContext.preHire?.evidenceJson&&<Box component="details"><Typography component="summary" sx={{cursor:"pointer",fontWeight:700}}>Compare pickup and return photos</Typography><Typography variant="body2">{inspectionContext.preHire.conditionNotes}</Typography><EvidenceComparison evidenceJson={inspectionContext.preHire.evidenceJson} photos={form.evidenceDataUrls}/></Box>}</Stack>}
        {step===4&&<TextField fullWidth sx={{mb:2}} type="datetime-local" label="Actual return time (Fiji time)" value={returnTime||(chargePreview?fijiInput(chargePreview.returnedAt):'')}
          onChange={event=>{setChargePreview(null);setDraftQuotedTime(undefined);setReturnTime(event.target.value);update({lateOverride:false,lateConfirmed:false,paidInFull:false,acceptOutstanding:false})}}
          InputLabelProps={{shrink:true}}/>}
        {step===4&&chargePreview&&<Stack spacing={2.5}>
          <Box>
            <Typography variant="h6" fontWeight={800}>Return charges</Typography>
            <Typography variant="body2" color="text.secondary">Return recorded for {date(chargePreview.returnedAt,true)} (Fiji time).</Typography>
          </Box>
          {chargePreview.lateFee>0&&<Alert severity="warning">Calculated late fee {money(chargePreview.lateFee)}. Check the actual return time before applying it.</Alert>}
          {(chargePreview.lateFee>0||form.lateOverride)&&<FormControlLabel control={<Checkbox checked={form.lateOverride} onChange={e=>update({lateOverride:e.target.checked,lateConfirmed:false,late:String(chargePreview.lateFee)})}/>} label="Adjust or waive the calculated late fee"/>}
          {form.lateOverride&&<Stack spacing={1.5}>
            <TextField type="number" id="return-late-amount" label="Late return (FJD)" value={form.late} inputProps={{min:0,step:0.01}} onChange={e=>update({late:e.target.value})}/>
            <TextField required id="return-late-reason" label="Reason for late fee adjustment" value={form.lateReason} inputProps={{maxLength:500}} onChange={e=>update({lateReason:e.target.value})}/>
          </Stack>}
          {!form.lateOverride&&chargePreview.lateFee>0&&<FormControlLabel control={<Checkbox checked={form.lateConfirmed} onChange={e=>update({lateConfirmed:e.target.checked})}/>} id="return-late-confirmed" label={'Apply calculated late fee · '+money(chargePreview.lateFee)}/>}
          <Accordion defaultExpanded={additionalCharges>0} disableGutters>
            <AccordionSummary expandIcon={<ExpandMoreOutlined/>}><Typography fontWeight={700}>Other return charges · {money(additionalCharges)}</Typography></AccordionSummary>
            <AccordionDetails><Grid container spacing={1.5}>
              {chargeFields.filter(([key])=>(key!=='refuelling'||checks.tracksFuel)&&(key!=='excess'||checks.tracksMeter)).map(([key,label])=><Grid key={key} size={{xs:12,sm:6}}>
                <TextField fullWidth type="number" inputProps={{min:0,max:1_000_000,step:0.01}} error={Boolean(paymentAmountError(form[key],label))}
                  helperText={returnAttempted?undefined:paymentAmountError(form[key],label)} id={'return-charge-'+key} label={label+' (FJD)'} value={form[key]} onChange={e=>update({[key]:e.target.value})}/>
              </Grid>)}
              {previewAmount(form.other)>0&&<Grid size={12}><TextField fullWidth required id="return-other-reason" label="Other charge reason" value={form.otherReason} onChange={e=>update({otherReason:e.target.value})}/></Grid>}
            </Grid></AccordionDetails>
          </Accordion>
          <Divider/>
          <Card variant="outlined"><CardContent><Stack spacing={2}>
            <Box>
              <Typography variant="h6" fontWeight={800}>Settle the refundable bond</Typography>
              <Typography color="text.secondary">{bondHeld>0?money(bondHeld)+' held':chargePreview.bondAlreadySettled?'Bond already settled.':row?.depositRequired?'No bond payment is recorded for this hire.':'No bond held.'}</Typography>
            </Box>
            {bondHeld>0&&<>
              {(maximumBondCredit>0||form.applyBond)&&<RadioGroup aria-label="How to settle the bond" value={form.applyBond?'apply':'refund'} onChange={(_,choice)=>{
                const apply=choice==='apply'
                update({applyBond:apply,bondCoversReturnCharges:charges>0,bondDeduction:apply?String(Math.min(maximumBondCredit,charges>0?charges:maximumBondCredit)):'0',refundConfirmed:false,refundReference:''})
              }}>
                <FormControlLabel value="refund" control={<Radio/>} label="Refund the full bond and collect charges separately"/>
                <FormControlLabel value="apply" control={<Radio/>} disabled={maximumBondCredit===0} label="Use the bond toward charges"/>
              </RadioGroup>}
              {form.applyBond&&<>
                <TextField select label="Apply bond to" value={form.bondCoversReturnCharges?'ReturnCharges':'InvoiceBalance'} onChange={e=>{
                  const returnCharges=e.target.value==='ReturnCharges'
                  update({bondCoversReturnCharges:returnCharges,bondDeduction:String(Math.min(maximumBondCredit,returnCharges?charges:maximumBondCredit)),refundConfirmed:false,refundReference:''})
                }}>
                  <MenuItem value="ReturnCharges" disabled={charges===0}>Return charges only</MenuItem>
                  <MenuItem value="InvoiceBalance">Total outstanding invoice balance</MenuItem>
                </TextField>
                <TextField type="number" id="return-bond-amount" label="Bond applied (FJD)" value={form.bondDeduction} inputProps={{min:0.01,max:bondLimit,step:0.01}}
                  error={Boolean(retainedBondError)} helperText={retainedBondError?(returnAttempted?undefined:retainedBondError):'Up to '+money(bondLimit)}
                  onChange={e=>update({bondDeduction:e.target.value,refundConfirmed:false,refundReference:''})}/>
                {retainedBond>0&&<TextField required id="return-bond-reason" label="Reason for bond deduction" value={form.bondReason} onChange={e=>update({bondReason:e.target.value})}/>}
              </>}
              {bondRefund>0&&!retainedBondError&&<>
                <FormControlLabel control={<Checkbox checked={form.refundConfirmed} onChange={e=>update({refundConfirmed:e.target.checked})}/>} id="return-refund-confirmed" label={'Refund already paid · '+money(bondRefund)}/>
                {form.refundConfirmed&&<ReceiptFields id="return-refund" method={form.bondRefundMethod} reference={form.refundReference} maxLength={60} error={refundReferenceError} showErrorText={!returnAttempted}
                  methodLabel="Refund method" referenceLabel="Refund transaction reference" cashReferenceLabel="Existing cash refund receipt"
                  onMethod={bondRefundMethod=>update({bondRefundMethod,refundReference:''})} onReference={refundReference=>update({refundReference})}/>}
              </>}
            </>}
            <Divider/>
            <Box>
              <Typography variant="h6" fontWeight={800}>Collect the remaining balance</Typography>
              <Typography color="text.secondary">Invoice {money(invoiceTotal)} · Previously paid or applied {money(paidHire)}{retainedBond>0?' · Bond applied '+money(retainedBond):''}</Typography>
              <Typography fontWeight={800} sx={{mt:1}}>{money(returnBalance)} due after bond</Typography>
            </Box>
            {paidHire>invoiceTotal&&<Alert severity="warning">Recorded payments exceed the final invoice by {money(paidHire-invoiceTotal)}. Refer the overpayment for review.</Alert>}
            {returnBalance>0&&<>
              <RadioGroup id="return-payment-choice" aria-label="How to settle the remaining balance"
                value={form.paidInFull?'full':form.partialPayment?'partial':form.acceptOutstanding?'outstanding':''}
                onChange={(_,choice)=>update({paidInFull:choice==='full',partialPayment:choice==='partial',acceptOutstanding:choice==='outstanding'})}>
                <FormControlLabel value="full" control={<Radio/>} label={'Full outstanding balance received · '+money(returnBalance)}/>
                <FormControlLabel value="partial" control={<Radio/>} label="Record a partial payment"/>
                <FormControlLabel value="outstanding" control={<Radio/>} label={'Leave '+money(returnBalance)+' outstanding on the invoice'}/>
              </RadioGroup>
              {form.partialPayment&&<TextField type="number" id="return-payment-amount" label="Partial payment received (FJD)" value={form.amountCollected}
                inputProps={{min:0.01,max:returnBalance,step:0.01}} error={Boolean(returnPaymentError)} helperText={returnAttempted?undefined:returnPaymentError}
                onChange={e=>update({amountCollected:e.target.value,acceptOutstanding:false})}/>}
              {amountCollected>0&&<ReceiptFields id="return-receipt" method={form.paymentMethod} reference={form.receiptNumber} maxLength={60} error={returnReceiptError} showErrorText={!returnAttempted}
                onMethod={paymentMethod=>update({paymentMethod,receiptNumber:''})} onReference={receiptNumber=>update({receiptNumber})}/>}
              {form.partialPayment&&amountCollected>0&&remainingDue>0&&<FormControlLabel control={<Checkbox checked={form.acceptOutstanding} onChange={e=>update({acceptOutstanding:e.target.checked})}/>}
                id="return-outstanding" label={'Leave '+money(remainingDue)+' outstanding after this payment'}/>}
            </>}
          </Stack></CardContent></Card>
        </Stack>}
        {step===6&&<Stack spacing={2}>
          <ReviewCard title="Customer settlement" rows={[
            ['Invoice total',money(invoiceTotal)],['Payment received today',money(amountCollected)],
            ['Remaining invoice balance',money(remainingDue)],...(bondHeld>0?[['Bond refund paid',money(bondRefund)] as [string,string]]:[]),
            ['Asset condition',form.damage||form.condition],
          ]}/>
          <TextField required id="return-signature-name" label="Customer acknowledgement name" value={form.signature} onChange={e=>update({signature:e.target.value})}/>
          <Box id="return-signature" tabIndex={-1}><SignaturePad value={form.signatureDataUrl} onChange={signatureDataUrl=>setForm(current=>({...current,signatureDataUrl,acknowledged:false}))}/></Box>
          <FormControlLabel control={<Checkbox checked={form.acknowledged} onChange={e=>setForm(current=>({...current,acknowledged:e.target.checked}))}/>} id="return-acknowledged" label="Customer acknowledges the return record, charges and bond settlement"/>
        </Stack>}
    </Box>
<Dialog open={confirmClose} onClose={()=>setConfirmClose(false)} fullWidth maxWidth="sm"><DialogTitle>Close return?</DialogTitle><DialogContent><Typography color="text.secondary">Your progress is saved on this device. Close to resume later, or discard the draft.</Typography></DialogContent><DialogActions sx={{p:3}}><Button onClick={()=>setConfirmClose(false)}>Keep editing</Button><Button onClick={()=>{if(row)void saveRentalDraft(`return:${row.id}`,{...form,savedStep:step,quotedAt:chargePreview?.returnedAt??draftQuotedTime}).then(onClose).catch(()=>{setError('Draft could not be saved.');setConfirmClose(false)});else onClose()}}>Save & close</Button><Button color="error" variant="contained" onClick={()=>{if(row){setDraftReady(false);void clearRentalDraft(`return:${row.id}`)}onClose()}}>Discard draft</Button></DialogActions></Dialog>
    </DialogContent>
    {error&&<FormError placement="footer">{error}</FormError>}<WorkflowValidation messages={returnStageErrors[step]} attempted={returnAttempted} targets={{
      ...Object.fromEntries(relevantCharges.map(([key,label])=>[paymentAmountError(form[key],label),'return-charge-'+key])),
      ...(returnPaymentError?{[returnPaymentError]:'return-payment-amount'}:{}),
      ...(retainedBondError?{[retainedBondError]:'return-bond-amount'}:{}),
      ...(returnReceiptError?{[returnReceiptError]:'return-receipt-reference'}:{}),
      ...(refundReferenceError?{[refundReferenceError]:'return-refund-reference'}:{}),
      ...(form.lateOverride?{[paymentAmountError(form.late,'Late fee')]:'return-late-amount'}:{}),
      'Select the refund method.':'return-refund-method',
      'Select the receipt payment method.':'return-receipt-method',
      'Use a separate reference for the refund.':'return-refund-reference',
      ['Enter a return usage reading from '+priorMeter+' to 10,000,000.']:'return-meter',
    }}/>
    <DialogActions sx={{p:2,borderTop:1,borderColor:'divider',flexWrap:'wrap',gap:1}}>
      <Button disabled={saving||savingDraft||readingPhotos} onClick={()=>dirty?setConfirmClose(true):onClose()}>Close</Button>
      <Button startIcon={<SaveOutlined/>} variant="outlined" disabled={saving||savingDraft||readingPhotos||!draftReady||draftFor!==row?.id} onClick={()=>void saveProgress()}>Save progress</Button>
      <Box sx={{flex:1}}/>
      <Button disabled={flowIndex===0||saving||readingPhotos} onClick={()=>goToStep(returnStages[flowIndex-1])}>Back</Button>
      <Button variant="contained" disabled={!draftReady||saving||readingPhotos||inspectionContext.loading||Boolean(inspectionContext.error)||step!==2&&!chargePreview}
        onClick={()=>{
          setError('')
          setReturnAttempted(true)
          if(step===6){
            const previous=returnStages.find(stage=>stage!==6&&returnStageErrors[stage].length>0)
            if(previous!==undefined){goToStep(previous);setReturnAttempted(true);return}
          }
          if(returnStageErrors[step].length>0)return
          if(step===6)void complete();else goToStep(returnStages[flowIndex+1])
        }}>
        {saving?'Completing return…':step===6?'Complete return & create invoice':'Continue'}
      </Button>
    </DialogActions>
  </Dialog>
}
function ReceiptFields({id,method,reference,maxLength,error,showErrorText=true,onMethod,onReference,methodLabel='Payment method',
  referenceLabel='Receipt / transaction reference',cashReferenceLabel='Existing till receipt'}:{
  id:string;method:string;reference:string;maxLength:number;error:string;showErrorText?:boolean;onMethod:(value:string)=>void;onReference:(value:string)=>void
  methodLabel?:string;referenceLabel?:string;cashReferenceLabel?:string
}){
  const cash=method==='Cash'
  return <Stack direction={{xs:'column',sm:'row'}} gap={1.5}>
    <TextField select fullWidth required id={id+'-method'} label={methodLabel} value={method} onChange={event=>onMethod(event.target.value)}>
      {paymentMethods.map(value=><MenuItem key={value} value={value}>{value.replace(/([a-z])([A-Z])/g,'$1 $2')}</MenuItem>)}
    </TextField>
    <TextField fullWidth required={!cash} id={id+'-reference'} label={cash?cashReferenceLabel+' (optional)':referenceLabel} value={reference} error={Boolean(error)}
      helperText={error?(showErrorText?error:undefined):cash?'CREMS generates a receipt number if left blank.':undefined}
      inputProps={{maxLength}} onChange={event=>onReference(event.target.value)}/>
  </Stack>
}

const workflowErrorTargets: Record<string, string> = {
  'Select the applicable licence classes from 1 to 8.': 'pickup-licence-class',
  'Enter a usage reading between the recorded reading and 10,000,000.': 'pickup-meter',
  'Enter a whole fuel percentage from 0 to 100.': 'pickup-fuel',
  'Enter the returned fuel or battery level from 0 to 100.': 'return-fuel',
  'Late fee must be a valid amount of zero or more.': 'return-late-amount',
  "Confirm the asset number on the physical label.": "pickup-asset",
  "Confirm that the customer identification was sighted and matches this booking.": "pickup-identification",
  "Confirm that the driver licence was sighted and is valid for this vehicle.": "pickup-licence-verified",
  "Enter a numeric driver licence number.": "pickup-licence-number",
  "Enter a future driver licence expiry date.": "pickup-licence-expiry",
  "Describe the existing damage below the acceptance section.": "pickup-damage",
  "Record the customer’s acceptance of the existing damage.": "pickup-accept-damage",
  "Confirm that the asset is safe and suitable, or choose a replacement.": "pickup-safety",
  "Add an acceptance note of at least 10 characters describing the customer arrangement.": "pickup-damage-note",
  "Confirm full payment or select an approved pay-at-return arrangement.": "pickup-payment-choice",
  "A branch manager or administrator must approve payment at return.": "pickup-payment-choice",
  "Record the approved payment arrangement.": "pickup-payment-arrangement",
  "Confirm receipt of the remaining refundable bond.": "pickup-bond",
  "Confirm staff approval to release the asset.": "pickup-approval",
  "Enter the customer signature name.": "pickup-signature-name",
  "Confirm that the customer accepts the agreement, charges and recorded condition.": "pickup-terms",
  "Confirm the physical asset label matches this hire.": "return-asset",
  "Describe the faults found.": "return-damage",
  "Record why the late fee was adjusted or waived.": "return-late-reason",
  "Confirm the calculated late fee or adjust it.": "return-late-confirmed",
  "Enter the reason for the other return charge.": "return-other-reason",
  "Record the reason for applying the bond.": "return-bond-reason",
  "Confirm the bond refund has been paid.": "return-refund-confirmed",
  "Enter the partial payment received or clear that option.": "return-payment-amount",
  "Confirm the amount to leave outstanding.": "return-outstanding",
  "Choose how to settle the remaining balance.": "return-payment-choice",
  "Enter the name of the person acknowledging the return.": "return-signature-name",
  "Confirm customer acknowledgement.": "return-acknowledged",
  "Draw the customer signature in the signature box below.": "pickup-signature",
  "Capture their signature.": "return-signature",
  'Describe the pre-hire condition.': 'pickup-condition',
  'Mark every checklist item as passed or issue found.': 'pickup-checklist',
  'Attach at least one inspection photo.': 'pickup-photos',
  'Record the return condition.': 'return-condition',
  'Mark each required inspection item.': 'return-checklist',
  'Attach at least one photo of the returned asset.': 'return-photos',
}

function WorkflowValidation({messages,attempted,targets}:{messages:string[];attempted:boolean;targets:Record<string,string>}){
  const fieldTargets={...workflowErrorTargets,...targets}
  const issues = messages.flatMap(message => {
    const target = fieldTargets[message]
    return target ? [{ target, message }] : []
  })
  const formMessages = [...new Set(messages.filter(message => !fieldTargets[message]))]
  return <>
    {attempted&&formMessages.length>0&&<FormError placement="footer">{formMessages.join(' ')}</FormError>}
    <Box sx={{px:2,alignSelf:'flex-end'}}><InlineIssueNavigation issues={issues} attempted={attempted}/></Box>
  </>
}
function ReviewCard({title,rows}:{title:string;rows:[string,string|undefined|null][]}){return <Card variant="outlined"><CardContent><Typography variant="h6" fontWeight={800} mb={2}>{title}</Typography><Grid container spacing={2}>{rows.map(([label,value])=><Grid key={label} size={{xs:12,sm:6}}><Typography variant="caption" color="text.secondary">{label}</Typography><Typography fontWeight={650}>{value||'Not recorded'}</Typography></Grid>)}</Grid></CardContent></Card>}
