import { useCallback, useEffect, useMemo, useState } from 'react'
import AccountBalanceWalletOutlined from '@mui/icons-material/AccountBalanceWalletOutlined'
import PaymentsOutlined from '@mui/icons-material/PaymentsOutlined'
import ReceiptLongOutlined from '@mui/icons-material/ReceiptLongOutlined'
import SearchOutlined from '@mui/icons-material/SearchOutlined'
import {
  Alert, Avatar, Box, Card, CardContent, Chip, CircularProgress, Grid,
  InputAdornment, Stack, Tab, Table, TableBody, TableCell, TableContainer,
  TableHead, TableRow, Tabs, TextField, Typography, Pagination,
} from '@mui/material'
import { api } from '../api/client'
import { PageHeader } from '../components/PageHeader'

type Invoice = { id:string;invoiceNumber:string;bookingNumber:string;customerName:string;branchName:string;total:number;amountPaid:number;balanceDue:number;status:string;issuedAt:string }
type Payment = { id:string;bookingNumber:string;customerName:string;branchName:string;type:string;method:string;amount:number;receiptNumber:string;status:string;createdAt:string }
type Workspace = { summary:{invoiced:number;received:number;outstanding:number;unpaidInvoices:number;depositsHeld:number};invoices:Invoice[];payments:Payment[] }
const money=(value:number)=>`FJD ${value.toLocaleString('en-FJ',{minimumFractionDigits:2,maximumFractionDigits:2})}`
const date=(value:string)=>new Date(value).toLocaleDateString('en-FJ')

export function FinancePage(){
  const [data,setData]=useState<Workspace|null>(null),[loading,setLoading]=useState(true),[error,setError]=useState(''),[tab,setTab]=useState(0),[search,setSearch]=useState(''),[page,setPage]=useState(1)
  const load=useCallback(async()=>{setLoading(true);try{setData((await api.get<Workspace>('/finance/workspace')).data);setError('')}catch{setError('Finance records could not be loaded for your assigned scope.')}finally{setLoading(false)}},[])
  useEffect(()=>{
    // Initial synchronization with the secured finance workspace.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load()
  },[load])
  const term=search.trim().toLowerCase()
  const invoices=useMemo(()=>data?.invoices.filter(x=>[x.invoiceNumber,x.bookingNumber,x.customerName,x.branchName,x.status].some(value=>value.toLowerCase().includes(term)))??[],[data,term])
  const payments=useMemo(()=>data?.payments.filter(x=>[x.receiptNumber,x.bookingNumber,x.customerName,x.branchName,x.method,x.type].some(value=>value.toLowerCase().includes(term)))??[],[data,term])
  const records=tab===0?invoices:payments; const pageCount=Math.max(1,Math.ceil(records.length/20)); const invoiceRows=invoices.slice((page-1)*20,page*20); const paymentRows=payments.slice((page-1)*20,page*20)
  return <Box sx={{p:{xs:2,sm:3,lg:4},maxWidth:1550,mx:'auto'}}>
    <PageHeader icon={<AccountBalanceWalletOutlined/>} title="Finance" subtitle="Invoices, payments and customer balances for your permitted branches." actions={<Chip icon={<AccountBalanceWalletOutlined/>} label={`${data?.summary.unpaidInvoices??0} invoices require payment`} color={(data?.summary.unpaidInvoices??0)>0?'warning':'success'} variant="outlined"/>} />
    {error&&<Alert severity="error" sx={{mb:2}}>{error}</Alert>}
    {loading?<Box sx={{minHeight:420,display:'grid',placeItems:'center'}}><CircularProgress/></Box>:data&&<>
      <Grid container spacing={2.5} mb={3}>{[
        ['Total invoiced',money(data.summary.invoiced),<ReceiptLongOutlined/>],['Payments received',money(data.summary.received),<PaymentsOutlined/>],['Outstanding',money(data.summary.outstanding),<AccountBalanceWalletOutlined/>],['Refundable bonds held',money(data.summary.depositsHeld),<PaymentsOutlined/>],
      ].map(([label,value,icon])=><Grid key={String(label)} size={{xs:12,sm:6,lg:3}}><Card variant="outlined" sx={{height:'100%',borderTop:3,borderTopColor:label==='Outstanding'&&data.summary.outstanding>0?'warning.main':'secondary.main'}}><CardContent><Stack direction="row" justifyContent="space-between"><Box><Typography variant="body2" color="text.secondary">{label}</Typography><Typography variant="h5" fontWeight={850} mt={1}>{value}</Typography></Box><Avatar sx={{bgcolor:'secondary.main',color:'#111'}}>{icon}</Avatar></Stack></CardContent></Card></Grid>)}</Grid>
      <Card variant="outlined" sx={{overflow:'hidden'}}><Tabs value={tab} onChange={(_,value)=>{setTab(value);setPage(1)}} sx={{px:1,borderBottom:1,borderColor:'divider'}}><Tab label={`Invoices (${data.invoices.length})`}/><Tab label={`Payments (${data.payments.length})`}/></Tabs><Box sx={{p:2,borderBottom:1,borderColor:'divider'}}><TextField size="small" value={search} onChange={event=>{setSearch(event.target.value);setPage(1)}} placeholder={tab===0?'Search invoice, booking or customer':'Search receipt, booking or customer'} sx={{width:{xs:'100%',sm:420}}} InputProps={{startAdornment:<InputAdornment position="start"><SearchOutlined/></InputAdornment>}}/></Box>
        {tab===0?<TableContainer><Table><TableHead><TableRow sx={{bgcolor:'grey.50'}}><TableCell>Invoice</TableCell><TableCell>Customer</TableCell><TableCell>Booking / branch</TableCell><TableCell align="right">Total</TableCell><TableCell align="right">Paid</TableCell><TableCell align="right">Balance</TableCell><TableCell>Status</TableCell></TableRow></TableHead><TableBody>{invoiceRows.map(row=><TableRow key={row.id} hover><TableCell><Typography fontWeight={800}>{row.invoiceNumber}</Typography><Typography variant="caption" color="text.secondary">Issued {date(row.issuedAt)}</Typography></TableCell><TableCell>{row.customerName}</TableCell><TableCell><Typography variant="body2" fontWeight={700}>{row.bookingNumber}</Typography><Typography variant="caption" color="text.secondary">{row.branchName}</Typography></TableCell><TableCell align="right">{money(row.total)}</TableCell><TableCell align="right">{money(row.amountPaid)}</TableCell><TableCell align="right" sx={{fontWeight:800,color:row.balanceDue>0?'warning.dark':'success.main'}}>{money(row.balanceDue)}</TableCell><TableCell><Chip size="small" label={row.status} color={row.status==='Paid'?'success':row.status==='PartiallyPaid'?'warning':'default'} variant="outlined"/></TableCell></TableRow>)}{!invoices.length&&<Empty columns={7}/>}</TableBody></Table></TableContainer>:<TableContainer><Table><TableHead><TableRow sx={{bgcolor:'grey.50'}}><TableCell>Receipt</TableCell><TableCell>Customer</TableCell><TableCell>Booking / branch</TableCell><TableCell>Type</TableCell><TableCell>Method</TableCell><TableCell align="right">Amount</TableCell><TableCell>Status</TableCell></TableRow></TableHead><TableBody>{paymentRows.map(row=><TableRow key={row.id} hover><TableCell><Typography fontWeight={800}>{row.receiptNumber}</Typography><Typography variant="caption" color="text.secondary">{date(row.createdAt)}</Typography></TableCell><TableCell>{row.customerName}</TableCell><TableCell><Typography variant="body2" fontWeight={700}>{row.bookingNumber}</Typography><Typography variant="caption" color="text.secondary">{row.branchName}</Typography></TableCell><TableCell>{row.type}</TableCell><TableCell>{row.method.replace(/([a-z])([A-Z])/g,'$1 $2')}</TableCell><TableCell align="right" sx={{fontWeight:800}}>{money(row.amount)}</TableCell><TableCell><Chip size="small" label={row.status} color={row.status==='Recorded'?'success':'default'} variant="outlined"/></TableCell></TableRow>)}{!payments.length&&<Empty columns={7}/>}</TableBody></Table></TableContainer>}
        {records.length>20&&<Stack direction="row" justifyContent="space-between" alignItems="center" sx={{p:2,borderTop:1,borderColor:'divider'}}><Typography variant="body2" color="text.secondary">Showing {(page-1)*20+1}–{Math.min(page*20,records.length)} of {records.length}</Typography><Pagination count={pageCount} page={page} onChange={(_,value)=>setPage(value)} size="small"/></Stack>}
      </Card>
    </>}
  </Box>
}

function Empty({columns}:{columns:number}){return <TableRow><TableCell colSpan={columns} align="center" sx={{py:8,color:'text.secondary'}}>No matching financial records.</TableCell></TableRow>}
